using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q2: bolts open a fight or go at a target nobody else is meleeing.</summary>
    [TestFixture]
    public class UT_VeteranBolts
    {
        [Test]
        public void BoltsOnlyIntoAFreeTarget() => Assert.Multiple(() =>
        {
            Assert.That(BotCasterPriority.BoltWorthwhile(0), Is.True, "opener or solo");
            Assert.That(BotCasterPriority.BoltWorthwhile(1), Is.False, "the tank is on it");
            Assert.That(BotCasterPriority.BoltWorthwhile(3), Is.False);
        });
    }
}
