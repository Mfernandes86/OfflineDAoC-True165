using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q6, with the owner's clarification: companions on /defensive ignored anything more than
    /// 350 units from the player, even an add hitting the healer. The 350 limit still governs starting fights and every
    /// pull (/pull, /grind, a player-led pull); the wider 2,000 range is only for a monster already in melee on a
    /// groupmate who is not pulling.
    /// </summary>
    [TestFixture]
    public class UT_VeteranDefensiveStance
    {
        // (within 350, on this companion, on a groupmate, within 2,000 of the player, in melee on its victim, pull in progress)

        [Test]
        public void APulledMonsterOnThePullerAt1500DoesNotWidenTheLeash() => Assert.Multiple(() =>
        {
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, false, true, true, true, true), Is.False,
                "the /pull or /grind target hitting the puller 1,500 units out: the group waits as before");
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, true, false, true, true, true), Is.False,
                "not even when this companion is the puller");
        });

        [Test]
        public void AnAddHittingTheHealerAt900IsAnswered() =>
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, false, true, true, true, false), Is.True);

        [Test]
        public void AMonsterOnItsWayInIsNotAnswered() =>
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, false, true, true, false, false), Is.False,
                "not in melee yet: wait for the 350 leash");

        [Test]
        public void ItsOwnMeleeAttackerIsAnswered() => Assert.Multiple(() =>
        {
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, true, false, false, true, false), Is.True);
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, false, true, false, true, false), Is.False,
                "a groupmate's attacker beyond 2,000 of the player");
            Assert.That(CompanionEngagementMode.DefendGroupRadius, Is.EqualTo(2000));
        });

        [Test]
        public void NothingStartsFarAwayAndTheOldRadiusStillWorks() => Assert.Multiple(() =>
        {
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, false, false, true, false, false), Is.False);
            Assert.That(CompanionEngagementMode.DefensiveAnswers(true, false, false, false, false, true), Is.True,
                "inside 350 everything works as today, pulls included");
        });
    }
}
