using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.RealmAbilities;
using NUnit.Framework;
using static DOL.GS.AutonomousBotRealmAbilities;

namespace DOL.UnitTests
{
    /// <summary>Item 53: True 1.65 bot realm abilities - tank builds and the class/caster actives with their use rules.</summary>
    [TestFixture]
    public class UT_BotRealmAbilities165
    {
        private static Dictionary<string, int> Catalog(params string[] keys) =>
            keys.Select(k => k.StartsWith("AtlasOF_") ? k : "AtlasOF_" + k).ToDictionary(k => k, k => k switch
            {
                Purge or PurgeReduced or SecondWind or IgnorePain or IgnorePainTank or MasteryOfConcentration or BunkerOfFaith or
                    PerfectRecovery or SpeedOfSound or Vanish or AmelioratingMelodies or SoldiersBarricade or FaithHealing or
                    RagingPower or VolcanicPillar or BatteryOfLife => 1,
                FirstAid or MysticCrystalLore => 3,
                _ => 5,
            });

        private static int Cost(string key, int level) => key switch
        {
            Purge or SecondWind or BunkerOfFaith or SpeedOfSound or Vanish or SoldiersBarricade or RagingPower or BatteryOfLife => 10,
            FaithHealing or VolcanicPillar => 14,
            PurgeReduced => 4,
            MasteryOfConcentration or PerfectRecovery or IgnorePain => 14,
            IgnorePainTank => 8,
            FirstAid or MysticCrystalLore => AtlasRAHelpers.GetCommonUpgradeCostFor3LevelsRA(level),
            _ => AtlasRAHelpers.GetCommonUpgradeCostFor5LevelsRA(level),
        };

        private static readonly Dictionary<string, int> Armsman = Catalog("AugStr", "AugCon", "AugDex", "AugQui", "Toughness",
            "AvoidanceOfMagic", "Determination", "MasteryOfPain", "MasteryOfParrying", "MasteryOfBlocking", "MasteryOfArms",
            PurgeReduced, SecondWind, FirstAid, IgnorePainTank);

        [Test]
        public void TanksBuildAsTanksOnlyInTrue165Mode()
        {
            Assert.That(BuildArchetype(Archetype.Melee, (int)eCharacterClass.Armsman, true), Is.EqualTo(Archetype.Tank));
            Assert.That(BuildArchetype(Archetype.Melee, (int)eCharacterClass.Warrior, true), Is.EqualTo(Archetype.Tank));
            Assert.That(BuildArchetype(Archetype.Melee, (int)eCharacterClass.Armsman, false), Is.EqualTo(Archetype.Melee));
            Assert.That(BuildArchetype(Archetype.Melee, (int)eCharacterClass.Infiltrator, true), Is.EqualTo(Archetype.Melee));
            Assert.That(BuildArchetype(Archetype.Caster, (int)eCharacterClass.Wizard, true), Is.EqualTo(Archetype.Caster));
        }

        [Test]
        public void ATankTakesPurgeFirstAndDeterminationRightAfter()
        {
            var four = Plan(Armsman, Archetype.Tank, (int)eCharacterClass.Armsman, 4, Cost, true);
            Assert.That(four.Keys, Is.EquivalentTo(new[] { PurgeReduced }));
            var five = Plan(Armsman, Archetype.Tank, (int)eCharacterClass.Armsman, 5, Cost, true);
            Assert.That(five.GetValueOrDefault("AtlasOF_Determination"), Is.EqualTo(1));
            var old = Plan(Armsman, Archetype.Melee, (int)eCharacterClass.Armsman, 5, Cost);
            Assert.That(old.ContainsKey(PurgeReduced) || old.ContainsKey("AtlasOF_Determination"), Is.False, "normal mode build unchanged");
        }

        [Test]
        public void TankBuildGrowsAndMeetsPrerequisites()
        {
            Dictionary<string, int> previous = new();
            for (int points = 0; points <= 120; points++)
            {
                var plan = Plan(Armsman, Archetype.Tank, (int)eCharacterClass.Armsman, points, Cost, true);
                int spent = plan.Sum(pair => Enumerable.Range(0, pair.Value).Sum(level => Cost(pair.Key, level)));
                Assert.That(spent, Is.LessThanOrEqualTo(points));
                foreach (var (key, _) in plan)
                    if (Requirement(key, (int)eCharacterClass.Armsman) is { } need)
                        Assert.That(plan.GetValueOrDefault(need.Key), Is.GreaterThanOrEqualTo(need.Level), key);
                foreach (var (key, level) in previous)
                    Assert.That(plan.GetValueOrDefault(key), Is.GreaterThanOrEqualTo(level), $"{key} never shrinks");
                previous = plan;
            }
        }

