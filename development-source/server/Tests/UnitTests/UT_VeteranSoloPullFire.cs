using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q28: the solo ranged pull keeps firing while the monster closes.</summary>
    [TestFixture]
    public class UT_VeteranSoloPullFire
    {
        [Test]
        public void AnotherShotOnlyWhenItFinishesBeforeTheMonsterArrives() => Assert.Multiple(() =>
        {
            // 1,200 units at 200 a second: 6 s away - a 3 s cast fits, a 6 s draw does not
            Assert.That(SoloRangedPull165.KeepFiring(1200, 200, 3000), Is.True);
            Assert.That(SoloRangedPull165.KeepFiring(1200, 200, 6000), Is.False);
            Assert.That(SoloRangedPull165.KeepFiring(0, 200, 0), Is.False, "already here");
            Assert.That(SoloRangedPull165.KeepFiring(1200, 0, 0), Is.False, "not coming");
        });

        /// <summary>Owner 2026-11-11: the Cleric's Prima chain - Smite, Smite, a stun (at higher levels), Smite.</summary>
        [Test]
        public void TheClericsStunComesOnceAfterTheSecondSmite() => Assert.Multiple(() =>
        {
            Assert.That(SoloRangedPull165.ChainStunTurn(cleric: true, followUps: 0, stunCast: false), Is.False, "the second Smite first");
            Assert.That(SoloRangedPull165.ChainStunTurn(cleric: true, followUps: 1, stunCast: false), Is.True);
            Assert.That(SoloRangedPull165.ChainStunTurn(cleric: true, followUps: 2, stunCast: true), Is.False, "once a pull");
            Assert.That(SoloRangedPull165.ChainStunTurn(cleric: false, followUps: 1, stunCast: false), Is.False, "the Cleric's chain only");
        });

        [Test]
        public void TheStunMustBuyMoreCastingTimeThanItCosts() => Assert.Multiple(() =>
        {
            // Stunning Flash (level 6, 3 s) and Flare (15, 5 s) cost a 2.5 s cast and hold too briefly for a 3 s Smite
            Assert.That(SoloRangedPull165.ChainStunWorthIt(3000, 2500, 3000), Is.False);
            Assert.That(SoloRangedPull165.ChainStunWorthIt(5000, 2500, 3000), Is.False);
            // Stunning Glare (26, 6 s), Halo (36, 8 s), Aura (46, 9 s): "at higher levels"
            Assert.That(SoloRangedPull165.ChainStunWorthIt(6000, 2500, 3000), Is.True);
            Assert.That(SoloRangedPull165.ChainStunWorthIt(9000, 2500, 3000), Is.True);
            Assert.That(SoloRangedPull165.ChainStunWorthIt(0, 2500, 3000), Is.False);
        });

        [Test]
        public void ASmiteFitsIntoTheRestOfTheStun() => Assert.Multiple(() =>
        {
            Assert.That(SoloRangedPull165.CastFitsStun(5000, 3000), Is.True);
            Assert.That(SoloRangedPull165.CastFitsStun(3100, 3000), Is.False);
            Assert.That(SoloRangedPull165.CastFitsStun(0, 3000), Is.False, "not stunned");
        });
    }
}
