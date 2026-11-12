using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// Coalesced Realm Exchange writes (2026-11-11 exchange plan, section 2.6, the optional phase 3). Reprices, relists and
/// relist bookkeeping are single-row, idempotent changes: they are applied in memory at once and the row is queued here,
/// last write wins. The existing one-second bot-save flush (AutonomousBotStatusPersistence.Flush) writes the queue in
/// one go under one DatabaseWriteLock hold, instead of one SQLite statement (and one lock wait) per change while the
/// exchange locks are held. Listing, pulling back, crediting and every sale stay synchronous (sales are atomic). After
/// a crash at most one second of reprices and relist counters is lost; the item rows stay consistent.
/// </summary>
public static class RealmExchangeWriteQueue
{
    private static readonly object Gate = new();
    // true = save (insert when not yet stored), false = delete
    private static readonly Dictionary<DataObject, bool> Pending = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<DataObject, int> Failures = new(ReferenceEqualityComparer.Instance);
    private static long _nextErrorTick;

    /// <summary>Attempts before a queued write that keeps failing is dropped (logged).</summary>
    public const int MaxAttempts = 5;

    /// <summary>When false (tests) every queued change is written at once.</summary>
    public static bool Enabled { get; set; } = true;

    public static int Count
    {
        get { lock (Gate) return Pending.Count; }
    }

    public static void Save(DataObject row)
    {
        // a row already deleted (a credit, a vendor sale, a sale transaction) is never saved again
        if (row == null || row.IsDeleted)
            return;
        if (!Enabled)
        {
            WriteNow(row, true);
            return;
        }
        lock (Gate)
            Pending[row] = true;
    }

    public static void Delete(DataObject row)
    {
        if (row == null)
            return;
        if (!Enabled)
        {
            WriteNow(row, false);
            return;
        }
        lock (Gate)
        {
            // a row never stored needs no delete: forget its pending insert
            if (!row.IsPersisted && Pending.TryGetValue(row, out bool save) && save)
                Pending.Remove(row);
            else
                Pending[row] = false;
        }
    }

    /// <summary>Drops a queued change (the sale transaction, a credit or the archive deletes the row itself).</summary>
    public static void Cancel(DataObject row)
    {
        if (row == null)
            return;
        lock (Gate)
        {
            Pending.Remove(row);
            Failures.Remove(row);
        }
    }

    /// <summary>Whether a change to this row is waiting.</summary>
    public static bool IsQueued(DataObject row)
    {
        if (row == null)
            return false;
        lock (Gate)
            return Pending.ContainsKey(row);
    }

    /// <summary>
    /// Writes everything queued: updates, relist inserts and deletes, under one DatabaseWriteLock hold. The queue is read
    /// inside that lock, so no exchange change (a sale, a credit - both hold it) can run between the read and the write.
    /// A row deleted meanwhile (IsDeleted) is dropped and never inserted again: a deleted listing row must not come back
    /// as a live listing at the next start (review 2026-11-11). Only relist rows are ever inserted, and a relist row whose
    /// key is already stored updates it instead. Rows whose write failed are queued again, at most
    /// <see cref="MaxAttempts"/> times.
    /// </summary>
    public static int Flush()
    {
        lock (Gate)
            if (Pending.Count == 0)
                return 0;
        IObjectDatabase database = GameServer.Database;
        if (database == null)
            return 0;
        KeyValuePair<DataObject, bool>[] work = Array.Empty<KeyValuePair<DataObject, bool>>();
        try
        {
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                lock (Gate)
                {
                    work = Pending.ToArray();
                    Pending.Clear();
                }
                var inserts = new List<DataObject>();
                var updates = new List<DataObject>();
                var deletes = new List<DataObject>();
                foreach ((DataObject row, bool save) in work)
                {
                    if (row.IsDeleted)
                        continue;
                    if (!save)
                    {
                        if (row.IsPersisted)
                            deletes.Add(row);
                        continue;
                    }
                    if (row.IsPersisted)
                        updates.Add(row);
                    else if (row is OfflineTrue165RelistRecord relist)
                    {
                        if (database.FindObjectByKey<OfflineTrue165RelistRecord>(relist.InventoryId) != null)
                        {
                            relist.IsPersisted = true;
                            relist.Dirty = true;
                            updates.Add(relist);
                        }
                        else
                            inserts.Add(relist);
                    }
                    // any other row that was never stored is not inserted by the queue
                }
                bool ok = true;
                if (inserts.Count > 0)
                    ok &= database.AddObject(inserts);
                if (updates.Count > 0)
                    ok &= database.SaveObject(updates);
                if (deletes.Count > 0)
                    ok &= database.DeleteObject(deletes);
                Settle(work, ok ? null : work.Where(entry => !entry.Key.IsDeleted &&
                    (entry.Value ? entry.Key.Dirty || !entry.Key.IsPersisted : entry.Key.IsPersisted)).ToArray());
            }
            return work.Length;
        }
        catch (Exception error)
        {
            Settle(work, work);
            LogError("Realm Exchange queued writes failed; they wait for the next flush.", error);
            return 0;
        }
    }

    /// <summary>Forgets the failure counts of the rows written and queues the failed ones again (a newer change queued
    /// meanwhile wins); a row that failed <see cref="MaxAttempts"/> times is dropped.</summary>
    private static void Settle(KeyValuePair<DataObject, bool>[] work, KeyValuePair<DataObject, bool>[] failed)
    {
        int dropped = 0;
        lock (Gate)
        {
            HashSet<DataObject> retry = failed == null
                ? null
                : new HashSet<DataObject>(failed.Select(entry => entry.Key), ReferenceEqualityComparer.Instance);
            foreach ((DataObject row, bool save) in work)
            {
                if (retry == null || !retry.Contains(row))
                {
                    Failures.Remove(row);
                    continue;
                }
                int attempts = Failures.GetValueOrDefault(row) + 1;
                if (attempts >= MaxAttempts)
                {
                    Failures.Remove(row);
                    dropped++;
                    continue;
                }
                Failures[row] = attempts;
                Pending.TryAdd(row, save);
            }
        }
        if (dropped > 0)
            LogError($"Realm Exchange queued writes: {dropped} row(s) failed {MaxAttempts} times and were dropped.", null);
    }

    private static void LogError(string message, Exception error)
    {
        long now = Environment.TickCount64;
        if (now < _nextErrorTick)
            return;
        _nextErrorTick = now + 60_000;
        try { Logging.LoggerManager.Create(typeof(RealmExchangeWriteQueue)).Error(message, error); }
        catch { }
    }

    private static void WriteNow(DataObject row, bool save)
    {
        IObjectDatabase database = GameServer.Database;
        if (database == null || row.IsDeleted)
            return;
        if (!save)
        {
            if (row.IsPersisted)
                database.DeleteObject(row);
            return;
        }
        if (row.IsPersisted)
            database.SaveObject(row);
        else
            database.AddObject(row);
    }

    /// <summary>Tests only.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Pending.Clear();
            Failures.Clear();
        }
    }
}
