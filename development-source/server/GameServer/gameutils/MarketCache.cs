using System;
using System.Collections.Generic;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS
{
    public static class MarketCache
    {
        private static readonly Logger log = LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static MarketSearchEngine _searchEngine = new();

        public static int ItemCount => _searchEngine.ItemCount;

        public static bool Initialize()
        {
            if (log.IsInfoEnabled)
                log.Info("Building Market Cache...");

            if (_searchEngine != null)
            {
                _searchEngine.Dispose();
                _searchEngine = new();
            }

            try
            {
                WhereClause filterBySlot = DB.Column("SlotPosition").IsGreaterOrEqualTo((int)eInventorySlot.Consignment_First).And(DB.Column("SlotPosition").IsLessOrEqualTo((int)eInventorySlot.Consignment_Last));
                filterBySlot = filterBySlot.Or(DB.Column("OwnerLot").IsEqualTo(RealmExchangeBroker.AlbionOwnerLot))
                    .Or(DB.Column("OwnerLot").IsEqualTo(RealmExchangeBroker.MidgardOwnerLot))
                    .Or(DB.Column("OwnerLot").IsEqualTo(RealmExchangeBroker.HiberniaOwnerLot));
                IList<DbInventoryItem> list = DOLDB<DbInventoryItem>.SelectObjects(filterBySlot);
                // player listing hours and the merchant stock into memory first: the index reads expiries and values
                // without the database, under its write lock
                RealmExchangeExpiry.WarmHours(GameServer.Database);
                ItemValue165.WarmMerchantStock();

                foreach (DbInventoryItem item in list)
                    _searchEngine.AddItem(GameInventoryItem.Create(item));

                RealmExchangeExpiry.Start();
                // the crafter recipe book is read off the game loop now, not on the first crafter bot tick that needs it
                CrafterBots165.Warm();

                if (log.IsInfoEnabled)
                    log.Info($"Market Cache initialized with {_searchEngine.ItemCount} items.");
            }
            catch (Exception ex)
            {
                if (log.IsErrorEnabled)
                    log.Error("Failed to initialize Market Cache.", ex);

                return false;
            }

            return true;
        }

        public static bool AddItem(DbInventoryItem item)
        {
            if (item == null)
                return false;

            if (!_searchEngine.AddItem(item))
            {
                if (log.IsErrorEnabled)
                    log.Error($"Attempted to add duplicate item to Market Cache {item.ObjectId}");

                return false;
            }

            return true;
        }

        public static bool RemoveItem(DbInventoryItem item)
        {
            if (item == null)
                return false;

            return _searchEngine.RemoveItem(item);
        }

        public static bool UpdateItem<TState>(DbInventoryItem item, Action<DbInventoryItem, TState> updateAction, TState state)
        {
            if (item == null)
                return false;

            // There is no elegant way to update an item in the search engine, so we remove and re-add it.

            if (!RemoveItem(item))
                return false;

            updateAction(item, state);
            return AddItem(item);
        }

        public static IEnumerable<DbInventoryItem> SearchItems(in ItemQuery query)
        {
            return _searchEngine.Search(query);
        }

        /// <summary>Visits matching items without copying the result set (the visitor must not change the market).</summary>
        public static void ForEach(in ItemQuery query, Action<DbInventoryItem> visit) => _searchEngine.ForEach(query, visit);

        /// <summary>Whether this exact item is listed in the cache (O(1)).</summary>
        public static bool Contains(DbInventoryItem item) => _searchEngine.Contains(item);

        /// <summary>Reads the Realm Exchange index (MarketBook) under the cache's read lock; the reader must not change the market.</summary>
        public static T ReadBook<T>(Func<MarketBook, T> read) => _searchEngine.ReadBook(read);

        /// <summary>Re-reads an item's facts into the index after a field changed outside UpdateItem: atomic (one write-lock
        /// hold) and only while the item is still listed, so it is safe outside the exchange locks.</summary>
        public static bool Refresh(DbInventoryItem item) => item != null && _searchEngine.Refresh(item);

        public static MarketBook.RealmSummary Summary(eRealm realm) => ReadBook(book => book.Summary(realm));

        /// <summary>The exchange index's change counter (no lock: one int read).</summary>
        public static int BookVersion => _searchEngine.SalesBook.Version;

        /// <summary>A sale at this price, for the recent-sale medians used to reprice stale listings.</summary>
        public static void NoteSale(eRealm realm, DbInventoryItem item, long price) =>
            _searchEngine.SalesBook.NoteSale(realm, item, MarketSearchEngine.GetClientSlot(item), price);

        public static long MedianSale(eRealm realm, string name) => _searchEngine.SalesBook.MedianSale(realm, name);

        public static long MedianSale(eRealm realm, int level, int clientSlot) => _searchEngine.SalesBook.MedianSale(realm, level, clientSlot);

        /// <summary>Whether the index matches the cache (the hourly drift check).</summary>
        public static bool CheckBook(out string note)
        {
            return _searchEngine.CheckBook(out note);
        }
    }
}
