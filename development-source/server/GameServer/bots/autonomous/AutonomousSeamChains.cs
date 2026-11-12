using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// owner 2026-11-10: zone crossings onto ground that reaches the goal's own ground. Albion bots took the Black Mtns.
    /// South corner onto the walled-off strip along the empty map gap (Camelot Hills 70035, Salisbury 28859, Campacorentin
    /// 7103): a crossing only had to lead to SOME ground of each next zone. Pure rules here; the itinerary applies them.
    /// </summary>
    public static class AutonomousSeamChains
    {
        [ServerProperty("bots", "bot_zone_step_target_ground",
            "Do bots crossing zone borders prefer crossings whose ground reaches the goal's own ground (when the goal's ground is known for sure)? Off: any crossing whose ground continues zone by zone, as before.", true)]
        public static bool TargetGround = true;

        /// <summary>The goal's nearest road anchor must be this close for its ground to count as known.</summary>
        public const float ConfidentAnchorDistance = 1_500f;
        /// <summary>No anchor of other ground may be within this much of the nearest one (goal between two grounds).</summary>
        public const float AmbiguityMargin = 512f;

        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Whether the ground past a crossing (its area in routeZoneIds[1]) reaches targetArea in the route's last zone,
        /// border by border by the seam file's area ids. Null when the data is missing (no border, or no area ids).
        /// </summary>
        public static bool? ChainReachesArea(Func<ushort, ushort, AutonomousZoneSeams.Crossing[]> seams,
            IReadOnlyList<ushort> routeZoneIds, int outsideArea, int targetArea)
        {
            if (seams == null || routeZoneIds == null || outsideArea < 0 || routeZoneIds.Count < 2) return null;
            var areas = new HashSet<int> { outsideArea };
            for (int i = 1; i + 1 < routeZoneIds.Count; i++)
            {
                AutonomousZoneSeams.Crossing[] next = seams(routeZoneIds[i], routeZoneIds[i + 1]);
                if (next == null) return null;
                var reached = new HashSet<int>();
                foreach (AutonomousZoneSeams.Crossing crossing in next)
                {
                    if (crossing.InsideArea < 0) return null;
                    if (areas.Contains(crossing.InsideArea)) reached.Add(crossing.OutsideArea);
                }
                if (reached.Count == 0) return false;
                areas = reached;
            }
            return areas.Contains(targetArea);
        }

        /// <summary>Filter only when at least one crossing is proven to reach the goal's ground.</summary>
        public static bool RequireTarget(IEnumerable<bool?> reaches)
        {
            if (reaches == null) return false;
            foreach (bool? reach in reaches)
                if (reach == true) return true;
            return false;
        }

        /// <summary>
        /// The goal's ground from its road anchors (nearest first, distance with height counted double): the nearest
        /// must be within maxDistance and no anchor of other ground within margin of it; otherwise null (not sure).
        /// </summary>
        public static int? ConfidentArea(IReadOnlyList<(float Distance, int Area)> nearestFirst,
            float maxDistance = ConfidentAnchorDistance, float margin = AmbiguityMargin)
        {
            if (nearestFirst == null || nearestFirst.Count == 0) return null;
            (float distance, int area) = nearestFirst[0];
            if (area < 0 || distance > maxDistance) return null;
            for (int i = 1; i < nearestFirst.Count; i++)
                if (nearestFirst[i].Area != area && nearestFirst[i].Distance <= distance + margin) return null;
            return area;
        }

        /// <summary>How much farther than the nearest road anchor a connected one may be and still label the point.</summary>
        public const float ConnectedAreaReach = 2_048f;

        /// <summary>
        /// owner 2026-11-12: the ground a point stands on for zone-chain planning. Its nearest road anchor's area, unless that
        /// area has no listed zone crossing at all (inSeamGraph false) and another area within reach of it does: then the
        /// nearest such area. Tuscaran Glacier's door (Delling Crater) stands on the main ground 69799, but the four nearest
        /// anchors are a floor piece stacked beside the ledge (11388, no crossing), so the zone chain to the door was empty.
        /// Ground without a crossing can never end or start a chain to another zone, so the switch only adds routes; every leg
        /// is still proven. Null without anchors (callers keep their old fallback).
        /// </summary>
        public static int? ConnectedArea(IReadOnlyList<(float Distance, int Area)> nearestFirst, Func<int, bool> inSeamGraph,
            float reach = ConnectedAreaReach)
        {
            if (nearestFirst == null || nearestFirst.Count == 0) return null;
            (float distance, int nearest) = nearestFirst[0];
            if (inSeamGraph == null || inSeamGraph(nearest)) return nearest;
            for (int i = 1; i < nearestFirst.Count && nearestFirst[i].Distance <= distance + reach; i++)
                if (inSeamGraph(nearestFirst[i].Area)) return nearestFirst[i].Area;
            return nearest;
        }

        /// <summary>
        /// Every (zone, area) that has a listed crossing, either way (built once per region from the seam file's area graph).
        /// Empty without area data, so <see cref="ConnectedArea"/> then keeps the nearest anchor as before.
        /// </summary>
        public static HashSet<(ushort Zone, int Area)> SeamGraphNodes(ushort regionId) =>
            GraphNodes.GetOrAdd(regionId, id =>
            {
                var nodes = new HashSet<(ushort Zone, int Area)>();
                foreach (var pair in AutonomousZoneSeams.AreaGraph(id))
                {
                    nodes.Add(pair.Key);
                    foreach (var next in pair.Value) nodes.Add(next);
                }
                return nodes;
            });

        /// <summary>The point's ground by <see cref="ConnectedArea"/> over the zone's road anchors (data lookups only).</summary>
        public static int? ConnectedAreaAt(ushort regionId, Zone zone, Vector3 point)
        {
            if (zone == null) return null;
            HashSet<(ushort Zone, int Area)> nodes = SeamGraphNodes(regionId);
            ushort zoneId = zone.ID;
            return ConnectedArea(AutonomousZoneRoads.AreasNear(zone, point, float.MaxValue), area => nodes.Contains((zoneId, area)));
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ushort, HashSet<(ushort Zone, int Area)>> GraphNodes = new();

        /// <summary>The goal's ground when known for sure (switch on, region in the seam file, goal inside the zone).</summary>
        public static int? ConfidentTargetArea(ushort regionId, Zone zone, Vector3 goal)
        {
            if (!TargetGround || regionId == 0 || zone == null) return null;
            if (goal.X < zone.XOffset || goal.X >= zone.XOffset + zone.Width || goal.Y < zone.YOffset || goal.Y >= zone.YOffset + zone.Height)
                return null;
            // owner 2026-11-10 (review): every zone step asks this, cache hits too (1,500-6,000 bots). The seam and road
            // data never change after load, so the answer is kept per exact goal and region coverage per region: no lock,
            // no lists, no sort on a repeat.
            var key = (regionId, zone.ID, (int)goal.X, (int)goal.Y, (int)goal.Z);
            if (TargetAreas.TryGetValue(key, out int known)) return known < 0 ? null : known;
            int? area = CoveredRegions.GetOrAdd(regionId, id => AutonomousZoneSeams.Covers(id))
                ? ConfidentArea(Connected(AutonomousZoneRoads.AreasNear(zone, goal, ConfidentAnchorDistance + AmbiguityMargin),
                    SeamGraphNodes(regionId), zone.ID))
                : null;
            if (TargetAreas.Count > MaxRememberedTargets) TargetAreas.Clear();
            TargetAreas[key] = area ?? -1;
            return area;
        }

        /// <summary>
        /// owner 2026-11-12: anchors of ground without any listed crossing cannot be the goal's ground for a zone chain, so
        /// they neither label it nor make it ambiguous (the Tuscaran door's stacked floor piece). Unchanged without area data.
        /// </summary>
        public static List<(float Distance, int Area)> Connected(List<(float Distance, int Area)> nearestFirst,
            HashSet<(ushort Zone, int Area)> nodes, ushort zoneId)
        {
            if (nearestFirst == null || nodes == null || nodes.Count == 0) return nearestFirst;
            nearestFirst.RemoveAll(entry => !nodes.Contains((zoneId, entry.Area)));
            return nearestFirst;
        }

        public const int MaxRememberedTargets = 50_000;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ushort, ushort, int, int, int), int> TargetAreas = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ushort, bool> CoveredRegions = new();

        // Measured: plans that used the filter, crossings it dropped, unfiltered retries and how many of those worked.
        private static int _filteredPlans, _excludedCrossings, _fallbacks, _fallbacksSolved;
        private static long _nextSummaryTick, _nextFallbackLogTick;

        public static void NoteFiltered(int excluded)
        {
            Interlocked.Increment(ref _filteredPlans);
            Interlocked.Add(ref _excludedCrossings, excluded);
            Summarize();
        }

        public static void NoteFallback(ushort region, ushort from, ushort to, int target, bool solved)
        {
            Interlocked.Increment(ref _fallbacks);
            if (solved) Interlocked.Increment(ref _fallbacksSolved);
            long now = GameLoop.GameLoopTime;
            long next = Interlocked.Read(ref _nextFallbackLogTick);
            if (now >= next && Interlocked.CompareExchange(ref _nextFallbackLogTick, now + 60_000, next) == next)
                Log.Info($"AUTONOMOUS_ZONE_STEP_TARGET_FALLBACK region={region} from={from} to={to} target={target} solved={solved}");
            Summarize();
        }

        private static void Summarize()
        {
            long now = GameLoop.GameLoopTime;
            long next = Interlocked.Read(ref _nextSummaryTick);
            if (now < next || Interlocked.CompareExchange(ref _nextSummaryTick, now + 5 * 60_000, next) != next) return;
            int plans = Interlocked.Exchange(ref _filteredPlans, 0), excluded = Interlocked.Exchange(ref _excludedCrossings, 0);
            int fallbacks = Interlocked.Exchange(ref _fallbacks, 0), solved = Interlocked.Exchange(ref _fallbacksSolved, 0);
            if (next == 0) return;
            Log.Info($"AUTONOMOUS_ZONE_STEP_TARGET_FILTER minutes=5 filteredPlans={plans} excludedCrossings={excluded} " +
                     $"fallbacks={fallbacks} fallbacksSolved={solved}");
        }
    }
}
