using System.Linq;
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

    [Test]
    public void LeftoverQuestItemsAreTheStepPiecesButNoRewardOrSharedItem()
    {
        var steps = QuestLeftovers.ItemIds("cq_lirele_s_scroll|cq_damp_bone;50||cq_ring_reward|cq_shared_staff|plain_item")
            .Concat(QuestLeftovers.ItemIds("|cq_damp_bone"));
        var left = QuestLeftovers.LeftoverIds(steps, new[] { "cq_ring_reward", null }, id => id == "cq_shared_staff");
        Assert.That(left.OrderBy(x => x), Is.EqualTo(new[] { "cq_damp_bone", "cq_lirele_s_scroll" }));
    }

    [Test]
    public void AGuideIsLookedUpByRowFirst()
    {
        Assert.That(QuestGuide.RowKeys("Hibernia", 21510, "Last Heir", 47), Is.EqualTo(new[] { "#21510", "Hibernia|Last Heir|47" }));
    }

    [Test]
    public void ALaterPartDoesNotShowTheFirstPartsGuide()
    {
        // Last Heir rows at 43, 45 and 47; the guide by name is the level 43 walkthrough
        int[] levels = { 43, 45, 47 };
        Assert.That(QuestGuide.IsAnotherPartsGuide(43, 47, levels), Is.True);
        Assert.That(QuestGuide.IsAnotherPartsGuide(43, 43, levels), Is.False);
        Assert.That(QuestGuide.IsAnotherPartsGuide(null, 47, levels), Is.False, "no level: as before");
        Assert.That(QuestGuide.IsAnotherPartsGuide(45, 30, new[] { 30 }), Is.False, "a level no row of the name has");
    }

    [TestCase("\"Level\": 7", 7)]
    [TestCase("\"Level\": \"7\"", 7)]
    [TestCase("\"Level\": \"15-20\"", null)]
    [TestCase("\"Level\": null", null)]
    public void AGuideLevelNeverBreaksTheFile(string level, int? expected)
    {
        var guide = JsonSerializer.Deserialize<QuestGuide.Guide>("{ \"Name\": \"x\", " + level + ", \"Lines\": [] }");
        Assert.That(guide.Level, Is.EqualTo(expected));
    }

    [Test]
    public void AnEventSpawnCanBringAnEscort()
    {
        var config = Parse("""
        { "Quests": { "20377": { "Steps": [ null, { "Spawn": { "Name": "Golga", "TemplateId": 1, "Peaceful": true,
            "Escort": [ { "Name": "svartalf chanter", "TemplateId": 2, "Level": 20, "Count": 4 } ] } } ] } } }
        """);
        var spawn = config.Quests[20377].Steps[1].Spawn;
        Assert.That(spawn.Escort, Has.Count.EqualTo(1));
        Assert.That(spawn.Escort[0].Count, Is.EqualTo(4));
        Assert.That(spawn.Escort[0].Peaceful, Is.False);
        Assert.That(Parse("""{ "Quests": { "1": { "Steps": [ null, { "Spawn": { "Name": "x" } } ] } } }""").Quests[1].Steps[1].Spawn.Escort, Is.Null);
    }

    [TestCase("The Moonstone Twin", true)]
    [TestCase("the moonstone twin (level 47)", true)]
    [TestCase("The Moonstone Twin (level 45)", true)]
    [TestCase("The Moonstone Twin (Level 50 Path of Essence Epic)", false)]
    [TestCase("The Moonstone Twins", false)]
    [TestCase("Seek the Moonstone", false)]
    public void APartOfAnEpicLineIsItsNameOrItsNameWithALevel(string rowName, bool part)
    {
        Assert.That(Epics165.IsPartOf(rowName, "The Moonstone Twin"), Is.EqualTo(part));
    }

    [Test]
    public void AFinaleIsTitledWithLevel50()
    {
        Assert.That(Epics165.FinaleTitle("Saving the Clan"), Is.EqualTo("Saving the Clan (level 50)"));
    }

    [TestCase("gray wolf pup", true)]
    [TestCase("Black Wolf Pup", true)]
    [TestCase("werewolf", false)]
    [TestCase("giant wolf", false)]
    [TestCase(null, false)]
    public void WolfPeltCloakCountsOnlyWolfPups(string name, bool counts)
    {
        Assert.That(DOL.GS.Quests.Albion.WolfPeltCloak.IsWolfPup(name), Is.EqualTo(counts));
    }

    [TestCase(8, 8, 16, true)]
    [TestCase(15, 8, 16, true)]
    [TestCase(16, 8, 16, false)]
    [TestCase(23, 16, 24, true)]
    [TestCase(0, 16, 24, false)]
    [TestCase(0, 0, 8, true)]
    [TestCase(2, 22, 4, true)]
    [TestCase(12, 22, 4, false)]
    public void SkullPostHoursRotate(int hour, int start, int end, bool shown)
    {
        Assert.That(HourWindows.Contains(hour, start, end), Is.EqualTo(shown));
    }

    [Test]
    public void TheSkullPostSpawnsEachHaveAThirdOfTheDay()
    {
        Assert.That(HourWindows.TryGet("thraxia", out var t) && t == (8, 16), Is.True);
        Assert.That(HourWindows.TryGet("Terje", out var te) && te == (16, 24), Is.True);
        Assert.That(HourWindows.TryGet("Lord Lauur", out var l) && l == (0, 8), Is.True);
        Assert.That(HourWindows.TryGet("anyone else", out _), Is.False);
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
