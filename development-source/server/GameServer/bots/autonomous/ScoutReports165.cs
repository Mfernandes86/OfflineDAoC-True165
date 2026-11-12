using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>
    /// Scout "inc" reports (veteran-bots review 2026-11-11, M10 / rvr-social G4; owner decision O9). The frontier
    /// stealthers watched the milegates and portal-keep roads and reported nothing. A 2003 scout called "~20 mids inc to
    /// amg from the bridge" on the chat group; defenders answered "omw" or "numbers?".
    /// - A stealther's own frontier scan (it already has the visible enemies) with 4 or more enemy players and gamebots
    ///   in sight counts them by realm and says one line on the realm chat group: "~20 mids inc near Ben".
    /// - At most one report per spot (a 4,000-unit cell) per 3 minutes and one per realm per 45 seconds.
    /// - O9: a report near one of its realm's keeps that a planned siege of the sighted realm is marching on starts that
    ///   keep's defense rally now (AutonomousRvrEventLayer.ObserveScoutReport) instead of at the first blow.
    /// Cost: a count over the scan's own short list, only when 4+ enemies are in sight; the landmark lookup only when a
    /// report is actually made.
    /// </summary>
    public static class ScoutReports165
    {
        public const int MinimumEnemies = 4;
        public const long SpotIntervalMilliseconds = 3 * 60_000;
        public const long RealmIntervalMilliseconds = 45_000;
        public const int SpotCell = 4_000;
        public const int KeepRange = 8_000;

        private static readonly ConcurrentDictionary<(eRealm, ushort, int, int), long> LastSpot = new();
        private static readonly ConcurrentDictionary<eRealm, long> LastRealm = new();

        /// <summary>The biggest enemy realm in sight and its count (players and gamebots only), or (None, 0).</summary>
        public static (eRealm Realm, int Count) LargestEnemy(eRealm own, IEnumerable<GameLiving> visible)
        {
            int alb = 0, mid = 0, hib = 0;
            foreach (GameLiving living in visible)
            {
                if (living is not (GamePlayer or GameBot) || !living.IsAlive || living.Realm == own)
                    continue;
                switch (living.Realm)
                {
                    case eRealm.Albion: alb++; break;
                    case eRealm.Midgard: mid++; break;
                    case eRealm.Hibernia: hib++; break;
                }
            }
            return Largest(alb, mid, hib);
        }

        /// <summary>The realm with the most of the three counts (Albion, Midgard, Hibernia), or (None, 0).</summary>
        public static (eRealm Realm, int Count) Largest(int alb, int mid, int hib)
        {
            if (alb >= mid && alb >= hib)
                return alb == 0 ? (eRealm.None, 0) : (eRealm.Albion, alb);
            return mid >= hib ? (eRealm.Midgard, mid) : (eRealm.Hibernia, hib);
        }

        /// <summary>The report line: "~20 mids inc near Ben".</summary>
        public static string Line(eRealm sighted, int count, string landmark, Random random)
        {
            string head = $"{HeraldShorthand165.RoughCount(count)} {HeraldShorthand165.RealmPlural(sighted)}";
            string where = string.IsNullOrWhiteSpace(landmark) ? string.Empty : $" {landmark}";
            return (random ?? Random.Shared).Next(3) switch
            {
                0 => $"{head} inc{where}",
                1 => $"{head}{where}, heads up",
                _ => $"inc {head}{where}",
            };
        }

        /// <summary>Whether a report from this spot may go out now (and marks it when it does).</summary>
        public static bool TryClaim(eRealm realm, ushort region, int x, int y, long now)
        {
            var spot = (realm, region, x / SpotCell, y / SpotCell);
            if (LastSpot.TryGetValue(spot, out long lastSpot) && now - lastSpot < SpotIntervalMilliseconds)
                return false;
            if (LastRealm.TryGetValue(realm, out long lastRealm) && now - lastRealm < RealmIntervalMilliseconds)
                return false;
            if (LastSpot.Count > 4_096)
                LastSpot.Clear();
            LastSpot[spot] = now;
            LastRealm[realm] = now;
            return true;
        }

        /// <summary>Tests: forget the rate limits.</summary>
        public static void Reset()
        {
            LastSpot.Clear();
            LastRealm.Clear();
        }

        /// <summary>FrontierThreat: a stealther's scan saw these enemies.</summary>
        public static void Observe(GameBot scout, GameLiving[] visible)
        {
            try
            {
                if (scout?.CurrentRegion == null || visible == null || visible.Length < MinimumEnemies ||
                    !ClientService.Instance.GetPlayersOfRealm(scout.Realm).Any())
                    return;
                (eRealm sighted, int count) = LargestEnemy(scout.Realm, visible);
                if (count < MinimumEnemies || !TryClaim(scout.Realm, scout.CurrentRegionID, scout.X, scout.Y, GameLoop.GameLoopTime))
                    return;
                AbstractGameKeep keep = NearestKeep(scout);
                string landmark = keep != null ? $"near {HeraldShorthand165.Nickname(keep.Name)}" : NearestAnchor(scout);
                BotChatChannels165.Send(BotChatChannels165.PeriodChannels ? BotChatChannels165.Channel.RvR : BotChatChannels165.Channel.Faction,
                    scout, Line(sighted, count, landmark, Random.Shared));
                FeatureCounters165.Add("scout_inc_report");
                if (keep != null && keep.Realm == scout.Realm &&
                    AutonomousRvrEventLayer.ObserveScoutReport($"rvr-keep-{keep.KeepID}", scout.Realm, sighted))
                    FeatureCounters165.Add("scout_inc_rally");
            }
            catch (Exception)
            {
                // a report never breaks the scout's own fight
            }
        }

        private static AbstractGameKeep NearestKeep(GameBot scout)
        {
            ICollection<AbstractGameKeep> keeps = GameServer.KeepManager?.GetKeepsOfRegion(scout.CurrentRegionID);
            if (keeps == null)
                return null;
            AbstractGameKeep best = null;
            long bestDistance = (long)KeepRange * KeepRange;
            foreach (AbstractGameKeep keep in keeps)
            {
                if (keep == null || keep.IsPortalKeep)
                    continue;
                long dx = keep.X - scout.X, dy = keep.Y - scout.Y;
                long d = dx * dx + dy * dy;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = keep;
                }
            }
            return best;
        }

        private static string NearestAnchor(GameBot scout)
        {
            string name = null;
            long best = 6_000L * 6_000L;
            foreach (AutonomousFrontierHotspots.Anchor anchor in AutonomousFrontierHotspots.For(scout.CurrentRegionID))
            {
                long dx = anchor.X - scout.X, dy = anchor.Y - scout.Y;
                long d = dx * dx + dy * dy;
                if (d < best)
                {
                    best = d;
                    name = anchor.Name;
                }
            }
            return name != null ? $"at {name}" : scout.CurrentZone?.Description is { } zone ? $"in {zone}" : null;
        }
    }
}
