namespace DOL.GS
{
    /// <summary>
    /// When a bot (gamebot or companion: the same brain) fires a class ability a 2003 veteran used (M8, veteran review
    /// 2026-11-11). Pure rules so the tests read them; the brain supplies what it already knows, so none of these costs
    /// an area search.
    /// </summary>
    public static class BotClassAbilities165
    {
        /// <summary>
        /// A pet caster nukes once its own pet holds the monster below this health. Prima's Spiritmaster routine waits
        /// for 75% (O10, owner 2026-11-11); the other pet casters keep 85% (period Enchanter advice: "send pet, start
        /// nuking at ~85%").
        /// </summary>
        public static int PetHoldsDamageAbovePercent(eCharacterClass characterClass) =>
            characterClass == eCharacterClass.Spiritmaster ? 75 : 85;

        /// <summary>
        /// Triple Wield (Blademaster, 30 s, 7 min reuse) goes on the fights that need a burst, the same test Berserk
        /// uses (target at or above the bot's level, a player-like enemy, two monsters on it, or the bot under 60%),
        /// with the target in reach and the effect not already running.
        /// </summary>
        public static bool TripleWieldWorthwhile(bool hardFight, bool inMeleeReach, bool alreadyActive) =>
            hardFight && inMeleeReach && !alreadyActive;

        /// <summary>
        /// Flurry (Blademaster, 2 min reuse) hits only players and keep guards, as the ability itself allows; a gamebot
        /// stands in for a player. In front of the bot, within the ability's 135 units.
        /// </summary>
        public static bool FlurryTargetAllowed(GameObject target) =>
            FlurryTargetAllowed(target is GamePlayer or GameBot, target is Keeps.GameKeepGuard);

        public static bool FlurryTargetAllowed(bool playerLike, bool keepGuard) => playerLike || keepGuard;

        public const int FlurryRange = 135;

        /// <summary>Shields a tank needs before it uses Engage (a shield-spec tank, not a two-hander with a few points).</summary>
        public const int EngageShieldSpec = 25;

        /// <summary>The longest a bot stays engaged without swinging.</summary>
        public const int EngageMaxMilliseconds = 15_000;

        /// <summary>
        /// Engage (M8, Prima Revised 7341-7348): a shield tank with its shield up concentrates on blocking one enemy that
        /// is on it and nobody hit in the last 5 s (the ability's own rule; Engage blocks only that enemy): an enemy archer
        /// (player-like, bow out) shooting at it while it closes in, or a monster coming at it while the group's healer is
        /// low on power. Never with another enemy already meleeing it.
        /// </summary>
        public static bool EngageWorthwhile(bool shieldTank, bool shieldUp, bool enemyOnBot, bool enemyAttackedRecently,
            bool otherEnemyInMelee, bool enemyArcher, bool healerLowOnPower) =>
            shieldTank && shieldUp && enemyOnBot && !enemyAttackedRecently && !otherEnemyInMelee &&
            (enemyArcher || healerLowOnPower);

        /// <summary>
        /// The bot stays engaged while nobody else hits the enemy (Engage then blocks nothing extra), for at most
        /// <see cref="EngageMaxMilliseconds"/>: against an archer until it reaches it, against a monster while the healer
        /// is still low on power. Then it swings again.
        /// </summary>
        public static bool EngageHolds(bool enemyAttackedRecently, long engagedMilliseconds, bool enemyArcher,
            bool inMeleeReach, bool healerLowOnPower) =>
            !enemyAttackedRecently && engagedMilliseconds < EngageMaxMilliseconds &&
            (enemyArcher ? !inMeleeReach : healerLowOnPower);

        public enum PaladinRole { Solo, MainTank, OffTank }

        /// <summary>
        /// Pure (O1 with M2): the Paladin's chant role. Solo when it has no party. In a gamebot party with a role sheet
        /// (BotGroupRoles, the spec-ranked tanks) the sheet decides: its main tank is the main tank, any other Paladin
        /// the off-tank. Without a sheet (a player's group, a party not yet formed) a Paladin with another tank beside
        /// it is the off-tank, else the main tank.
        /// </summary>
        public static PaladinRole PaladinRoleFor(bool grouped, bool? sheetSaysMainTank, bool hasTankPartner) =>
            !grouped ? PaladinRole.Solo :
            (sheetSaysMainTank ?? !hasTankPartner) ? PaladinRole.MainTank : PaladinRole.OffTank;

        /// <summary>A Paladin keeps a chant it started for at least this long before it changes (O1).</summary>
        public const int PaladinChantHoldMilliseconds = 10_000;

        /// <summary>
        /// O1 (owner 2026-11-11): a Paladin holds one chant (no cycling) and picks it by situation. Main tank: the heal
        /// chant (Prima: "full time will guarantee that you keep the aggro"). Off-tank: the damage add. Solo: the damage
        /// add, the heal chant when hurt (Prima 6237-6280). The endurance chant out of a fight, and in a fight only when
        /// its own endurance is under 30% and it is not tanking. The 1.64 client table has all four (Refreshment, Battle,
        /// Crusader's, Chant of Endurance); a missing one falls back to damage add, heal, endurance, armor.
        /// </summary>
        public static eSpellType PaladinChant(PaladinRole role, bool fighting, bool tanking, int healthPercent,
            int endurancePercent, System.Collections.Generic.ICollection<eSpellType> available)
        {
            eSpellType want;
            if (!fighting || endurancePercent < 30 && !tanking)
                want = eSpellType.EnduranceRegenBuff;
            else
                want = role switch
                {
                    PaladinRole.MainTank => eSpellType.CombatHeal,
                    PaladinRole.OffTank => eSpellType.DamageAdd,
                    _ => healthPercent < 70 ? eSpellType.CombatHeal : eSpellType.DamageAdd,
                };
            if (available.Contains(want))
                return want;
            foreach (eSpellType fallback in new[] { eSpellType.DamageAdd, eSpellType.CombatHeal, eSpellType.EnduranceRegenBuff,
                         eSpellType.SpecArmorFactorBuff })
                if (available.Contains(fallback))
                    return fallback;
            return eSpellType.EnduranceRegenBuff;
        }

        /// <summary>Pure: keep the running chant while it is younger than the hold (and still a chant the Paladin has).</summary>
        public static bool PaladinKeepsChant(bool activeIsTheOneItStarted, long sinceMilliseconds) =>
            activeIsTheOneItStarted && sinceMilliseconds < PaladinChantHoldMilliseconds;

        /// <summary>
        /// Rapid Fire (Ranger) is a stance: on for a runner (a monster under 30% health moving away and not on the
        /// archer: get the arrow off before it brings friends) or a player-like enemy that is casting or running
        /// (Prima: Rapid Fire finishes a runner and races a caster); off otherwise, where full-strength shots are better.
        /// </summary>
        public static bool WantsRapidFire(bool targetPlayerLike, bool targetMoving, bool targetCasting,
            int targetHealthPercent, bool targetOnArcher)
        {
            if (targetPlayerLike)
                return targetCasting || targetMoving && !targetOnArcher;
            return targetMoving && targetHealthPercent < 30 && !targetOnArcher;
        }
    }
}
