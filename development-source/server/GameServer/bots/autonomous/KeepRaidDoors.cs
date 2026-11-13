using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// The one rule for which keep doors a raid breaks, as a player raid chose them (keeps audit 2026-11-11 L2, relics audit
/// L1). Every caller that asks "which door now" or "is the keep still sealed" uses it: the ram's door, the door the army
/// batters by hand, the gate the approach planner aims at, the lord's shield against bots and the defenders' posts.
/// - An ordinary keep: the outer gate, then the keep door. A postern, or the unflagged half of one (Berkstead, Boldiam and
///   Sursbrooke have a door 03 standing 65 units from their flagged postern door 04), is never a raid door: players left
///   posterns alone, and the lord's shield already ignored it while the planner, the rams and the batterers did not.
/// - A relic keep: only the doors on the walk from outside the outer gate up to the relic pad. At all six keeps that is
///   the outer gate, the relic gate and one of the inner keep doors 10/11; the upper doors 07-09 are not on the way.
///   The walk is planned once per keep with every door counted open (doors never move) and cached; until a walk is
///   known every attackable door counts, as before.
/// Order: lowest door number first (01 outer gate, 02 keep or relic gate, then the inner door), then outermost.
/// Cost: a scan of one keep's doors (at most 11) per call; one navmesh walk per relic keep per server run.
/// </summary>
public static class KeepRaidDoors
{
    /// <summary>Pure rule: whether a door is one the raid must break.</summary>
    public static bool IsRaidGate(bool attackable, bool posternLike, bool relicKeep, bool? onShrineWalk) =>
        attackable && (relicKeep ? onShrineWalk ?? true : !posternLike);

    /// <summary>Pure order: the next shut raid door (lowest number, then farthest from the keep's centre); -1 for none.</summary>
    public static int NextRaidGate(IEnumerable<(int Number, bool Shut, float CentreDistance)> raidGates)
    {
        int best = -1, bestNumber = int.MaxValue;
        float bestDistance = float.MinValue;
        int index = 0;
        foreach (var gate in raidGates ?? [])
        {
            if (gate.Shut && (gate.Number < bestNumber || gate.Number == bestNumber && gate.CentreDistance > bestDistance))
            {
                best = index;
                bestNumber = gate.Number;
                bestDistance = gate.CentreDistance;
            }
            index++;
        }
        return best;
    }

    /// <summary>Pure rule: the doors a walk passes, from the doors' numbers and positions (null when the walk does not
    /// even pass the outer gate: then it proves nothing about the way in).</summary>
    public static HashSet<int> DoorsOnWalk(IReadOnlyList<Vector3> walk, IEnumerable<(int Number, Vector3 Position)> doors)
    {
        var passed = new HashSet<int>();
        foreach (var (number, position) in doors ?? [])
            if (AutonomousRelicShrine.WalkPassesDoor(walk, position))
                passed.Add(number);
        return passed.Contains(1) && passed.Count >= 2 ? passed : null;
    }

    /// <summary>The door's number within its keep (01 outer gate, 02 keep or relic gate, ...).</summary>
    public static int Number(GameKeepDoor door) => door.DoorId % 100;

    /// <summary>A postern, or the other half of one (within 200 of a flagged postern door).</summary>
    public static bool PosternLike(AbstractGameKeep keep, GameKeepDoor door) =>
        door.IsPostern || keep.Doors.Values.Any(p => p != door && p.IsPostern && p.IsWithinRadius(door, 200));

    public static bool IsRaidGate(AbstractGameKeep keep, GameKeepDoor door)
    {
        if (keep == null || door == null || !door.IsAttackableDoor) return false;
        if (keep.IsRelic)
            return IsRaidGate(true, false, true, ShrineWalkDoors(keep) is { } walk ? walk.Contains(Number(door)) : null);
        return IsRaidGate(true, PosternLike(keep, door), false, null);
    }

    public static bool IsShutRaidGate(AbstractGameKeep keep, GameKeepDoor door) =>
        door != null && door.IsAlive && door.State == eDoorState.Closed && IsRaidGate(keep, door);

    /// <summary>The raid doors still standing shut.</summary>
    public static GameKeepDoor[] ShutRaidGates(AbstractGameKeep keep) =>
        keep == null ? [] : keep.Doors.Values.Where(door => IsShutRaidGate(keep, door)).ToArray();

    /// <summary>Whether the raid still has a door to break (the lord stays off-limits to bots while one stands).</summary>
    public static bool Sealed(AbstractGameKeep keep) => keep != null && keep.Doors.Values.Any(door => IsShutRaidGate(keep, door));

    /// <summary>The door the raid works on now: rams, batterers and the approach all go for this one.</summary>
    public static GameKeepDoor NextShutRaidGate(AbstractGameKeep keep)
    {
        GameKeepDoor[] shut = ShutRaidGates(keep);
        int index = NextRaidGate(shut.Select(door => (Number(door), true,
            Vector2.Distance(new(door.X, door.Y), new(keep.X, keep.Y)))));
        return index < 0 ? null : shut[index];
    }

