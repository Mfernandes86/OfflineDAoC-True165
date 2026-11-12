using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q5: heal claims were switched off for ordinary bot parties, so two healers of an 8-man
    /// landed on the same target. Every party now shares them; a healer keeps its own triage and only moves off a
    /// target another healer's heal already covers.
    /// </summary>
    [TestFixture]
    public class UT_VeteranHealSplit
    {
        private static BotGroupSupport.OrdinaryHealSplit Split(bool incoming, double projected, double other) =>
            BotGroupSupport.ChooseOrdinaryHealSplit(incoming, projected, other);

        [Test]
        public void NoHealOnTheWayKeepsTheHealersOwnChoice() =>
            Assert.That(Split(false, 99, 10), Is.EqualTo(BotGroupSupport.OrdinaryHealSplit.Preferred));

        [Test]
        public void ATargetStillShortAfterTheIncomingHealGetsTheSecondHeal() =>
            Assert.That(Split(true, 60, 10), Is.EqualTo(BotGroupSupport.OrdinaryHealSplit.Preferred));

        [Test]
        public void ACoveredTargetSendsTheHealerToTheNextHurtMember() =>
            Assert.That(Split(true, 95, 50), Is.EqualTo(BotGroupSupport.OrdinaryHealSplit.Other));

        [Test]
        public void ACoveredTargetWithNobodyElseHurtMeansNoDoubleHeal() => Assert.Multiple(() =>
        {
            Assert.That(Split(true, 95, double.MaxValue), Is.EqualTo(BotGroupSupport.OrdinaryHealSplit.Skip));
            Assert.That(Split(true, 90, 85), Is.EqualTo(BotGroupSupport.OrdinaryHealSplit.Skip), "others at 80%+ are fine");
        });
    }
}
