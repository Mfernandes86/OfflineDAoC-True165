using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Q30 (veteran review 2026-11-11): a Necromancer's servant commands, best damage level and power drains are built
    /// once per spell list and level, not on every command tick.
    /// </summary>
    [TestFixture]
    public class UT_ServantCommandTable
    {
        private static Spell Command(int id, int level, int payloadId) =>
            new(new DbSpell { SpellID = id, Name = "command", Type = "PetSpell", Target = "Enemy", SubSpellID = payloadId }, level);

        private static Spell Payload(int id, string type) =>
            new(new DbSpell { SpellID = id, Name = "payload", Type = type, Target = "Enemy", Damage = 50 }, 1);

        [Test]
        public void TheTableHoldsPayloadsTheBestDamageLevelAndTheDrains()
        {
            var payloads = new Dictionary<int, Spell>
            {
                [9001] = Payload(9001, "DirectDamage"),
                [9002] = Payload(9002, "PowerDrainPet"),
                [9003] = Payload(9003, "DirectDamage"),
            };
            Spell low = Command(1, 5, 9001), drain = Command(2, 12, 9002), high = Command(3, 40, 9003), missing = Command(4, 8, 9999);
            var spells = new List<Spell> { low, drain, high, missing };
            var table = new BotBrain.ServantCommandTable(spells, 30, id => payloads.GetValueOrDefault(id));
            Assert.Multiple(() =>
            {
                Assert.That(table.PayloadById.Keys, Is.EquivalentTo(new[] { 1, 2 }), "above its level, or no payload: not a command");
                Assert.That(table.BestDamageLevel, Is.EqualTo(12), "the best harmful command at level 30");
                Assert.That(table.Drains, Is.EqualTo(new[] { drain }));
                Assert.That(table.DrainIds, Is.EquivalentTo(new[] { 2 }));
                Assert.That(table.Matches(spells, 30), Is.True, "no rebuild on the next command tick");
                Assert.That(table.Matches(spells, 40), Is.False, "a level up rebuilds it");
                Assert.That(new BotBrain.ServantCommandTable(spells, 40, id => payloads.GetValueOrDefault(id)).BestDamageLevel, Is.EqualTo(40));
            });
        }
    }
}
