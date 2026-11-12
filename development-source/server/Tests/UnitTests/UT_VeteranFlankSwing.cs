using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q13: bots stopped attacking for up to 4 s while circling to a flank.</summary>
    [TestFixture]
    public class UT_VeteranFlankSwing
    {
        [Test]
        public void KeepSwingingWhileTheFlankWalkStaysInReach() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.FlankKeepsSwinging(true, 80, 200), Is.True);
            Assert.That(BotBrain.FlankKeepsSwinging(false, 80, 200), Is.False, "starting out of reach: walk first");
            Assert.That(BotBrain.FlankKeepsSwinging(true, 250, 200), Is.False, "a spot out of reach");
        });

        /// <summary>Owner 2026-11-11: bots re-ordered the flank walk on every think and crept into position.</summary>
        [Test]
        public void OneWalkOrderPerFlank() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.FlankNeedsMoveOrder(true, 0, false, 0), Is.True, "a new try walks");
            Assert.That(BotBrain.FlankNeedsMoveOrder(false, 12, true, 500), Is.False, "the monster's small turns change nothing");
            Assert.That(BotBrain.FlankNeedsMoveOrder(false, 12, true, 5_000), Is.False, "still walking: no new order");
            Assert.That(BotBrain.FlankNeedsMoveOrder(false, BotBrain.FlankRespotDistance + 1, true, 100), Is.True, "the spot really moved");
            Assert.That(BotBrain.FlankNeedsMoveOrder(false, 0, false, 200), Is.False, "just stopped: wait");
            Assert.That(BotBrain.FlankNeedsMoveOrder(false, 0, false, BotBrain.FlankReissueMilliseconds), Is.True, "stood still short of it");
        });
    }
}
