using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Q30 (veteran review 2026-11-11): an RvR healer keeps its support target for 2 s and ranks the candidates before any
    /// line-of-sight raycast, instead of an area search, a raycast per keep guard and a sort on every heal check.
    /// </summary>
    [TestFixture]
    public class UT_RvrHealerSupportCache
    {
        [Test]
        public void TheRankIsRelicCarrierThenKeepLordThenGuards() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.RvrSupportRank(carriesRelic: true, keepLord: false), Is.EqualTo(0));
            Assert.That(BotBrain.RvrSupportRank(carriesRelic: false, keepLord: true), Is.EqualTo(1));
            Assert.That(BotBrain.RvrSupportRank(carriesRelic: false, keepLord: false), Is.EqualTo(2));
        });

        [Test]
        public void AHeldTargetIsKeptTwoSecondsWhileItStillNeedsHealing() => Assert.Multiple(() =>
        {
            long until = 10_000 + BotBrain.RvrSupportHoldMilliseconds;
            Assert.That(BotBrain.KeepsRvrSupport(11_000, until, true, true, 60), Is.True, "no new search inside the hold");
            Assert.That(BotBrain.KeepsRvrSupport(until, until, true, true, 60), Is.False, "the hold ran out: search again");
            Assert.That(BotBrain.KeepsRvrSupport(11_000, until, false, true, 60), Is.False, "dead");
            Assert.That(BotBrain.KeepsRvrSupport(11_000, until, true, false, 60), Is.False, "out of heal range");
            Assert.That(BotBrain.KeepsRvrSupport(11_000, until, true, true, BotBrain.RvrSupportBelowPercent), Is.False, "healed up");
            Assert.That(BotBrain.RvrSupportHoldOver(11_000, until), Is.False, "nobody needed it: kept as nobody for the hold");
            Assert.That(BotBrain.RvrSupportHoldOver(until, until), Is.True);
            Assert.That(BotBrain.RelicCarrierRefreshMilliseconds, Is.EqualTo(2_000));
        });
    }
}