    /// <summary>
    /// Pure: a spot <paramref name="distance"/> in front of a door, on its outer side, along its passage (battlegrounds fix
    /// 2026-11-12, S1b). A keep door's passage runs along its heading (all four battleground keeps' door pairs lie on their
    /// heading's line); the side away from the keep's centre is the outer one. Caer Caledon's doors sit 40 degrees off the
    /// line to the keep's centre, so "away from the centre" alone puts the spot in the gatehouse wall.
    /// </summary>
    public static Vector2 StandOff(Vector2 door, int heading, Vector2 keepCentre, float distance)
    {
        double angle = heading * (Math.PI * 2 / 4096);
        Vector2 axis = new((float)-Math.Sin(angle), (float)Math.Cos(angle));
        if (Vector2.Dot(door - keepCentre, axis) < 0) axis = -axis;
        return door + axis * distance;
    }

    /// <summary>Where a bot stands to batter <paramref name="door"/>: 150 in front of it on its outer side (in the gate
    /// passage for an inner door), well inside the 450 the door work needs, never on the door's own blocking polygons.</summary>
    public static Vector3 StandOff(AbstractGameKeep keep, GameKeepDoor door, float distance = 150)
    {
        Vector2 spot = StandOff(new(door.X, door.Y), door.Heading, new(keep.X, keep.Y), distance);
        return new(spot.X, spot.Y, door.Z);
    }

    // ---- the walk to a relic pad, once per keep

    private static readonly object WalkGate = new();
    private static readonly Dictionary<int, HashSet<int>> Walks = new();
    private static readonly Dictionary<int, long> WalkRetryAt = new();
    public const long WalkRetryMilliseconds = 10 * 60_000;

    /// <summary>The numbers of a relic keep's doors on the walk from outside its outer gate to its pad; null while unknown.</summary>
    public static HashSet<int> ShrineWalkDoors(AbstractGameKeep keep)
    {
        if (keep?.IsRelic != true) return null;
        lock (WalkGate)
        {
            if (Walks.TryGetValue(keep.KeepID, out HashSet<int> known)) return known;
            long now = GameLoop.GameLoopTime;
            if (WalkRetryAt.TryGetValue(keep.KeepID, out long retry) && now < retry) return null;
            WalkRetryAt[keep.KeepID] = now + WalkRetryMilliseconds;
            HashSet<int> walked = null;
            try { walked = PlanShrineWalk(keep); }
            catch (Exception) { }
            var log = DOL.Logging.LoggerManager.Create(typeof(KeepRaidDoors));
            if (walked == null)
            {
                if (log.IsInfoEnabled) log.Info($"RVR_RAID_DOORS keep={keep.KeepID} name=\"{keep.Name}\" walk=unknown retryMinutes={WalkRetryMilliseconds / 60_000}");
                return null;
            }
            Walks[keep.KeepID] = walked;
            if (log.IsInfoEnabled) log.Info($"RVR_RAID_DOORS keep={keep.KeepID} name=\"{keep.Name}\" doors={string.Join(",", walked.OrderBy(n => n).Select(n => n.ToString("00")))}");
            return walked;
        }
    }

    private static HashSet<int> PlanShrineWalk(AbstractGameKeep keep)
    {
        var nav = PathfindingProvider.Instance;
        GameRelicPad pad = AutonomousRelicShrine.PadOf(keep);
        GameKeepDoor gate = keep.Doors.Values.Where(door => Number(door) == 1).FirstOrDefault();
        Region region = WorldMgr.GetRegion(keep.Region);
        if (nav == null || !nav.IsAvailable || pad == null || gate == null || region == null) return null;
        Vector2 outward = new(gate.X - keep.X, gate.Y - keep.Y);
        if (outward.LengthSquared() < 1) return null;
        outward = Vector2.Normalize(outward);
        Vector3 outside = new(gate.X + outward.X * 400, gate.Y + outward.Y * 400, gate.Z);
        Zone zone = region.GetZone((int)outside.X, (int)outside.Y);
        if (zone == null || !nav.HasNavmesh(zone) || region.GetZone(pad.X, pad.Y) != zone) return null;
        Vector3? start = nav.GetClosestPoint(zone, outside, 96, 96, 256, nav.DefaultFilters);
        Vector3? end = AutonomousRelicShrine.ShrineFloor(nav, pad);
        if (!start.HasValue || !end.HasValue) return null;
        // Every door counts open here (the default filters walk through door polygons): this is the way in, not
        // whether it is open now.
        var nodes = new WrappedPathfindingNode[256];
        PathfindingResult result = nav.GetPathStraight(zone, start.Value, end.Value, nav.DefaultFilters, nodes);
        if (result.Status != PathfindingStatus.PathFound || result.NodeCount <= 0) return null;
        var walk = new List<Vector3>(Math.Min(result.NodeCount, nodes.Length) + 1) { start.Value };
        for (int i = 0; i < Math.Min(result.NodeCount, nodes.Length); i++) walk.Add(nodes[i].Position);
        return DoorsOnWalk(walk, keep.Doors.Values.Where(door => door.IsAttackableDoor)
            .Select(door => (Number(door), new Vector3(door.X, door.Y, door.Z))));
    }
}
