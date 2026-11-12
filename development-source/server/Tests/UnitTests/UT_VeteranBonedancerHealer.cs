using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q17: a Bonedancer always brings its healer minion, summoned first.</summary>
    [TestFixture]
    public class UT_VeteranBonedancerHealer
    {
        [Test]
        public void TheHealerMinionComesFirst() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPetSupport.BonedancerHealerFirst(false, true), Is.True);
            Assert.That(AutonomousPetSupport.BonedancerHealerFirst(true, true), Is.False, "one is out already");
            Assert.That(AutonomousPetSupport.BonedancerHealerFirst(false, false), Is.False, "not learned yet");
        });
    }
}
