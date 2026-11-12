using System.Collections.Generic;
using System.Text.Json;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// The quest features the 0.7 audit could not write as data (owner 2026-11-11: "make those now"): a whisper step with a
/// choice of words, side turn-ins, steps that need worn items, kill steps that count only the quest's own monster, event
/// monsters that walk a path, and what is remembered across restarts.
/// </summary>
[TestFixture]
public class UT_QuestFeatures07
{
    private static ClassicQuests.Config Parse(string json) =>
        JsonSerializer.Deserialize<ClassicQuests.Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    [Test]
    public void TheNewPropertiesAreRead()
    {
        ClassicQuests.Config config = Parse("""
            {"Quests": {"20001": {
                "Steps": [null,
                    {"Custom": {"Kind": "whisper", "Target": "Arawn Commander", "Words": "knowledge|power"}},
                    {"Wearing": ["relic_a", "relic_b"], "OwnSpawnOnly": true,
                     "Spawn": {"Name": "Cadoc", "Region": 200, "X": 1, "Y": 2, "Z": 3, "PathId": "river_road",
                               "Escort": [{"Name": "steed", "Count": 2}]}}],
                "SideTurnIns": [{"Item": "head", "Target": "Kimmy Glaze", "Reward": "jewel", "FromStep": 2, "ToStep": 5,
                                 "DropFrom": "Lieutenant Zudd", "DropChance": 50, "Thanks": "Well done."}]}},
             "HourWindows": {"Commander Slor'vid": {"Start": 0, "End": 3}}}
            """);
        ClassicQuests.QuestInfo info = config.Quests[20001];
        Assert.Multiple(() =>
        {
            Assert.That(info.Steps[1].Custom.Words, Is.EqualTo("knowledge|power"));
            Assert.That(info.Steps[2].Wearing, Is.EqualTo(new[] { "relic_a", "relic_b" }));
            Assert.That(info.Steps[2].OwnSpawnOnly, Is.True);
            Assert.That(info.Steps[2].Spawn.PathId, Is.EqualTo("river_road"));
            Assert.That(info.Steps[2].Spawn.Escort[0].Count, Is.EqualTo(2));
            Assert.That(info.SideTurnIns[0].DropFrom, Is.EqualTo("Lieutenant Zudd"));
            Assert.That(info.SideTurnIns[0].DropChance, Is.EqualTo(50));
            Assert.That(config.HourWindows["Commander Slor'vid"].End, Is.EqualTo(3));
            Assert.That(info.Steps[1].OwnSpawnOnly, Is.False, "off unless asked for");
            Assert.That(info.Steps[1].Wearing, Is.Null);
        });
    }

    [Test]
    public void AWhisperStepTakesAnyOfItsWords() => Assert.Multiple(() =>
    {
        Assert.That(ClassicQuests.WhisperOptions("knowledge|power"), Is.EqualTo(new[] { "knowledge", "power" }));
        Assert.That(ClassicQuests.WhisperChoice("knowledge|power", "Power"), Is.EqualTo("power"));
        Assert.That(ClassicQuests.WhisperChoice("knowledge|power", "knowledge."), Is.EqualTo("knowledge"), "a click or whisper may end in a full stop");
        Assert.That(ClassicQuests.WhisperChoice("knowledge|power", "glory"), Is.Null);
        Assert.That(ClassicQuests.WhisperChoice("", "anything"), Is.Null);
        Assert.That(ClassicQuests.WhisperChoice(null, null), Is.Null);
    });

    [Test]
    public void TheNpcShowsEveryWordAsALink() => Assert.Multiple(() =>
    {
        Assert.That(ClassicQuests.WhisperChoicePrompt("Answer the riddle: [knowledge] or [power]. (whisper: knowledge)", "knowledge|power"),
            Is.EqualTo("Answer the riddle: [knowledge] or [power]."), "the journal already links both; the generator's note goes");
        Assert.That(ClassicQuests.WhisperChoicePrompt("Answer the Arawn Commander's riddle.", "knowledge|power"),
            Is.EqualTo("Answer the Arawn Commander's riddle. [knowledge] or [power]"));
        Assert.That(ClassicQuests.WhisperChoicePrompt("Say [knowledge] if you dare.", "knowledge|power"),
            Is.EqualTo("Say [knowledge] if you dare. [power]"), "only the missing word is added");
        Assert.That(ClassicQuests.WhisperChoicePrompt(null, "knowledge|power"), Is.EqualTo("[knowledge] or [power]"));
    });

