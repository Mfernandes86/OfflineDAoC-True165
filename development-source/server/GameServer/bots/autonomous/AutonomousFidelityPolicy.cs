using System;

namespace DOL.GS;

public enum eAutonomousFidelity
{
    Efficient,
    Standard,
    NearbyHuman,
}

/// <summary>
/// Scales decision detail by human proximity while retaining real actor combat,
/// movement and progression at every tier.
/// </summary>
public static class AutonomousFidelityPolicy
{
    public static eAutonomousFidelity Select(bool anyHumanOnline, bool sameZoneAsHuman, int nearestHumanDistance)
    {
        if (sameZoneAsHuman && nearestHumanDistance is >= 0 and <= 6000)
            return eAutonomousFidelity.NearbyHuman;
        if (sameZoneAsHuman)
            return eAutonomousFidelity.Standard;
        return eAutonomousFidelity.Efficient;
    }

    public static int IntervalMilliseconds(eAutonomousThinkMode mode, eAutonomousFidelity fidelity)
        => IntervalMilliseconds(mode, fidelity, 0);

    public static int IntervalMilliseconds(eAutonomousThinkMode mode, eAutonomousFidelity fidelity, int onlineBots)
    {
        int baseline = AutonomousAiBudget.IntervalMilliseconds(mode);
        if (mode == eAutonomousThinkMode.Combat)
            return fidelity switch
            {
                eAutonomousFidelity.NearbyHuman => baseline,
                eAutonomousFidelity.Standard => 350,
                _ => 500,
            };

        double multiplier = fidelity switch
        {
            eAutonomousFidelity.NearbyHuman => 1,
            eAutonomousFidelity.Standard => 1.6,
            _ => 2.6,
        };
        int interval = Math.Max(baseline, (int)Math.Round(baseline * multiplier));
        // At large populations, slow only background planning. Movement remains
        // continuous in NpcMovementComponent and attacks wake the brain at once.
        // Even at/above the 1,500-bot target no actor waits more than seconds.
        double pressure = Math.Clamp((onlineBots - 300) / 1200d, 0, 1);
        double populationMultiplier = mode switch
        {
            eAutonomousThinkMode.Travel => 1 + 0.80 * pressure,
            eAutonomousThinkMode.Planning => 1 + 0.60 * pressure,
            eAutonomousThinkMode.Resting => 1 + 0.40 * pressure,
            _ => 1,
        };
        int maximum = mode switch
        {
            eAutonomousThinkMode.Travel => 5_000,
            eAutonomousThinkMode.Planning => 12_000,
            eAutonomousThinkMode.Resting => 7_000,
            _ => interval,
        };
        // Veteran review M4: the curve above is flat from 1,500 bots. Past that, only bots no player can see (Efficient)
        // slow a little more, up to 6,000 bots: travel to 7 s, planning to 15 s, resting to 9 s at most. Combat, the
        // player-led cadence and anything near a player are unchanged.
        double crowd = fidelity == eAutonomousFidelity.Efficient ? CrowdPressure(onlineBots) : 0;
        if (crowd > 0)
        {
            populationMultiplier *= mode switch
            {
                eAutonomousThinkMode.Travel => 1 + 0.40 * crowd,
                eAutonomousThinkMode.Planning => 1 + 0.25 * crowd,
                eAutonomousThinkMode.Resting => 1 + 0.30 * crowd,
                _ => 1,
            };
            maximum += mode switch
            {
                eAutonomousThinkMode.Travel => (int)Math.Round(2_000 * crowd),
                eAutonomousThinkMode.Planning => (int)Math.Round(3_000 * crowd),
                eAutonomousThinkMode.Resting => (int)Math.Round(2_000 * crowd),
                _ => 0,
            };
        }
        return Math.Min(maximum, (int)Math.Round(interval * populationMultiplier));
    }

    /// <summary>Veteran review M4: 0 at 1,500 online bots or fewer, rising to 1 at 6,000.</summary>
    public static double CrowdPressure(int onlineBots) => Math.Clamp((onlineBots - 1_500) / 4_500d, 0, 1);

    public static int Stagger(int intervalMilliseconds, long stableBotId)
    {
        if (intervalMilliseconds <= 0)
            return intervalMilliseconds;
        int spread = Math.Max(1, intervalMilliseconds / 8);
        long positive = stableBotId == long.MinValue ? 0 : Math.Abs(stableBotId);
        return intervalMilliseconds + (int)(positive % spread);
    }

    public static int CandidateLimit(eAutonomousFidelity fidelity) => fidelity switch
    {
        eAutonomousFidelity.NearbyHuman => 24,
        eAutonomousFidelity.Standard => 12,
        _ => 6,
    };
}
