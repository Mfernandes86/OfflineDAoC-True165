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

        [Test]
        public void TripleWieldWaitsForAFightThatNeedsIt() => Assert.Multiple(() =>
        {
            Assert.That(BotClassAbilities165.TripleWieldWorthwhile(hardFight: true, inMeleeReach: true, alreadyActive: false), Is.True);
            Assert.That(BotClassAbilities165.TripleWieldWorthwhile(hardFight: false, inMeleeReach: true, alreadyActive: false), Is.False,
                "not on a grey: it is a seven-minute ability");
            Assert.That(BotClassAbilities165.TripleWieldWorthwhile(hardFight: true, inMeleeReach: false, alreadyActive: false), Is.False);
            Assert.That(BotClassAbilities165.TripleWieldWorthwhile(hardFight: true, inMeleeReach: true, alreadyActive: true), Is.False);
        });

        [Test]
        public void FlurryHitsOnlyPlayerLikeEnemiesAndKeepGuards() => Assert.Multiple(() =>
        {
            // the ability's own rule (FlurryAbilityHandler): players and keep guards; a gamebot stands in for a player
            Assert.That(BotClassAbilities165.FlurryTargetAllowed(playerLike: false, keepGuard: false), Is.False, "never on a monster");
            Assert.That(BotClassAbilities165.FlurryTargetAllowed(playerLike: true, keepGuard: false), Is.True);
            Assert.That(BotClassAbilities165.FlurryTargetAllowed(playerLike: false, keepGuard: true), Is.True);
            Assert.That(BotClassAbilities165.FlurryRange, Is.EqualTo(135));
        });

        [Test]
        public void RapidFireFinishesARunnerAndRacesACaster() => Assert.Multiple(() =>
        {
            Assert.That(BotClassAbilities165.WantsRapidFire(false, targetMoving: true, targetCasting: false, 25, targetOnArcher: false), Is.True,
                "a monster running off under 30%");
            Assert.That(BotClassAbilities165.WantsRapidFire(false, targetMoving: true, targetCasting: false, 25, targetOnArcher: true), Is.False,
                "one running at the archer is coming to it");
            Assert.That(BotClassAbilities165.WantsRapidFire(false, targetMoving: true, targetCasting: false, 60, targetOnArcher: false), Is.False);
            Assert.That(BotClassAbilities165.WantsRapidFire(false, targetMoving: false, targetCasting: false, 10, targetOnArcher: false), Is.False,
                "a monster standing still takes full-strength arrows");
            Assert.That(BotClassAbilities165.WantsRapidFire(true, targetMoving: false, targetCasting: true, 90, targetOnArcher: false), Is.True,
                "race a casting enemy");
            Assert.That(BotClassAbilities165.WantsRapidFire(true, targetMoving: true, targetCasting: false, 90, targetOnArcher: false), Is.True);
            Assert.That(BotClassAbilities165.WantsRapidFire(true, targetMoving: false, targetCasting: false, 90, targetOnArcher: false), Is.False);
        });
    }
}
