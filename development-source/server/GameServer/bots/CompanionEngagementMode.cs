using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>The companions' stance (op 30 byte [4] of the DebugMode 'H' packet sends this number).</summary>
    public enum CompanionStance : byte { Aggressive = 0, Defensive = 1, Passive = 2 }

    public static class CompanionEngagementMode
    {
        public const int DefensiveRadius = 350;
        private sealed class Mode
        {
            public CompanionStance Stance;
            public GameLiving Pull;
            // /camp (M7): the hold point companions lay out around and the pull comes back to
            public bool HasCamp;
            public Vector3 Camp;
            public ushort CampRegion, CampHeading;
            public long CampAwaySince;
        }
        private static readonly ConditionalWeakTable<GamePlayer, Mode> Modes = new();
        private static readonly Logger Log = LoggerManager.Create(typeof(CompanionEngagementMode));

        /// <summary>/defensive (true) or /aggressive (false); kept for the existing callers.</summary>
        public static void Set(GamePlayer player, bool defensive) =>
            Set(player, defensive ? CompanionStance.Defensive : CompanionStance.Aggressive);

        public static void Set(GamePlayer player, CompanionStance stance)
        {
            if (player == null) return;
            Mode mode = Modes.GetOrCreateValue(player);
            CompanionStance previous = mode.Stance;
            mode.Stance = stance;
            mode.Pull = null;
            // /passive is for walking away: the camp goes with it
            if (stance == CompanionStance.Passive)
                mode.HasCamp = false;
            CompanionPvpEngagement.Reset(player);
            // The new client's group-window stance button shows it (op 30 of the DebugMode 'H' packet, byte [4]:
            // 0 aggressive, 1 defensive, 2 passive; owner 2026-10-30, passive 2026-11-11). The original client keeps
            // /aggressive, /defensive and /passive.
            Hire165.Send(player, 30, [StanceByte(stance)]);
            if (previous == CompanionStance.Passive && stance != CompanionStance.Passive)
                RestorePets(player);
            if (previous != stance && Log.IsInfoEnabled)
                Log.Info($"COMPANION_STANCE player=\"{player.Name}\" stance={stance.ToString().ToLowerInvariant()} " +
                         $"was={previous.ToString().ToLowerInvariant()}");
        }

        /// <summary>
        /// Leaving /passive: the companions' pets defend again at once (outside a capital, where companion pets stay
        /// passive anyway), instead of waiting for their next attack order.
        /// </summary>
        private static void RestorePets(GamePlayer player)
        {
            if (player.Group == null) return;
            foreach (GameBot bot in player.Group.GetMembersInTheGroup().OfType<GameBot>())
            {
                if (!bot.IsPlayerCompanion || (bot.PlayerGroupLeader ?? bot.Owner) != player || PassiveLeader(bot) != null)
                    continue;
                RestorePets(bot);
            }
        }

        /// <summary>One companion's pets defend again (outside a capital, where companion pets stay passive anyway).</summary>
        public static void RestorePets(GameBot bot)
        {
            if (bot == null || bot.CurrentRegion?.IsCapitalCity == true) return;
            foreach (GameNPC pet in BotGroupPetBuffTargets.AttachedTree(bot.ControlledBrain, bot, new HashSet<IControlledBrain>()))
                if (pet.Brain is IControlledBrain brain && brain.AggressionState == eAggressionState.Passive)
                    brain.SetAggressionState(eAggressionState.Defensive);
        }

        /// <summary>
        /// /camp (M7, veteran review; period groups: "only the designated puller pulls", the group waits at the camp and
        /// the pull comes back to it): the player's spot and facing become the companions' hold point. They lay out their
        /// formation around it instead of around the player, /defensive measures its 350 from it, and a /pull brings the
        /// monster back to it. Dropped by /camp off, /passive, a region change, or the player staying more than 2,500
        /// units away for 10 s.
        /// </summary>
        public static void SetCamp(GamePlayer player)
        {
            if (player == null) return;
            Mode mode = Modes.GetOrCreateValue(player);
            mode.HasCamp = true;
            mode.Camp = new(player.X, player.Y, player.Z);
            mode.CampRegion = player.CurrentRegionID;
            mode.CampHeading = player.Heading;
            mode.CampAwaySince = 0;
            if (Log.IsInfoEnabled)
                Log.Info($"COMPANION_CAMP player=\"{player.Name}\" region={player.CurrentRegionID} at={player.X},{player.Y},{player.Z}");
        }

        public static void ClearCamp(GamePlayer player)
        {
            if (player != null && Modes.TryGetValue(player, out Mode mode))
                mode.HasCamp = false;
        }

        /// <summary>The player's camp, if one holds (checked lazily: region, and 2,500 units for 10 s).</summary>
        public static bool CampOf(GamePlayer player, out Vector3 camp, out ushort heading)
        {
            camp = default;
            heading = 0;
            if (player == null || !Modes.TryGetValue(player, out Mode mode) || !mode.HasCamp)
                return false;
            long now = GameLoop.GameLoopTime;
            int distance = (int)Vector3.Distance(mode.Camp, new(player.X, player.Y, player.Z));
            if (distance <= CompanionControls165.CampLeaveDistance)
                mode.CampAwaySince = 0;
            else if (mode.CampAwaySince == 0)
                mode.CampAwaySince = now;
            if (!CompanionControls165.CampHolds(player.CurrentRegionID == mode.CampRegion, distance,
                    mode.CampAwaySince == 0 ? 0 : now - mode.CampAwaySince))
            {
                mode.HasCamp = false;
                return false;
            }
            camp = mode.Camp;
            heading = mode.CampHeading;
            return true;
        }

        /// <summary>Where a /pull brings its monster back to: the camp, else the player.</summary>
        public static Vector3 ReturnPoint(GamePlayer player) =>
            CampOf(player, out Vector3 camp, out _) ? camp : new(player.X, player.Y, player.Z);

        /// <summary>/defensive's 350: from the camp when one holds, else from the player.</summary>
        private static bool WithinDefensiveRadius(GamePlayer leader, GameLiving target) =>
            CampOf(leader, out Vector3 camp, out _)
                ? Vector3.DistanceSquared(camp, new(target.X, target.Y, target.Z)) <= (float)DefensiveRadius * DefensiveRadius
                : leader.IsWithinRadius(target, DefensiveRadius);

        /// <summary>The op-30 payload byte for a stance: 0 aggressive, 1 defensive, 2 passive.</summary>
        public static byte StanceByte(CompanionStance stance) => (byte)stance;

        public static CompanionStance StanceOf(GamePlayer player) =>
            player != null && Modes.TryGetValue(player, out Mode mode) ? mode.Stance : CompanionStance.Aggressive;

        /// <summary>The player has /defensive on.</summary>
        public static bool IsDefensive(GamePlayer player) => StanceOf(player) == CompanionStance.Defensive;

        /// <summary>
        /// The player has /passive on (owner 2026-11-11: "for retreating or walking through areas without stopping to
        /// fight anything"): their companions and the companions' pets follow and fight nothing.
        /// </summary>
        public static bool IsPassive(GamePlayer player) => StanceOf(player) == CompanionStance.Passive;

        public static GamePlayer DefensiveLeader(GameLiving actor) => LeaderWith(actor, CompanionStance.Defensive);

        /// <summary>The player whose /passive holds this companion or companion pet; never a human's pet or a gamebot.</summary>
        public static GamePlayer PassiveLeader(GameLiving actor) => LeaderWith(actor, CompanionStance.Passive);

        private static GamePlayer LeaderWith(GameLiving actor, CompanionStance stance)
        {
            // Include companion pets, never human pets or autonomous bots.
            for (int depth = 0; depth < 8 && actor != null; depth++)
            {
                if (actor is GameBot bot)
                {
                    GamePlayer leader = bot.PlayerGroupLeader ?? bot.Owner;
                    if (!bot.IsPlayerCompanion || leader == null || bot.Group == null || bot.Group != leader.Group)
                        return null;
                    // M7: a companion's own stance (AI Options "Stance") wins over its player's
                    CompanionStance group = Modes.TryGetValue(leader, out Mode mode) ? mode.Stance : CompanionStance.Aggressive;
                    return CompanionControls165.EffectiveStance(CompanionOptions165.Choice(bot, "stance"), group) == stance
                        ? leader : null;
                }
                actor = (actor as GameNPC)?.Brain is IControlledBrain controlled ? controlled.Owner : null;
            }
            return null;
        }
        public static bool Allows(GameLiving actor, GameLiving target)
        {
            // /passive: nothing at all, not even an ordered /pull target (the order is refused and cancelled anyway).
            if (PassiveLeader(actor) != null) return false;
            GamePlayer leader = DefensiveLeader(actor);
            // The /pull puller may reach past the leash for the monster it was sent for (owner 2026-11-06; aggressive
            // mode has no leash and is unchanged).
            return leader == null || PlayerLedPullCoordinator.IsOrderedPuller(actor, target) ||
                target != null && target.CurrentRegionID == leader.CurrentRegionID &&
                (WithinDefensiveRadius(leader, target) || CompanionPvpEngagement.Defending(actor, target) ||
                 CompanionPvpEngagement.FrontierRoamerInRange(leader, target, true) ||
                 DefendsTheGroup(actor, leader, target));
        }

        /// <summary>How far from the player a /defensive companion still answers a monster that is attacking the group.</summary>
        public const int DefendGroupRadius = 2000;

        /// <summary>
        /// Pure (Q6, veteran review 2026-11-11, with the owner's clarification): /defensive starts no fight beyond 350
        /// units of the player, but it does defend. A monster already in melee on the companion itself, or on any
        /// groupmate or groupmate's pet within 2,000 units of the player, is answered - an add hitting the healer.
        /// Never the monster being pulled (/pull, /grind, any player-led pull), and never one on the puller: those wait
        /// for the 350 leash or the tank's engage exactly as before, so nobody runs out to meet the pull.
        /// </summary>
        public static bool DefensiveAnswers(bool withinDefensiveRadius, bool attacksThisCompanion, bool attacksGroupmate,
            bool withinGroupRadius, bool inMeleeOnVictim, bool pullInProgress) =>
            withinDefensiveRadius ||
            inMeleeOnVictim && !pullInProgress && (attacksThisCompanion || attacksGroupmate && withinGroupRadius);

        private static bool DefendsTheGroup(GameLiving actor, GamePlayer leader, GameLiving target)
        {
            if (!target.IsAlive || target.TargetObject is not GameLiving victim || !victim.IsAlive)
                return false;
            bool onMe = victim == actor;
            bool onGroup = !onMe && InGroupOf(victim, leader);
            if (!onMe && !onGroup)
                return false;
            bool inMelee = target.IsWithinRadius(victim, target.MeleeAttackRange + 50) &&
                           victim.attackComponent?.AttackerTracker.ContainsAttacker(target) == true;
            Group group = leader.Group;
            bool pulling = PlayerLedPullCoordinator.IsPullInProgress(group, target, victim) ||
                           PlayerLedPullCoordinator.IsBringingBack(victim, target) ||
                           Modes.TryGetValue(leader, out Mode mode) && mode.Pull == target;
            return DefensiveAnswers(false, onMe, onGroup, leader.IsWithinRadius(target, DefendGroupRadius), inMelee, pulling);
        }

        /// <summary>A member of the player's group, or a pet (or sub-pet) of one.</summary>
        private static bool InGroupOf(GameLiving living, GamePlayer leader)
        {
            Group group = leader.Group;
            for (int depth = 0; depth < 4 && living != null; depth++)
            {
                if (living == leader || group != null && group.IsInTheGroup(living))
                    return true;
                living = (living as GameNPC)?.Brain is IControlledBrain controlled ? controlled.Owner : null;
            }
            return false;
        }
        public static void RememberPull(GamePlayer player, GameLiving target)
        {
            // kept whatever the group's stance: a companion on its own Defensive stance waits for the pull too (M7)
            if (player != null) Modes.GetOrCreateValue(player).Pull = target;
        }
        public static GameLiving NearbyPull(GameBot bot)
        {
            GamePlayer player = DefensiveLeader(bot);
            if (player == null || !Modes.TryGetValue(player, out Mode mode)) return null;
            GameLiving target = mode.Pull;
            if (target?.IsAlive != true || target.ObjectState != GameObject.eObjectState.Active ||
                target.CurrentRegionID != player.CurrentRegionID)
            {
                mode.Pull = null;
                return null;
            }
            return Allows(bot, target) ? target : null;
        }
    }
}
