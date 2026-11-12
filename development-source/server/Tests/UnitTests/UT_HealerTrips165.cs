using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review Q29: a healer is worth its own trip only for a big Constitution loss or long sickness; a smaller
/// loss is bought back when the bot is in town anyway.</summary>
[TestFixture]
public sealed class UT_HealerTrips165
{
    [Test]
    public void SmallLossIsBoughtBackOnlyWhenInTownAnyway()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DeathPenalty165.ShouldVisitHealer(1, true, 0, alreadyInTown: false), Is.False);
            Assert.That(DeathPenalty165.ShouldVisitHealer(2, true, 0, alreadyInTown: false), Is.False);
            Assert.That(DeathPenalty165.ShouldVisitHealer(1, true, 0, alreadyInTown: true), Is.True);
        });
    }

    [Test]
    public void ThreePointsOrLongSicknessIsWorthATrip()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DeathPenalty165.ShouldVisitHealer(3, true, 0, alreadyInTown: false), Is.True);
            Assert.That(DeathPenalty165.ShouldVisitHealer(0, false, 120_001, alreadyInTown: false), Is.True);
            Assert.That(DeathPenalty165.ShouldVisitHealer(0, false, 120_000, alreadyInTown: false), Is.False);
        });
    }

    [Test]
    public void BrokeBotsStillWaitTheLossOut()
    {
        Assert.That(DeathPenalty165.ShouldVisitHealer(3, false, 0, alreadyInTown: false), Is.False);
        Assert.That(DeathPenalty165.ShouldVisitHealer(3, false, 0, alreadyInTown: true), Is.False);
    }
}
