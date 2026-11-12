using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Veteran review 2026-11-11, Q14: damage adds and weapon procs go on classes that melee only.</summary>
    [TestFixture]
    public sealed class UT_VeteranMeleeBuffs
    {
        private static Spell Buff(string type) => new(new DbSpell { Type = type, Target = "Realm", Duration = 1200, Value = 10 }, 1);

        [Test]
        public void DamageAddsAndProcsSkipCastersAndHealers() => Assert.Multiple(() =>
        {
            foreach (string type in new[] { "DamageAdd", "OffensiveProc" })
            {
                Spell buff = Buff(type);
                Assert.That(BotBrain.BuffHelps(new ClassRunemaster(), buff, true), Is.False, $"{type} Runemaster");
                Assert.That(BotBrain.BuffHelps(new ClassSpiritmaster(), buff, true), Is.False, $"{type} Spiritmaster");
                Assert.That(BotBrain.BuffHelps(new ClassHealer(), buff, true), Is.False, $"{type} Healer");
                Assert.That(BotBrain.BuffHelps(new ClassWarrior(), buff, true), Is.True, $"{type} Warrior");
                Assert.That(BotBrain.BuffHelps(new ClassBerserker(), buff, true), Is.True, $"{type} Berserker");
                Assert.That(BotBrain.BuffHelps(new ClassShaman(), buff, true), Is.True, $"{type} Shaman melees");
            }
            Assert.That(BotBrain.BuffHelps(new ClassRunemaster(), Buff("DefensiveProc"), true), Is.True, "defensive procs unchanged");
        });
    }
}
