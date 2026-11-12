using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q3: a Cleric's attack spells used a random roll (max(5, power - 50)%) and walked in with
    /// the mace whenever it failed. Now: a Smite Cleric fights from spell range, and every Cleric keeps a fixed power
    /// reserve - bigger in a group, where its power belongs to the heals.
    /// </summary>
    [TestFixture]
    public class UT_VeteranSmiteCleric
    {
        [Test]
        public void SmiteSpecIsSmiteTrainedAtLeastAsHighAsRejuvenation() => Assert.Multiple(() =>
        {
            Assert.That(BotCasterPriority.SmiteSpecced(1, 1), Is.False, "a fresh Cleric is not a Smite Cleric");
            Assert.That(BotCasterPriority.SmiteSpecced(29, 23), Is.True);
            Assert.That(BotCasterPriority.SmiteSpecced(23, 23), Is.True);
            Assert.That(BotCasterPriority.SmiteSpecced(7, 40), Is.False, "a Rejuvenation Cleric");
        });

        [Test]
        public void GroupedClericsKeepAHealReserve() => Assert.Multiple(() =>
        {
            Assert.That(BotCasterPriority.ClericOffenceAllowed(false, true, 61), Is.True);
            Assert.That(BotCasterPriority.ClericOffenceAllowed(false, true, 60), Is.False, "support Cleric keeps 60%");
            Assert.That(BotCasterPriority.ClericOffenceAllowed(true, true, 36), Is.True);
            Assert.That(BotCasterPriority.ClericOffenceAllowed(true, true, 35), Is.False, "Smite Cleric keeps 35%");
        });

        [Test]
        public void SoloSmiteClericNukesDownToTheOutOfPowerWait() => Assert.Multiple(() =>
        {
            Assert.That(BotCasterPriority.ClericOffenceAllowed(true, false, 1), Is.True);
            Assert.That(BotCasterPriority.ClericOffenceAllowed(true, false, 0), Is.False);
            Assert.That(BotCasterPriority.ClericOffenceAllowed(false, false, 31), Is.True);
            Assert.That(BotCasterPriority.ClericOffenceAllowed(false, false, 30), Is.False, "solo support keeps its own heals");
        });

        [Test]
        public void TheDecisionIsTheSameEveryTime()
        {
            for (int i = 0; i < 50; i++)
                Assert.That(BotCasterPriority.ClericOffenceAllowed(false, true, 80), Is.True, "no random roll");
        }
    }
}
