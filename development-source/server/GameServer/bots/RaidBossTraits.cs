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

        /// <summary>Radius of the circle round <see cref="RaidLairCentre"/> outside which the boss takes no damage; 0 for
        /// none (the three dragons: 2,000 round their lair).</summary>
        int RaidLairRadius => 0;

        /// <summary>The centre of the lair circle (null when there is none).</summary>
        Point3D RaidLairCentre => null;
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

        public enum LairAction { Hold, Lead, Return }

        /// <summary>
        /// What a bot targeting a boss that has left its lair circle does: <see cref="LairAction.Lead"/> when the boss is
        /// after this bot (it walks home and the boss follows), <see cref="LairAction.Return"/> when the bot itself stands
        /// outside the circle (it walks back inside), else <see cref="LairAction.Hold"/> (it stops hitting and waits).
        /// </summary>
        public static LairAction OutsideLairAction(bool bossChasesBot, bool botOutsideCircle) =>
            bossChasesBot ? LairAction.Lead : botOutsideCircle ? LairAction.Return : LairAction.Hold;

        /// <summary>How far inside the lair circle a bot that stood outside it walks back to.</summary>
        public const int LairReturnMargin = 300;

        /// <summary>
        /// A point on the line from the circle's centre (<paramref name="cx"/>, <paramref name="cy"/>) towards the bot, at
        /// <paramref name="radius"/> less <paramref name="margin"/>: a bot outside the circle walks just back inside it.
        /// A bot already that close keeps its spot.
        /// </summary>
        public static (int X, int Y) PointInsideCircle(int cx, int cy, int x, int y, int radius, int margin)
        {
            double dx = x - cx, dy = y - cy;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double reach = Math.Max(0, radius - margin);
            if (length <= reach || length < 1)
                return (x, y);
            return ((int)Math.Round(cx + dx / length * reach), (int)Math.Round(cy + dy / length * reach));
        }
    }
}
