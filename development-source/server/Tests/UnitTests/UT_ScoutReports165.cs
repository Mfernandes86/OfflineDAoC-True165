using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Stealthers report "inc" with a rough count (veteran-bots review M10 / rvr-social G4, owner decision O9).</summary>
[TestFixture, NonParallelizable]
public class UT_ScoutReports165
{
    [SetUp]
    public void SetUp() => ScoutReports165.Reset();

    [TearDown]
    public void TearDown() => ScoutReports165.Reset();

    [Test]
    public void TheReportNamesTheBiggestEnemyRealmWithARoughCount()
    {
        Assert.That(ScoutReports165.Largest(3, 21, 2), Is.EqualTo((eRealm.Midgard, 21)));
        Assert.That(ScoutReports165.Largest(0, 0, 0), Is.EqualTo((eRealm.None, 0)));
        for (int seed = 0; seed < 6; seed++)
        {
            string line = ScoutReports165.Line(eRealm.Midgard, 21, "near Ben", new Random(seed));
            Assert.That(line, Does.Contain("~20 mids"));
            Assert.That(line, Does.Contain("near Ben"));
        }
    }

    [Test]
    public void ReportsAreRateLimitedPerSpotAndPerRealm()
    {
        Assert.That(ScoutReports165.TryClaim(eRealm.Albion, 1, 10_000, 10_000, 0), Is.True);
        Assert.That(ScoutReports165.TryClaim(eRealm.Albion, 1, 50_000, 50_000, 10_000), Is.False, "the realm had one 10 s ago");
        Assert.That(ScoutReports165.TryClaim(eRealm.Albion, 1, 50_000, 50_000, ScoutReports165.RealmIntervalMilliseconds), Is.True);
        Assert.That(ScoutReports165.TryClaim(eRealm.Albion, 1, 10_500, 10_200, 2 * ScoutReports165.RealmIntervalMilliseconds), Is.False,
            "the same spot within 3 minutes");
        Assert.That(ScoutReports165.TryClaim(eRealm.Midgard, 1, 10_000, 10_000, 1_000), Is.True, "another realm's scouts");
        Assert.That(ScoutReports165.TryClaim(eRealm.Albion, 1, 10_500, 10_200, ScoutReports165.SpotIntervalMilliseconds + 1), Is.True);
    }

    [Test]
    public void AReportWithoutALiveSiegeStartsNoRally()
    {
        Assert.That(AutonomousRvrEventLayer.ObserveScoutReport("rvr-keep-999999", eRealm.Albion, eRealm.Midgard), Is.False);
        Assert.That(AutonomousRvrEventLayer.ObserveScoutReport(null, eRealm.Albion, eRealm.Midgard), Is.False);
    }
}
