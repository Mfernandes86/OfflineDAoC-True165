using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Bots answer live keep and relic questions (veteran-bots review M10 / rvr-social G10).</summary>
[TestFixture]
public class UT_BotRvrKnowledge165
{
    [TestCase("who has Ben?", true)]
    [TestCase("is crau under attack?", true)]
    [TestCase("where's the action?", true)]
    [TestCase("how many relics do we have?", true)]
    [TestCase("where is Caer Benowyc?", false)]       // way-finding: the location answer
    [TestCase("anyone want to group?", false)]
    [TestCase("Ben is a nice name", false)]           // not a question
    public void RvrQuestionsAreRecognised(string line, bool expected) =>
        Assert.That(BotRvrKnowledge165.LooksLikeRvrQuestion(line), Is.EqualTo(expected));

    [Test]
    public void KeepsAreFoundByNameOrNickname()
    {
        Assert.That(BotRvrKnowledge165.MentionedKeepWord("who has Ben?"), Is.EqualTo("Benowyc"));
        Assert.That(BotRvrKnowledge165.MentionedKeepWord("is Dun Crauchon safe?"), Is.EqualTo("Crauchon"));
        Assert.That(BotRvrKnowledge165.MentionedKeepWord("benefits of a heal?"), Is.Null, "a nickname counts only as a whole word");
    }

    [Test]
    public void AnswersReadLikeAVeteransShorthand()
    {
        Assert.That(BotRvrKnowledge165.KeepLine("Caer Benowyc", eRealm.Midgard, eRealm.Albion, true), Is.EqualTo("mids have Ben, under attack atm"));
        Assert.That(BotRvrKnowledge165.KeepLine("Caer Benowyc", eRealm.Albion, eRealm.Albion, false), Is.EqualTo("we have Ben"));
        string summary = BotRvrKnowledge165.Summary(eRealm.Albion, 2,
            new Dictionary<eRealm, int> { [eRealm.Albion] = 7, [eRealm.Midgard] = 9, [eRealm.Hibernia] = 8 },
            new List<string> { "mids hitting Crau" });
        Assert.That(summary, Is.EqualTo("mids hitting Crau, keeps: albs 7 mids 9 hibs 8, we have 2 relics"));
        Assert.That(BotRvrKnowledge165.Summary(eRealm.Albion, 1, new Dictionary<eRealm, int>(), new List<string>()),
            Is.EqualTo("we have 1 relic, quiet out there atm"));
    }
}

/// <summary>Guild hello and gratz lines (veteran-bots review M10 / rvr-social G9).</summary>
[TestFixture]
public class UT_BotGuildGratz165
{
    [Test]
    public void GratzNamesThePlayerOrTheLevelWhenItSaysEither()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            string line = AutonomousBotGuilds.GratzLine("Aldric", 24, new System.Random(seed));
            Assert.That(line, Does.Not.Contain("{"));
            Assert.That(line.ToLowerInvariant(), Does.StartWith("g"));
        }
        Assert.That(AutonomousBotGuilds.HelloChance, Is.EqualTo(0.3));
        Assert.That(AutonomousBotChatCoordinator.AdviceAnswerLevel, Is.EqualTo(20));
    }

/// <summary>Guild goodbye lines on a planned logout (veteran-bots review M10 / rvr-social G9).</summary>
[TestFixture]
public class UT_BotGuildBye165
{
    [Test]
    public void AThirdOfPlannedLogoutsSayGoodnightInGuildChat()
    {
        Assert.That(AutonomousBotGuilds.ByeChance, Is.EqualTo(0.3));
        for (int seed = 0; seed < 30; seed++)
        {
            string line = AutonomousBotGuilds.ByeLine(new System.Random(seed));
            Assert.That(line, Is.Not.Empty);
            Assert.That(line, Does.Not.Contain("{"));
        }
        Assert.DoesNotThrow(() => AutonomousBotGuilds.SayGoodbye(null));
    }
}
}
