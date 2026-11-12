using DOL.GS.SkillHandler;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q1: a Hero bot multiplied Stag's duration (already milliseconds) by 1,000, never
    /// started the effect and locked the ability for DURATION * 5000 ms (about 42 hours). The bot now starts the effect
    /// with the handler's own duration and spends the handler's own 15-minute reuse.
    /// </summary>
    [TestFixture]
    public class UT_VeteranStag
    {
        [Test]
        public void StagTimesAreMillisecondsAndTheReuseIsFifteenMinutes() => Assert.Multiple(() =>
        {
            Assert.That(StagAbilityHandler.DURATION, Is.EqualTo(30_000), "30 s in milliseconds");
            Assert.That(StagAbilityHandler.REUSE_TIMER, Is.EqualTo(15 * 60_000), "the player's 15-minute reuse");
            Assert.That(StagAbilityHandler.DURATION * 5000L, Is.GreaterThan(40L * 3600_000), "the old lock-out (about 42 h)");
        });
    }
}
