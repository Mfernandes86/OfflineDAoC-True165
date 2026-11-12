using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Realm event audit 2026-11-12: a horn raid's own end conditions and the numbers that must agree.</summary>
[TestFixture]
public class UT_HornRaidLifecycle
{
    private const long Minute = 60_000;

    [Test]
    public void HornHubLeavesWithTheNumbersTheAssaultAccepts()
    {
        Assert.That(RealmRaidRecruitmentPolicy.HornDepartHub(300, 300, 0, 0), Is.True, "300 at once");
        Assert.That(RealmRaidRecruitmentPolicy.HornDepartHub(200, 300, 5 * Minute, 30_000), Is.False, "200 waits while arrivals come");
        Assert.That(RealmRaidRecruitmentPolicy.HornDepartHub(200, 300, 5 * Minute, 2 * Minute), Is.True, "200 once nobody new for 2 minutes");
        Assert.That(RealmRaidRecruitmentPolicy.HornDepartHub(170, 170, 1 * Minute, 0), Is.True, "everyone expected is there");
        Assert.That(RealmRaidRecruitmentPolicy.HornDepartHub(170, 250, 29 * Minute, 0), Is.False);
        Assert.That(RealmRaidRecruitmentPolicy.HornDepartHub(170, 250, 30 * Minute, 0), Is.True, "patience spent with 160+");
        Assert.That(RealmRaidRecruitmentPolicy.HornDepartHub(150, 250, 60 * Minute, 10 * Minute), Is.False,
            "never leaves with fewer than the assault can ever use (the old 120)");
    }

    [Test]
    public void HornMusterThatCannotGatherFailsAfterFortyFiveMinutes()
    {
        Assert.That(RealmRaidRecruitmentPolicy.HornMusterFailed(false, 150, 44 * Minute), Is.False);
        Assert.That(RealmRaidRecruitmentPolicy.HornMusterFailed(false, 150, 45 * Minute), Is.True);
        Assert.That(RealmRaidRecruitmentPolicy.HornMusterFailed(false, 160, 45 * Minute), Is.False, "160 leave instead");
        Assert.That(RealmRaidRecruitmentPolicy.HornMusterFailed(true, 0, 300 * Minute), Is.False, "the hub clock ends at departure");
    }

    [Test]
    public void HornStagingDeadlineIsTheAutomaticClockWithArrivalExtensions()
    {
        long departed = 10 * Minute;
        Assert.That(RealmRaidRecruitmentPolicy.HornStagingDeadline(departed, 0), Is.EqualTo(departed + 45 * Minute));
        Assert.That(RealmRaidRecruitmentPolicy.HornStagingDeadline(departed, departed + 50 * Minute), Is.EqualTo(departed + 60 * Minute));
        Assert.That(RealmRaidRecruitmentPolicy.HornStagingDeadline(departed, departed + 300 * Minute), Is.EqualTo(departed + 75 * Minute),
            "at most 30 minutes past the deadline");
        Assert.That(RealmRaidRecruitmentPolicy.HornStagingFailed(159, true), Is.True);
        Assert.That(RealmRaidRecruitmentPolicy.HornStagingFailed(160, true), Is.False, "160+ go in (or wait for a landing)");
        Assert.That(RealmRaidRecruitmentPolicy.HornStagingFailed(0, false), Is.False);
    }
}
