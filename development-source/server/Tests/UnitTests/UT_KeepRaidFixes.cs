using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Keep sieges by gamebots, fixes from the keeps and relics audits of 2026-11-11.</summary>
[TestFixture]
public class UT_KeepRaidFixes
{
    [TestCase(false, false, ExpectedResult = false, TestName = "An ordinary keep without a lord cannot be captured")]
    [TestCase(false, true, ExpectedResult = true, TestName = "An ordinary keep with its lord can be captured")]
    [TestCase(true, false, ExpectedResult = true, TestName = "A relic keep has no lord: its relic is the prize")]
    public bool OnlyCapturableKeepsAreSiegeTargets(bool relic, bool hasLord) =>
        AutonomousRvrKeepPolicy.IsCapturable(relic, hasLord);

    [Test]
    public void OneRuleDecidesTheRaidDoors() => Assert.Multiple(() =>
    {
        // ordinary keep: the outer gate and the keep door; a postern or its unflagged half never
        Assert.That(KeepRaidDoors.IsRaidGate(true, false, false, null), Is.True);
        Assert.That(KeepRaidDoors.IsRaidGate(true, true, false, null), Is.False, "the other half of a postern (Berkstead, Boldiam, Sursbrooke door 03)");
        Assert.That(KeepRaidDoors.IsRaidGate(false, false, false, null), Is.False, "a door that cannot be attacked");
        // relic keep: only the doors on the walk to the pad
        Assert.That(KeepRaidDoors.IsRaidGate(true, false, true, true), Is.True);
        Assert.That(KeepRaidDoors.IsRaidGate(true, false, true, false), Is.False, "doors 07-09 are not on the way to the relic");
        Assert.That(KeepRaidDoors.IsRaidGate(true, false, true, null), Is.True, "until the walk is known every attackable door counts");
    });

    [Test]
    public void TheRaidWorksTheLowestNumberedShutDoorFirst()
    {
        var gates = new List<(int, bool, float)> { (11, true, 200), (2, true, 900), (1, false, 1500) };
        Assert.That(KeepRaidDoors.NextRaidGate(gates), Is.EqualTo(1), "the outer gate is down: the keep or relic gate next");
        gates[2] = (1, true, 1500);
        Assert.That(KeepRaidDoors.NextRaidGate(gates), Is.EqualTo(2), "the outer gate first");
        Assert.That(KeepRaidDoors.NextRaidGate([(2, false, 0f), (11, false, 0f)]), Is.EqualTo(-1), "nothing left to break");
        Assert.That(KeepRaidDoors.NextRaidGate([(3, true, 100f), (3, true, 400f)]), Is.EqualTo(1), "the outermost of two halves");
    }

