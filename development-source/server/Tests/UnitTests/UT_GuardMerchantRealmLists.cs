using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>True 1.65 merchant keep merchants (battlegrounds fix 2026-11-12, BGCONTENT R2): name and list per owning realm.</summary>
    [TestFixture]
    public class UT_GuardMerchantRealmLists
    {
        [Test]
        public void TheListFollowsTheRealm()
        {
            Assert.That(GuardMerchant.ListFor(null, eRealm.Albion), Is.EqualTo("AlbRvRCraftingList"), "a row without a base: as before");
            Assert.That(GuardMerchant.ListFor(null, eRealm.Midgard), Is.EqualTo("MidRvRCraftingList"));
            Assert.That(GuardMerchant.ListFor("true165MkSiege", eRealm.Hibernia), Is.EqualTo("true165MkSiegeHib"));
            Assert.That(GuardMerchant.ListFor("true165MkWood", eRealm.Midgard), Is.EqualTo("true165MkWoodMid"));
            Assert.That(GuardMerchant.ListFor("x", eRealm.None), Is.Null, "no realm: the list is left as it is");
        }

        [Test]
        public void TheNameFollowsTheRealm()
        {
            string[] bledmeer = { "Collen", "Frenrik", "Freagh" };
            Assert.That(GuardMerchant.NameFor(bledmeer, eRealm.Albion), Is.EqualTo("Collen"));
            Assert.That(GuardMerchant.NameFor(bledmeer, eRealm.Midgard), Is.EqualTo("Frenrik"));
            Assert.That(GuardMerchant.NameFor(bledmeer, eRealm.Hibernia), Is.EqualTo("Freagh"));
            Assert.That(GuardMerchant.NameFor(new[] { "", "Luthor", "" }, eRealm.Albion), Is.Null, "an empty part keeps the server's name");
            Assert.That(GuardMerchant.NameFor(new[] { "", "Luthor", "" }, eRealm.Midgard), Is.EqualTo("Luthor"));
            Assert.That(GuardMerchant.NameFor(null, eRealm.Hibernia), Is.Null);
            Assert.That(GuardMerchant.NameFor(new[] { "Only" }, eRealm.Albion), Is.Null);
        }

        [Test]
        public void MerchantKeepListsAreNeverTakenForSiegeMasters()
        {
            // Bots buy deeds only from the SiegeMerchantAlb/Mid/Hib lists (battleground Siege Masters); the merchant keeps
            // sell siege parts and wood, which no bot code buys.
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                Assert.That(AutonomousSiegePolicy.IsSiegeMerchant(realm, GuardMerchant.ListFor("true165MkSiege", realm)), Is.False);
                Assert.That(AutonomousWorldBotController.IsSiegeMasterFor(realm, realm, GuardMerchant.ListFor("true165MkSiege", realm)), Is.False);
            }
        }
    }
}
