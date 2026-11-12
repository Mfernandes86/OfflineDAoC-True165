using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Veteran review M4: a party's meetup plan is estimated outside the coordinator lock and committed only when the
/// session is still the one it was worked out from.
/// </summary>
[TestFixture]
public sealed class UT_MeetupPlanOutsideLock
{
    private static readonly AutonomousBotGroupCoordinator.MeetupSnapshot Planned =
        new(51, new Vector3(1_000, 2_000, 3_000), "Leader staging", 42);

    [Test]
    public void UnchangedSessionCommits()
    {
        Assert.That(Planned.Matches(51, new Vector3(1_000, 2_000, 3_000), "Leader staging", 42), Is.True);
    }

    [Test]
    public void AnyChangeWhileEstimatingIsRetriedLater()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Planned.Matches(52, new Vector3(1_000, 2_000, 3_000), "Leader staging", 42), Is.False, "region");
            Assert.That(Planned.Matches(51, new Vector3(1_001, 2_000, 3_000), "Leader staging", 42), Is.False, "rendezvous");
            Assert.That(Planned.Matches(51, new Vector3(1_000, 2_000, 3_000), "Meeting up", 42), Is.False, "phase");
            Assert.That(Planned.Matches(51, new Vector3(1_000, 2_000, 3_000), "Leader staging", 43), Is.False, "members");
        });
    }
}
