using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Quest audit 2026-11-11: a chain whose parts share one name needs the part just before, not any part. The rows are
    /// the True 1.65 world's (DataQuest table).
    /// </summary>
    [TestFixture]
    public class UT_QuestDependencyRule
    {
        private static readonly QuestDependencyRule.Row[] Rows =
        {
            // Feast of the Decadent: 43 per class, 45 per class, 48 shared
            new(20392, "Feast of the Decadent", 43, new[] { 2 }),
            new(20393, "Feast of the Decadent", 43, new[] { 3 }),
            new(20407, "Feast of the Decadent", 45, new[] { 2 }),
            new(20408, "Feast of the Decadent", 45, new[] { 3 }),
            new(21287, "Feast of the Decadent", 48, new[] { 2, 3, 10, 5 }),
            // Legend of the Lake 15 / 20 / 25, then Hands Of Fate 30 depends on it by another name
            new(21288, "Legend of the Lake", 15, new[] { 2 }),
            new(20419, "Legend of the Lake", 20, new[] { 2 }),
            new(21476, "Legend of the Lake", 25, new[] { 2 }),
            // Immediate Resolution: three class-locked data quests, and Master Torr's scripted one for everybody
            new(20204, "Immediate Resolution", 1, new[] { 15 }),
        };

        private static IReadOnlyList<QuestDependencyRule.Row> RowsOf(string name) =>
            Rows.Where(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

        private static bool Met(string dep, string own, int ownMin, int cls, IEnumerable<int> finishedIds, params string[] scripted)
        {
            var finished = finishedIds.Select(id => Rows.First(r => r.Id == id))
                .Select(r => new QuestDependencyRule.Finished(r.Id, r.Name, r.MinLevel)).ToList();
            return QuestDependencyRule.IsMet(dep, own, ownMin, cls, finished, scripted, RowsOf);
        }

        [Test]
        public void TheLastPartNeedsTheMiddlePartNotJustTheFirst()
        {
            Assert.That(Met("Feast of the Decadent", "Feast of the Decadent", 48, 2, new[] { 20392 }), Is.False);
            Assert.That(Met("Feast of the Decadent", "Feast of the Decadent", 48, 2, new[] { 20392, 20407 }), Is.True);
        }

        [Test]
        public void TheMiddlePartNeedsTheFirst()
        {
            Assert.That(Met("Feast of the Decadent", "Feast of the Decadent", 45, 3, Array.Empty<int>()), Is.False);
            Assert.That(Met("Feast of the Decadent", "Feast of the Decadent", 45, 3, new[] { 20393 }), Is.True);
        }

        [Test]
        public void AnotherNameNeedsItsHighestPartAtOrBelowThisLevel()
        {
            // Hands Of Fate (30) after Legend of the Lake: the level 25 part, not the level 15 one
            Assert.That(Met("Legend of the Lake", "Hands Of Fate", 30, 2, new[] { 21288 }), Is.False);
            Assert.That(Met("Legend of the Lake", "Hands Of Fate", 30, 2, new[] { 21288, 20419, 21476 }), Is.True);
        }

        [Test]
        public void AScriptedFinaleOfTheLinesNameNeverOpensItsDataQuestParts()
        {
            Assert.That(Met("Feast of the Decadent", "Feast of the Decadent", 48, 2, Array.Empty<int>(), "Feast of the Decadent"), Is.False);
        }

        [Test]
        public void AScriptedQuestStillMeetsAnotherQuestsDependency()
        {
            // Commencement (Master Torr) needs Immediate Resolution: the scripted one counts for every class, the class's
            // own data quest too
            Assert.That(Met("Immediate Resolution", "Commencement", 2, 15, Array.Empty<int>(), "Immediate Resolution"), Is.True);
            Assert.That(Met("Immediate Resolution", "Commencement", 2, 15, new[] { 20204 }), Is.True);
            Assert.That(Met("Immediate Resolution", "Commencement", 2, 15, Array.Empty<int>()), Is.False);
            // a class with no data quest of that name: the old name match
            Assert.That(Met("Immediate Resolution", "Commencement", 2, 1, Array.Empty<int>(), "Immediate Resolution"), Is.True);
        }

        [Test]
        public void AClassWithoutAnEarlierPartFallsBackToTheNameMatch()
        {
            // class 10 has no 43/45 Feast row in this sample: any finished quest of the name
            Assert.That(Met("Feast of the Decadent", "Feast of the Decadent", 48, 10, new[] { 20392 }), Is.True);
            Assert.That(Met("Feast of the Decadent", "Feast of the Decadent", 48, 10, Array.Empty<int>()), Is.False);
        }

        [Test]
        public void AnIdTokenNamesExactRows()
        {
            Assert.That(Met("#20407", "x", 48, 2, new[] { 20392 }), Is.False);
            Assert.That(Met("#20407", "x", 48, 2, new[] { 20407 }), Is.True);
            Assert.That(Met("#20407,20408", "x", 48, 3, new[] { 20408 }), Is.True);
        }

        [TestCase("", 0)]
        [TestCase("0", 0)]
        [TestCase("2|3|10", 3)]
        [TestCase("45;41", 2)]
        public void AllowedClassesParse(string allowed, int count)
        {
            Assert.That(QuestDependencyRule.ParseClasses(allowed), Has.Count.EqualTo(count));
        }
    }
}
