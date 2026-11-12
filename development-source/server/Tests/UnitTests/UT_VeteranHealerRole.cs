using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q7: a Cleric, Druid or Healer companion set to a fighting Role was still left out
    /// when the player started a fight (the assist and pull checks only asked "is it a support class?").
    /// </summary>
    [TestFixture]
    public class UT_VeteranHealerRole
    {
        [Test]
        public void FightingRolesJoinTheFight() => Assert.Multiple(() =>
        {
            foreach (eCharacterClass healer in new[] { eCharacterClass.Cleric, eCharacterClass.Druid, eCharacterClass.Healer })
            {
                Assert.That(CompanionOptions165.HealerRoleFights(healer, 0), Is.False, $"{healer} Automatic heals and buffs");
                Assert.That(CompanionOptions165.HealerRoleFights(healer, 1), Is.True, $"{healer} heal first, then attack");
                Assert.That(CompanionOptions165.HealerRoleFights(healer, 2), Is.True, $"{healer} Attack");
                Assert.That(CompanionOptions165.HealerRoleFights(healer, 3), Is.True, $"{healer} Melee and heal");
            }
            Assert.That(CompanionOptions165.HealerRoleFights(eCharacterClass.Shaman, 2), Is.False, "not a support class row");
            Assert.That(CompanionOptions165.HealerRoleFights(eCharacterClass.Cleric, 7), Is.False, "unknown choice");
        });
    }
}
