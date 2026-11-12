using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Owner 2026-11-11 (replaces the veteran review's Q25): a grouped Spiritmaster's pet fights the group's
    /// target and picks up an add heading for the casters and healers.</summary>
    [TestFixture]
    public class UT_VeteranSpiritmasterPet
    {
        [Test]
        public void ThePetGuardsTheCastersOnlyWithATankInTheGroup() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPetSupport.PetGuardsCasters(true, true, true), Is.True);
            Assert.That(AutonomousPetSupport.PetGuardsCasters(true, false, true), Is.False, "solo: the pet is the tank");
            Assert.That(AutonomousPetSupport.PetGuardsCasters(true, true, false), Is.False, "no tank: the pet tanks");
            Assert.That(AutonomousPetSupport.PetGuardsCasters(false, true, true), Is.False, "True 1.65 Mode only");
        });

        [Test]
        public void ThePetAssistsByDefaultAndPicksUpAnAdd() => Assert.Multiple(() =>
        {
            var fight = (GameNPC)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            var add = (GameNPC)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            Assert.That(AutonomousPetSupport.PetTargetWithAddPickup(fight, null), Is.SameAs(fight), "no add: it fights the group's target");
            Assert.That(AutonomousPetSupport.PetTargetWithAddPickup(fight, add), Is.SameAs(add), "an add on a caster comes first");
            Assert.That(AutonomousPetSupport.PetTargetWithAddPickup(null, null), Is.Null);
        });

        [Test]
        public void TakeAddsIsOnlyThePlayersChoice() => Assert.Multiple(() =>
        {
            Assert.That(CompanionControls165.PetTakesAdds(CompanionControls165.PetTakeAdds, false), Is.True);
            Assert.That(CompanionControls165.PetTakesAdds(CompanionControls165.PetAutomatic, false), Is.False,
                "Automatic assists (owner 2026-11-11)");
            Assert.That(CompanionControls165.PetTakesAdds(CompanionControls165.PetAttack, false), Is.False);
        });
    }
}
