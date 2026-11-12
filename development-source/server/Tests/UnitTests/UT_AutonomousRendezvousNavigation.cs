using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_AutonomousRendezvousNavigation
{
    private sealed class Mesh : PathfindingMgrBase
    {
        public bool Available = true;
        public bool IsolatedAnchor;
        public bool OneWay;
        public int Samples;
        public int Paths;
        public override bool IsAvailable => Available;
        public override bool HasNavmesh(Zone zone) => Available;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 position, float x, float y, float z, EDtPolyFlags[] filters) => position;
        public override Vector3? GetRandomPoint(Zone zone, Vector3 position, float radius, EDtPolyFlags[] filters)
        {
            Samples++;
            return position + new Vector3(30, 0, 52); // nearby but disconnected prop top
        }
        public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to, EDtPolyFlags[] filters, Span<WrappedPathfindingNode> nodes)
        {
            Paths++;
            bool connected = !IsolatedAnchor && to.Z == from.Z && (!OneWay || to.X >= from.X && to.Y >= from.Y);
            nodes[0] = new(connected ? to : from, EDtPolyFlags.Walk);
            return new(connected ? PathfindingStatus.PathFound : PathfindingStatus.PartialPathFound, 1);
        }
    }
    private static Zone TestZone => new(null, 2, "Town", 0, 0, 65536, 65536, 2, false, 0, false, 0, 0, 0, 0, 0);

    [Test]
    public void DisconnectedRandomPropNeverReplacesConnectedAnchor()
    {
        var nav = new Mesh();
        Vector3 anchor = new(1000, 1000, 0);
        Assert.That(AutonomousRendezvousNavigation.TryChoosePoint(nav, TestZone, anchor, out var chosen), Is.True);
        // The plain choice (street, camp and staging anchors) keeps its anchor. A service NPC's anchor goes through
        // TryChooseClearPoint instead (GitHub #48), tested below.
        Assert.That(chosen, Is.EqualTo(anchor));
        Assert.That(nav.Samples, Is.EqualTo(4), "Randomization is bounded and only occurs at session creation");
        Assert.That(nav.Paths, Is.LessThanOrEqualTo(10));
    }

    [Test]
    public void IsolatedAnchorCannotSeedANewGroup()
    {
        var nav = new Mesh { IsolatedAnchor = true };
        Assert.That(AutonomousRendezvousNavigation.TryChoosePoint(nav, TestZone, new(1000, 1000, 0), out _), Is.False);
        Assert.That(nav.Samples, Is.Zero);
        Assert.That(nav.Paths, Is.EqualTo(4));
    }

    [Test]
    public void OneWayDropIsNotAUsableAssemblyExit()
    {
        var nav = new Mesh { OneWay = true };
        Assert.That(AutonomousRendezvousNavigation.TryChoosePoint(nav, TestZone, new(1000, 1000, 0), out _), Is.False);
    }

    [Test]
    public void MissingMeshDoesNotCertifyAnUnsafePoint()
    {
        var nav = new Mesh { Available = false };
        Assert.That(AutonomousRendezvousNavigation.TryChoosePoint(nav, TestZone, new(1000, 1000, 0), out _), Is.False);
        Assert.That(nav.Paths, Is.Zero);
        Assert.That(nav.Samples, Is.Zero);
    }

    [Test]
    public void LocallyWalkableIslandCannotReachAFormationSlot()
    {
        var nav = new Mesh { IsolatedAnchor = true };
        Vector3 member = new(1000, 1000, 0);
        Vector3 slot = new(1120, 1000, 0);

        Assert.That(AutonomousRendezvousNavigation.CanReachFrom(nav, TestZone, member, slot), Is.False);
    }

    // GitHub #48: a meetup beside a stable master / merchant is never on the NPC's own spot.
    [Test]
    public void ServiceAnchorMeetupIsBesideTheNpcNotOnIt()
    {
        var nav = new Mesh();
        Vector3 npc = new(1000, 1000, 0);
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 17, null, out var chosen), Is.True);
        float across = Vector2.Distance(new(chosen.X, chosen.Y), new(npc.X, npc.Y));
        Assert.That(across, Is.GreaterThanOrEqualTo(AutonomousServiceNpcClearance.MeetClearRadius));
        Assert.That(across, Is.LessThanOrEqualTo(AutonomousServiceNpcClearance.MeetOuterRadius));
        Assert.That(across, Is.EqualTo(420f).Within(1f), "the farther ring is preferred");
        Assert.That(nav.Samples, Is.Zero, "no random sampling: fixed rings, once per formation");
    }

    [Test]
    public void RejectedFarRingFallsBackToTheNearerRingThenFails()
    {
        var nav = new Mesh();
        Vector3 npc = new(1000, 1000, 0);
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 0,
            p => Vector2.Distance(new(p.X, p.Y), new(npc.X, npc.Y)) < 400, out var chosen), Is.True);
        Assert.That(Vector2.Distance(new(chosen.X, chosen.Y), new(npc.X, npc.Y)), Is.EqualTo(340f).Within(1f));
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 0, _ => false, out _), Is.False);
    }

    [Test]
    public void TwoPartiesAtOneNpcGetDifferentSpots()
    {
        var nav = new Mesh();
        Vector3 npc = new(1000, 1000, 0);
        AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 10, null, out var a);
        AutonomousRendezvousNavigation.TryChooseClearPoint(nav, TestZone, npc, 200, null, out var b);
        Assert.That(Vector3.Distance(a, b), Is.GreaterThan(150f));
    }

    [Test]
    public void ClearPointNeedsAConnectedFloor()
    {
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(new Mesh { IsolatedAnchor = true }, TestZone,
            new(1000, 1000, 0), 0, null, out _), Is.False);
        Assert.That(AutonomousRendezvousNavigation.TryChooseClearPoint(new Mesh { Available = false }, TestZone,
            new(1000, 1000, 0), 0, null, out _), Is.False);
    }

    [Test]
    public void ClearBandAndServiceClearance()
    {
        Vector3 npc = new(0, 0, 0);
        Assert.That(AutonomousRendezvousNavigation.IsInClearBand(npc, new(80, 0, 0)), Is.False, "the old 80-unit ring");
        Assert.That(AutonomousRendezvousNavigation.IsInClearBand(npc, new(350, 0, 0)), Is.True);
        Assert.That(AutonomousRendezvousNavigation.IsInClearBand(npc, new(600, 0, 0)), Is.False);
        Vector3[] services = [new(0, 0, 0), new(1000, 0, 0)];
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(500, 0, 0), services, 300), Is.True);
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(800, 0, 0), services, 300), Is.False);
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(800, 0, 900), services, 300), Is.True, "another floor");
        Assert.That(AutonomousServiceNpcClearance.IsClearOf(new(1, 1, 0), null, 300), Is.True);
    }
}
