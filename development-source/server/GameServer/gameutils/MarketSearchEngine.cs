using System;
using System.Collections.Generic;
using System.Threading;
using DOL.Database;

namespace DOL.GS
{
    public readonly struct ItemQuery
    {
        public readonly eRealm? Realm { get; init; }
        public readonly int? Slot { get; init; }
        public readonly bool? IsCrafted { get; init; }
        public readonly bool? HasVisual { get; init; }
        public readonly string Owner { get; init; }

        public bool HasAny =>
            Realm.HasValue ||
            Slot.HasValue ||
            IsCrafted.HasValue ||
            HasVisual.HasValue ||
            !string.IsNullOrEmpty(Owner);
    }

    public class MarketSearchEngine : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly Dictionary<DbInventoryItem, IndexKeys> _itemKeyCache = new();
        private readonly Dictionary<eRealm, HashSet<DbInventoryItem>> _byRealm = new();
        private readonly Dictionary<int, HashSet<DbInventoryItem>> _bySlot = new();
        private readonly Dictionary<bool, HashSet<DbInventoryItem>> _byCrafted = new();
        private readonly Dictionary<bool, HashSet<DbInventoryItem>> _byVisual = new();
        private readonly Dictionary<string, HashSet<DbInventoryItem>> _byOwner = new();

        /// <summary>The Realm Exchange index kept in step with this cache (changed only under this engine's write lock).</summary>
        private readonly MarketBook _book = new();

