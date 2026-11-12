using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DOL.Database;
using DOL.GS.Movement;

namespace DOL.GS.Quests
{
    /// <summary>
    /// A quest step whose target walks a patrol shows its route on the map (owner 2026-11-11: "for patrol routes you
    /// should show a red line indicating the mob patrols that area"). The route is sent as map marker points with ids of
    /// their own - <see cref="RouteMarkers"/> + quest slot x 64 + point index - next to the step's ordinary marker; the
    /// new client joins a quest's points into one red line (it draws no medallions for them).
    /// A route is the patrol path of the NPC named by the step that stands nearest the step's marker, thinned to at most
    /// <see cref="MaxPoints"/> points; worked out once per NPC name and region. Sent only when a step or its route
    /// changes: nothing per tick.
    /// </summary>
    public static class QuestPatrolRoutes
    {
        /// <summary>First id of the route points (below the quest giver markers 0xFFFC0000; 2,048 quests x 64 points).</summary>
        public const uint RouteMarkers = 0xFFFA0000;
        public const int PointsPerQuest = 64;
        /// <summary>Most points of one route (a long loop is thinned to this many, its turns kept).</summary>
        public const int MaxPoints = 48;
        /// <summary>The patrolling NPC must stand this near the step's marker (a namesake elsewhere is not it).</summary>
        public const int MarkerReach = 60_000;

        private sealed record Shown(int Step, string Key, int Count);

        private static readonly ConcurrentDictionary<GamePlayer, ConcurrentDictionary<int, Shown>> ShownRoutes = new();
        private static readonly ConcurrentDictionary<(ushort Region, string Name), (string Key, ClassicQuests.Point[] Points)> Routes = new();

        /// <summary>The id of point <paramref name="index"/> of a quest's route.</summary>
        public static uint PointId(int questId, int index) =>
            RouteMarkers + (uint)(Slot(questId) * PointsPerQuest + Math.Clamp(index, 0, PointsPerQuest - 1));

        /// <summary>Pure: a quest's slot among the route ids (classic quests are 20000 and up).</summary>
        public static int Slot(int questId) => Math.Abs(questId - 20000) % 2048;

        /// <summary>
        /// Pure: at most <paramref name="max"/> points of a route, evenly taken along it, always with its first and last.
        /// </summary>
        public static List<T> Thin<T>(IReadOnlyList<T> points, int max)
        {
            var result = new List<T>();
            if (points == null || points.Count == 0 || max <= 0)
                return result;
            if (max == 1)
            {
                result.Add(points[0]);
                return result;
            }
            if (points.Count <= max)
            {
                result.AddRange(points);
                return result;
            }
            for (int i = 0; i < max; i++)
                result.Add(points[(int)Math.Round(i * (points.Count - 1) / (double)(max - 1))]);
            return result;
        }

        /// <summary>
        /// Shows (or takes away) the patrol route of the quest's current step for this player. Called with the step's
        /// marker each time the quest markers are refreshed; sends packets only when something changed.
        /// </summary>
        public static void Show(GamePlayer player, DataQuest quest, ClassicQuests.Point marker)
        {
            if (player == null || quest == null)
                return;
            var onMap = ShownRoutes.GetOrAdd(player, _ => new ConcurrentDictionary<int, Shown>());
            string key = string.Empty;
            ClassicQuests.Point[] points = [];
            if (marker != null)
            {
                string name = SafeTargetName(quest);
                if (!string.IsNullOrEmpty(name))
                    (key, points) = RouteOf(marker, name);
            }
            onMap.TryGetValue(quest.ID, out Shown shown);
            if (points.Length == 0)
            {
                if (shown != null)
                {
                    Remove(player, quest.ID, shown.Count);
                    onMap.TryRemove(quest.ID, out _);
                }
                return;
            }
            if (shown != null && shown.Step == quest.Step && shown.Key == key && shown.Count == points.Length)
                return;
            if (shown != null)
                Remove(player, quest.ID, shown.Count);
            for (int i = 0; i < points.Length; i++)
                player.Out.SendMinotaurRelicMapUpdate(PointId(quest.ID, i), points[i].Region, points[i].X, points[i].Y, points[i].Z);
            onMap[quest.ID] = new Shown(quest.Step, key, points.Length);
        }

        /// <summary>A quest left the player's journal (finished, given up): its route goes.</summary>
        public static void Hide(GamePlayer player, int questId)
        {
            if (player != null && ShownRoutes.TryGetValue(player, out var onMap) && onMap.TryRemove(questId, out Shown shown))
                Remove(player, questId, shown.Count);
        }

        /// <summary>The quests of this player that show a route now.</summary>
        public static ICollection<int> ShownQuests(GamePlayer player) =>
            player != null && ShownRoutes.TryGetValue(player, out var onMap) ? onMap.Keys : Array.Empty<int>();

        /// <summary>The player left: forget what was shown (the client drops its markers with the session).</summary>
        public static void Forget(GamePlayer player)
        {
            if (player != null)
                ShownRoutes.TryRemove(player, out _);
        }

        private static void Remove(GamePlayer player, int questId, int count)
        {
            for (int i = 0; i < count; i++)
                player.Out.SendMinotaurRelicMapRemove(PointId(questId, i));
        }

        private static string SafeTargetName(DataQuest quest)
        {
            try
            {
                return quest.TargetName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The patrol route of the NPC of this name nearest the marker (empty: it walks no path).</summary>
        private static (string Key, ClassicQuests.Point[] Points) RouteOf(ClassicQuests.Point marker, string name)
        {
            var cacheKey = (marker.Region, name.ToLowerInvariant());
            if (Routes.TryGetValue(cacheKey, out var cached))
                return cached;
            GameNPC walker = null;
            long best = long.MaxValue;
            foreach (eRealm realm in new[] { eRealm.None, eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                foreach (GameNPC npc in WorldMgr.GetNPCsByNameFromRegion(name, marker.Region, realm))
                {
                    if (npc == null || string.IsNullOrEmpty(npc.PathID))
                        continue;
                    long dx = npc.SpawnPoint.X - marker.X, dy = npc.SpawnPoint.Y - marker.Y;
                    long distance = dx * dx + dy * dy;
                    if (distance < best && distance <= (long)MarkerReach * MarkerReach)
                    {
                        best = distance;
                        walker = npc;
                    }
                }
            }
            (string, ClassicQuests.Point[]) route = (string.Empty, Array.Empty<ClassicQuests.Point>());
            if (walker != null)
            {
                var all = new List<ClassicQuests.Point>();
                PathPoint first = MovementMgr.LoadPath(walker.PathID);
                int guard = 0;
                for (PathPoint point = first; point != null && guard < 2_000; point = point.Next, guard++)
                {
                    all.Add(new ClassicQuests.Point(marker.Region, point.X, point.Y, point.Z));
                    if (point.Next == first)
                        break;
                }
                // a loop is drawn closed: its first point again at the end
                if (all.Count >= 3 && first.Type == EPathType.Loop)
                    all.Add(all[0]);
                if (all.Count >= 2)
                    route = (walker.PathID, Thin(all, MaxPoints).ToArray());
            }
            if (Routes.Count > 4_096)
                Routes.Clear();
            // no NPC of the name up right now (an event spawn, a night spawn): ask again next time instead of remembering "none"
            if (walker != null || WorldMgr.GetNPCsByNameFromRegion(name, marker.Region, eRealm.None).Length > 0)
                Routes[cacheKey] = route;
            return route;
        }

        /// <summary>The spawns or paths changed (a content refresh): routes are worked out again.</summary>
        public static void Reset() => Routes.Clear();
    }
}