    [Test]
    public void TheShrineWalkPicksTheDoorsOnTheWay()
    {
        // outside the gate (x=0) up through the gate (x=400), the relic gate (x=800), then the keep door 11 (x=1200)
        var walk = new List<Vector3> { new(0, 0, 0), new(1600, 0, 0) };
        var doors = new List<(int, Vector3)>
        {
            (1, new(400, 0, 0)), (2, new(800, 0, 0)), (11, new(1200, 0, 0)),
            (10, new(1200, 600, 0)), (7, new(800, 0, 368 + 300)),
        };
        Assert.That(KeepRaidDoors.DoorsOnWalk(walk, doors), Is.EquivalentTo(new[] { 1, 2, 11 }),
            "door 10 stands beside the walk and door 07 high above it");
        Assert.That(KeepRaidDoors.DoorsOnWalk(walk, [(2, new Vector3(800, 0, 0))]), Is.Null,
            "a walk that does not pass the outer gate proves nothing about the way in");
    }
    [Test]
    public void TheRepelClockWaitsForTheWalkBackFromTheReleasePoint() => Assert.Multiple(() =>
    {
        Assert.That(AutonomousRvrEventLayer.RepelWindowMilliseconds(0), Is.EqualTo(AutonomousRvrEventLayer.RepelMilliseconds));
        // 85,000 units (Albion's portal outpost to Hlidskialf): 85,000 x 1.35 / 191 = about 10 minutes of walking
        Assert.That(AutonomousRvrEventLayer.RepelWindowMilliseconds(85_000) / 60_000, Is.EqualTo(20));
        Assert.That(AutonomousRvrEventLayer.RepelWindowMilliseconds(5_000_000),
            Is.EqualTo(AutonomousRvrEventLayer.RepelMilliseconds + AutonomousRvrEventLayer.MaxWalkBackMilliseconds), "bounded");
        Assert.That(AutonomousRvrEventLayer.RepelWindowMilliseconds(double.NaN), Is.EqualTo(AutonomousRvrEventLayer.RepelMilliseconds));
    });
    [Test]
    public void OneWarbandDoesNotMarchBeforeTheRealmCouldGather() => Assert.Multiple(() =>
    {
        long window = 18 * 60_000;
        Assert.That(AutonomousRvrEventLayer.MayMarchEarly(false, 60_000, window, 8, 8), Is.False, "eight at the outpost after a minute");
        Assert.That(AutonomousRvrEventLayer.MayMarchEarly(false, window / 3, window, 8, 8), Is.True, "a third of the window has gone");
        Assert.That(AutonomousRvrEventLayer.MayMarchEarly(false, window / 3, window, 30, 40), Is.False, "under 85% of those called");
        Assert.That(AutonomousRvrEventLayer.MayMarchEarly(false, 60_000, window, 108, 120), Is.True, "a full army needs no wait");
        Assert.That(AutonomousRvrEventLayer.MayMarchEarly(true, 60_000, window, 170, 200), Is.True);
    });

