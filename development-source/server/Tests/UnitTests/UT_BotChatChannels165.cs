using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Bot talk on the period channels instead of &lt;Faction&gt; (veteran-bots review M10 / rvr-social G1).</summary>
[TestFixture, NonParallelizable]
public class UT_BotChatChannels165
{
    private bool _was;

    [SetUp]
    public void SetUp() => _was = BotChatChannels165.PeriodChannels;

    [TearDown]
    public void TearDown() => BotChatChannels165.PeriodChannels = _was;

    [Test]
    public void LinesGoWhereA2003PlayerLookedForThem()
    {
        BotChatChannels165.PeriodChannels = true;
        Assert.That(BotChatChannels165.Classify("rvr_defense", "need ppl at the door", false), Is.EqualTo(BotChatChannels165.Channel.RvR));
        Assert.That(BotChatChannels165.Classify("trading", "anyone selling a bow", false), Is.EqualTo(BotChatChannels165.Channel.Trade));
        Assert.That(BotChatChannels165.Classify(null, "Good hunting, everyone.", true), Is.EqualTo(BotChatChannels165.Channel.RvR),
            "talk on the frontier is RvR talk");
        Assert.That(BotChatChannels165.Classify(null, "Safe roads and good hunting to everyone.", false),
            Is.Not.EqualTo(BotChatChannels165.Channel.Faction));
    }

    [Test]
    public void BusyZonesTalkMoreOftenQuietOnesKeepNinetySeconds()
    {
        Assert.That(AutonomousBotChatCoordinator.LocalIntervalMilliseconds(0), Is.EqualTo(90_000));
        Assert.That(AutonomousBotChatCoordinator.LocalIntervalMilliseconds(10), Is.EqualTo(90_000));
        Assert.That(AutonomousBotChatCoordinator.LocalIntervalMilliseconds(40), Is.EqualTo(60_000));
        Assert.That(AutonomousBotChatCoordinator.LocalIntervalMilliseconds(300), Is.EqualTo(30_000));
    }

    [Test]
    public void WithTheSwitchOffEverythingStaysOnFaction()
    {
        BotChatChannels165.PeriodChannels = false;
        Assert.That(BotChatChannels165.Classify("rvr_defense", "need ppl", true), Is.EqualTo(BotChatChannels165.Channel.Faction));
    }

    [Test]
    public void EachChannelUsesTheServersOwnFormatAndPacketType()
    {
        Assert.That(BotChatChannels165.Format(BotChatChannels165.Channel.RvR, "Aldric", eRealm.Albion, "mids inc"),
            Is.EqualTo("[Chat] Aldric: \"mids inc\""));
        Assert.That(BotChatChannels165.Format(BotChatChannels165.Channel.Trade, "Aldric", eRealm.Albion, "wts bow"),
            Is.EqualTo("[Broadcast] Aldric: wts bow"));
        Assert.That(BotChatChannels165.Format(BotChatChannels165.Channel.Advice, "Aldric", eRealm.Midgard, "where is Mularn?"),
            Is.EqualTo("[ADVICE MID] Aldric: where is Mularn?"));
        Assert.That(BotChatChannels165.Format(BotChatChannels165.Channel.Lfg, "Aldric", eRealm.Albion, "LF xp grp", 24, "Cleric"),
            Is.EqualTo("[LFG] Aldric (24, Cleric): LF xp grp"));
        Assert.That(BotChatChannels165.Format(BotChatChannels165.Channel.Faction, "Aldric", eRealm.Albion, "hi"),
            Is.EqualTo("(Aldric) <Faction> hi"), "the owner's /faction keeps its form");
        Assert.That(BotChatChannels165.TypeOf(BotChatChannels165.Channel.RvR), Is.EqualTo(eChatType.CT_Chat));
        Assert.That(BotChatChannels165.TypeOf(BotChatChannels165.Channel.Trade), Is.EqualTo(eChatType.CT_Broadcast));
        Assert.That(BotChatChannels165.TypeOf(BotChatChannels165.Channel.Advice), Is.EqualTo(eChatType.CT_Advise));
        Assert.That(BotChatChannels165.TypeOf(BotChatChannels165.Channel.Lfg), Is.EqualTo(eChatType.CT_LFG));
        Assert.That(BotChatChannels165.IsRegional(BotChatChannels165.Channel.Trade), Is.True);
        Assert.That(BotChatChannels165.IsRegional(BotChatChannels165.Channel.RvR), Is.False);
    }
}