    [Test]
    public void AStepThatNeedsWornItemsNamesTheFirstOneMissing()
    {
        var worn = new HashSet<string> { "relic_a", "relic_c" };
        Assert.Multiple(() =>
        {
            Assert.That(ClassicQuests.MissingWorn(new[] { "relic_a", "relic_b", "relic_c" }, worn.Contains), Is.EqualTo("relic_b"));
            Assert.That(ClassicQuests.MissingWorn(new[] { "relic_a", "relic_c" }, worn.Contains), Is.Null, "all worn: the step goes on");
            Assert.That(ClassicQuests.MissingWorn(null, worn.Contains), Is.Null, "nothing asked for");
            Assert.That(ClassicQuests.MissingWorn(new[] { "", null }, worn.Contains), Is.Null);
        });
    }

    [Test]
    public void OnlyTheQuestsOwnMonsterCountsWhenAsked() => Assert.Multiple(() =>
    {
        Assert.That(ClassicQuests.IsOwnSpawn("Playername"), Is.True, "an event monster (spawned for someone on the quest)");
        Assert.That(ClassicQuests.IsOwnSpawn(null), Is.False, "the permanent monster of the same name");
        Assert.That(ClassicQuests.IsOwnSpawn(""), Is.False);
        // a quest the file does not know, or a step without the flag, counts every kill as before
        Assert.That(ClassicQuests.KillCounts(-1, 1, null), Is.True);
    });

    [Test]
    public void ASideTurnInIsOpenOnlyInItsSteps()
    {
        var side = new ClassicQuests.SideTurnIn { Item = "head", FromStep = 2, ToStep = 5 };
        var toTheEnd = new ClassicQuests.SideTurnIn { Item = "head" };
        Assert.Multiple(() =>
        {
            Assert.That(ClassicQuests.SideTurnInOpen(side, 1), Is.False);
            Assert.That(ClassicQuests.SideTurnInOpen(side, 2), Is.True);
            Assert.That(ClassicQuests.SideTurnInOpen(side, 5), Is.True);
            Assert.That(ClassicQuests.SideTurnInOpen(side, 6), Is.False);
            Assert.That(ClassicQuests.SideTurnInOpen(toTheEnd, 1), Is.True, "from the first step");
            Assert.That(ClassicQuests.SideTurnInOpen(toTheEnd, 40), Is.True, "to the end of the quest");
            Assert.That(ClassicQuests.SideTurnInOpen(toTheEnd, 0), Is.False, "not before the quest is taken");
            Assert.That(ClassicQuests.SideTurnInOpen(null, 3), Is.False);
            Assert.That(toTheEnd.DropChance, Is.EqualTo(100));
        });
    }

    [Test]
    public void WhatIsRememberedSurvivesTheFile() => Assert.Multiple(() =>
    {
        string grant = ClassicQuests.GrantLine("char-guid-1", 21264, 6, "cq_ring_of_spun_silk");
        Assert.That(ClassicQuests.TryParseGrantLine(grant, out var g), Is.True);
        Assert.That(g, Is.EqualTo(("char-guid-1", 21264, 6, "cq_ring_of_spun_silk")));
        string side = ClassicQuests.SideLine("char-guid-1", 20640, "zudd_head");
        Assert.That(ClassicQuests.TryParseSideLine(side, out var s), Is.True);
        Assert.That(s, Is.EqualTo(("char-guid-1", 20640, "zudd_head")));
        Assert.That(ClassicQuests.TryParseGrantLine("broken", out _), Is.False);
        Assert.That(ClassicQuests.TryParseGrantLine("a|x|1|item", out _), Is.False);
        Assert.That(ClassicQuests.TryParseSideLine("a|1", out _), Is.False);
        Assert.That(ClassicQuests.TryParseSideLine(null, out _), Is.False);
    });

    [Test]
    public void ARealMidQuestItemIsGrantedOncePerQuest()
    {
        var given = new[] { ("char-1", 21264, 6, "cq_mid_ring_of_spun_silk") };
        Assert.Multiple(() =>
        {
            Assert.That(ClassicQuests.GrantedInQuest(given, "char-1", 21264, "CQ_MID_RING_OF_SPUN_SILK"), Is.True, "a later catch-up step does not give it again");
            Assert.That(ClassicQuests.GrantedInQuest(given, "char-1", 21264, "cq_mid_herbalist_s_hauberk_of_ivy"), Is.False);
            Assert.That(ClassicQuests.GrantedInQuest(given, "char-2", 21264, "cq_mid_ring_of_spun_silk"), Is.False, "another character");
            Assert.That(ClassicQuests.GrantedInQuest(given, "char-1", 21263, "cq_mid_ring_of_spun_silk"), Is.False, "another quest");
        });
    }

    [Test]
    public void AnEventMonsterOnAPathIsNotMadeHarmless()
    {
        // one whose template gives it no aggression still attacks whoever comes within reach of its walk
        Assert.That(ClassicQuests.RoutedAggroLevel, Is.GreaterThan(0));
        Assert.That(ClassicQuests.RoutedAggroRange, Is.EqualTo(500));
    }
}