        [Test]
        public void ClassActivesAreBoughtAfterPurgeAndMocAfterAcuityThree()
        {
            var cleric = Catalog("AugAcuity", "AugCon", "AugDex", "MasteryOfHealing", "WildHealing", "Serenity", "Toughness",
                Purge, MasteryOfConcentration, BunkerOfFaith, MysticCrystalLore);
            var plan = Plan(cleric, Archetype.Healer, (int)eCharacterClass.Cleric, 80, Cost, true);
            Assert.That(plan.ContainsKey(BunkerOfFaith), Is.True);
            Assert.That(plan.ContainsKey(MasteryOfConcentration), Is.True);
            Assert.That(plan[AugAcuity], Is.GreaterThanOrEqualTo(3));
            var normal = Plan(cleric, Archetype.Healer, (int)eCharacterClass.Cleric, 40, Cost);
            Assert.That(normal.ContainsKey(BunkerOfFaith), Is.False, "normal mode has no class actives in its build");
        }

        [Test]
        public void ClassicActivesAreOnlyAddedInTrue165Mode()
        {
            Assert.That(UsableActivesFor(false), Is.EqualTo(UsableActives));
            Assert.That(UsableActivesFor(true), Is.SupersetOf(UsableActives));
            Assert.That(UsableActivesFor(true), Does.Contain("AtlasOF_MCL"), "the Atlas OF key of Mystic Crystal Lore");
            Assert.That(UsableActivesFor(true), Is.SupersetOf(new[] { MasteryOfConcentration, BunkerOfFaith, PerfectRecovery,
                SpeedOfSound, AmelioratingMelodies, Vanish }));
        }

        private static Situation S(bool combat = true, bool frontier = true, int health = 100, int mana = 100, bool controlled = false,
            bool sitting = false, bool casting = false, bool interrupted = false, int near = 8, int hurt = 0, bool fighting = true,
            bool dead = false) =>
            new(combat, frontier, health, mana, controlled, sitting, casting, interrupted, near, hurt, fighting, dead);

        [Test]
        public void GroupBurstCallsForBunkerAndSpeedOfSound()
        {
            Assert.That(Wants(BunkerOfFaith, S(hurt: 4)), Is.True);
            Assert.That(Wants(BunkerOfFaith, S(hurt: 3)), Is.False);
            Assert.That(Wants(BunkerOfFaith, S(near: 2, hurt: 2)), Is.True);
            Assert.That(Wants(BunkerOfFaith, S(near: 1, hurt: 1)), Is.False, "at least two hurt");
            Assert.That(Wants(BunkerOfFaith, S(hurt: 8, fighting: false)), Is.False);
            Assert.That(Wants(SpeedOfSound, S(hurt: 4)), Is.True);
            Assert.That(Wants(SpeedOfSound, S(hurt: 4, frontier: false)), Is.False);
            Assert.That(Wants(AmelioratingMelodies, S(hurt: 2)), Is.True);
            Assert.That(Wants(AmelioratingMelodies, S(hurt: 1)), Is.False);
        }

        [Test]
        public void MocWhenHitWhileCastingMclWhenLowOutOfCombat()
        {
            Assert.That(Wants(MasteryOfConcentration, S(interrupted: true)), Is.True);
            Assert.That(Wants(MasteryOfConcentration, S(interrupted: false)), Is.False);
            Assert.That(Wants(MasteryOfConcentration, S(interrupted: true, frontier: false)), Is.False);
            Assert.That(Wants(MasteryOfConcentration, S(interrupted: true, frontier: false, health: 50)), Is.True);
            Assert.That(Wants(MysticCrystalLore, S(combat: false, mana: 20)), Is.True);
            Assert.That(Wants(MysticCrystalLore, S(combat: true, mana: 20)), Is.False);
            Assert.That(Wants(MysticCrystalLore, S(combat: false, mana: 20, sitting: true)), Is.False);
            Assert.That(Wants(MysticCrystalLore, S(combat: false, mana: 50)), Is.False);
        }

        [Test]
        public void VanishEscapesAFrontierFightGoingBadly()
        {
            Assert.That(Wants(Vanish, S(health: 30)), Is.True);
            Assert.That(Wants(Vanish, S(health: 50)), Is.False);
            Assert.That(Wants(Vanish, S(health: 30, frontier: false)), Is.False);
            Assert.That(Wants(Vanish, S(health: 30, controlled: true)), Is.False, "Vanish cannot be used mezzed or stunned");
        }

