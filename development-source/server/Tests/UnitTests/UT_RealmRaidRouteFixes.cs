using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Realm raid route inside the dungeon: the pure rules behind the 2026-11-12 audit fixes.</summary>
    public class UT_RealmRaidRouteFixes
    {
        // A reset boss is measured from its new health; a boss in a fight keeps its low.
        [TestCase(28_000, 100_000, false, 100_000)]  // reset after a wipe: the mark is full health again
        [TestCase(28_000, 30_000, true, 28_000)]     // healing in the fight is not a reset
        [TestCase(28_000, 20_000, false, 28_000)]    // below the mark: progress is counted by the caller
        public void ResetTargetIsMeasuredFromItsNewHealth(int lowest, int health, bool inCombat, int mark) =>
            Assert.That(RealmRaidDungeonRoute.ProgressMark(lowest, health, inCombat), Is.EqualTo(mark));

        // The absence allowance runs from the last time 8 raiders stood by the target, not from the target's start.
        [TestCase(3, 25 * 60_000, 0, 22 * 60_000, false, true)]   // wiped 3 minutes ago, 25 minutes into the target
        [TestCase(3, 25 * 60_000, 0, 0, false, false)]            // never reached in 25 minutes: the clock runs
        [TestCase(3, 19 * 60_000, 0, 0, false, true)]             // never reached yet, still inside 20 minutes
        [TestCase(3, 50 * 60_000, 0, 22 * 60_000, true, true)]    // still closing in
        [TestCase(8, 1_000, 0, 0, false, false)]                  // the raid is there: the clock runs
        public void AbsenceAllowanceRunsFromTheLastPresence(int near, long now, long start, long present, bool approaching, bool paused) =>
            Assert.That(RealmRaidDungeonRoute.AbsencePausesClock(near, now, start, present, approaching), Is.EqualTo(paused));
    }
}
