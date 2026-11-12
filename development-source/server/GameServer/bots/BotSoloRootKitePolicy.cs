using System;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// O3 (owner 2026-11-11, limited): solo root-kiting for the Shaman and the Runemaster, as Prima teaches (Shaman: "hold
    /// down the Root spell. Once it's rooted, cast Disease..."; Runemaster: "cast Root to stop it so you can reset your
    /// distance and resume nuking"). Solo only, never grouped; at most once every 20 s; the root on the bot's own monster
    /// while it comes at the bot, then ONE straight step back (a single walk, no route request). This is the only
    /// exception to the 2026-11-08 rule that roots never go on the bot's own monster.
    /// </summary>
    public static class BotSoloRootKitePolicy
    {
        public const long ReuseMilliseconds = 20_000;
        /// <summary>The monster is rooted no farther out than this (it is coming at the bot, or already on it).</summary>
        public const int MaxStartDistance = 700;
        /// <summary>A nearly dead monster is just finished off.</summary>
        public const int MinTargetHealthPercent = 40;
        public const int MaxStepDistance = 600;
        /// <summary>A step shorter than this gains nothing, so no root is cast for it.</summary>
        public const int MinStepDistance = 150;
        /// <summary>The step ends at least this far inside the root spell's range, so the next spell still reaches.</summary>
        public const int RangeMargin = 100;
        /// <summary>The step owns the bot's turn for at most this long.</summary>
        public const long StepHoldMilliseconds = 4_000;
        /// <summary>How long after the cast should have finished the bot still waits to see the root land.</summary>
        public const long RootLandGraceMilliseconds = 1_500;

        public static bool ClassKites(eCharacterClass characterClass) =>
            characterClass is eCharacterClass.Shaman or eCharacterClass.Runemaster;

        /// <summary>Pure: whether to open the kite with a root on the bot's own monster now.</summary>
        public static bool ShouldRoot(bool classKites, bool solo, bool monster, bool targetOnBot, double distance,
            int targetHealthPercent, bool alreadyHeld, bool snareImmune, bool dungeon, long now, long nextAllowedTick) =>
            classKites && solo && monster && targetOnBot && !dungeon && distance <= MaxStartDistance &&
            targetHealthPercent >= MinTargetHealthPercent && !alreadyHeld && !snareImmune && now >= nextAllowedTick;

        /// <summary>Pure: how far to step back (0 = no step worth taking, so no root either).</summary>
        public static int StepDistance(double distanceToTarget, int spellRange)
        {
            int room = spellRange - RangeMargin - (int)Math.Ceiling(distanceToTarget);
            int step = Math.Min(MaxStepDistance, room);
            return step < MinStepDistance ? 0 : step;
        }

        /// <summary>Pure: the point <paramref name="step"/> units straight away from the monster (false when on top of it).</summary>
        public static bool TryStepPoint(int botX, int botY, int targetX, int targetY, int step, out int x, out int y)
        {
            double dx = botX - targetX, dy = botY - targetY;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1 || step <= 0)
            {
                x = botX;
                y = botY;
                return false;
            }
            x = botX + (int)Math.Round(dx / length * step);
            y = botY + (int)Math.Round(dy / length * step);
            return true;
        }
    }
}
