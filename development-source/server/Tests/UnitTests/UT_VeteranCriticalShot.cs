using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q15: a Hunter sent its pet first, so the monster was already in combat and the Critical
    /// Shot opener never fired. The pet now waits at heel for the opener (at most 7 s).
    /// </summary>
    [TestFixture]
    public class UT_VeteranCriticalShot
    {
        [Test]
        public void ThePetWaitsForTheOpenerOnAnIdleMonster() =>
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, true, false, 0), Is.True);

        [Test]
        public void ThePetGoesOnceTheOpenerIsNotPossibleOrTooSlow() => Assert.Multiple(() =>
        {
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(false, true, true, false, 0), Is.False, "no Critical Shot");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, false, true, false, 0), Is.False, "no bow in hand");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, false, false, 0), Is.False, "already fighting");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, true, true, 0), Is.False, "pet already on it");
            Assert.That(BotRangedCombat.HoldPetForCriticalShot(true, true, true, false,
                BotRangedCombat.CriticalShotPetHoldMilliseconds), Is.False, "held long enough");
        });
    }
}
