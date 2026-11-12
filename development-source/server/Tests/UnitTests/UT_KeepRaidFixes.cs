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
}
