using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>O4 (owner 2026-11-11): a solo bot stands once it has enough for the next pull; endurance 90%+ is full.</summary>
    [TestFixture]
    public class UT_RestNextPullThresholds
    {
        [TestCase(ConColor.GREY, 65, 45)]
        [TestCase(ConColor.GREEN, 65, 45)]
        [TestCase(ConColor.BLUE, 80, 65)]
        [TestCase(ConColor.YELLOW, 100, 100)]
        [TestCase(ConColor.ORANGE, 100, 100)]
        [TestCase(ConColor.RED, 100, 100)]
        [TestCase(ConColor.PURPLE, 100, 100)]
        [TestCase(ConColor.UNKNOWN, 100, 100)]
        public void StandTargetFollowsTheNextPullsCon(ConColor con, int health, int power)
        {
            Assert.That(AutonomousRestPolicy.StandTargetForNextPull(con), Is.EqualTo(((byte)health, (byte)power)));
        }

        [Test]
        public void NoPullYetOrAfterADeathRestsToFull()
        {
            Assert.That(AutonomousRestPolicy.StandTargetForNextPull(null), Is.EqualTo(((byte)100, (byte)100)));
            Assert.That(AutonomousRestPolicy.EnoughForNextPull(99, 100, 100, true, (ConColor?)null), Is.False);
            Assert.That(AutonomousRestPolicy.EnoughForNextPull(100, 100, 90, true, (ConColor?)null), Is.True);
        }

        [TestCase(65, 45, 90, true)]
        [TestCase(64, 100, 100, false)]
        [TestCase(100, 44, 100, false)]
        [TestCase(100, 100, 89, false)]
        public void GreenNextPull(int hp, int power, int endurance, bool enough)
        {
            Assert.That(AutonomousRestPolicy.EnoughForNextPull((byte)hp, (byte)power, (byte)endurance, true, ConColor.GREEN),
                Is.EqualTo(enough));
        }

        [TestCase(80, 65, 95, true)]
        [TestCase(79, 100, 100, false)]
        [TestCase(100, 64, 100, false)]
        [TestCase(70, 45, 100, false)]
        public void BlueNextPull(int hp, int power, int endurance, bool enough)
        {
            Assert.That(AutonomousRestPolicy.EnoughForNextPull((byte)hp, (byte)power, (byte)endurance, true, ConColor.BLUE),
                Is.EqualTo(enough));
        }

        [TestCase(99, 100, 100, false)]
        [TestCase(100, 99, 100, false)]
        [TestCase(100, 100, 90, true)]
        public void YellowNextPullStillRestsToFull(int hp, int power, int endurance, bool enough)
        {
            Assert.That(AutonomousRestPolicy.EnoughForNextPull((byte)hp, (byte)power, (byte)endurance, true, ConColor.YELLOW),
                Is.EqualTo(enough));
        }

        [Test]
        public void ANonCasterIgnoresItsEmptyPowerBar()
        {
            Assert.That(AutonomousRestPolicy.EnoughForNextPull(65, 0, 90, false, ConColor.GREEN), Is.True);
            Assert.That(AutonomousRestPolicy.EnoughForNextPull(100, 0, 90, false, ConColor.YELLOW), Is.True);
        }

        [TestCase(100, 100, 89, false)]
        [TestCase(100, 100, 90, true)]
        [TestCase(100, 100, 98, true)]
        public void EnduranceFromNinetyPercentCountsAsFull(int hp, int power, int endurance, bool full)
        {
            Assert.That(AutonomousRestPolicy.IsFullyRecovered((byte)hp, (byte)power, (byte)endurance, true), Is.EqualTo(full));
        }

        [Test]
        public void TheLog98PercentEnduranceSitNoLongerHappens()
        {
            // BOT_REST_BEGIN hp=100% power=0% endurance=98% (veteran review P8): full already, so no rest begins
            Assert.That(AutonomousRestPolicy.IsFullyRecovered(100, 0, 98, false), Is.True);
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, false, true, 100, 0, 98, false), Is.False);
        }

        [Test]
        public void HealthAndPowerStillRestToFullWhenSeated()
        {
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, false, true, 99, 100, 100, true), Is.True);
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, false, true, 100, 99, 100, true), Is.True);
        }

        [Test]
        public void FullStandTargetMatchesFullRecovery()
        {
            for (int value = 0; value <= 100; value += 5)
            {
                byte v = (byte)value;
                Assert.That(AutonomousRestPolicy.EnoughForNextPull(v, v, v, true, 100, 100),
                    Is.EqualTo(AutonomousRestPolicy.IsFullyRecovered(v, v, v, true)), $"at {value}%");
            }
        }
    }
}
