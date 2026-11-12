using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>M8 class abilities (veteran review 2026-11-11): when gamebots and companions fire them.</summary>
    [TestFixture]
    public sealed class UT_BotClassAbilities165
    {
        [Test]
        public void ASpiritmasterNukesOnceItsPetHoldsTheMonsterUnder75Percent() => Assert.Multiple(() =>
        {
            // O10 (owner 2026-11-11): Prima's Spiritmaster routine waits for 75%; other pet casters keep 85%
            Assert.That(BotClassAbilities165.PetHoldsDamageAbovePercent(eCharacterClass.Spiritmaster), Is.EqualTo(75));
            Assert.That(BotClassAbilities165.PetHoldsDamageAbovePercent(eCharacterClass.Enchanter), Is.EqualTo(85));
            Assert.That(BotClassAbilities165.PetHoldsDamageAbovePercent(eCharacterClass.Cabalist), Is.EqualTo(85));
            Assert.That(BotClassAbilities165.PetHoldsDamageAbovePercent(eCharacterClass.Theurgist), Is.EqualTo(85));
        });
    }
}
