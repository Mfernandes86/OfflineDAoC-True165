using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Owner decision O9: a stealther's sighting of a siege army near its realm's target keep starts the rally.</summary>
[TestFixture]
public sealed class UT_ScoutReports
{
    [Test]
    public void DefendersScoutOfTheAttackersNearTheTargetCounts()
    {
        Assert.That(AutonomousRvrEventLayer.ScoutReportApplies(eRealm.Midgard, eRealm.Albion, 6, 163, 10_000, 10_000,
            eRealm.Midgard, eRealm.Albion, 163, 15_000, 15_000, alreadyRallying: false), Is.True);
    }

    [Test]
    public void OtherSightingsDoNotCount()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.ScoutReportApplies(eRealm.Midgard, eRealm.Albion, 3, 163, 0, 0,
                eRealm.Midgard, eRealm.Albion, 163, 0, 0, false), Is.False, "too few");
            Assert.That(AutonomousRvrEventLayer.ScoutReportApplies(eRealm.Hibernia, eRealm.Albion, 6, 163, 0, 0,
                eRealm.Midgard, eRealm.Albion, 163, 0, 0, false), Is.False, "not the defender's scout");
            Assert.That(AutonomousRvrEventLayer.ScoutReportApplies(eRealm.Midgard, eRealm.Hibernia, 6, 163, 0, 0,
                eRealm.Midgard, eRealm.Albion, 163, 0, 0, false), Is.False, "not the attacking realm");
            Assert.That(AutonomousRvrEventLayer.ScoutReportApplies(eRealm.Midgard, eRealm.Albion, 6, 163, 0, 0,
                eRealm.Midgard, eRealm.Albion, 163, 20_000, 0, false), Is.False, "too far from the keep");
            Assert.That(AutonomousRvrEventLayer.ScoutReportApplies(eRealm.Midgard, eRealm.Albion, 6, 1, 0, 0,
                eRealm.Midgard, eRealm.Albion, 163, 0, 0, false), Is.False, "another region");
            Assert.That(AutonomousRvrEventLayer.ScoutReportApplies(eRealm.Midgard, eRealm.Albion, 6, 163, 0, 0,
                eRealm.Midgard, eRealm.Albion, 163, 0, 0, true), Is.False, "already rallying");
        });
    }

    [Test]
    public void RoughCountsReadLikeAVeteran()
    {
        Assert.That(AutonomousRvrEventLayer.RoughCount(5), Is.EqualTo("~5"));
        Assert.That(AutonomousRvrEventLayer.RoughCount(9), Is.EqualTo("a full group of"));
        Assert.That(AutonomousRvrEventLayer.RoughCount(30), Is.EqualTo("a zerg of"));
    }
}
