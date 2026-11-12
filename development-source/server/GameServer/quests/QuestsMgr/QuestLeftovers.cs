using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS.Quests
{
    /// <summary>
    /// Quest audit 2026-11-11 (P02-D6, P03-E5): 17 classic quests whose last step is a talk (InteractFinish) left the
    /// collected quest pieces in the backpack (20009, 20019, 20030, 20035, 20037, 20039, 20041, 20042, 20046, 20069, 20071,
    /// 20072, 20081, 20082, 20084, 20217-20219). When a data quest finishes, the classic quest items ("cq_" ids) its steps
    /// handed out or asked for are taken from the backpack - never a final or optional reward, never a choose-step reward,
    /// and never an item another quest also uses (a later part may need it). Runs once at finish.
    /// </summary>
    public static class QuestLeftovers
    {
        public const string Prefix = "cq_";

        private static object _source;
        private static Dictionary<string, HashSet<string>> _usersByItem = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        /// <summary>The item ids of a "|"-separated step list, without a ";chance" suffix.</summary>
        public static IEnumerable<string> ItemIds(string list) =>
            (list ?? string.Empty).Split('|')
                .Select(part => part.Split(';')[0].Trim())
                .Where(id => id.Length > 0);

        /// <summary>
        /// The leftover classic quest items of a finished quest: its step and collect items with the "cq_" prefix, less the
        /// rewards and less the items any other quest (another name or level) also names.
        /// </summary>
        public static HashSet<string> LeftoverIds(IEnumerable<string> stepAndCollectItems, IEnumerable<string> rewards,
            Func<string, bool> sharedWithAnotherQuest)
        {
            var keep = new HashSet<string>(rewards.Where(r => !string.IsNullOrEmpty(r)), StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in stepAndCollectItems)
                if (id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && !keep.Contains(id) && !sharedWithAnotherQuest(id))
                    ids.Add(id);
            return ids;
        }

        /// <summary>Is the item named by a data quest other than this one (other name or other MinLevel; the class copies of
        /// one part count as one quest)? Built once from the data quest cache.</summary>
        public static bool SharedWithAnotherQuest(string itemId, DbDataQuest quest)
        {
            Dictionary<ushort, List<DbDataQuest>> source = GameObject.DataQuestCacheByRegion;
            Dictionary<string, HashSet<string>> users = _usersByItem;
            if (!ReferenceEquals(source, _source))
            {
                lock (_lock)
                {
                    if (!ReferenceEquals(source, _source))
                    {
                        users = new(StringComparer.OrdinalIgnoreCase);
                        if (source != null)
                            foreach (DbDataQuest row in source.Values.SelectMany(list => list))
                            {
                                if (row == null) continue;
                                string key = QuestKey(row);
                                foreach (string id in ItemIds(row.StepItemTemplates).Concat(ItemIds(row.CollectItemTemplate)))
                                {
                                    if (!users.TryGetValue(id, out HashSet<string> set))
                                        users[id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    set.Add(key);
                                }
                            }
                        _usersByItem = users;
                        _source = source;
                    }
                    else
                        users = _usersByItem;
                }
            }
            return users.TryGetValue(itemId, out HashSet<string> named) && named.Any(k => k != QuestKey(quest));
        }

        private static string QuestKey(DbDataQuest row) => (row.Name ?? string.Empty).ToLowerInvariant() + "|" + row.MinLevel;
    }
}
