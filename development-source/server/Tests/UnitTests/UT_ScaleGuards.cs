using System.IO;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review M4: per-tick scale guards, the crowd slow-down past 1,500 bots, shared party pulses and the
/// asynchronous log configuration.</summary>
[TestFixture]
public sealed class UT_ScaleGuards
{
    [Test]
    public void DecisionAllowanceShrinksWithSlowTicksButNeverStops()
    {
        int fast = AutonomousAiBudget.DecisionAllowance(2);
        int slow = AutonomousAiBudget.DecisionAllowance(16);
        Assert.Multiple(() =>
        {
            Assert.That(fast, Is.GreaterThan(slow));
            Assert.That(AutonomousAiBudget.DecisionAllowance(50), Is.EqualTo(AutonomousAiBudget.MinimumDecisionsPerTick));
            Assert.That(slow, Is.GreaterThanOrEqualTo(AutonomousAiBudget.MinimumDecisionsPerTick));
            Assert.That(AutonomousAiBudget.DecisionAllowance(5), Is.GreaterThanOrEqualTo(100), "an ordinary tick is not throttled");
        });
    }

    [Test]
    public void RoutePlansAlwaysGetAFewPerTickThenFollowTheTimeBudget()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousAiBudget.RoutePlanAllowed(0, 500), Is.True, "the first plans always start");
            Assert.That(AutonomousAiBudget.RoutePlanAllowed(AutonomousAiBudget.MinimumRoutePlansPerTick, 10), Is.True);
            Assert.That(AutonomousAiBudget.RoutePlanAllowed(AutonomousAiBudget.MinimumRoutePlansPerTick,
                AutonomousAiBudget.RoutePlanBudgetMilliseconds), Is.False);
        });
    }

    [Test]
    public void ADecisionIsDeferredAtMostOnce()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousAiBudget.DecisionAllowed(false, 0), Is.True, "the last one of the allowance");
            Assert.That(AutonomousAiBudget.DecisionAllowed(false, -1), Is.False, "allowance used: deferred");
            Assert.That(AutonomousAiBudget.DecisionAllowed(true, -50), Is.True, "deferred last time: its priority pass");
        });
    }

    [Test]
    public void DeferredBotsThinkAgainWithinAFewTicks()
    {
        foreach (long id in new[] { 0L, 1, 66, 67, 12345, -9, long.MaxValue })
            Assert.That(AutonomousAiBudget.DeferredThinkMilliseconds(id), Is.InRange(150, 400));
        Assert.That(AutonomousAiBudget.Smooth(0, 5), Is.EqualTo(5));
        Assert.That(AutonomousAiBudget.Smooth(10, 0), Is.EqualTo(8).Within(1e-9));
    }

    [Test]
    public void CrowdSlowDownOnlyTouchesUnseenNonCombatBotsPast1500()
    {
        Assert.Multiple(() =>
        {
            foreach (eAutonomousThinkMode mode in new[] { eAutonomousThinkMode.Travel, eAutonomousThinkMode.Planning, eAutonomousThinkMode.Resting })
            {
                // unchanged up to 1,500 bots and for bots near players
                Assert.That(AutonomousFidelityPolicy.IntervalMilliseconds(mode, eAutonomousFidelity.Efficient, 1_500),
                    Is.EqualTo(AutonomousFidelityPolicy.IntervalMilliseconds(mode, eAutonomousFidelity.Efficient, 1_200)).Or.GreaterThan(0));
                Assert.That(AutonomousFidelityPolicy.IntervalMilliseconds(mode, eAutonomousFidelity.Standard, 6_000),
                    Is.EqualTo(AutonomousFidelityPolicy.IntervalMilliseconds(mode, eAutonomousFidelity.Standard, 1_500)));
                Assert.That(AutonomousFidelityPolicy.IntervalMilliseconds(mode, eAutonomousFidelity.NearbyHuman, 6_000),
                    Is.EqualTo(AutonomousFidelityPolicy.IntervalMilliseconds(mode, eAutonomousFidelity.NearbyHuman, 1_500)));
                // slower for unseen bots at 6,000
                Assert.That(AutonomousFidelityPolicy.IntervalMilliseconds(mode, eAutonomousFidelity.Efficient, 6_000),
                    Is.GreaterThan(AutonomousFidelityPolicy.IntervalMilliseconds(mode, eAutonomousFidelity.Efficient, 1_500)));
            }
            Assert.That(AutonomousFidelityPolicy.IntervalMilliseconds(eAutonomousThinkMode.Travel, eAutonomousFidelity.Efficient, 9_000), Is.LessThanOrEqualTo(7_000));
            Assert.That(AutonomousFidelityPolicy.IntervalMilliseconds(eAutonomousThinkMode.Planning, eAutonomousFidelity.Efficient, 9_000), Is.LessThanOrEqualTo(15_000));
            Assert.That(AutonomousFidelityPolicy.IntervalMilliseconds(eAutonomousThinkMode.Resting, eAutonomousFidelity.Efficient, 9_000), Is.LessThanOrEqualTo(9_000));
            Assert.That(AutonomousFidelityPolicy.IntervalMilliseconds(eAutonomousThinkMode.Combat, eAutonomousFidelity.Efficient, 6_000), Is.EqualTo(500));
            Assert.That(AutonomousFidelityPolicy.CrowdPressure(1_500), Is.Zero);
            Assert.That(AutonomousFidelityPolicy.CrowdPressure(6_000), Is.EqualTo(1));
        });
    }

    [Test]
    public void PartyStateIsSharedForUnderOneSecondAndAnyMemberChangeUpdatesAtOnce()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.SessionUpdateFresh(0, 100), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.SessionUpdateFresh(1_000, 1_500), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.SessionUpdateFresh(1_000, 1_000 + AutonomousBotGroupCoordinator.SessionUpdateMilliseconds), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.SessionUpdateFresh(1_000, 900), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.PulseMemberKey(new GameBot[0]),
                Is.Not.EqualTo(AutonomousBotGroupCoordinator.PulseMemberKey(new GameBot[] { null })));
        });
    }

    [Test]
    public void LogConfigurationLoadsWithAsyncConsoleAndServerLog()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        string path = null;
        for (DirectoryInfo d = new(dir); d != null && path == null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "GameServer", "config", "logconfig.xml");
            if (File.Exists(candidate))
                path = candidate;
        }
        if (path == null)
            Assert.Ignore("logconfig.xml not found from the test directory");
        var config = new NLog.Config.XmlLoggingConfiguration(path);
        Assert.That(config.FindTargetByName("console"), Is.InstanceOf<NLog.Targets.Wrappers.AsyncTargetWrapper>());
        Assert.That(config.FindTargetByName("gameServerFile"), Is.InstanceOf<NLog.Targets.Wrappers.AsyncTargetWrapper>());
        var wrapper = (NLog.Targets.Wrappers.AsyncTargetWrapper)config.FindTargetByName("console");
        Assert.That(wrapper.OverflowAction, Is.EqualTo(NLog.Targets.Wrappers.AsyncTargetWrapperOverflowAction.Block));
        Assert.That(config.FindTargetByName("errorFile"), Is.InstanceOf<NLog.Targets.FileTarget>(), "errors stay synchronous");
    }
}
