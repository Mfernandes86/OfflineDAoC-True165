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
    }
}
