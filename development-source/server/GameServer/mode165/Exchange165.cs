using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS
{
    /// <summary>
    /// A bot listing's relist state (Exchange165): Tries 0 = re-undercut once at 12 h, 1 = relisted once at 24 h,
    /// 2 = failed (pulled back unsold or bumped): the bot never lists that item again and sells it to a merchant.
    /// </summary>
    [DataTable(TableName = "offline_true165_relists")]
    public sealed class OfflineTrue165RelistRecord : DataObject
    {
        [PrimaryKey] public string InventoryId { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int Tries { get; set; }
        [DataElement(AllowDbNull = false)] public int OriginalPrice { get; set; }
    }

    /// <summary>
    /// True 1.65 Mode Realm Exchange rules for bot sellers (owner 2026-10-29/30, plan section 5; stale cycle from the
    /// 2026-11-11 exchange plan, section 2.4).
    /// - Pricing: valuable items ask more, within reason: base by level x quality x bonuses, from the plan's level-50
    ///   table (96% plate chest about 200 gold, 99% about 320, masterpiece about 495, masterpiece with 4 gems about 640),
    ///   capped. ItemValue165 builds the fair price on this curve.
    /// - Unsold (bot listings): at 12 h re-undercut once (the cheapest other copy -2%, or 85% of the ask, never under
    ///   1.25 x the merchant value; RealmMarketPass); at 24 h relisted once for 12 h more at 70% of the ask, or the median
    ///   recent sale price of its level and slot if lower, never under the merchant value and never dearer than before
    ///   (it went straight back to the owner when that floor can't be met); at 36 h pulled. A pulled item goes back to a
    ///   logged-in owner's bag marked "failed" (the next merchant visit sells it; it is never listed again). When the bag
    ///   is full or the owner is offline, the owner gets the merchant value in coin: an item never vanishes for nothing.
    ///   An offline owner's listing is not relisted: the first expiry pulls it, by the same rule.
    /// </summary>
    public static class Exchange165
    {
        private const long Gold = 10_000;
        public const int TriesUndercut = 0, TriesRelisted = 1, TriesFailed = 2;
        public static readonly TimeSpan UndercutAge = TimeSpan.FromHours(12);
        /// <summary>The second life of a relisted bot listing: 24 h + 12 h = 36 h in all.</summary>
        public static readonly TimeSpan RelistLifetime = TimeSpan.FromHours(12);
        public const double RelistShare = 0.70, UndercutShare = 0.85, UndercutOther = 0.98;

        private static readonly ConcurrentDictionary<string, OfflineTrue165RelistRecord> Relists = new(StringComparer.Ordinal);

        /// <summary>The 1.65 asking price in copper, or 0 to use the normal price (materials, stacks).</summary>
        public static long Price(DbInventoryItem item, double sellerDisposition)
        {
            if (!Classic165Mode.IsOn || item == null || item.MaxCount > 1 || item.Count > 1 ||
                (eObjectType)item.Object_Type is eObjectType.GenericItem or eObjectType.Poison or eObjectType.Arrow or eObjectType.Bolt)
                return 0;
            int level = Math.Clamp(item.Level, 1, 50);
            double baseGold = 200 * Math.Pow(level / 50d, 3) * SlotFactor(item);
            double quality = QualityFactor(item.Quality);
            int bonuses = Math.Abs(item.Bonus1) + Math.Abs(item.Bonus2) + Math.Abs(item.Bonus3) + Math.Abs(item.Bonus4) +
                          Math.Abs(item.Bonus5) + Math.Abs(item.Bonus6) + Math.Abs(item.Bonus7) + Math.Abs(item.Bonus8) +
                          Math.Abs(item.Bonus9) + Math.Abs(item.Bonus10) + Math.Abs(item.ExtraBonus);
            double utility = 1 + 0.0055 * bonuses;
            double condition = 0.45 + 0.55 * Math.Clamp((int)item.ConditionPercent, 1, 100) / 100d;
            double price = baseGold * quality * utility * condition * (1 + sellerDisposition) * Gold;
            double cap = Math.Max(1, 800 * Math.Pow(level / 50d, 3)) * Gold;
            return (long)Math.Clamp(Math.Round(price), 1, cap);
        }

        /// <summary>The owner's quality steps: 96% is the reference, masterpieces ask most.</summary>
        public static double QualityFactor(int quality) => quality switch
        {
            >= 100 => 2.475, 99 => 1.6, 98 => 1.35, 97 => 1.15, 96 => 1.0, 95 => 0.85, 94 => 0.75, _ => 0.6,
        };

        /// <summary>The body piece is the reference; other armour, shields and jewellery ask less, two-handers more.</summary>
        public static double SlotFactor(DbInventoryItem item) => (eInventorySlot)item.Item_Type switch
        {
            eInventorySlot.TorsoArmor => 1.0,
            eInventorySlot.LegsArmor => 0.85,
            eInventorySlot.HeadArmor or eInventorySlot.ArmsArmor => 0.7,
            eInventorySlot.HandsArmor or eInventorySlot.FeetArmor => 0.6,
            eInventorySlot.TwoHandWeapon => 1.1,
            eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.DistanceWeapon => 0.9,
            eInventorySlot.Cloak => 0.5,
            _ => 0.6,   // jewellery, belts, bracers, rings, necks
        };

        // ---- relist state (in memory; the table is read once at startup and written through the exchange write queue)

        public static OfflineTrue165RelistRecord RelistOf(DbInventoryItem item) =>
            item?.ObjectId != null && Relists.TryGetValue(item.ObjectId, out OfflineTrue165RelistRecord record) ? record : null;

        /// <summary>Pulled back unsold or bumped: the bot sells it to a merchant and never lists it again.</summary>
        public static bool HasFailed(DbInventoryItem item) => RelistOf(item)?.Tries >= TriesFailed;

        public static void MarkFailed(DbInventoryItem item, int originalPrice)
        {
            if (item?.ObjectId == null)
                return;
            OfflineTrue165RelistRecord record = Relists.GetOrAdd(item.ObjectId,
                id => new OfflineTrue165RelistRecord { InventoryId = id, OriginalPrice = originalPrice });
            record.Tries = TriesFailed;
            record.Dirty = true;
            RealmExchangeWriteQueue.Save(record);
        }

        /// <summary>The item left the bot (sold to a merchant, destroyed): its relist row goes too.</summary>
        public static void Forget(DbInventoryItem item)
        {
            if (item?.ObjectId != null && Relists.TryRemove(item.ObjectId, out OfflineTrue165RelistRecord record))
                RealmExchangeWriteQueue.Delete(record);
        }

        /// <summary>A sale is about to commit: the row is taken out of memory and returned (when stored) so the sale
        /// transaction deletes it. <see cref="RestoreAfterFailedSale"/> puts it back when the sale fails.</summary>
        public static OfflineTrue165RelistRecord TakeForSale(DbInventoryItem item)
        {
            if (item?.ObjectId == null || !Relists.TryRemove(item.ObjectId, out OfflineTrue165RelistRecord record))
                return null;
            RealmExchangeWriteQueue.Cancel(record);
            return record;
        }

        public static void RestoreAfterFailedSale(OfflineTrue165RelistRecord record)
        {
            if (record?.InventoryId == null)
                return;
            Relists[record.InventoryId] = record;
            if (record.Dirty || !record.IsPersisted)
                RealmExchangeWriteQueue.Save(record);
        }

        public static int RelistCount => Relists.Count;

        /// <summary>
        /// A bot's items leave the world (cold archive, retirement): their records leave memory and their queued writes are
        /// cancelled, as their rows are deleted with the bot. Returns the records taken (put back by <see cref="Adopt"/>
        /// when the move fails).
        /// </summary>
        public static List<OfflineTrue165RelistRecord> Detach(IEnumerable<string> inventoryIds)
        {
            var taken = new List<OfflineTrue165RelistRecord>();
            if (inventoryIds == null)
                return taken;
            foreach (string id in inventoryIds)
            {
                if (id == null || !Relists.TryRemove(id, out OfflineTrue165RelistRecord record))
                    continue;
                RealmExchangeWriteQueue.Cancel(record);
                taken.Add(record);
            }
            return taken;
        }

        /// <summary>Relist rows that are in the database again (a bot restored from the cold archive, or a failed move put
        /// back) come into memory. A record already in memory stays and is saved over the stored row.</summary>
        public static int Adopt(IEnumerable<OfflineTrue165RelistRecord> rows)
        {
            int adopted = 0;
            if (rows == null)
                return 0;
            foreach (OfflineTrue165RelistRecord row in rows)
            {
                if (string.IsNullOrEmpty(row?.InventoryId))
                    continue;
                if (Relists.TryAdd(row.InventoryId, row))
                {
                    adopted++;
                    if (row.Dirty || !row.IsPersisted)
                        RealmExchangeWriteQueue.Save(row);
                    continue;
                }
                if (Relists.TryGetValue(row.InventoryId, out OfflineTrue165RelistRecord existing) && !ReferenceEquals(existing, row))
                {
                    existing.IsPersisted = true;
                    existing.Dirty = true;
                    RealmExchangeWriteQueue.Save(existing);
                }
            }
            return adopted;
        }

        /// <summary>
        /// Startup: reads the relist table once and removes its orphan rows (2026-11-11 exchange audit: 249 rows pointed at
        /// items that had sold; a buyer listing such an item later skipped its discount). Only bookkeeping rows are touched,
        /// never an item: a row is an orphan when its item is no longer listed (a relist or undercut row), or, for a
        /// "failed" row, when its item no longer exists in a bag. Returns the rows deleted.
        /// </summary>
        public static int LoadRelists(IObjectDatabase database, ISet<string> listedIds)
        {
            Relists.Clear();
            if (database == null)
                return 0;
            IList<OfflineTrue165RelistRecord> rows;
            try
            {
                rows = database.SelectAllObjects<OfflineTrue165RelistRecord>();
            }
            catch (Exception)
            {
                return 0;
            }
            if (rows == null)
                return 0;
            var orphans = new List<DataObject>();
            var failed = new List<OfflineTrue165RelistRecord>();
            foreach (OfflineTrue165RelistRecord row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.InventoryId))
                    continue;
                if (listedIds.Contains(row.InventoryId))
                    Relists[row.InventoryId] = row;
                else if (row.Tries >= TriesFailed)
                    failed.Add(row);
                else
                    orphans.Add(row);
            }
            if (failed.Count > 0)
            {
                var existing = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    foreach (OfflineTrue165RelistRecord[] chunk in failed.Chunk(500))
                        foreach (DbInventoryItem item in DOLDB<DbInventoryItem>.SelectObjects(
                                     DB.Column("Inventory_ID").IsIn(chunk.Select(row => row.InventoryId))) ?? [])
                            if (item != null && item.OwnerLot == 0)
                                existing.Add(item.ObjectId);
                }
                catch (Exception)
                {
                    // can't tell: keep every failed row
                    foreach (OfflineTrue165RelistRecord row in failed)
                        existing.Add(row.InventoryId);
                }
                foreach (OfflineTrue165RelistRecord row in failed)
                {
                    if (existing.Contains(row.InventoryId))
                        Relists[row.InventoryId] = row;
                    else
                        orphans.Add(row);
                }
            }
            if (orphans.Count > 0)
            {
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                    database.DeleteObject(orphans);
            }
            return orphans.Count;
        }

        // ---- the stale cycle

        /// <summary>
        /// 12 h unsold (RealmMarketPass): re-undercut once - the cheapest other copy -2%, or 85% of the ask, never under
        /// 1.25 x the merchant value and never dearer than before. True when the price changed. The caller holds the
        /// exchange locks.
        /// </summary>
        public static bool TryUndercut(DbInventoryItem item, DateTime utcNow)
        {
            if (item == null || !AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _) || item.SellPrice <= 0 ||
                RelistOf(item) != null || !MarketCache.Contains(item))
                return false;
            MarketBook.Listing listing = MarketCache.ReadBook(book => book.Find(item));
            if (listing == null || listing.ListedTicks <= 0 || utcNow.Ticks - listing.ListedTicks < UndercutAge.Ticks ||
                listing.ExpiresTicks > 0 && listing.ExpiresTicks <= utcNow.Ticks)
                return false;   // an expired listing is the expiry sweep's (it relists or pulls it)
            // per unit: the cheapest other copy's price for as many items as this listing holds
            long cheapestOther = MarketCache.ReadBook(book => book.Name(listing.Realm, listing.NameKey, listing.Owner)).CheapestFor(listing.Count);
            long ask = item.SellPrice;
            long target = UndercutPrice(ask, cheapestOther, ItemValue165.AskFloor(item));
            var record = new OfflineTrue165RelistRecord { InventoryId = item.ObjectId, Tries = TriesUndercut, OriginalPrice = (int)ask };
            Relists[item.ObjectId] = record;
            RealmExchangeWriteQueue.Save(record);
            if (target >= ask)
                return false;
            MarketCache.UpdateItem(item, static (listed, value) => listed.SellPrice = value, (int)target);
            item.Dirty = true;
            RealmExchangeWriteQueue.Save(item);
            RealmExchangeStats.Note(listing.Realm, RealmExchangeStats.Counter.Undercut);
            return true;
        }

        /// <summary>Pure rule: the 12 h re-undercut price (never above the ask).</summary>
        public static long UndercutPrice(long ask, long cheapestOther, long askFloor)
        {
            double target = cheapestOther > 0 && cheapestOther < ask ? cheapestOther * UndercutOther : ask * UndercutShare;
            return Math.Min(ask, Math.Max(askFloor, (long)target));
        }

        /// <summary>Pure rule: the 24 h relist price, or 0 when the floor can't be met below the ask (it is pulled then).
        /// 70% of the ask, or the recent median sale price when lower, never under the merchant value + 1, never dearer.</summary>
        public static long RelistPrice(long ask, long floor, long medianSale)
        {
            long price = (long)(ask * RelistShare);
            if (medianSale > 0)
                price = Math.Min(price, medianSale);
            price = Math.Max(price, floor + 1);
            return price >= ask ? 0 : price;
        }

        /// <summary>
        /// An expired bot listing (24 h, or 36 h after a relist). The caller holds the exchange locks (money included).
        /// True when it was handled (relisted, returned or credited); false leaves it listed for the next sweep.
        /// </summary>
        public static bool HandleExpired(DbInventoryItem item, long botId, DateTime utcNow)
        {
            if (item == null)
                return false;
            OfflineTrue165RelistRecord record = RelistOf(item);
            bool online = AutonomousBotRegistry.TryGet(botId, out GameBot live) && live?.Inventory != null;
            eRealm realm = MarketSearchEngine.GetRealmOfLot(item.OwnerLot);
            int ask = item.SellPrice;
            if (online && (record == null || record.Tries < TriesRelisted))
            {
                long floor = ItemValue165.Floor(item);
                long median = MarketCache.MedianSale(realm, item.Level, MarketSearchEngine.GetClientSlot(item));
                long price = RelistPrice(ask, floor, median);
                if (price > 0)
                {
                    record ??= Relists.GetOrAdd(item.ObjectId, id => new OfflineTrue165RelistRecord { InventoryId = id, OriginalPrice = ask });
                    if (record.OriginalPrice <= 0)
                        record.OriginalPrice = ask;
                    record.Tries = TriesRelisted;
                    record.Dirty = true;
                    RealmExchangeWriteQueue.Save(record);
                    // its second life is 12 h: the listing clock is set so it expires 12 h from now
                    string relisted = utcNow.ToUniversalTime().Subtract(RealmExchangeExpiry.BotListingLifetime - RelistLifetime).ToString("O");
                    MarketCache.UpdateItem(item, static (listed, state) =>
                    {
                        listed.SellPrice = state.Price;
                        listed.RealmExchangeListedUtc = state.Listed;
                    }, (Price: (int)price, Listed: relisted));
                    item.Dirty = true;
                    RealmExchangeWriteQueue.Save(item);
                    RealmExchangeStats.Note(realm, RealmExchangeStats.Counter.Relisted);
                    return true;
                }
            }
            return Pull(item, botId, record?.OriginalPrice > 0 ? record.OriginalPrice : ask, RealmExchangeStats.Counter.Returned);
        }

        /// <summary>
        /// Takes a bot listing off the exchange (36 h unsold, bumped by a cheaper copy, or an offline owner's expiry): back to
        /// a logged-in owner's bag, marked failed; with no room or the owner offline, the owner is paid the merchant value
        /// and the item is gone (it was sold to "the merchant"). Never deleted for nothing. The caller holds the exchange
        /// locks (money included). True when it left the exchange.
        /// </summary>
        public static bool Pull(DbInventoryItem item, long botId, int originalPrice, RealmExchangeStats.Counter reason)
        {
            if (item == null)
                return false;
            eRealm realm = MarketSearchEngine.GetRealmOfLot(item.OwnerLot);
            if (AutonomousBotRegistry.TryGet(botId, out GameBot live) && live?.Inventory != null)
            {
                eInventorySlot free = live.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (free != eInventorySlot.Invalid)
                {
                    ushort lot = item.OwnerLot;
                    int price = item.SellPrice, slot = item.SlotPosition;
                    string listed = item.RealmExchangeListedUtc;
                    MarketCache.RemoveItem(item);
                    item.OwnerLot = 0;
                    item.SellPrice = 0;
                    item.RealmExchangeListedUtc = string.Empty;
                    if (live.Inventory.AddItem(free, item))
                    {
                        MarkFailed(item, originalPrice);
                        AutonomousBotEconomy.MarkInventoryChanged(live);
                        AutonomousBotStatusPersistence.Queue(live, true);
                        RealmExchangeStats.Note(realm, reason);
                        return true;
                    }
                    item.OwnerLot = lot;
                    item.SellPrice = price;
                    item.SlotPosition = slot;
                    item.RealmExchangeListedUtc = listed;
                    item.OwnerID = AutonomousBotEconomy.GetOwnerId(botId);
                    MarketCache.AddItem(item);
                }
            }
            return Credit(item, botId, realm);
        }

        /// <summary>The owner is paid what a merchant pays and the item row is deleted (the merchant has it).</summary>
        private static bool Credit(DbInventoryItem item, long botId, eRealm realm)
        {
            long copper = ItemValue165.Floor(item);
            if (!GameServer.Database.DeleteObject(item))
                return false;
            // a reprice or relist of this row may still wait in the write queue: it must not insert the row again
            RealmExchangeWriteQueue.Cancel(item);
            MarketCache.RemoveItem(item);
            Forget(item);
            if (copper > 0)
                AutonomousBotEconomy.AddMoney(botId, copper);
            else
                AutonomousBotEconomy.MarkEconomyChanged(botId);
            RealmExchangeStats.Note(realm, RealmExchangeStats.Counter.Credited);
            return true;
        }
    }
}
