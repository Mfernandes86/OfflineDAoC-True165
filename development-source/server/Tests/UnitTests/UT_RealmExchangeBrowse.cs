using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The 2026-11-11 Realm Exchange overhaul, phase 6 (what the player sees): search categories for ammunition, poisons
    /// and gems, one row per item with a copy count, the fit and newest orders, and the status line.
    /// </summary>
    [TestFixture]
    public class UT_RealmExchangeBrowse
    {
        private static DbInventoryItem Item(string name, eObjectType type, eInventorySlot itemType = 0, int level = 10,
            int price = 100, string owner = "seller", int count = 1, string id = null, DateTime? listed = null)
        {
            var item = new DbInventoryItem
            {
                Name = name,
                Object_Type = (int)type,
                Item_Type = (int)itemType,
                Level = level,
                Quality = 90,
                SellPrice = price,
                OwnerID = owner,
                Count = count,
                OwnerLot = RealmExchangeBroker.HiberniaOwnerLot,
                RealmExchangeListedUtc = (listed ?? new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc)).ToString("O"),
            };
            item.ObjectId = id ?? Guid.NewGuid().ToString("N");
            return item;
        }

        [Test]
        public void AmmunitionPoisonsAndGemsHaveTheirOwnCategoriesAndTheRestIsOtherItems()
        {
            Assert.That(MarketSearchEngine.GetClientSlot(Item("rough blunt arrows", eObjectType.Arrow)), Is.EqualTo(MarketSearchEngine.AmmunitionSlot));
            Assert.That(MarketSearchEngine.GetClientSlot(Item("bolts", eObjectType.Bolt)), Is.EqualTo(MarketSearchEngine.AmmunitionSlot));
            Assert.That(MarketSearchEngine.GetClientSlot(Item("lesser lethal poison", eObjectType.Poison)), Is.EqualTo(MarketSearchEngine.PoisonSlot));
            Assert.That(MarketSearchEngine.GetClientSlot(Item("raw sapphire", eObjectType.SpellcraftGem)), Is.EqualTo(MarketSearchEngine.GemSlot));
            Assert.That(MarketSearchEngine.GetClientSlot(Item("tincture", eObjectType.AlchemyTincture)), Is.EqualTo(MarketSearchEngine.GemSlot));
            Assert.That(MarketSearchEngine.GetClientSlot(Item("Mystical Beast Eye", eObjectType.GenericItem)), Is.EqualTo(MarketSearchEngine.OtherSlot));
            Assert.That(MarketSearchEngine.GetClientSlot(Item("leather square", eObjectType.Magical)), Is.EqualTo(MarketSearchEngine.OtherSlot));
            // gear keeps its slot
            Assert.That(MarketSearchEngine.GetClientSlot(Item("tunic", eObjectType.Cloth, eInventorySlot.TorsoArmor)), Is.EqualTo(5));
            Assert.That(MarketSearchEngine.GetClientSlot(Item("bow", eObjectType.RecurvedBow, eInventorySlot.DistanceWeapon)), Is.EqualTo(103));
        }

        private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        private static List<DbInventoryItem> Market()
        {
            var items = new List<DbInventoryItem>();
            // five stacks of arrows: the cheapest per arrow is the 40-stack at 200c (5c each)
            items.Add(Item("rough blunt arrows", eObjectType.Arrow, price: 111, count: 20, id: "a1"));
            items.Add(Item("Rough Blunt Arrows", eObjectType.Arrow, price: 200, count: 40, id: "a2"));
            items.Add(Item("rough blunt arrows", eObjectType.Arrow, price: 150, count: 20, id: "a3"));
            items.Add(Item("rough blunt arrows", eObjectType.Arrow, price: 120, count: 20, id: "a4"));
            items.Add(Item("rough blunt arrows", eObjectType.Arrow, price: 300, count: 20, id: "a5", listed: Now.AddMinutes(-10)));
            items.Add(Item("Mildewed Tunic", eObjectType.Cloth, eInventorySlot.TorsoArmor, level: 6, price: 90, id: "t1"));
            items.Add(Item("Mildewed Tunic", eObjectType.Cloth, eInventorySlot.TorsoArmor, level: 6, price: 80, id: "t2", owner: "me"));
            items.Add(Item("Great Iron Sword", eObjectType.TwoHandedWeapon, eInventorySlot.TwoHandWeapon, level: 12, price: 900, id: "s1",
                listed: Now.AddMinutes(-30)));
            return items;
        }

        [Test]
        public void CopiesBecomeOneRowShowingTheCheapestCopyTheViewerMayBuy()
        {
            RealmExchangeBrowse.View view = RealmExchangeBrowse.Build(Market(), true, "me", Now);

            Assert.That(view.Rows, Has.Count.EqualTo(3));
            Assert.That((view.Listings, view.Distinct, view.Fresh, view.Grouped), Is.EqualTo((8, 3, 2, true)));
            RealmExchangeBrowse.Row arrows = view.Rows.Single(row => row.Item.Object_Type == (int)eObjectType.Arrow);
            Assert.That((arrows.Item.ObjectId, arrows.Copies), Is.EqualTo(("a2", 5)), "the cheapest per arrow, names compared without case");
            Assert.That(arrows.NewestTicks, Is.EqualTo(Now.AddMinutes(-10).Ticks));
            RealmExchangeBrowse.Row tunic = view.Rows.Single(row => row.Item.Name == "Mildewed Tunic");
            Assert.That((tunic.Item.ObjectId, tunic.Copies), Is.EqualTo(("t1", 2)), "the viewer's own cheaper copy is not the one he buys");

            RealmExchangeBrowse.View alone = RealmExchangeBrowse.Build(Market().Where(item => item.ObjectId == "t2").ToList(), true, "me", Now);
            Assert.That(alone.Rows.Single().Item.ObjectId, Is.EqualTo("t2"), "his own copy still shows when it is the only one");
        }

        [Test]
        public void EveryCopyFlagKeepsTheOldRowsAndTheDistinctCount()
        {
            RealmExchangeBrowse.View view = RealmExchangeBrowse.Build(Market(), false, "me", Now);

            Assert.That(view.Rows, Has.Count.EqualTo(8));
            Assert.That(view.Rows.All(row => row.Copies == 1), Is.True);
            Assert.That((view.Distinct, view.Grouped), Is.EqualTo((3, false)));
            Assert.That(RealmExchangeBrowse.Lots(view.Rows, view.Grouped), Is.Null, "ungrouped rows keep each item's own lot");
        }

        [Test]
        public void GroupedRowsSendTheirCopyCountInTheLotField()
        {
            var rows = new List<RealmExchangeBrowse.Row>
            {
                new(Item("a", eObjectType.Arrow), 14, 0),
                new(Item("b", eObjectType.Arrow), 1, 0),
                new(Item("c", eObjectType.Arrow), 70_000, 0),
            };
            Assert.That(RealmExchangeBrowse.Lots(rows, true), Is.EqualTo(new ushort[] { 14, 1, RealmExchangeBrowse.MaxCopyLot }));
            Assert.That(RealmExchangeBrowse.MaxCopyLot, Is.LessThan(RealmExchangeBroker.AlbionOwnerLot),
                "a copy count never looks like an exchange lot");
        }

        [Test]
        public void TheDefaultOrderIsFitToThePlayersLevel()
        {
            var rows = new[] { 30, 5, 24, 23, 18, 20 }
                .Select(level => new RealmExchangeBrowse.Row(Item($"item {level}", eObjectType.Cloth, level: level), 1, 0)).ToList();

            List<int> Levels(string sort) => RealmExchangeBrowse.Order(rows.ToList(), sort, 20).Select(row => row.Item.Level).ToList();

            Assert.That(Levels(null), Is.EqualTo(new[] { 20, 18, 23, 5, 24, 30 }),
                "at or below level + 3 first, closest first; items above that follow, nearest first");
            Assert.That(Levels("fit asc"), Is.EqualTo(Levels(null)));
            Assert.That(Levels("bogus asc"), Is.EqualTo(Levels(null)));
            Assert.That(Levels("level asc"), Is.EqualTo(new[] { 5, 18, 20, 23, 24, 30 }));
            Assert.That(Levels("level desc"), Is.EqualTo(new[] { 30, 24, 23, 20, 18, 5 }));
        }

        [Test]
        public void NewestAndPriceOrdersBreakTiesOnPriceThenName()
        {
            RealmExchangeBrowse.View view = RealmExchangeBrowse.Build(Market(), true, "me", Now);

            List<string> newest = RealmExchangeBrowse.Order(view.Rows.ToList(), "newest desc", 20).Select(row => row.Item.ObjectId).ToList();
            Assert.That(newest, Is.EqualTo(new[] { "a2", "s1", "t1" }), "a row is as new as its newest copy");
            List<string> cheap = RealmExchangeBrowse.Order(view.Rows.ToList(), "price asc", 20).Select(row => row.Item.ObjectId).ToList();
            Assert.That(cheap, Is.EqualTo(new[] { "t1", "a2", "s1" }));
        }

        [Test]
        public void TheStatusLineKeepsTheOldStartForOlderClients()
        {
            string status = RealmExchangeBrowse.Status(1_464, 254, 37);

            Assert.That(status, Does.StartWith("Realm Exchange returned 1464 real listing(s)"));
            Assert.That(status.Split(' ')[3], Is.EqualTo("1464"), "older clients read the count as the fourth word");
            Assert.That(status, Does.Contain("254 different item(s), 37 new in the last hour"));
        }
    }
}
