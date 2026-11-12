using System;
using System.Linq;
using System.Numerics;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public class UT_AutonomousDarknessFallsPolicy
{
    [Test]
    public void EntranceAuthority_UsesOwnerAndFifteenMinutePreviousOwnerGrace()
    {
        const long grace = 900_000;
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsPolicy.CanEnter(eRealm.Albion, eRealm.Albion, eRealm.Midgard,
                1_000_000, 500_000, grace, false, true), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanEnter(eRealm.Midgard, eRealm.Albion, eRealm.Midgard,
                1_000_000, 500_000, grace, false, true), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanEnter(eRealm.Midgard, eRealm.Albion, eRealm.Midgard,
                1_400_001, 500_000, grace, false, true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanEnter(eRealm.Hibernia, eRealm.Albion, eRealm.Midgard,
                1_000_000, 500_000, grace, false, true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanEnter(eRealm.Hibernia, eRealm.None, eRealm.None,
                1_000_000, 0, grace, true, true), Is.True);
        });
    }

    [Test]
    public void RegionEdges_BlockClosedEntryButNeverTrapCharactersAlreadyInside()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Albion, 1, 249, _ => false), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Albion, 249, 1, _ => false), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Albion, 249, 100, _ => true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Hibernia, 249, 1, _ => true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Albion, 1, 249, _ => true), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Midgard, 100, 249, _ => true), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Hibernia, 200, 249, _ => true), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Midgard, 1, 249, _ => true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.Hibernia, 100, 249, _ => true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUseRegionEdge(eRealm.None, 1, 249, _ => true), Is.False);
        });
    }

    [Test]
    public void DarknessFallsEntrance_UsesOnlyNormalHomePortalRows()
    {
        var albion = new DbZonePoint { Id = 81, SourceRegion = 1, TargetRegion = 249, Realm = 1 };
        var albionRelic = new DbZonePoint { Id = 79, SourceRegion = 1, TargetRegion = 249, Realm = 1 };
        var midgard = new DbZonePoint { Id = 84, SourceRegion = 100, TargetRegion = 249, Realm = 2 };
        var midgardYggdra = new DbZonePoint { Id = 83, SourceRegion = 100, TargetRegion = 249, Realm = 2 };
        var midgardUppland = new DbZonePoint { Id = 82, SourceRegion = 100, TargetRegion = 249, Realm = 2 };
        // The installed Hibernia home entrance is the realm-neutral ID 87 row.
        var hibernia = new DbZonePoint { Id = 87, SourceRegion = 200, TargetRegion = 249, Realm = 0 };
        var hiberniaOther = new DbZonePoint { Id = 86, SourceRegion = 200, TargetRegion = 249, Realm = 3 };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsPolicy.HomeEntranceZonePointId(eRealm.Albion), Is.EqualTo(81));
            Assert.That(AutonomousDarknessFallsPolicy.HomeEntranceZonePointId(eRealm.Midgard), Is.EqualTo(84));
            Assert.That(AutonomousDarknessFallsPolicy.HomeEntranceZonePointId(eRealm.Hibernia), Is.EqualTo(87));
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Albion, albion), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Midgard, midgard), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Hibernia, hibernia), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Albion, albionRelic), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Midgard, midgardYggdra), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Midgard, midgardUppland), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Hibernia, hiberniaOther), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Midgard, albion), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Albion, midgard), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Hibernia,
                new DbZonePoint { Id = 87, SourceRegion = 100, TargetRegion = 249, Realm = 0 }), Is.False,
                "The neutral Hibernia row is valid only at its own home source.");
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Hibernia,
                new DbZonePoint { Id = 87, SourceRegion = 200, TargetRegion = 249, Realm = 3 }), Is.False,
                "The installed Hibernia entrance is its exact neutral DB row.");
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Midgard,
                new DbZonePoint { Id = 84, SourceRegion = 100, TargetRegion = 249, Realm = 0 }), Is.False,
                "Midgard must use its own realm-specific normal entrance row.");
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.None,
                new DbZonePoint { Id = 0, SourceRegion = 0, TargetRegion = 249, Realm = 0 }), Is.False);
        });
    }

    [Test]
    public void HiberniaExteriorLoopGuard_OnlyMatchesTheAuditedSoloPortalSeam()
    {
        var portal = new DbZonePoint
        {
            Id = 87, SourceRegion = 200, TargetRegion = 249, Realm = 0
        };
        Vector3 seam = new(385088, 483424, 7262);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsPolicy.IsAuditedHiberniaExteriorLoop(
                eRealm.Hibernia, portal, 200, 206, seam, 1), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.IsAuditedHiberniaExteriorLoop(
                eRealm.Hibernia, portal, 200, 206, seam + new Vector3(100, 0, 0), 1), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.IsAuditedHiberniaExteriorLoop(
                eRealm.Hibernia, portal, 200, 206, seam + new Vector3(193, 0, 0), 1), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.IsAuditedHiberniaExteriorLoop(
                eRealm.Hibernia, portal, 200, 200, seam, 1), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.IsAuditedHiberniaExteriorLoop(
                eRealm.Hibernia, portal, 181, 206, seam, 1), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.IsAuditedHiberniaExteriorLoop(
                eRealm.Hibernia, portal, 200, 206, seam, 8), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.IsAuditedHiberniaExteriorLoop(
                eRealm.Midgard, portal, 200, 206, seam, 1), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.IsAuditedHiberniaExteriorLoop(
                eRealm.Hibernia, new DbZonePoint
                {
                    Id = 86, SourceRegion = 200, TargetRegion = 249, Realm = 3
                }, 200, 206, seam, 1), Is.False);
        });
    }

    [Test]
    public void DarknessFallsExit_UsesOnlyOwnRealmSpecificPortalRow()
    {
        var albion = new DbZonePoint { Id = 74, SourceRegion = 249, TargetRegion = 1, Realm = 1 };
        var midgard = new DbZonePoint { Id = 74, SourceRegion = 249, TargetRegion = 100, Realm = 2 };
        var unowned = new DbZonePoint { Id = 74, SourceRegion = 249, TargetRegion = 1, Realm = 0 };
        var wrongPhysicalPortal = new DbZonePoint { Id = 70, SourceRegion = 249, TargetRegion = 1, Realm = 1 };
        var ownMidgard = new DbZonePoint { Id = 70, SourceRegion = 249, TargetRegion = 100, Realm = 2 };
        var ownHibernia = new DbZonePoint { Id = 72, SourceRegion = 249, TargetRegion = 200, Realm = 3 };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Albion, albion), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Midgard, ownMidgard), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Hibernia, ownHibernia), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Albion, midgard), Is.False,
                "A portal at the same coordinates still needs the bot's own realm DB row.");
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Albion, wrongPhysicalPortal), Is.False,
                "Each exit has an Albion row, but the bot must use its own physical corridor.");
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Midgard, albion), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanUsePortalRow(eRealm.Albion, unowned), Is.False,
                "A realm-neutral fallback cannot bypass the own-exit requirement.");
            Assert.That(AutonomousDarknessFallsPolicy.CanEvacuateThroughHomeExit(eRealm.Albion, 249, albion), Is.True,
                "An already-inside bot can select its own exit while new DF goals are gated.");
            Assert.That(AutonomousDarknessFallsPolicy.CanEvacuateThroughHomeExit(eRealm.Albion, 1, albion), Is.False,
                "The evacuation exception cannot open a new DF entrance.");
            Assert.That(AutonomousDarknessFallsPolicy.CanEvacuateThroughHomeExit(eRealm.Albion, 249, wrongPhysicalPortal), Is.False,
                "Another faction's physical exit is not a valid evacuation route.");
            Assert.That(AutonomousDarknessFallsPolicy.MustRetireOrdinaryGoal(249, false), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.MustRetireOrdinaryGoal(249, true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.MustRetireOrdinaryGoal(248, false), Is.False,
                "Ordinary goals outside Darkness Falls are untouched.");
        });
    }

    [Test]
    public void LocalPvp_RequiresTwoLiveOpposingRealmsInsideDarknessFalls()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsPolicy.CanEngageLocalOpponent(eRealm.Albion, eRealm.Midgard, 249, 249, true, true), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.CanEngageLocalOpponent(eRealm.Albion, eRealm.Albion, 249, 249, true, true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanEngageLocalOpponent(eRealm.Albion, eRealm.Midgard, 249, 1, true, true), Is.False);
            Assert.That(AutonomousDarknessFallsPolicy.CanEngageLocalOpponent(eRealm.Albion, eRealm.Midgard, 249, 249, false, true), Is.False);
        });
    }

    private const string TestMeshHash = "ABCDEF0123";

    private static AutonomousDarknessFallsNavigation.SpawnProof ProofFor(DbMob mob) => new()
    {
        Id = mob.ObjectId, Name = mob.Name, Level = mob.Level,
        ClassType = mob.ClassType, Model = mob.Model, Flags = mob.Flags,
        Spawn = [mob.X, mob.Y, mob.Z], Approach = [mob.X, mob.Y, mob.Z],
        AttackableFromApproach = true,
        Routes = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
            .Select(realm => new AutonomousDarknessFallsNavigation.RouteProof
            {
                Realm = realm, DistanceFromEntrance = 100 + mob.X, DistanceToOwnExit = 100,
                ExitZonePointId = AutonomousDarknessFallsPolicy.HomeExitZonePointId(realm),
                ExitTargetRegion = AutonomousDarknessFallsPolicy.HomeRegion(realm),
                Exit = [1, 2, 3],
                InWaypoints = [[1, 2, 3], [mob.X, mob.Y, mob.Z]],
                OutWaypoints = [[mob.X, mob.Y, mob.Z], [1, 2, 3]]
            }).ToArray()
    };

    private static AutonomousDarknessFallsNavigation.ExcludedOrdinarySpawn ExclusionFor(DbMob mob, string reason) => new()
    {
        Id = mob.ObjectId, Name = mob.Name, Level = mob.Level, ClassType = mob.ClassType,
        Model = mob.Model, Flags = mob.Flags, Spawn = [mob.X, mob.Y, mob.Z], Reason = reason
    };

    /// <summary>The audited world: 2,375 ordinary rows (the first 2,000 proven, the rest excluded) and the 85
    /// raid/event rows, plus a certificate and the three real exit rows that match it.</summary>
    private static (DbMob[] Ordinary, DbMob[] Raid, AutonomousDarknessFallsNavigation.Certificate Certificate,
        DbZonePoint[] Exits) AuditedWorld()
    {
        DbMob[] ordinary = Enumerable.Range(0, AutonomousDarknessFallsNavigation.BaselineOrdinaryCombatRows)
            .Select(index => new DbMob
            {
                ObjectId = $"df-{index}", Name = "test monster", Region = 249,
                Realm = 0, Level = 25, X = index, Y = 100, Z = 200
            }).ToArray();
        DbMob[] raid = AutonomousDarknessFallsGoalScope.SnapshotRaidEventRows().Select(row => new DbMob
        {
            ObjectId = row.Id, Name = row.Name, Level = (byte)row.Level,
            ClassType = row.ClassType, Region = 249, Realm = 0,
            X = row.Spawn[0], Y = row.Spawn[1], Z = row.Spawn[2]
        }).ToArray();
        var certificate = new AutonomousDarknessFallsNavigation.Certificate
        {
            NavSha256 = TestMeshHash,
            Spawns = ordinary.Take(2000).Select(ProofFor).ToArray(),
            ExcludedOrdinarySpawns = ordinary.Skip(2000).Select(mob => ExclusionFor(mob, "UnverifiedRoute")).ToArray(),
            TraversalLinks = [new() { Kind = "Climb", Start = [0, 0, 0], End = [10, 0, 50], Bidirectional = true }]
        };
        DbZonePoint[] exits = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.Select(realm => new DbZonePoint
        {
            Id = AutonomousDarknessFallsPolicy.HomeExitZonePointId(realm), Realm = (ushort)realm,
            SourceRegion = 249, SourceX = 1, SourceY = 2, SourceZ = 3,
            TargetRegion = AutonomousDarknessFallsPolicy.HomeRegion(realm)
        }).ToArray();
        return (ordinary, raid, certificate, exits);
    }

    [Test]
    public void NavigationCertificate_AuditedWorld_OpensWithEveryProofAsGoal()
    {
        var (ordinary, raid, certificate, exits) = AuditedWorld();
        Assert.That(raid, Has.Length.EqualTo(AutonomousDarknessFallsGoalScope.RaidEventRows));
        string closed = AutonomousDarknessFallsNavigation.Judge(certificate, TestMeshHash.ToLowerInvariant(),
            ordinary.Concat(raid), exits, out var coverage);
        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.Null);
            Assert.That(coverage.ProofsMatched, Is.EqualTo(2000));
            Assert.That(coverage.ProofsRowGone + coverage.ProofsRowChanged, Is.Zero);
            Assert.That(coverage.RowsWithoutProof, Is.EqualTo(375));
            Assert.That(coverage.RowsUnknown, Is.Zero);
            Assert.That(coverage.CombatRows, Is.EqualTo(AutonomousDarknessFallsGoalScope.TotalCombatRows));
        });
    }

    [Test]
    public void NavigationCertificate_ChangedWorld_StaysOpenAndKeepsOnlyMatchingProofsAsGoals()
    {
        var (ordinary, raid, certificate, exits) = AuditedWorld();
        // As the 2026-11-11 spawn trim and the later period fixes did: rows removed (proven and
        // excluded ones), rows added, levels changed.
        var live = ordinary.Where((mob, index) => index % 10 != 0).ToList();   // 238 gone: 200 proven, 38 excluded
        live.Single(mob => mob.ObjectId == "df-1").Level = 26;                  // a proven row changed level
        live.Single(mob => mob.ObjectId == "df-2").Z += 5;                      // a proven row moved
        live.Single(mob => mob.ObjectId == "df-2001").Level = 61;               // an excluded row changed level
        live.Add(new DbMob { ObjectId = "added-1", Name = "Hopeless Soul", Region = 249, Level = 31, X = 5, Y = 5, Z = 5 });
        live.Add(new DbMob { ObjectId = "added-2", Name = "Commander Abgar", Region = 249, Level = 61, X = 6, Y = 6, Z = 6 });

        string closed = AutonomousDarknessFallsNavigation.Judge(certificate, TestMeshHash,
            live.Concat(raid), exits, out var coverage);
        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.Null, "Removed, added and changed ordinary rows never close Darkness Falls.");
            Assert.That(coverage.ProofsMatched, Is.EqualTo(2000 - 200 - 2));
            Assert.That(coverage.ProofsRowGone, Is.EqualTo(200));
            Assert.That(coverage.ProofsRowChanged, Is.EqualTo(2));
            Assert.That(coverage.RowsWithoutProof, Is.EqualTo(2 + (375 - 38) + 2));
            Assert.That(coverage.RowsUnknown, Is.EqualTo(2));
            Assert.That(coverage.OrdinaryRows, Is.EqualTo(live.Count));
            Assert.That(coverage.GoalIds, Does.Contain("df-3"));
            Assert.That(coverage.GoalIds, Does.Not.Contain("df-0"), "A proof whose row is gone is no goal.");
            Assert.That(coverage.GoalIds, Does.Not.Contain("df-1"), "A level-changed row is no goal.");
            Assert.That(coverage.GoalIds, Does.Not.Contain("df-2"), "A moved row is no goal.");
            Assert.That(coverage.GoalIds, Does.Not.Contain("added-1"), "A row without a proof is never a goal.");
            Assert.That(coverage.GoalIds, Does.Not.Contain("df-2001"), "An excluded row stays excluded.");
        });

        // A gone spawn's proof stays route geometry: its approach is still a chain waypoint.
        var goals = AutonomousDarknessFallsNavigation.SelectGoals(certificate.Spawns, coverage.GoalIds);
        var approaches = AutonomousDarknessFallsNavigation.BuildApproaches(certificate.Spawns, eRealm.Midgard);
        Assert.Multiple(() =>
        {
            Assert.That(goals, Has.Length.EqualTo(coverage.ProofsMatched));
            Assert.That(goals.Select(proof => proof.Id), Does.Not.Contain("df-0"));
            Assert.That(approaches, Has.Length.EqualTo(2000));
            Assert.That(approaches.Select(approach => approach.ProofId), Does.Contain("df-0"));
            Assert.That(approaches[0].ProofId, Is.EqualTo("df-0"), "Nearest to the entrance first.");
            Assert.That(approaches[0].InWaypoints, Has.Length.EqualTo(2));
        });

        // A proof whose id has since become a raid/event row is no ordinary goal either.
        string raidId = raid[0].ObjectId;
        live.RemoveAll(mob => mob.ObjectId == "df-3");
        certificate.Spawns[3].Id = raidId;
        Assert.That(AutonomousDarknessFallsNavigation.Judge(certificate, TestMeshHash, live.Concat(raid), exits,
            out coverage), Is.Null);
        Assert.That(coverage.GoalIds, Does.Not.Contain(raidId));
    }

    [Test]
    public void NavigationCertificate_ClosesOnMeshLinksExitsRaidManifestOrMalformedFile()
    {
        var (ordinary, raid, certificate, exits) = AuditedWorld();
        DbMob[] world = ordinary.Concat(raid).ToArray();
        string Judge() => AutonomousDarknessFallsNavigation.Judge(certificate, TestMeshHash, world, exits, out _);
        Assert.That(Judge(), Is.Null);

        Assert.That(AutonomousDarknessFallsNavigation.Judge(certificate, "0000", world, exits, out _),
            Is.Not.Null, "A changed mesh closes the certificate.");
        Assert.That(AutonomousDarknessFallsNavigation.Judge(certificate, null, world, exits, out _),
            Is.Not.Null, "A missing mesh closes it.");

        DbMob named = raid.First();
        named.X++;
        Assert.That(Judge(), Is.Not.Null, "A moved raid/event encounter closes it.");
        named.X--;
        world = world.Where(mob => mob != named).ToArray();
        Assert.That(Judge(), Is.Not.Null, "A missing named encounter closes it.");
        world = world.Append(named).ToArray();
        DbMob oro = raid.Single(mob => mob.ObjectId == "504d573f-deab-4cb2-9dbc-d9d053d7af2f");
        byte oroLevel = oro.Level;
        oro.Level = 68;
        Assert.That(Judge(), Is.Null, "A normal startup roll of Oro's 65-70 template must not close Darkness Falls.");
        oro.Level = 71;
        Assert.That(Judge(), Is.Not.Null);
        oro.Level = oroLevel;
        Assert.That(Judge(), Is.Null);

        Assert.That(AutonomousDarknessFallsNavigation.Judge(certificate, TestMeshHash, world, [], out _),
            Is.Not.Null, "Every proven route must end at a real realm exit row.");

        var links = certificate.TraversalLinks;
        certificate.TraversalLinks = [];
        Assert.That(Judge(), Is.Not.Null, "No traversal links: closed.");
        certificate.TraversalLinks = [new() { Kind = "Fall", Start = [0, 0, 0], End = [0, 0, 500] }];
        Assert.That(Judge(), Is.Not.Null, "A malformed traversal link: closed.");
        certificate.TraversalLinks = links;

        var proofs = certificate.Spawns;
        certificate.Spawns = proofs.Append(proofs[0]).ToArray();
        Assert.That(Judge(), Is.Not.Null, "A duplicate proof: closed.");
        certificate.Spawns = proofs;
        proofs[0].Routes[1].DistanceToOwnExit = 0;
        Assert.That(Judge(), Is.Not.Null, "Every realm needs a proven return to its own portal.");
        proofs[0].Routes[1].DistanceToOwnExit = 100;
        proofs[0].Routes[0].OutWaypoints = [[ordinary[0].X, ordinary[0].Y, ordinary[0].Z]];
        Assert.That(Judge(), Is.Not.Null, "A route needs independently auditable adjacent return legs.");
        proofs[0].Routes[0].OutWaypoints = [[ordinary[0].X, ordinary[0].Y, ordinary[0].Z], [1, 2, 3]];
        proofs[0].Flags = (uint)GameNPC.eFlags.FLYING;
        Assert.That(Judge(), Is.Not.Null, "A flagged flying mob cannot enter the certified goal subset.");
        proofs[0].Flags = 0;
        proofs[0].Level = 61;
        Assert.That(Judge(), Is.Not.Null, "No proof above level 60.");
        proofs[0].Level = 25;
        Assert.That(Judge(), Is.Null);

        var excluded = certificate.ExcludedOrdinarySpawns;
        certificate.ExcludedOrdinarySpawns = excluded.Append(ExclusionFor(ordinary[0], "UnverifiedRoute")).ToArray();
        Assert.That(Judge(), Is.Not.Null, "A row cannot be both proven and excluded.");
        DbMob flyer = ordinary[2100];
        flyer.Flags = (uint)GameNPC.eFlags.FLYING;
        certificate.ExcludedOrdinarySpawns = excluded.Select(item => item.Id == flyer.ObjectId
            ? ExclusionFor(flyer, "UnverifiedRoute") : item).ToArray();
        Assert.That(Judge(), Is.Not.Null, "A flying monster cannot be disguised as an ordinary deferred route.");
        certificate.ExcludedOrdinarySpawns = excluded;
        Assert.That(Judge(), Is.Null, "A row that no longer matches its exclusion is simply no goal.");

        Assert.That(AutonomousDarknessFallsGoalScope.IsRaidEventId(raid[0].ObjectId), Is.True);
        Assert.That(AutonomousDarknessFallsGoalScope.IsOrdinaryCatalogId(raid[0].ObjectId), Is.False);
    }

    [Test]
    public void RaidEventManifest_UsesExactIdsAndClosesOnDatabaseDrift()
    {
        AutonomousDarknessFallsGoalScope.RaidEventSpawn[] raid =
            AutonomousDarknessFallsGoalScope.SnapshotRaidEventRows();
        DbMob[] live = raid.Select(row => new DbMob
        {
            ObjectId = row.Id, Name = row.Name, Level = (byte)row.Level,
            ClassType = row.ClassType, Region = 249, Realm = 0,
            X = row.Spawn[0], Y = row.Spawn[1], Z = row.Spawn[2]
        }).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(raid, Has.Length.EqualTo(85));
            Assert.That(AutonomousDarknessFallsGoalScope.HasExactRaidEventRows(live, raid), Is.True);
            Assert.That(AutonomousDarknessFallsGoalScope.IsOrdinaryCatalogId("newly-scripted-not-certified"), Is.True,
                "Selection uses frozen IDs, not a broad class or level predicate; full proof still gates goals.");
        });
        live[0].ClassType = "DOL.GS.GameNPC.changed";
        Assert.That(AutonomousDarknessFallsGoalScope.HasExactRaidEventRows(live, raid), Is.False);
        live[0].ClassType = raid[0].ClassType;
        live[0].Name += " moved";
        Assert.That(AutonomousDarknessFallsGoalScope.HasExactRaidEventRows(live, raid), Is.False);
        live[0].Name = raid[0].Name;
        Assert.That(AutonomousDarknessFallsGoalScope.HasExactRaidEventRows(live.Skip(1), raid), Is.False);
    }

    [Test]
    public void RaidEventManifest_AllowsOnlyHighLordOrosTemplatedStartupLevel()
    {
        AutonomousDarknessFallsGoalScope.RaidEventSpawn[] raid =
            AutonomousDarknessFallsGoalScope.SnapshotRaidEventRows();
        DbMob[] live = raid.Select(row => new DbMob
        {
            ObjectId = row.Id, Name = row.Name, Level = (byte)row.Level,
            ClassType = row.ClassType, Region = 249, Realm = 0,
            X = row.Spawn[0], Y = row.Spawn[1], Z = row.Spawn[2]
        }).ToArray();
        const string oroId = "504d573f-deab-4cb2-9dbc-d9d053d7af2f";
        int oroIndex = Array.FindIndex(raid, row => row.Id == oroId);
        Assert.That(oroIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(raid[oroIndex].AllowedLevels, Is.EqualTo(new[] { 65, 66, 67, 68, 69, 70 }));

        foreach (byte level in new byte[] { 65, 66, 67, 68, 69, 70 })
        {
            live[oroIndex].Level = level;
            Assert.That(AutonomousDarknessFallsGoalScope.HasExactRaidEventRows(live, raid), Is.True,
                $"The scripted boss's original NPC template can save level {level} at startup.");
        }
        foreach (byte level in new byte[] { 64, 71 })
        {
            live[oroIndex].Level = level;
            Assert.That(AutonomousDarknessFallsGoalScope.HasExactRaidEventRows(live, raid), Is.False);
        }
        live[oroIndex].Level = 68;
        live[oroIndex].X++;
        Assert.That(AutonomousDarknessFallsGoalScope.HasExactRaidEventRows(live, raid), Is.False,
            "The level allowance must not mask a moved boss.");
        live[oroIndex].X--;
        live[0].Level++;
        Assert.That(AutonomousDarknessFallsGoalScope.HasExactRaidEventRows(live, raid), Is.False,
            "Other raid monsters keep their exact frozen level.");
    }

    [Test]
    public void ExitCertificate_RequiresRealMatchingRealmPortalAndLanding()
    {
        var proof = new AutonomousDarknessFallsNavigation.SpawnProof
        {
            Routes =
            [
                new()
                {
                    Realm = eRealm.Albion, ExitZonePointId = 74, ExitTargetRegion = 1,
                    Exit = [36118, 30504, 22381]
                }
            ]
        };
        var albion = new DbZonePoint
        {
            Id = 74, Realm = 1, SourceRegion = 249, SourceX = 36118,
            SourceY = 30504, SourceZ = 22381, TargetRegion = 1
        };
        var midgard = new DbZonePoint
        {
            Id = 74, Realm = 2, SourceRegion = 249, SourceX = 36118,
            SourceY = 30504, SourceZ = 22381, TargetRegion = 100
        };
        var wrongPhysicalPortal = new DbZonePoint
        {
            Id = 70, Realm = 1, SourceRegion = 249, SourceX = 36118,
            SourceY = 30504, SourceZ = 22381, TargetRegion = 1
        };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsNavigation.HasAuthoritativeRealmExits([proof], [albion]), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.HasAuthoritativeRealmExits([proof], [midgard]), Is.False);
        });
        proof.Routes[0].ExitZonePointId = 70;
        Assert.That(AutonomousDarknessFallsNavigation.HasAuthoritativeRealmExits([proof], [wrongPhysicalPortal]), Is.False,
            "A correctly realm-tagged row at the wrong physical DF exit is never acceptable.");
        proof.Routes[0].ExitZonePointId = 74;
        proof.Routes[0].Exit[0] += 100;
        Assert.That(AutonomousDarknessFallsNavigation.HasAuthoritativeRealmExits([proof], [albion]), Is.False,
            "An exit on the wrong corridor is not enough merely because its ID matches.");
    }

    [Test]
    public void EntryCertificate_UsesOwnRealPortalArrival_NotSparseHistoricalSamples()
    {
        var hib = new DbZonePoint
        {
            SourceRegion = 200, TargetRegion = 249, Realm = 3,
            TargetX = 44310, TargetY = 37978, TargetZ = 20832
        };
        Vector3 arrival = new(44310, 37978, 20833);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsNavigation.MatchesCertifiedEntrance(
                hib, eRealm.Hibernia, arrival), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.MatchesCertifiedEntrance(
                hib, eRealm.Albion, arrival), Is.False);
            Assert.That(AutonomousDungeonGoalCatalog.CanUseEntrance(
                hib, 249, 44310, 37978), Is.False,
                "Without a physically audited embedded certificate, old DF point samples cannot open bot goals.");
        });
        hib.TargetX += 100;
        Assert.That(AutonomousDarknessFallsNavigation.MatchesCertifiedEntrance(
            hib, eRealm.Hibernia, arrival), Is.False);
        hib.TargetX -= 100;
        hib.SourceRegion = 1;
        Assert.That(AutonomousDarknessFallsNavigation.MatchesCertifiedEntrance(
            hib, eRealm.Hibernia, arrival), Is.False);
    }

    [Test]
    public void CertificateLazy_IsNotEvaluatedBeforeDatabaseIsReady()
    {
        int calls = 0;
        bool Loader() { calls++; return true; }
        Assert.That(AutonomousDarknessFallsNavigation.ReadyWhen(false, Loader), Is.False);
        Assert.That(calls, Is.Zero);
        Assert.That(AutonomousDarknessFallsNavigation.ReadyWhen(true, Loader), Is.True);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void NativePartialPath_NeverBecomesACompleteDarknessFallsCorridor()
    {
        Vector3 start = new(100, 100, 1000), end = new(200, 100, 900);
        WrappedPathfindingNode[] valid =
        [
            new(start, EDtPolyFlags.Walk),
            new(new Vector3(120, 100, 1000), EDtPolyFlags.Walk),
            new(new Vector3(125, 100, 950), EDtPolyFlags.Jump),
            new(new Vector3(130, 100, 900), EDtPolyFlags.Jump),
            new(end, EDtPolyFlags.Walk)
        ];
        AutonomousDarknessFallsNavigation.TraversalLink[] approved =
        [
            new() { Kind = "Climb", Start = [120, 100, 1000], End = [125, 100, 950], Bidirectional = true },
            new() { Kind = "Climb", Start = [125, 100, 950], End = [130, 100, 900], Bidirectional = true }
        ];
        WrappedPathfindingNode[] falseFloorLeap =
        [
            new(start, EDtPolyFlags.Walk),
            new(new Vector3(105, 100, 200), EDtPolyFlags.Walk),
            new(end, EDtPolyFlags.Walk)
        ];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsNavigation.MayQueuePath(true, 249, false,
                new(PathfindingStatus.PathFound, valid.Length), valid, start, end, approved), Is.False,
                "The real bot mover must remain fail-closed without a matching installed certificate.");
            Assert.That(AutonomousDarknessFallsNavigation.MayQueuePath(true, 249, true,
                new(PathfindingStatus.PathFound, valid.Length), valid, start, end, approved), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.MayQueuePath(true, 249, true,
                new(PathfindingStatus.PartialPathFound, valid.Length), valid, start, end, approved), Is.False);
            Assert.That(AutonomousDarknessFallsNavigation.MayQueuePath(true, 249, true,
                new(PathfindingStatus.PathFound, valid.Length), valid, start, end), Is.False,
                "An unapproved Jump cannot be sent to the ordinary XYZ interpolator.");
            Assert.That(AutonomousDarknessFallsNavigation.MayQueuePath(true, 250, false,
                new(PathfindingStatus.PartialPathFound, valid.Length), valid, start, end), Is.True,
                "Other zones retain their existing path behavior.");
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, valid.Length), valid, start, end, approved), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PartialPathFound, valid.Length), valid, start, end, approved), Is.False,
                "Even a partial path whose last numerical node is the endpoint is not complete.");
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, falseFloorLeap.Length), falseFloorLeap, start, end), Is.False);
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, valid.Length), valid, start, end), Is.False,
                "An otherwise plausible Jump is not valid without a matching audited climb link.");
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, valid.Length), valid, start, end, approved[..1]), Is.False,
                "Every steep link in the chain needs its own proof.");
        });
    }

    [Test]
    public void LongFlatApproachToFallLip_IsNotMisclassifiedAsTheFall()
    {
        Vector3 start = new(100, 100, 1000);
        Vector3 lip = new(375, 100, 1000);
        Vector3 landing = new(400, 100, 800);
        Vector3 end = new(405, 100, 800);
        WrappedPathfindingNode[] nodes =
        [
            new(start, EDtPolyFlags.Walk),
            new(lip, EDtPolyFlags.Jump),
            new(landing, EDtPolyFlags.Jump),
            new(end, EDtPolyFlags.Walk)
        ];
        AutonomousDarknessFallsNavigation.TraversalLink[] directedFall =
        [
            new() { Kind = "Fall", Start = [375, 100, 1000],
                Air = [400, 100, 1000], End = [400, 100, 800], Bidirectional = false }
        ];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, nodes.Length), nodes, start, end, directedFall), Is.True,
                "A long flat walk into the lip is not itself an off-mesh traversal.");
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, nodes.Length), nodes, start, end), Is.False,
                "The actual fall still requires its exact directed proof.");
        });
    }

    [Test]
    public void ClosedOrdinaryGoals_StillPermitACompleteWalkToOwnPhysicalExit()
    {
        Vector3 from = new(36040, 30480, 22381), ownExit = new(36118, 30504, 22381);
        WrappedPathfindingNode[] walk =
        [
            new(from, EDtPolyFlags.Walk),
            new(ownExit, EDtPolyFlags.Walk)
        ];
        WrappedPathfindingNode[] jump =
        [
            new(from, EDtPolyFlags.Walk),
            new(ownExit, EDtPolyFlags.Jump)
        ];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsNavigation.MayQueueOwnExitGroundPath(eRealm.Albion,
                new(PathfindingStatus.PathFound, walk.Length), walk, from, ownExit), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.MayQueueOwnExitGroundPath(eRealm.Midgard,
                new(PathfindingStatus.PathFound, walk.Length), walk, from, ownExit), Is.False);
            Assert.That(AutonomousDarknessFallsNavigation.MayQueueOwnExitGroundPath(eRealm.Albion,
                new(PathfindingStatus.PartialPathFound, walk.Length), walk, from, ownExit), Is.False);
            Assert.That(AutonomousDarknessFallsNavigation.MayQueueOwnExitGroundPath(eRealm.Albion,
                new(PathfindingStatus.PathFound, jump.Length), jump, from, ownExit), Is.False);
        });
    }

    [Test]
    public void EntranceFall_IsCertifiedOnlyInTheDownwardDirection()
    {
        Vector3 upper = new(100, 100, 1000), lower = new(140, 100, 834);
        var fall = new AutonomousDarknessFallsNavigation.TraversalLink
        {
            Kind = "Fall", Start = [upper.X, upper.Y, upper.Z],
            Air = [lower.X, lower.Y, upper.Z + 8],
            End = [lower.X, lower.Y, lower.Z], Bidirectional = false
        };
        WrappedPathfindingNode[] down =
        [
            new(upper, EDtPolyFlags.Walk), new(lower, EDtPolyFlags.Jump)
        ];
        WrappedPathfindingNode[] downReplot =
        [
            new(upper, EDtPolyFlags.Walk), new(lower, EDtPolyFlags.Walk)
        ];
        WrappedPathfindingNode[] up =
        [
            new(lower, EDtPolyFlags.Jump), new(upper, EDtPolyFlags.Walk)
        ];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, down.Length), down, upper, lower, [fall]), Is.True,
                "A measured one-way DF ledge can connect entrance to the lower floor.");
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, downReplot.Length), downReplot, upper, lower, [fall]), Is.True,
                "An exact-lip Detour replot can mark the same certified drop Walk rather than Jump.");
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, downReplot.Length), downReplot, upper, lower), Is.False,
                "A steep Walk with no exact fall manifest remains forbidden.");
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, up.Length), up, lower, upper, [fall]), Is.False,
                "The same ledge is never an escape route back upward.");
            Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                new(PathfindingStatus.PathFound, down.Length), down, upper, lower), Is.False,
                "An unproven 166-unit drop is never inferred from a Jump flag.");
            Assert.That(AutonomousDarknessFallsNavigation.TryResolveFall(upper, lower, [fall],
                out Vector3 air, out Vector3 landing), Is.True);
            Assert.That(air, Is.EqualTo(new Vector3(lower.X, lower.Y, upper.Z + 8)));
            Assert.That(landing, Is.EqualTo(lower));
            Assert.That(AutonomousDarknessFallsNavigation.TryResolveFall(lower, upper, [fall],
                out _, out _), Is.False, "A fall cannot be followed upward.");
        });
        fall.Air[0] += 100;
        Assert.That(AutonomousDarknessFallsNavigation.TryResolveFall(upper, lower, [fall],
            out _, out _), Is.False, "The vertical descent must stay over its landing, not cross the wall.");
    }

    [Test]
    public void AllNineEntranceFallLipReplots_RequireExactForwardManifestEdges()
    {
        // Collision-backed DF ledges from the isolated fall manifest. These
        // are one-way gravity drops, not climbable stairs or generic Jump links.
        (Vector3 Upper, Vector3 Lower)[] ledges =
        [
            (new(33508f, 27951.7f, 22884.4f), new(33541f, 27951.2f, 22709.2f)),
            (new(33805.8f, 27957.4f, 22704.6f), new(33839.9f, 27956.9f, 22513.8f)),
            (new(34269f, 27960.2f, 22512.6f), new(34301f, 27960.5f, 22384.7f)),
            (new(16196.2f, 18680.7f, 22893.4f), new(16164f, 18680.4f, 22706.7f)),
            (new(15873.2f, 18671.4f, 22704.6f), new(15838.1f, 18672.1f, 22512.6f)),
            (new(15407.7f, 18658.5f, 22512.6f), new(15373.1f, 18660.1f, 22381.4f)),
            (new(44310f, 37978f, 20833.1f), new(44279.8f, 37988.7f, 20659f)),
            (new(44031f, 38021.4f, 20656.6f), new(43997f, 38021.5f, 20465.8f)),
            (new(43560.6f, 38054.6f, 20464.6f), new(43528.8f, 38057.9f, 20336.6f)),
        ];
        Assert.That(ledges, Has.Length.EqualTo(9));
        foreach ((Vector3 upper, Vector3 lower) in ledges)
        {
            var fall = new AutonomousDarknessFallsNavigation.TraversalLink
            {
                Kind = "Fall", Start = [upper.X, upper.Y, upper.Z],
                Air = [lower.X, lower.Y, upper.Z + 8],
                End = [lower.X, lower.Y, lower.Z], Bidirectional = false
            };
            WrappedPathfindingNode[] down =
            [
                new(upper, EDtPolyFlags.Walk), new(lower, EDtPolyFlags.Walk)
            ];
            WrappedPathfindingNode[] reverse =
            [
                new(lower, EDtPolyFlags.Walk), new(upper, EDtPolyFlags.Walk)
            ];
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                    new(PathfindingStatus.PathFound, 2), down, upper, lower, [fall]), Is.True);
                Assert.That(AutonomousDarknessFallsNavigation.TryResolveFall(
                    upper, lower, [fall], out _, out _), Is.True);
                Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                    new(PathfindingStatus.PathFound, 2), reverse, lower, upper, [fall]), Is.False);
                Assert.That(AutonomousDarknessFallsNavigation.TryResolveFall(
                    lower, upper, [fall], out _, out _), Is.False);
                Assert.That(AutonomousDarknessFallsNavigation.IsStrictSegment(
                    new(PathfindingStatus.PathFound, 2), down, upper, lower), Is.False);
            });
        }
    }

    [Test]
    public void OrderedDarknessFallsRoute_RejoinsOnlyThroughACompleteThreeDimensionalLeg()
    {
        Assert.That(AutonomousDarknessFallsRoutePlan.TryGetCampMobId(
            "df-live:000432e9-0a51-4d37-94f8-7fdb7f73ae4b:249:room",
            out string exactId), Is.True);
        Assert.That(exactId, Is.EqualTo("000432e9-0a51-4d37-94f8-7fdb7f73ae4b"));
        Assert.That(AutonomousDarknessFallsRoutePlan.TryGetCampMobId(
            "dungeon-live:249:room", out _), Is.False);
        Assert.That(AutonomousDarknessFallsRoutePlan.TryGetCampMobId(
            "df-live:missing:249:room", out _), Is.False);

        Vector3[] chain = [new(100, 100, 1000), new(200, 100, 1000), new(232, 100, 832)];
        Vector3 upper = new(202, 100, 1000);
        Assert.That(AutonomousDarknessFallsRoutePlan.TryRejoin(upper, chain,
            (_, _) => false, out int next), Is.True);
        Assert.That(next, Is.EqualTo(2), "A bot already at the upper lip advances toward its certified fall.");

        Vector3 lower = new(230, 100, 832);
        Assert.That(AutonomousDarknessFallsRoutePlan.TryRejoin(lower, chain,
            (_, _) => false, out next), Is.True);
        Assert.That(next, Is.EqualTo(3), "A bot already below the one-way drop never returns upward.");

        Vector3 wrongFloor = new(205, 100, 850);
        Assert.That(AutonomousDarknessFallsRoutePlan.TryRejoin(wrongFloor, chain,
            (_, _) => false, out _), Is.False,
            "XY closeness across stacked DF floors does not constitute a route.");
        Assert.That(AutonomousDarknessFallsRoutePlan.TryRejoin(wrongFloor, chain,
            (_, end) => end == chain[2], out next), Is.True,
            "A bot can rejoin that lower floor only after a complete native leg is proven.");
        Assert.That(next, Is.EqualTo(2));

        Vector3 reachable = new(145, 100, 1000);
        Assert.That(AutonomousDarknessFallsRoutePlan.TryRejoin(reachable, chain,
            (_, end) => end == chain[1], out next), Is.True);
        Assert.That(next, Is.EqualTo(1));
        Assert.That(AutonomousDarknessFallsRoutePlan.TryGetWaypoints(
            [[100, 100, 1000], [200, 100, 1000]], out Vector3[] parsed), Is.True);
        Assert.That(parsed, Has.Length.EqualTo(2));
        Assert.That(AutonomousDarknessFallsRoutePlan.TryGetWaypoints(
            [[100, 100, 1000], [float.NaN, 100, 1000]], out _), Is.False);
    }

    [Test]
    public void FailedHomeExitProbe_BacksOffUnlessRouteOrPositionChanges()
    {
        Vector3 position = new(100, 200, 300);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsRoutePlan.ShouldProbeExit(
                null, "out:3:72", position, position, 1_000, 16_000), Is.True);
            Assert.That(AutonomousDarknessFallsRoutePlan.ShouldProbeExit(
                "out:3:72", "out:3:72", position, position, 10_000, 16_000), Is.False);
            Assert.That(AutonomousDarknessFallsRoutePlan.ShouldProbeExit(
                "out:3:72", "out:3:72", position, position, 16_000, 16_000), Is.True);
            Assert.That(AutonomousDarknessFallsRoutePlan.ShouldProbeExit(
                "out:3:72", "out:1:74", position, position, 10_000, 16_000), Is.True);
            Assert.That(AutonomousDarknessFallsRoutePlan.ShouldProbeExit(
                "out:3:72", "out:3:72", position,
                position + new Vector3(97, 0, 0), 10_000, 16_000), Is.True);
        });
    }

    [Test]
    public void SameNamedCamps_PreferOwnWingAndNearestVerifiedRooms()
    {
        var camps = new[]
        {
            (Name: "lurker", Level: 25, Wing: eRealm.Albion, Distance: 900f),
            (Name: "lurker", Level: 25, Wing: eRealm.Midgard, Distance: 1100f),
            (Name: "lurker", Level: 25, Wing: eRealm.Hibernia, Distance: 1200f),
            (Name: "demon", Level: 26, Wing: eRealm.None, Distance: 1800f),
            (Name: "demon", Level: 26, Wing: eRealm.None, Distance: 5900f),
        };
        var selected = AutonomousDarknessFallsNavigation.PreferEntranceSide(camps, eRealm.Midgard,
            camp => camp.Name, camp => camp.Level, camp => camp.Wing, camp => camp.Distance);
        Assert.That(selected, Has.Length.EqualTo(2));
        Assert.That(selected, Does.Contain(camps[1]));
        Assert.That(selected, Does.Contain(camps[3]));
        Assert.That(selected, Does.Not.Contain(camps[0]));
        Assert.That(selected, Does.Not.Contain(camps[2]));
        Assert.That(selected, Does.Not.Contain(camps[4]));
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsNavigation.IsInAssignedRoom(
                new(1000, 1000, 500), new(1750, 1000, 510)), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.IsInAssignedRoom(
                new(1000, 1000, 500), new(5000, 1000, 510)), Is.False,
                "A same-name mob from another DF wing is not the assigned camp.");
            Assert.That(AutonomousDarknessFallsNavigation.IsInAssignedRoom(
                new(1000, 1000, 500), new(1000, 1000, 1100)), Is.False,
                "A same-XY mob on another DF floor is not the assigned camp.");
        });
    }

    [Test]
    public void PveContracts_AreTimeOnlyEvenForLegacyKillRecords()
    {
        DateTime now = DateTime.UtcNow;
        var group = new OfflineWorldBotRecord
        {
            ObjectiveKind = "GroupPve",
            ObjectiveAssignmentId = "group-1",
            ObjectivePveMode = "Time",
            ObjectiveExpiresUtc = now.AddMinutes(45).ToString("O"),
        };
        var soloKills = new OfflineWorldBotRecord
        {
            ObjectiveKind = "SoloPve",
            ObjectiveAssignmentId = "solo-1",
            ObjectivePveMode = "Kills",
            ObjectivePveKillTarget = 20,
            ObjectivePveKills = 19,
            ObjectiveExpiresUtc = now.AddMinutes(90).ToString("O"),
        };

        Assert.Multiple(() =>
        {
            Assert.That(AutonomousObjectiveAssignments.HasActivePveAssignment(group, now), Is.True);
            Assert.That(AutonomousObjectiveAssignments.HasActivePveAssignment(group, now.AddMinutes(46)), Is.False);
            Assert.That(AutonomousObjectiveAssignments.HasActivePveAssignment(soloKills, now), Is.True);
            Assert.That(AutonomousObjectiveAssignments.HasActivePveAssignment(soloKills, now.AddMinutes(91)), Is.False);
            soloKills.ObjectivePveKills = 20;
            Assert.That(AutonomousObjectiveAssignments.HasActivePveAssignment(soloKills, now), Is.True);
            Assert.That(Enumerable.Range(0, 200).Select(seed => AutonomousObjectiveAssignments.RollPveTenure(new Random(seed))),
                Has.All.InRange(AutonomousObjectiveAssignments.MinimumPveTenure, AutonomousObjectiveAssignments.MaximumPveTenure));
            Assert.That(AutonomousObjectiveAssignments.MinimumPveTenure, Is.EqualTo(TimeSpan.FromMinutes(45)));
            Assert.That(AutonomousObjectiveAssignments.MaximumPveTenure, Is.EqualTo(TimeSpan.FromMinutes(120)));
            Assert.That(Enumerable.Range(0, 200).Select(seed => AutonomousObjectiveAssignments.RollSoloPveCompletionMode(new Random(seed))).Distinct().Count(),
                Is.EqualTo(1));
            Assert.That(AutonomousObjectiveAssignments.RollSoloPveCompletionMode(), Is.EqualTo(eAutonomousPveCompletionMode.Time));
        });
    }
}
