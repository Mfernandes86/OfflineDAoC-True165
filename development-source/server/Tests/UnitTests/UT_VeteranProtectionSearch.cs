using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q10: the group-protection search ran two 2,600-unit area searches on every combat think
    /// of every grouped tank. One answer is now kept for 0.75 s while it still points at a living groupmate.
    /// </summary>
    [TestFixture]
    public class UT_VeteranProtectionSearch
    {
        [Test]
        public void TheAnswerIsKeptForThreeQuartersOfASecondWhileValid() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.ProtectionSearchIntervalMs, Is.EqualTo(750));
            Assert.That(BotBrain.ReuseProtectionAnswer(1000, 1750, true), Is.True);
            Assert.That(BotBrain.ReuseProtectionAnswer(1750, 1750, true), Is.False, "time is up");
            Assert.That(BotBrain.ReuseProtectionAnswer(1000, 1750, false), Is.False, "the monster died or changed target");
            Assert.That(BotBrain.ProtectionAreaScanIntervalMs, Is.GreaterThan(BotBrain.ProtectionSearchIntervalMs));
        });
    }
}
