using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Holders repair their keep's doors by the player rules (veteran-bots review M10 / rvr-social G8).</summary>
[TestFixture]
public class UT_KeepDoorRepair165
{
    [Test]
    public void ARepairCostsTheDoorLevelsWoodAtTheBoardPrice()
    {
        Assert.That(KeepDoorRepair165.RepairCost(5, 3), Is.EqualTo(RepairCommandHandler.GetNeededWoodForOneTick(5) * 3L));
        Assert.That(KeepDoorRepair165.RepairCost(1, 3), Is.EqualTo(6));
        Assert.That(KeepDoorRepair165.RepairShare, Is.EqualTo(0.05), "the same 5% a player's repair gives");
    }

    [Test]
    public void OnlyWoodworkersRepairAndTheyRollThePlayersChance() => Assert.Multiple(() =>
    {
        Assert.That(KeepDoorRepair165.CanRepair(0), Is.False, "an alchemist, spellcrafter, tailor or armourcrafter has no woodworking");
        Assert.That(KeepDoorRepair165.CanRepair(1), Is.True);
        // skill / ((door level + 1) * 50), the player's own roll
        Assert.That(RepairCommandHandler.RepairChance(250, 9), Is.EqualTo(0.5));
        Assert.That(RepairCommandHandler.RepairChance(500, 9), Is.EqualTo(1.0));
        Assert.That(RepairCommandHandler.RepairChance(100, 1), Is.EqualTo(1.0));
    });

    [Test]
    public void OnlyAHeldQuietKeepsDamagedDoorIsRepaired()
    {
        Assert.That(KeepDoorRepair165.NeedsRepair(60, eRealm.Albion, false, false), Is.True);
        Assert.That(KeepDoorRepair165.NeedsRepair(100, eRealm.Albion, false, false), Is.False);
        Assert.That(KeepDoorRepair165.NeedsRepair(60, eRealm.Albion, true, false), Is.False, "under attack");
        Assert.That(KeepDoorRepair165.NeedsRepair(60, eRealm.None, false, false), Is.False, "nobody holds it");
        Assert.That(KeepDoorRepair165.NeedsRepair(60, eRealm.Albion, false, true), Is.False, "portal keep");
    }
}
