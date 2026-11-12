using System.Collections.Generic;
using System.Linq;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Owner 2026-11-11: a quest target's patrol route is sent to the map as a line of marker points.</summary>
    [TestFixture]
    public class UT_QuestPatrolRoutes
    {
        [Test]
        public void RoutePointIdsSitBelowTheQuestGiverMarkersAndNeverCollide() => Assert.Multiple(() =>
        {
            Assert.That(QuestPatrolRoutes.PointId(20000, 0), Is.EqualTo(QuestPatrolRoutes.RouteMarkers));
            Assert.That(QuestPatrolRoutes.PointId(21540, QuestPatrolRoutes.PointsPerQuest - 1), Is.LessThan(0xFFFC0000u),
                "below the quest giver markers");
            Assert.That(QuestPatrolRoutes.PointId(20368, 47), Is.LessThan(QuestPatrolRoutes.PointId(20369, 0)),
                "one quest's points never reach the next quest's");
            Assert.That(QuestPatrolRoutes.MaxPoints, Is.LessThanOrEqualTo(QuestPatrolRoutes.PointsPerQuest));
            var ids = new HashSet<uint>();
            for (int quest = 20000; quest <= 21540; quest++)
                Assert.That(ids.Add(QuestPatrolRoutes.PointId(quest, 0)), Is.True, "every classic quest has its own slot");
        });

        [Test]
        public void ALongRouteIsThinnedKeepingItsEnds() => Assert.Multiple(() =>
        {
            List<int> route = Enumerable.Range(0, 200).ToList();
            List<int> thin = QuestPatrolRoutes.Thin(route, 48);
            Assert.That(thin, Has.Count.EqualTo(48));
            Assert.That(thin.First(), Is.EqualTo(0));
            Assert.That(thin.Last(), Is.EqualTo(199));
            Assert.That(thin, Is.Ordered);
            Assert.That(QuestPatrolRoutes.Thin(Enumerable.Range(0, 13).ToList(), 48), Has.Count.EqualTo(13), "a short route is sent whole");
            Assert.That(QuestPatrolRoutes.Thin(new List<int>(), 48), Is.Empty);
        });
    }
}
