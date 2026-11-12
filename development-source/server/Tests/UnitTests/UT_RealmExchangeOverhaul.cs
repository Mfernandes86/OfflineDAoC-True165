using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The 2026-11-11 Realm Exchange overhaul with real (inert) bots and an in-memory store: the MarketBook against the
    /// cache, the expiry wheel, smart selling (own supplies, copy caps, soft ceiling, the replace rule), the stale cycle,
    /// sale bookkeeping, the startup cleanup, wish-list buying against the old full scan, and the non-blocking locks.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RealmExchangeOverhaul
    {
        private sealed class OverhaulBot : GameBot
        {
            private OverhaulBot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override ICharacterClass CharacterClass => new ClassEnchanter();
            public override void RefreshItemBonuses() { }
        }

        private sealed class OverhaulServer : GameServer
        {
            public IObjectDatabase TestDatabase;
            protected override IObjectDatabase DataBaseImpl => TestDatabase;
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
        }

        /// <summary>Accepts every write and records deletes; selects return the configured rows or nothing. With
        /// <see cref="Realistic"/> it also records adds and saves and sets the persistence flags as the SQL store does
        /// (a delete marks the row deleted and no longer stored); <see cref="FailSaves"/> makes adds and saves fail;
        /// <see cref="ByKey"/> answers FindObjectByKey.</summary>
        public class StoreDatabase : DispatchProxy
        {
            public readonly List<DataObject> Deleted = new();
            public readonly List<DataObject> Added = new();
            public readonly List<DataObject> Saved = new();
            public readonly Dictionary<Type, IList> Rows = new();
            public readonly Dictionary<(Type, string), DataObject> ByKey = new();
            public bool Realistic;
            public bool FailSaves;

            private static IEnumerable<DataObject> RowsOf(object arg) =>
                arg is DataObject row ? new[] { row } : arg as IEnumerable<DataObject> ?? Array.Empty<DataObject>();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(IObjectDatabase.DeleteObject))
                {
                    foreach (DataObject row in RowsOf(args[0]).ToList())
                    {
                        Deleted.Add(row);
                        if (Realistic)
                        {
                            row.IsPersisted = false;
                            row.IsDeleted = true;
                        }
                    }
                    return true;
                }
                if (Realistic && method.Name is nameof(IObjectDatabase.AddObject) or nameof(IObjectDatabase.SaveObject))
                {
                    foreach (DataObject row in RowsOf(args[0]).ToList())
                    {
                        (method.Name == nameof(IObjectDatabase.AddObject) ? Added : Saved).Add(row);
                        if (FailSaves)
                            continue;
                        row.IsPersisted = true;
                        row.IsDeleted = false;
                        row.Dirty = false;
                    }
                    return !FailSaves;
                }
                if (method.Name == nameof(IObjectDatabase.FindObjectByKey) && method.IsGenericMethod)
                    return ByKey.TryGetValue((method.GetGenericArguments()[0], args[0]?.ToString()), out DataObject found) ? found : null;
                if (method.Name is nameof(IObjectDatabase.SelectObjects) or nameof(IObjectDatabase.SelectAllObjects))
                {
                    Type type = method.GetGenericArguments()[0];
                    IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
                    if (Rows.TryGetValue(type, out IList rows))
                        foreach (object row in rows)
                            list.Add(row);
                    return list;
                }
                if (method.Name == nameof(IObjectDatabase.MultipleSelectObjects))
                {
                    Type itemType = method.GetGenericArguments()[0];
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(IList<>).MakeGenericType(itemType)));
                }
                if (method.ReturnType == typeof(bool)) return true;
                if (method.ReturnType == typeof(int)) return 0;
                return null;
            }
        }

        private GameServer _previousServer;
        private MarketSearchEngine _previousMarket;
        private StoreDatabase _store;
        private int _previousSellRatio;
        private bool _previousMode;
        private static long _nextId = 930_000;
        private readonly List<GameBot> _bots = new();

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            _previousSellRatio = Properties.ITEM_SELL_RATIO;
            _previousMode = Properties.CLASSIC165_MODE;
            Properties.ITEM_SELL_RATIO = 50;
            Properties.CLASSIC165_MODE = false;
            IObjectDatabase database = DispatchProxy.Create<IObjectDatabase, StoreDatabase>();
            _store = (StoreDatabase)(object)database;
            OverhaulServer server = (OverhaulServer)RuntimeHelpers.GetUninitializedObject(typeof(OverhaulServer));
            server.TestDatabase = database;
            GameServer.LoadTestDouble(server);
            FieldInfo market = typeof(MarketCache).GetField("_searchEngine", BindingFlags.Static | BindingFlags.NonPublic);
            _previousMarket = (MarketSearchEngine)market.GetValue(null);
            market.SetValue(null, new MarketSearchEngine());
            RealmExchangeWriteQueue.Clear();
            RealmExchangeWriteQueue.Enabled = false;
            Exchange165.LoadRelists(database, new HashSet<string>());
            CrafterBots165.ResetForTests();
            // the one-second persistence timer stays out of these tests (it would flush the exchange queue mid-test)
            FlushingField.SetValue(null, 1);
            RealmExchangeStats.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameBot bot in _bots)
                AutonomousBotRegistry.Unregister(bot);
            _bots.Clear();
            FieldInfo market = typeof(MarketCache).GetField("_searchEngine", BindingFlags.Static | BindingFlags.NonPublic);
            ((MarketSearchEngine)market.GetValue(null)).Dispose();
            market.SetValue(null, _previousMarket);
            RealmExchangeWriteQueue.Enabled = true;
            RealmExchangeWriteQueue.Clear();
            Exchange165.LoadRelists(null, new HashSet<string>());
            CrafterBots165.ResetForTests();
            FlushingField.SetValue(null, 0);
            ItemValue165.SetMerchantStockForTests(null);
            Properties.ITEM_SELL_RATIO = _previousSellRatio;
            Properties.CLASSIC165_MODE = _previousMode;
            GameServer.LoadTestDouble(_previousServer);
        }

        private static readonly FieldInfo FlushingField =
            typeof(AutonomousBotStatusPersistence).GetField("_flushing", BindingFlags.Static | BindingFlags.NonPublic);

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private OverhaulBot Bot(int level = 20, long money = 1_000_000, bool betweenTasks = true)
        {
            OverhaulBot bot = (OverhaulBot)RuntimeHelpers.GetUninitializedObject(typeof(OverhaulBot));
            Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.Level = (byte)level;
            bot.Realm = eRealm.Hibernia;
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.Name = "Overhaul bot " + bot.DatabaseID;
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            bot.ObjectState = GameObject.eObjectState.Active;
            bot.Inventory = new BotInventory(bot.InternalID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            Field(typeof(GameLiving), bot, "_abilitiesLock", new Lock());
            Field(typeof(GameLiving), bot, "m_abilities", new Dictionary<string, Ability>
            {
                [Abilities.HibArmor] = new Ability(new DbAbility { KeyName = Abilities.HibArmor, Name = "Cloth" }, 1)
            });
            Field(typeof(GameBot), bot, "<PersistentRecord>k__BackingField", new OfflineWorldBotRecord
            {
                BotId = bot.DatabaseID, Realm = (int)eRealm.Hibernia, Level = level, MoneyCopper = money, CraftSkill = -1,
                ObjectiveAssignmentId = betweenTasks ? "between-pve-services-I-" + bot.DatabaseID + "-0" : "ordinary-pve-" + bot.DatabaseID,
            });
            AutonomousBotRegistry.Register(bot);
            _bots.Add(bot);
            return bot;
        }

        private static DbItemTemplate Template(string name, int level, int quality, eInventorySlot slot, int price,
            eObjectType type = eObjectType.Cloth, int dpsAf = 10) => new()
        {
            Id_nb = "ovh-" + Guid.NewGuid(), Name = name, Realm = (int)eRealm.Hibernia, Level = level,
            Item_Type = (int)slot, Object_Type = (int)type, Quality = quality, MaxCount = 1, PackSize = 1,
            MaxCondition = 10000, MaxDurability = 10000, DPS_AF = dpsAf, Price = price, IsDropable = true, IsTradable = true,
        };

        private static DbInventoryItem Item(string name, int level, int quality, eInventorySlot slot, int price,
            eObjectType type = eObjectType.Cloth, int dpsAf = 10) =>
            GameInventoryItem.Create(Template(name, level, quality, slot, price, type, dpsAf));

        private static DbInventoryItem Valuable(string name = "valuable cloth robe") =>
            Item(name, 20, 99, eInventorySlot.FirstBackpack, 10_000, eObjectType.Cloth, 30);

        private static DbInventoryItem AddBackpack(GameBot bot, DbInventoryItem item)
        {
            Assert.That(bot.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item), Is.True);
            return item;
        }

        private static void FillBackpack(GameBot bot)
        {
            while (bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) != eInventorySlot.Invalid)
                AddBackpack(bot, Item("filler rag", 1, 80, eInventorySlot.FirstBackpack, 2));
        }

        private static DbInventoryItem List(DbInventoryItem item, string owner, int slotIndex, int price, DateTime? listedUtc = null,
            ushort lot = RealmExchangeBroker.HiberniaOwnerLot)
        {
            item.OwnerID = owner;
            item.OwnerLot = lot;
            item.SlotPosition = (int)eInventorySlot.Consignment_First + slotIndex;
            item.SellPrice = price;
            item.RealmExchangeListedUtc = (listedUtc ?? DateTime.UtcNow).ToString("O");
            Assert.That(MarketCache.AddItem(item), Is.True);
            return item;
        }

        // ---- the index

        [Test]
        public void MarketBook_StaysIdenticalToTheCache_AfterRandomAddsRemovesAndUpdates()
        {
            var random = new Random(1165);
            var listed = new List<DbInventoryItem>();
            string[] owners = Enumerable.Range(0, 12).Select(i => i < 9 ? AutonomousBotEconomy.GetOwnerId(800_000 + i) : "player-" + i).ToArray();
            ushort[] lots = [RealmExchangeBroker.AlbionOwnerLot, RealmExchangeBroker.MidgardOwnerLot, RealmExchangeBroker.HiberniaOwnerLot, 7];
            string[] names = ["tunic", "sword", "arrows", "ring", "cloak"];
            for (int step = 0; step < 600; step++)
            {
                int op = random.Next(10);
                if (op < 5 || listed.Count == 0)
                {
                    DbInventoryItem item = Item(names[random.Next(names.Length)], random.Next(1, 51), 90,
                        (eInventorySlot)new[] { 25, 21, 22, 10, 40 }[random.Next(5)], random.Next(1, 5_000));
                    string owner = owners[random.Next(owners.Length)];
                    int slot = Enumerable.Range(0, 100).First(s => listed.All(l => l.OwnerID != owner || l.SlotPosition != (int)eInventorySlot.Consignment_First + s));
                    List(item, owner, slot, random.Next(0, 9_000), DateTime.UtcNow.AddHours(-random.Next(0, 30)), lots[random.Next(lots.Length)]);
                    listed.Add(item);
                }
                else if (op < 8)
                {
                    DbInventoryItem item = listed[random.Next(listed.Count)];
                    Assert.That(MarketCache.RemoveItem(item), Is.True);
                    listed.Remove(item);
                }
                else
                {
                    DbInventoryItem item = listed[random.Next(listed.Count)];
                    Assert.That(MarketCache.UpdateItem(item, static (i, p) => { i.SellPrice = p; i.RealmExchangeListedUtc = DateTime.UtcNow.ToString("O"); }, random.Next(1, 9_000)), Is.True);
                }
            }

            Assert.That(MarketCache.CheckBook(out string note), Is.True, note);
            foreach (string owner in owners)
            {
                DbInventoryItem[] own = listed.Where(i => i.OwnerID == owner && (i.OwnerLot is RealmExchangeBroker.AlbionOwnerLot or RealmExchangeBroker.MidgardOwnerLot or RealmExchangeBroker.HiberniaOwnerLot)).ToArray();
                Assert.That(MarketCache.ReadBook(b => b.OwnerCount(owner)), Is.EqualTo(own.Length));
                int expectedSlot = Enumerable.Range((int)eInventorySlot.Consignment_First, 100).First(s => own.All(i => i.SlotPosition != s));
                Assert.That(MarketCache.ReadBook(b => b.FreeSlot(owner)), Is.EqualTo(expectedSlot));
            }
            foreach (string name in names)
            {
                int botCopies = listed.Count(i => i.Name == name && i.OwnerLot == RealmExchangeBroker.HiberniaOwnerLot &&
                                                  AutonomousBotEconomy.TryParseOwnerId(i.OwnerID, out _));
                Assert.That(MarketCache.ReadBook(b => b.Name(eRealm.Hibernia, name, null)).BotCopies, Is.EqualTo(botCopies));
            }
            Assert.That(MarketCache.Summary(eRealm.Hibernia).BotListings + MarketCache.Summary(eRealm.Hibernia).PlayerListings,
                Is.EqualTo(listed.Count(i => i.OwnerLot == RealmExchangeBroker.HiberniaOwnerLot)));
        }

        [Test]
        public void ExpiryWheel_HandsOutOnlyDueListings()
        {
            DateTime now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
            string owner = AutonomousBotEconomy.GetOwnerId(810_001);
            DbInventoryItem due = List(Item("due", 5, 90, eInventorySlot.TorsoArmor, 100), owner, 0, 500, now.AddHours(-24).AddMinutes(-1));
            DbInventoryItem young = List(Item("young", 5, 90, eInventorySlot.TorsoArmor, 100), owner, 1, 500, now.AddHours(-23));
            List<DbInventoryItem> popped = MarketCache.ReadBook(b => b.Due(now, 200));
            Assert.That(popped, Does.Contain(due));
            Assert.That(popped, Does.Not.Contain(young));
        }

        // ---- smart selling

        [Test]
        public void NeverListsItsOwnArrows_ButListsThemWithoutTheBow()
        {
            OverhaulBot bot = Bot();
            DbInventoryItem arrows = Item("fine broadhead arrows", 20, 99, eInventorySlot.FirstBackpack, 40_000, eObjectType.Arrow);
            AddBackpack(bot, arrows);
            FillBackpack(bot);
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(bot)?.Item, Is.SameAs(arrows), "anyone else's arrows are stock");

            DbInventoryItem bow = Item("test longbow", 20, 90, eInventorySlot.DistanceWeapon, 100, eObjectType.Longbow);
            Assert.That(bot.Inventory.AddItem(eInventorySlot.DistanceWeapon, bow), Is.True);
            Assert.That(AutonomousBotEconomy.IsOwnSupply(bot, arrows), Is.True);
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(bot)?.Item, Is.Not.SameAs(arrows));
        }

        [Test]
        public void CopyLimit_ListsOnlyByUndercuttingTheDearestCopy_WhichIsBumped()
        {
            OverhaulBot bot = Bot();
            DbInventoryItem mine = AddBackpack(bot, Valuable("crowded robe"));
            FillBackpack(bot);
            long ask = ItemValue165.Ask(mine, 0);
            for (int i = 0; i < AutonomousBotEconomy.MaxRealmBotCopies; i++)
                List(Valuable("crowded robe"), AutonomousBotEconomy.GetOwnerId(820_000 + i), 0, (int)(ask * 3) + i);

            AutonomousBotEconomy.ListingCandidate plan = AutonomousBotEconomy.FindValuableListingCandidate(bot);
            Assert.That(plan?.Item, Is.SameAs(mine));
            Assert.That(plan.Bump, Is.Not.Null);
            Assert.That(plan.Bump.Price, Is.EqualTo(ask * 3 + AutonomousBotEconomy.MaxRealmBotCopies - 1), "the dearest copy is bumped");
            Assert.That(plan.PriceCopper, Is.LessThan(plan.Bump.Price));

            // six copies cheaper than anything it could ask: it goes to a merchant
            foreach (DbInventoryItem copy in MarketCache.SearchItems(new ItemQuery { Realm = eRealm.Hibernia }).ToArray())
                MarketCache.UpdateItem(copy, static (i, p) => i.SellPrice = p, 1);
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(bot)?.Item, Is.Not.SameAs(mine));
        }

        [Test]
        public void ABotListsAtMostTwoCopiesOfOneItem()
        {
            OverhaulBot bot = Bot(level: 35);
            for (int i = 0; i < AutonomousBotEconomy.MaxBotCopiesPerName; i++)
                List(Valuable("twin robe"), bot.InternalID, i, 900_000);
            DbInventoryItem third = AddBackpack(bot, Valuable("twin robe"));
            FillBackpack(bot);
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(bot)?.Item, Is.Not.SameAs(third));
        }

        [Test]
        public void SoftCeiling_AllowsOnlyAReplacement()
        {
            OverhaulBot bot = Bot(level: 20);
            for (int i = 0; i < AutonomousBotEconomy.RealmSoftCeiling; i++)
                List(Item("ceiling filler " + i % 500, 1, 80, eInventorySlot.FirstBackpack, 2), AutonomousBotEconomy.GetOwnerId(830_000 + i / 3), i % 3, 3);
            DbInventoryItem good = AddBackpack(bot, Valuable("ceiling robe"));
            FillBackpack(bot);
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(bot), Is.Null, "nothing of its own to replace");

            DbInventoryItem weak = List(Item("weak old listing", 12, 85, eInventorySlot.TorsoArmor, 400), bot.InternalID, 0, 500,
                DateTime.UtcNow.AddHours(-3));
            AutonomousBotEconomy.ListingCandidate plan = AutonomousBotEconomy.FindValuableListingCandidate(bot);
            Assert.That(plan?.Item, Is.SameAs(good));
            Assert.That(plan.Replace?.Item, Is.SameAs(weak));
        }

        [Test]
        public void ReplaceRule_NeedsOnePointFiveTimesTheGainAndTwoHours_AndSwapsOncePerVisit()
        {
            OverhaulBot bot = Bot(level: 15);
            DbInventoryItem[] weak = Enumerable.Range(0, 3)
                .Select(i => List(Item("weak listing " + i, 12, 85, eInventorySlot.TorsoArmor, 400 + i), bot.InternalID, i, 500 + i,
                    DateTime.UtcNow.AddMinutes(-60)))
                .ToArray();
            DbInventoryItem good = AddBackpack(bot, Valuable("swap robe"));
            FillBackpack(bot);
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(bot), Is.Null, "listings under two hours old stay");

            foreach (DbInventoryItem listing in weak)
                MarketCache.UpdateItem(listing, static (i, t) => i.RealmExchangeListedUtc = t, DateTime.UtcNow.AddHours(-3).ToString("O"));
            AutonomousBotEconomy.ListingCandidate plan = AutonomousBotEconomy.FindValuableListingCandidate(bot);
            Assert.That(plan?.Replace, Is.Not.Null);
            Assert.That(plan.Replace.Item, Is.SameAs(weak.OrderBy(i => ItemValue165.Gain(i)).First()));

            Assert.That(AutonomousBotEconomy.TryListPlanned(bot, plan, plan.PriceCopper, out bool busy), Is.True);
            Assert.That(busy, Is.False);
            DbInventoryItem pulled = plan.Replace.Item;
            Assert.That(good.OwnerLot, Is.EqualTo(RealmExchangeBroker.HiberniaOwnerLot));
            Assert.That(bot.Inventory.AllItems, Does.Contain(pulled));
            Assert.That(Exchange165.HasFailed(pulled), Is.True, "the pulled item is sold to a merchant, never listed again");
            Assert.That(AutonomousBotEconomy.SwappedThisVisit(bot), Is.True);
            Assert.That(RealmExchangeStats.Read(eRealm.Hibernia, RealmExchangeStats.Counter.Swapped), Is.EqualTo(1));

            AddBackpackOverFiller(bot, Valuable("second swap robe"));
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(bot), Is.Null, "one swap per visit");
        }

        private static void AddBackpackOverFiller(GameBot bot, DbInventoryItem item)
        {
            DbInventoryItem filler = bot.Inventory.AllItems.First(i => i.Name == "filler rag");
            Assert.That(bot.Inventory.RemoveItemWithoutDbDeletion(filler), Is.True);
            AddBackpack(bot, item);
        }

        [Test]
        public void MarketPass_DrainsAWallOfEqualCopiesDownToTheCopyLimit_BackToTheirOwners()
        {
            OverhaulBot[] sellers = Enumerable.Range(0, 9).Select(_ => Bot()).ToArray();
            foreach (OverhaulBot seller in sellers)
                List(Valuable("wall robe"), seller.InternalID, 0, 40_000);
            for (int pass = 0; pass < 5; pass++)
                RealmMarketPass.RunRealm(eRealm.Hibernia, DateTime.UtcNow);
            Assert.That(MarketCache.ReadBook(b => b.Name(eRealm.Hibernia, "wall robe", null)).BotCopies,
                Is.EqualTo(AutonomousBotEconomy.MaxRealmBotCopies));
            Assert.That(sellers.Count(s => s.Inventory.AllItems.Any(i => i.Name == "wall robe" && Exchange165.HasFailed(i))), Is.EqualTo(3));
            Assert.That(RealmExchangeStats.Read(eRealm.Hibernia, RealmExchangeStats.Counter.Bumped), Is.EqualTo(3));
        }

        // ---- the stale cycle

        [Test]
        public void SecondExpiry_WithAFullBag_PaysTheMerchantValue_NeverDeletesForNothing()
        {
            OverhaulBot bot = Bot(money: 1_000);
            FillBackpack(bot);
            DbInventoryItem listing = List(Valuable("stale robe"), bot.InternalID, 0, 50_000, DateTime.UtcNow.AddHours(-37));
            _store.Rows[typeof(OfflineTrue165RelistRecord)] = new List<OfflineTrue165RelistRecord>
                { new() { InventoryId = listing.ObjectId, Tries = Exchange165.TriesRelisted, OriginalPrice = 60_000, IsPersisted = true } };
            Exchange165.LoadRelists(GameServer.Database, new HashSet<string> { listing.ObjectId });
            long floor = ItemValue165.Floor(listing);

            Assert.That(Exchange165.HandleExpired(listing, bot.DatabaseID, DateTime.UtcNow), Is.True);
            Assert.That(bot.PersistentRecord.MoneyCopper, Is.EqualTo(1_000 + floor));
            Assert.That(_store.Deleted, Does.Contain(listing));
            Assert.That(MarketCache.Contains(listing), Is.False);
        }

        [Test]
        public void SecondExpiry_WithRoom_ReturnsTheItemMarkedFailed_FirstExpiryRelistsCheaperForTwelveHours()
        {
            OverhaulBot bot = Bot();
            DateTime now = DateTime.UtcNow;
            DbInventoryItem listing = List(Valuable("expiring robe"), bot.InternalID, 0, 50_000, now.AddHours(-24));

            Assert.That(Exchange165.HandleExpired(listing, bot.DatabaseID, now), Is.True);
            Assert.That(listing.SellPrice, Is.LessThanOrEqualTo(35_000));
            Assert.That(RealmExchangeExpiry.ExpiresUtc(listing), Is.EqualTo(now.AddHours(12)).Within(TimeSpan.FromSeconds(1)));
            Assert.That(MarketCache.ReadBook(b => b.Find(listing)).ExpiresTicks, Is.EqualTo(RealmExchangeExpiry.ExpiresUtc(listing).Value.Ticks));

            Assert.That(Exchange165.HandleExpired(listing, bot.DatabaseID, now.AddHours(12)), Is.True);
            Assert.That(bot.Inventory.AllItems, Does.Contain(listing));
            Assert.That(listing.OwnerLot, Is.Zero);
            Assert.That(Exchange165.HasFailed(listing), Is.True);
            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(bot)?.Item, Is.Not.SameAs(listing), "never listed again");
        }

        // ---- sale bookkeeping and startup cleanup

        [Test]
        public void Sale_DeletesTheRelistRow()
        {
            OverhaulBot seller = Bot(money: 0);
            OverhaulBot buyer = Bot(money: 100_000);
            DbInventoryItem listing = List(Valuable("sold robe"), seller.InternalID, 0, 5_000);
            var record = new OfflineTrue165RelistRecord { InventoryId = listing.ObjectId, Tries = Exchange165.TriesRelisted, OriginalPrice = 7_000, IsPersisted = true };
            _store.Rows[typeof(OfflineTrue165RelistRecord)] = new List<OfflineTrue165RelistRecord> { record };
            Exchange165.LoadRelists(GameServer.Database, new HashSet<string> { listing.ObjectId });
            Assert.That(Exchange165.RelistOf(listing), Is.SameAs(record));

            Assert.That(AutonomousBotEconomy.TryBuy(buyer, listing), Is.True);
            Assert.That(_store.Deleted, Does.Contain(record));
            Assert.That(Exchange165.RelistOf(listing), Is.Null);
            Assert.That(seller.PersistentRecord.MoneyCopper, Is.EqualTo(5_000));
        }

        [Test]
        public void StartupCleanup_RemovesOnlyOrphanBookkeepingRows()
        {
            var listed = new OfflineTrue165RelistRecord { InventoryId = "listed-item", Tries = 1, IsPersisted = true };
            var orphan = new OfflineTrue165RelistRecord { InventoryId = "sold-item", Tries = 1, IsPersisted = true };
            var liveHours = new OfflineTrue165ListingHours { InventoryId = "listed-player-item", Hours = 24, IsPersisted = true };
            var orphanHours = new OfflineTrue165ListingHours { InventoryId = "gone-player-item", Hours = 24, IsPersisted = true };
            _store.Rows[typeof(OfflineTrue165RelistRecord)] = new List<OfflineTrue165RelistRecord> { listed, orphan };
            _store.Rows[typeof(OfflineTrue165ListingHours)] = new List<OfflineTrue165ListingHours> { liveHours, orphanHours };

            int removed = RealmExchangeExpiry.CleanBookkeeping(GameServer.Database,
                new HashSet<string> { "listed-item", "listed-player-item" });
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(_store.Deleted, Is.EquivalentTo(new DataObject[] { orphan, orphanHours }));
            Assert.That(_store.Deleted.OfType<DbInventoryItem>(), Is.Empty, "never a listing");
        }

        // ---- smart buying

        [Test]
        public void WishListProbe_PicksTheSameUpgradeAsTheFullScan_AndTheEquipStepAgrees()
        {
            OverhaulBot buyer = Bot(level: 20, money: 500_000);
            Assert.That(buyer.Inventory.AddItem(eInventorySlot.TorsoArmor, Item("worn vest", 10, 85, eInventorySlot.TorsoArmor, 100)), Is.True);
            string seller = Bot(level: 30, money: 0).InternalID;
            int slot = 0;
            foreach ((string name, int level, eInventorySlot itemSlot, int price) in new[]
                     {
                         ("cheap low gloves", 3, eInventorySlot.HandsArmor, 200), ("fine vest", 19, eInventorySlot.TorsoArmor, 20_000),
                         ("better vest", 21, eInventorySlot.TorsoArmor, 30_000), ("too high vest", 30, eInventorySlot.TorsoArmor, 30_000),
                         ("good boots", 18, eInventorySlot.FeetArmor, 9_000), ("unusable junk", 1, eInventorySlot.FirstBackpack, 5),
                     })
            {
                DbInventoryItem item = Item(name, level, 96, itemSlot, 500);
                List(item, seller, slot++, price);
            }
            AutonomousBotEconomy.PurchaseCandidate probe = AutonomousBotEconomy.FindBestUsefulExchangePurchase(buyer);
            AutonomousBotEconomy.PurchaseCandidate scan = AutonomousBotEconomy.FindBestUsefulExchangePurchaseFullScan(buyer);
            Assert.That(probe, Is.Not.Null);
            Assert.That(probe.Item, Is.SameAs(scan?.Item));
            Assert.That(probe.Item.Level, Is.LessThanOrEqualTo(buyer.Level + AutonomousBotEconomy.PurchaseLevelsAbove));

            Assert.That(AutonomousBotEconomy.TryBuy(buyer, probe.Item), Is.True);
            Assert.That(AutonomousBotEconomy.TryEquipPurchasedUpgrade(buyer, probe), Is.True, "what the scorer buys, the equip step wears");
        }

        [Test]
        public void Buyer_KeepsItsCoinReserve_AndSkipsOverpricedBotListings()
        {
            OverhaulBot buyer = Bot(level: 20, money: AutonomousWorldBotController.ShoppingReserve(20) + 100);
            DbInventoryItem legs = Item("affordable legs", 18, 96, eInventorySlot.LegsArmor, 500);
            List(legs, AutonomousBotEconomy.GetOwnerId(850_000), 0, 150);
            Assert.That(AutonomousBotEconomy.FindBestUsefulExchangePurchase(buyer), Is.Null, "150 copper would eat into the reserve");

            OverhaulBot rich = Bot(level: 20, money: 10_000_000);
            MarketCache.UpdateItem(legs, static (i, p) => i.SellPrice = p, (int)(ItemValue165.Fair(legs) * 2));
            Assert.That(AutonomousBotEconomy.FindBestUsefulExchangePurchase(rich), Is.Null, "over 1.5 x its fair value");
            MarketCache.UpdateItem(legs, static (i, p) => i.SellPrice = p, (int)(ItemValue165.Fair(legs) / 2));
            Assert.That(AutonomousBotEconomy.FindBestUsefulExchangePurchase(rich)?.Item, Is.SameAs(legs));
        }

        [Test]
        public void TrueMode_BotsBuyFromLevelFive_SellFromLevelTen()
        {
            Properties.CLASSIC165_MODE = true;
            Assert.That(AutonomousBotEconomy.CanBuyOnExchange(Bot(level: 4)), Is.False);
            OverhaulBot young = Bot(level: 7);
            Assert.That(AutonomousBotEconomy.CanBuyOnExchange(young), Is.True);
            Assert.That(AutonomousBotEconomy.CanSellOnExchange(young), Is.False);
            OverhaulBot seller = Bot(level: 10);
            Assert.That(AutonomousBotEconomy.CanBuyOnExchange(seller) && AutonomousBotEconomy.CanSellOnExchange(seller), Is.True);
        }

        // ---- review fixes (2026-11-11)

        [Test]
        public void QueuedSave_OfACreditedListing_IsNeverInsertedAgain()
        {
            _store.Realistic = true;
            RealmExchangeWriteQueue.Enabled = true;
            DateTime now = DateTime.UtcNow;
            long offlineOwner = 870_001;
            DbInventoryItem listing = List(Valuable("credited robe"), AutonomousBotEconomy.GetOwnerId(offlineOwner), 0, 50_000, now.AddHours(-13));
            listing.IsPersisted = true;

            Assert.That(Exchange165.TryUndercut(listing, now), Is.True, "12 h unsold: re-undercut");
            Assert.That(RealmExchangeWriteQueue.IsQueued(listing), Is.True);
            // 24 h: the owner is offline, so it is paid out and the row deleted before the queue is written
            Assert.That(Exchange165.HandleExpired(listing, offlineOwner, now.AddHours(11)), Is.True);
            Assert.That(_store.Deleted, Does.Contain(listing));
            Assert.That(RealmExchangeWriteQueue.IsQueued(listing), Is.False, "the credit cancels the queued reprice");
            RealmExchangeWriteQueue.Save(listing);   // a late save of the deleted row is ignored too
            RealmExchangeWriteQueue.Flush();
            Assert.That(_store.Added, Does.Not.Contain(listing), "never inserted again: no live listing at the next start");
            Assert.That(_store.Saved, Does.Not.Contain(listing));
            Assert.That(MarketCache.Contains(listing), Is.False);
        }

        [Test]
        public void WriteQueue_RelistRowOfAStoredKeyUpdates_AndAWriteThatKeepsFailingIsDropped()
        {
            _store.Realistic = true;
            RealmExchangeWriteQueue.Enabled = true;
            var record = new OfflineTrue165RelistRecord { InventoryId = "stored-key", Tries = Exchange165.TriesFailed };
            _store.ByKey[(typeof(OfflineTrue165RelistRecord), "stored-key")] =
                new OfflineTrue165RelistRecord { InventoryId = "stored-key", IsPersisted = true };
            RealmExchangeWriteQueue.Save(record);
            RealmExchangeWriteQueue.Flush();
            Assert.That(_store.Added, Does.Not.Contain(record), "its key is stored: an update, not a failing insert");
            Assert.That(_store.Saved, Does.Contain(record));

            _store.FailSaves = true;
            record.Dirty = true;
            RealmExchangeWriteQueue.Save(record);
            for (int attempt = 1; attempt < RealmExchangeWriteQueue.MaxAttempts; attempt++)
                RealmExchangeWriteQueue.Flush();
            Assert.That(RealmExchangeWriteQueue.Count, Is.EqualTo(1), "retried while under the attempt limit");
            RealmExchangeWriteQueue.Flush();
            Assert.That(RealmExchangeWriteQueue.Count, Is.Zero, "dropped, not retried every second forever");
        }

        [Test]
        public void MarketPass_LeavesAnExpiredListingToTheSweep()
        {
            OverhaulBot bot = Bot();
            DateTime now = DateTime.UtcNow;
            DbInventoryItem listing = List(Valuable("expired robe"), bot.InternalID, 0, 50_000, now.AddHours(-25));
            RealmMarketPass.RunRealm(eRealm.Hibernia, now);
            Assert.That(listing.SellPrice, Is.EqualTo(50_000));
            Assert.That(Exchange165.RelistOf(listing), Is.Null);
        }

        [Test]
        public void Archive_TakesRelistRecordsOutOfMemory_AndARestoreAdoptsThem()
        {
            RealmExchangeWriteQueue.Enabled = true;
            OverhaulBot bot = Bot();
            DbInventoryItem item = AddBackpack(bot, Valuable("failed robe"));
            Exchange165.MarkFailed(item, 9_000);
            OfflineTrue165RelistRecord record = Exchange165.RelistOf(item);
            Assert.That(RealmExchangeWriteQueue.IsQueued(record), Is.True);

            List<OfflineTrue165RelistRecord> taken = Exchange165.Detach(new[] { item.ObjectId });
            Assert.That(taken, Is.EquivalentTo(new[] { record }));
            Assert.That(Exchange165.HasFailed(item), Is.False);
            Assert.That(RealmExchangeWriteQueue.IsQueued(record), Is.False, "its row goes with the archived bot");

            var restored = new OfflineTrue165RelistRecord { InventoryId = item.ObjectId, Tries = Exchange165.TriesFailed, IsPersisted = true };
            Assert.That(Exchange165.Adopt(new[] { restored }), Is.EqualTo(1));
            Assert.That(Exchange165.HasFailed(item), Is.True, "a restored bot never lists its failed items again");
            Assert.That(RealmExchangeWriteQueue.IsQueued(restored), Is.False, "already stored");
        }

        [Test]
        public void Crafter_KeepsOnlyWhatItsNextRecipesUse_ASurplusStackMayGo()
        {
            Properties.CLASSIC165_MODE = true;
            _store.Rows[typeof(DbCraftedXItem)] = new List<DbCraftedXItem>
                { new() { CraftedItemId_nb = "ovh-product", IngredientId_nb = "ovh-cloth", Count = 2 } };
            _store.Rows[typeof(DbCraftedItem)] = new List<DbCraftedItem>
                { new() { Id_nb = "ovh-product", CraftingLevel = 1, CraftingSkillType = (int)eCraftingSkill.Tailoring } };
            _store.ByKey[(typeof(DbItemTemplate), "ovh-product")] = new DbItemTemplate { Id_nb = "ovh-product", Price = 100 };
            _store.ByKey[(typeof(DbItemTemplate), "ovh-cloth")] = new DbItemTemplate { Id_nb = "ovh-cloth", Price = 10 };
            OverhaulBot bot = Bot();
            bot.PersistentRecord.CraftSkill = (int)eCraftingSkill.Tailoring;
            bot.PersistentRecord.CraftLevel = 1;

            int wanted = CrafterBots165.WantedMaterial(bot.PersistentRecord, eRealm.Hibernia, "ovh-cloth");
            Assert.That(wanted, Is.EqualTo(2 * CrafterBots165.MaterialCombinesKept));
            Assert.That(CrafterBots165.WantedMaterial(bot.PersistentRecord, eRealm.Hibernia, "ovh-unused"), Is.Zero);
            DbInventoryItem[] stacks = Enumerable.Range(0, 3).Select(_ =>
            {
                DbItemTemplate cloth = Template("ovh cloth", 1, 100, eInventorySlot.FirstBackpack, 10, eObjectType.GenericItem, 0);
                cloth.Id_nb = "ovh-cloth";
                cloth.MaxCount = 100;
                DbInventoryItem stack = GameInventoryItem.Create(cloth);
                stack.Count = 8;
                return AddBackpack(bot, stack);
            }).ToArray();
            Assert.That(AutonomousBotEconomy.IsOwnCraftMaterial(bot, stacks[0]), Is.True);
            Assert.That(AutonomousBotEconomy.IsOwnCraftMaterial(bot, stacks[1]), Is.True, "8 kept so far, it keeps 10");
            Assert.That(AutonomousBotEconomy.IsOwnCraftMaterial(bot, stacks[2]), Is.False, "a surplus stack may be listed or sold");
        }

        [Test]
        public void Stacks_CompareCopiesPerUnit()
        {
            DbItemTemplate Arrows()
            {
                DbItemTemplate t = Template("ovh arrows", 10, 100, eInventorySlot.FirstBackpack, 50, eObjectType.Arrow, 0);
                t.MaxCount = 100;
                return t;
            }
            DbInventoryItem small = GameInventoryItem.Create(Arrows());
            small.Count = 20;
            DbInventoryItem big = GameInventoryItem.Create(Arrows());
            big.Count = 99;
            List(small, AutonomousBotEconomy.GetOwnerId(871_001), 0, 200);   // 10 copper an arrow
            List(big, AutonomousBotEconomy.GetOwnerId(871_002), 0, 495);     // 5 copper an arrow
            MarketBook.NameStats stats = MarketCache.ReadBook(b => b.Name(eRealm.Hibernia, "ovh arrows", null));
            Assert.That(stats.Cheapest.Item, Is.SameAs(big), "the 99-stack is the cheaper per arrow");
            Assert.That(stats.CheapestFor(20), Is.EqualTo(100));
            Assert.That(stats.DearestOtherBot.Item, Is.SameAs(small));
            MarketBook.Listing smallListing = MarketCache.ReadBook(b => b.Find(small));
            Assert.That(MarketCache.ReadBook(b => b.CheaperBotCopies(smallListing)), Is.EqualTo(1));
        }

        [Test]
        public void CraftedProduct_NeverAsksUnderItsMaterials()
        {
            DbInventoryItem product = Valuable("crafted robe");
            product.IsCrafted = true;
            Assert.That(ItemValue165.Ask(product, 0, cheapestOther: 1_000, materialCost: 100_000), Is.GreaterThanOrEqualTo(110_000));

            OverhaulBot crafter = Bot(level: 20);
            List(Valuable("crafted robe"), AutonomousBotEconomy.GetOwnerId(872_001), 0, 20_000);
            Assert.That(AutonomousBotEconomy.PlanCraftedListing(crafter, product, 50_000, 0), Is.Null,
                "1.1 x its materials can't undercut the copy listed: a merchant buys it, never a listing at a loss");
            AutonomousBotEconomy.ListingCandidate plan = AutonomousBotEconomy.PlanCraftedListing(crafter, product, 1_000, 0);
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.PriceCopper, Is.LessThan(20_000).And.GreaterThanOrEqualTo(1_100));
        }

        [Test]
        public void Refresh_NeverAddsAnItemThatIsNoLongerListed()
        {
            DbInventoryItem sold = Valuable("sold meanwhile");
            sold.OwnerLot = RealmExchangeBroker.HiberniaOwnerLot;
            sold.OwnerID = AutonomousBotEconomy.GetOwnerId(873_001);
            Assert.That(MarketCache.Refresh(sold), Is.False);
            Assert.That(MarketCache.Contains(sold), Is.False);
            DbInventoryItem listed = List(Valuable("still listed"), AutonomousBotEconomy.GetOwnerId(873_002), 0, 9_000);
            listed.SellPrice = 8_000;   // changed outside UpdateItem
            Assert.That(MarketCache.Refresh(listed), Is.True);
            Assert.That(MarketCache.ReadBook(b => b.Find(listed)).Price, Is.EqualTo(8_000));
        }

        [Test]
        public void TakeBackThenNewHours_TheQueuedDeleteDoesNotWipeThem()
        {
            RealmExchangeWriteQueue.Enabled = true;
            DbInventoryItem item = Valuable("player robe");
            item.OwnerID = "player-character-id";
            RealmExchangeExpiry.ForgetHours(item);
            Assert.That(RealmExchangeWriteQueue.Count, Is.EqualTo(1));
            RealmExchangeExpiry.SetPlayerListingHours(item, 24);
            Assert.That(RealmExchangeWriteQueue.Count, Is.Zero, "the pending delete is cancelled");
            Assert.That(RealmExchangeExpiry.PlayerListingHours(item), Is.EqualTo(24));
        }

        [Test]
        public void Shutdown_WritesTheExchangeQueue_EvenWithNoBotSaveWaiting()
        {
            _store.Realistic = true;
            RealmExchangeWriteQueue.Enabled = true;
            var record = new OfflineTrue165RelistRecord { InventoryId = "shutdown-row", Tries = Exchange165.TriesFailed };
            RealmExchangeWriteQueue.Save(record);
            FlushingField.SetValue(null, 0);
            AutonomousBotStatusPersistence.FlushAll();
            Assert.That(RealmExchangeWriteQueue.Count, Is.Zero);
            Assert.That(_store.Added, Does.Contain(record));
        }

        [Test]
        public void WorthlessPulledListing_IsNeverDeleted_ItWaitsAnHour()
        {
            OverhaulBot bot = Bot();
            FillBackpack(bot);
            DateTime now = DateTime.UtcNow;
            DbInventoryItem listing = List(Item("worthless robe", 20, 99, eInventorySlot.FirstBackpack, 0, eObjectType.Cloth, 30),
                bot.InternalID, 0, 5_000, now.AddHours(-37));
            Assert.That(ItemValue165.Floor(listing), Is.Zero);
            Assert.That(Exchange165.Pull(listing, bot.DatabaseID, 5_000, RealmExchangeStats.Counter.Returned), Is.False);
            Assert.That(_store.Deleted, Does.Not.Contain(listing));
            Assert.That(MarketCache.Contains(listing), Is.True, "still listed, still buyable");
            Assert.That(RealmExchangeExpiry.ExpiresUtc(listing), Is.GreaterThan(now.AddMinutes(50)), "tried again in an hour");
            Assert.That(MarketCache.ReadBook(b => b.Due(now.AddMinutes(1), 10)), Does.Not.Contain(listing), "it does not block the wheel");
        }

        [Test]
        public void ExpirySweep_PaysOutAtMostTwentyOwnersPerMinute()
        {
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < 25; i++)
                List(Valuable("capped robe " + i), AutonomousBotEconomy.GetOwnerId(874_000 + i), 0, 50_000, now.AddHours(-25));
            Assert.That(RealmExchangeExpiry.ExpireDue(now), Is.EqualTo(RealmExchangeExpiry.MaxCreditsPerSweep));
            Assert.That(_store.Deleted.OfType<DbInventoryItem>().Count(), Is.EqualTo(RealmExchangeExpiry.MaxCreditsPerSweep));
            Assert.That(RealmExchangeExpiry.ExpireDue(now), Is.EqualTo(5), "the rest a minute later");
        }

        [Test]
        public void ClothCaster_FindsItsClothUpgrade_BehindDearerPlateItCannotWear()
        {
            OverhaulBot buyer = Bot(level: 20, money: 5_000_000);
            Assert.That(buyer.Inventory.AddItem(eInventorySlot.TorsoArmor, Item("worn vest", 10, 85, eInventorySlot.TorsoArmor, 100)), Is.True);
            string seller = AutonomousBotEconomy.GetOwnerId(875_000);
            for (int i = 0; i < 12; i++)
                List(Item("heavy scale hauberk " + i, 20, 99, eInventorySlot.TorsoArmor, 500, eObjectType.Scale, 80), seller, i, 30_000);
            Assert.That(AutonomousBotEconomy.WantsWindowShopping(buyer), Is.False, "no town errand for armour it can't wear");
            Assert.That(AutonomousBotEconomy.FindBestUsefulExchangePurchase(buyer), Is.Null);
            DbInventoryItem cloth = List(Item("fine cloth vest", 19, 96, eInventorySlot.TorsoArmor, 500, eObjectType.Cloth, 20), seller, 20, 20_000);

            AutonomousBotEconomy.PurchaseCandidate probe = AutonomousBotEconomy.FindBestUsefulExchangePurchase(buyer);
            Assert.That(probe?.Item, Is.SameAs(cloth), "the scale it can't wear never takes the scored places");
        }

        [Test]
        public void Probe_LeavesOutExpiredAndTooHighListings()
        {
            string seller = AutonomousBotEconomy.GetOwnerId(876_000);
            DateTime now = DateTime.UtcNow;
            DbInventoryItem expired = List(Item("old gloves", 18, 96, eInventorySlot.HandsArmor, 500), seller, 0, 1_000, now.AddHours(-25));
            DbInventoryItem high = List(Item("high gloves", 30, 96, eInventorySlot.HandsArmor, 500), seller, 1, 1_000);
            DbInventoryItem fresh = List(Item("fresh gloves", 18, 96, eInventorySlot.HandsArmor, 500), seller, 2, 1_000);
            int bucket = MarketSearchEngine.GetClientSlot(fresh);
            List<MarketBook.Listing> found = MarketCache.ReadBook(b => b.Probe(eRealm.Hibernia, bucket, 0, MarketBook.Bands - 1, 10_000, null, 22, now.Ticks));
            Assert.That(found.Select(l => l.Item), Is.EquivalentTo(new[] { fresh }));
            Assert.That(MarketCache.ReadBook(b => b.Probe(eRealm.Hibernia, bucket, 0, MarketBook.Bands - 1, 10_000, null)).Count, Is.EqualTo(3));
        }

        // ---- locks

        [Test]
        public void BusyLocks_FailFastAsBusy_NotAsAFailure()
        {
            OverhaulBot bot = Bot();
            DbInventoryItem item = AddBackpack(bot, Valuable("busy robe"));
            OverhaulBot buyer = Bot();
            DbInventoryItem listing = List(Valuable("busy listing"), AutonomousBotEconomy.GetOwnerId(860_000), 0, 1_000);
            using var held = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var holder = new Thread(() =>
            {
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                {
                    held.Set();
                    release.Wait(5_000);
                }
            });
            holder.Start();
            try
            {
                Assert.That(held.Wait(5_000), Is.True);
                long busyBefore = RealmExchangeStats.LockBusy;
                Assert.That(AutonomousBotEconomy.TryList(bot, item, 500, out bool listBusy), Is.False);
                Assert.That(listBusy, Is.True);
                Assert.That(AutonomousBotEconomy.TryBuy(buyer, listing, out bool buyBusy), Is.False);
                Assert.That(buyBusy, Is.True);
                Assert.That(RealmExchangeStats.LockBusy, Is.EqualTo(busyBefore + 2));
                Assert.That(AutonomousServiceFallback.IsExchangeUnreachable(bot.DatabaseID, GameLoop.GameLoopTime), Is.False);
                Assert.That(item.OwnerLot, Is.Zero, "nothing changed");
            }
            finally
            {
                release.Set();
                holder.Join(5_000);
            }
            Assert.That(AutonomousBotEconomy.TryList(bot, item, 500, out bool laterBusy), Is.True);
            Assert.That(laterBusy, Is.False);
        }
    }
}
