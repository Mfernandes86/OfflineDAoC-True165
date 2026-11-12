using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Getting a horn raid there and getting raiders back (realm event audits 2026-11-11).</summary>
public class UT_RealmRaidTravelFixes
{
    [TestCase(true, 60, 500, 800f)]
    [TestCase(true, 54, 0, 750f)]
    [TestCase(true, 59, 1500, 1800f)]
    [TestCase(true, 20, 500, 100f)]
    [TestCase(false, 67, 500, 100f)]
    public void StagingPostsKeepOutOfAggressiveMonstersReach(bool aggressive, int level, int aggroRange, float clearance) =>
        Assert.That(RealmRaidStaging.HazardClearance(aggressive, level, aggroRange), Is.EqualTo(clearance));

    [Test]
    public void StagingPostIsClearOnlyOutsideEveryHazard()
    {
        var hazards = new[] { new RealmRaidStaging.PostHazard(new Vector3(0, 0, 0), 800), new RealmRaidStaging.PostHazard(new Vector3(5000, 0, 0), 100) };
        Assert.That(RealmRaidStaging.IsClear(new Vector3(790, 0, 0), hazards), Is.False);
        Assert.That(RealmRaidStaging.IsClear(new Vector3(810, 0, 0), hazards), Is.True);
        Assert.That(RealmRaidStaging.IsClear(new Vector3(5050, 0, 0), hazards), Is.False);
        Assert.That(RealmRaidStaging.IsClear(new Vector3(810, 0, 0), []), Is.True);
    }

    [Test]
    public void PlayerLootShareIsForScriptedEncountersNotEpicTrash()
    {
        Assert.That(RealmRaidLootOwner.IsScriptedEpicEncounter(typeof(GameEpicNPC)), Is.False);
        Assert.That(RealmRaidLootOwner.IsScriptedEpicEncounter(typeof(GameEpicDungeonNPC)), Is.False);
        Assert.That(RealmRaidLootOwner.IsScriptedEpicEncounter(typeof(GameNPC)), Is.False);
        Assert.That(RealmRaidLootOwner.IsScriptedEpicEncounter(typeof(GameEpicBoss)), Is.True);
        Assert.That(RealmRaidLootOwner.IsScriptedEpicEncounter(typeof(GrandSummonerGovannon)), Is.True);
        Assert.That(RealmRaidLootOwner.IsScriptedEpicEncounter(typeof(SummonerRoesia)), Is.True);
    }

    [TestCase(true, true, true, false, true, 0f, true, TestName = "Dungeon raider outside, released or late, gathers")]
    [TestCase(true, true, true, true, true, 0f, false, TestName = "Dungeon raider already inside goes on")]
    [TestCase(false, true, true, false, true, 0f, false, TestName = "Before the assault staging is the gathering")]
    [TestCase(true, false, true, false, true, 0f, false, TestName = "A member with the raid column never gathers")]
    [TestCase(true, true, true, false, false, 0f, false, TestName = "No gather point in this region")]
    [TestCase(true, true, false, false, true, 20000f, true, TestName = "Dragon raider at the bindstone gathers")]
    [TestCase(true, true, false, false, true, 5000f, false, TestName = "Dragon raider inside the staging ring goes on")]
    public void ReinforcementsGatherOnlyWhenAwayFromTheRaid(bool started, bool catchingUp, bool dungeon, bool inside, bool gatherRegion,
        float lairDistance, bool gather) =>
        Assert.That(AutonomousRealmRaid.ReinforcementMustGather(started, catchingUp, dungeon, inside, gatherRegion, lairDistance), Is.EqualTo(gather));

    [Test]
    public void ReinforcementWaveGoesAtEightOrAfterAShortWait()
    {
        const long t = 1_000_000;
        Assert.That(AutonomousRealmRaid.ReinforcementWaveGoes(8, 12, t, t), Is.True, "a party-sized wave goes at once");
        Assert.That(AutonomousRealmRaid.ReinforcementWaveGoes(5, 12, t, t + 179_000), Is.False, "others are still coming");
        Assert.That(AutonomousRealmRaid.ReinforcementWaveGoes(5, 12, t, t + 180_000), Is.True, "never more than three minutes");
        Assert.That(AutonomousRealmRaid.ReinforcementWaveGoes(3, 3, t, t + 30_000), Is.False, "a minute for corpses about to release");
        Assert.That(AutonomousRealmRaid.ReinforcementWaveGoes(3, 3, t, t + 60_000), Is.True, "everyone expected is here");
        Assert.That(AutonomousRealmRaid.ReinforcementWaveGoes(0, 4, long.MaxValue, t), Is.False, "nobody gathered yet");
    }

    [Test]
    public void OnlyLongTripsAskForARide()
    {
        Assert.That(AutonomousWorldBotController.ShouldTryExpeditionRide(40_000), Is.True);
        Assert.That(AutonomousWorldBotController.ShouldTryExpeditionRide(3_000), Is.False);
    }

    [Test]
    public void AnUnplaceableSlotDoesNotBlockLaterParties()
    {
        var failed = new System.Collections.Generic.Dictionary<int, long>();
        int tries = 0;
        int slot = AutonomousRealmRaid.NextPlaceableSlot(38, s => s == 0, s => failed.ContainsKey(s), s => { tries++; return s != 1; }, s => failed[s] = 1);
        Assert.That(slot, Is.EqualTo(2), "slot 1 has no post; the next free slot is used");
        Assert.That(failed.Keys, Is.EquivalentTo(new[] { 1 }));
        tries = 0;
        slot = AutonomousRealmRaid.NextPlaceableSlot(38, s => s is 0 or 2, s => failed.ContainsKey(s), s => { tries++; return true; }, s => failed[s] = 1);
        Assert.That(slot, Is.EqualTo(3));
        Assert.That(tries, Is.EqualTo(1), "a slot that just failed is not searched again");
        Assert.That(AutonomousRealmRaid.NextPlaceableSlot(2, s => false, s => false, s => false, s => { }), Is.EqualTo(-1));
    }
}
