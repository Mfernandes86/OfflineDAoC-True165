using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>The shared bot spec engine and the weighted build family roll.</summary>
    [TestFixture, NonParallelizable]
    public class UT_BotSpecTrainer
    {
        // Weapon lines resolve through SkillBase, which needs a server and its language strings (as UT_TemporaryGroupSpecCatalog).
        private sealed class SpecTestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private static readonly IObjectDatabase EmptyDatabase =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previousServer;
        private PetTestLanguageScope _language;

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((SpecTestServer)RuntimeHelpers.GetUninitializedObject(typeof(SpecTestServer)));
        }

        [TearDown]
        public void TearDown()
        {
            GameServer.LoadTestDouble(_previousServer);
            _language.Dispose();
        }

        /// <summary>A class's spec point multiplier (10 = 1x, 20 = 2x ...).</summary>
        public static int Multiplier(eCharacterClass characterClass)
        {
            Type type = typeof(GameBot).Assembly.GetTypes().First(t => !t.IsAbstract &&
                t.GetCustomAttributes<CharacterClassAttribute>(false).Any(attribute => attribute.ID == (int)characterClass));
            return ((ICharacterClass)Activator.CreateInstance(type)).SpecPointsMultiplier;
        }

        /// <summary>Levels a plan to <paramref name="toLevel"/>, training every level (or all at once, like a new bot).</summary>
        public static Dictionary<string, int> Level(BotSpec plan, int multiplier, int toLevel, bool everyLevel = true)
        {
            Dictionary<string, int> levels = plan.SpecLines.Select(line => line.Spec).Distinct().ToDictionary(spec => spec, _ => 1);
            List<BotSpecLine> lines = plan.SpecLines.OrderByDescending(line => line.levelRatio).ToList();
            int left = 0;
            if (everyLevel)
                for (int level = 2; level <= toLevel; level++)
                    left = BotSpecTrainer.Spend(lines, level, level, level, left, multiplier, s => levels[s], (s, v) => levels[s] = v);
            else
                BotSpecTrainer.Spend(lines, 1, toLevel, toLevel, 0, multiplier, s => levels[s], (s, v) => levels[s] = v);
            return levels;
        }

        public static Dictionary<string, int> Level(BotSpec plan, eCharacterClass characterClass, int toLevel) =>
            Level(plan, Multiplier(characterClass), toLevel);

        private static int Cost(int level) => level * (level + 1) / 2 - 1;

        [TestCase(10, 1494)]
        [TestCase(15, 2223)]
        [TestCase(20, 2979)]
        [TestCase(22, 3253)]
        [TestCase(25, 3706)]
        public void PointsToFiftyMatchAPlayersIncludingHalfLevels(int multiplier, int expected)
        {
            // GamePlayer: levels 2-5 give the level, 6+ give multiplier * level / 10, half levels 40.5-49.5 half of that.
            int player = 0;
            for (int level = 2; level <= 50; level++)
            {
                player += level > 5 ? multiplier * level / 10 : level;
                if (level > 40) player += multiplier * (level - 1) / 20;
            }
            int bot = Enumerable.Range(2, 49).Sum(level => BotSpecTrainer.PointsForLevel(multiplier, level));
            Assert.That(bot, Is.EqualTo(player));
            Assert.That(bot, Is.EqualTo(expected));
        }

        [Test]
        public void ABotTrainingEveryLevelGetsAsManyPointsAsOneCatchingUp()
        {
            // Before: only a multi-level catch-up got the half level points; a bot training each level lost 220 (1x).
            BotSpec plan = new SpiritmasterBotSpec(eSpecType.DarkSpirit);
            int total(Dictionary<string, int> levels) => levels.Values.Sum(Cost);
            int everyLevel = total(Level(plan, 10, 50));
            int atOnce = total(Level(plan, 10, 50, false));
            Assert.That(everyLevel, Is.GreaterThan(1494 - 50));
            Assert.That(atOnce, Is.GreaterThan(1494 - 50));
        }

        [Test]
        public void ARatioZeroLineWaitsForFifty()
        {
            BotSpec plan = new();
            plan.SpecLines.Add(new BotSpecLine(Specs.Darkness, 50, 1.0f));
            plan.SpecLines.Add(new BotSpecLine(Specs.Suppression, 50, 0.0f));
            Dictionary<string, int> at49 = Level(plan, 10, 49);
            Assert.That(at49[Specs.Darkness], Is.EqualTo(49));
            Assert.That(at49[Specs.Suppression], Is.EqualTo(1));
            Assert.That(Level(plan, 10, 50)[Specs.Suppression], Is.GreaterThan(1));
        }

        [Test]
        public void SpendLeavesUnaffordablePointsForTheNextLevel()
        {
            List<BotSpecLine> lines = [new BotSpecLine(Specs.Darkness, 10, 1.0f)];
            Dictionary<string, int> levels = new() { [Specs.Darkness] = 10 };
            int left = BotSpecTrainer.Spend(lines, 20, 20, 20, 3, 10, s => levels[s], (s, v) => levels[s] = v);
            Assert.That(left, Is.EqualTo(23));
            Assert.That(levels[Specs.Darkness], Is.EqualTo(10));
        }

        [Test]
        public void WeightsListOnlyTheClassesOwnFamilies()
        {
            foreach (eCharacterClass characterClass in Enum.GetValues<eCharacterClass>())
            {
                IReadOnlyList<(eSpecType Spec, int Weight)> weights = BotSpec.GetSpecializationWeights(characterClass);
                Assert.That(weights.Select(w => w.Spec), Is.EquivalentTo(BotSpec.GetSpecializationChoices(characterClass)), $"{characterClass}");
                Assert.That(weights.All(w => w.Weight > 0), Is.True, $"{characterClass}");
            }
        }

        [Test]
        public void PersistentRollFollowsTheWeights()
        {
            foreach (eCharacterClass characterClass in Enum.GetValues<eCharacterClass>())
            {
                IReadOnlyList<(eSpecType Spec, int Weight)> weights = BotSpec.GetSpecializationWeights(characterClass);
                int total = weights.Sum(w => w.Weight);
                const int bots = 40000;
                Dictionary<eSpecType, int> seen = Enumerable.Range(1, bots)
                    .GroupBy(id => BotSpec.ChoosePersistentSpecialization(characterClass, id))
                    .ToDictionary(g => g.Key, g => g.Count());
                foreach ((eSpecType spec, int weight) in weights)
                    Assert.That(seen.GetValueOrDefault(spec) / (double)bots, Is.EqualTo(weight / (double)total).Within(0.02), $"{characterClass} {spec}");
            }
        }

        [Test]
        public void NewMidgardBotsFollowThePeriodSpecMix()
        {
            // M8: Pac and Mend Healers common, Aug rare; shield Warriors and Thanes common
            int weight(eCharacterClass c, eSpecType s) => BotSpec.GetSpecializationWeights(c).Single(w => w.Spec == s).Weight;
            Assert.That(weight(eCharacterClass.Healer, eSpecType.AugHealer), Is.LessThan(weight(eCharacterClass.Healer, eSpecType.PacHealer)));
            Assert.That(weight(eCharacterClass.Healer, eSpecType.AugHealer), Is.LessThan(weight(eCharacterClass.Healer, eSpecType.MendHealer)));
            foreach (eCharacterClass tank in new[] { eCharacterClass.Warrior, eCharacterClass.Thane })
                Assert.That(weight(tank, eSpecType.OneHandAndShield), Is.GreaterThan(2 * weight(tank, eSpecType.TwoHanded)), $"{tank}");
            Dictionary<eSpecType, int> healers = Enumerable.Range(1, 20000)
                .GroupBy(id => BotSpec.ChoosePersistentSpecialization(eCharacterClass.Healer, id)).ToDictionary(g => g.Key, g => g.Count());
            Assert.That(healers[eSpecType.AugHealer] / 20000.0, Is.EqualTo(0.1).Within(0.02));
        }

        [Test]
        public void PickFollowsTheWeights()
        {
            int[] seen = new int[3];
            for (int i = 0; i < 30000; i++)
                seen[BotSpec.Pick(1, 0, 3)]++;
            Assert.That(seen[1], Is.EqualTo(0));
            Assert.That(seen[2] / 30000.0, Is.EqualTo(0.75).Within(0.02));
        }

        [Test]
        public void ASavedPlanWinsOverWhateverTheClassRollsNow()
        {
            // GameBot(record): the class constructor rolls a plan, then BotLifetimeBuild.Restore puts the saved one back.
            BotSpec saved = new();
            saved.SpecType = eSpecType.DualWieldAndShield;
            saved.WeaponOneType = eObjectType.CrushingWeapon;
            saved.SpecLines.Add(new BotSpecLine(Specs.Crush, 39, 0.8f));
            saved.SpecLines.Add(new BotSpecLine(Specs.Shields, 42, 0.5f));
            BotSpec rolled = new MercenaryBotSpec(eSpecType.DualWield);
            Assert.That(BotLifetimeBuild.Restore(rolled, BotLifetimeBuild.Encode(saved)), Is.True);
            Assert.That(rolled.SpecType, Is.EqualTo(eSpecType.DualWieldAndShield));
            Assert.That(rolled.WeaponOneType, Is.EqualTo(eObjectType.CrushingWeapon));
            Assert.That(rolled.SpecLines.Select(line => line.Spec), Is.EqualTo(new[] { Specs.Crush, Specs.Shields }));
        }

        /// <summary>Every variant of every class's build families (random weapon and variant rolls sampled).</summary>
        private static IEnumerable<(eCharacterClass Class, eSpecType Family, BotSpec Plan)> EveryTemplate()
        {
            foreach (eCharacterClass characterClass in Enum.GetValues<eCharacterClass>())
            {
                if (BotSpec.GetSpec(characterClass, eSpecType.None) == null)
                    continue;
                foreach ((eSpecType family, int _) in BotSpec.GetSpecializationWeights(characterClass))
                {
                    HashSet<string> seen = new();
                    for (int sample = 0; sample < 400; sample++)
                    {
                        BotSpec plan = BotSpec.GetSpec(characterClass, family);
                        string key = string.Join(" ", plan.SpecLines.Select(l => $"{l.Spec}:{l.SpecCap}@{l.levelRatio}"));
                        if (seen.Add(key))
                            yield return (characterClass, family, plan);
                    }
                }
            }
        }

        [Test]
        public void EveryTemplateFitsTheClassPointsAtFifty()
        {
            // O11 (owner 2026-11-11): 13+ templates asked for more points than level 50 gives, starving their last line.
            List<string> over = new();
            foreach ((eCharacterClass characterClass, eSpecType family, BotSpec plan) in EveryTemplate())
            {
                int multiplier = Multiplier(characterClass);
                int cost = BotSpecBudget.CostAtFifty(plan.SpecLines);
                int budget = BotSpecBudget.PointsAtFifty(multiplier);
                if (cost > budget)
                    over.Add($"{characterClass} {family} {string.Join("/", plan.SpecLines.Select(l => $"{l.Spec}:{l.SpecCap}@{l.levelRatio}"))} = {cost} > {budget}");
            }
            Assert.That(over, Is.Empty, string.Join("\n", over));
        }

        [Test]
        public void EveryTemplateReachesEveryCapAtFifty()
        {
            List<string> short_ = new();
            foreach ((eCharacterClass characterClass, eSpecType family, BotSpec plan) in EveryTemplate())
            {
                Dictionary<string, int> at50 = Level(plan, Multiplier(characterClass), 50);
                foreach (IGrouping<string, BotSpecLine> line in plan.SpecLines.Where(l => !BotSpecBudget.IsRestOfPointsLine(l)).GroupBy(l => l.Spec))
                {
                    int cap = (int)Math.Min(50, line.Max(l => l.SpecCap));
                    if (at50[line.Key] < cap)
                        short_.Add($"{characterClass} {family} {line.Key} {at50[line.Key]} < {cap}");
                }
            }
            Assert.That(short_, Is.Empty, string.Join("\n", short_));
        }

        [Test]
        public void BudgetMatchesThePointsTable()
        {
            Assert.That(BotSpecBudget.PointsAtFifty(10), Is.EqualTo(1494));
            Assert.That(BotSpecBudget.PointsAtFifty(20), Is.EqualTo(2979));
            Assert.That(BotSpecBudget.LineCost(1), Is.EqualTo(0));
            Assert.That(BotSpecBudget.LineCost(50), Is.EqualTo(1274));
        }

        [Test]
        public void FitTrimsAnOldOverBudgetPlansLowestRatioLine()
        {
            // the pre-fix Air Theurgist: Wind 45 + Earth 28 left 55 points for a planned Cold 20
            BotSpec old = new();
            old.SpecLines.Add(new BotSpecLine(Specs.Earth_Magic, 28, 0.1f));
            old.SpecLines.Add(new BotSpecLine(Specs.Cold_Magic, 20, 0.0f));
            old.SpecLines.Add(new BotSpecLine(Specs.Wind_Magic, 45, 1.0f));
            Assert.That(BotSpecBudget.Fit(old, 10), Is.True);
            Assert.That(old.SpecLines.Select(l => l.SpecCap), Is.EqualTo(new uint[] { 28, 10, 45 }));
            Assert.That(BotSpecBudget.Fits(old, 10), Is.True);
            Assert.That(BotSpecBudget.Fit(old, 10), Is.False, "a plan that fits is left alone");
        }

        [Test]
        public void FitLeavesEveryCurrentTemplateUnchanged()
        {
            foreach ((eCharacterClass characterClass, eSpecType family, BotSpec plan) in EveryTemplate())
                Assert.That(BotSpecBudget.Fit(plan, Multiplier(characterClass)), Is.False, $"{characterClass} {family}");
        }

        [Test]
        public void FitNeverLowersPointsAlreadySpent()
        {
            // existing bots keep what they trained: the trainer only buys up to a cap, it never takes points back
            BotSpec old = new();
            old.SpecLines.Add(new BotSpecLine(Specs.Wind_Magic, 45, 1.0f));
            old.SpecLines.Add(new BotSpecLine(Specs.Earth_Magic, 28, 0.1f));
            old.SpecLines.Add(new BotSpecLine(Specs.Cold_Magic, 20, 0.0f));
            BotSpecBudget.Fit(old, 10);
            Dictionary<string, int> levels = new() { [Specs.Wind_Magic] = 45, [Specs.Earth_Magic] = 28, [Specs.Cold_Magic] = 15 };
            BotSpecTrainer.Spend(old.SpecLines, 50, 50, 50, 40, 10, s => levels[s], (s, v) => levels[s] = v);
            Assert.That(levels[Specs.Cold_Magic], Is.EqualTo(15));
        }

        /// <summary>Prints every class's builds at 20/30/40/50 (run by hand when tuning plans).</summary>
        [Test, Explicit]
        public void ReportEveryBuild()
        {
            foreach (eCharacterClass characterClass in Enum.GetValues<eCharacterClass>())
            {
                if (BotSpec.GetSpec(characterClass, eSpecType.None) == null)
                    continue;
                int multiplier = Multiplier(characterClass);
                foreach ((eSpecType family, int weight) in BotSpec.GetSpecializationWeights(characterClass))
                {
                    HashSet<string> printed = new();
                    for (int sample = 0; sample < 200; sample++)
                    {
                        BotSpec plan = BotSpec.GetSpec(characterClass, family);
                        string key = string.Join(" ", plan.SpecLines.Select(l => $"{l.Spec}:{l.SpecCap}@{l.levelRatio}"));
                        if (!printed.Add(key))
                            continue;
                        string at(int level) => string.Join("/", plan.SpecLines.Select(l => Level(plan, multiplier, level)[l.Spec]));
                        TestContext.WriteLine($"{characterClass} {family}(w{weight}) {key} | 20:{at(20)} 30:{at(30)} 40:{at(40)} 50:{at(50)}");
                    }
                }
            }
        }
    }
}
