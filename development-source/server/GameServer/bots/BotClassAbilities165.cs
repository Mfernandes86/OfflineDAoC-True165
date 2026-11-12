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
