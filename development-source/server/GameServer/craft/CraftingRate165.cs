using System;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// The launcher's Crafting skill rate option (owner 2026-11-12: "when one point is gained multiple ones are"): the
    /// server property crafting_skill_rate, default 1x. When a player's craft wins a skill gain (the normal chance, so a
    /// grey recipe still gives nothing), each skill the gain raises rises by the rate instead of by one, never past the
    /// cap that skill already has (secondary skills stop at 1300; the main trades have none). Whole points only: a rate
    /// below 1 or not a number counts as the original 1x, as XpRates165.Sanitize does for the XP rates.
    /// Players only (gamebots are not GamePlayers and keep their own crafting).
    /// </summary>
    public static class CraftingRate165
    {
        /// <summary>No cap (the main trades).</summary>
        public const int NoCap = int.MaxValue;
        /// <summary>A hand-edited huge value is held here (the launcher offers 1 to 10).</summary>
        public const int MaxRate = 100;

        /// <summary>A stored rate as whole points per gain: not a number, infinite or below 1 counts as 1; fractions drop.</summary>
        public static int Sanitize(double rate) =>
            double.IsFinite(rate) && rate >= 1 ? (int)Math.Min(MaxRate, Math.Floor(rate)) : 1;

        /// <summary>The player's points per skill gain now (the launcher's choice, read at server start).</summary>
        public static int PlayerRate => Sanitize(ServerProperties.Properties.CRAFTING_SKILL_RATE);

        /// <summary>
        /// The points one skill gain adds to a skill at <paramref name="current"/>: the rate, but never past
        /// <paramref name="cap"/> (0 when the skill is already at or above it).
        /// </summary>
        public static int Points(int current, double rate, int cap = NoCap)
        {
            if (current >= cap)
                return 0;
            long room = (long)cap - current;
            return (int)Math.Min(Sanitize(rate), room);
        }

        /// <summary>
        /// The crafting title levels (each 100 up to 1000, GamePlayer.HasPlayerReachedNewCraftingTitle) a gain from
        /// <paramref name="before"/> to <paramref name="after"/> reached, lowest first. A one-point gain gives what the
        /// old "new value is a multiple of 100" check gave; a bigger gain no longer steps over a title.
        /// </summary>
        public static List<int> TitlesReached(int before, int after)
        {
            var titles = new List<int>();
            int top = Math.Min(after, 1000);
            if (top <= before)
                return titles;
            int first = before < 0 ? 0 : (before / 100 + 1) * 100;
            for (int level = first; level <= top; level += 100)
                titles.Add(level);
            return titles;
        }
    }
}
