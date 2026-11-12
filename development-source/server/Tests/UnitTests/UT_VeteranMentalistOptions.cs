using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q8: a hired Mentalist had no "Attack spells" row, no Strict save-power choice and no
    /// Role row. It now gets the caster rows and reuses the Cleric/Druid/Healer Role row.
    /// </summary>
    [TestFixture]
    public class UT_VeteranMentalistOptions
    {
        [Test]
        public void MentalistGetsTheCasterAndRoleRows() => Assert.Multiple(() =>
        {
            Assert.That(CompanionSpellOptions165.IsCasterClass(eCharacterClass.Mentalist), Is.True);
            Assert.That(CompanionSpellOptions165.HasRoleRow(eCharacterClass.Mentalist), Is.True);
            Assert.That(CompanionSpellOptions165.UsesHealerRoleRow(eCharacterClass.Mentalist), Is.True);
            Assert.That(CompanionSpellOptions165.MeleeHealRole(eCharacterClass.Mentalist, CompanionSpellOptions165.RoleMeleeHeal),
                Is.True);
        });

        [Test]
        public void TheOtherHealerRowsAreUnchanged() => Assert.Multiple(() =>
        {
            foreach (eCharacterClass healer in new[] { eCharacterClass.Cleric, eCharacterClass.Druid, eCharacterClass.Healer })
                Assert.That(CompanionSpellOptions165.UsesHealerRoleRow(healer), Is.True, healer.ToString());
            foreach (eCharacterClass other in new[] { eCharacterClass.Shaman, eCharacterClass.Friar, eCharacterClass.Wizard })
                Assert.That(CompanionSpellOptions165.UsesHealerRoleRow(other), Is.False, other.ToString());
            Assert.That(CompanionSpellOptions165.HasRoleRow(eCharacterClass.Shaman), Is.True);
            Assert.That(CompanionSpellOptions165.HasRoleRow(eCharacterClass.Friar), Is.True);
        });
    }
}
