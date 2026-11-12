using System;

namespace DOL.GS
{
    /// <summary>
    /// What a raid boss tells the bots about itself (realm-event fixes 2026-11-12). A boss script states the rules its
    /// TakeDamage already enforces, so a gamebot can stand and fight the way a veteran player does instead of hitting an
    /// immune boss. Every member has a default: a boss class carries the marker and states only what applies to it.
    /// Nothing here changes a boss: the numbers are the ones its script already uses.
    /// A bot reads these once when its target changes (BotBrain.RaidBoss), never per tick.
    /// </summary>
    public interface IRaidBossTraits
    {
        /// <summary>The greatest distance an attacker may stand from the boss and still damage it; 0 for no limit
        /// (Legion: 1,400).</summary>
        int RaidMaxDamageRange => 0;
    }

    /// <summary>Pure rules for <see cref="IRaidBossTraits"/>, unit tested.</summary>
    public static class RaidBossTraits
    {
        /// <summary>How far inside a damage range limit a ranged bot stands (a step of the boss or a lagging follow
        /// must not carry it back out).</summary>
        public const int DamageRangeMargin = 100;

        /// <summary>
        /// The range a ranged bot stands at against a boss: its own spell or bow range, but never beyond the boss's
        /// damage range limit less the margin. Unchanged when either is unknown (0).
        /// </summary>
        public static int CapStandOff(int ownRange, int maxDamageRange) =>
            ownRange <= 0 || maxDamageRange <= 0 ? ownRange : Math.Min(ownRange, Math.Max(1, maxDamageRange - DamageRangeMargin));

        /// <summary>True when an attacker this far from the boss does it no damage.</summary>
        public static bool BeyondDamageRange(int maxDamageRange, double distance) =>
            maxDamageRange > 0 && distance > maxDamageRange - DamageRangeMargin / 2;
    }
}