    [Test]
    public void TheMusterWaitsForTheArmyThatWasCalledNotAFixedNumber() => Assert.Multiple(() =>
    {
        Assert.That(AutonomousRvrEventLayer.MusterTarget(false, 40), Is.EqualTo(20), "half of a 40-strong keep army");
        Assert.That(AutonomousRvrEventLayer.MusterTarget(false, 128), Is.EqualTo(64), "never more than the old 64");
        Assert.That(AutonomousRvrEventLayer.MusterTarget(true, 60), Is.EqualTo(51), "85% for a relic army");
        Assert.That(AutonomousRvrEventLayer.MusterTarget(true, 240), Is.EqualTo(120), "never more than the old 120");
        // a 40-strong keep army with 25 gathered marches at the deadline; with 15 it waits once
        Assert.That(AutonomousRvrEventLayer.ShouldExtendMuster(false, 0, 25, 40, 0), Is.False);
        Assert.That(AutonomousRvrEventLayer.ShouldExtendMuster(false, 0, 15, 40, 0), Is.True);
        Assert.That(AutonomousRvrEventLayer.ShouldExtendMuster(false, 1, 15, 40, 10), Is.False, "a keep muster waits once");
        // relic: waits while it still grows, up to six times
        Assert.That(AutonomousRvrEventLayer.ShouldExtendMuster(true, 1, 45, 60, 40), Is.True, "45 of 60, still growing");
        Assert.That(AutonomousRvrEventLayer.ShouldExtendMuster(true, 1, 45, 60, 45), Is.False, "nobody else is coming");
        Assert.That(AutonomousRvrEventLayer.ShouldExtendMuster(true, AutonomousRvrEventLayer.RelicMusterExtensions, 46, 60, 45), Is.False);
    });
    [Test]
    public void TheDefenseRallyWaitsForTheDefendersWhoAnswered() => Assert.Multiple(() =>
    {
        Assert.That(AutonomousRvrEventLayer.DefenseMarchThreshold(64, 20), Is.EqualTo(17), "85% of the 20 who answered, not of the cap of 64");
        Assert.That(AutonomousRvrEventLayer.DefenseMarchThreshold(64, 200), Is.EqualTo(54), "never more than 85% of the cap");
        Assert.That(AutonomousRvrEventLayer.DefenseMarchThreshold(64, 3), Is.EqualTo(3), "three defenders go in together");
        Assert.That(AutonomousRvrEventLayer.DefenseMarchThreshold(64, 6), Is.EqualTo(5));
    });
    [Test]
    public void ASiegeRecruitAtTheSecondBorderKeepGoesToTheTeleporter() => Assert.Multiple(() =>
    {
        Assert.That(AutonomousWorldBotController.MayFetchFrontierMedallion(5_000, false), Is.True);
        Assert.That(AutonomousWorldBotController.MayFetchFrontierMedallion(92_500, false), Is.False,
            "a roamer at Vindsaul walks into its own frontier through Vindsaul's gate");
        Assert.That(AutonomousWorldBotController.MayFetchFrontierMedallion(92_500, true), Is.True,
            "a siege recruit at Vindsaul rides or walks to Svasud's teleporter");
    });
    [Test]
    public void AWarbandHoldsTheCapturedKeepUntilItsGuardsAreBack() => Assert.Multiple(() =>
    {
        Assert.That(AutonomousRvrEventLayer.ChooseHoldingForce([("party-a", 3, 8), ("party-b", 6, 6), ("rvr-77", 9, 1)]),
            Is.EqualTo("party-b"), "the warband with most bots at the keep; a lone recruit never holds");
        Assert.That(AutonomousRvrEventLayer.ChooseHoldingForce([("rvr-1", 1, 1)]), Is.Null);
        Assert.That(AutonomousRvrEventLayer.ChooseHoldingForce([("party-a", 4, 6), ("party-b", 4, 8)]), Is.EqualTo("party-b"));
        Assert.That(AutonomousRvrEventLayer.HoldEnds(true, 60_000, true), Is.False, "not in the first two minutes");
        Assert.That(AutonomousRvrEventLayer.HoldEnds(true, 10 * 60_000, false), Is.False, "guards still missing");
        Assert.That(AutonomousRvrEventLayer.HoldEnds(true, 10 * 60_000, true), Is.True, "the guards are back");
        Assert.That(AutonomousRvrEventLayer.HoldEnds(true, AutonomousRvrEventLayer.HoldMaximumMilliseconds, false), Is.True, "time is up");
        Assert.That(AutonomousRvrEventLayer.HoldEnds(false, 0, false), Is.True, "the keep was lost");
    });
    [TestCase(true, ExpectedResult = false, TestName = "A caster does not batter a door its spells cannot hurt")]
    [TestCase(false, ExpectedResult = true, TestName = "Melee and archers batter the door")]
    public bool OnlyMeleeAndArchersBatterDoors(bool listCaster) => AutonomousWorldBotController.BattersDoorsByHand(listCaster);
    [Test]
    public void ARaidBringsAndCrewsItsRamsLikePlayers() => Assert.Multiple(() =>
    {
        Assert.That(AutonomousRamCrews.RamsForDoor(3), Is.EqualTo(0));
        Assert.That(AutonomousRamCrews.RamsForDoor(8), Is.EqualTo(1), "a warband crews one ram");
        Assert.That(AutonomousRamCrews.RamsForDoor(16), Is.EqualTo(2), "a raid that can crew both brings two");
        Assert.That(AutonomousRamCrews.RamsForDoor(128), Is.EqualTo(2), "a door takes two rams at most");
        Assert.That(AutonomousRamCrews.IsCrewCandidate(false, false), Is.True, "melee fighters crew the ram");
        Assert.That(AutonomousRamCrews.IsCrewCandidate(true, false), Is.False, "casters and archers shoot the walls");
        Assert.That(AutonomousRamCrews.IsCrewCandidate(false, true), Is.False, "healers keep the crew alive");
    });

