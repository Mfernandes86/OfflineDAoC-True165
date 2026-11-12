using DOL.Database;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, M1: the style-chain table (built once per style list and level, in place of comparing every
    /// style with every other on each swing) and the endurance reserve for grouped melee.
    /// </summary>
    [TestFixture]
    public class UT_VeteranStyleChains
    {
        private static Style St(int id, double growth, int level = 10, int endurance = 10, int opener = 0)
            => new(new DbStyle
            {
                Name = "style" + id, ID = id, SpecKeyName = Specs.Sword, SpecLevelRequirement = level, WeaponTypeRequirement = 11,
                OpeningRequirementType = opener == 0 ? 0 : (int)Style.eOpening.Offensive, OpeningRequirementValue = opener,
                GrowthRate = growth, EnduranceCost = endurance,
            }, null);

        [Test]
        public void StyleChainTableKeepsTheBestTrainedFollowUps()
        {
            Style opener = St(1, .3);
            Style follow = St(2, 1.1, level: 20, opener: 1);
            Style tauntFollow = St(3, 1.4, level: 20, opener: 1);
            tauntFollow.Procs.Add(new StyleProcInfo(new Spell(new DbSpell { SpellID = 20100, Type = "StyleTaunt",
                Target = "Enemy", Value = 17 }, 1), 0, 100));
            Style untrained = St(4, 2.0, level: 45, opener: 1);
            Style lonely = St(5, .8);
            var table = BotMeleeStylePolicy.BuildChains(new[] { opener, follow, tauntFollow, untrained, lonely }, 30);
            Assert.Multiple(() =>
            {
                Assert.That(table.ContainsKey(1), Is.True);
                Assert.That(table[1].Any, Is.EqualTo(1.4), "the taunt follow-up when taunting is allowed");
                Assert.That(table[1].NoTaunt, Is.EqualTo(1.1));
                Assert.That(table.ContainsKey(5), Is.False, "no follow-up known");
                Assert.That(BotMeleeStylePolicy.BuildChains(new[] { opener, untrained }, 30), Is.Empty,
                    "a follow-up above the level does not count");
                Assert.That(BotMeleeStylePolicy.BuildChains(null, 50), Is.Empty);
            });
        }

        [Test]
        public void GroupedMeleeKeepsAnEnduranceReserve()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotMeleeStylePolicy.EnduranceReserveApplies(true, 29, 80), Is.True);
                Assert.That(BotMeleeStylePolicy.EnduranceReserveApplies(true, 30, 80), Is.False);
                Assert.That(BotMeleeStylePolicy.EnduranceReserveApplies(false, 5, 80), Is.False, "solo spends freely (Armsman solo)");
                Assert.That(BotMeleeStylePolicy.EnduranceReserveApplies(true, 10, 24), Is.False, "finish a nearly dead target");
                Assert.That(BotMeleeStylePolicy.ReserveSpendsOn(true, false, true), Is.True, "a wanted stun (peel, interrupt)");
                Assert.That(BotMeleeStylePolicy.ReserveSpendsOn(false, true, false), Is.True, "a taunt the tank needs");
                Assert.That(BotMeleeStylePolicy.ReserveSpendsOn(false, true, true), Is.False, "already on the tank");
                Assert.That(BotMeleeStylePolicy.ReserveSpendsOn(false, false, false), Is.False, "plain damage waits");
                Assert.That(BotMeleeStylePolicy.EnduranceReservePlainSwingPercent,
                    Is.LessThan(BotMeleeStylePolicy.EnduranceReservePercent));
            });
        }
    }
}
