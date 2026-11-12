using System.Collections.Generic;
using System.Threading;
using DOL.GS;
using DOL.GS.Keeps;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Q30 (veteran review 2026-11-11, completeness review): the RvR healer's support target and the relic-carrier list,
    /// cached. Before this every heal check of every RvR healer ran a 2,000-unit area search, a navmesh line-of-sight
    /// raycast for each keep guard in it and a sort, and walked the relic list with LINQ twice: thousands of raycasts a
    /// second at a keep fight. Now:
    /// - the relic carriers are read at most every 2 s for the whole server;
    /// - a healer keeps its chosen support target for 2 s, re-checking only that it is alive, in range and under 85%;
    /// - a rebuild ranks the candidates first (relic carrier, keep lord, other guards; then the lowest health) and raycasts
    ///   only the best one, moving to the next only when that one has no line of sight. "Nobody needs it" is kept 2 s too.
    /// The choice itself is unchanged.
    /// </summary>
    public partial class BotBrain
    {
        public const long RvrSupportHoldMilliseconds = 2_000;
        public const int RvrSupportRadius = 2_000;
        public const int RvrSupportBelowPercent = 85;
        public const long RelicCarrierRefreshMilliseconds = 2_000;

        private static GameLiving[] _relicCarriers = [];
        private static long _relicCarriersUntil = long.MinValue;
        private static readonly object RelicCarriersLock = new();

        private GameLiving _rvrSupportTarget;
        private long _rvrSupportUntil = long.MinValue;

        /// <summary>The living relic carriers, refreshed at most every 2 s for the whole server.</summary>
        internal static GameLiving[] RelicCarriers()
        {
            long now = GameLoop.GameLoopTime;
            if (now < Volatile.Read(ref _relicCarriersUntil))
                return Volatile.Read(ref _relicCarriers);
            lock (RelicCarriersLock)
            {
                if (now < _relicCarriersUntil)
                    return _relicCarriers;
                var carriers = new List<GameLiving>(2);
                foreach (GameRelic relic in RelicMgr.GetRelics() ?? [])
                    if (relic?.CurrentCarrier is GameLiving carrier && carrier.IsAlive)
                        carriers.Add(carrier);
                Volatile.Write(ref _relicCarriers, carriers.ToArray());
                Volatile.Write(ref _relicCarriersUntil, now + RelicCarrierRefreshMilliseconds);
                return _relicCarriers;
            }
        }

        /// <summary>Pure: a support candidate's rank, lower first - a relic carrier, then a keep lord, then other guards.</summary>
        public static int RvrSupportRank(bool carriesRelic, bool keepLord) => carriesRelic ? 0 : keepLord ? 1 : 2;

        /// <summary>Pure: the held support target is kept (no new search) while its hold lasts and it still needs it.</summary>
        public static bool KeepsRvrSupport(long now, long holdUntil, bool alive, bool inRange, int healthPercent) =>
            now < holdUntil && alive && inRange && healthPercent < RvrSupportBelowPercent;

        /// <summary>Pure: the hold has run out (a held "nobody" counts as held until then).</summary>
        public static bool RvrSupportHoldOver(long now, long holdUntil) => now >= holdUntil;

        private bool SupportCandidate(GameLiving living) =>
            living?.IsAlive == true && living.Realm == Body.Realm && living.HealthPercent < RvrSupportBelowPercent &&
            living.CurrentRegion == Body.CurrentRegion && CanHealRelicCarrier(living);

        /// <summary>The RvR healer's support target (a relic carrier or keep guard of its realm under 85%), or null.</summary>
        private GameLiving RvrSupportTarget()
        {
            long now = GameLoop.GameLoopTime;
            GameLiving held = _rvrSupportTarget;
            if (held != null)
            {
                if (KeepsRvrSupport(now, _rvrSupportUntil, held.IsAlive && held.Realm == Body.Realm &&
                        held.CurrentRegion == Body.CurrentRegion, CanHealRelicCarrier(held), held.HealthPercent))
                    return held;
            }
            else if (!RvrSupportHoldOver(now, _rvrSupportUntil))
                return null;

            var candidates = new List<(GameLiving Living, int Rank, int Health)>();
            foreach (GameLiving carrier in RelicCarriers())
                if (SupportCandidate(carrier))
                    candidates.Add((carrier, RvrSupportRank(GameRelic.IsPlayerCarryingRelic(carrier), false), carrier.HealthPercent));
            foreach (GameNPC npc in Body.GetNPCsInRadius(RvrSupportRadius))
                if (npc is GameKeepGuard guard && SupportCandidate(guard))
                    candidates.Add((guard, RvrSupportRank(GameRelic.IsPlayerCarryingRelic(guard), guard is GuardLord), guard.HealthPercent));
            candidates.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Health.CompareTo(b.Health));

            GameLiving chosen = null;
            var nav = PathfindingProvider.Instance;
            foreach (var candidate in candidates)
            {
                GameLiving living = candidate.Living;
                if (nav.HasLineOfSight(Body.CurrentZone, new(Body.X, Body.Y, Body.Z), new(living.X, living.Y, living.Z), nav.DefaultFilters))
                {
                    chosen = living;
                    break;
                }
            }
            _rvrSupportTarget = chosen;
            _rvrSupportUntil = now + RvrSupportHoldMilliseconds;
            return chosen;
        }
    }
}