    [Test]
    public void EveryCrewSpotStandsBehindTheRamWithinItsCrewReach()
    {
        Vector2 ram = new(1000, 0), door = new(700, 0);
        var spots = new HashSet<(int, int)>();
        for (int seat = 0; seat < 12; seat++)
        {
            Vector2 spot = AutonomousRamCrews.CrewSpot(ram, door, seat);
            Assert.That(Vector2.Distance(spot, ram), Is.LessThan(280), $"seat {seat} is counted by the ram (300)");
            Assert.That(spot.X, Is.GreaterThan(ram.X - 60), $"seat {seat} stands on the far side from the door");
            Assert.That(spots.Add(((int)spot.X, (int)spot.Y)), Is.True, $"seat {seat} has its own spot");
        }
    }

    [Test]
    public void SiegeEnginesAreRepairedByThePlayerRuleOnly() => Assert.Multiple(() =>
    {
        Assert.That(BotSiegeRuntime.MayRepairSiege(301, 0, 10_000, 576), Is.True);
        Assert.That(BotSiegeRuntime.MayRepairSiege(300, 0, 10_000, 576), Is.False, "woodworking 301 or more");
        Assert.That(BotSiegeRuntime.MayRepairSiege(500, 4, 10_000, 576), Is.False, "repaired four times already");
        Assert.That(BotSiegeRuntime.MayRepairSiege(500, 0, 100, 576), Is.False, "no free repairs: the wood is paid");
        Assert.That(DOL.GS.Commands.RepairCommandHandler.GetNeededWoodForOneTick(3), Is.EqualTo(192), "a level-3 ram's wood");
    });
}

/// <summary>Keeps audit T4: a captured keep keeps a holding party until its guards are back.</summary>
[TestFixture, NonParallelizable]
public class UT_KeepHoldingParty
{
    [SetUp] public void SetUp() => RvrEventTestState.Clear();
    [TearDown] public void TearDown() => RvrEventTestState.Clear();

    [Test]
    public void TheWarbandAtTheKeepStaysAndTheRestAreReleased()
    {
        string id = System.Guid.NewGuid().ToString();
        long now = GameLoop.GameLoopTime;
        var target = new AutonomousRvrEventLayer.LiveObjective(id, "Held keep", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 200, 300000, 400000, 100, false, 0, 0, 10, 2);
        var first = new AutonomousRvrEventLayer.Force(id + ":a", eRealm.Albion, 8, 50, 2);
        var second = new AutonomousRvrEventLayer.Force(id + ":b", eRealm.Albion, 8, 50, 2);
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(first, [target], now, 0)?.TargetId, Is.EqualTo(id));
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(second, [target], now, 0)?.TargetId, Is.EqualTo(id));
        for (int i = 0; i < 5; i++)
            AutonomousRvrEventLayer.ReportAttendance(id, second.GroupId, eRealm.Albion, 2000 + i, true, now + 1);
        AutonomousRvrEventLayer.EndTarget(id, now + 2, eRealm.Albion);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.TryConsumeRelease(first.GroupId, now + 3, out _), Is.True, "the other warband goes");
            Assert.That(AutonomousRvrEventLayer.TryConsumeRelease(second.GroupId, now + 3, out _), Is.False, "the warband at the keep holds it");
            Assert.That(AutonomousRvrEventLayer.KeepPlan(second.GroupId, eRealm.Albion, now + 3)?.Intent,
                Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent));
            Assert.That(AutonomousRvrEventLayer.IsTargetActive(id, now + 3), Is.True);
            Assert.That(AutonomousRvrEventLayer.IsForceCommitted(second.GroupId, now + 3), Is.True);
        });
        long over = now + 3 + AutonomousRvrEventLayer.HoldMaximumMilliseconds;
        Assert.That(AutonomousRvrEventLayer.TryConsumeRelease(second.GroupId, over, out string reason), Is.True);
        Assert.That(reason, Does.Contain("Holding party relieved"));
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(id, over + 1), Is.False);
    }
}
