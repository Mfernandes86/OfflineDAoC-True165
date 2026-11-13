namespace DOL.GS
{
    /// <summary>
    /// The launcher's two XP rate options (owner 2026-11-10): Player XP rate (xp_rate) and
    /// Gamebot XP rate (bot_xp_rate). Both default to 1x. A hired companion earns at the player rate, as in the old
    /// repo (bot_xp_rate never applied to companion helpers), so it keeps pace with the player it follows.
    /// </summary>
    public static class XpRates165
    {
        /// <summary>A bad stored rate (NaN, infinite, zero or less) counts as the original 1x, as the launcher reads it
        /// (XpRateSettings.Parse; owner 2026-11-10).</summary>
        public static double Sanitize(double rate) => double.IsFinite(rate) && rate > 0 ? rate : 1.0;

        /// <summary>The player's kill XP multiplier: the launcher's Player XP rate everywhere. In the frontier zones, the
        /// battlegrounds, Darkness Falls, Summoner's Hall and the frontier dungeons the RvR rate (1x by default) multiplies
        /// it, as it does for gamebots. Until 2026-11-12 the RvR rate REPLACED the player rate there, so a 5x setting gave
        /// 1x in Darkness Falls (owner, GitHub #51: "make these areas count for the player and bot xp rate").</summary>
        public static double PlayerMultiplier(bool isRvR, double playerRate, double rvrRate) =>
            Sanitize(playerRate) * (isRvR ? Sanitize(rvrRate) : 1.0);

        /// <summary>
        /// A gamebot's kill XP multiplier. An autonomous bot uses the gamebot rate (times the RvR rate in RvR zones,
        /// as before); a hired companion uses exactly the player's multiplier.
        /// </summary>
        public static double BotMultiplier(bool hiredCompanion, bool isRvR, double playerRate, double botRate, double rvrRate) =>
            hiredCompanion
                ? PlayerMultiplier(isRvR, playerRate, rvrRate)
                : Sanitize(botRate) * (isRvR ? Sanitize(rvrRate) : 1.0);
    }
}
