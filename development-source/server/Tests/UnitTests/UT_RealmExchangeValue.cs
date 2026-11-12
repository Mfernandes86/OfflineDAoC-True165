using System;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>The 2026-11-11 Realm Exchange overhaul's pure rules: ItemValue165, the stale cycle prices, caps and purse.</summary>
    [TestFixture, NonParallelizable]
    public class UT_RealmExchangeValue
    {
        private int _previousSellRatio;

        [SetUp]
        public void SetUp()
        {
            _previousSellRatio = Properties.ITEM_SELL_RATIO;
            Properties.ITEM_SELL_RATIO = 50;
        }

        [TearDown]
        public void TearDown()
        {
            Properties.ITEM_SELL_RATIO = _previousSellRatio;
            ItemValue165.SetMerchantStockForTests(null);
        }

        private static DbInventoryItem Gear(int level, int quality, int price, eObjectType type = eObjectType.Plate,
            eInventorySlot slot = eInventorySlot.TorsoArmor)
        {
            var template = new DbItemTemplate
            {
                Id_nb = Guid.NewGuid().ToString(), Name = "value test gear " + level, Level = level, Quality = quality,
                Item_Type = (int)slot, Object_Type = (int)type, MaxCount = 1, PackSize = 1, Price = price,
                MaxCondition = 10000, MaxDurability = 10000, IsDropable = true, IsTradable = true,
            };
            return GameInventoryItem.Create(template);
        }

        private static DbInventoryItem Stack(string id, eObjectType type, int count, int pack, int price, int level = 1)
        {
            var template = new DbItemTemplate
            {
                Id_nb = id, Name = id, Level = level, Object_Type = (int)type, MaxCount = 200, PackSize = pack, Price = price,
                IsDropable = true, IsTradable = true, Item_Type = 40,
            };
            DbInventoryItem item = GameInventoryItem.Create(template);
            item.Count = count;
            return item;
        }

        [Test]
        public void Ask_StaysBetweenTheFloorAndNinetyPercentOfTheShelf()
        {
            DbInventoryItem arrows = Stack("rough blunt arrows test", eObjectType.Arrow, 20, 20, 19);
            ItemValue165.SetMerchantStockForTests(["rough blunt arrows test"]);
            long floor = ItemValue165.Floor(arrows), shelf = ItemValue165.Shelf(arrows);
            Assert.That(shelf, Is.EqualTo(19));
            foreach (double mood in new[] { -0.5, ItemValue165.MoodLow, 0, ItemValue165.MoodHigh, 1.0 })
            {
                long ask = ItemValue165.Ask(arrows, mood);
                Assert.That(ask, Is.GreaterThanOrEqualTo(floor));
                Assert.That(ask, Is.LessThanOrEqualTo(Math.Max(ItemValue165.AskFloor(arrows), shelf * 9 / 10)));
            }
        }

        [Test]
        public void MerchantStock_IsNotWorthListing_AndSmallGainsAreNot()
        {
            // the old valuer asked 111 copper for arrows a merchant sells for 19: never listed now
            DbInventoryItem arrows = Stack("rough blunt arrows test", eObjectType.Arrow, 20, 20, 19);
            ItemValue165.SetMerchantStockForTests(["rough blunt arrows test"]);
            Assert.That(ItemValue165.WorthListing(arrows, 20), Is.False);

            DbInventoryItem cheap = Gear(2, 85, 40);
            Assert.That(ItemValue165.WorthListing(cheap, 10), Is.False, "gain under 300 copper");

            DbInventoryItem good = Gear(14, 85, 5_000);
            Assert.That(ItemValue165.WorthListing(good, 20), Is.True);
            Assert.That(ItemValue165.WorthListing(good, 40), Is.False, "grey to a level-40 bot");
        }

        [Test]
        public void Utility_WeighsSkillsAboveStatsAboveHits_AndIgnoresBonusPercent()
        {
            DbInventoryItem skill = Gear(20, 96, 1_000);
            skill.Bonus1Type = (int)eProperty.Skill_Slashing;
            skill.Bonus1 = 3;
            DbInventoryItem stat = Gear(20, 96, 1_000);
            stat.Bonus1Type = (int)eProperty.Strength;
            stat.Bonus1 = 3;
            DbInventoryItem hits = Gear(20, 96, 1_000);
            hits.Bonus1Type = (int)eProperty.MaxHealth;
            hits.Bonus1 = 3;
            hits.Bonus = 35;

            Assert.That(ItemValue165.Utility(skill), Is.EqualTo(15));
            Assert.That(ItemValue165.Utility(stat), Is.EqualTo(3));
            Assert.That(ItemValue165.Utility(hits), Is.EqualTo(0.75));
            Assert.That(ItemValue165.Fair(skill), Is.GreaterThan(ItemValue165.Fair(stat)));
            Assert.That(ItemValue165.Fair(stat), Is.GreaterThan(ItemValue165.Fair(hits)));
        }

        [Test]
        public void Fair_GearIsAtLeastTwiceTheFloor_CraftedAtLeastItsMaterials()
        {
            DbInventoryItem gear = Gear(5, 85, 50_000);
            Assert.That(ItemValue165.Fair(gear), Is.GreaterThanOrEqualTo(2 * ItemValue165.Floor(gear)));

            DbInventoryItem crafted = Gear(10, 96, 100);
            crafted.IsCrafted = true;
            Assert.That(ItemValue165.Fair(crafted, 1_000_000), Is.GreaterThanOrEqualTo(1_100_000));
            Assert.That(ItemValue165.Gain(crafted, 1_000_000), Is.EqualTo(ItemValue165.Fair(crafted, 1_000_000) - ItemValue165.Floor(crafted)));
        }

        [Test]
        public void Ask_UndercutsTheCheapestCopyButNeverGoesUnderTheFloor()
        {
            DbInventoryItem gear = Gear(20, 99, 5_000);
            long alone = ItemValue165.Ask(gear, 0);
            long under = ItemValue165.Ask(gear, 0, alone / 2);
            Assert.That(under, Is.LessThan(alone / 2));
            Assert.That(under, Is.GreaterThanOrEqualTo(alone / 2 * 95 / 100 - 1));
            Assert.That(ItemValue165.Ask(gear, 0, 1), Is.EqualTo(ItemValue165.AskFloor(gear)));
        }

        [Test]
        public void StaleCycle_NeverRaisesAPrice()
        {
            for (long ask = 1; ask < 200_000; ask = ask * 3 + 7)
                foreach (long floor in new[] { 0L, ask / 3, ask, ask * 2 })
                    foreach (long other in new[] { 0L, ask / 2, ask * 2 })
                    {
                        Assert.That(Exchange165.UndercutPrice(ask, other, floor * 5 / 4 + 1), Is.LessThanOrEqualTo(ask));
                        long relist = Exchange165.RelistPrice(ask, floor, other);
                        Assert.That(relist == 0 || relist < ask && relist > floor, Is.True, $"ask {ask} floor {floor} median {other}");
                    }
            // the old rule max(vendor + 1, 60%) doubled a stack listed under its vendor value: now it is pulled instead
            Assert.That(Exchange165.RelistPrice(1_000, 2_000, 0), Is.Zero);
            Assert.That(Exchange165.RelistPrice(10_000, 100, 0), Is.EqualTo(7_000));
            Assert.That(Exchange165.RelistPrice(10_000, 100, 5_000), Is.EqualTo(5_000));
            Assert.That(Exchange165.UndercutPrice(10_000, 0, 100), Is.EqualTo(8_500));
            Assert.That(Exchange165.UndercutPrice(10_000, 9_000, 100), Is.EqualTo(8_820));
        }

        [Test]
        public void ListingCap_ByLevel()
        {
            Assert.That(AutonomousBotEconomy.ListingCap(10), Is.EqualTo(3));
            Assert.That(AutonomousBotEconomy.ListingCap(19), Is.EqualTo(3));
            Assert.That(AutonomousBotEconomy.ListingCap(20), Is.EqualTo(4));
            Assert.That(AutonomousBotEconomy.ListingCap(34), Is.EqualTo(4));
            Assert.That(AutonomousBotEconomy.ListingCap(35), Is.EqualTo(5));
            var room = new AutonomousBotEconomy.ListingRoom(3, 2, 3, 1);
            Assert.That(room.For(false), Is.False);
            Assert.That(room.For(true), Is.True, "a crafter's own product still has a crafted-only slot");
        }

        [Test]
        public void PurseShare_SmallUpgradeQuarter_EmptySlotEightyPercent()
        {
            Assert.That(AutonomousBotEconomy.PurseAllows(10_000, 2_500, 9, false), Is.True);
            Assert.That(AutonomousBotEconomy.PurseAllows(10_000, 3_000, 9, false), Is.False);
            Assert.That(AutonomousBotEconomy.PurseAllows(10_000, 8_000, 9, true), Is.True);
            Assert.That(AutonomousBotEconomy.PurseAllows(10_000, 8_000, 60, false), Is.True);
            Assert.That(AutonomousBotEconomy.PurseAllows(10_000, 8_100, 60, false), Is.False);
            Assert.That(AutonomousBotEconomy.PurseAllows(0, 1, 100, true), Is.False);
        }

        // owner call (valuation option 8): gear ranking weights bonuses by type and leaves out the Bonus% field
        [Test]
        public void EquipmentValue_WeightsSkillsOverHitsAndIgnoresTheBonusPercentField()
        {
            bool previous = AutonomousBotEconomy.WeightedGearRanking;
            try
            {
                DbInventoryItem skill = Gear(20, 90, 1_000, eObjectType.Cloth);
                skill.Bonus1Type = (int)eProperty.Skill_Slashing;
                skill.Bonus1 = 3;
                DbInventoryItem hits = Gear(20, 90, 1_000, eObjectType.Cloth);
                hits.Bonus1Type = (int)eProperty.MaxHealth;
                hits.Bonus1 = 12;
                DbInventoryItem plain = Gear(20, 90, 1_000, eObjectType.Cloth);

                AutonomousBotEconomy.WeightedGearRanking = true;
                int basis = AutonomousBotEconomy.EquipmentValue(plain);
                Assert.That(AutonomousBotEconomy.EquipmentValue(skill) - basis, Is.EqualTo(45), "+3 skill = 3 x 5 x 3");
                Assert.That(AutonomousBotEconomy.EquipmentValue(hits) - basis, Is.EqualTo(9), "+12 hits = 12 x 0.25 x 3");
                plain.Bonus = 35;
                Assert.That(AutonomousBotEconomy.EquipmentValue(plain), Is.EqualTo(basis), "the Bonus% field is left out");

                AutonomousBotEconomy.WeightedGearRanking = false;
                Assert.That(AutonomousBotEconomy.EquipmentValue(hits), Is.GreaterThan(AutonomousBotEconomy.EquipmentValue(skill)),
                    "switched off, every point counts the same again");
            }
            finally
            {
                AutonomousBotEconomy.WeightedGearRanking = previous;
            }
        }
    }
}
