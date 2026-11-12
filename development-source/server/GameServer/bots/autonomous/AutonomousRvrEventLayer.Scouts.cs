namespace DOL.GS;

public static partial class AutonomousRvrEventLayer
{
    /// <summary>
    /// Owner decision O9 (veteran-bots review 2026-11-11): a scout's "inc" report near one of its realm's keeps that a
    /// planned enemy siege is marching on marks that siege as scouted (the O9 flag ReportScoutedForce also sets), so the
    /// defense rally (RealmEventPolicy.CanRespond) starts when the army is spotted instead of when the first blow lands on
    /// the door. Only the timing of an existing rally changes; its size, caps and everything it does are untouched. True
    /// when it started the rally.
    /// </summary>
    public static bool ObserveScoutReport(string targetId, eRealm reporterRealm, eRealm sightedRealm)
    {
        if (string.IsNullOrEmpty(targetId))
            return false;
        lock (Sync)
        {
            if (!ScoutReportsRally || !Events.TryGetValue(targetId, out ActiveEvent planned) || planned.DefenseReaction ||
                planned.Scouted || planned.AttackObserved || planned.BattleStarted ||
                planned.DefenderRealm != reporterRealm || planned.AttackerRealm != sightedRealm)
                return false;
            // the same O9 flag the siege layer's own scout sighting sets (ReportScoutedForce): the rally may start, the
            // attack itself is not marked as seen, and rvr_scout_reports_rally switches both off
            planned.Scouted = true;
            return true;
        }
    }
}
