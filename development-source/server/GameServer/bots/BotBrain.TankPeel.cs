using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.GS;
using DOL.GS.SkillHandler;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Tanks use Protect, Guard and Intercept as players do (owner 2026-10-29: bots only ever taunted). In a group fight,
    /// once a second: Protect sits on the group's healer (or the squishy member being hit), so the monsters' anger at it
    /// goes to the tank; Guard, only with a shield in use, covers the squishiest member a monster is hitting in melee
    /// (otherwise the healer); Intercept, on its one-minute reuse, takes the blows for a squishy member below half health
    /// being hit next to the tank. The existing priority taunt still pulls monsters off group members.
    /// </summary>
    public partial class BotBrain
    {
        private long _nextTankPeelTick;
        public const int InterceptHealthPercent = 50;

        private void MaintainTankPeel()
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot?.Group == null || bot.Group.MemberCount < 2 || !bot.IsAlive || bot.IsIncapacitated || now < _nextTankPeelTick)
                return;
            _nextTankPeelTick = now + 1_000;
            bool guard = bot.HasAbility(Abilities.Guard), protect = bot.HasAbility(Abilities.Protect),
                intercept = bot.HasAbility(Abilities.Intercept);
            if (!guard && !protect && !intercept)
                return;

            GameLiving[] squishies = bot.Group.GetMembersInTheGroup()
                .Where(member => member != null && member != bot && member.IsAlive &&
                    member.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(member, ProtectAbilityHandler.PROTECT_DISTANCE) &&
                    PeelClass(member) is eCharacterClass characterClass && BotPartyRoles.For(characterClass) != BotPartyRole.Tank)
                .ToArray();
            if (squishies.Length == 0)
                return;
            // M5 (veteran review 2026-11-11): who is being hit in melee comes from the group's fight board
            HashSet<GameLiving> hitInMelee = FightBoard()?.HitInMelee;
            bool BeingHit(GameLiving member) => hitInMelee?.Contains(member) ?? BeingHitInMelee(member);
            GameLiving threatened = squishies.Where(BeingHit).OrderBy(member => member.HealthPercent).FirstOrDefault();
            // M2: protect whichever healer is being hit (then the lowest), not always the first one found; with an off-tank
            // near, the main tank covers the second healer and the off-tank the first
            GameLiving[] healers = squishies.Where(member =>
                    PeelClass(member) is eCharacterClass characterClass && BotPartyRoles.IsHealingClass(characterClass))
                .OrderBy(member => BeingHit(member) ? 0 : 1).ThenBy(member => member.HealthPercent).ToArray();
            GameLiving healer = healers.Length == 0 ? null : healers[PeelHealerIndex(healers.Length,
                Classic165Mode.IsOn && BotGroupRoles.IsMainTank(bot) &&
                BotGroupRoles.OffTankNear(bot, ProtectAbilityHandler.PROTECT_DISTANCE) != null)];

            // The player's choice for a hired tank (AI Options): guard and protect the player, the healer, or nobody.
            int choice = CompanionOptions165.Choice(bot, "guard");
            if (choice == 3)
                return;
            GameLiving chosen = choice switch
            {
                1 => AssistedPlayer is { IsAlive: true } you && you.CurrentRegion == bot.CurrentRegion &&
                     bot.IsWithinRadius(you, ProtectAbilityHandler.PROTECT_DISTANCE) ? you : null,
                2 => healer,
                _ => null,
            };
            // True 1.65 Mode: Protect never goes on the player who is the main tank (it would pull their monster off them)
            GameLiving protectThreatened = threatened != null && IsMainTankPlayer(threatened)
                ? squishies.Where(member => member != threatened && BeingHit(member))
                    .OrderBy(member => member.HealthPercent).FirstOrDefault()
                : threatened;
            if (protect && (chosen ?? healer ?? protectThreatened) is GameLiving protectTarget)
                Apply(eEffect.Protect, protectTarget);
            // Guard blocks only within 256 of the guarded member: the member being hit within reach first, else the
            // healer or another squishy member within reach (Prima: guarded healers stick close; the tank never left its
            // monster to stand by the healer, so a far-away guard did nothing, 2026-11-05).
            GameLiving guardTarget = chosen ?? squishies
                .Where(member => bot.IsWithinRadius(member, GuardAbilityHandler.GUARD_DISTANCE))
                .OrderBy(member => member == threatened ? 0 : member == healer ? 1 : BeingHit(member) ? 2 : 3)
                .ThenBy(member => member.HealthPercent)
                .FirstOrDefault() ?? threatened ?? healer;
            if (guard && ShieldInUse(bot) && guardTarget != null)
                Apply(eEffect.Guard, guardTarget);
            if (intercept && threatened != null && threatened.HealthPercent < InterceptHealthPercent &&
                bot.IsWithinRadius(threatened, InterceptAbilityHandler.INTERCEPT_DISTANCE) &&
                bot.GetAbility(Abilities.Intercept) is Ability interceptAbility && bot.GetSkillDisabledDuration(interceptAbility) <= 0)
            {
                InterceptAbilityHandler.CheckExistingEffectsOnTarget(bot, threatened, false, out bool ours, out var other);
                if (!ours && other == null)
                {
                    InterceptAbilityHandler.CancelOurEffectThenAddOnTarget(bot, threatened);
                    bot.DisableSkill(interceptAbility, InterceptAbilityHandler.REUSE_TIMER);
                }
            }
        }

        /// <summary>The companion's player, being the main tank against the monster they fight (BotMeleeStylePolicy).</summary>
        private bool IsMainTankPlayer(GameLiving member) =>
            member is GamePlayer player && player == BotMeleeStylePolicy.TauntRulesPlayer(BotBody) &&
            BotMeleeStylePolicy.PlayerIsMainTank(player, player.TargetObject as GameLiving ?? Body.TargetObject as GameLiving);

        private void Apply(eEffect kind, GameLiving target)
        {
            if (kind == eEffect.Guard)
            {
                GuardAbilityHandler.CheckExistingEffectsOnTarget(Body, target, false, out bool ours, out var other);
                if (!ours && other == null)
                    GuardAbilityHandler.CancelOurEffectThenAddOnTarget(Body, target);
            }
            else
            {
                ProtectAbilityHandler.CheckExistingEffectsOnTarget(Body, target, false, out bool ours, out var other);
                if (!ours && other == null)
                    ProtectAbilityHandler.CancelOurEffectThenAddOnTarget(Body, target);
            }
        }

        private static eCharacterClass? PeelClass(GameLiving member) => member switch
        {
            GameBot { CharacterClass: not null } memberBot => (eCharacterClass)memberBot.CharacterClass.ID,
            GamePlayer { CharacterClass: not null } player => (eCharacterClass)player.CharacterClass.ID,
            _ => null,
        };

        /// <summary>
        /// An enemy in melee range is attacking this member. Q9 (veteran review 2026-11-11): read from the member's own
        /// attacker list (what the game already keeps) instead of a 400-unit area search per groupmate per second - and
        /// enemy players and bots count too, so Guard and Intercept work in RvR (the area search skipped them).
        /// </summary>
        public static bool BeingHitInMelee(GameLiving member)
        {
            if (member?.attackComponent == null)
                return false;
            foreach (GameLiving attacker in member.attackComponent.AttackerTracker.Attackers)
                if (IsMeleeAttackerOn(attacker, member))
                    return true;
            return false;
        }

        /// <summary>A live attacker that is on <paramref name="member"/>, fighting, and in melee reach of it.</summary>
        public static bool IsMeleeAttackerOn(GameLiving attacker, GameLiving member) =>
            attacker != null && attacker.IsAlive && attacker.TargetObject == member &&
            (attacker.IsAttacking || attacker.InCombat) && attacker.IsWithinRadius(member, attacker.MeleeAttackRange + 50);

        private static bool ShieldInUse(GameBot bot) =>
            bot.ActiveWeaponSlot == eActiveWeaponSlot.Standard &&
            bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon) is { Object_Type: (int)eObjectType.Shield };

        /// <summary>
        /// Pure (M2): which of the healers (best first: the one being hit, then the lowest) this tank guards and protects.
        /// A main tank with an off-tank near takes the second, so two tanks cover two healers.
        /// </summary>
        public static int PeelHealerIndex(int healers, bool mainTankWithOffTank) =>
            healers <= 0 ? -1 : mainTankWithOffTank && healers > 1 ? 1 : 0;
    }

    /// <summary>
    /// M2 (veteran review 2026-11-11): the add each tank has gone for (its peel), for a few seconds, so two tanks never
    /// run at the same add and a main tank can see whether its off-tank is already busy with one. Lock-free: one small
    /// holder per add and one per tank, weak keys.
    /// </summary>
    public static class BotPeelReservations
    {
        public const long HoldMilliseconds = 3_000;

        private sealed class AddHolder
        {
            public volatile GameLiving Tank;
            public long Until;
        }

        private sealed class TankHolder
        {
            public long BusyUntil;
        }

        private static readonly ConditionalWeakTable<GameLiving, AddHolder> Adds = new();
        private static readonly ConditionalWeakTable<GameLiving, TankHolder> Tanks = new();

        public static void Reserve(GameLiving add, GameLiving tank, long until)
        {
            if (add == null || tank == null)
                return;
            AddHolder holder = Adds.GetValue(add, _ => new AddHolder());
            holder.Tank = tank;
            Volatile.Write(ref holder.Until, until);
            Volatile.Write(ref Tanks.GetValue(tank, _ => new TankHolder()).BusyUntil, until);
        }

        /// <summary>Another tank has gone for this add within the last few seconds.</summary>
        public static bool IsReservedByOther(GameLiving add, GameLiving tank, long now) =>
            add != null && Adds.TryGetValue(add, out AddHolder holder) && now < Volatile.Read(ref holder.Until) &&
            holder.Tank != tank && holder.Tank is { IsAlive: true };

        /// <summary>This tank is busy with a peel of its own.</summary>
        public static bool IsBusy(GameLiving tank, long now) =>
            tank != null && Tanks.TryGetValue(tank, out TankHolder holder) && now < Volatile.Read(ref holder.BusyUntil);
    }
}
