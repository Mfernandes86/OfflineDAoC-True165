using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Herald notices in veteran shorthand (veteran-bots review M10 / rvr-social G5).</summary>
[TestFixture]
public class UT_HeraldShorthand165
{
    private static AutonomousRvrEventLayer.BattleNotice Siege(bool started, long muster, int attackers) =>
        new("rvr-1", "SIEGE", eRealm.Midgard, eRealm.Albion, attackers, 10, 0, 64, 600_000, started, attackers, 10, 0, muster);

    [Test]
    public void KeepsGoByThePlayersNicknames()
    {
        Assert.That(HeraldShorthand165.Nickname("Caer Benowyc"), Is.EqualTo("Ben"));
        Assert.That(HeraldShorthand165.Nickname("Dun Crauchon"), Is.EqualTo("Crau"));
        Assert.That(HeraldShorthand165.Nickname("Bledmeer Faste"), Is.EqualTo("Bled"));
        Assert.That(HeraldShorthand165.Nickname("Albion Portal Keep"), Is.EqualTo("Albion Portal Keep"));
        Assert.That(HeraldShorthand165.ShortenNames("The army marches on Caer Benowyc! Form up."),
            Is.EqualTo("The army marches on Ben! Form up."));
        Assert.That(HeraldShorthand165.ShortenNames("Bledmeer Faste has fallen!"), Is.EqualTo("Bled has fallen!"));
    }

    [TestCase(2, "a few")]
    [TestCase(8, "1fg")]
    [TestCase(14, "2fg")]
    [TestCase(23, "~25")]
    [TestCase(60, "zerg")]
    public void HeadCountsAreRough(int count, string expected) =>
        Assert.That(HeraldShorthand165.RoughCount(count), Is.EqualTo(expected));

    [Test]
    public void DefendersHearWhoIsAtTheDoorAndHowMany()
    {
        string line = HeraldShorthand165.Rewrite(eRealm.Albion, "Caer Benowyc", Siege(true, -1, 22), new Random(1));
        Assert.That(line, Does.Contain("Ben"));
        Assert.That(line, Does.Contain("mids").Or.Contain("~20"));
        Assert.That(line.Length, Is.LessThan(60), "short, as a call on chat");
    }

    [Test]
    public void AttackersHearTheMusterAndTheMarch()
    {
        for (int seed = 0; seed < 6; seed++)
        {
            Assert.That(HeraldShorthand165.Rewrite(eRealm.Midgard, "Caer Benowyc", Siege(false, 30_000, 12), new Random(seed)),
                Does.Contain("Ben"));
            Assert.That(HeraldShorthand165.Rewrite(eRealm.Midgard, "Caer Benowyc", Siege(false, 0, 12), new Random(seed)),
                Does.Contain("Ben"));
        }
        Assert.That(HeraldShorthand165.Rewrite(eRealm.Hibernia, "Caer Benowyc", Siege(true, -1, 30), new Random(1)),
            Does.StartWith("mids hitting albs at Ben"));
        Assert.That(HeraldShorthand165.Rewrite(eRealm.Albion, "Caer Benowyc", null), Is.Null);
    }

    [Test]
    public void ARaidNoticeOrTheSwitchOffKeepsItsText()
    {
        Assert.That(HeraldShorthand165.ForNotice("raid-dragon", eRealm.Albion, "The raid gathers."), Is.EqualTo("The raid gathers."));
        bool was = HeraldShorthand165.Enabled;
        try
        {
            HeraldShorthand165.Enabled = false;
            Assert.That(HeraldShorthand165.ForNotice("rvr-5", eRealm.Albion, "Caer Benowyc is under siege!"),
                Is.EqualTo("Caer Benowyc is under siege!"));
        }
        finally
        {
            HeraldShorthand165.Enabled = was;
        }
    }
}
