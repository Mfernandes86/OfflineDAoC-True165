using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q16: a grouped Pacification Healer stuns the kill target once as the tank engages.</summary>
    [TestFixture]
    public class UT_VeteranOpeningStun
    {
        private static bool Stun(bool pac = true, bool grouped = true, bool engaged = true, bool done = false, int power = 80,
            int health = 100, int monsters = 1, bool immune = false) =>
            BotBrain.OpeningStunWorthwhile(pac, grouped, engaged, done, power, health, monsters, immune);

        [Test]
        public void StunsOnceAsTheTankEngages() => Assert.That(Stun(), Is.True);

        [Test]
        public void OnlyWhenItIsWorthIt() => Assert.Multiple(() =>
        {
            Assert.That(Stun(pac: false), Is.False, "a Mending Healer heals");
            Assert.That(Stun(grouped: false), Is.False);
            Assert.That(Stun(engaged: false), Is.False, "the tank has not engaged yet");
            Assert.That(Stun(done: true), Is.False, "once per fight");
            Assert.That(Stun(power: 49), Is.False, "half power or more");
            Assert.That(Stun(health: 74), Is.False, "an opener, not a mid-fight stun");
            Assert.That(Stun(monsters: 2), Is.False, "an add needs the control instead");
            Assert.That(Stun(immune: true), Is.False);
        });
    }
}
