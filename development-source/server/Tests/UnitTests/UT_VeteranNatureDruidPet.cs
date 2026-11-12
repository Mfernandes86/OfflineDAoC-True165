using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q24: a Nature Druid's pet was summoned and never fought.</summary>
    [TestFixture]
    public class UT_VeteranNatureDruidPet
    {
        [Test]
        public void ANatureDruidsPetFights() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPetSupport.IsDefensiveOnlyPetClass(eCharacterClass.Druid, false, true), Is.False);
            Assert.That(AutonomousPetSupport.IsDefensiveOnlyPetClass(eCharacterClass.Druid, true, true), Is.False);
            Assert.That(AutonomousPetSupport.IsDefensiveOnlyPetClass(eCharacterClass.Druid, false, false), Is.True,
                "Regrowth and Nurture Druids unchanged");
            Assert.That(AutonomousPetSupport.IsDefensiveOnlyPetClass(eCharacterClass.Minstrel, false, false), Is.True);
        });

        [Test]
        public void NatureSpec() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPetSupport.NatureSpecced(40, 20, 10), Is.True);
            Assert.That(AutonomousPetSupport.NatureSpecced(1, 1, 1), Is.False, "a fresh Druid");
            Assert.That(AutonomousPetSupport.NatureSpecced(20, 40, 10), Is.False);
        });
    }
}
