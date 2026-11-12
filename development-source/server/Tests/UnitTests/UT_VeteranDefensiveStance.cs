using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q6: companions on /defensive ignored anything more than 350 units from the player, even
    /// a monster hitting the healer or the companion itself. The 350 limit now only governs starting fights.
    /// </summary>
    [TestFixture]
    public class UT_VeteranDefensiveStance
    {
        [Test]
        public void DefensiveStillStartsNothingFarAway() =>
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, false, false, true), Is.False);

        [Test]
        public void DefensiveAlwaysAnswersItsOwnAttacker() =>
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, true, false, false), Is.True);

        [Test]
        public void DefensiveAnswersAMonsterOnAGroupmateWithinTwoThousand() => Assert.Multiple(() =>
        {
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, false, true, true), Is.True);
            Assert.That(CompanionEngagementMode.DefensiveAnswers(false, false, true, false), Is.False, "too far from the player");
            Assert.That(CompanionEngagementMode.DefendGroupRadius, Is.EqualTo(2000));
        });

        [Test]
        public void TheOldRadiusStillWorks() =>
            Assert.That(CompanionEngagementMode.DefensiveAnswers(true, false, false, false), Is.True);
    }
}
