using System.Collections.Generic;
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
        public void AShieldTankEngagesAnArcherOrAMonsterWhileTheHealerIsLow() => Assert.Multiple(() =>
        {
            Assert.That(BotClassAbilities165.EngageWorthwhile(true, true, true, false, false, enemyArcher: true, healerLowOnPower: false), Is.True,
                "an archer shooting at it");
            Assert.That(BotClassAbilities165.EngageWorthwhile(true, true, true, false, false, enemyArcher: false, healerLowOnPower: true), Is.True,
                "a monster coming at it while the healer rests");
            Assert.That(BotClassAbilities165.EngageWorthwhile(true, true, true, false, false, false, false), Is.False,
                "otherwise it fights");
            Assert.That(BotClassAbilities165.EngageWorthwhile(true, true, true, enemyAttackedRecently: true, false, true, true), Is.False,
                "the ability's own rule: nobody hit the enemy in the last 5 s");
            Assert.That(BotClassAbilities165.EngageWorthwhile(true, true, true, false, otherEnemyInMelee: true, true, true), Is.False,
                "Engage blocks one enemy only");
            Assert.That(BotClassAbilities165.EngageWorthwhile(false, true, true, false, false, true, true), Is.False, "not a shield tank");
            Assert.That(BotClassAbilities165.EngageWorthwhile(true, false, true, false, false, true, true), Is.False, "no shield up");
            Assert.That(BotClassAbilities165.EngageWorthwhile(true, true, false, false, false, true, true), Is.False, "the enemy is not on it");
            Assert.That(BotClassAbilities165.EngageShieldSpec, Is.EqualTo(25));
        });

        [Test]
        public void EngageEndsWhenItNoLongerHelps() => Assert.Multiple(() =>
        {
            Assert.That(BotClassAbilities165.EngageHolds(false, 2_000, enemyArcher: true, inMeleeReach: false, false), Is.True);
            Assert.That(BotClassAbilities165.EngageHolds(false, 2_000, enemyArcher: true, inMeleeReach: true, false), Is.False,
                "it reached the archer: swing");
            Assert.That(BotClassAbilities165.EngageHolds(false, 2_000, enemyArcher: false, false, healerLowOnPower: true), Is.True);
            Assert.That(BotClassAbilities165.EngageHolds(false, 2_000, enemyArcher: false, false, healerLowOnPower: false), Is.False,
                "the healer has power again");
            Assert.That(BotClassAbilities165.EngageHolds(true, 2_000, true, false, true), Is.False, "someone else hits it: no extra block");
            Assert.That(BotClassAbilities165.EngageHolds(false, 15_000, true, false, true), Is.False, "at most 15 s without a swing");
        });

        [Test]
        public void APaladinPicksItsOneChantBySituation() => Assert.Multiple(() =>
        {
            // O1 (owner 2026-11-11)
            var all = new HashSet<eSpellType> { eSpellType.EnduranceRegenBuff, eSpellType.DamageAdd, eSpellType.CombatHeal,
                eSpellType.SpecArmorFactorBuff };
            var main = BotClassAbilities165.PaladinRole.MainTank;
            var off = BotClassAbilities165.PaladinRole.OffTank;
            var solo = BotClassAbilities165.PaladinRole.Solo;
            Assert.That(BotClassAbilities165.PaladinChant(main, true, true, 100, 100, all), Is.EqualTo(eSpellType.CombatHeal),
                "main tank: Refreshment full time keeps the aggro");
            Assert.That(BotClassAbilities165.PaladinChant(main, true, true, 100, 10, all), Is.EqualTo(eSpellType.CombatHeal),
                "a tank keeps the heal chant even low on endurance");
            Assert.That(BotClassAbilities165.PaladinChant(off, true, false, 100, 100, all), Is.EqualTo(eSpellType.DamageAdd));
            Assert.That(BotClassAbilities165.PaladinChant(off, true, false, 100, 20, all), Is.EqualTo(eSpellType.EnduranceRegenBuff),
                "endurance only when low and not tanking");
            Assert.That(BotClassAbilities165.PaladinChant(solo, true, true, 90, 100, all), Is.EqualTo(eSpellType.DamageAdd));
            Assert.That(BotClassAbilities165.PaladinChant(solo, true, true, 50, 100, all), Is.EqualTo(eSpellType.CombatHeal));
            Assert.That(BotClassAbilities165.PaladinChant(solo, true, true, 90, 20, all), Is.EqualTo(eSpellType.DamageAdd),
                "solo with the monster on it: tanking, no endurance switch");
            Assert.That(BotClassAbilities165.PaladinChant(main, false, true, 100, 100, all), Is.EqualTo(eSpellType.EnduranceRegenBuff),
                "out of a fight");
            Assert.That(BotClassAbilities165.PaladinChant(main, true, true, 100, 100, new HashSet<eSpellType> { eSpellType.DamageAdd }),
                Is.EqualTo(eSpellType.DamageAdd), "a young Paladin without the heal chant");
        });

        [Test]
        public void APaladinsChantRoleFollowsThePartyRoleSheet() => Assert.Multiple(() =>
        {
            // O1 with M2 (merge of the veteran-bots lanes): the spec-ranked role sheet decides who main-tanks
            Assert.That(BotClassAbilities165.PaladinRoleFor(false, null, false), Is.EqualTo(BotClassAbilities165.PaladinRole.Solo));
            Assert.That(BotClassAbilities165.PaladinRoleFor(true, true, true), Is.EqualTo(BotClassAbilities165.PaladinRole.MainTank),
                "the sheet's main tank, even with a two-hander Armsman beside it");
            Assert.That(BotClassAbilities165.PaladinRoleFor(true, false, false), Is.EqualTo(BotClassAbilities165.PaladinRole.OffTank),
                "the sheet names another main tank");
            Assert.That(BotClassAbilities165.PaladinRoleFor(true, null, true), Is.EqualTo(BotClassAbilities165.PaladinRole.OffTank),
                "no sheet: another tank in the group");
            Assert.That(BotClassAbilities165.PaladinRoleFor(true, null, false), Is.EqualTo(BotClassAbilities165.PaladinRole.MainTank),
                "no sheet: the only tank");
        });

        [Test]
        public void APaladinHoldsAChantItStartedForTenSeconds() => Assert.Multiple(() =>
        {
            Assert.That(BotClassAbilities165.PaladinKeepsChant(true, 9_000), Is.True);
            Assert.That(BotClassAbilities165.PaladinKeepsChant(true, 10_000), Is.False);
            Assert.That(BotClassAbilities165.PaladinKeepsChant(false, 1_000), Is.False, "a chant it did not start is not held");
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
