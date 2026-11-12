using System;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// Hourly Realm Exchange counters (2026-11-11 exchange plan, phase 0): the server logged no listing, sale, expiry or
/// return, so turnover could only be measured from DB snapshots. A few Interlocked counters per realm, written as one
/// EXCHANGE_HOURLY line per realm an hour by the expiry timer. No names, no per-tick logging.
/// </summary>
public static class RealmExchangeStats
{
    public enum Counter
    {
        Listed, Sold, Relisted, Undercut, Returned, Credited, Swapped, Bumped, Bought, Vendored,
    }

    private static readonly int CounterCount = Enum.GetValues<Counter>().Length;
    // [realm 0-3][counter]; realm 0 collects anything without a player realm
    private static readonly long[][] Counts = [new long[CounterCount], new long[CounterCount], new long[CounterCount], new long[CounterCount]];
    private static long _lockBusy, _scans, _scanTicks, _sweeps, _sweepTicks, _passes, _passTicks;
    private static long _nextHourlyUtcTicks;

    public static void Note(eRealm realm, Counter counter, long amount = 1) =>
        Interlocked.Add(ref Counts[RealmIndex(realm)][(int)counter], amount);

    public static void NoteLockBusy() => Interlocked.Increment(ref _lockBusy);

    /// <summary>A bot's purchase appraisal (a wish-list probe at the broker or a window-shopping check).</summary>
    public static void NoteScan(long stopwatchTicks)
    {
        Interlocked.Increment(ref _scans);
        Interlocked.Add(ref _scanTicks, stopwatchTicks);
    }

    public static void NoteSweep(long stopwatchTicks)
    {
        Interlocked.Increment(ref _sweeps);
        Interlocked.Add(ref _sweepTicks, stopwatchTicks);
    }

    public static void NotePass(long stopwatchTicks)
    {
        Interlocked.Increment(ref _passes);
        Interlocked.Add(ref _passTicks, stopwatchTicks);
    }

    public static long Read(eRealm realm, Counter counter) => Interlocked.Read(ref Counts[RealmIndex(realm)][(int)counter]);

    public static long LockBusy => Interlocked.Read(ref _lockBusy);

    private static int RealmIndex(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia ? (int)realm : 0;

    /// <summary>Called by the expiry timer: once an hour, one line per realm, then the counters start over.</summary>
    public static void LogHourlyIfDue(DateTime utcNow)
    {
        long now = utcNow.Ticks;
        long due = Interlocked.Read(ref _nextHourlyUtcTicks);
        if (due == 0)
        {
            Interlocked.CompareExchange(ref _nextHourlyUtcTicks, now + TimeSpan.TicksPerHour, 0);
            return;
        }
        if (now < due || Interlocked.CompareExchange(ref _nextHourlyUtcTicks, now + TimeSpan.TicksPerHour, due) != due)
            return;
        WriteHourly();
    }

    public static void WriteHourly()
    {
        var log = Logging.LoggerManager.Create(typeof(RealmExchangeStats));
        long busy = Interlocked.Exchange(ref _lockBusy, 0);
        long scans = Interlocked.Exchange(ref _scans, 0), scanTicks = Interlocked.Exchange(ref _scanTicks, 0);
        long sweeps = Interlocked.Exchange(ref _sweeps, 0), sweepTicks = Interlocked.Exchange(ref _sweepTicks, 0);
        long passes = Interlocked.Exchange(ref _passes, 0), passTicks = Interlocked.Exchange(ref _passTicks, 0);
        bool bookOk = MarketCache.CheckBook(out string bookNote);
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            long[] c = Counts[(int)realm];
            long Take(Counter counter) => Interlocked.Exchange(ref c[(int)counter], 0);
            MarketBook.RealmSummary stock = MarketCache.Summary(realm);
            try
            {
                log.Info($"EXCHANGE_HOURLY realm={(int)realm} listed={Take(Counter.Listed)} sold={Take(Counter.Sold)} " +
                         $"bought={Take(Counter.Bought)} relisted={Take(Counter.Relisted)} undercut={Take(Counter.Undercut)} " +
                         $"returned={Take(Counter.Returned)} credited={Take(Counter.Credited)} swapped={Take(Counter.Swapped)} " +
                         $"bumped={Take(Counter.Bumped)} vendored={Take(Counter.Vendored)} stock={stock.BotListings + stock.PlayerListings} " +
                         $"botStock={stock.BotListings} playerStock={stock.PlayerListings} distinct={stock.DistinctNames} " +
                         $"sellers={stock.Sellers} lockBusy={busy} scans={scans} scanAvgUs={Micros(scanTicks, scans)} " +
                         $"sweepAvgUs={Micros(sweepTicks, sweeps)} passAvgUs={Micros(passTicks, passes)} " +
                         $"book={(bookOk ? "ok" : "drift")}{(bookOk ? string.Empty : " note=\"" + bookNote + "\"")}");
            }
            catch { } // a diagnostics failure never touches the exchange
        }
    }

    private static long Micros(long stopwatchTicks, long count) =>
        count <= 0 ? 0 : stopwatchTicks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency / count;

    /// <summary>Tests only.</summary>
    public static void Reset()
    {
        foreach (long[] realm in Counts)
            Array.Clear(realm);
        Interlocked.Exchange(ref _lockBusy, 0);
        Interlocked.Exchange(ref _scans, 0);
        Interlocked.Exchange(ref _scanTicks, 0);
    }
}
