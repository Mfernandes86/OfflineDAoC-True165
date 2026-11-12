using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q18: melee hybrids put one debuff on fights that last, none on easy ones.</summary>
    [TestFixture]
    public class UT_VeteranHybridDebuffs
    {
        [Test]
        public void OneDebuffOnAFightThatLasts() => Assert.Multiple(() =>
        {
            Assert.That(BotCasterPriority.MeleeHybridDebuffAllowed(true, 0, 0), Is.True);
            Assert.That(BotCasterPriority.MeleeHybridDebuffAllowed(true, 1, 0), Is.False, "only one");
            Assert.That(BotCasterPriority.MeleeHybridDebuffAllowed(false, 0, 0), Is.False, "greys and blues: none");
        });

        [Test]
        public void APlayersPickedDebuffsAreAllCast() => Assert.Multiple(() =>
        {
            Assert.That(BotCasterPriority.MeleeHybridDebuffAllowed(true, 2, 3), Is.True);
            Assert.That(BotCasterPriority.MeleeHybridDebuffAllowed(false, 0, 3), Is.False, "still only on fights that last");
        });
    }
}
