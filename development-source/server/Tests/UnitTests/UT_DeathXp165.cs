using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Owner decision O6: the player's death experience rule for solo gamebots, off by default.</summary>
[TestFixture]
public sealed class UT_DeathXp165
{
    [Test]
    public void SwitchIsOffByDefault()
    {
        Assert.That(DeathXp165.Enabled, Is.False);
    }

    [Test]
    public void LossFollowsThePlayerRule()
    {
        Assert.Multiple(() =>
        {
            // GamePlayer.Die: (50 - level) per mille, 10 from level 40; a third, then two thirds, then full
            Assert.That(DeathXp165.LossPerMille(20, 0), Is.EqualTo(30 / 3));
            Assert.That(DeathXp165.LossPerMille(20, 1), Is.EqualTo(30 * 2 / 3));
            Assert.That(DeathXp165.LossPerMille(20, 2), Is.EqualTo(30));
            Assert.That(DeathXp165.LossPerMille(45, 5), Is.EqualTo(10));
            long span = GamePlayer.GetExperienceAmountForLevel(20) - GamePlayer.GetExperienceAmountForLevel(19);
            Assert.That(DeathXp165.LossAtDeath(20, 2), Is.EqualTo(span * 30 / 1000));
            Assert.That(DeathXp165.LossAtDeath(50, 2), Is.Zero, "level 50 loses nothing to a level");
        });
    }

    [Test]
    public void LossNeverDropsALevelOrASecondStage()
    {
        long start20 = GamePlayer.GetExperienceAmountForLevel(19);
        Assert.That(DeathXp165.Floor(20, start20 + 5), Is.EqualTo(start20));
        long start42 = GamePlayer.GetExperienceAmountForLevel(41);
        long next42 = GamePlayer.GetExperienceAmountForLevel(42);
        long second = 1 + start42 + (next42 - start42) / 2;
        Assert.That(DeathXp165.Floor(42, second + 10), Is.EqualTo(second));
        Assert.That(DeathXp165.Floor(42, second - 10), Is.EqualTo(start42));
    }

    [Test]
    public void PrayerNeedsTheGraveWithin2000()
    {
        Assert.That(DeathXp165.AtGrave(100, 5000, 5000, 100, 6000, 6000), Is.True);
        Assert.That(DeathXp165.AtGrave(100, 5000, 5000, 100, 7100, 5000), Is.False);
        Assert.That(DeathXp165.AtGrave(100, 5000, 5000, 101, 5000, 5000), Is.False);
        Assert.That(DeathXp165.AtGrave(0, 0, 0, 0, 0, 0), Is.False, "no grave");
    }
}
