using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Craft orders and milestone crafted sets (veteran-bots review M9 / economy G5).</summary>
[TestFixture, NonParallelizable]
public class UT_CraftOrders165
{
    private GameServer _oldServer;
    private bool _classic;
    private sealed class EmptyServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl =>
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    }

    private sealed class Bot : GameBot
    {
        private Bot() : base((OfflineWorldBotRecord)null) { }
    }

    [SetUp]
    public void SetUp()
    {
        _oldServer = GameServer.Instance;
        GameServer.LoadTestDouble((EmptyServer)RuntimeHelpers.GetUninitializedObject(typeof(EmptyServer)));
        _classic = Properties.CLASSIC165_MODE;
        Properties.CLASSIC165_MODE = true;
        CraftOrders165.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        CraftOrders165.Reset();
        Properties.CLASSIC165_MODE = _classic;
        GameServer.LoadTestDouble(_oldServer);
    }

    private static CraftOrders165.Order Order(long buyer, eCraftingSkill craft, int recipeLevel, long posted) =>
        new(buyer, eRealm.None, new CraftOrders165.Piece(craft, new CrafterBots165.Recipe("x", recipeLevel, 1_000),
            eInventorySlot.TorsoArmor, CraftOrders165.PriceOf(1_000)), 20, posted);

    [Test]
    public void MilestonesAreTwentyThirtyAndForty()
    {
        Assert.That(CraftOrders165.MilestoneFor(19), Is.Zero);
        Assert.That(CraftOrders165.MilestoneFor(20), Is.EqualTo(20));
        Assert.That(CraftOrders165.MilestoneFor(29), Is.EqualTo(20));
        Assert.That(CraftOrders165.MilestoneFor(35), Is.EqualTo(30));
        Assert.That(CraftOrders165.MilestoneFor(50), Is.EqualTo(40));
    }

    [Test]
    public void TheBuyerPaysMaterialsTimesOnePointSevenFiveAndOrdersOnlyAWholeSetItCanAfford()
    {
        Assert.That(CraftOrders165.PriceOf(1_000), Is.EqualTo(1_750));
        var plan = Enumerable.Range(0, 6).Select(_ => new CraftOrders165.Piece(eCraftingSkill.ArmorCrafting,
            new CrafterBots165.Recipe("x", 300, 1_000), eInventorySlot.TorsoArmor, 1_750)).ToArray();
        Assert.That(CraftOrders165.CanAfford(10_500 + 400, 400, plan), Is.True);
        Assert.That(CraftOrders165.CanAfford(10_499 + 400, 400, plan), Is.False, "the reserve stays");
    }

    [Test]
    public void ACrafterTakesOnlyOrdersWithinItsReach()
    {
        Assert.That(CraftOrders165.CanMake(290, new CrafterBots165.Recipe("x", 300, 1)), Is.True);
        Assert.That(CraftOrders165.CanMake(289, new CrafterBots165.Recipe("x", 300, 1)), Is.False);
    }

    [Test]
    public void LapsedOrdersLeaveTheQueueAndOtherCraftsWaitForTheirCrafter()
    {
        var crafter = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
        crafter.Inventory = new BotInventory();
        var record = new OfflineWorldBotRecord { CraftLevel = 400 };
        CraftOrders165.Enqueue(Order(11, eCraftingSkill.ArmorCrafting, 300, -CraftOrders165.OrderLifetimeMilliseconds - 1));
        CraftOrders165.Enqueue(Order(12, eCraftingSkill.Tailoring, 300, 0));
        CraftOrders165.Enqueue(Order(13, eCraftingSkill.ArmorCrafting, 300, 0));   // buyer 13 is not online: kept

        Assert.That(CraftOrders165.FillOrders(crafter, eRealm.None, 1, record, eCraftingSkill.ArmorCrafting), Is.Zero);
        Assert.That(CraftOrders165.QueuedCount(eRealm.None), Is.EqualTo(2), "the lapsed order went, the other two wait");
    }

    [Test]
    public void ACrafterLooksAtAFewOrdersPerErrandNotTheWholeQueue()
    {
        var crafter = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
        crafter.Inventory = new BotInventory();
        var record = new OfflineWorldBotRecord { CraftLevel = 400 };
        for (int i = 0; i < 50; i++)
            CraftOrders165.Enqueue(Order(100 + i, eCraftingSkill.Tailoring, 300, -CraftOrders165.OrderLifetimeMilliseconds - 1));
        CraftOrders165.FillOrders(crafter, eRealm.None, 1, record, eCraftingSkill.ArmorCrafting);
        Assert.That(CraftOrders165.QueuedCount(eRealm.None), Is.EqualTo(50 - CraftOrders165.MaxScannedPerErrand));
    }
}