        public int ItemCount
        {
            get
            {
                _lock.EnterReadLock();

                try
                {
                    return _itemKeyCache.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        public bool AddItem(DbInventoryItem item)
        {
            _lock.EnterWriteLock();

            try
            {
                IndexKeys keys = GetIndexKeys(item);

                if (!_itemKeyCache.TryAdd(item, keys))
                    return false;

                AddToIndex(_byRealm, keys.Realm, item);
                AddToIndex(_bySlot, keys.Slot, item);
                AddToIndex(_byCrafted, keys.IsCrafted, item);
                AddToIndex(_byVisual, keys.HasVisual, item);
                AddToIndex(_byOwner, keys.Owner, item);
                _book.Add(item, keys.Slot);
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool RemoveItem(DbInventoryItem item)
        {
            _lock.EnterWriteLock();

            try
            {
                if (!_itemKeyCache.Remove(item, out IndexKeys keys))
                    return false;

                RemoveFromIndex(_byRealm, keys.Realm, item);
                RemoveFromIndex(_bySlot, keys.Slot, item);
                RemoveFromIndex(_byCrafted, keys.IsCrafted, item);
                RemoveFromIndex(_byVisual, keys.HasVisual, item);
                RemoveFromIndex(_byOwner, keys.Owner, item);
                _book.Remove(item);
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IEnumerable<DbInventoryItem> Search(in ItemQuery query)
        {
            _lock.EnterReadLock();

            try
            {
                List<HashSet<DbInventoryItem>> resultSets = GetMatchingSets(query);

                if (resultSets == null || resultSets.Count == 0)
                    return query.HasAny ? [] : [.. _itemKeyCache.Keys];

                // Find the smallest set to start with.
                HashSet<DbInventoryItem> smallestSet = resultSets[0];
                int minCount = smallestSet.Count;

                for (int i = 1; i < resultSets.Count; i++)
                {
                    HashSet<DbInventoryItem> set = resultSets[i];

                    if (set.Count < minCount)
                    {
                        minCount = set.Count;
                        smallestSet = set;
                    }
                }

                if (smallestSet.Count == 0)
                    return [];

                // Intersect all other sets with the smallest set.
                HashSet<DbInventoryItem> finalResult = [.. smallestSet];

                for (int i = 0; i < resultSets.Count; i++)
                {
                    if (resultSets[i] == smallestSet)
                        continue;

                    finalResult.IntersectWith(resultSets[i]);

                    if (finalResult.Count == 0)
                        break;
                }

                return finalResult;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>Reads the Realm Exchange index under this engine's read lock. The reader must not change the market.</summary>
        public T ReadBook<T>(Func<MarketBook, T> read)
        {
            _lock.EnterReadLock();

            try
            {
                return read(_book);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>Whether the Realm Exchange index holds exactly the cached exchange listings (under one read lock).</summary>
        public bool CheckBook(out string note)
        {
            _lock.EnterReadLock();

            try
            {
                return _book.Matches(_itemKeyCache.Keys, out note);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>Recent sale prices have their own lock; no engine lock is needed.</summary>
        public MarketBook SalesBook => _book;

        /// <summary>Whether this exact listing is in the cache (O(1), no copy).</summary>
        public bool Contains(DbInventoryItem item)
        {
            if (item == null)
                return false;
            _lock.EnterReadLock();

            try
            {
                return _itemKeyCache.ContainsKey(item);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>Calls <paramref name="visit"/> for each item matching the query without copying the result set first
        /// (the visitor runs under the read lock and must not change the market).</summary>
        public void ForEach(in ItemQuery query, Action<DbInventoryItem> visit)
        {
            _lock.EnterReadLock();

            try
            {
                List<HashSet<DbInventoryItem>> sets = GetMatchingSets(query);
                if (sets == null || sets.Count == 0)
                {
                    if (query.HasAny)
                        return;
                    foreach (DbInventoryItem item in _itemKeyCache.Keys)
                        visit(item);
                    return;
                }
                HashSet<DbInventoryItem> smallest = sets[0];
                foreach (HashSet<DbInventoryItem> set in sets)
                    if (set.Count < smallest.Count)
                        smallest = set;
                foreach (DbInventoryItem item in smallest)
                {
                    bool all = true;
                    foreach (HashSet<DbInventoryItem> set in sets)
                        if (!ReferenceEquals(set, smallest) && !set.Contains(item))
                        {
                            all = false;
                            break;
                        }
                    if (all)
                        visit(item);
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public static eRealm GetRealmOfLot(ushort lot)
        {
            if (lot == RealmExchangeBroker.AlbionOwnerLot) return eRealm.Albion;
            if (lot == RealmExchangeBroker.MidgardOwnerLot) return eRealm.Midgard;
            if (lot == RealmExchangeBroker.HiberniaOwnerLot) return eRealm.Hibernia;
            if (lot > 0)
            {
                if (lot <= 1382)
                    return eRealm.Albion;
                else if (lot <= 2573)
                    return eRealm.Midgard;
                else if (lot <= 4398)
                    return eRealm.Hibernia;
            }

            return eRealm.None;
        }

        private static IndexKeys GetIndexKeys(DbInventoryItem item)
        {
            return new(
                GetRealmOfLot(item.OwnerLot),
                GetClientSlot(item),
                item.IsCrafted,
                item.Effect > 0,
                item.OwnerID
            );
        }

        public static int GetClientSlot(DbInventoryItem item)
        {
            if ((eInventorySlot) item.Item_Type is eInventorySlot.TorsoArmor)
                return 5;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.HeadArmor)
                return 1;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.ArmsArmor)
                return 8;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.HandsArmor)
                return 2;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.LegsArmor)
                return 7;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.FeetArmor)
                return 3;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.Neck)
                return 9;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.Cloak)
                return 6;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.Jewelry)
                return 4;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.Waist)
                return 12;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.RightBracer || (eInventorySlot) item.Item_Type is eInventorySlot.LeftBracer)
                return 13;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.RightRing || (eInventorySlot) item.Item_Type is eInventorySlot.LeftRing)
                return 15;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.RightHandWeapon)
                return 100;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.LeftHandWeapon)
            {
                if ((eObjectType) item.Object_Type is eObjectType.Shield)
                    return 105;

                return 101;
            }

            if ((eInventorySlot) item.Item_Type is eInventorySlot.TwoHandWeapon)
                return 102;

            if ((eInventorySlot) item.Item_Type is eInventorySlot.DistanceWeapon)
            {
                if ((eObjectType) item.Object_Type is eObjectType.Instrument)
                    return 104;

                return 103;
            }

            if ((eObjectType) item.Object_Type is eObjectType.GenericItem)
                return 106;

            return 0;
        }

        private List<HashSet<DbInventoryItem>> GetMatchingSets(in ItemQuery query)
        {
            List<HashSet<DbInventoryItem>> sets = new();

            if (query.Realm.HasValue)
            {
                if (_byRealm.TryGetValue(query.Realm.Value, out var realmSet))
                    sets.Add(realmSet);
                else
                    return null;
            }

            if (query.Slot.HasValue)
            {
                if (_bySlot.TryGetValue(query.Slot.Value, out var slotSet))
                    sets.Add(slotSet);
                else
                    return null;
            }

            if (query.IsCrafted.HasValue)
            {
                if (_byCrafted.TryGetValue(query.IsCrafted.Value, out var craftedSet))
                    sets.Add(craftedSet);
                else
                    return null;
            }

            if (query.HasVisual.HasValue)
            {
                if (_byVisual.TryGetValue(query.HasVisual.Value, out var visualSet))
                    sets.Add(visualSet);
                else
                    return null;
            }

            if (!string.IsNullOrEmpty(query.Owner))
            {
                if (_byOwner.TryGetValue(query.Owner, out var ownerSet))
                    sets.Add(ownerSet);
                else
                    return null;
            }

            return sets;
        }

        private static void AddToIndex<TKey>(Dictionary<TKey, HashSet<DbInventoryItem>> index, TKey key, DbInventoryItem item)
        {
            if (!index.TryGetValue(key, out var set))
            {
                set = new();
                index[key] = set;
            }

            set.Add(item);
        }

        private static void RemoveFromIndex<TKey>(Dictionary<TKey, HashSet<DbInventoryItem>> index, TKey key, DbInventoryItem item)
        {
            if (index.TryGetValue(key, out var set))
            {
                set.Remove(item);

                if (set.Count == 0)
                    index.Remove(key);
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _lock.Dispose();
        }

        private readonly struct IndexKeys
        {
            public readonly eRealm Realm;
            public readonly int Slot;
            public readonly bool IsCrafted;
            public readonly bool HasVisual;
            public readonly string Owner;

            public IndexKeys(eRealm realm, int slot, bool isCrafted, bool hasVisual, string owner)
            {
                Realm = realm;
                Slot = slot;
                IsCrafted = isCrafted;
                HasVisual = hasVisual;
                Owner = owner;
            }
        }
    }
}
