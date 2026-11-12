using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>O3 (owner 2026-11-11): solo root-kiting for the Shaman and the Runemaster, limited.</summary>
    [TestFixture]
    public class UT_BotSoloRootKite
    {
        [TestCase(eCharacterClass.Shaman, true)]
        [TestCase(eCharacterClass.Runemaster, true)]
        [TestCase(eCharacterClass.Spiritmaster, false)]
        [TestCase(eCharacterClass.Healer, false)]
        [TestCase(eCharacterClass.Cabalist, false)]
        [TestCase(eCharacterClass.Druid, false)]
        public void OnlyTheShamanAndTheRunemasterKite(eCharacterClass characterClass, bool kites)
        {
            Assert.That(BotSoloRootKitePolicy.ClassKites(characterClass), Is.EqualTo(kites));
        }

        private static bool Root(bool solo = true, bool monster = true, bool onBot = true, double distance = 400,
            int health = 100, bool held = false, bool immune = false, bool dungeon = false, long now = 50_000,
            long next = 0, bool classKites = true) =>
            BotSoloRootKitePolicy.ShouldRoot(classKites, solo, monster, onBot, distance, health, held, immune, dungeon, now,
                next);

        [Test]
        public void RootsItsOwnMonsterWhileItComes()
        {
            Assert.That(Root(), Is.True);
            Assert.That(Root(distance: 150), Is.True, "already in melee");
            Assert.That(Root(distance: 700), Is.True);
        }

        [Test]
        public void NeverGrouped()
        {
            Assert.That(Root(solo: false), Is.False);
        }

        [Test]
        public void AtMostOnceEveryTwentySeconds()
        {
            Assert.That(BotSoloRootKitePolicy.ReuseMilliseconds, Is.EqualTo(20_000));
            Assert.That(Root(now: 10_000, next: 10_001), Is.False);
            Assert.That(Root(now: 10_001, next: 10_001), Is.True);
        }

        [Test]
        public void LeavesEverythingElseAlone()
        {
            Assert.That(Root(classKites: false), Is.False);
            Assert.That(Root(monster: false), Is.False, "players keep the PvP crowd control rules");
            Assert.That(Root(onBot: false), Is.False, "not coming at the bot");
            Assert.That(Root(distance: 701), Is.False, "still far off");
            Assert.That(Root(health: 39), Is.False, "nearly dead: finish it");
            Assert.That(Root(held: true), Is.False, "already mezzed, stunned or rooted");
            Assert.That(Root(immune: true), Is.False);
            Assert.That(Root(dungeon: true), Is.False, "no straight steps in a dungeon");
        }

        [TestCase(150, 1500, 600)]
        [TestCase(900, 1500, 500)]
        [TestCase(1200, 1500, 200)]
        [TestCase(1300, 1500, 0)]   // under 150 units of room: no step, so no root
        [TestCase(0, 2000, 600)]
        public void OneStepOfAtMostSixHundredUnitsInsideSpellRange(double distance, int range, int step)
        {
            Assert.That(BotSoloRootKitePolicy.StepDistance(distance, range), Is.EqualTo(step));
        }

        [Test]
        public void TheStepGoesStraightAwayFromTheMonster()
        {
            Assert.That(BotSoloRootKitePolicy.TryStepPoint(1000, 1000, 1000, 800, 600, out int x, out int y), Is.True);
            Assert.That((x, y), Is.EqualTo((1000, 1600)));
            Assert.That(BotSoloRootKitePolicy.TryStepPoint(1000, 1000, 700, 600, 500, out x, out y), Is.True);
            Assert.That((x, y), Is.EqualTo((1300, 1400)));
            Assert.That(BotSoloRootKitePolicy.TryStepPoint(1000, 1000, 1000, 1000, 600, out _, out _), Is.False,
                "standing on the monster: no direction");
            Assert.That(BotSoloRootKitePolicy.TryStepPoint(1000, 1000, 1000, 800, 0, out _, out _), Is.False);
        }
    }
}
