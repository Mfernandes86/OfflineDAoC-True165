using DOL.GS;
using NUnit.Framework;
using static DOL.GS.CompanionControls165;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner decision O2 (2026-11-11): /passive keeps the lifesavers - instant heals and cure mez while the player moves,
    /// normal heals when the player stops, the speed song or chant, escape abilities - and still no fighting or long casts.
    /// The old /bot stance passive takes the same path.
    /// </summary>
    [TestFixture]
    public class UT_CompanionPassiveLifesavers
    {
        [Test]
        public void WhileThePlayerMovesOnlyInstantHealsAndCureMez() => Assert.Multiple(() =>
        {
            Assert.That(PassiveAllowsHeal(PassiveHealing.WhileMoving, true, false), Is.True, "instant heal");
            Assert.That(PassiveAllowsHeal(PassiveHealing.WhileMoving, false, true), Is.True, "cure mez");
            Assert.That(PassiveAllowsHeal(PassiveHealing.WhileMoving, false, false), Is.False, "cast-time heal");
        });

        [Test]
        public void WhenThePlayerStopsNormalHeals() => Assert.Multiple(() =>
        {
            Assert.That(PassiveAllowsHeal(PassiveHealing.Stopped, false, false), Is.True);
            Assert.That(PassiveAllowsHeal(PassiveHealing.None, true, false), Is.False, "not passive: no limit applied here");
        });

        [Test]
        public void PassiveLetsOnlyLifesaverCastsFinish() => Assert.Multiple(() =>
        {
            Assert.That(PassiveKeepsCast(true, false, false, false, false, true), Is.True, "charm");
            Assert.That(PassiveKeepsCast(false, true, false, false, false, true), Is.True, "speed song");
            Assert.That(PassiveKeepsCast(false, false, true, false, false, true), Is.True, "cure mez");
            Assert.That(PassiveKeepsCast(false, false, false, true, true, true), Is.True, "instant heal on the move");
            Assert.That(PassiveKeepsCast(false, false, false, true, false, false), Is.True, "cast-time heal, player still");
            Assert.That(PassiveKeepsCast(false, false, false, true, false, true), Is.False, "cast-time heal, player moving");
            Assert.That(PassiveKeepsCast(false, false, false, false, false, false), Is.False, "a nuke or a buff stops");
        });

        [Test]
        public void RetreatingUsesOnlyTheEscapes() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotRealmAbilities.WantsWhileRetreating(AutonomousBotRealmAbilities.Vanish, true, 40, false, true),
                Is.True);
            Assert.That(AutonomousBotRealmAbilities.WantsWhileRetreating(AutonomousBotRealmAbilities.Vanish, true, 80, false, true),
                Is.False, "not hurt yet");
            Assert.That(AutonomousBotRealmAbilities.WantsWhileRetreating(AutonomousBotRealmAbilities.SpeedOfSound, false, 100, false, true),
                Is.True, "the group is being hit");
            Assert.That(AutonomousBotRealmAbilities.WantsWhileRetreating(AutonomousBotRealmAbilities.SpeedOfSound, false, 100, false, false),
                Is.False);
            Assert.That(AutonomousBotRealmAbilities.WantsWhileRetreating(AutonomousBotRealmAbilities.Purge, true, 90, true, true),
                Is.True, "mezzed in a fight");
            Assert.That(AutonomousBotRealmAbilities.WantsWhileRetreating(AutonomousBotRealmAbilities.BunkerOfFaith, true, 10, false, true),
                Is.False, "never a fight active");
            Assert.That(AutonomousBotRealmAbilities.RetreatActives, Does.Not.Contain(AutonomousBotRealmAbilities.MasteryOfConcentration));
        });
    }
}
