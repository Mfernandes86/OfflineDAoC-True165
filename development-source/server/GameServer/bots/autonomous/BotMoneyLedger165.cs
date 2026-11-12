using System;
using System.Reflection;
using System.Text;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// BOT_MONEY_LEDGER_165 (veteran-bots review 2026-11-11, M9): where the gamebots' coin comes from and where it goes.
    /// The review found bots broke (median 1.97 gold at levels 15-29) and no way to see why. Every credit and debit of a
    /// bot's coin (AutonomousBotEconomy.AddMoney / TrySpend) is added here, by kind when the caller names one, and one
    /// line per hour sums them: "in_*" and "out_*" per kind plus the totals, so a run shows the real sinks before anyone
    /// thinks of touching an amount (owner rule: coin amounts unchanged). Cost: two Interlocked adds per coin move; the
    /// line is written by the first move after the hour ends, so nothing runs while nothing happens.
    /// </summary>
    public static class BotMoneyLedger165
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const string Tag = "BOT_MONEY_LEDGER_165";
        public const long WindowMilliseconds = 60 * 60_000;

        /// <summary>What a coin move was for. Other = a caller that names no kind (exchange sales, refunds of those...).</summary>
        public enum Kind
        {
            Other = 0,
            LootCoin,
            VendorSale,
            VendorGear,
            ExchangeSale,
            Repair,
            Healer,
            CraftMaterials,
            CraftSale,
            CraftOrder,
            Enchant,
            Dye,
            Ammo,
            Poison,
            Potion,
            Travel,
            Siege,
            Guild,
            /// <summary>The O5 kill-task reward (BotKillTasks165), a coin source of its own.</summary>
            KillTask,
        }

        private static readonly int KindCount = Enum.GetValues<Kind>().Length;
        private static readonly long[] In = new long[KindCount];
        private static readonly long[] Out = new long[KindCount];
        private static readonly long[] InCount = new long[KindCount];
        private static readonly long[] OutCount = new long[KindCount];
        private static long _windowStart = -1;

        /// <summary>Milliseconds clock (replaceable by tests).</summary>
        public static Func<long> Clock { get; set; } = () => Environment.TickCount64;

        /// <summary>Where the summary line goes (replaceable by tests).</summary>
        public static Action<string> Sink { get; set; } = line => Log.Info(line);

        /// <summary>Coin a bot received.</summary>
        public static void Credit(Kind kind, long copper) => Note(In, InCount, kind, copper);

        /// <summary>Coin a bot paid.</summary>
        public static void Debit(Kind kind, long copper) => Note(Out, OutCount, kind, copper);

        private static void Note(long[] sums, long[] counts, Kind kind, long copper)
        {
            if (copper <= 0)
                return;
            try
            {
                int index = (int)kind;
                if ((uint)index >= (uint)KindCount)
                    index = 0;
                Interlocked.Add(ref sums[index], copper);
                Interlocked.Increment(ref counts[index]);
                long now = Clock();
                long start = Interlocked.Read(ref _windowStart);
                if (start < 0)
                {
                    Interlocked.CompareExchange(ref _windowStart, now, start);
                    return;
                }
                if (now - start >= WindowMilliseconds && Interlocked.CompareExchange(ref _windowStart, now, start) == start)
                    Flush(now - start);
            }
            catch { } // diagnostics never break a coin move
        }

        /// <summary>Writes the summary since the last line and resets it; null when nothing moved.</summary>
        public static string Flush(long elapsedMilliseconds)
        {
            var line = new StringBuilder(Tag).Append(" window=").Append(Math.Max(0, elapsedMilliseconds / 1000)).Append('s');
            long totalIn = 0, totalOut = 0;
            var parts = new StringBuilder();
            for (int i = 0; i < KindCount; i++)
            {
                long credit = Interlocked.Exchange(ref In[i], 0);
                long debit = Interlocked.Exchange(ref Out[i], 0);
                long credits = Interlocked.Exchange(ref InCount[i], 0);
                long debits = Interlocked.Exchange(ref OutCount[i], 0);
                string name = ((Kind)i).ToString().ToLowerInvariant();
                if (credit > 0)
                    parts.Append(" in_").Append(name).Append('=').Append(credit).Append('/').Append(credits);
                if (debit > 0)
                    parts.Append(" out_").Append(name).Append('=').Append(debit).Append('/').Append(debits);
                totalIn += credit;
                totalOut += debit;
            }
            if (totalIn == 0 && totalOut == 0)
                return null;
            line.Append(" in_total=").Append(totalIn).Append(" out_total=").Append(totalOut)
                .Append(" net=").Append(totalIn - totalOut).Append(parts);
            string text = line.ToString();
            Sink?.Invoke(text);
            return text;
        }

        /// <summary>Tests: clears the sums and the window.</summary>
        public static void Reset()
        {
            for (int i = 0; i < KindCount; i++)
            {
                Interlocked.Exchange(ref In[i], 0);
                Interlocked.Exchange(ref Out[i], 0);
                Interlocked.Exchange(ref InCount[i], 0);
                Interlocked.Exchange(ref OutCount[i], 0);
            }
            Interlocked.Exchange(ref _windowStart, -1);
        }
    }
}
