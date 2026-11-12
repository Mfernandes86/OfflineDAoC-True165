using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, M5: one shared fight board per group, rebuilt at most once a second, with one area
    /// search per group at most every 2 s, instead of every member's own area searches.
    /// </summary>
    [TestFixture]
    public class UT_VeteranFightBoard
    {
        [Test]
        public void TheFightBoardIsRebuiltAtMostOnceASecond() => Assert.Multiple(() =>
        {
            Assert.That(GroupFightBoard.RebuildMilliseconds, Is.EqualTo(1_000));
            Assert.That(GroupFightBoard.NeedsRebuild(1_999, 1_000, true), Is.False);
            Assert.That(GroupFightBoard.NeedsRebuild(2_000, 1_000, true), Is.True);
            Assert.That(GroupFightBoard.NeedsRebuild(1_100, 1_000, false), Is.True, "another region");
            Assert.That(new GroupFightBoard { BuiltAt = 1_000, RegionId = 51 }.IsFresh(1_500, 51), Is.True);
            Assert.That(new GroupFightBoard { BuiltAt = 1_000, RegionId = 51 }.IsFresh(2_000, 51), Is.False);
            Assert.That(new GroupFightBoard { BuiltAt = 1_000, RegionId = 51 }.IsFresh(1_500, 52), Is.False);
        });

        [Test]
        public void OneAreaSearchPerGroupAtMostEveryTwoSeconds() => Assert.Multiple(() =>
        {
            Assert.That(GroupFightBoard.ScanRadius, Is.LessThanOrEqualTo(2_600), "never wider than the old protection search");
            Assert.That(GroupFightBoard.ScanIntervalMilliseconds, Is.EqualTo(2_000));
            Assert.That(GroupFightBoard.ScanIntervalMilliseconds, Is.GreaterThanOrEqualTo(GroupFightBoard.RebuildMilliseconds));
        });

        [Test]
        public void AnEmptyBoardHasNothingOnTheGroup()
        {
            var board = new GroupFightBoard();
            Assert.Multiple(() =>
            {
                Assert.That(board.MonstersOnGroup, Is.Zero);
                Assert.That(board.UncontrolledOnGroup, Is.Zero);
                Assert.That(board.NpcsOnGroup, Is.Empty);
                Assert.That(board.Threats, Is.Empty);
                Assert.That(board.ActiveTargets, Is.Empty);
                Assert.That(BotBrain.GetFightBoard(null), Is.Null);
            });
        }
    }
}
