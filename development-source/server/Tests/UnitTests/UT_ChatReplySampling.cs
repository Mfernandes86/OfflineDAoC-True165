using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review Q30: chat replies score a bounded sample of the realm, most relevant first.</summary>
[TestFixture]
public sealed class UT_ChatReplySampling
{
    [Test]
    public void AtMostTheSampleIsScored()
    {
        int scored = 0;
        string[] ranked = AutonomousBotChatCoordinator.RankReplyCandidates(
            Enumerable.Range(0, 2_000).Select(i => $"bot{i}"), "anyone hunting toads", new Random(5),
            name => { scored++; return name; });
        Assert.That(ranked.Length, Is.EqualTo(AutonomousBotChatCoordinator.ReplyCandidateSample));
        Assert.That(scored, Is.EqualTo(AutonomousBotChatCoordinator.ReplyCandidateSample));
        Assert.That(ranked.Distinct().Count(), Is.EqualTo(ranked.Length));
    }

    [Test]
    public void RelevantCandidatesComeFirst()
    {
        string[] ranked = AutonomousBotChatCoordinator.RankReplyCandidates(
            new[] { "Resting in town", "Hunting hill toads in Lough Derg", "Traveling to Tir na Nog" },
            "Anyone hunting toads?", new Random(1), text => text);
        Assert.That(ranked[0], Is.EqualTo("Hunting hill toads in Lough Derg"));
    }

    [Test]
    public void MessageWordsAreTrimmedOnce()
    {
        string[] words = AutonomousBotChatCoordinator.RelevanceWords("Where are the 'toads', friend?");
        Assert.That(words, Is.EqualTo(new[] { "Where", "toads", "friend" }));
        Assert.That(AutonomousBotChatCoordinator.RelevanceScore("toads by the lake", words), Is.EqualTo(1));
    }
}
