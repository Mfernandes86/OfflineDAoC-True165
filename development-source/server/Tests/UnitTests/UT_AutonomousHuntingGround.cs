using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review M3: solo hunting grounds (zone preference, moving up, area hunting, pull order, con
/// preference, binding near the ground, deadly horse landings).</summary>
[TestFixture]
public sealed class UT_AutonomousHuntingGround
{
    [Test]
    public void ZoneFactorKeepsTheBotInItsGround()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousHuntingGround.ZoneFactor(0, 0, 200, 5, false), Is.EqualTo(1), "no ground yet: unchanged");
            Assert.That(AutonomousHuntingGround.ZoneFactor(200, 5, 200, 5, false), Is.EqualTo(8));
            Assert.That(AutonomousHuntingGround.ZoneFactor(200, 5, 200, 6, true), Is.EqualTo(3));
            Assert.That(AutonomousHuntingGround.ZoneFactor(200, 5, 200, 9, false), Is.EqualTo(1));
            Assert.That(AutonomousHuntingGround.ZoneFactor(200, 5, 1, 5, true), Is.EqualTo(0.25));
        });
    }

    [Test]
    public void MostlyGreenKillsMoveTheBotUp()
    {
        int fill = 0;
        byte bits = 0;
        bool[] kills = { true, false, true, true, true, false, true, true };
        foreach (bool green in kills)
        {
            Assert.That(AutonomousHuntingGround.ShouldMoveUp(fill, bits), Is.False);
            (fill, bits) = AutonomousHuntingGround.AddKill(fill, bits, green);
        }
        Assert.That(AutonomousHuntingGround.ShouldMoveUp(fill, bits), Is.True, "6 of the last 8 were green");
        // a ninth, blue kill pushes out the oldest green: 5 of 8
        (fill, bits) = AutonomousHuntingGround.AddKill(fill, bits, false);
        Assert.That(AutonomousHuntingGround.ShouldMoveUp(fill, bits), Is.False);
    }

    [Test]
    public void TooFewKillsNeverMoveUp()
    {
        int fill = 0;
        byte bits = 0;
        for (int i = 0; i < 7; i++)
            (fill, bits) = AutonomousHuntingGround.AddKill(fill, bits, true);
        Assert.That(AutonomousHuntingGround.ShouldMoveUp(fill, bits), Is.False);
    }

    [Test]
    public void AreaHuntingIsSoloOutdoorsOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, true, true, false, false), Is.True);
            Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, true, false, false, false), Is.False, "groups");
            Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, true, true, true, false), Is.False, "dungeons");
            Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, true, true, false, true), Is.False, "Darkness Falls");
            Assert.That(AutonomousHuntingGround.AreaHuntingApplies(true, false, true, false, false), Is.False, "normal mode");
            Assert.That(AutonomousHuntingGround.AreaHuntingApplies(false, true, true, false, false), Is.False, "switched off");
            Assert.That(AutonomousHuntingGround.WithinCampArea(1000, 1000, 1000 + 2600, 1000), Is.True);
            Assert.That(AutonomousHuntingGround.WithinCampArea(1000, 1000, 1000 + 2601, 1000), Is.False);
        });
    }

    [Test]
    public void TheMonsterStandingApartIsPulledFirst()
    {
        // candidate 0 stands in a pack of three, candidate 1 alone; the cheap order put the pack first
        Vector3[] candidates = { new(0, 0, 0), new(5000, 0, 0) };
        Vector3[] pool = { new(0, 0, 0), new(200, 0, 0), new(0, 250, 0), new(5000, 0, 0) };
        int[] counts = AutonomousHuntingGround.NeighbourCounts(candidates, new[] { 400, 400 }, pool);
        Assert.That(counts, Is.EqualTo(new[] { 2, 0 }));
        Assert.That(AutonomousHuntingGround.IsolationOrder(counts), Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public void IsolationOrderKeepsTheCheapOrderAmongEquals()
    {
        Assert.That(AutonomousHuntingGround.IsolationOrder(new[] { 1, 0, 1, 0, 2 }), Is.EqualTo(new[] { 1, 3, 0, 2, 4 }));
    }

    [Test]
    public void NonAggressiveMonstersStillCountTheirFriends()
    {
        int[] counts = AutonomousHuntingGround.NeighbourCounts(new[] { new Vector3(0, 0, 0) }, new[] { 0 },
            new[] { new Vector3(450, 0, 0), new Vector3(600, 0, 0) });
        Assert.That(counts[0], Is.EqualTo(1), "300 social reach + 200 path margin");
    }

    [Test]
    public void SupportClassesAndArchersPreferBlue()
    {
        foreach (eCharacterClass c in new[] { eCharacterClass.Shaman, eCharacterClass.Healer, eCharacterClass.Cleric,
                     eCharacterClass.Friar, eCharacterClass.Druid, eCharacterClass.Bard, eCharacterClass.Minstrel,
                     eCharacterClass.Scout, eCharacterClass.Hunter, eCharacterClass.Ranger })
            Assert.That(AutonomousHuntingGround.PreferredSoloCon(c), Is.EqualTo(ConColor.BLUE), c.ToString());
        foreach (eCharacterClass c in new[] { eCharacterClass.Thane, eCharacterClass.Skald, eCharacterClass.Armsman,
                     eCharacterClass.Wizard, eCharacterClass.Enchanter })
            Assert.That(AutonomousHuntingGround.PreferredSoloCon(c), Is.EqualTo(ConColor.YELLOW), c.ToString());
    }

    [Test]
    public void YellowPreferenceKeepsTheOldTargetSort()
    {
        foreach (ConColor con in new[] { ConColor.GREEN, ConColor.BLUE, ConColor.YELLOW, ConColor.ORANGE })
            Assert.That(AutonomousHuntingGround.ConSortPenalty(con, ConColor.YELLOW), Is.EqualTo(System.Math.Abs((int)con) * 220));
        Assert.That(AutonomousHuntingGround.ConSortPenalty(ConColor.BLUE, ConColor.BLUE), Is.Zero);
        Assert.That(AutonomousHuntingGround.ConSortPenalty(ConColor.YELLOW, ConColor.BLUE), Is.EqualTo(220));
    }

    [Test]
    public void RebindOnlyAtTheVisitedTownWhenMuchNearer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousHuntingGround.ShouldRebind(500, 8_000, 60_000), Is.True);
            Assert.That(AutonomousHuntingGround.ShouldRebind(500, 8_000, double.PositiveInfinity), Is.True, "bound in another region");
            Assert.That(AutonomousHuntingGround.ShouldRebind(3_500, 8_000, 60_000), Is.False, "the stone is not in this town");
            Assert.That(AutonomousHuntingGround.ShouldRebind(500, 41_000, double.PositiveInfinity), Is.False, "still far");
            Assert.That(AutonomousHuntingGround.ShouldRebind(500, 8_000, 12_000), Is.False, "not 5,000 nearer");
            Assert.That(AutonomousHuntingGround.ShouldRebind(500, 20_000, 30_000), Is.False, "not under 60%");
        });
    }
}
