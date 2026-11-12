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
    private static long _nextErrorTick;

    /// <summary>When false (tests) every queued change is written at once.</summary>
    public static bool Enabled { get; set; } = true;

    public static int Count
    {
        get { lock (Gate) return Pending.Count; }
    }

    public static void Save(DataObject row)
    {
        if (row == null)
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

    /// <summary>Drops a queued change (the sale transaction deletes the row itself).</summary>
    public static void Cancel(DataObject row)
    {
        if (row == null)
            return;
        lock (Gate)
            Pending.Remove(row);
    }

    /// <summary>Writes everything queued: inserts, updates and deletes, under one DatabaseWriteLock hold. Rows whose
    /// write failed are queued again for the next flush.</summary>
    public static int Flush()
    {
        KeyValuePair<DataObject, bool>[] work;
        lock (Gate)
        {
            if (Pending.Count == 0)
                return 0;
            work = Pending.ToArray();
            Pending.Clear();
        }
        IObjectDatabase database = GameServer.Database;
        if (database == null)
        {
            Requeue(work);
            return 0;
        }
        DataObject[] inserts = work.Where(entry => entry.Value && !entry.Key.IsPersisted).Select(entry => entry.Key).ToArray();
        DataObject[] updates = work.Where(entry => entry.Value && entry.Key.IsPersisted).Select(entry => entry.Key).ToArray();
        DataObject[] deletes = work.Where(entry => !entry.Value && entry.Key.IsPersisted).Select(entry => entry.Key).ToArray();
        try
        {
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                bool ok = true;
                if (inserts.Length > 0)
                    ok &= database.AddObject(inserts);
                if (updates.Length > 0)
                    ok &= database.SaveObject(updates);
                if (deletes.Length > 0)
                    ok &= database.DeleteObject(deletes);
                if (!ok)
                    Requeue(work.Where(entry => entry.Value ? entry.Key.Dirty || !entry.Key.IsPersisted : entry.Key.IsPersisted).ToArray());
            }
            return work.Length;
        }
        catch (Exception error)
        {
            Requeue(work);
            long now = Environment.TickCount64;
            if (now >= _nextErrorTick)
            {
                _nextErrorTick = now + 60_000;
                try { Logging.LoggerManager.Create(typeof(RealmExchangeWriteQueue)).Error("Realm Exchange queued writes failed; they wait for the next flush.", error); }
                catch { }
            }
            return 0;
        }
    }

    private static void Requeue(IEnumerable<KeyValuePair<DataObject, bool>> entries)
    {
        lock (Gate)
            foreach ((DataObject row, bool save) in entries)
                Pending.TryAdd(row, save);   // a newer change queued meanwhile wins
    }

    private static void WriteNow(DataObject row, bool save)
    {
        IObjectDatabase database = GameServer.Database;
        if (database == null)
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
            Pending.Clear();
    }
}
