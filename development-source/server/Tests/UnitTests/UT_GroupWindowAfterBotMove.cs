using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>GitHub #49: a bot that comes back into the world under a new object id has the group window sent again, so
/// the player's client does not keep the old id for its row.</summary>
[TestFixture]
public sealed class UT_GroupWindowAfterBotMove
{
    [TestCase(true, (ushort)120, (ushort)345, true, true)]
    [TestCase(true, (ushort)120, (ushort)120, true, false)]
    [TestCase(true, (ushort)120, (ushort)345, false, false)]
    [TestCase(false, (ushort)120, (ushort)0, true, false)]
    public void ResentOnlyForAGroupedBotWhoseObjectIdChanged(bool moved, ushort before, ushort after, bool grouped, bool expected) =>
        Assert.That(GameBot.GroupWindowNeedsResendAfterMove(moved, before, after, grouped), Is.EqualTo(expected));
}
