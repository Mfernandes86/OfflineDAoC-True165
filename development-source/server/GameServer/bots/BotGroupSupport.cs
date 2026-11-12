using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    // Group-local, short-lived claims. No database work or additional timers.
    public static class BotGroupSupport
    {
        public static bool CanFinishResurrectionBeforeRelease(long castStarted, long deadline, long now, int castTime) =>
            castStarted > 0 && castStarted <= deadline && now >= castStarted &&
            now < castStarted + Math.Clamp((long)castTime + 2000, 2000, 60000);

        // owner 2026-11-10: 42 of 101 resurrection timeouts had a resurrector alive and near but short of power, and the dead
        // released after 60 s. Out of combat, a resurrector short only of power sits for it, and the corpse it sits for
        // waits up to 120 s instead. Spell costs are unchanged.
        public const long ResurrectionReleaseMilliseconds = 60_000;
        public const long ResurrectionRestReleaseMilliseconds = 120_000;
        public const long RestingForResurrectionMilliseconds = 15_000;   // outlasts a slow bot think

        /// <summary>The power a resurrection of this corpse takes (the reservation's native cost), at least the spell's own.</summary>
        public static int ResurrectionPowerCost(int maxMana, int casterLevel, int corpseLevel, int spellPowerCost) =>
            Math.Max(spellPowerCost,
                (int)(maxMana * Math.Max(.1f, .5f + (corpseLevel - casterLevel) / (float)Math.Max(1, casterLevel))));

        /// <summary>A resurrector sits for power only when power is the one thing stopping the raise and resting can reach it.</summary>
        public static bool ShouldRestForResurrection(bool corpseWaitingInRange, bool partyInCombat, bool casting,
            bool interruptedOrControlled, bool onRecast, int mana, int maxMana, int requiredPower) =>
            corpseWaitingInRange && !partyInCombat && !casting && !interruptedOrControlled && !onRecast &&
            mana < requiredPower && requiredPower <= maxMana;

        /// <summary>How long a corpse waits after combat clears for a raise.</summary>
        public static long ResurrectionReleaseWait(bool resurrectorRestingForIt) =>
            resurrectorRestingForIt ? ResurrectionRestReleaseMilliseconds : ResurrectionReleaseMilliseconds;

        public static bool IsRestingClaimCurrent(long noteTick, long nowTick) =>
            noteTick > 0 && nowTick >= noteTick && nowTick - noteTick < RestingForResurrectionMilliseconds;

        private sealed class RestForRaise
        {
            public GameLiving Corpse;
            public long NoteTick;
        }
        private static readonly ConditionalWeakTable<GameBot, RestForRaise> RestingForRaise = new();

        /// <summary>Notes that <paramref name="bot"/> sits for the power to raise <paramref name="corpse"/>; refreshed each think.</summary>
        public static void NoteRestingForResurrection(GameBot bot, GameLiving corpse, long nowTick)
        {
            RestForRaise note = RestingForRaise.GetOrCreateValue(bot);
            note.Corpse = corpse;
            note.NoteTick = nowTick;
        }

        public static void ClearRestingForResurrection(GameBot bot)
        {
            if (bot != null && RestingForRaise.TryGetValue(bot, out RestForRaise note))
                note.NoteTick = 0;
        }

        /// <summary>True while <paramref name="bot"/> sits for a raise (of <paramref name="corpse"/>, or of anyone when null).</summary>
        public static bool IsRestingForResurrection(GameBot bot, GameLiving corpse, long nowTick) =>
            bot != null && RestingForRaise.TryGetValue(bot, out RestForRaise note) &&
            IsRestingClaimCurrent(note.NoteTick, nowTick) && (corpse == null || note.Corpse == corpse);

        public static bool HasCorpseLineOfSight(GameBot caster, GameLiving corpse) =>
            caster.CurrentZone?.IsDungeon != true ||
            !PathfindingProvider.Instance.IsAvailable || !PathfindingProvider.Instance.HasNavmesh(caster.CurrentZone) ||
            PathfindingProvider.Instance.HasLineOfSight(caster.CurrentZone,
                new(caster.X, caster.Y, caster.Z), new(corpse.X, corpse.Y, corpse.Z),
                PathfindingProvider.Instance.DefaultFilters);
        private sealed class State
        {
            public readonly object Sync = new();
            public readonly CompanionRaidResurrectionReservations<GameLiving> Resurrection = new();
            public readonly Dictionary<GameBot, HealClaim> Heals = new();
        }
        private sealed record HealClaim(GameLiving Target, Spell Spell, long Started, long Expires);
        private static readonly ConditionalWeakTable<object, State> States = new();

        /// <summary>
        /// Pure: which corpse is raised first (veteran review 2026-11-11, Q21; period groups raise the raisers first).
        /// A human player first as before, then anyone who can raise others, then power users, then tanks, then the
        /// rest; distance breaks ties.
        /// </summary>
        public static int ResurrectionOrder(bool humanPlayer, bool canResurrect, bool usesPower, bool tank) =>
            humanPlayer ? 0 : canResurrect ? 1 : usesPower ? 2 : tank ? 3 : 4;

        private static int ResurrectionOrder(GameLiving corpse)
        {
            ICharacterClass characterClass = corpse switch
            {
                GameBot b => b.CharacterClass,
                GamePlayer p => p.CharacterClass,
                _ => null
            };
            bool usesPower = characterClass?.ClassType is eClassType.ListCaster or eClassType.Hybrid && corpse.MaxMana > 0;
            return ResurrectionOrder(corpse is GamePlayer, corpse is GameBot raiser && raiser.ResurrectionSpell != null,
                usesPower, BotHealTriage165.IsTank(corpse));
        }

        public static int CombatResurrectionBudget(int healers, int criticalMembers) =>
            Math.Max(0, Math.Min(healers / 2, healers - Math.Max(1, criticalMembers)));

        public static double ProjectedHealthPercent(int health, int maxHealth, double incoming) =>
            100.0 * (health + Math.Max(0, incoming)) / Math.Max(1, maxHealth);

        private static bool UnderAttack(GameBot bot) => bot.IsBeingInterruptedByOther ||
            GameLoop.GameLoopTime - bot.LastAttackedByEnemyTick < 3000 ||
            bot.attackComponent.AttackerTracker.Attackers.Any(a => a.IsAlive && a.TargetObject == bot);

        public static bool ContinueResurrection(GameBot bot)
        {
            if (bot.Group != null && bot.IsAlive && !bot.IsCrowdControlled && !bot.IsSilenced && !UnderAttack(bot))
                return true;
            bot.StopCurrentSpellcast();
            CancelResurrection(bot);
            return false;
        }

        /// <summary>
        /// The bot's support scope, read once (owner 2026-11-04: a group that broke up between a bot.Group check and
        /// the scope lookup handed null to the state table; the ArgumentNullException made the NPC service remove the
        /// healer bots from the world).
        /// </summary>
        private static object Scope(GameBot bot) => AutonomousRealmRaid.SupportScope(bot);

        public static void CancelResurrection(GameBot bot)
        {
            object scope = Scope(bot);
            if (scope != null && States.TryGetValue(scope, out State state))
                state.Resurrection.ReleaseCaster(bot);
        }

        public static GameLiving ReserveResurrection(GameBot bot, Spell spell)
        {
            if (bot.Group == null || !bot.IsAlive || bot.IsCasting || bot.IsCrowdControlled || bot.IsSilenced || UnderAttack(bot))
                return null;
            object scope = Scope(bot);
            if (scope == null)
                return null;
            int range = bot.castingComponent.CalculateSpellRange(spell);
            // A wounded party elsewhere in the dungeon cannot consume this
            // room's healer budget. The shared reservation still prevents two
            // parties assigning a resurrector to the same corpse.
            var members = AutonomousRealmRaid.SupportMembers(bot)
                .Where(m => m.ObjectState == GameObject.eObjectState.Active &&
                    m.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(m, range)).ToArray();
            bool combat = members.Any(m => m.IsAlive && m.InCombat);
            int healers = members.OfType<GameBot>().Count(b => b.IsAlive && b.CanCastHealSpells &&
                b.CurrentRegionID == bot.CurrentRegionID && !b.IsCrowdControlled && !b.IsSilenced && !UnderAttack(b) && b.ManaPercent >= 20);
            // Keep at least half the available healers healing during combat.
            int critical = members.Count(m => m.IsAlive && m.CurrentRegionID == bot.CurrentRegionID && m.HealthPercent < 35);
            int budget = combat ? CombatResurrectionBudget(healers, critical) : healers;
            State state = States.GetOrCreateValue(scope);
            foreach (GameLiving corpse in members.Where(m => !m.IsAlive && m.ObjectState == GameObject.eObjectState.Active &&
                m.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(m, range)).OrderBy(ResurrectionOrder).ThenBy(bot.GetDistanceTo))
            {
                if (!HasCorpseLineOfSight(bot, corpse)) continue;
                if (corpse.TempProperties.GetProperty<GameLiving>("RESURRECT_CASTER") != null) continue;
                int cost = ResurrectionPowerCost(bot.MaxMana, bot.Level, corpse.Level, 0);
                if (bot.Mana < cost) continue;
                if (state.Resurrection.TryReserve(bot, corpse, GameLoop.GameLoopTime, spell.CastTime,
                    true, combat, combat ? budget + 1 : healers, false, bot.CanCastHealSpells)) return corpse;
            }
            return null;
        }

        /// <summary>
        /// Diagnostic only: the first check that stops this bot reserving a resurrection of
        /// <paramref name="corpse"/>. Mirrors ReserveResurrection without reserving anything.
        /// </summary>
        /// <summary>A resurrector with power and a waiting corpse in range pauses chant and song upkeep.</summary>
        public static bool ResurrectionOutranksUpkeep(bool hasResurrection, bool grouped, bool enoughPower, bool corpseWaitingInRange) =>
            hasResurrection && grouped && enoughPower && corpseWaitingInRange;

        public static string DescribeResurrectionBlock(GameBot bot, Spell spell, GameLiving corpse)
        {
            if (bot.IsCasting) return "casting another spell";
            if (bot.IsCrowdControlled || bot.IsSilenced) return "crowd controlled or silenced";
            if (UnderAttack(bot)) return "under attack";
            if (!HasCorpseLineOfSight(bot, corpse)) return "no line of sight to the corpse";
            if (corpse.TempProperties.GetProperty<GameLiving>("RESURRECT_CASTER") != null) return "another resurrection is already pending on the corpse";
            int cost = ResurrectionPowerCost(bot.MaxMana, bot.Level, corpse.Level, 0);
            if (bot.Mana < cost) return $"not enough power for the reservation ({bot.Mana}/{cost} of {bot.MaxMana})";
            return "the shared reservation refused (another resurrector holds it, or healers are needed for combat)";
        }

        public static void CancelHeal(GameBot bot)
        {
            object scope = Scope(bot);
            if (scope != null && States.TryGetValue(scope, out State state))
                lock (state.Sync) state.Heals.Remove(bot);
        }

        /// <summary>What a healer in an ordinary party does with its own chosen heal target when another healer of the
        /// party already has a heal on the way to it (veteran review 2026-11-11, Q5).</summary>
        public enum OrdinaryHealSplit { Preferred, Other, Skip }

        /// <summary>Health percent the target reaches with the heals already on the way, above which a second heal on it
        /// would mostly be wasted and the healer looks for someone else.</summary>
        public const double SecondHealWastedPercent = 90;

        /// <summary>Below this health percent the preferred target is an emergency and always keeps a second heal (the
        /// healers' emergency line).</summary>
        public const int EmergencyHealPercent = 40;

        /// <summary>Enemies hitting the preferred target in melee from which it always keeps a second heal (burst).</summary>
        public const int BurstMeleeAttackers = 2;

        /// <summary>
        /// Pure: an ordinary bot party's heal split. The healer keeps its own triage (tank first, emergencies) - it only
        /// moves off its target when another healer's heal already covers it: then the most hurt other member under 80%
        /// in range, or no heal this think. A target still short after the heals on the way keeps the second heal, and so
        /// does one in an emergency (under 40%, or hit in melee by two or more enemies): the projection ignores the damage
        /// it takes during the cast, so two healers on a tank under burst stay on it (combat review 2026-11-11).
        /// </summary>
        public static OrdinaryHealSplit ChooseOrdinaryHealSplit(bool preferredHasIncoming, double preferredProjected,
            double bestOtherProjected, int preferredHealthPercent = 100, int preferredMeleeAttackers = 0) =>
            !preferredHasIncoming || preferredProjected < SecondHealWastedPercent ||
            preferredHealthPercent < EmergencyHealPercent || preferredMeleeAttackers >= BurstMeleeAttackers
                ? OrdinaryHealSplit.Preferred :
            bestOtherProjected < 80 ? OrdinaryHealSplit.Other : OrdinaryHealSplit.Skip;

        /// <summary>Enemies hitting <paramref name="target"/> in melee, counted up to <see cref="BurstMeleeAttackers"/>.</summary>
        private static int MeleeAttackersOn(GameLiving target)
        {
            if (target?.attackComponent == null)
                return 0;
            int count = 0;
            foreach (GameLiving attacker in target.attackComponent.AttackerTracker.Attackers)
                if (DOL.AI.Brain.BotBrain.IsMeleeAttackerOn(attacker, target) && ++count >= BurstMeleeAttackers)
                    break;
            return count;
        }

        public static GameLiving ReserveHeal(GameBot bot, Spell spell, GameLiving preferred)
        {
            if (!spell.IsHealing || spell.Target != eSpellTarget.REALM || spell.IsInstantCast || spell.Duration > 0)
                return preferred;
            bool sharedSupport = CompanionRaid.IsMember(bot) || AutonomousRealmRaid.HasSharedSupport(bot);
            // Q5: every bot party shares heal claims, not only raids (two healers of an 8-man landed on the same target)
            if (!sharedSupport && !(bot.Group?.MemberCount > 1))
                return preferred;
            // an ordinary party's scope is its group (what SupportScope returns without a raid or warband): no raid lock
            // per heal cast (performance review 2026-11-11)
            if (!sharedSupport)
                return bot.Group is { } party ? ReserveOrdinaryHeal(bot, spell, preferred, States.GetOrCreateValue(party), party) : preferred;
            object scope = Scope(bot);
            if (scope == null)
                return preferred;
            State state = States.GetOrCreateValue(scope);
            int range = bot.castingComponent.CalculateSpellRange(spell);
            var candidates = AutonomousRealmRaid.SupportMembers(bot).Concat(AutonomousRealmRaid.SupportPets(bot, range)).Concat(
                TemporaryCompanionPetHealing.TriageTargets(bot, range).Cast<GameLiving>()).Distinct().ToArray();
            lock (state.Sync)
            {
                long now = GameLoop.GameLoopTime;
                foreach (var entry in state.Heals.ToArray())
                    if (entry.Value.Expires <= now || !entry.Key.IsAlive || !ReferenceEquals(AutonomousRealmRaid.SupportScope(entry.Key), scope) ||
                        now - entry.Value.Started > 500 && entry.Key.castingComponent.SpellHandler?.Spell != entry.Value.Spell)
                        state.Heals.Remove(entry.Key);
                state.Heals.Remove(bot);
                GameLiving best = null;
                double bestScore = double.MaxValue;
                foreach (GameLiving target in candidates)
                {
                    if (!target.IsAlive || target.CurrentRegionID != bot.CurrentRegionID || !bot.IsWithinRadius(target, range) || target.HealthPercent >= 80) continue;
                    double incoming = state.Heals.Where(p => p.Value.Target == target).Sum(p => Math.Max(0, GameBot.HealAmount(p.Value.Spell, target)));
                    double score = ProjectedHealthPercent(target.Health, target.MaxHealth, incoming);
                    if (score >= 100) continue;
                    if (score < bestScore || score == bestScore && target == preferred) { best = target; bestScore = score; }
                }
                if (best != null)
                    state.Heals[bot] = new(best, spell, now, now + Math.Clamp(spell.CastTime + 500, 1000, 15000));
                return best;
            }
        }

        private static double IncomingHeal(State state, GameLiving target)
        {
            double incoming = 0;
            foreach (var claim in state.Heals)
                if (claim.Value.Target == target)
                    incoming += Math.Max(0, GameBot.HealAmount(claim.Value.Spell, target));
            return incoming;
        }

        /// <summary>The ordinary party's split (ChooseOrdinaryHealSplit): the bot's own triage target unless another
        /// healer's heal already covers it. The preferred target may be out of range (the healer walks to it).</summary>
        private static GameLiving ReserveOrdinaryHeal(GameBot bot, Spell spell, GameLiving preferred, State state, Group group)
        {
            lock (state.Sync)
            {
                long now = GameLoop.GameLoopTime;
                foreach (var entry in state.Heals.ToArray())
                    if (entry.Value.Expires <= now || !entry.Key.IsAlive || entry.Key.Group != group ||
                        now - entry.Value.Started > 500 && entry.Key.castingComponent.SpellHandler?.Spell != entry.Value.Spell)
                        state.Heals.Remove(entry.Key);
                state.Heals.Remove(bot);
                long expires = now + Math.Clamp(spell.CastTime + 500, 1000, 15000);
                bool preferredHasIncoming = false;
                foreach (var claim in state.Heals)
                    if (claim.Value.Target == preferred) { preferredHasIncoming = true; break; }
                double preferredProjected = preferredHasIncoming
                    ? ProjectedHealthPercent(preferred.Health, preferred.MaxHealth, IncomingHeal(state, preferred))
                    : preferred.HealthPercent;
                GameLiving other = null;
                double otherProjected = double.MaxValue;
                int preferredMelee = preferredHasIncoming && preferredProjected >= SecondHealWastedPercent &&
                    preferred.HealthPercent >= EmergencyHealPercent ? MeleeAttackersOn(preferred) : 0;
                if (ChooseOrdinaryHealSplit(preferredHasIncoming, preferredProjected, 0, preferred.HealthPercent, preferredMelee) !=
                    OrdinaryHealSplit.Preferred)
                {
                    int range = bot.castingComponent.CalculateSpellRange(spell);
                    foreach (GameLiving member in group.GetMembersInTheGroup())
                    {
                        if (member == null || member == preferred || !member.IsAlive ||
                            member.CurrentRegionID != bot.CurrentRegionID || member.HealthPercent >= 80 ||
                            !bot.IsWithinRadius(member, range)) continue;
                        double projected = ProjectedHealthPercent(member.Health, member.MaxHealth, IncomingHeal(state, member));
                        if (projected < otherProjected) { other = member; otherProjected = projected; }
                    }
                }
                switch (ChooseOrdinaryHealSplit(preferredHasIncoming, preferredProjected, otherProjected, preferred.HealthPercent,
                            preferredMelee))
                {
                    case OrdinaryHealSplit.Preferred:
                        state.Heals[bot] = new(preferred, spell, now, expires);
                        return preferred;
                    case OrdinaryHealSplit.Other:
                        state.Heals[bot] = new(other, spell, now, expires);
                        return other;
                    default:
                        return null;
                }
            }
        }
    }
}
