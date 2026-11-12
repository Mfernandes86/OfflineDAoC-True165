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
}
