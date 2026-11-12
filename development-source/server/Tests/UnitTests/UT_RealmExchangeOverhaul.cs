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

        /// <summary>Accepts every write and records deletes; selects return the configured rows or nothing.</summary>
        public class StoreDatabase : DispatchProxy
        {
            public readonly List<DataObject> Deleted = new();
            public readonly Dictionary<Type, IList> Rows = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(IObjectDatabase.DeleteObject))
                {
                    if (args[0] is DataObject row)
                        Deleted.Add(row);
                    else if (args[0] is IEnumerable<DataObject> rows)
                        Deleted.AddRange(rows);
                    return true;
                }
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
            ItemValue165.SetMerchantStockForTests(null);
            Properties.ITEM_SELL_RATIO = _previousSellRatio;
            Properties.CLASSIC165_MODE = _previousMode;
            GameServer.LoadTestDouble(_previousServer);
        }

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
