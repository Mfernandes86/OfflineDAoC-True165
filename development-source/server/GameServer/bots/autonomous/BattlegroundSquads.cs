using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Battleground squads (veteran-bots review 2026-11-11, M10 / rvr-social G7). Battleground bots outside the keep
    /// roamed alone, each to its own random landmark every two minutes; Thidranki and Murdaggan had packs of about eight
    /// camping the portal approaches and contesting the central keep. A realm's roamers are now split into four squads by
    /// id, and a squad's members head for the same landmark (the other realms' portal approaches or the central keep) in
    /// the same two-minute window, each to its own point around it, so they arrive and fight as a pack. No group is formed
    /// and nothing new runs: the same roam pick, made the same way by every member of a squad.
    /// </summary>
    public static class BattlegroundSquads
    {
        [ServerProperty("bots", "bot_battleground_squads",
            "Do a realm's roaming battleground bots move in four squads, each heading for the same landmark (portal approach or central keep) at the same time, instead of every bot picking its own?", true)]
        public static bool Enabled = true;

        public const int Squads = 4;
        public const long WindowMilliseconds = 120_000;

        /// <summary>The bot's squad, fixed by its id.</summary>
        public static int SquadOf(long botId) => (int)(((ulong)botId * 0x9E3779B97F4A7C15UL >> 35) % Squads);

        /// <summary>The share of a window over which one squad's members re-pick (four squads: 0-100 s of the 120 s).</summary>
        public const long RepickSpreadMilliseconds = 25_000;

        /// <summary>
        /// When in the next window a member re-picks its roam point (performance review 2026-11-11): each squad at its own
        /// 25-second slot, its members spread over that slot - not every roamer of every realm within the same 5 s, a burst
        /// of navmesh picks and route plans every two minutes. Always inside the window, so the squad still agrees.
        /// </summary>
        public static long RepickOffsetMilliseconds(int squad, long objectId) =>
            squad * RepickSpreadMilliseconds + (long)((ulong)objectId % (ulong)RepickSpreadMilliseconds);

        /// <summary>The landmark a squad heads for in a window: the same for all its members, different between squads.</summary>
        public static int AnchorIndex(eRealm realm, int squad, long window, int anchorCount)
        {
            if (anchorCount <= 1)
                return 0;
            ulong key = (ulong)(((long)realm * 31 + squad) * 1_000_003 + window);
            ulong mixed = key * 0x9E3779B97F4A7C15UL;
            return (int)((mixed >> 33) % (ulong)anchorCount);
        }
    }
}
