using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review M3: a horse landing where bots of one level band keep dying is skipped by that band for now.</summary>
[TestFixture]
public sealed class UT_StableLandingDeaths
{
    [Test]
    public void DeadlyLandingNeedsSixDifferentBotsWithinAnHour()
    {
        long now = 10 * 60 * 60_000L;
        var five = Enumerable.Range(1, 5).Select(i => ((long)i, now - i * 60_000L)).ToList();
        Assert.That(AutonomousStableLandingWatch.IsDeadly(five, now), Is.False);
        five.Add((5, now - 1_000));   // the same bot again
        Assert.That(AutonomousStableLandingWatch.IsDeadly(five, now), Is.False);
        five.Add((6, now - 2_000));
        Assert.That(AutonomousStableLandingWatch.IsDeadly(five, now), Is.True);
        Assert.That(AutonomousStableLandingWatch.IsDeadly(five, now + AutonomousStableLandingWatch.DeadlyLandingWindowMilliseconds), Is.False,
            "the deaths age out");
        Assert.That(AutonomousStableLandingWatch.LevelBand(14), Is.EqualTo(AutonomousStableLandingWatch.LevelBand(10)));
        Assert.That(AutonomousStableLandingWatch.LevelBand(15), Is.Not.EqualTo(AutonomousStableLandingWatch.LevelBand(14)));
    }
}
