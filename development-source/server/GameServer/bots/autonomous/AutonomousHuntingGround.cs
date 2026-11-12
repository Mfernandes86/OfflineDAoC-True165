using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Veteran review M3 (2026-11-11): a solo gamebot keeps a hunting ground, as a 2003 player levelled through one zone
    /// at a time. The live logs had solo bots spending 46% of their hunting time travelling (a quarter of their goals in
    /// another region, a median same-region trip of 24,000 units, 58% of solo deaths away from the camp), so kills per hour
    /// halved. This class holds the rules; <see cref="AutonomousWorldBotController"/> applies them:
    /// <list type="bullet">
    /// <item>the camp draw strongly prefers the bot's hunting zone and its neighbours, and weakly other regions;</item>
    /// <item>at a camp whose monster is gone a solo bot hunts the area around it (anything that cons right, same filters);</item>
    /// <item>a solo bot pulls the monster standing apart first (as parties already do);</item>
    /// <item>support classes and archers prefer blue targets to yellow (the con ceiling is unchanged);</item>
    /// <item>once most kills con green the bot moves up a spot, preferring the same or a neighbouring zone;</item>
    /// <item>it binds at the stone of a town it visits anyway when that stone is much nearer its hunting ground.</item>
    /// </list>
    /// Everything here is a table lookup or a few comparisons per candidate on work that already happens; no new scans,
    /// no route requests, no special trips. No combat, XP or coin numbers change.
    /// </summary>
    public static class AutonomousHuntingGround
    {
        [ServerProperty("bots", "bot_hunting_grounds",
            "Veteran review M3 (2026-11-11): solo gamebots keep a hunting ground (same and neighbouring zones preferred), hunt the area around an empty camp, pull the monster standing apart, move up a spot when most kills con green and bind near their hunting ground on town visits.", true)]
        public static bool Enabled = true;

        // ---------------------------------------------------------------- zone preference

        public const double SameZoneFactor = 8;
        public const double NeighbourZoneFactor = 3;
        public const double SameRegionFactor = 1;
        public const double OtherRegionFactor = 0.25;

        /// <summary>
        /// Pure rule: the draw multiplier for a camp, given the bot's hunting zone (region 0 = none yet: every camp 1).
        /// </summary>
        public static double ZoneFactor(ushort homeRegion, ushort homeZone, ushort campRegion, ushort campZone, bool neighbour)
        {
            if (homeRegion == 0)
                return 1;
            if (campRegion != homeRegion)
                return OtherRegionFactor;
            if (campZone == homeZone)
                return SameZoneFactor;
            return neighbour ? NeighbourZoneFactor : SameRegionFactor;
        }

        private static readonly ConcurrentDictionary<(ushort, ushort), bool> Neighbours = new();

        /// <summary>Two zones of a region that share an edge (worked out once per pair, then a dictionary lookup).</summary>
        public static bool AreNeighbours(Zone a, Zone b)
        {
            if (a == null || b == null || a == b || a.ZoneRegion != b.ZoneRegion)
                return false;
            (ushort, ushort) key = a.ID < b.ID ? (a.ID, b.ID) : (b.ID, a.ID);
            return Neighbours.GetOrAdd(key, pair => AutonomousZoneItinerary.TryGetSharedEdge(a, b, out _));
        }

        // ---------------------------------------------------------------- per-bot state

        private sealed class State
        {
            public readonly object Gate = new();
            public ushort HomeRegion;
            public ushort HomeZone;
            public int HomeX, HomeY;
            public string CampId;
            public int KillFill;     // kills counted at this camp, up to 8
            public byte GreenBits;   // the last 8 kills, 1 = green
            public bool MoveUp;
        }

        private static readonly ConcurrentDictionary<long, State> States = new();

        public static (ushort Region, ushort Zone, int X, int Y) Home(long botId) =>
            States.TryGetValue(botId, out State state) ? (state.HomeRegion, state.HomeZone, state.HomeX, state.HomeY) : default;

        /// <summary>A solo bot chose an outdoor camp: that zone is its hunting ground, and the kill count starts over.</summary>
        public static void NoteSoloCamp(long botId, string campId, ushort region, ushort zone, int x, int y)
        {
            if (botId <= 0 || region == 0)
                return;
            State state = States.GetOrAdd(botId, _ => new State());
            lock (state.Gate)
            {
                state.HomeRegion = region;
                state.HomeZone = zone;
                state.HomeX = x;
                state.HomeY = y;
                if (!string.Equals(state.CampId, campId, StringComparison.Ordinal))
                {
                    state.CampId = campId;
                    state.KillFill = 0;
                    state.GreenBits = 0;
                    state.MoveUp = false;
                }
            }
        }

        /// <summary>The bot left its hunting ground for good (grouped up, outleveled the zone, went to RvR).</summary>
        public static void Forget(long botId) => States.TryRemove(botId, out _);

        // ---------------------------------------------------------------- moving up

        public const int KillWindow = 8;
        public const int GreenKillsToMoveUp = 6;

        /// <summary>Pure rule: the window of the last kills is full and most of them conned green.</summary>
        public static bool ShouldMoveUp(int fill, byte greenBits) =>
            fill >= KillWindow && System.Numerics.BitOperations.PopCount(greenBits) >= GreenKillsToMoveUp;

        /// <summary>Pure rule: the green-kill window after one more kill.</summary>
        public static (int Fill, byte Bits) AddKill(int fill, byte bits, bool green) =>
            (Math.Min(KillWindow, fill + 1), (byte)((bits << 1) | (green ? 1 : 0)));

        /// <summary>A solo kill (from the kill award): counts toward moving up when it is at the bot's current camp.</summary>
        public static void RecordKill(GameBot bot, GameNPC killed)
        {
            if (!Enabled || bot == null || killed == null || bot.Group != null || bot.DatabaseID <= 0 ||
                !States.TryGetValue(bot.DatabaseID, out State state))
                return;
            string camp = bot.PersistentRecord?.CurrentCampId;
            bool green = ConLevels.GetConColor(bot.GetConLevel(killed)) <= ConColor.GREEN;
            lock (state.Gate)
            {
                if (string.IsNullOrEmpty(camp) || !string.Equals(state.CampId, camp, StringComparison.Ordinal))
                    return;
                (state.KillFill, state.GreenBits) = AddKill(state.KillFill, state.GreenBits, green);
                if (ShouldMoveUp(state.KillFill, state.GreenBits))
                {
                    state.MoveUp = true;
                    state.KillFill = 0;
                    state.GreenBits = 0;
                }
            }
        }

        /// <summary>True once (then cleared) when most recent kills at the current camp conned green.</summary>
        public static bool TakeMoveUp(long botId)
        {
            if (!States.TryGetValue(botId, out State state))
                return false;
            lock (state.Gate)
            {
                if (!state.MoveUp)
                    return false;
                state.MoveUp = false;
                return true;
            }
        }

        // ---------------------------------------------------------------- area hunting

        /// <summary>How far from its camp anchor a solo bot hunts other monsters once the camp's own monster is gone.</summary>
        public const int CampAreaRadius = 2_600;

        /// <summary>Pure rule: area hunting is for solo bots outdoors, outside Darkness Falls, in True 1.65 Mode.</summary>
        public static bool AreaHuntingApplies(bool enabled, bool classic165, bool solo, bool dungeon, bool darknessFalls) =>
            enabled && classic165 && solo && !dungeon && !darknessFalls;

        public static bool WithinCampArea(int campX, int campY, int x, int y)
        {
            long dx = (long)x - campX, dy = (long)y - campY;
            return dx * dx + dy * dy <= (long)CampAreaRadius * CampAreaRadius;
        }

        // ---------------------------------------------------------------- solo pull order

        /// <summary>Candidates (in the cheap distance + con order) that get the "standing apart" check.</summary>
        public const int IsolationCandidates = 8;

        /// <summary>The extra reach of a pull path around a monster's aggro range.</summary>
        public const int PullPathMargin = 200;

        /// <summary>The social reach used for a monster that does not attack on sight (its friends still help it).</summary>
        public const int MinimumSocialRange = 300;

        /// <summary>
        /// Pure rule: how many monsters of <paramref name="pool"/> stand within each candidate's reach (its aggro range, at
        /// least <see cref="MinimumSocialRange"/>, plus <see cref="PullPathMargin"/>). A candidate does not count itself.
        /// </summary>
        public static int[] NeighbourCounts(Vector3[] candidates, int[] aggroRanges, Vector3[] pool)
        {
            int[] counts = new int[candidates.Length];
            for (int i = 0; i < candidates.Length; i++)
            {
                float reach = Math.Max(MinimumSocialRange, aggroRanges[i]) + PullPathMargin;
                float reachSquared = reach * reach;
                foreach (Vector3 other in pool)
                {
                    float d = Vector3.DistanceSquared(candidates[i], other);
                    if (d > 1 && d <= reachSquared)
                        counts[i]++;
                }
            }
            return counts;
        }

        /// <summary>Pure rule: the candidates' indexes, fewest neighbours first, keeping the given order among equals.</summary>
        public static int[] IsolationOrder(int[] neighbourCounts)
        {
            int[] order = new int[neighbourCounts.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            // insertion sort: stable and allocation-free for at most eight entries
            for (int i = 1; i < order.Length; i++)
            {
                int current = order[i];
                int j = i - 1;
                while (j >= 0 && neighbourCounts[order[j]] > neighbourCounts[current])
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = current;
            }
            return order;
        }

        /// <summary>
        /// The sorted solo candidates with the first <see cref="IsolationCandidates"/> reordered so the one standing apart
        /// from the other live monsters around (<paramref name="nearby"/>, the scan the search already made) comes first.
        /// The rest keep their order. Lazy: the expensive route checks after it still stop at the first good target.
        /// </summary>
        public static IEnumerable<GameNPC> PreferIsolated(IEnumerable<GameNPC> sorted, List<GameNPC> nearby)
        {
            using IEnumerator<GameNPC> e = sorted.GetEnumerator();
            var head = new List<GameNPC>(IsolationCandidates);
            while (head.Count < IsolationCandidates && e.MoveNext())
                head.Add(e.Current);
            if (head.Count > 1)
            {
                var pool = new List<Vector3>(nearby.Count);
                foreach (GameNPC npc in nearby)
                    if (npc != null && npc.IsAlive && npc.Realm == eRealm.None && npc.Brain != null &&
                        (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0 && npc is not GameSummonedPet)
                        pool.Add(new Vector3(npc.X, npc.Y, npc.Z));
                var positions = new Vector3[head.Count];
                var ranges = new int[head.Count];
                for (int i = 0; i < head.Count; i++)
                {
                    positions[i] = new Vector3(head[i].X, head[i].Y, head[i].Z);
                    ranges[i] = (head[i].Brain as IOldAggressiveBrain)?.AggroRange ?? 0;
                }
                int[] order = IsolationOrder(NeighbourCounts(positions, ranges, pool.ToArray()));
                foreach (int index in order)
                    yield return head[index];
            }
            else if (head.Count == 1)
                yield return head[0];
            while (e.MoveNext())
                yield return e.Current;
        }

        // ---------------------------------------------------------------- con preference by class

        /// <summary>
        /// Pure rule (Prima class sections): the con a solo bot of this class prefers among targets it may pull. Shaman
        /// ("yellow mobs will often kill you"), Healer, Cleric, Friar, Druid, Bard, Minstrel and the archers prefer blue
        /// (and green); everyone else keeps yellow first. The con ceiling itself is unchanged.
        /// </summary>
        public static ConColor PreferredSoloCon(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Shaman or eCharacterClass.Healer or eCharacterClass.Cleric or eCharacterClass.Friar or
                eCharacterClass.Druid or eCharacterClass.Bard or eCharacterClass.Minstrel or
                eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger => ConColor.BLUE,
            _ => ConColor.YELLOW,
        };

        /// <summary>Pure rule: the con part of the target sort (220 units of walking per con step from the preferred con).</summary>
        public static int ConSortPenalty(ConColor con, ConColor preferred) => Math.Abs((int)con - (int)preferred) * 220;

        // ---------------------------------------------------------------- binding near the hunting ground

        /// <summary>Only a bindstone this close to the bot counts as "the stone of the town it is visiting".</summary>
        public const int TownStoneRadius = 3_000;

        /// <summary>A stone farther than this from the hunting ground is no help.</summary>
        public const int MaximumStoneToGround = 40_000;

        /// <summary>
        /// Pure rule: bind at the visited town's stone when it is much nearer the hunting ground than the current bind: at
        /// least 5,000 units nearer and at most 60% of the old distance (a bind in another region counts as infinitely far).
        /// Never flip-flops between two similar stones.
        /// </summary>
        public static bool ShouldRebind(double botToStone, double stoneToGround, double bindToGround)
        {
            if (botToStone > TownStoneRadius || stoneToGround > MaximumStoneToGround)
                return false;
            if (double.IsInfinity(bindToGround))
                return true;
            return bindToGround - stoneToGround >= 5_000 && stoneToGround <= bindToGround * 0.6;
        }
    }
}
