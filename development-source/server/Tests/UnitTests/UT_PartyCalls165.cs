using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Party calls: pulling, oom, mezzing add, rez pls (veteran-bots review M10 / rvr-social G11).</summary>
[TestFixture]
public class UT_PartyCalls165
{
    [Test]
    public void OomIsCalledOnceWhenPowerCrossesFifteenPercent()
    {
        Assert.That(PartyCalls165.CrossedOom(20, 14), Is.True);
        Assert.That(PartyCalls165.CrossedOom(14, 10), Is.False, "already called on the way down");
        Assert.That(PartyCalls165.CrossedOom(50, 30), Is.False);
    }

    [Test]
    public void ResurrectingClassesAreKnown()
    {
        Assert.That(PartyCalls165.CanResurrect(eCharacterClass.Cleric), Is.True);
        Assert.That(PartyCalls165.CanResurrect(eCharacterClass.Druid), Is.True);
        Assert.That(PartyCalls165.CanResurrect(eCharacterClass.Wizard), Is.False);
    }

    [Test]
    public void CallsAreShort()
    {
        for (int seed = 0; seed < 8; seed++)
        {
            Assert.That(PartyCalls165.PullLine("a forest bear", new Random(seed)).Length, Is.LessThan(30));
            Assert.That(PartyCalls165.OomLine(new Random(seed)), Does.Contain("oom").Or.Contain("pow"));
        }
    }
}
