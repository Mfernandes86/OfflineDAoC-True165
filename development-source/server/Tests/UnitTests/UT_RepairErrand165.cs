using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review Q33: worn gear sends a gamebot to town at the period repair point (85-90%), not at 80%.</summary>
[TestFixture]
public sealed class UT_RepairErrand165
{
    [Test]
    public void RepairErrandFollowsThePeriodRepairPoint()
    {
        Assert.That(TownErrands165.RepairErrandBelowPercent, Is.InRange(85, 90));
    }
}
