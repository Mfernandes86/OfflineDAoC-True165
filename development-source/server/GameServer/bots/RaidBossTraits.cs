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

        /// <summary>The boss strips bladeturn from its target and turns the stripped bubbles against the raid (Legion's
        /// soul consumption blast after five), so its attackers keep no bladeturn up.</summary>
        bool RaidConsumesBladeturn => false;
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

        /// <summary>
        /// df-bosses L3: a bot casts, pulses and maintains no bladeturn while its own target, its party's kill target or
        /// any target of its party's fight is a boss that consumes bladeturn.
        /// </summary>
        public static bool BladeturnFeedsBoss(bool ownTargetConsumes, bool killTargetConsumes, bool partyTargetConsumes) =>
            ownTargetConsumes || killTargetConsumes || partyTargetConsumes;

        /// <summary>
        /// epic-bosses L1: a foe that turned untargetable stays fought by a bot that already had it on its aggro list,
        /// when it is a scripted boss (a player keeps a target that turns untargetable; a new one cannot be picked).
        /// </summary>
        public static bool KeepsFoeThatTurnedUntargetable(bool alreadyOnAggroList, bool scriptedBoss) =>
            alreadyOnAggroList && scriptedBoss;

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
        // ---- epic-bosses L2, sh-bosses: stepping out of a boss's area spell

        /// <summary>Where a boss's area spell lands.</summary>
        public enum AreaCentre { None, Ground, CastTarget, Caster }

        /// <summary>Pure: the centre of a harmful spell with a radius: its ground target for a ranged area spell, the
        /// caster for a point blank one, the cast target for a targeted spell with a radius (a damage over time "around
        /// his target"); none without a radius.</summary>
        public static AreaCentre CentreOf(eSpellTarget target, int range, int radius) =>
            radius <= 0 ? AreaCentre.None : target switch
            {
                eSpellTarget.AREA => range > 0 ? AreaCentre.Ground : AreaCentre.Caster,
                eSpellTarget.ENEMY => AreaCentre.CastTarget,
                eSpellTarget.SELF or eSpellTarget.GROUP or eSpellTarget.REALM => AreaCentre.Caster,
                _ => AreaCentre.None,
            };

        /// <summary>How far outside the radius a bot steps.</summary>
        public const int AreaStepMargin = 150;

        /// <summary>A tank holding the boss leaves the area only under this much health (moving drags the boss along).</summary>
        public const int TankLeavesAreaHealthPercent = 50;

        /// <summary>
        /// Pure: a bot inside a boss's area spell steps out when it can keep fighting from outside: ranged bots and
        /// healers always; the tank holding the boss by its health; a melee bot when the area is not on the boss (it
        /// returns after the cast) or it cannot reach the boss anyway (Apocalypse in the air). The cast's own target of
        /// a targeted spell stays (the spell lands on it wherever it goes).
        /// </summary>
        public static bool StepsOutOfArea(bool inside, bool castTargetOfTargetedSpell, bool rangedOrHealer, bool holdsBoss,
            int healthPercent, bool centredOnBoss, bool canHitBossNow)
        {
            if (!inside || castTargetOfTargetedSpell)
                return false;
            if (holdsBoss)
                return healthPercent < TankLeavesAreaHealthPercent;
            return rangedOrHealer || !centredOnBoss || !canHitBossNow;
        }

        /// <summary>
        /// Pure: a melee bot that stepped out waits outside, rather than walk back in to the boss, while the area lasts
        /// and the boss stands inside it.
        /// </summary>
        public static bool WaitsOutsideArea(bool areaActive, bool inside, bool rangedOrHealer, bool holdsBoss, bool bossInArea) =>
            areaActive && !inside && !rangedOrHealer && !holdsBoss && bossInArea;

        /// <summary>
        /// The point just outside a circle on the bot's side of it (straight out from the centre through the bot); a bot
        /// standing on the centre steps out on the side away from <paramref name="awayX"/>, <paramref name="awayY"/>, or
        /// to the east when that is the centre too.
        /// </summary>
        public static (int X, int Y) PointOutsideCircle(int cx, int cy, int x, int y, int radius, int margin, int awayX, int awayY)
        {
            double dx = x - cx, dy = y - cy;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1)
            {
                dx = cx - awayX; dy = cy - awayY;
                length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1)
                {
                    dx = 1; dy = 0; length = 1;
                }
            }
            double reach = radius + margin;
            return ((int)Math.Round(cx + dx / length * reach), (int)Math.Round(cy + dy / length * reach));
        }
    }
}
