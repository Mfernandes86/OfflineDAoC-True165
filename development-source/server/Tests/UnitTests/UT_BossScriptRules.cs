using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests
{
    /// <summary>Pure rules behind the realm-event boss script fixes (bosses lane, 2026-11-12).</summary>
    [TestFixture]
    public class UT_BossScriptRules
    {
        [TestCase(false, true, true, true, TestName = "A living pet is devoured")]
        [TestCase(true, true, true, false, TestName = "A gamebot is never devoured although its brain is a controlled brain")]
        [TestCase(false, false, true, false, TestName = "An ordinary monster is not a pet")]
        [TestCase(false, true, false, false, TestName = "A dead pet is not devoured again")]
        public void PrincesDevourPetsOnly(bool isGameBot, bool hasControlledBrain, bool isAlive, bool expected)
        {
            Assert.That(EpicEncounterMechanics.IsDevourablePet(isGameBot, hasControlledBrain, isAlive), Is.EqualTo(expected));
        }

        [TestCase(true, false, false, true, false, true, TestName = "A monster of the boss's realm answers his call")]
        [TestCase(true, true, false, true, false, false, TestName = "A gamebot in the room is not ordered to attack")]
        [TestCase(true, false, true, true, false, false, TestName = "A pet in the room is not ordered to attack")]
        [TestCase(false, false, false, true, false, false, TestName = "An NPC of another realm is not ordered to attack")]
        [TestCase(true, false, false, false, false, false, TestName = "A corpse is not ordered to attack")]
        [TestCase(true, false, false, true, true, false, TestName = "A peaceful NPC is not ordered to attack")]
        public void BossCallReachesItsOwnKindOnly(bool sameRealm, bool isGameBot, bool controlled, bool alive, bool peaceful, bool expected)
        {
            Assert.That(EpicEncounterMechanics.IsOwnKind(sameRealm, isGameBot, controlled, alive, peaceful), Is.EqualTo(expected));
        }

        [Test]
        public void SharedHealthBodiesPayOnceWithEveryAttackerCredited()
        {
            var copy = new System.Collections.Generic.Dictionary<string, double> { ["tank"] = 300, ["caster"] = 50 };
            var main = new System.Collections.Generic.Dictionary<string, double> { ["tank"] = 100, ["archer"] = 20 };
            EpicEncounterMechanics.MergeDamageCredit(copy, main);
            Assert.That(main["tank"], Is.EqualTo(400), "Damage to both bodies adds up for the same attacker");
            Assert.That(main["caster"], Is.EqualTo(50), "An attacker of the other body only is credited too");
            Assert.That(main["archer"], Is.EqualTo(20));
            Assert.That(main.Count, Is.EqualTo(3));
        }

        private const long Out = AutonomousTetherReset.OutOfTetherLeashMilliseconds;
        private const long Stranded = AutonomousTetherReset.StrandedLeashMilliseconds;

        [Test]
        public void ASecondTripOutsideTheTetherStartsItsOwnClock()
        {
            var clock = new TetherExcursionClock<string>();
            Assert.That(clock.DueForLeash("dragon", 0, engaged: true), Is.False);
            Assert.That(clock.DueForLeash("dragon", Out - 1_000, engaged: true), Is.False, "Fighting in reach, not yet out long enough");
            clock.BackInside("dragon");
            Assert.That(clock.IsTracking("dragon"), Is.False);
            // Out again 100 s after the first trip began: before the fix the old clock still read 100 s and leashed at once.
            Assert.That(clock.DueForLeash("dragon", 100_000, engaged: true), Is.False);
            Assert.That(clock.DueForLeash("dragon", 100_000 + Out - 1, engaged: true), Is.False);
            Assert.That(clock.DueForLeash("dragon", 100_000 + Out, engaged: true), Is.True, "A long trip is still leashed");
            Assert.That(clock.IsTracking("dragon"), Is.False, "A leash forgets both clocks");
        }

        [Test]
        public void AStrandedBossIsLeashedAfterThirtySecondsAndFightingResetsThatClock()
        {
            var clock = new TetherExcursionClock<string>();
            Assert.That(clock.DueForLeash("aidon", 0, engaged: false), Is.False);
            Assert.That(clock.DueForLeash("aidon", Stranded - 1, engaged: false), Is.False);
            Assert.That(clock.DueForLeash("aidon", Stranded - 1, engaged: true), Is.False, "Someone in reach again");
            Assert.That(clock.DueForLeash("aidon", Stranded, engaged: false), Is.False, "The stranded clock restarted");
            Assert.That(clock.DueForLeash("aidon", 2 * Stranded, engaged: false), Is.True);
        }

        [Test]
        public void BackInsideOnAnUntrackedBossIsHarmless()
        {
            var clock = new TetherExcursionClock<string>();
            clock.BackInside("nobody");
            Assert.That(clock.IsTracking("nobody"), Is.False);
        }
    }
}
