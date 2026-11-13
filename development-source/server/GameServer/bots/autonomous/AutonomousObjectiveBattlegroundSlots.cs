using System;
using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Battleground PvP-focus slots (owner 2026-11-12, battleground fix pass): the launcher's Battlegrounds % is the share
    /// of the bots that CAN enter a battleground (levels 15-35) that get the PvP focus: roaming the field, sieging and
    /// holding the central keep. Grinding battleground monsters is open to every bot of the bracket and needs no slot.
    /// - The share is taken of the eligible bots, not the whole level band (BG253 F1: levels 15-19 are about a quarter of
    ///   band 1-19, so 10% became about 38% of them; band 20-49 about 1.9x).
    /// - The slots are spread over the brackets: a thin bracket gets <see cref="ThinBracketFloor"/> first when it has that
    ///   many bots (so it can still reach the 8-bot assault wave), the rest follows the bracket sizes, and no realm puts
    ///   more than <see cref="CapPerRealm"/> PvP bots into one battleground (BG252: about 450 bots in Thidranki at 1,000 a
    ///   realm). Slots a bracket cannot use go back to PvE.
    /// - Every way a bot can be given the Battleground goal (the one-minute allocator and the task-boundary rolls) takes a
    ///   slot here, so the cap holds; the table is rebuilt by the allocator's one-minute pass (no per-think work).
    /// </summary>
    public static class AutonomousBattlegroundSlots
    {
        [ServerProperty("bots", "bot_battleground_pvp_cap",
            "Most bots of one realm with the Battlegrounds goal (PvP focus) in one battleground at a time; the rest of the Battlegrounds % share does PvE. 0 = no cap.", 60)]
        public static int CapPerRealm = 60;

        [ServerProperty("bots", "bot_battleground_grind",
            "May any gamebot whose level fits a battleground's bracket choose that battleground's monsters as a hunting spot, whatever its goal? (The launcher's Battlegrounds % is the PvP focus.)", true)]
        public static bool GrindEnabled = true;

        /// <summary>A bracket with at least this many eligible bots gets this many slots first: the assault wave needs
        /// <see cref="AutonomousBattlegroundWave.MinimumWave"/> inside, and some are always dead or on the way.</summary>
        public const int ThinBracketFloor = AutonomousBattlegroundWave.MinimumWave + 2;

        /// <summary>Pure: the PvP slots for this many eligible bots at this Battlegrounds % (rounded, never more than eligible).</summary>
        public static int Slots(int eligible, int percent)
        {
            if (eligible <= 0 || percent <= 0) return 0;
            return Math.Min(eligible, (int)Math.Round(eligible * Math.Min(100, percent) / 100d, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// Pure: spreads <paramref name="total"/> slots over brackets with these eligible counts. First every bracket with at
        /// least <paramref name="floor"/> eligible gets the floor (thinnest first, while slots last), then the rest goes in
        /// proportion to the eligible counts (largest remainder). No bracket gets more than its eligible bots or
        /// <paramref name="cap"/> (0 = no cap). The sum can be below <paramref name="total"/>: that surplus is PvE.
        /// </summary>
        public static int[] Spread(IReadOnlyList<int> eligible, int total, int floor, int cap)
        {
            int count = eligible?.Count ?? 0;
            var slots = new int[count];
            if (count == 0 || total <= 0) return slots;
            int Room(int index) => Math.Max(0, Math.Min(Math.Max(0, eligible[index]), cap > 0 ? cap : int.MaxValue) - slots[index]);
            int remaining = total;

            var order = new List<int>(count);
            for (int index = 0; index < count; index++)
                if (eligible[index] >= floor && floor > 0) order.Add(index);
            order.Sort((a, b) => eligible[a] != eligible[b] ? eligible[a].CompareTo(eligible[b]) : a.CompareTo(b));
            foreach (int index in order)
            {
                int give = Math.Min(Math.Min(floor, Room(index)), remaining);
                slots[index] += give;
                remaining -= give;
                if (remaining == 0) return slots;
            }

            // Proportional rounds: brackets that fill up drop out and their share goes to the others.
            while (remaining > 0)
            {
                long weight = 0;
                for (int index = 0; index < count; index++)
                    if (Room(index) > 0) weight += Math.Max(0, eligible[index]);
                if (weight == 0) break;
                int handed = 0;
                var fractions = new List<(int Index, double Fraction)>(count);
                int pool = remaining;
                for (int index = 0; index < count; index++)
                {
                    int room = Room(index);
                    if (room == 0) continue;
                    double exact = pool * (double)Math.Max(0, eligible[index]) / weight;
                    int give = Math.Min(room, (int)Math.Floor(exact));
                    slots[index] += give;
                    handed += give;
                    if (give < room) fractions.Add((index, exact - Math.Floor(exact)));
                }
                remaining -= handed;
                fractions.Sort((a, b) => a.Fraction != b.Fraction ? b.Fraction.CompareTo(a.Fraction) : a.Index.CompareTo(b.Index));
                foreach (var (index, _) in fractions)
                {
                    if (remaining == 0) break;
                    if (Room(index) == 0) continue;
                    slots[index]++;
                    remaining--;
                    handed++;
                }
                if (handed == 0) break;
            }
            return slots;
        }

        /// <summary>
        /// Pure: the battleground whose monsters a bot of this level may hunt (SelectCamp adds its region), or null. Owner
        /// 2026-11-12: it depends on the level only, never on the bot's goal or the launcher's Battlegrounds % (that is the
        /// PvP focus); a way in must exist (the portal keep teleporter in True 1.65, the town teleporters in normal mode),
        /// the switch must be on, and the bot must not have been taken out for making no progress lately.
        /// </summary>
        public static BattlegroundBrackets.Bracket GrindBracket(int level, bool wayIn, bool grindEnabled, bool reentryBlocked) =>
            wayIn && grindEnabled && !reentryBlocked ? BattlegroundBrackets.ForLevel(level) : null;

        /// <summary>The bracket index (0 Abermenai .. 3 Caledonia) for a level, or -1.</summary>
        public static int BracketIndex(int level)
        {
            for (int index = 0; index < BattlegroundBrackets.Count; index++)
                if (BattlegroundBrackets.Allows(BattlegroundBrackets.Get(index), level)) return index;
            return -1;
        }

        private static readonly object Sync = new();
        private static readonly Dictionary<(eRealm Realm, int Bracket), (int Target, int Taken)> Table = new();

        /// <summary>The allocator's one-minute pass: this realm's slots in one bracket and the PvP tours already running there.</summary>
        public static void Publish(eRealm realm, int bracket, int target, int taken)
        {
            lock (Sync) Table[(realm, bracket)] = (Math.Max(0, target), Math.Max(0, taken));
        }

        /// <summary>Slots still free for a bot of this realm and level (int.MaxValue before the first pass).</summary>
        public static int Free(eRealm realm, int level)
        {
            int bracket = BracketIndex(level);
            if (bracket < 0) return 0;
            lock (Sync)
                return Table.TryGetValue((realm, bracket), out var row) ? Math.Max(0, row.Target - row.Taken) : int.MaxValue;
        }

        /// <summary>Takes a slot for a new Battleground goal; false when the bracket is full for the realm (the caller
        /// gives a PvE goal instead). Before the first pass only the cap applies, which the pass then enforces.</summary>
        public static bool TryTake(eRealm realm, int level)
        {
            int bracket = BracketIndex(level);
            if (bracket < 0) return false;
            lock (Sync)
            {
                if (!Table.TryGetValue((realm, bracket), out var row)) return true;
                if (row.Taken >= row.Target) return false;
                Table[(realm, bracket)] = (row.Target, row.Taken + 1);
                return true;
            }
        }

        public static void Clear()
        {
            lock (Sync) Table.Clear();
        }
    }
}
