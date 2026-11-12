using System;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        // One planning view per NPC phase, not one global population regrouping
        // per warband. No combat target, inventory, purchase, or route is cached.
        private sealed record RvrPlanningView(GameBot[] Candidates, Dictionary<ushort, int[]> Population, long BuiltTick)
        {
            public (int Allies, int Enemies) Count(ushort region, eRealm realm)
            {
                if (!Population.TryGetValue(region, out int[] counts)) return default;
                int allies = (int)realm is >= 0 and < 4 ? counts[(int)realm] : 0;
                return (allies, counts[4] - allies);
            }
        }
        private static readonly Lock RvrPlanningLock = new();
        private static RvrPlanningView _rvrPlanningView;

        /// <summary>How long one planning view is shared (veteran review Q30: it was rebuilt with a full roster walk on
        /// every tick it was used, up to 30 times a second). Users re-check every candidate live (alive, realm, frontier)
        /// and read live positions, so a view up to 0.75 s old only delays who is counted, never what is attacked.</summary>
        public const int RvrPlanningViewMilliseconds = 750;

        public static void PrepareRvrPlanningTick()
        {
            RvrPlanningView view = Volatile.Read(ref _rvrPlanningView);
            if (view != null && !RvrPlanningViewExpired(view.BuiltTick, GameLoop.GameLoopTime))
                return;
            Volatile.Write(ref _rvrPlanningView, null);
        }

        public static bool RvrPlanningViewExpired(long builtTick, long now) =>
            now < builtTick || now - builtTick >= RvrPlanningViewMilliseconds;

        private static void ResetRvrPlanningView() => Volatile.Write(ref _rvrPlanningView, null);

        private static RvrPlanningView GetRvrPlanningView()
        {
            RvrPlanningView view = Volatile.Read(ref _rvrPlanningView);
            if (view != null) return view;
            lock (RvrPlanningLock)
            {
                if (_rvrPlanningView != null) return _rvrPlanningView;
                var population = new Dictionary<ushort, int[]>();
                var candidates = new List<GameBot>();
                foreach (GameBot actor in AutonomousBotRegistry.Snapshot())
                {
                    if (!actor.IsAlive) continue;
                    if (!population.TryGetValue(actor.CurrentRegionID, out int[] counts))
                        population[actor.CurrentRegionID] = counts = new int[5];
                    counts[4]++;
                    if ((int)actor.Realm is >= 0 and < 4) counts[(int)actor.Realm]++;
                    if (actor.Realm != eRealm.None && AutonomousObjectiveAssignments.Is(actor, eAutonomousObjectiveKind.RvR) && IsInFrontier(actor))
                        candidates.Add(actor);
                }
                view = new(candidates.ToArray(), population, GameLoop.GameLoopTime);
                Volatile.Write(ref _rvrPlanningView, view);
                return view;
            }
        }
    }
}
