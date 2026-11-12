using System;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Owner decision O9 (veteran review 2026-11-11): a stealther's "inc" report starts the defence rally earlier. In 2003 a
/// scout at the milegate called "~20 mids inc" and defenders moved before the doors were hit. Today a planned siege's
/// defenders respond only once the attack lands (RealmEventPolicy.CanRespond). Now, when a solo stealther of the
/// defending realm sees a sizeable force of the attacking realm near the target keep of a planned siege, that siege counts
/// as scouted and the defenders may rally at once. Only when an existing rally starts changes; no combat numbers, caps or
/// siege sizes. The stealther's own frontier scan supplies the count (no new scans).
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    private static readonly Logger ScoutLog = LoggerManager.Create(typeof(AutonomousRvrEventLayer));

    [ServerProperties.ServerProperty("autonomous", "rvr_scout_reports_rally",
        "Owner decision O9 (2026-11-11): a stealther that sees 4+ attackers of a planned siege near its realm's target keep lets that keep's defenders rally before the attack lands.", true)]
    public static bool ScoutReportsRally = true;

    public const int ScoutReportMinimumEnemies = 4;
    public const int ScoutReportRadius = 12_000;

    /// <summary>Pure rule: this sighting scouts that siege (defender's scout, attacker's force, near the target).</summary>
    public static bool ScoutReportApplies(eRealm scoutRealm, eRealm sightedRealm, int sightedCount, ushort region, int x, int y,
        eRealm defenderRealm, eRealm attackerRealm, ushort targetRegion, int targetX, int targetY, bool alreadyRallying)
    {
        if (alreadyRallying || sightedCount < ScoutReportMinimumEnemies || scoutRealm != defenderRealm ||
            sightedRealm != attackerRealm || region != targetRegion)
            return false;
        long dx = (long)x - targetX, dy = (long)y - targetY;
        return dx * dx + dy * dy <= (long)ScoutReportRadius * ScoutReportRadius;
    }

    /// <summary>
    /// A stealther of <paramref name="scoutRealm"/> sees <paramref name="count"/> enemies of <paramref name="sightedRealm"/>
    /// at a spot: a planned siege of theirs on a nearby keep of the scout's realm counts as scouted. True when it did.
    /// </summary>
    public static bool ReportScoutedForce(string scoutName, eRealm scoutRealm, eRealm sightedRealm, int count, ushort region,
        int x, int y)
    {
        if (!ScoutReportsRally || count < ScoutReportMinimumEnemies)
            return false;
        lock (Sync)
        {
            foreach (ActiveEvent active in Events.Values)
            {
                if (active.DefenseReaction || active.Target == null ||
                    !ScoutReportApplies(scoutRealm, sightedRealm, count, region, x, y, active.DefenderRealm, active.AttackerRealm,
                        active.Target.RegionId, active.Target.X, active.Target.Y,
                        active.Scouted || active.AttackObserved || active.BattleStarted))
                    continue;
                active.Scouted = true;
                RealmEventNotices.Queue(active.TargetId, active.DefenderRealm,
                    $"Scouts report {RoughCount(count)} {GlobalConstants.RealmToName(sightedRealm)} heading for {Place(active.Target.Name)}! Available warbands can reinforce the defenders.");
                ScoutLog.Info($"AUTONOMOUS_RVR_SCOUT_REPORT scout=\"{scoutName}\" realm={scoutRealm} sighted={sightedRealm} count={count} " +
                              $"target=\"{active.Target.Name}\" region={region} at={x},{y}");
                return true;
            }
        }
        return false;
    }

    /// <summary>Veteran shorthand for a head count ("~6", "1fg", "2fg+", "a zerg").</summary>
    public static string RoughCount(int count) => count switch
    {
        < 8 => $"~{count}",
        < 12 => "a full group of",
        < 24 => "two groups of",
        _ => "a zerg of",
    };
}
