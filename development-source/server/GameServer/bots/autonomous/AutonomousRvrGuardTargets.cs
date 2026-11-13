using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>A small time-to-live cache: one build per key per TTL, shared by every caller (thread-safe, last build wins).</summary>
    public sealed class AutonomousTtlCache<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, (long Until, TValue Value)> _entries = new();
        private readonly long _ttlMilliseconds;

        public AutonomousTtlCache(long ttlMilliseconds) => _ttlMilliseconds = ttlMilliseconds;

        public TValue Get(TKey key, long now, Func<TKey, TValue> build)
        {
            if (_entries.TryGetValue(key, out var entry) && now < entry.Until)
                return entry.Value;
            TValue value = build(key);
            if (_entries.Count > 4_096) _entries.Clear();
            _entries[key] = (now + _ttlMilliseconds, value);
            return value;
        }

        public void Clear() => _entries.Clear();
    }

    /// <summary>
    /// Battlegrounds fix 2026-11-12 (BG252 scale: "FindRvrTarget runs GetNPCsInRadius plus a navmesh line-of-sight raycast
    /// and a corridor check per keep guard for every assaulter, uncached"; now also beside 124 portal keep guards). The
    /// keep assault's guard targets, three layers cheaper with the same choice rules:
    /// - the candidate guards of a keep for one attacking realm come from the keep's own guard list (no 5,000-unit area
    ///   scan of every NPC, the portal keep guards and monsters included), built once a second per keep and realm and
    ///   shared by every bot of that realm (with the keep's "still sealed" flag);
    /// - a bot's checked target list (line of sight + corridor) is held for <see cref="BotHoldMilliseconds"/> while it
    ///   stays near where it was built, re-checking only that each guard is alive and attackable;
    /// - a rebuild raycasts the lord (when the keep is open) and at most <see cref="MaxLineChecks"/> nearest other guards.
    /// </summary>
    public static class AutonomousRvrGuardTargets
    {
        public const long CandidateTtlMilliseconds = 1_000;
        public const long BotHoldMilliseconds = 1_500;
        public const int HoldMoveDistance = 300;
        public const int MaxLineChecks = 8;

        public sealed record KeepCandidates(bool Sealed, GameKeepGuard[] Guards);

        private static readonly AutonomousTtlCache<(int Keep, eRealm Attacker), KeepCandidates> Cache = new(CandidateTtlMilliseconds);

        /// <summary>The keep's living enemy guards a raid of this realm may attack, shared for one second.</summary>
        public static KeepCandidates Candidates(AbstractGameKeep keep, eRealm attacker, long now) =>
            keep == null ? new(false, []) : Cache.Get((keep.KeepID, attacker), now, _ => Build(keep, attacker));

        private static KeepCandidates Build(AbstractGameKeep keep, eRealm attacker)
        {
            GameKeepGuard[] all;
            // keep.Guards is a plain Dictionary that GameKeepGuard.LoadFromDatabase writes under lock (keep.Guards).
            try { lock (keep.Guards) all = keep.Guards.Values.ToArray(); }
            catch (InvalidOperationException) { all = []; } // the keep changed its list mid-copy; the next second rebuilds
            bool battleground = BattlegroundBrackets.IsBattlegroundRegion(keep.Region);
            bool objective = AutonomousRvrKeepPolicy.IsSiegeObjective(keep);
            GameKeepGuard[] guards = objective
                ? all.Where(guard => guard != null && guard.IsAlive && guard.ObjectState == GameObject.eObjectState.Active &&
                                     guard.Realm != attacker && (guard.Realm != eRealm.None || battleground) &&
                                     !guard.IsPortalKeepGuard).ToArray()
                : [];
            return new(KeepRaidDoors.Sealed(keep), guards);
        }

        /// <summary>Pure: a bot's held target list is still good (same keep, inside its hold, not moved far).</summary>
        public static bool HoldValid(long now, long holdUntil, int keepId, int heldKeepId, float movedSquared) =>
            now < holdUntil && keepId == heldKeepId && movedSquared <= (float)HoldMoveDistance * HoldMoveDistance;

        /// <summary>
        /// Pure: which candidates get the line-of-sight and corridor checks, as indices into the candidate arrays: every
        /// lord, then the <paramref name="limit"/> nearest others, nearest first.
        /// </summary>
        public static int[] Shortlist(IReadOnlyList<float> distances, IReadOnlyList<bool> lords, int limit)
        {
            var result = new List<int>();
            for (int index = 0; index < distances.Count; index++)
                if (lords[index]) result.Add(index);
            result.AddRange(Enumerable.Range(0, distances.Count).Where(index => !lords[index])
                .OrderBy(index => distances[index]).ThenBy(index => index).Take(Math.Max(0, limit)));
            return result.ToArray();
        }

        public static void Clear() => Cache.Clear();
    }
}