        [Test]
        public void TheBuildFollowsTheSpecInTrue165Mode() => Assert.Multiple(() =>
        {
            // M8 (veteran review 2026-11-11)
            Assert.That(SpecArchetype(Archetype.Melee, (int)eCharacterClass.Friar, eSpecType.RejuvFriar, false, true), Is.EqualTo(Archetype.Healer));
            Assert.That(SpecArchetype(Archetype.Melee, (int)eCharacterClass.Friar, eSpecType.StaffFriar, true, true), Is.EqualTo(Archetype.Melee));
            Assert.That(SpecArchetype(Archetype.Healer, (int)eCharacterClass.Cleric, eSpecType.SmiteCleric, false, true), Is.EqualTo(Archetype.Caster));
            Assert.That(SpecArchetype(Archetype.Healer, (int)eCharacterClass.Cleric, eSpecType.RejuvCleric, false, true), Is.EqualTo(Archetype.Healer));
            Assert.That(SpecArchetype(Archetype.Tank, (int)eCharacterClass.Armsman, eSpecType.TwoHanded, true, true), Is.EqualTo(Archetype.TwoHander));
            Assert.That(SpecArchetype(Archetype.Tank, (int)eCharacterClass.Armsman, eSpecType.OneHandAndShield, false, true), Is.EqualTo(Archetype.Tank));
            Assert.That(SpecArchetype(Archetype.Melee, (int)eCharacterClass.Friar, eSpecType.RejuvFriar, false, false), Is.EqualTo(Archetype.Melee),
                "normal mode unchanged");
        });

        [Test]
        public void AShieldTankTakesDeterminationAndMasteryOfBlockingFirst()
        {
            var plan = Plan(Armsman, Archetype.Tank, (int)eCharacterClass.Armsman, 20, Cost, true);
            Assert.That(plan.GetValueOrDefault("AtlasOF_Determination"), Is.GreaterThanOrEqualTo(1));
            Assert.That(plan.GetValueOrDefault("AtlasOF_MasteryOfBlocking"), Is.GreaterThanOrEqualTo(1));
            Assert.That(plan.ContainsKey("AtlasOF_MasteryOfArms"), Is.False);
        }

        [Test]
        public void ATwoHanderTakesMasteryOfPainAndArmsAndNeverBlocking()
        {
            var early = Plan(Armsman, Archetype.TwoHander, (int)eCharacterClass.Armsman, 20, Cost, true);
            Assert.That(early.GetValueOrDefault("AtlasOF_MasteryOfPain"), Is.GreaterThanOrEqualTo(1));
            Assert.That(early.GetValueOrDefault("AtlasOF_MasteryOfArms"), Is.GreaterThanOrEqualTo(1));
            Dictionary<string, int> previous = new();
            for (int points = 0; points <= 120; points++)
            {
                var plan = Plan(Armsman, Archetype.TwoHander, (int)eCharacterClass.Armsman, points, Cost, true);
                int spent = plan.Sum(pair => Enumerable.Range(0, pair.Value).Sum(level => Cost(pair.Key, level)));
                Assert.That(spent, Is.LessThanOrEqualTo(points));
                foreach (var (key, _) in plan)
                    if (Requirement(key, (int)eCharacterClass.Armsman) is { } need)
                        Assert.That(plan.GetValueOrDefault(need.Key), Is.GreaterThanOrEqualTo(need.Level), key);
                foreach (var (key, level) in previous)
                    Assert.That(plan.GetValueOrDefault(key), Is.GreaterThanOrEqualTo(level), $"{key} never shrinks");
                previous = plan;
            }
            var build = Plan(Armsman, Archetype.TwoHander, (int)eCharacterClass.Armsman, 60, Cost, true);
            Assert.That(build.ContainsKey("AtlasOF_MasteryOfBlocking"), Is.False, "only bought, if ever, among the leftovers");
        }

        [Test]
        public void ARejuvFriarTakesTheHealerBuild()
        {
            var friar = Catalog("AugStr", "AugDex", "AugCon", "AugAcuity", "MasteryOfPain", "MasteryOfParrying", "MasteryOfArms",
                "Serenity", "MasteryOfHealing", "MasteryOfTheArcane", "Toughness", Purge, MysticCrystalLore, RagingPower);
            var plan = Plan(friar, Archetype.Healer, (int)eCharacterClass.Friar, 20, Cost, true);
            Assert.That(plan.GetValueOrDefault(AugAcuity), Is.GreaterThanOrEqualTo(2));
            Assert.That(plan.ContainsKey("AtlasOF_MasteryOfHealing"), Is.True);
            Assert.That(plan.ContainsKey("AtlasOF_MasteryOfPain"), Is.False);
        }

