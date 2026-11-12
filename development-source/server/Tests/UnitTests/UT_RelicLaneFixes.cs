using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Realm-event audit 2026-11-11, relics lane: the pure rules behind the relic raid, carry and placement fixes.</summary>
[TestFixture, NonParallelizable]
public sealed class UT_RelicLaneFixes
{
    private static AutonomousRelicShrine.RelicFact Relic(eRealm holder, eRealm original, eRelicType type, bool mounted = true) =>
        new(holder, original, type, mounted);

    [Test]
    public void ARelicRaidIsPlannedOnlyWhenItsPrizeCanBeLiftedAndPlaced()
    {
        // Starting world: every realm holds its own two relics.
        var start = new[]
        {
            Relic(eRealm.Albion, eRealm.Albion, eRelicType.Strength), Relic(eRealm.Albion, eRealm.Albion, eRelicType.Magic),
            Relic(eRealm.Midgard, eRealm.Midgard, eRelicType.Strength), Relic(eRealm.Midgard, eRealm.Midgard, eRelicType.Magic),
            Relic(eRealm.Hibernia, eRealm.Hibernia, eRelicType.Strength), Relic(eRealm.Hibernia, eRealm.Hibernia, eRelicType.Magic),
        };
        // Albion took Midgard's strength relic: Mjollner Faste's pad is empty, Castle Excalibur holds two.
        var afterRaid = new[]
        {
            Relic(eRealm.Albion, eRealm.Albion, eRelicType.Strength), Relic(eRealm.Albion, eRealm.Midgard, eRelicType.Strength),
            Relic(eRealm.Albion, eRealm.Albion, eRelicType.Magic), Relic(eRealm.Midgard, eRealm.Midgard, eRelicType.Magic),
            Relic(eRealm.Hibernia, eRealm.Hibernia, eRelicType.Strength), Relic(eRealm.Hibernia, eRealm.Hibernia, eRelicType.Magic),
        };
        bool Room(eRelicType _) => true;
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRelicShrine.RaidCanPlace(eRealm.Albion, [start[2]], start, Room), Is.True, "Albion on Mjollner Faste");
            // Midgard lost its strength relic: Lug's Spear in Dun Lamfhota cannot be lifted by it (own-relic-missing at the pad).
            Assert.That(AutonomousRelicShrine.RaidCanPlace(eRealm.Midgard, [afterRaid[4]], afterRaid, Room), Is.False, "Midgard on Dun Lamfhota");
            // ... but it may always take back its own Thor's Hammer from Castle Excalibur.
            Assert.That(AutonomousRelicShrine.RaidCanPlace(eRealm.Midgard, [afterRaid[0], afterRaid[1]], afterRaid, Room), Is.True, "Midgard retakes");
            // Hibernia still holds its own strength relic, so Albion's own Scabbard and Thor's Hammer there can be lifted.
            Assert.That(AutonomousRelicShrine.RaidCanPlace(eRealm.Hibernia, [afterRaid[0], afterRaid[1]], afterRaid, Room), Is.True);
            // An empty pad holds no prize.
            Assert.That(AutonomousRelicShrine.RaidCanPlace(eRealm.Hibernia, [], afterRaid, Room), Is.False, "empty Mjollner Faste");
            // A full home pad (three relics) takes nothing more.
            Assert.That(AutonomousRelicShrine.RaidCanPlace(eRealm.Albion, [start[2]], start, _ => false), Is.False, "home pad full");
            // A relic lying on the ground or already the attacker's is no prize on the shrine.
            Assert.That(AutonomousRelicShrine.RaidCanPlace(eRealm.Albion, [Relic(eRealm.Midgard, eRealm.Midgard, eRelicType.Strength, false)], start, Room), Is.False);
            Assert.That(AutonomousRelicShrine.RaidCanPlace(eRealm.Albion, [afterRaid[1]], afterRaid, Room), Is.False, "already Albion's");
        });
    }

    private static AutonomousRvrEventLayer.RelicCandidate Keep(string id, int sentinels, int withoutLord = 0, bool retake = false) =>
        new(id, sentinels, withoutLord, retake);

    [Test]
    public void AnOpenRelicKeepIsTheNextSiegeOwnLostRelicFirst()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.RelicKeepOpen(2), Is.True);
            Assert.That(AutonomousRvrEventLayer.RelicKeepOpen(3), Is.False);
            Assert.That(AutonomousRvrEventLayer.ChooseOpenRelic([Keep("a", 4), Keep("b", 3)]), Is.Null, "none open");
            Assert.That(AutonomousRvrEventLayer.ChooseOpenRelic([Keep("a", 4), Keep("b", 2)]), Is.EqualTo("b"), "taken at once, no 12 % roll");
            Assert.That(AutonomousRvrEventLayer.ChooseOpenRelic([Keep("a", 0), Keep("b", 2, retake: true)]), Is.EqualTo("b"), "own relic first");
            Assert.That(AutonomousRvrEventLayer.ChooseOpenRelic([Keep("b", 2), Keep("a", 1)]), Is.EqualTo("a"), "then the fewest sentinels");
        });
    }

    [Test]
    public void ARealmAimsAtTheRelicKeepItCanOpenWithTheFewestSentinelKeepsLeft()
    {
        Assert.Multiple(() =>
        {
            // Today's world (relic audit B1): Castle Excalibur has 6 sentinel keeps, 5 of them without a lord: it cannot open.
            Assert.That(AutonomousRvrEventLayer.RelicKeepOpenable(6, 5), Is.False);
            // Dun Lamfhota: 6 held, nGed and Crauchon have no lord: 4 can fall, 2 stay: it can open.
            Assert.That(AutonomousRvrEventLayer.RelicKeepOpenable(6, 2), Is.True);
            Assert.That(AutonomousRvrEventLayer.ChooseRelicAim([Keep("excalibur", 6, 5), Keep("lamfhota", 6, 2), Keep("dagda", 6, 3)]),
                Is.EqualTo("lamfhota"));
            // Once the garrisons are restored every keep has a lord: the fewest sentinel keeps left wins, a retake first.
            Assert.That(AutonomousRvrEventLayer.ChooseRelicAim([Keep("a", 6), Keep("b", 4)]), Is.EqualTo("b"));
            Assert.That(AutonomousRvrEventLayer.ChooseRelicAim([Keep("a", 6, retake: true), Keep("b", 4)]), Is.EqualTo("a"));
            // An open keep is no aim (it is sieged at once).
            Assert.That(AutonomousRvrEventLayer.ChooseRelicAim([Keep("a", 2)]), Is.Null);
            Assert.That(AutonomousRvrEventLayer.ChooseRelicAim([]), Is.Null);
        });
    }

    [Test]
    public void ASentinelKeepStepCountsAsTakenOnlyWhenItChangedHands()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.RelicStepTaken(eRealm.Hibernia, eRealm.Albion), Is.True);
            Assert.That(AutonomousRvrEventLayer.RelicStepTaken(eRealm.Hibernia, eRealm.Midgard), Is.True, "the third realm took it: still a sentinel down");
            Assert.That(AutonomousRvrEventLayer.RelicStepTaken(eRealm.Hibernia, eRealm.Hibernia), Is.False);
            Assert.That(AutonomousRvrEventLayer.RelicStepTaken(eRealm.Hibernia, eRealm.None), Is.False);
        });
    }

    [Test]
    public void ACampaignDoesNotGoBackToASentinelKeepItFailedToTake()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.SkipFailedStep(false, eRealm.Hibernia, eRealm.Hibernia), Is.True, "lost: skipped");
            Assert.That(AutonomousRvrEventLayer.SkipFailedStep(false, eRealm.Hibernia, eRealm.Albion), Is.False, "taken");
            Assert.That(AutonomousRvrEventLayer.SkipFailedStep(true, eRealm.Hibernia, eRealm.Hibernia), Is.False, "still under siege");
        });
    }

    [Test]
    public void ARelicRoadStruggleHasAHardEndThatGrowsOnlyWithRegionsCrossed()
    {
        long window = AutonomousRvrEventLayer.CarrierEventMilliseconds;
        long extension = AutonomousRvrEventLayer.CarrierCrossingExtensionMilliseconds;
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.CarrierHardEnd(1_000, 0), Is.EqualTo(1_000 + window));
            Assert.That(AutonomousRvrEventLayer.CarrierHardEnd(1_000, 1), Is.EqualTo(1_000 + window + extension), "the port home");
            Assert.That(AutonomousRvrEventLayer.CarrierHardEnd(1_000, 99),
                Is.EqualTo(1_000 + window + AutonomousRvrEventLayer.MaxCarrierCrossingExtensions * extension), "bounded");
        });
    }

    [Test]
    public void ARelicRoadStruggleHoldsBackOnlyTheSiegesOfTheRealmsInIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.CarrierBlocksSiege(eRealm.Albion, eRealm.Albion, eRealm.Hibernia, []), Is.True, "carrier");
            Assert.That(AutonomousRvrEventLayer.CarrierBlocksSiege(eRealm.Hibernia, eRealm.Albion, eRealm.Hibernia, []), Is.True, "robbed");
            Assert.That(AutonomousRvrEventLayer.CarrierBlocksSiege(eRealm.Midgard, eRealm.Albion, eRealm.Hibernia, []), Is.False, "uninvolved");
            Assert.That(AutonomousRvrEventLayer.CarrierBlocksSiege(eRealm.Midgard, eRealm.Albion, eRealm.Hibernia, [eRealm.Midgard]), Is.True, "intercepting");
            Assert.That(AutonomousRvrEventLayer.CarrierBlocksSiege(eRealm.Midgard, eRealm.None, eRealm.None, [eRealm.Albion]), Is.False);
        });
    }
}
