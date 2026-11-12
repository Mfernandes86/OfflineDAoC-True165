using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Gamebots remember who killed them (veteran-bots review M10 / rvr-social G13).</summary>
[TestFixture]
public class UT_BotMemory165
{
    [Test]
    public void AGrudgeLastsTwoHours()
    {
        var grudge = new BotMemory165.Grudge { Name = "Aldric", Tick = 1_000 };
        Assert.That(BotMemory165.Fresh(grudge, 1_000 + BotMemory165.GrudgeMilliseconds - 1), Is.True);
        Assert.That(BotMemory165.Fresh(grudge, 1_000 + BotMemory165.GrudgeMilliseconds), Is.False);
        Assert.That(BotMemory165.Fresh(null, 0), Is.False);
    }

    [Test]
    public void TheCallOutNamesThePlayer()
    {
        for (int seed = 0; seed < 6; seed++)
        {
            string line = BotMemory165.CallOut("Aldric", "Infiltrator", "Pennine Mountains", new Random(seed));
            Assert.That(line, Does.Contain("Aldric"));
            Assert.That(line.Length, Is.LessThan(80));
        }
        Assert.That(BotMemory165.CallOut("Aldric", null, null, new Random(0)), Does.Contain("Aldric"));
    }

    [Test]
    public void ABotWithNoGrudgeHasNoKiller()
    {
        Assert.That(BotMemory165.Of(-424242), Is.Null);
        Assert.That(BotMemory165.IsKiller(null, null), Is.False);
    }
}
