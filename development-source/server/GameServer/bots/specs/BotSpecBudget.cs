using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// The spec points a plan needs at level 50 against the points a class earns by 50 (O11, owner 2026-11-11).
    /// Thirteen-plus period templates asked for more than level 50 gives, so their lowest-ratio line was starved
    /// (an Air Theurgist's Cold 20 ended near 9). The templates are trimmed by hand; <see cref="Fit"/> re-plans a saved
    /// plan from before the fix the same way: points a bot already spent stay, only the caps of future points change.
    /// </summary>
    public static class BotSpecBudget
    {
        /// <summary>Points a line costs from 1 to <paramref name="level"/> (a line starts at 1).</summary>
        public static int LineCost(int level)
        {
            level = Math.Clamp(level, 1, 50);
            return level * (level + 1) / 2 - 1;
        }

        /// <summary>Every point a class earns from level 2 to 50, half levels included (BotSpecTrainer.PointsForLevel).</summary>
        public static int PointsAtFifty(int multiplier)
        {
            int points = 0;
            for (int level = 2; level <= 50; level++)
                points += BotSpecTrainer.PointsForLevel(multiplier, level);
            return points;
        }

        /// <summary>
        /// A ratio-0 line capped at 50 is a plan's "every point left at 50" line (the Bonedancer's secondary): it takes
        /// what is left by design and never counts against the budget.
        /// </summary>
        public static bool IsRestOfPointsLine(BotSpecLine line) => line.levelRatio <= 0 && line.SpecCap >= 50;

        /// <summary>Points the plan's caps cost at 50; a line listed twice counts once at its higher cap.</summary>
        public static int CostAtFifty(IEnumerable<BotSpecLine> lines) =>
            lines.Where(line => !IsRestOfPointsLine(line)).GroupBy(line => line.Spec)
                .Sum(group => LineCost((int)group.Max(line => line.SpecCap)));

        public static bool Fits(BotSpec plan, int multiplier) =>
            plan?.SpecLines == null || CostAtFifty(plan.SpecLines) <= PointsAtFifty(multiplier);

        /// <summary>
        /// Lowers the caps of an over-budget plan until it fits: always the lowest-ratio line first (the last one listed
        /// on a tie), the "rest of the points" line a template fills last. Returns true when a cap changed.
        /// </summary>
        public static bool Fit(BotSpec plan, int multiplier)
        {
            if (plan?.SpecLines == null || plan.SpecLines.Count == 0 || multiplier <= 0)
                return false;
            int budget = PointsAtFifty(multiplier);
            bool changed = false;
            while (CostAtFifty(plan.SpecLines) > budget)
            {
                int pick = -1;
                for (int i = 0; i < plan.SpecLines.Count; i++)
                {
                    BotSpecLine line = plan.SpecLines[i];
                    if (line.SpecCap <= 1 || IsRestOfPointsLine(line))
                        continue;
                    if (pick < 0 || line.levelRatio <= plan.SpecLines[pick].levelRatio)
                        pick = i;
                }
                if (pick < 0)
                    break;
                BotSpecLine trimmed = plan.SpecLines[pick];
                // a duplicated spec counts at its highest cap: lower every copy above the new cap together
                uint newCap = Math.Min(trimmed.SpecCap, 50) - 1;
                for (int i = 0; i < plan.SpecLines.Count; i++)
                {
                    BotSpecLine line = plan.SpecLines[i];
                    if (line.Spec == trimmed.Spec && line.SpecCap > newCap)
                        plan.SpecLines[i] = new BotSpecLine(line.Spec, newCap, line.levelRatio);
                }
                changed = true;
            }
            return changed;
        }
    }
}