        [Test]
        public void AlbionClassActivesAreBoughtAndRagingPowerWaitsForCrystalLoreTwo()
        {
            var armsman = new Dictionary<string, int>(Armsman) { [SoldiersBarricade] = 1 };
            Assert.That(Plan(armsman, Archetype.Tank, (int)eCharacterClass.Armsman, 80, Cost, true).ContainsKey(SoldiersBarricade), Is.True);
            Assert.That(Requirement(RagingPower, (int)eCharacterClass.Wizard), Is.EqualTo(((string, int)?)(MysticCrystalLore, 2)));
            var wizard = Catalog("AugAcuity", "AugCon", "AugDex", "MasteryOfMagery", "WildPower", "Serenity", "MasteryOfTheArcane",
                "Toughness", Purge, MasteryOfConcentration, MysticCrystalLore, RagingPower, VolcanicPillar);
            Dictionary<string, int> previous = new();
            for (int points = 0; points <= 100; points++)
            {
                var plan = Plan(wizard, Archetype.Caster, (int)eCharacterClass.Wizard, points, Cost, true);
                if (plan.ContainsKey(RagingPower))
                    Assert.That(plan.GetValueOrDefault(MysticCrystalLore), Is.GreaterThanOrEqualTo(2), $"{points}");
                foreach (var (key, level) in previous)
                    Assert.That(plan.GetValueOrDefault(key), Is.GreaterThanOrEqualTo(level), $"{key} never shrinks");
                previous = plan;
            }
            Assert.That(previous.ContainsKey(VolcanicPillar), Is.True);
            Assert.That(previous.ContainsKey(RagingPower), Is.True);
            Assert.That(UsableActivesFor(true), Is.SupersetOf(new[] { SoldiersBarricade, FaithHealing, RagingPower, VolcanicPillar, BatteryOfLife }));
            var cleric = Catalog("AugAcuity", "AugCon", "AugDex", "MasteryOfHealing", "WildHealing", "Serenity", "Toughness",
                Purge, MasteryOfConcentration, BunkerOfFaith, BatteryOfLife, MysticCrystalLore);
            Assert.That(Plan(cleric, Archetype.Healer, (int)eCharacterClass.Cleric, 100, Cost, true).ContainsKey(BatteryOfLife), Is.True);
            Assert.That(UsableActivesFor(false), Does.Not.Contain(SoldiersBarricade), "normal mode unchanged");
        }

        [Test]
        public void AlbionActivesFireInTheirSituation() => Assert.Multiple(() =>
        {
            Assert.That(Wants(SoldiersBarricade, S(hurt: 4)), Is.True, "group burst");
            Assert.That(Wants(SoldiersBarricade, S(hurt: 1)), Is.False);
            Assert.That(Wants(BatteryOfLife, S(hurt: 4)), Is.True, "group burst");
            Assert.That(Wants(BatteryOfLife, S(hurt: 1)), Is.False);
            Assert.That(Wants(FaithHealing, S(hurt: 4)), Is.True);
            Assert.That(Wants(FaithHealing, S(near: 1, hurt: 1, health: 20)), Is.True, "the Paladin itself near death");
            Assert.That(Wants(FaithHealing, S(hurt: 2)), Is.False, "a 30-minute heal waits for a real emergency");
            Assert.That(Wants(RagingPower, S(mana: 20)), Is.True);
            Assert.That(Wants(RagingPower, S(mana: 20, combat: false)), Is.False, "out of a fight MCL does it");
            Assert.That(Wants(RagingPower, S(mana: 40)), Is.False);
            Assert.That(Wants(VolcanicPillar, S() with { EnemiesAtTarget = 3 }), Is.True);
            Assert.That(Wants(VolcanicPillar, S() with { EnemiesAtTarget = 2 }), Is.False);
            Assert.That(Wants(VolcanicPillar, S(casting: true) with { EnemiesAtTarget = 4 }), Is.False);
            Assert.That(Wants(VolcanicPillar, S(controlled: true) with { EnemiesAtTarget = 4 }), Is.False);
        });

        [Test]
        public void PerfectRecoveryNeedsADeadMemberAndAFight()
        {
            Assert.That(Wants(PerfectRecovery, S()), Is.False);
            Assert.That(Wants(PerfectRecovery, S(dead: true)), Is.True);
            Assert.That(Wants(PerfectRecovery, S(dead: true, fighting: false)), Is.False);
            Assert.That(Wants(PerfectRecovery, S(dead: true, casting: true)), Is.False);
        }
    }
}
