using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review M7 / G5 (2026-11-11): the tank companion is the main assist while its player is not
    /// attacking; while the player attacks, companions assist the player as before.</summary>
    [TestFixture]
    public class UT_CompanionMainAssist
    {
        [Test]
        public void TheTankCompanionLeadsOnlyWhileThePlayerIsNotAttacking() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.CompanionMainAssistApplies(true, true, true, true, false), Is.True, "player healing or idle");
            Assert.That(BotBrain.CompanionMainAssistApplies(true, true, true, true, true), Is.False, "player attacking");
            Assert.That(BotBrain.CompanionMainAssistApplies(true, false, true, true, false), Is.False, "a gamebot");
            Assert.That(BotBrain.CompanionMainAssistApplies(true, true, true, false, false), Is.False, "no group");
            Assert.That(BotBrain.CompanionMainAssistApplies(false, true, true, true, false), Is.False, "normal mode");
        });

        [Test]
        public void ThePlayerStillLeadsForFourSecondsAfterTheirLastAttack() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.PlayerLeadsStickyMilliseconds, Is.EqualTo(4_000));
            Assert.That(BotBrain.PlayerStillLeads(10_000, 10_000), Is.True, "attacking now");
            Assert.That(BotBrain.PlayerStillLeads(13_000, 10_000), Is.True, "a caster's gap between casts");
            Assert.That(BotBrain.PlayerStillLeads(14_000, 10_000), Is.False, "the player stopped: the tank leads");
            Assert.That(BotBrain.PlayerStillLeads(10_000, long.MinValue / 2), Is.False, "never attacked");
        });

        [Test]
        public void TheKillTargetRulesAreShared() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.AssistsKillTarget(true, false, 80, false), Is.True);
            Assert.That(BotBrain.AssistsKillTarget(true, true, 80, false), Is.False, "never onto a mezzed monster");
            Assert.That(BotBrain.AssistsKillTarget(true, false, 30, true), Is.False, "low and hit: fights back");
        });
    }
}
