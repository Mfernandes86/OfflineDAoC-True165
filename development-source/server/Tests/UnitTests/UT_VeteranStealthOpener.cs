using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q31: the stealth opener re-planned its route on every think for up to 25 s. Now: only on
    /// a still or slow target, a route at most once a second, a straight walk inside 300 units, give up after 12 s.
    /// </summary>
    [TestFixture]
    public class UT_VeteranStealthOpener
    {
        [Test]
        public void OnlyStillOrSlowTargets() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.OpenerTargetSlowEnough(0), Is.True);
            Assert.That(BotBrain.OpenerTargetSlowEnough(80), Is.True);
            Assert.That(BotBrain.OpenerTargetSlowEnough(200), Is.False);
        });

        [Test]
        public void RouteAtMostOnceASecondAndStraightWhenClose() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.OpenerMove(250, 0, 5000), Is.EqualTo(1), "close: straight walk");
            Assert.That(BotBrain.OpenerMove(900, 1000, 1000), Is.EqualTo(2), "far, due: a new route");
            Assert.That(BotBrain.OpenerMove(900, 1000, 1500), Is.EqualTo(0), "far, not due: keep going");
            Assert.That(BotBrain.StealthOpenerGiveUpMilliseconds, Is.EqualTo(12_000));
        });
    }
}
