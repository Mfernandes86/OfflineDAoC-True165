using System.Linq;
using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Gamebot emotes on kills, captures and waves (veteran-bots review M10 / rvr-social G3).</summary>
[TestFixture]
public class UT_BotEmotes165
{
    [Test]
    public void MostBotsStayQuietOverAKillAFewSaluteOrMock()
    {
        var manners = Enumerable.Range(1, 10_000).Select(id => BotEmotes165.MannerOf(id)).ToList();
        int polite = manners.Count(m => m == BotEmotes165.Manner.Polite);
        int rude = manners.Count(m => m == BotEmotes165.Manner.Rude);
        Assert.That(polite, Is.InRange(400, 800));
        Assert.That(rude, Is.InRange(400, 800));
        Assert.That(BotEmotes165.MannerOf(42), Is.EqualTo(BotEmotes165.MannerOf(42)), "a bot keeps its manner");
        Assert.That(BotEmotes165.KillEmote(BotEmotes165.Manner.Quiet, 0), Is.Null);
        Assert.That(BotEmotes165.KillEmote(BotEmotes165.Manner.Polite, 0), Is.EqualTo(eEmote.Salute));
        Assert.That(BotEmotes165.KillEmote(BotEmotes165.Manner.Rude, 1), Is.EqualTo(eEmote.Rude));
    }

    [Test]
    public void ABotAnswersAPlayersEmoteInKind()
    {
        Assert.That(BotEmotes165.AnswerTo(eEmote.Wave, BotEmotes165.Manner.Quiet), Is.EqualTo(eEmote.Wave));
        Assert.That(BotEmotes165.AnswerTo(eEmote.Bow, BotEmotes165.Manner.Quiet), Is.EqualTo(eEmote.Bow));
        Assert.That(BotEmotes165.AnswerTo(eEmote.Rude, BotEmotes165.Manner.Quiet), Is.EqualTo(eEmote.Laugh));
        Assert.That(BotEmotes165.AnswerTo(eEmote.Rude, BotEmotes165.Manner.Rude), Is.EqualTo(eEmote.Rude));
        Assert.That(BotEmotes165.AnswerTo(eEmote.Drink, BotEmotes165.Manner.Quiet), Is.Null);
    }
}
