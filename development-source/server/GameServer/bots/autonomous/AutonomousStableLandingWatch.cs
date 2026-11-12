using System.Collections.Concurrent;
using System.Collections.Generic;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Owner 2026-11-10 (P3 watch): one line when a gamebot dies within <see cref="WatchMilliseconds"/> of a horse landing,
    /// so the 5-minute death rate per landing can be measured (East Lough Derg, and the Mag Mell / Tir na mBeo landings
    /// solo bots take instead).
    /// Veteran review M3 (2026-11-11): the watch now also feeds the horse choice. When
    /// <see cref="DeadlyLandingBots"/> different bots of one 5-level band died soon after landing at one destination within
    /// the last <see cref="DeadlyLandingWindowMilliseconds"/>, solo bots of that band skip that landing (they take another
    /// horse or walk) until the deaths age out (1,250 landing deaths in 4.2 h; Howth alone 260).
    /// </summary>
    public static class AutonomousStableLandingWatch
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousStableLandingWatch));
        public const long WatchMilliseconds = 5 * 60_000;
        private const int MaxEntries = 10_000;

        private readonly record struct Landing(string Destination, ushort Region, int X, int Y, long Tick);

        private static readonly ConcurrentDictionary<long, Landing> Landings = new();

        /// <summary>Pure rule: a death this soon after the landing counts.</summary>
        public static bool CountsAsLandingDeath(long landedTick, long now) =>
            landedTick > 0 && now >= landedTick && now - landedTick <= WatchMilliseconds;

        public static void Arrived(GameBot bot, string destination)
        {
            if (bot == null || bot.DatabaseID <= 0)
                return;
            long now = GameLoop.GameLoopTime;
            if (Landings.Count >= MaxEntries)
                foreach (var entry in Landings)
                    if (!CountsAsLandingDeath(entry.Value.Tick, now))
                        Landings.TryRemove(entry.Key, out _);
            Landings[bot.DatabaseID] = new Landing(destination ?? string.Empty, bot.CurrentRegionID, bot.X, bot.Y, now);
        }

        public static void Died(GameBot bot, GameObject killer)
        {
            if (bot == null || bot.DatabaseID <= 0 || !Landings.TryRemove(bot.DatabaseID, out Landing landing))
                return;
            long now = GameLoop.GameLoopTime;
            if (!CountsAsLandingDeath(landing.Tick, now))
                return;
            RecordLandingDeath(landing.Region, landing.Destination, bot.Level, bot.DatabaseID, now);
            Log.Info($"AUTONOMOUS_STABLE_LANDING_DEATH bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                     $"solo={bot.Group == null} destination=\"{landing.Destination}\" seconds={(now - landing.Tick) / 1000} " +
                     $"killer=\"{killer?.Name}\" killer_level={(killer as GameLiving)?.Level ?? 0} " +
                     $"landing={landing.Region}:{landing.X},{landing.Y} at={bot.CurrentRegionID}:{bot.X},{bot.Y}");
        }

        public const int DeadlyLandingBots = 6;
        public const long DeadlyLandingWindowMilliseconds = 60 * 60_000;
        private const int DeathsKeptPerLanding = 32;

        public static int LevelBand(int level) => System.Math.Max(0, level) / 5;

        private static readonly ConcurrentDictionary<(ushort Region, string Destination, int Band), Queue<(long BotId, long Tick)>>
            LandingDeaths = new();

        private static void RecordLandingDeath(ushort region, string destination, int level, long botId, long now)
        {
            if (string.IsNullOrEmpty(destination))
                return;
            Queue<(long BotId, long Tick)> deaths = LandingDeaths.GetOrAdd((region, destination, LevelBand(level)), _ => new());
            lock (deaths)
            {
                deaths.Enqueue((botId, now));
                while (deaths.Count > DeathsKeptPerLanding)
                    deaths.Dequeue();
            }
        }

        /// <summary>Pure rule: enough different bots died here within the window.</summary>
        public static bool IsDeadly(IEnumerable<(long BotId, long Tick)> deaths, long now)
        {
            var bots = new HashSet<long>();
            foreach ((long botId, long tick) in deaths)
                if (now >= tick && now - tick <= DeadlyLandingWindowMilliseconds)
                    bots.Add(botId);
            return bots.Count >= DeadlyLandingBots;
        }

        /// <summary>Solo bots of this level band should skip this landing for now (cheap: one dictionary lookup).</summary>
        public static bool IsDeadlyLanding(ushort region, string destination, int level, long now)
        {
            if (string.IsNullOrEmpty(destination) ||
                !LandingDeaths.TryGetValue((region, destination, LevelBand(level)), out Queue<(long BotId, long Tick)> deaths))
                return false;
            lock (deaths)
                return IsDeadly(deaths, now);
        }
    }
}
