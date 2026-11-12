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
}
