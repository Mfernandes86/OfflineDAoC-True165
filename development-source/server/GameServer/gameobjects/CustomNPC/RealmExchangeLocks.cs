using System;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// Non-blocking entry to the Realm Exchange locks (2026-11-11 exchange plan, phase 0). TryList, TryBuy, a player's buy, the
/// expiry sweep and the market pass took RealmExchangeBroker.TransactionLock, the bot money lock and
/// AutonomousBotStatusPersistence.DatabaseWriteLock with a blocking lock and then wrote to SQLite on the game loop: 60
/// long NpcService ticks of 100-4,328 ms fell on the very tick a bot listed an item, waiting behind the 1 s bot-save batch.
/// Now the locks are tried, always in the same order (transaction, money, database), as the archive worker already does
/// (WorldArchiveHooks.TryLocked). A bot that finds them busy tries again at its next broker check (3-7 s later); a busy
/// result is not a fruitless check. A player's buy waits up to 50 ms, then is told the exchange is busy.
/// </summary>
public static class RealmExchangeLocks
{
    /// <summary>How long a player's buy may wait for the exchange before "The exchange is busy".</summary>
    public const int PlayerWaitMilliseconds = 50;

    /// <summary>The locks held by a successful <see cref="TryEnter"/>; dispose to release them.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly bool _transaction, _money, _write;

        internal Scope(bool transaction, bool money, bool write)
        {
            _transaction = transaction;
            _money = money;
            _write = write;
        }

        public void Dispose()
        {
            if (_write)
                Monitor.Exit(AutonomousBotStatusPersistence.DatabaseWriteLock);
            if (_money)
                Monitor.Exit(typeof(AutonomousBotEconomy));
            if (_transaction)
                RealmExchangeBroker.TransactionLock.Exit();
        }
    }

    /// <summary>
    /// Takes the transaction lock, the bot money lock when <paramref name="money"/> is set, and the database write lock,
    /// waiting at most <paramref name="timeoutMilliseconds"/> in all (0: never wait). False when any is busy; nothing is
    /// held then, and the busy count of the hourly exchange line goes up. The locks are re-entrant, so a caller that
    /// already holds them gets them at once.
    /// </summary>
    public static bool TryEnter(int timeoutMilliseconds, bool money, out Scope scope)
    {
        scope = default;
        long deadline = Environment.TickCount64 + Math.Max(0, timeoutMilliseconds);
        if (!RealmExchangeBroker.TransactionLock.TryEnter(Math.Max(0, timeoutMilliseconds)))
        {
            RealmExchangeStats.NoteLockBusy();
            return false;
        }
        bool economy = false, write = false;
        try
        {
            if (money)
            {
                Monitor.TryEnter(typeof(AutonomousBotEconomy), Remaining(deadline), ref economy);
                if (!economy)
                    return Busy();
            }
            Monitor.TryEnter(AutonomousBotStatusPersistence.DatabaseWriteLock, Remaining(deadline), ref write);
            if (!write)
                return Busy();
            scope = new Scope(true, economy, true);
            return true;
        }
        catch
        {
            if (write)
                Monitor.Exit(AutonomousBotStatusPersistence.DatabaseWriteLock);
            write = false;
            Release();
            throw;
        }

        bool Busy()
        {
            Release();
            RealmExchangeStats.NoteLockBusy();
            return false;
        }

        void Release()
        {
            if (economy)
                Monitor.Exit(typeof(AutonomousBotEconomy));
            economy = false;
            RealmExchangeBroker.TransactionLock.Exit();
        }
    }

    private static int Remaining(long deadline) => (int)Math.Clamp(deadline - Environment.TickCount64, 0, int.MaxValue);
}
