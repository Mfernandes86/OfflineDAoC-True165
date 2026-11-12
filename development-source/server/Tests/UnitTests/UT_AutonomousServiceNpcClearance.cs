using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

// GitHub #48: bots stood on stable masters and other service NPCs.
[TestFixture]
public class UT_AutonomousServiceNpcClearance
{
    private sealed class Mesh : PathfindingMgrBase
    {
        public bool Connected = true;
        public float FloorShift;
        public int Projections;
        public override bool IsAvailable => true;
        public override bool HasNavmesh(Zone zone) => true;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 position, float x, float y, float z, EDtPolyFlags[] filters)
        {
            Projections++;
            return position + new Vector3(FloorShift, 0, 0);
        }
        public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> nodes)
        {
            nodes[0] = new(Connected ? to : from, EDtPolyFlags.Walk);
            return new(Connected ? PathfindingStatus.PathFound : PathfindingStatus.PartialPathFound, 1);
        }
    }
    private static Zone TestZone => new(null, 2, "Town", 0, 0, 65536, 65536, 2, false, 0, false, 0, 0, 0, 0, 0);

    [Test]
    public void LandingStepOffIsOwnSpotAwayFromTheStableMaster()
    {
        Vector3 end = new(10_000, 10_000, 0);
        Vector3 master = new(10_000, 9_700, 0);   // the master is south of the route end
        Vector2 away = Vector2.Normalize(new(end.X - master.X, end.Y - master.Y));
        for (long id = 1; id < 400; id++)
        {
            Vector3 spot = AutonomousServiceNpcClearance.LandingStepOff(end, master, id);
            Vector2 step = new(spot.X - end.X, spot.Y - end.Y);
            Assert.That(step.Length(), Is.InRange(AutonomousServiceNpcClearance.LandingStepMin - 0.01f,
                AutonomousServiceNpcClearance.LandingStepMax + 0.01f));
            Assert.That(Vector2.Dot(Vector2.Normalize(step), away), Is.GreaterThanOrEqualTo(MathF.Cos(75.5f * MathF.PI / 180f)),
                "never toward the master");
            Assert.That(spot, Is.EqualTo(AutonomousServiceNpcClearance.LandingStepOff(end, master, id)), "the same bot, the same spot");
        }
        Assert.That(AutonomousServiceNpcClearance.LandingStepOff(end, master, 7),
            Is.Not.EqualTo(AutonomousServiceNpcClearance.LandingStepOff(end, master, 8)), "riders of one route spread out");
        Vector3 noMaster = AutonomousServiceNpcClearance.LandingStepOff(end, null, 7);
        Assert.That(Vector2.Distance(new(noMaster.X, noMaster.Y), new(end.X, end.Y)),
            Is.InRange(AutonomousServiceNpcClearance.LandingStepMin - 0.01f, AutonomousServiceNpcClearance.LandingStepMax + 0.01f));
    }

    [Test]
    public void LandingStepOffNeedsAConnectedFloorNearTheSpot()
    {
        Vector3 end = new(10_000, 10_000, 0);
        var nav = new Mesh();
        Assert.That(AutonomousServiceNpcClearance.TryResolveLandingStepOff(nav, TestZone, end, null, 5, out Vector3 spot), Is.True);
        Assert.That(nav.Projections, Is.EqualTo(1), "one floor projection per arrival");
        Assert.That(Vector3.Distance(spot, end), Is.GreaterThanOrEqualTo(AutonomousServiceNpcClearance.LandingStepMin - 0.01f));
        Assert.That(AutonomousServiceNpcClearance.TryResolveLandingStepOff(new Mesh { Connected = false }, TestZone, end, null, 5, out _),
            Is.False, "an island or prop top keeps the rider where it landed");
        Assert.That(AutonomousServiceNpcClearance.TryResolveLandingStepOff(new Mesh { FloorShift = 90 }, TestZone, end, null, 5, out _),
            Is.False, "the floor found is far from the chosen spot");
    }

    [Test]
    public void ServiceStopSpotsAreOwnAndClearButInRange()
    {
        Vector3 npc = new(5_000, 5_000, 0);
        const int range = 256;
        var spots = new System.Collections.Generic.HashSet<(int, int)>();
        for (long id = 1; id <= 40; id++)
        {
            Vector3 spot = npc + AutonomousSupplyMerchantSpace.Offset(id, 0, range);
            Assert.That(AutonomousSupplyMerchantSpace.IsInteractionSpot(npc, spot, range - 8), Is.True,
                "100+ from the NPC and inside the interaction range");
            spots.Add(((int)spot.X, (int)spot.Y));
        }
        Assert.That(spots.Count, Is.GreaterThan(30), "bots coming in on one road no longer share one approach point");
        Vector3 own = npc + AutonomousSupplyMerchantSpace.Offset(9, 0, range);
        Assert.That(AutonomousSupplyMerchantSpace.IsAtOwnSpot(npc, own, own + new Vector3(20, 0, 0), range), Is.True);
        Assert.That(AutonomousSupplyMerchantSpace.IsAtOwnSpot(npc, own, own + new Vector3(60, 0, 0), range), Is.False);
        Assert.That(AutonomousSupplyMerchantSpace.IsAtOwnSpot(npc, npc + new Vector3(40, 0, 0), npc + new Vector3(40, 0, 0), range),
            Is.False, "a spot on the NPC is never a stop");
    }

    [Test]
    public void TownBreakSpotsKeepClearOfServiceNpcs()
    {
        Vector3[] services = [new(1_000, 1_000, 0), new(2_000, 1_000, 0)];
        float clear = AutonomousServiceNpcClearance.TownBreakClearRadius;
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1_090, 1_000, 0), services, clear), Is.False, "on the merchant");
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1_150, 1_100, 0), services, clear), Is.False, "inside 250");
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1_500, 1_000, 0), services, clear), Is.True, "between them, clear");
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1_500, 1_000, 0), [], clear), Is.True);
    }

    [Test]
    public void AServiceStopStartsOverOnTheNextVisit()
    {
        Assert.That(AutonomousSupplyMerchantSpace.IsNewVisit(now: 200_000, lastAsked: 100_000), Is.True, "came back later");
        Assert.That(AutonomousSupplyMerchantSpace.IsNewVisit(now: 101_500, lastAsked: 100_000), Is.False, "the same stop, next AI turn");
        Assert.That(AutonomousSupplyMerchantSpace.IsNewVisit(now: 100_000, lastAsked: 0), Is.True, "the first visit");
    }
}
