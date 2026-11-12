using System.Collections.Generic;
using System.Numerics;
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

        // A cleared name clears every monster of that name; a boss's name is cleared only by a boss kill.
        [TestCase(false, false, true, false, true)]   // Oro's clone after Oro (or another clone) died
        [TestCase(true, false, true, false, false)]   // Oro himself after only a clone died: still a target
        [TestCase(true, false, true, true, true)]     // a boss of that name died: its respawn is not fought again
        [TestCase(true, true, true, true, false)]     // final encounters are never cleared by name
        [TestCase(false, false, false, false, false)] // nothing of that name died yet
        public void ClearedNameCoversEveryMonsterOfThatName(bool boss, bool final, bool nameKilled, bool bossKilled, bool cleared) =>
            Assert.That(RealmRaidDungeonRoute.NameCleared(boss, final, nameKilled, bossKilled), Is.EqualTo(cleared));

        // Epic dungeons: one of each name for the first hour, then scripted bosses, finals and boss adds only.
        [TestCase(true, false, false, false, true)]
        [TestCase(false, false, false, false, false)]  // epic-class trash after the first hour
        [TestCase(false, true, false, false, true)]
        [TestCase(false, false, true, false, true)]
        [TestCase(false, false, false, true, true)]
        public void EpicDungeonTargetsAreBossesAfterTheFirstHour(bool firstHour, bool boss, bool final, bool add, bool target) =>
            Assert.That(RealmRaidDungeonRoute.IsEpicDungeonTarget(firstHour, boss, final, add), Is.EqualTo(target));

        // The muster point stands beyond the encounter's own reach; posts keep out of its aggro range plus 300.
        [TestCase(600, 1_400f, 900f)]     // Govannon and the summoners
        [TestCase(850, 1_450f, 1_150f)]   // Legion
        [TestCase(1_300, 1_900f, 1_600f)] // Apocalypse
        [TestCase(1_500, 2_100f, 1_800f)] // Olcasgean
        [TestCase(0, 1_400f, 300f)]       // no aggro range (Uaimh Lairmaster)
        public void MusterStandsOutsideTheEncounterReach(int aggro, float standOff, float keepOut)
        {
            Assert.That(RealmRaidDungeonRoute.MusterStandOff(aggro), Is.EqualTo(standOff));
            Assert.That(RealmRaidDungeonRoute.MusterKeepOutRadius(aggro), Is.EqualTo(keepOut));
        }

        // The re-muster after a wipe asks for half of the roster, not half of whoever is alive at that moment.
        [TestCase(300, 0, 150)]
        [TestCase(60, 0, 30)]     // 60 in the roster: 30, even if only 10 are alive just after the wipe
        [TestCase(300, 20 * 60_000, 99)]
        public void ReMusterIsBasedOnTheRoster(int roster, long mustering, int needed) =>
            Assert.That(RealmRaidDungeonRoute.FinalMusterNeeded(roster, mustering), Is.EqualTo(needed));

        // A boss that was fought and has been out of combat for two pulses is a wiped attempt.
        [TestCase(true, 100_000, 110_000, true)]
        [TestCase(true, 100_000, 105_000, false)]  // one pulse out of combat: not yet
        [TestCase(true, 0, 110_000, false)]        // still in combat
        [TestCase(false, 100_000, 200_000, false)] // never fought since the muster: the raid is still crossing
        public void AWipeIsTheBossBackOutOfCombatAfterAFight(bool sawCombat, long outSince, long now, bool wiped) =>
            Assert.That(RealmRaidDungeonRoute.WipeConfirmed(sawCombat, outSince, now), Is.EqualTo(wiped));

        // Every named encounter gets a muster, not only the final one.
        [TestCase(true, false, false, true)]
        [TestCase(false, true, false, true)]    // a scripted boss in an epic dungeon
        [TestCase(false, false, true, true)]    // a Darkness Falls lord or a summoner
        [TestCase(false, false, false, false)]  // trash and adds are fought as they come
        public void EveryNamedEncounterIsMustered(bool final, bool boss, bool objective, bool muster) =>
            Assert.That(RealmRaidDungeonRoute.NeedsMuster(final, boss, objective), Is.EqualTo(muster));

        // A lesser encounter waits for a quarter of the roster (8-60), an eighth after 5 minutes, nobody after 15.
        [TestCase(300, 0, 60)]
        [TestCase(200, 0, 50)]
        [TestCase(20, 0, 8)]
        [TestCase(200, 5 * 60_000, 25)]
        [TestCase(300, 15 * 60_000, 0)]
        public void LesserEncounterMusterIsSmaller(int roster, long mustering, int needed) =>
            Assert.That(RealmRaidDungeonRoute.EncounterMusterNeeded(roster, mustering), Is.EqualTo(needed));

        // A lesser encounter wiped on three times is set aside; a final one never is.
        [TestCase(false, 2, false)]
        [TestCase(false, 3, true)]
        [TestCase(true, 9, false)]
        public void LesserEncounterIsSetAsideAfterThreeWipes(bool final, int wipes, bool setAside) =>
            Assert.That(RealmRaidDungeonRoute.WipedOut(final, wipes), Is.EqualTo(setAside));

        // The raid goes on to the final trigger once every remaining target is on a retry timer.
        [TestCase(new long[] { 2_000, 5_000 }, 1_000, true)]
        [TestCase(new long[] { 2_000, 0 }, 1_000, false)]      // one not probed yet
        [TestCase(new long[] { 2_000, 1_000 }, 1_000, false)]  // one whose retry has just run out
        [TestCase(new long[0], 1_000, true)]                   // nothing left
        public void EverySetAsideTargetSendsTheRaidToTheFinal(long[] retryUntil, long now, bool setAside) =>
            Assert.That(RealmRaidDungeonRoute.EverySetAside(retryUntil, now), Is.EqualTo(setAside));

        // Darkness Falls is stacked: an approach on the encounter's floor is tried before a nearer one on another floor.
        [Test]
        public void ChainApproachesOnTheEncountersFloorComeFirst()
        {
            Vector3 encounter = new(30000, 30000, 15000);
            Vector3 sameFloor = new(35700, 30000, 15050);     // 5,700 across, same floor
            Vector3 floorAbove = new(31500, 30000, 17500);    // 2,900 in 3D, a floor above
            Assert.That(Vector3.Distance(floorAbove, encounter), Is.LessThan(Vector3.Distance(sameFloor, encounter)));
            Assert.That(RealmRaidDungeonRoute.ApproachSearchKey(sameFloor, encounter),
                Is.LessThan(RealmRaidDungeonRoute.ApproachSearchKey(floorAbove, encounter)));
        }

        [Test]
        public void MusterPostsNeverLandInsideTheKeepOutCircle()
        {
            var nav = new UT_RealmRaidFormation.Mesh();
            var occupied = new List<Vector3>();
            Vector3 center = new(20000, 20000, 1000);
            // Legion 1,450 from the muster point, keep-out 1,150: a post spread of up to 930 would reach 520 from him.
            Vector3 boss = center + new Vector3(1450, 0, 0);
            int placed = 0;
            for (int slot = 0; slot < RealmRaidRecruitmentPolicy.MaximumParties; slot++)
            {
                if (!RealmRaidFormation.TryResolve(nav, UT_RealmRaidFormation.Zone(), center, slot, occupied, (boss, 1150f), out Vector3 post))
                    continue;
                Assert.That(Vector3.Distance(post, boss), Is.GreaterThanOrEqualTo(1150f));
                occupied.Add(post);
                placed++;
            }
            Assert.That(placed, Is.GreaterThan(RealmRaidRecruitmentPolicy.MaximumParties / 2));
            Assert.That(RealmRaidFormation.OutsideKeepOut(boss + new Vector3(0, 1149, 0), boss, 1150f), Is.False);
            Assert.That(RealmRaidFormation.OutsideKeepOut(boss, boss, 0), Is.True);
        }

        [Test]
        public void TheRouteRegistersTheKeepOutForItsOwnMusterPointOnly()
        {
            var nav = new UT_RealmRaidFormation.Mesh();
            Vector3 center = new(30000, 30000, 1000), boss = center + new Vector3(200, 0, 0);
            RealmRaidFormation.SetKeepOut(center, boss, 5000f);
            try
            {
                Assert.That(RealmRaidFormation.TryResolve(nav, UT_RealmRaidFormation.Zone(), center, 0, [], out _), Is.False);
                Assert.That(RealmRaidFormation.TryResolve(nav, UT_RealmRaidFormation.Zone(), center + new Vector3(1, 0, 0), 0, [], out _), Is.True);
            }
            finally { RealmRaidFormation.ClearKeepOut(center); }
            Assert.That(RealmRaidFormation.TryResolve(nav, UT_RealmRaidFormation.Zone(), center, 0, [], out _), Is.True);
        }
    }
}
