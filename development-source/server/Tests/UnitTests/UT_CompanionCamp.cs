using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review M7 (2026-11-11): /camp makes a hold point the companions lay out around (the pull comes back to it,
    /// /defensive measures from it).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_CompanionCamp
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previous;

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public int TestX;
            public ushort TestRegion = 1;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => TestRegion; set { } }
            public override IPacketLib Out => new BotDummyPacketLib();
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup() => GameServer.LoadTestDouble(_previous);

        private static Player NewPlayer() => (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));

        [Test]
        public void CampHoldsUntilThePlayerLeavesItOrTheRegion() => Assert.Multiple(() =>
        {
            Assert.That(CompanionControls165.CampHolds(true, 400, 0), Is.True);
            Assert.That(CompanionControls165.CampHolds(true, 3000, 5_000), Is.True, "away only 5 s");
            Assert.That(CompanionControls165.CampHolds(true, 3000, 10_000), Is.False, "away 10 s");
            Assert.That(CompanionControls165.CampHolds(false, 0, 0), Is.False, "another region");
        });

        [Test]
        public void CampIsTheReturnPointAndPassiveOrOffDropsIt()
        {
            Player player = NewPlayer();
            player.TestX = 1000;
            Assert.That(CompanionEngagementMode.CampOf(player, out _, out _), Is.False, "no camp by default");
            Assert.That(CompanionEngagementMode.ReturnPoint(player), Is.EqualTo(new Vector3(1000, 0, 0)), "no camp: the player");
            CompanionEngagementMode.SetCamp(player);
            player.TestX = 1800;   // the player walks out to pull
            Assert.That(CompanionEngagementMode.CampOf(player, out Vector3 camp, out _), Is.True);
            Assert.That(camp, Is.EqualTo(new Vector3(1000, 0, 0)));
            Assert.That(CompanionEngagementMode.ReturnPoint(player), Is.EqualTo(new Vector3(1000, 0, 0)), "the pull comes back to camp");
            CompanionEngagementMode.ClearCamp(player);
            Assert.That(CompanionEngagementMode.CampOf(player, out _, out _), Is.False, "/camp off");
            CompanionEngagementMode.SetCamp(player);
            CompanionEngagementMode.Set(player, CompanionStance.Passive);
            Assert.That(CompanionEngagementMode.CampOf(player, out _, out _), Is.False, "/passive drops the camp");
            CompanionEngagementMode.Set(player, false);
        }

        [Test]
        public void ARegionChangeDropsTheCamp()
        {
            Player player = NewPlayer();
            CompanionEngagementMode.SetCamp(player);
            player.TestRegion = 2;
            Assert.That(CompanionEngagementMode.CampOf(player, out _, out _), Is.False);
            player.TestRegion = 1;
            Assert.That(CompanionEngagementMode.CampOf(player, out _, out _), Is.False, "it stays dropped");
        }
    }
}
