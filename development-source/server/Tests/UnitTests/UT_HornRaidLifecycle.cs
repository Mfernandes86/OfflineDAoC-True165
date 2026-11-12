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

    [Test]
    public void AWipeAtTheFinalEncounterIsCountedOnceWhenItIsWholeAgain()
    {
        bool engaged = false;
        Assert.That(RealmRaidRecruitmentPolicy.FinalEngagementWiped(ref engaged, true, false, 100), Is.False, "untouched");
        Assert.That(RealmRaidRecruitmentPolicy.FinalEngagementWiped(ref engaged, true, true, 100), Is.False, "pulled");
        Assert.That(RealmRaidRecruitmentPolicy.FinalEngagementWiped(ref engaged, true, false, 40), Is.False, "hurt, healing back");
        Assert.That(RealmRaidRecruitmentPolicy.FinalEngagementWiped(ref engaged, true, false, 100), Is.True, "whole again: one wipe");
        Assert.That(RealmRaidRecruitmentPolicy.FinalEngagementWiped(ref engaged, true, false, 100), Is.False, "counted once");
        Assert.That(RealmRaidRecruitmentPolicy.FinalEngagementWiped(ref engaged, true, true, 90), Is.False);
        Assert.That(RealmRaidRecruitmentPolicy.FinalEngagementWiped(ref engaged, false, false, 0), Is.False, "a kill is no wipe");
        Assert.That(engaged, Is.False);
        Assert.That(RealmRaidRecruitmentPolicy.HornBattleFailed(2), Is.False);
        Assert.That(RealmRaidRecruitmentPolicy.HornBattleFailed(3), Is.True);
    }

    [Test]
    public void AHornBattleWithoutProgressForAnHourTimesOut()
    {
        long start = 5 * Minute;
        Assert.That(RealmRaidRecruitmentPolicy.HornBattleStalled(start, 0, start + 59 * Minute), Is.False);
        Assert.That(RealmRaidRecruitmentPolicy.HornBattleStalled(start, 0, start + 60 * Minute), Is.True);
        Assert.That(RealmRaidRecruitmentPolicy.HornBattleStalled(start, start + 50 * Minute, start + 100 * Minute), Is.False, "a kill resets it");
        Assert.That(RealmRaidRecruitmentPolicy.HornBattleStalled(0, 0, 500 * Minute), Is.False, "not in battle");
    }

    [Test]
    public void EventCooldownIsNoneAfterAKillOrAStopAndShortAfterAFailedBattle()
    {
        Assert.That(RealmRaidRecruitmentPolicy.EventCooldownMilliseconds(false, true, true), Is.Zero, "a kill: the respawn is the wait");
        Assert.That(RealmRaidRecruitmentPolicy.EventCooldownMilliseconds(true, true, false), Is.Zero, "a player stop");
        Assert.That(RealmRaidRecruitmentPolicy.EventCooldownMilliseconds(false, false, false), Is.Zero, "a failed rally never went in");
        Assert.That(RealmRaidRecruitmentPolicy.EventCooldownMilliseconds(false, true, false), Is.EqualTo(10 * Minute),
            "route blocked, failed or timed out");
    }

    [Test]
    public void TheSounderCallsOffWithASecondSoundWithinThirtySeconds()
    {
        Assert.That(HornWindow165.CallOffConfirmed("dragon-albion", 1_000, "dragon-albion", 20_000), Is.True);
        Assert.That(HornWindow165.CallOffConfirmed("dragon-albion", 1_000, "dragon-albion", 32_000), Is.False, "too late: asks again");
        Assert.That(HornWindow165.CallOffConfirmed("epic-albion", 1_000, "dragon-albion", 2_000), Is.False, "another hunt");
        Assert.That(HornWindow165.CallOffConfirmed(null, 0, "dragon-albion", 2_000), Is.False, "first sound only asks");
        foreach (string phase in new[] { "Muster", "Staging", "Battle", "" })
            Assert.That(HornWindow165.OwnHuntStatus(phase, 300).Length, Is.LessThanOrEqualTo(63), "fits the horn row");
    }

    [Test]
    public void TheHornListKeepsTheStartsRouteProofForMinutes()
    {
        Assert.That(AutonomousRealmRaid.RouteProofExpiry(true, 1_000), Is.EqualTo(1_000 + 5 * Minute), "a proven way is rechecked every 5 minutes");
        Assert.That(AutonomousRealmRaid.RouteProofExpiry(false, 1_000), Is.EqualTo(1_000 + Minute), "a failed one every minute");
    }

    [Test]
    public void ARaidCutOffFromDarknessFallsEndsWhenFewerThanEightAreLeftInsideForFiveMinutes()
    {
        Assert.That(RealmRaidRecruitmentPolicy.AccessLostAbandoned(0, 100 * Minute), Is.False, "eight or more inside");
        Assert.That(RealmRaidRecruitmentPolicy.AccessLostAbandoned(10 * Minute, 14 * Minute), Is.False);
        Assert.That(RealmRaidRecruitmentPolicy.AccessLostAbandoned(10 * Minute, 15 * Minute), Is.True);
    }

    [Test]
    public void CaerSidiReopensAfterAFailedHorsemanFightAndStaysClosedOnlyForItsRespawn()
    {
        Assert.That(AutonomousRealmRaid.CaerSidiEncounterOpen(false, 0, true), Is.True, "untouched");
        Assert.That(AutonomousRealmRaid.CaerSidiEncounterOpen(true, 0, true), Is.True, "a raid failed with a horseman still up");
        Assert.That(AutonomousRealmRaid.CaerSidiEncounterOpen(true, 30 * Minute, false), Is.False, "Apocalypse died: its respawn");
        Assert.That(AutonomousRealmRaid.CaerSidiEncounterOpen(true, 0, false), Is.False, "Apocalypse just died, respawn not yet set");
    }

    [Test]
    public void TuscaranNeedsTheKingAndTheQueen()
    {
        var tuscaran = System.Array.Find(AutonomousRealmRaid.Definitions, d => d.Id == "epic-midgard");
        Assert.That(AutonomousRealmRaid.StandingFinals(tuscaran), Is.EquivalentTo(new[] { "KingTuscar", "QueenKula" }));
        var caerSidi = System.Array.Find(AutonomousRealmRaid.Definitions, d => d.Id == "epic-albion");
        Assert.That(AutonomousRealmRaid.StandingFinals(caerSidi), Is.EqualTo(new[] { "ApocInitializator" }), "Apocalypse appears in the fight");
    }

    [Test]
    public void RaidTimeRespawnFloorCoversEpicTrashButNoBossNamedMonsterOrScriptedAdd()
    {
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameEpicNPC), "grey noble", false), Is.True, "epic sidhe trash");
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameEpicDungeonNPC), "custodian", true), Is.True);
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameEpicNPC), "Duke Sallis", true), Is.False, "a named monster keeps its timer");
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameEpicBoss), "legion", true), Is.False, "a boss never");
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(DOL.GS.Scripts.Legion), "Legion", true), Is.False);
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameNPC), "essence shredder", true), Is.True, "as before");
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameNPC), "Aidon the Archwizard", false), Is.False,
            "a named monster in an approach dungeon keeps its timer");
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameNPC), "phantom huscarl", false), Is.True, "approach dungeon trash");
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameNPC), "Phantom Huscarl", false, repeatedName: true), Is.True,
            "capitalised trash shared by many spawns");
        Assert.That(AutonomousRealmRaid.RepeatedNames(new[] { "Phantom Huscarl", "Phantom Huscarl", "Phantom Huscarl", "Aidon the Archwizard",
            "Summoned Guardian", "Summoned Guardian" }), Is.EquivalentTo(new[] { "Phantom Huscarl" }));
        Assert.That(AutonomousRealmRaid.IsRaidTrash(typeof(GameSummonedPet), "wolf", true), Is.False);
        Assert.That(AutonomousRealmRaid.RaidInsideRegions(248, 246), Is.EqualTo(new ushort[] { 248, 246 }), "Summoner's Hall via Dodens Gruva");
        Assert.That(AutonomousRealmRaid.RaidInsideRegions(160, 160), Is.EqualTo(new ushort[] { 160 }));
    }

    [Test]
    public void AFightingDragonRaidHoldsOnlyWhileTheDragonFlies()
    {
        Assert.That(AutonomousRealmRaid.BattleHold(false, false, false), Is.True, "before the battle: wait for the landing");
        Assert.That(AutonomousRealmRaid.BattleHold(false, true, false), Is.False);
        Assert.That(AutonomousRealmRaid.BattleHold(true, false, false), Is.False, "grounded beyond 2,000: keep fighting");
        Assert.That(AutonomousRealmRaid.BattleHold(true, false, true), Is.True, "in the air: hold");
    }
}
