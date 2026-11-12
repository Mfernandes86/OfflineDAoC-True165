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
    }
}
