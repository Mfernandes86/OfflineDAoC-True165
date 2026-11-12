using System.Reflection;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Relics lane hooks in AutonomousRvrEventLayer.cs (ChooseCore): an open relic keep is taken without the old 12 % roll
/// (B3) and a relic road struggle holds back only the realms in it (L6). Needs the hooks patch applied.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RelicLaneHooks
{
    [SetUp] public void ResetEvents() => RvrEventTestState.Clear();
    [TearDown] public void ClearEvents() => RvrEventTestState.Clear();

    private static AutonomousRvrEventLayer.Force Warband(string id, eRealm realm) =>
        new(id, realm, 8, 50, 2, MemberIds: [1, 2, 3, 4, 5, 6, 7, 8]);

    private static void SiegeRollReady()
    {
        typeof(AutonomousRvrEventLayer).GetField("_siegeStartReady", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
    }

    [Test]
    public void AnOpenRelicKeepIsTheRealmsNextSiegeEveryTime()
    {
        var keep = new AutonomousRvrEventLayer.LiveObjective("keep-a", "Dun Bolg", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 200, 1000, 1000, 0, false, 0, 0, 0, 0);
        var relic = new AutonomousRvrEventLayer.LiveObjective("relic-a", "Dun Lamfhota", AutonomousRvrEventLayer.Intent.AssaultRelicKeep,
            eRealm.Hibernia, 200, 5000, 5000, 0, true, 0, 0, 0, 0);
        for (int i = 0; i < 20; i++)
        {
            RvrEventTestState.Clear();
            AutonomousRvrEventLayer.SiegeScheduling = true;
            SiegeRollReady();
            var plan = AutonomousRvrEventLayer.ChooseOrJoin(Warband($"alb-{i}", eRealm.Albion), [keep, relic], 1_000 + i, 0.99);
            Assert.That(plan?.TargetId, Is.EqualTo("relic-a"), $"decision {i}");
        }
    }

    [Test]
    public void ARelicRoadStruggleDoesNotHoldBackAnUninvolvedRealm()
    {
        var hibKeep = new AutonomousRvrEventLayer.LiveObjective("rvr-keep-990", "Dun Crimthain", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 200, 1000, 1000, 0, false, 0, 0, 0, 0);
        Assert.That(AutonomousRvrEventLayer.ForceStart(hibKeep, eRealm.Albion, 10, out _), Is.True);
        AutonomousRvrEventLayer.TransferToRelicCarrier(hibKeep.Id, "rvr-relic-carrier-3-1", 11);
        var albKeep = new AutonomousRvrEventLayer.LiveObjective("keep-alb", "Caer Benowyc", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Albion, 1, 2000, 2000, 0, false, 0, 0, 0, 0);
        var midPlan = AutonomousRvrEventLayer.ChooseOrJoin(Warband("mid-1", eRealm.Midgard), [albKeep], 12, 0);
        Assert.That(midPlan?.TargetId, Is.EqualTo("keep-alb"), "Midgard is not in the struggle for Hibernia's relic");
        var midKeep = new AutonomousRvrEventLayer.LiveObjective("keep-mid", "Bledmeer Faste", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Midgard, 100, 3000, 3000, 0, false, 0, 0, 0, 0);
        var hibPlan = AutonomousRvrEventLayer.ChooseOrJoin(Warband("hib-1", eRealm.Hibernia), [midKeep], 13, 0);
        Assert.That(hibPlan?.TargetId, Is.Not.EqualTo("keep-mid"), "Hibernia, robbed, opens no new siege");
    }
}
