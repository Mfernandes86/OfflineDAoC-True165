using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Vendor armour only for an empty slot or a worn piece that cons green (veteran-bots review M9 / economy G4).</summary>
[TestFixture, NonParallelizable]
public class UT_VendorArmorRule165
{
    private GameServer _oldServer;
    private sealed class EmptyServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl =>
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    }

    private sealed class Bot : GameBot
    {
        private Bot() : base((OfflineWorldBotRecord)null) { }
        public byte TestLevel;
        public override byte Level { get => TestLevel; set => TestLevel = value; }
    }

    [SetUp]
    public void SetUp()
    {
        _oldServer = GameServer.Instance;
        GameServer.LoadTestDouble((EmptyServer)RuntimeHelpers.GetUninitializedObject(typeof(EmptyServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_oldServer);

    private static Bot MakeBot(int level)
    {
        var bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
        bot.TestLevel = (byte)level;
        bot.Inventory = new BotInventory();
        return bot;
    }

    private static DbItemTemplate Piece(eObjectType type, eInventorySlot slot, int level) => new()
    {
        Id_nb = $"test_{type}_{slot}_{level}", Name = "test piece", Level = level, Object_Type = (int)type,
        Item_Type = (int)slot, DPS_AF = level * 2, SPD_ABS = 19, Quality = 90, Price = 100,
    };

    private static void Wear(Bot bot, eObjectType type, eInventorySlot slot, int level) =>
        bot.Inventory.AddItem(slot, GameInventoryItem.Create(Piece(type, slot, level)));

    [Test]
    public void AnEmptySlotAlwaysTakesVendorArmourClothIncluded()
    {
        var bot = MakeBot(20);
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Chain, eInventorySlot.TorsoArmor, 20)), Is.True);
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Cloth, eInventorySlot.HeadArmor, 12)), Is.True);
    }

    [Test]
    public void AWornPieceIsReplacedOnlyOnceItConsGreenAndByAPieceAtTheBotsLevel()
    {
        var bot = MakeBot(20);
        Wear(bot, eObjectType.Chain, eInventorySlot.TorsoArmor, 19);   // blue/yellow: kept
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Chain, eInventorySlot.TorsoArmor, 21)), Is.False);

        bot.Inventory.RemoveItem(bot.Inventory.GetItem(eInventorySlot.TorsoArmor));
        Wear(bot, eObjectType.Chain, eInventorySlot.TorsoArmor, 12);   // green at 20
        Assert.That(AutonomousWorldBotController.WornPieceConsGreen(20, 12), Is.True);
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Chain, eInventorySlot.TorsoArmor, 20)), Is.True);
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Chain, eInventorySlot.TorsoArmor, 15)), Is.False,
            "a piece well under the bot's level is not worth the trip");
    }

    [Test]
    public void VendorClothNeverReplacesAWornPiece()
    {
        var caster = MakeBot(30);
        Wear(caster, eObjectType.Cloth, eInventorySlot.LegsArmor, 10);   // grey at 30
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(caster, Piece(eObjectType.Cloth, eInventorySlot.LegsArmor, 30)), Is.False);
    }

    [Test]
    public void JewelleryFollowsTheSameRuleAndARingPairWithAnEmptySideIsOpen()
    {
        var bot = MakeBot(25);
        Wear(bot, eObjectType.Magical, eInventorySlot.LeftRing, 24);
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Magical, eInventorySlot.RightRing, 25)), Is.True,
            "the right ring finger is empty");
        Wear(bot, eObjectType.Magical, eInventorySlot.RightRing, 24);
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Magical, eInventorySlot.LeftRing, 25)), Is.False);
    }

    [Test]
    public void WeaponsKeepTodaysRule()
    {
        var bot = MakeBot(20);
        Wear(bot, eObjectType.Sword, eInventorySlot.RightHandWeapon, 19);
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Sword, eInventorySlot.RightHandWeapon, 21)), Is.True);
        Assert.That(AutonomousWorldBotController.VendorPieceAllowed(bot, Piece(eObjectType.Shield, eInventorySlot.LeftHandWeapon, 21)), Is.True);
    }
}
