using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review Q30: shared planning answers are reused for a short, bounded time.</summary>
[TestFixture]
public sealed class UT_PlanningCaches
{
    [Test]
    public void RvrPlanningViewIsSharedForUnderOneSecond()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousWorldBotController.RvrPlanningViewMilliseconds, Is.InRange(500, 1_000));
            Assert.That(AutonomousWorldBotController.RvrPlanningViewExpired(10_000, 10_100), Is.False);
            Assert.That(AutonomousWorldBotController.RvrPlanningViewExpired(10_000,
                10_000 + AutonomousWorldBotController.RvrPlanningViewMilliseconds), Is.True);
            // a clock that went backwards (server restart in tests) rebuilds at once
            Assert.That(AutonomousWorldBotController.RvrPlanningViewExpired(10_000, 9_000), Is.True);
        });
    }

    [Test]
    public void ReachableRegionsAreReusedForMinutesNotHours()
    {
        Assert.That(AutonomousWorldBotController.ReachableRegionsCacheMilliseconds, Is.InRange(60_000, 15 * 60_000));
    }
}
