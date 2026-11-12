using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// owner 2026-11-12: the ground a zone-chain plan gives its start and goal. Delling Crater (zone 153, region 151): the
    /// Tuscaran Glacier door at 398621,271246,8575 stands on the main ground 69799, but its nearest road anchors belong to a
    /// stacked floor piece 11388 that has no listed zone crossing. Distances are the roads.json ones (height counts double).
    /// </summary>
    [TestFixture]
    public class UT_ConnectedGoalArea
    {
        private const int Main = 69799, Island = 11388, East = 80335;
        private static readonly HashSet<int> Crossed = [Main, East];
        private static bool InGraph(int area) => Crossed.Contains(area);

        private static readonly List<(float, int)> TuscaranDoor =
            [(680, Island), (1_115, Island), (1_422, Main), (1_490, Main), (2_400, Island), (3_125, East)];

        [Test]
        public void DoorNextToAFloorPieceWithoutCrossingsTakesTheConnectedGround() =>
            Assert.That(AutonomousSeamChains.ConnectedArea(TuscaranDoor, InGraph), Is.EqualTo(Main));

        [Test]
        public void NearestConnectedGroundIsKept() =>
            Assert.That(AutonomousSeamChains.ConnectedArea([(400, East), (500, Main)], InGraph), Is.EqualTo(East));

        [Test]
        public void SingleCandidateIsKeptEvenWithoutCrossings()
        {
            Assert.That(AutonomousSeamChains.ConnectedArea([(700, Island)], InGraph), Is.EqualTo(Island));
            Assert.That(AutonomousSeamChains.ConnectedArea([(700, Main)], InGraph), Is.EqualTo(Main));
        }

        [Test]
        public void NoCandidateGivesNoAnswerSoTheOldFallbackApplies()
        {
            Assert.That(AutonomousSeamChains.ConnectedArea([], InGraph), Is.Null);
            Assert.That(AutonomousSeamChains.ConnectedArea(null, InGraph), Is.Null);
        }

        [Test]
        public void ConnectedGroundFarBeyondTheReachDoesNotRelabel() =>
            Assert.That(AutonomousSeamChains.ConnectedArea([(300, Island), (300 + AutonomousSeamChains.ConnectedAreaReach + 1, Main)], InGraph),
                Is.EqualTo(Island));

        [Test]
        public void WithoutAreaDataTheNearestAnchorDecidesAsBefore()
        {
            Assert.That(AutonomousSeamChains.ConnectedArea(TuscaranDoor, _ => false), Is.EqualTo(Island));
            Assert.That(AutonomousSeamChains.ConnectedArea(TuscaranDoor, null), Is.EqualTo(Island));
        }

        [Test]
        public void ConfidentGoalGroundIgnoresTheFloorPieceWithoutCrossings()
        {
            var nodes = new HashSet<(ushort, int)> { (153, Main), (153, East) };
            List<(float, int)> nearby = [(680, Island), (1_115, Island), (1_422, Main), (1_490, Main)];
            Assert.That(AutonomousSeamChains.ConfidentArea(new List<(float, int)>(nearby)), Is.EqualTo(Island), "today: the floor piece, which no crossing reaches");
            Assert.That(AutonomousSeamChains.ConfidentArea(AutonomousSeamChains.Connected(nearby, nodes, 153)), Is.EqualTo(Main));
        }

        [Test]
        public void ConfidentGoalGroundUnchangedWithoutAreaData()
        {
            List<(float, int)> nearby = [(680, Island), (1_115, Island)];
            Assert.That(AutonomousSeamChains.ConfidentArea(AutonomousSeamChains.Connected(nearby, [], 153)), Is.EqualTo(Island));
        }
    }
}
