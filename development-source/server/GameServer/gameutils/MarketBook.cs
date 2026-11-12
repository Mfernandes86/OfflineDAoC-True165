using System;
using System.Collections.Generic;
using System.Globalization;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// A per-realm in-memory index of the Realm Exchange (2026-11-11 exchange plan, section 2.6). It lives inside the
    /// MarketSearchEngine and changes only where the market already changes (MarketCache.AddItem / RemoveItem /
    /// UpdateItem), under the engine's write lock, so it can never hold a listing the cache doesn't. It is rebuilt with the
    /// cache at startup. Per listing it keeps the facts every decision needs, worked out once: the expiry (parsed once,
    /// for the expiry wheel), owner, level, slot bucket, price and value score (ItemValue165). Per item name it keeps the
    /// copies; per owner the listings and a slot bitmap; per realm the counts for the soft ceiling and the hourly line.
    /// Bots read their own few listings, a name's few copies or one slot/level bucket - never the whole market.
    /// Cost: about 1-2 MB at 15,000 listings; O(1) per change.
    /// </summary>
    public sealed class MarketBook
    {
        public sealed class Listing
        {
            public DbInventoryItem Item;
            public eRealm Realm;
            public string Owner;
            public long BotId;
            public bool IsBot => BotId > 0;
            public string NameKey;
            public string IdKey;
            public int Level;
            public int Bucket;
            public int Band;
            public long Price;
            public long Fair;
            public long Floor;
            public long Gain => Math.Max(0, Fair - Floor);
            public long ListedTicks;
            public long ExpiresTicks;
            public long WheelMinute;
            public int Slot;
        }

        public readonly record struct RealmSummary(int BotListings, int PlayerListings, int DistinctNames, int Sellers);

        /// <summary>A name's copies as one seller sees them: the bot copies in the realm, this seller's own copies, the
        /// cheapest copy of anyone else, and the dearest bot copy of anyone else (the one a new lister would bump).</summary>
        public readonly record struct NameStats(int BotCopies, int OwnCopies, long CheapestOther, Listing DearestOtherBot);

        public const int Bands = 11;

        private sealed class RealmBook
        {
            public int BotListings, PlayerListings;
            public readonly Dictionary<string, HashSet<Listing>> ByName = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, HashSet<Listing>> ById = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, HashSet<Listing>> Buckets = new();
            public readonly Dictionary<string, int> Sellers = new(StringComparer.Ordinal);
            public int SellersVersion;
            public string[] SellerSnapshot = Array.Empty<string>();
            public int SnapshotVersion = -1;
        }

        private sealed class OwnerBook
        {
            public readonly HashSet<Listing> Listings = new();
            public ulong Low, High;   // consignment slots 0-63, 64-127
        }

        private sealed class SaleRing
        {
            public readonly long[] Prices = new long[16];
            public int Count, Next;

            public void Add(long price)
            {
                Prices[Next] = price;
                Next = (Next + 1) % Prices.Length;
                if (Count < Prices.Length)
                    Count++;
            }

            public long Median()
            {
                if (Count == 0)
                    return 0;
                long[] copy = new long[Count];
                Array.Copy(Prices, copy, Count);
                Array.Sort(copy);
                return copy[Count / 2];
            }
        }

        private readonly Dictionary<DbInventoryItem, Listing> _all = new();
        private readonly RealmBook[] _realms = [new(), new(), new(), new()];
        private readonly Dictionary<string, OwnerBook> _owners = new(StringComparer.Ordinal);
        private readonly SortedDictionary<long, HashSet<Listing>> _wheel = new();
        private readonly object _salesGate = new();
        private readonly Dictionary<(int Realm, string Name), SaleRing> _salesByName = new();
        private readonly Dictionary<(int Realm, int Band, int Bucket), SaleRing> _salesByBand = new();

        public int Count => _all.Count;

        private static int RealmIndex(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia ? (int)realm : 0;
        private static int BucketKey(int bucket, int band) => bucket * 16 + band;
        public static int BandOf(int level) => Math.Clamp(level, 0, 54) / 5;

        // ---- changes (called by MarketSearchEngine under its write lock)

        internal void Add(DbInventoryItem item, int clientSlot)
        {
            if (item == null || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) || _all.ContainsKey(item))
                return;
            var listing = new Listing
            {
                Item = item,
                Realm = MarketSearchEngine.GetRealmOfLot(item.OwnerLot),
                Owner = item.OwnerID ?? string.Empty,
                NameKey = item.Name ?? string.Empty,
                IdKey = item.Id_nb ?? string.Empty,
                Level = item.Level,
                Bucket = clientSlot,
                Band = BandOf(item.Level),
                Price = item.SellPrice,
                Slot = item.SlotPosition,
            };
            AutonomousBotEconomy.TryParseOwnerId(listing.Owner, out listing.BotId);
            try
            {
                listing.Fair = ItemValue165.Fair(item);
                listing.Floor = ItemValue165.Floor(item);
            }
            catch (Exception)
            {
                listing.Fair = Math.Max(1, item.SellPrice);
            }
            if (DateTime.TryParse(item.RealmExchangeListedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime listed))
                listing.ListedTicks = listed.ToUniversalTime().Ticks;
            try
            {
                listing.ExpiresTicks = RealmExchangeExpiry.ExpiresUtc(item)?.Ticks ?? 0;
            }
            catch (Exception)
            {
                listing.ExpiresTicks = 0;
            }
            _all[item] = listing;

            RealmBook realm = _realms[RealmIndex(listing.Realm)];
            if (listing.IsBot)
            {
                realm.BotListings++;
                realm.Sellers[listing.Owner] = realm.Sellers.GetValueOrDefault(listing.Owner) + 1;
                if (realm.Sellers[listing.Owner] == 1)
                    realm.SellersVersion++;
            }
            else
                realm.PlayerListings++;
            AddTo(realm.ByName, listing.NameKey, listing);
            AddTo(realm.ById, listing.IdKey, listing);
            AddTo(realm.Buckets, BucketKey(listing.Bucket, listing.Band), listing);

            if (!_owners.TryGetValue(listing.Owner, out OwnerBook owner))
                _owners[listing.Owner] = owner = new OwnerBook();
            owner.Listings.Add(listing);
            SetSlot(owner, listing.Slot, true);

            if (listing.ExpiresTicks > 0)
            {
                listing.WheelMinute = listing.ExpiresTicks / TimeSpan.TicksPerMinute;
                AddTo(_wheel, listing.WheelMinute, listing);
            }
        }

        internal void Remove(DbInventoryItem item)
        {
            if (item == null || !_all.Remove(item, out Listing listing))
                return;
            RealmBook realm = _realms[RealmIndex(listing.Realm)];
            if (listing.IsBot)
            {
                realm.BotListings--;
                int left = realm.Sellers.GetValueOrDefault(listing.Owner) - 1;
                if (left <= 0)
                {
                    realm.Sellers.Remove(listing.Owner);
                    realm.SellersVersion++;
                }
                else
                    realm.Sellers[listing.Owner] = left;
            }
            else
                realm.PlayerListings--;
            RemoveFrom(realm.ByName, listing.NameKey, listing);
            RemoveFrom(realm.ById, listing.IdKey, listing);
            RemoveFrom(realm.Buckets, BucketKey(listing.Bucket, listing.Band), listing);
            if (_owners.TryGetValue(listing.Owner, out OwnerBook owner))
            {
                owner.Listings.Remove(listing);
                SetSlot(owner, listing.Slot, false);
                // another listing of this owner in the same slot (a corrupt chest) keeps it taken
                foreach (Listing other in owner.Listings)
                    if (other.Slot == listing.Slot)
                        SetSlot(owner, other.Slot, true);
                if (owner.Listings.Count == 0)
                    _owners.Remove(listing.Owner);
            }
            if (listing.ExpiresTicks > 0)
                RemoveFrom(_wheel, listing.WheelMinute, listing);
        }

        private static void SetSlot(OwnerBook owner, int slot, bool taken)
        {
            int index = slot - (int)eInventorySlot.Consignment_First;
            if (index is < 0 or >= 128)
                return;
            ulong bit = 1UL << (index & 63);
            if (index < 64)
                owner.Low = taken ? owner.Low | bit : owner.Low & ~bit;
            else
                owner.High = taken ? owner.High | bit : owner.High & ~bit;
        }

        private static void AddTo<TKey>(IDictionary<TKey, HashSet<Listing>> index, TKey key, Listing listing)
        {
            if (!index.TryGetValue(key, out HashSet<Listing> set))
                index[key] = set = new HashSet<Listing>();
            set.Add(listing);
        }

        private static void RemoveFrom<TKey>(IDictionary<TKey, HashSet<Listing>> index, TKey key, Listing listing)
        {
            if (index.TryGetValue(key, out HashSet<Listing> set) && set.Remove(listing) && set.Count == 0)
                index.Remove(key);
        }

        // ---- queries (called by MarketSearchEngine under its read lock)

        public bool Contains(DbInventoryItem item) => item != null && _all.ContainsKey(item);

        /// <summary>Every listed item (the hourly resync only).</summary>
        public IEnumerable<DbInventoryItem> AllItems() => _all.Keys;

        /// <summary>How many bot copies of this name rank before this listing - cheaper, or as cheap and older by id - (the
        /// market pass, for a name over its copy limit; the tie-break lets a wall of equal prices drain too).</summary>
        public int CheaperBotCopies(Listing listing)
        {
            if (listing == null || string.IsNullOrEmpty(listing.NameKey) ||
                !_realms[RealmIndex(listing.Realm)].ByName.TryGetValue(listing.NameKey, out HashSet<Listing> copies))
                return 0;
            int cheaper = 0;
            foreach (Listing copy in copies)
                if (copy.IsBot && !ReferenceEquals(copy, listing) && (copy.Price < listing.Price ||
                    copy.Price == listing.Price && string.CompareOrdinal(copy.Item.ObjectId, listing.Item.ObjectId) < 0))
                    cheaper++;
            return cheaper;
        }

        public Listing Find(DbInventoryItem item) => item != null && _all.TryGetValue(item, out Listing listing) ? listing : null;

        public int OwnerCount(string owner) => owner != null && _owners.TryGetValue(owner, out OwnerBook book) ? book.Listings.Count : 0;

        public Listing[] OwnerListings(string owner)
        {
            if (owner == null || !_owners.TryGetValue(owner, out OwnerBook book))
                return Array.Empty<Listing>();
            var result = new Listing[book.Listings.Count];
            book.Listings.CopyTo(result);
            return result;
        }

        /// <summary>The first free consignment slot of this owner's 100-slot chest, or -1.</summary>
        public int FreeSlot(string owner)
        {
            ulong low = 0, high = 0;
            if (owner != null && _owners.TryGetValue(owner, out OwnerBook book))
            {
                low = book.Low;
                high = book.High;
            }
            int span = (int)eInventorySlot.Consignment_Last - (int)eInventorySlot.Consignment_First + 1;
            for (int index = 0; index < span && index < 128; index++)
            {
                ulong bits = index < 64 ? low : high;
                if ((bits & (1UL << (index & 63))) == 0)
                    return (int)eInventorySlot.Consignment_First + index;
            }
            return -1;
        }

        public int BotListings(eRealm realm) => _realms[RealmIndex(realm)].BotListings;

        public NameStats Name(eRealm realm, string name, string seller)
        {
            if (string.IsNullOrEmpty(name) || !_realms[RealmIndex(realm)].ByName.TryGetValue(name, out HashSet<Listing> copies))
                return default;
            int bots = 0, own = 0;
            long cheapest = 0;
            Listing dearest = null;
            foreach (Listing copy in copies)
            {
                if (copy.IsBot)
                    bots++;
                if (copy.Owner == seller)
                {
                    own++;
                    continue;
                }
                if (copy.Price > 0 && (cheapest == 0 || copy.Price < cheapest))
                    cheapest = copy.Price;
                if (copy.IsBot && (dearest == null || copy.Price > dearest.Price))
                    dearest = copy;
            }
            return new NameStats(bots, own, cheapest, dearest);
        }

        /// <summary>Listings of one slot bucket and a band range, priced and at most <paramref name="maxPrice"/>.</summary>
        public List<Listing> Probe(eRealm realm, int bucket, int bandLow, int bandHigh, long maxPrice, string excludeOwner)
        {
            var result = new List<Listing>();
            RealmBook book = _realms[RealmIndex(realm)];
            for (int band = Math.Max(0, bandLow); band <= Math.Min(Bands - 1, bandHigh); band++)
            {
                if (!book.Buckets.TryGetValue(BucketKey(bucket, band), out HashSet<Listing> set))
                    continue;
                foreach (Listing listing in set)
                    if (listing.Price > 0 && listing.Price <= maxPrice && listing.Owner != excludeOwner)
                        result.Add(listing);
            }
            return result;
        }

        /// <summary>Priced listings of one template (ammunition, poisons, materials).</summary>
        public List<Listing> OfTemplate(eRealm realm, string templateId, long maxPrice, string excludeOwner)
        {
            var result = new List<Listing>();
            if (string.IsNullOrEmpty(templateId) || !_realms[RealmIndex(realm)].ById.TryGetValue(templateId, out HashSet<Listing> set))
                return result;
            foreach (Listing listing in set)
                if (listing.Price > 0 && listing.Price <= maxPrice && listing.Owner != excludeOwner)
                    result.Add(listing);
            return result;
        }

        /// <summary>The templates listed in the realm, for a crafter matching them against its recipes.</summary>
        public int TemplateCount(eRealm realm) => _realms[RealmIndex(realm)].ById.Count;

        public List<string> Templates(eRealm realm) => new(_realms[RealmIndex(realm)].ById.Keys);

        /// <summary>The bot sellers of a realm (a snapshot, rebuilt only when a seller comes or goes).</summary>
        public string[] Sellers(eRealm realm)
        {
            RealmBook book = _realms[RealmIndex(realm)];
            if (book.SnapshotVersion != book.SellersVersion)
            {
                var keys = new string[book.Sellers.Count];
                book.Sellers.Keys.CopyTo(keys, 0);
                Array.Sort(keys, StringComparer.Ordinal);
                book.SellerSnapshot = keys;
                book.SnapshotVersion = book.SellersVersion;
            }
            return book.SellerSnapshot;
        }

        /// <summary>The listings whose expiry minute has come, oldest first (the expiry wheel): only these are looked at.</summary>
        public List<DbInventoryItem> Due(DateTime utcNow, int max)
        {
            var result = new List<DbInventoryItem>();
            long nowMinute = utcNow.ToUniversalTime().Ticks / TimeSpan.TicksPerMinute;
            foreach ((long minute, HashSet<Listing> set) in _wheel)
            {
                if (minute > nowMinute || result.Count >= max)
                    break;
                foreach (Listing listing in set)
                {
                    if (result.Count >= max)
                        break;
                    result.Add(listing.Item);
                }
            }
            return result;
        }

        public RealmSummary Summary(eRealm realm)
        {
            RealmBook book = _realms[RealmIndex(realm)];
            return new RealmSummary(book.BotListings, book.PlayerListings, book.ByName.Count, book.Sellers.Count);
        }

        /// <summary>Whether the book holds exactly these exchange listings, with matching facts (the hourly drift check
        /// and the unit test).</summary>
        public bool Matches(IEnumerable<DbInventoryItem> cached, out string note)
        {
            int exchange = 0;
            foreach (DbInventoryItem item in cached)
            {
                if (!RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot))
                    continue;
                exchange++;
                if (!_all.TryGetValue(item, out Listing listing))
                {
                    note = "missing listing";
                    return false;
                }
                if (listing.Price != item.SellPrice || listing.Owner != (item.OwnerID ?? string.Empty) || listing.Slot != item.SlotPosition)
                {
                    note = "stale listing facts";
                    return false;
                }
            }
            if (exchange != _all.Count)
            {
                note = $"count {_all.Count} vs {exchange}";
                return false;
            }
            int bots = 0, players = 0;
            foreach (RealmBook realm in _realms)
            {
                bots += realm.BotListings;
                players += realm.PlayerListings;
            }
            if (bots + players != _all.Count)
            {
                note = "realm counts";
                return false;
            }
            note = string.Empty;
            return true;
        }

        // ---- recent sale prices (their own small lock: sales come from several threads)

        public void NoteSale(eRealm realm, DbInventoryItem item, int clientSlot, long price)
        {
            if (item == null || price <= 0)
                return;
            int r = RealmIndex(realm);
            lock (_salesGate)
            {
                var nameKey = (r, (item.Name ?? string.Empty).ToLowerInvariant());
                if (!_salesByName.TryGetValue(nameKey, out SaleRing byName))
                {
                    if (_salesByName.Count > 20_000)
                        _salesByName.Clear();
                    _salesByName[nameKey] = byName = new SaleRing();
                }
                byName.Add(price);
                var bandKey = (r, BandOf(item.Level), clientSlot);
                if (!_salesByBand.TryGetValue(bandKey, out SaleRing byBand))
                    _salesByBand[bandKey] = byBand = new SaleRing();
                byBand.Add(price);
            }
        }

        /// <summary>The median of the recent sales of this name, or 0.</summary>
        public long MedianSale(eRealm realm, string name)
        {
            lock (_salesGate)
                return _salesByName.TryGetValue((RealmIndex(realm), (name ?? string.Empty).ToLowerInvariant()), out SaleRing ring) ? ring.Median() : 0;
        }

        /// <summary>The median of the recent sales of this level band and slot bucket, or 0.</summary>
        public long MedianSale(eRealm realm, int level, int clientSlot)
        {
            lock (_salesGate)
                return _salesByBand.TryGetValue((RealmIndex(realm), BandOf(level), clientSlot), out SaleRing ring) ? ring.Median() : 0;
        }
    }
}
