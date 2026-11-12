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
}
