using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q14: damage adds and weapon procs go on classes that melee.</summary>
    [TestFixture]
    public class UT_VeteranMeleeBuffs
    {
        [Test]
        public void DamageAddsGoOnClassesThatMelee() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.MeleeBuffHelps(eClassType.PureTank, eCharacterClass.Blademaster), Is.True);
            Assert.That(BotBrain.MeleeBuffHelps(eClassType.Hybrid, eCharacterClass.Warden), Is.True);
            Assert.That(BotBrain.MeleeBuffHelps(eClassType.Hybrid, eCharacterClass.Friar), Is.True);
            Assert.That(BotBrain.MeleeBuffHelps(eClassType.ListCaster, eCharacterClass.Valewalker), Is.True,
                "a Valewalker is a list caster by class type but fights with its scythe");
            Assert.That(BotBrain.MeleeBuffHelps(eClassType.ListCaster, eCharacterClass.Eldritch), Is.False);
            Assert.That(BotBrain.MeleeBuffHelps(eClassType.ListCaster, eCharacterClass.Runemaster), Is.False);
            Assert.That(BotBrain.MeleeBuffHelps(eClassType.Hybrid, eCharacterClass.Healer), Is.False);
            Assert.That(BotBrain.MeleeBuffHelps(eClassType.Hybrid, eCharacterClass.Druid), Is.False);
        });
    }
}
