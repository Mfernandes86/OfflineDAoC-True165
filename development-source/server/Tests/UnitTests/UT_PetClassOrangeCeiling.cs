using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Owner decision O7 (2026-11-11): pet classes may hunt orange solo while their pet is up, behind the server
    /// property bot_pet_class_orange, OFF by default.</summary>
    [TestFixture]
    public class UT_PetClassOrangeCeiling
    {
        [Test]
        public void TheSwitchIsOffByDefault() => Assert.That(AutonomousLowLevelTargetPolicy.PetClassOrange, Is.False);

        [Test]
        public void OffChangesNothing() =>
            Assert.That(AutonomousLowLevelTargetPolicy.PetUpCeiling(ConColor.YELLOW, (int)eCharacterClass.Enchanter, true, false),
                Is.EqualTo(ConColor.YELLOW));

        [Test]
        public void OnAPetClassWithItsPetUpHuntsOrange() => Assert.Multiple(() =>
        {
            foreach (eCharacterClass petClass in new[] { eCharacterClass.Cabalist, eCharacterClass.Enchanter,
                         eCharacterClass.Spiritmaster, eCharacterClass.Bonedancer, eCharacterClass.Necromancer, eCharacterClass.Druid })
                Assert.That(AutonomousLowLevelTargetPolicy.PetUpCeiling(ConColor.YELLOW, (int)petClass, true, true),
                    Is.EqualTo(ConColor.ORANGE), petClass.ToString());
        });

        [Test]
        public void NoPetOrNoPetClassOrABlueCeilingStays() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousLowLevelTargetPolicy.PetUpCeiling(ConColor.YELLOW, (int)eCharacterClass.Cabalist, false, true),
                Is.EqualTo(ConColor.YELLOW), "pet down");
            Assert.That(AutonomousLowLevelTargetPolicy.PetUpCeiling(ConColor.YELLOW, (int)eCharacterClass.Armsman, true, true),
                Is.EqualTo(ConColor.YELLOW), "not a pet class");
            Assert.That(AutonomousLowLevelTargetPolicy.PetUpCeiling(ConColor.BLUE, (int)eCharacterClass.Cabalist, true, true),
                Is.EqualTo(ConColor.BLUE), "level-1 blue ceiling");
        });
    }
}
