using System.Text.Json;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Quest fix 0.7 (quest audit 2026-11-11): the classic-quests.json properties and rules the data fixes rely on.</summary>
[TestFixture]
public class UT_QuestFix07
{
    private static ClassicQuests.Config Parse(string json) =>
        JsonSerializer.Deserialize<ClassicQuests.Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    [Test]
    public void RequiresAnyOfParses()
    {
        var config = Parse("""{ "Quests": { "21076": { "Steps": [ null ], "RequiresAnyOf": [ 21070, 21071, 21075 ] } } }""");
        Assert.That(config.Quests[21076].RequiresAnyOf, Is.EqualTo(new[] { 21070, 21071, 21075 }));
        Assert.That(Parse("""{ "Quests": { "1": { "Steps": [ null ] } } }""").Quests[1].RequiresAnyOf, Is.Empty);
    }

    private const string TwoChooseSteps = """
    { "Quests": { "20677": { "Steps": [ null, null,
        { "Custom": { "Kind": "choose", "Target": "Edric", "Choices": { "Bracer of Might": "cq_bracer_might", "Bracer of Wit": "cq_bracer_wit" } } },
        { "Custom": { "Kind": "choose", "Target": "Edric", "Choices": { "Bracer of Might": "cq_bracer_might", "Bracer of Wit": "cq_bracer_wit" } } } ] },
      "1": { "Steps": [ null, { "Custom": { "Kind": "choose", "Target": "Vismer", "Choices": { "Staff": "cq_staff" } } } ] } } }
    """;

    [Test]
    public void TheSecondChooseStepRefusesTheItemPickedOnTheFirst()
    {
        var config = Parse(TwoChooseSteps);
        var info = config.Quests[20677];
        bool HoldsMight(string id) => id == "cq_bracer_might";
        Assert.That(ClassicQuests.RepeatsEarlierChoice(info, 3, "cq_bracer_might", HoldsMight), Is.True);
        Assert.That(ClassicQuests.RepeatsEarlierChoice(info, 3, "cq_bracer_wit", HoldsMight), Is.False);
        Assert.That(ClassicQuests.RepeatsEarlierChoice(info, 3, "cq_bracer_might", _ => false), Is.False, "sold or never picked");
    }

    [Test]
    public void ASingleChooseStepNeverRefusesAnItemOwnedFromElsewhere()
    {
        var info = Parse(TwoChooseSteps).Quests[1];
        Assert.That(ClassicQuests.RepeatsEarlierChoice(info, 1, "cq_staff", _ => true), Is.False);
    }

    [TestCase("", 6, true)]
    [TestCase("0", 6, true)]
    [TestCase("1|6", 6, true)]
    [TestCase("1;2", 6, false)]
    [TestCase("2,6", 6, true)]
    public void AllowedClassesOfAChosenReward(string allowed, int classId, bool mayHave)
    {
        Assert.That(ClassicQuests.ClassMayHave(allowed, classId), Is.EqualTo(mayHave));
    }

    [Test]
    public void ChooseAndTradeTargetsAreTalkTargets()
    {
        var names = ClassicQuests.TalkTargetsOf(Parse(TwoChooseSteps));
        Assert.That(names.Contains("edric") && names.Contains("Vismer"), Is.True, "case does not matter, as for NPC names");
        Assert.That(ClassicQuests.TalkTargetsOf(Parse("""{ "Quests": { "1": { "Steps": [ null, { "Custom": { "Kind": "travel", "Target": "x" } } ] } } }""")), Is.Empty);
    }

    [TestCase(3, false)]   // delivering to him is the talk step itself (the per-step truce covers it)
    [TestCase(4, true)]    // hunting the hill hound: he waits
    [TestCase(5, false)]   // the step that targets him: the fight is on
    [TestCase(2, false)]   // another NPC's delivery
    public void EssenceOfHostilityTheFallenOneWaitsForTheHillHound(int step, bool truce)
    {
        // 20078: kill Rolling Stone, deliver to Guardian Andraste, deliver to Fallen One, kill hill hound, kill Fallen One
        var steps = new (DataQuest.eStepType, string, ushort)[]
        {
            (DataQuest.eStepType.Kill, "Rolling Stone", 200), (DataQuest.eStepType.Deliver, "Guardian Andraste", 200),
            (DataQuest.eStepType.Deliver, "Fallen One", 200), (DataQuest.eStepType.Kill, "hill hound", 200),
            (DataQuest.eStepType.KillFinish, "Fallen One", 200),
        };
        (DataQuest.eStepType Type, string Target, ushort Region)? At(int s) => s >= 1 && s <= steps.Length ? steps[s - 1] : null;
        Assert.That(QuestTalkTruce.TalkedToBeforeThisHunt(At, step, "Fallen One", 200), Is.EqualTo(truce));
    }

    [Test]
    public void CollectRewardsParseAndRollByWeight()
    {
        var config = Parse("""
        { "Quests": { "117": { "Steps": [ null ], "CollectRewards": { "XpWeight": 50, "MoneyWeight": 30, "MoneyMin": 4000, "MoneyMax": 6000,
            "ItemWeight": 15, "Items": [ "gem_a", "gem_b" ], "HostileWeight": 5, "HostileName": "freed spirit", "HostileLevel": 30 } } } }
        """);
        var rewards = config.Quests[117].CollectRewards;
        Assert.Multiple(() =>
        {
            Assert.That(rewards.TotalWeight, Is.EqualTo(100));
            Assert.That(rewards.Pick(0), Is.EqualTo(ClassicQuests.CollectRewards.Kind.Xp));
            Assert.That(rewards.Pick(49), Is.EqualTo(ClassicQuests.CollectRewards.Kind.Xp));
            Assert.That(rewards.Pick(50), Is.EqualTo(ClassicQuests.CollectRewards.Kind.Money));
            Assert.That(rewards.Pick(80), Is.EqualTo(ClassicQuests.CollectRewards.Kind.Item));
            Assert.That(rewards.Pick(95), Is.EqualTo(ClassicQuests.CollectRewards.Kind.Hostile));
            Assert.That(rewards.Items, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void ACollectRewardWithNothingToGiveHasNoWeight()
    {
        var rewards = new ClassicQuests.CollectRewards { XpWeight = 10, ItemWeight = 90, HostileWeight = 50 };   // no items, no hostile name
        Assert.That(rewards.TotalWeight, Is.EqualTo(10));
        Assert.That(rewards.Pick(5), Is.EqualTo(ClassicQuests.CollectRewards.Kind.Xp));
        Assert.That(new ClassicQuests.CollectRewards().TotalWeight, Is.Zero);
    }

    [TestCase(0, false)]
    [TestCase(3, true)]
    [TestCase(35, true)]
    [TestCase(41, false)]
    [TestCase(42, true)]
    [TestCase(45, true)]
    public void GearIsWeaponsArmourShieldsAndInstruments(int objectType, bool gear)
    {
        Assert.That(ClassicQuests.IsGear(objectType), Is.EqualTo(gear));
    }
}
