using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionControls165;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 / G12 (2026-11-11): a pure caster hit in melee walks once to its group's tank instead of
    /// staff-meleeing at once.</summary>
    [TestFixture]
    public class UT_CasterWalksToTank
    {
        [Test]
        public void WalksOnceToANearTank() => Assert.Multiple(() =>
        {
            Assert.That(CasterWalksToTank(true, true, 600, false), Is.True);
            Assert.That(CasterWalksToTank(true, true, 600, true), Is.False, "only once per attacker");
            Assert.That(CasterWalksToTank(true, true, CasterBesideTank, false), Is.False, "already at the tank's side");
            Assert.That(CasterWalksToTank(true, true, CasterToTankReach + 1, false), Is.False, "tank too far: fight back");
            Assert.That(CasterWalksToTank(true, false, 600, false), Is.False, "no living tank");
            Assert.That(CasterWalksToTank(false, true, 600, false), Is.False, "hybrids and healers unchanged");
        });
    }
}
