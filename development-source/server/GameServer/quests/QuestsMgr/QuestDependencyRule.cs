using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS.Quests
{
    /// <summary>
    /// When a data quest's QuestDependency counts as met (quest audit 2026-11-11, P04-E2, P07, P09, P10-E5).
    /// Dependencies used to match ANY finished quest of that name, and the classic epic and story chains give every part
    /// the same name, so a later part opened as soon as the first part was done: Feast of the Decadent 48 after part 1
    /// (skipping 45), Hands Of Fate after Legend of the Lake 15 (skipping 20 and 25), Thane's Blood after Red Dagger 15
    /// (the period needs 25), and a scripted level 50 finale of the line's name met them all.
    /// <list type="bullet">
    /// <item>"#21505" (or "#21505,21506" for any of several) names exact data quest IDs.</item>
    /// <item>A name that data quest rows carry: among the rows of that name the player's class may take, with a MinLevel
    /// below this quest's (its own name: the earlier parts of the same line) or at most this quest's (another name), the
    /// highest such MinLevel is the part that must be finished. A finished data quest of that name at that level or above
    /// meets it. A scripted quest of the name meets another quest's dependency (a base class may have done Master Torr's
    /// scripted Immediate Resolution instead of its own data quest of that name) but never the line's own later parts, so
    /// a scripted level 50 finale no longer opens the 45/47/48 parts of its line.</item>
    /// <item>Any other name (no row of it the class may take at those levels): any finished quest of that name, scripted
    /// ones included, as before (Master Torr's Commencement needs the scripted Immediate Resolution).</item>
    /// </list>
    /// The rows of each name are read once from the data quest cache; qualification stays a short list scan.
    /// </summary>
    public static class QuestDependencyRule
    {
        public readonly record struct Row(int Id, string Name, int MinLevel, IReadOnlyCollection<int> Classes);

        public readonly record struct Finished(int Id, string Name, int MinLevel);

        private static object _cacheSource;
        private static Dictionary<string, List<Row>> _rowsByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        /// <summary>The class ids of an AllowedClasses value ("" or "0": every class).</summary>
        public static IReadOnlyCollection<int> ParseClasses(string allowed)
        {
            var classes = new List<int>();
            foreach (string part in (allowed ?? string.Empty).Split('|', ';', ','))
                if (int.TryParse(part.Trim(), out int id) && id > 0)
                    classes.Add(id);
            return classes;
        }

        /// <summary>Is one dependency met? <paramref name="rowsOf"/> gives every data quest row carrying a name.</summary>
        public static bool IsMet(string dependency, string ownName, int ownMinLevel, int classId,
            IReadOnlyList<Finished> finishedDataQuests, IEnumerable<string> finishedScriptedNames, Func<string, IReadOnlyList<Row>> rowsOf)
        {
            string dep = (dependency ?? string.Empty).Trim();
            if (dep.Length == 0)
                return true;

            if (dep[0] == '#')
            {
                var ids = new HashSet<int>();
                foreach (string part in dep[1..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (int.TryParse(part.TrimStart('#'), out int id))
                        ids.Add(id);
                return finishedDataQuests.Any(f => ids.Contains(f.Id));
            }

            bool own = string.Equals(dep, ownName, StringComparison.OrdinalIgnoreCase);
            int required = -1;
            foreach (Row row in rowsOf(dep) ?? Array.Empty<Row>())
            {
                if (row.Classes != null && row.Classes.Count > 0 && !row.Classes.Contains(classId))
                    continue;
                if (own ? row.MinLevel >= ownMinLevel : row.MinLevel > ownMinLevel)
                    continue;
                required = Math.Max(required, row.MinLevel);
            }

            bool scripted = finishedScriptedNames.Any(n => string.Equals(n, dep, StringComparison.OrdinalIgnoreCase));
            if (required < 0)
                return scripted || finishedDataQuests.Any(f => string.Equals(f.Name, dep, StringComparison.OrdinalIgnoreCase));
            if (scripted && !own)
                return true;
            return finishedDataQuests.Any(f => f.MinLevel >= required && string.Equals(f.Name, dep, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The live data quest rows of a name (from <see cref="GameObject.DataQuestCacheByRegion"/>, rebuilt when it is refilled).</summary>
        public static IReadOnlyList<Row> RowsOf(string name)
        {
            Dictionary<ushort, List<DbDataQuest>> source = GameObject.DataQuestCacheByRegion;
            Dictionary<string, List<Row>> byName = _rowsByName;
            if (!ReferenceEquals(source, _cacheSource))
            {
                lock (_lock)
                {
                    if (!ReferenceEquals(source, _cacheSource))
                    {
                        byName = new(StringComparer.OrdinalIgnoreCase);
                        if (source != null)
                            foreach (DbDataQuest q in source.Values.SelectMany(list => list))
                            {
                                if (string.IsNullOrEmpty(q?.Name))
                                    continue;
                                if (!byName.TryGetValue(q.Name, out List<Row> rows))
                                    byName[q.Name] = rows = new List<Row>();
                                rows.Add(new Row(q.ID, q.Name, q.MinLevel, ParseClasses(q.AllowedClasses)));
                            }
                        _rowsByName = byName;
                        _cacheSource = source;
                    }
                    else
                        byName = _rowsByName;
                }
            }
            return byName.TryGetValue(name ?? string.Empty, out List<Row> found) ? found : Array.Empty<Row>();
        }

        private static Dictionary<string, List<Row>> _lineRows = new(StringComparer.OrdinalIgnoreCase);
        private static object _lineSource;

        /// <summary>Every data quest row of an epic line: the line's name or the line's name with "(level N)"
        /// (<see cref="Epics165.IsPartOf"/>); cached per line until the data quest cache is refilled.</summary>
        public static IReadOnlyList<Row> RowsOfLine(string lineName)
        {
            Dictionary<ushort, List<DbDataQuest>> source = GameObject.DataQuestCacheByRegion;
            lock (_lock)
            {
                if (!ReferenceEquals(source, _lineSource))
                {
                    _lineRows = new(StringComparer.OrdinalIgnoreCase);
                    _lineSource = source;
                }
                if (_lineRows.TryGetValue(lineName ?? string.Empty, out List<Row> cached))
                    return cached;
                var rows = new List<Row>();
                if (source != null)
                    foreach (DbDataQuest q in source.Values.SelectMany(list => list))
                        if (q != null && Epics165.IsPartOf(q.Name, lineName))
                            rows.Add(new Row(q.ID, q.Name, q.MinLevel, ParseClasses(q.AllowedClasses)));
                _lineRows[lineName ?? string.Empty] = rows;
                return rows;
            }
        }

        /// <summary>Are all of a quest's dependencies met by what this player has finished?</summary>
        public static bool AllMet(IReadOnlyCollection<string> dependencies, string ownName, int ownMinLevel, GamePlayer player,
            IEnumerable<AbstractQuest> finishedQuests)
        {
            if (dependencies == null || dependencies.Count == 0)
                return true;
            var finishedData = new List<Finished>();
            var scriptedNames = new List<string>();
            foreach (AbstractQuest quest in finishedQuests)
            {
                if (quest is DataQuest dq)
                {
                    if (dq.DBDataQuest != null)
                        finishedData.Add(new Finished(dq.ID, dq.DBDataQuest.Name, dq.DBDataQuest.MinLevel));
                }
                else if (quest != null)
                    scriptedNames.Add(quest.Name);
            }
            int classId = player.CharacterClass.ID;
            foreach (string dep in dependencies)
                if (!IsMet(dep, ownName, ownMinLevel, classId, finishedData, scriptedNames, RowsOf))
                    return false;
            return true;
        }
    }
}
