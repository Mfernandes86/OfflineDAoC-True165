using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionControls165;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 (2026-11-11): the Stag, Critical Shot opener and Opening stun AI rows.</summary>
    [TestFixture]
    public class UT_CompanionAbilityRows
    {
        [Test]
        public void StagFollowsItsRow() => Assert.Multiple(() =>
        {
            Assert.That(StagWanted(StagAutomatic, true), Is.True, "hard fight");
            Assert.That(StagWanted(StagAutomatic, false), Is.False, "Automatic saves it");
            Assert.That(StagWanted(StagOnCooldown, false), Is.True);
            Assert.That(StagWanted(StagOff, true), Is.False);
        });

        [Test]
        public void GamebotsKeepTheirOpeners() => Assert.Multiple(() =>
        {
            Assert.That(CriticalShotAllowed(null), Is.True);
            Assert.That(OpeningStunAllowed(null), Is.True);
        });

        [Test]
        public void TheRowsAreDeclared() => Assert.Multiple(() =>
        {
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindStag)?.Key, Is.EqualTo("stag"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindCriticalShot)?.Key, Is.EqualTo("critshot"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindOpeningStun)?.Key, Is.EqualTo("openstun"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindStag)?.Choices.Length, Is.EqualTo(3));
        });
    }
}
