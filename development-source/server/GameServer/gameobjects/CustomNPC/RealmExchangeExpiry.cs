using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS;

/// <summary>A player's listing time (owner 2026-11-05, reset-day S38): 12, 24 or 48 hours, chosen when it is priced.</summary>
[DataTable(TableName = "offline_true165_listing_hours")]
public sealed class OfflineTrue165ListingHours : DataObject
{
    [PrimaryKey] public string InventoryId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Hours { get; set; }
}

public static class RealmExchangeExpiry
{
    public static readonly TimeSpan BotListingLifetime = TimeSpan.FromHours(24);
    public static readonly int[] PlayerHours = [12, 24, 48];
    public const int DefaultPlayerHours = 48;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> HoursCache = new();
    // hours rows whose delete waits in the write queue (a take-back): setting the hours again cancels the delete
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OfflineTrue165ListingHours> PendingHoursDeletes = new();
    private static Timer _timer;
    private static int _running;

    private static bool IsBotListing(DbInventoryItem item) => AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _);

    private static volatile bool _hoursLoaded;

    /// <summary>
    /// Startup, before the market cache is built: every listing-hours row into memory, so the exchange index never reads
    /// the database under its write lock (review 2026-11-11: a player pricing a fresh chest item did a SQLite read while
    /// every bot's probe waited on the index). Once loaded, an item with no row has the default hours.
    /// </summary>
    public static int WarmHours(IObjectDatabase database)
    {
        if (database == null)
            return 0;
        try
        {
            int loaded = 0;
            foreach (OfflineTrue165ListingHours row in database.SelectAllObjects<OfflineTrue165ListingHours>() ?? [])
                if (!string.IsNullOrEmpty(row?.InventoryId) && PlayerHours.Contains(row.Hours))
                {
                    HoursCache[row.InventoryId] = row.Hours;
                    loaded++;
                }
            _hoursLoaded = true;
            return loaded;
        }
        catch (Exception error)
        {
            Note("Realm Exchange listing hours not preloaded; they are read one by one.", error);
            return 0;
        }
    }

    /// <summary>A player listing's hours from memory only (the default when none is known): for the exchange index.</summary>
    public static int CachedPlayerListingHours(DbInventoryItem item) =>
        item?.ObjectId != null && HoursCache.TryGetValue(item.ObjectId, out int hours) ? hours : DefaultPlayerHours;

    /// <summary>A player listing's time: the hours chosen for it, 48 if none was chosen.</summary>
    public static int PlayerListingHours(DbInventoryItem item)
    {
        if (item?.ObjectId == null) return DefaultPlayerHours;
        if (HoursCache.TryGetValue(item.ObjectId, out int hours)) return hours;
        if (_hoursLoaded || GameServer.Database == null) return DefaultPlayerHours;
        hours = GameServer.Database.FindObjectByKey<OfflineTrue165ListingHours>(item.ObjectId)?.Hours ?? DefaultPlayerHours;
        HoursCache[item.ObjectId] = hours;
        return hours;
    }

    public static void SetPlayerListingHours(DbInventoryItem item, int hours)
    {
        if (item?.ObjectId == null || !PlayerHours.Contains(hours)) return;
        // a take-back in the same second queued this row's delete: it would wipe the hours set now (review 2026-11-11)
        if (PendingHoursDeletes.TryRemove(item.ObjectId, out OfflineTrue165ListingHours pending))
            RealmExchangeWriteQueue.Cancel(pending);
        var record = GameServer.Database.FindObjectByKey<OfflineTrue165ListingHours>(item.ObjectId);
        if (record == null)
            GameServer.Database.AddObject(new OfflineTrue165ListingHours { InventoryId = item.ObjectId, Hours = hours });
        else
        {
            record.Hours = hours;
            GameServer.Database.SaveObject(record);
        }
        HoursCache[item.ObjectId] = hours;
    }

    /// <summary>
    /// When a listing ends: a bot's 24 h after it was listed; a player's 12, 24 or 48 h after it was priced (owner
    /// 2026-11-05: player listings never expired). A listing priced before then, or stored at no price, has none.
    /// </summary>
    public static DateTime? ExpiresUtc(DbInventoryItem item) => ExpiresUtc(item, false);

    /// <summary>As <see cref="ExpiresUtc(DbInventoryItem)"/>; <paramref name="cachedOnly"/> never reads the database
    /// (the exchange index calls it under its write lock).</summary>
    public static DateTime? ExpiresUtc(DbInventoryItem item, bool cachedOnly)
    {
        if (item == null || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) ||
            !DateTime.TryParse(item.RealmExchangeListedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime listed))
            return null;
        if (IsBotListing(item))
            return listed.ToUniversalTime().Add(BotListingLifetime);
        if (item.SellPrice <= 0)
            return null;
        return listed.ToUniversalTime().AddHours(cachedOnly ? CachedPlayerListingHours(item) : PlayerListingHours(item));
    }

    public static bool IsExpired(DbInventoryItem item, DateTime utcNow) => ExpiresUtc(item) is DateTime expiry && expiry <= utcNow;

    /// <summary>Minutes left on a listing for the new client: 0xFFFF none (no time limit), 0 not offered.</summary>
    public static ushort MinutesLeft(DbInventoryItem item, DateTime utcNow)
    {
        if (item == null || item.SellPrice <= 0) return 0;
        if (ExpiresUtc(item) is not DateTime expiry) return 0xFFFF;
        return (ushort)Math.Clamp((int)Math.Ceiling((expiry - utcNow).TotalMinutes), 1, 0xFFFE);
    }

    public static string Describe(DbInventoryItem item, DateTime utcNow)
    {
        bool bot = IsBotListing(item);
        if (ExpiresUtc(item) is not DateTime expiry)
            return bot ? "Bot listing." : item?.SellPrice > 0 ? "Player listing: no expiration." : "Stored, not offered for sale.";
        TimeSpan remaining = expiry - utcNow;
        string who = bot ? "Bot" : "Player";
        return remaining <= TimeSpan.Zero ? $"{who} listing expired; awaiting removal." :
            $"{who} listing expires in {(int)remaining.TotalHours}h {remaining.Minutes}m ({expiry:yyyy-MM-dd HH:mm} UTC).";
    }


    /// <summary>A player listing is being sold: its hours row is returned so the sale transaction deletes it.</summary>
    public static OfflineTrue165ListingHours TakeHoursForSale(string objectId, string sellerId)
    {
        if (objectId == null || AutonomousBotEconomy.TryParseOwnerId(sellerId, out _))
            return null;
        HoursCache.TryRemove(objectId, out _);
        return new OfflineTrue165ListingHours { InventoryId = objectId, IsPersisted = true };
    }

    /// <summary>A player took an item back out of the listing chest: its hours row goes.</summary>
    public static void ForgetHours(DbInventoryItem item)
    {
        if (item?.ObjectId == null || AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _))
            return;
        HoursCache.TryRemove(item.ObjectId, out _);
        var row = new OfflineTrue165ListingHours { InventoryId = item.ObjectId, IsPersisted = true };
        if (PendingHoursDeletes.Count > 10_000)
            PendingHoursDeletes.Clear();
        PendingHoursDeletes[item.ObjectId] = row;
        RealmExchangeWriteQueue.Delete(row);
    }

    internal static void Start()
    {
        // Old listings predate the timestamp column: give those bots one full
        // bot-listing lifetime from migration, never expire an item on an unknown age.
        var listedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (DbInventoryItem item in MarketCache.SearchItems(new ItemQuery()))
        {
            if (!RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot))
                continue;
            if (item.ObjectId != null)
                listedIds.Add(item.ObjectId);
            if (!AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _) || ExpiresUtc(item) != null) continue;
            MarketCache.UpdateItem(item, static (listed, now) => listed.RealmExchangeListedUtc = now, DateTime.UtcNow.ToString("O"));
            GameServer.Database.SaveObject(item);
        }
        CleanBookkeeping(GameServer.Database, listedIds);
        _timer ??= new Timer(_ => Tick(), null, TickMilliseconds, TickMilliseconds);
    }

    /// <summary>
    /// Startup: loads the relist rows and removes the orphan bookkeeping rows - relist and listing-hours rows whose item
    /// is no longer listed (2026-11-11 exchange audit: 249 relist and 67 listing-hours rows of items that had sold).
    /// Side-table rows only, never a listing. Returns the rows deleted.
    /// </summary>
    public static int CleanBookkeeping(IObjectDatabase database, ISet<string> listedIds)
    {
        int removed = 0;
        try
        {
            removed += Exchange165.LoadRelists(database, listedIds);
            IList<OfflineTrue165ListingHours> hours = database?.SelectAllObjects<OfflineTrue165ListingHours>();
            if (hours != null)
            {
                DataObject[] orphans = hours.Where(row => row != null && !listedIds.Contains(row.InventoryId)).Cast<DataObject>().ToArray();
                foreach (OfflineTrue165ListingHours orphan in orphans.Cast<OfflineTrue165ListingHours>())
                    if (orphan.InventoryId != null)
                        HoursCache.TryRemove(orphan.InventoryId, out _);
                if (orphans.Length > 0)
                {
                    lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                        database.DeleteObject(orphans);
                    removed += orphans.Length;
                }
            }
            if (removed > 0)
                Note($"EXCHANGE_CLEANUP orphanRows={removed} relists={Exchange165.RelistCount}", null);
        }
        catch (Exception error)
        {
            Note("Realm Exchange bookkeeping cleanup skipped.", error);
        }
        return removed;
    }

    private static void Note(string message, Exception error)
    {
        try
        {
            var log = Logging.LoggerManager.Create(typeof(RealmExchangeExpiry));
            if (error == null)
                log.Info(message);
            else
                log.Warn(message, error);
        }
        catch { } // no logger (tests): diagnostics never stop the cleanup
    }

    /// <summary>The exchange timer: the market pass every 30 s, the expiry sweep every minute, the hourly line.</summary>
    public const int TickMilliseconds = 30_000;
    private static int _tickCount;

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        try
        {
            DateTime now = DateTime.UtcNow;
            RealmMarketPass.Run(now);
            int tick = Interlocked.Increment(ref _tickCount);
            if (tick % 2 == 0)
            {
                long start = Stopwatch.GetTimestamp();
                ExpireDue(now);
                RealmExchangeStats.NoteSweep(Stopwatch.GetTimestamp() - start);
            }
            if (tick % 120 == 0)
                ResyncWheel();
            RealmExchangeStats.LogHourlyIfDue(now);
        }
        catch (Exception error) { Logging.LoggerManager.Create(typeof(RealmExchangeExpiry)).Error("Realm Exchange expiry sweep failed; retained remaining items.", error); }
        finally { Volatile.Write(ref _running, 0); }
    }

    /// <summary>Hourly safety net: a listing whose timestamp changed without the index seeing it is re-read, so the
    /// expiry wheel can't keep an item past its time. Returns the listings re-read.</summary>
    public static int ResyncWheel()
    {
        List<DbInventoryItem> stale = MarketCache.ReadBook(book =>
        {
            var found = new List<DbInventoryItem>();
            foreach (DbInventoryItem item in book.AllItems())
                if (book.Find(item) is { } listing && listing.ExpiresTicks != (ExpiresUtc(item, true)?.Ticks ?? 0))
                    found.Add(item);
            return found;
        });
        int refreshed = 0;
        foreach (DbInventoryItem item in stale)
            if (MarketCache.Refresh(item))
                refreshed++;
        return refreshed;
    }

    /// <summary>Most expired listings handled per sweep (bounded writes; the rest wait a minute).</summary>
    public const int MaxPerSweep = 200;
    /// <summary>Most due listings a sweep looks at (a logged-off owner's waiting listing costs one lookup).</summary>
    public const int MaxDuePerSweep = 2_000;
    /// <summary>Most owners paid out per sweep: each payout writes to the database under the exchange and money locks
    /// (review 2026-11-11: after a restart the catch-up of offline owners stalled merchant trades behind them).</summary>
    public const int MaxCreditsPerSweep = 20;

    public static int ExpireDue(DateTime utcNow)
    {
        int removed = 0;
        int creditsAtStart = Exchange165.CreditCount;
        // Once a minute, only the listings whose expiry minute has come (the expiry wheel): no full copy, no parsing of
        // every timestamp. Nothing runs in individual AI turns.
        int handled = 0;
        foreach (DbInventoryItem item in MarketCache.ReadBook(book => book.Due(utcNow, MaxDuePerSweep)))
        {
            if (Exchange165.CreditCount - creditsAtStart >= MaxCreditsPerSweep || handled >= MaxPerSweep)
                break;   // the rest wait for the next minute
            if (!IsExpired(item, utcNow))
            {
                // the index had an older timestamp: re-read it so the wheel holds the real expiry
                MarketCache.Refresh(item);
                continue;
            }
            // owner 2026-11-11: a logged-off bot's listing waits for it (no relist, no pull, no coin): the sweep looks
            // again later, or as soon as the owner logs in. No lock, no write, not counted against the sweep's work.
            if (AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out long waitingOwner) &&
                Exchange165.ListingWaitsForOwner(true, Exchange165.IsOwnerOnline(waitingOwner)))
            {
                Exchange165.WaitForOwner(item, utcNow);
                continue;
            }
            handled++;
            // never wait long for the locks: a busy exchange tries again next minute
            if (!RealmExchangeLocks.TryEnter(25, true, out RealmExchangeLocks.Scope scope))
                break;
            using (scope)
            {
                if (!IsExpired(item, utcNow) || !MarketCache.Contains(item)) continue;
                if (!AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out long id))
                {
                    // a player's listing ran out: it stays in their listing chest, no longer offered (they can price it again)
                    MarketCache.UpdateItem(item, static (listed, value) =>
                    {
                        listed.SellPrice = value;
                        listed.RealmExchangeListedUtc = string.Empty;
                    }, 0);
                    item.Dirty = true;
                    GameServer.Database.SaveObject(item);
                    if (ClientService.Instance?.GetPlayers().FirstOrDefault(p => p.InternalID == item.OwnerID) is GamePlayer seller)
                        seller.Out.SendMessage($"Your Realm Exchange listing of {item.Name} ran out unsold; it waits in your listing chest.", PacketHandler.eChatType.CT_Merchant, PacketHandler.eChatLoc.CL_SystemWindow);
                    removed++;
                    continue;
                }
                // a bot's: relisted once for 12 h more, then back to the bot or paid out at the merchant value (Exchange165)
                if (Exchange165.HandleExpired(item, id, utcNow))
                {
                    AutonomousBotEconomy.MarkEconomyChanged(id);
                    removed++;
                }
            }
        }
        return removed;
    }
}
