using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q20: a resisted mez is tried again at once, not 15 s later.</summary>
    [TestFixture]
    public class UT_VeteranMezRetry
    {
        [Test]
        public void TheAddIsLeftAloneOnlyUntilTheCastLands() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.AddControlRetryMilliseconds(2_500), Is.EqualTo(3_500));
            Assert.That(BotBrain.AddControlRetryMilliseconds(0), Is.EqualTo(1_500), "an instant still gets a moment to land");
            Assert.That(BotBrain.AddControlRetryMilliseconds(3_000), Is.LessThan(15_000));
        });
    }
}
