using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>The Hero's Stag form is drawn for gamebots and companions as for players (owner 2026-11-12).</summary>
[TestFixture]
public class UT_StagFormModel
{
    [Test]
    public void ALurikeenTakesTheSmallStagEveryOtherRaceTheStagMan() => Assert.Multiple(() =>
    {
        Assert.That(StagECSGameEffect.FormModel((int)eRace.Lurikeen), Is.EqualTo(13));
        Assert.That(StagECSGameEffect.FormModel((int)eRace.Celt), Is.EqualTo(4));
        Assert.That(StagECSGameEffect.FormModel((int)eRace.Firbolg), Is.EqualTo(4));
        Assert.That(StagECSGameEffect.FormModel((int)eRace.Unknown), Is.EqualTo(4));
    });

    [Test]
    public void NobodyMeansNoForm() => Assert.That(StagECSGameEffect.FormRace(null), Is.Null);
}
