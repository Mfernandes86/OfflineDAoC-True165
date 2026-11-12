using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Battleground roamers move in squads (veteran-bots review M10 / rvr-social G7).</summary>
[TestFixture]
public class UT_BattlegroundSquads
{
    [Test]
    public void ARealmsRoamersSplitIntoFourSquads()
    {
        var sizes = Enumerable.Range(1, 4_000).GroupBy(id => BattlegroundSquads.SquadOf(id)).ToDictionary(g => g.Key, g => g.Count());
        Assert.That(sizes.Keys, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        Assert.That(sizes.Values.All(n => n > 700), Is.True, "squads are about even");
    }

    [Test]
    public void RoamersRepickSpreadOverTheWindowSquadBySquad()
    {
        var offsets = new HashSet<long>();
        for (int squad = 0; squad < BattlegroundSquads.Squads; squad++)
            for (long id = 0; id < 100_000; id += 37)
            {
                long offset = BattlegroundSquads.RepickOffsetMilliseconds(squad, id);
                Assert.That(offset, Is.InRange(squad * BattlegroundSquads.RepickSpreadMilliseconds,
                    (squad + 1) * BattlegroundSquads.RepickSpreadMilliseconds - 1), "each squad in its own slot");
                Assert.That(offset, Is.LessThan(BattlegroundSquads.WindowMilliseconds), "still inside the window: the squad agrees");
                offsets.Add(offset / 5_000);
            }
        Assert.That(offsets.Count, Is.GreaterThan(15), "not all in one 5-second burst");
    }

    [Test]
    public void ASquadsMembersShareTheirLandmarkAndItMovesOn()
    {
        int first = BattlegroundSquads.AnchorIndex(eRealm.Albion, 2, 100, 3);
        Assert.That(BattlegroundSquads.AnchorIndex(eRealm.Albion, 2, 100, 3), Is.EqualTo(first), "every member picks the same");
        var picks = new HashSet<int>();
        for (long window = 0; window < 50; window++)
            picks.Add(BattlegroundSquads.AnchorIndex(eRealm.Albion, 2, window, 3));
        Assert.That(picks.Count, Is.EqualTo(3), "over time it visits every landmark");
        Assert.That(BattlegroundSquads.AnchorIndex(eRealm.Albion, 2, 7, 1), Is.Zero);
    }
}
