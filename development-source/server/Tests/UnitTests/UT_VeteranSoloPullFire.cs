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
    }
}
