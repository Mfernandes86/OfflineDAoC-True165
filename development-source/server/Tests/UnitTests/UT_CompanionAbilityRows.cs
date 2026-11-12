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
        public void TripleWieldFollowsItsRowLikeStag() => Assert.Multiple(() =>
        {
            Assert.That(TripleWieldWanted(StagAutomatic, true), Is.True, "hard fight");
            Assert.That(TripleWieldWanted(StagAutomatic, false), Is.False, "Automatic saves it");
            Assert.That(TripleWieldWanted(StagOnCooldown, false), Is.True);
            Assert.That(TripleWieldWanted(StagOff, true), Is.False);
        });

        [Test]
        public void GamebotsKeepTheirOpeners() => Assert.Multiple(() =>
        {
            Assert.That(CriticalShotAllowed(null), Is.True);
            Assert.That(OpeningStunAllowed(null), Is.True);
            Assert.That(EngageAllowed(null), Is.True, "gamebots keep Engage");
        });

        [Test]
        public void TheRowsAreDeclared() => Assert.Multiple(() =>
        {
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindStag)?.Key, Is.EqualTo("stag"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindCriticalShot)?.Key, Is.EqualTo("critshot"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindOpeningStun)?.Key, Is.EqualTo("openstun"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindStag)?.Choices.Length, Is.EqualTo(3));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindEngage)?.Key, Is.EqualTo("engage"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindEngage)?.Choices, Is.EqualTo(new[] { "Automatic", "Off" }));
            Assert.That(CompanionOptions165.KindEngage, Is.EqualTo(50), "the kind the client protocol comment names");
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindTripleWield)?.Key, Is.EqualTo("triplewield"));
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindTripleWield)?.Choices,
                Is.EqualTo(new[] { "Automatic (hard fights)", "On cooldown", "Off" }));
        });
    }
}
