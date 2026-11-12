using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// owner 2026-11-12 (Summoner's Hall audit, defect 6): a whole path that fills the 256-point buffer before its end comes
    /// back from Detour as plain success; the corridor check must follow it on, not call it "no corridor".
    /// </summary>
    [TestFixture]
    public class UT_TruncatedPathCorridor
    {
        [Test]
        public void FullBufferWithoutPartialFlagIsTruncated()
        {
            Assert.That(LocalPathfindingMgr.IsTruncated(256, 256, partial: false), Is.True);
            Assert.That(LocalPathfindingMgr.IsTruncated(255, 256, partial: false), Is.False, "room left: the path ended");
            Assert.That(LocalPathfindingMgr.IsTruncated(256, 256, partial: true), Is.False, "partial already says it");
            Assert.That(LocalPathfindingMgr.IsTruncated(12, 256, partial: true), Is.False);
        }

        [Test]
        public void CorridorFollowsPartialAndTruncatedPathsOnly()
        {
            Assert.That(AutonomousZoneItinerary.ContinuesCorridor(new(PathfindingStatus.PartialPathFound, 40)), Is.True);
            Assert.That(AutonomousZoneItinerary.ContinuesCorridor(new(PathfindingStatus.PathFound, 250, truncated: true)), Is.True);
            Assert.That(AutonomousZoneItinerary.ContinuesCorridor(new(PathfindingStatus.PathFound, 30)), Is.False,
                "a whole path that ends away from the goal is still no corridor");
            Assert.That(AutonomousZoneItinerary.ContinuesCorridor(new(PathfindingStatus.NoPathFound, 0)), Is.False);
        }

        [Test]
        public void OldResultsAreNeverTruncated() =>
            Assert.That(new PathfindingResult(PathfindingStatus.PathFound, 256).Truncated, Is.False);
    }
}
