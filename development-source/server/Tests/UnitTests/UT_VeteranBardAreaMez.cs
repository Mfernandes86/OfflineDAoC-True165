using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q26: Bards use their area mez on clustered adds, never near the kill target.</summary>
    [TestFixture]
    public class UT_VeteranBardAreaMez
    {
        [Test]
        public void AreaMezOnTwoOrMoreSafeAddsOnly() => Assert.Multiple(() =>
        {
            Assert.That(BardBotCrowdControlPolicy.AreaMezWorthwhile(2, false, false), Is.True);
            Assert.That(BardBotCrowdControlPolicy.AreaMezWorthwhile(1, false, false), Is.False, "one add: the single mez");
            Assert.That(BardBotCrowdControlPolicy.AreaMezWorthwhile(3, true, false), Is.False, "the kill target is near");
            Assert.That(BardBotCrowdControlPolicy.AreaMezWorthwhile(3, false, true), Is.False, "it would wake or pull something");
        });
    }
}
