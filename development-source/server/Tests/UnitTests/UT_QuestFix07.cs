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
