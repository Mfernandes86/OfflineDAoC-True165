using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS.Quests
{
    /// <summary>
    /// Data quests attach to every object whose name equals the quest's StartName, and the talk truce goes by name too.
    /// A hostile realm-0 monster that happens to carry the name of a realm quest NPC in the same region therefore offered
    /// the quest and stopped aggroing (quest audit 2026-11-11): the six level 17-19 "Tracker" monsters at Moon Lake
    /// offered Merle's Informant and counted as Merle's whisper target; the level 62 Prima boss "Tethra" in Breifine
    /// gave Straw's In Town and would not attack anyone level 7-50. Such a monster - realm 0, not peaceful, with an NPC of
    /// the same name of a player realm in its region - gets no data quests and no talk truce. Checked once when the
    /// server has started (all spawns loaded) and, after that, when a new object loads its quests.
    /// </summary>
    public static class QuestRealmImpostors
    {
        /// <summary>Temp property set on such a monster; QuestTalkTruce reads it.</summary>
        public const string Property = "QuestRealmImpostor";
        private static readonly Logger Log = LoggerManager.Create(typeof(QuestRealmImpostors));
        private static volatile bool _started;

        /// <summary>The cheap part: a hostile realm-0 NPC (only those can be impostors).</summary>
        public static bool CouldBe(GameNPC npc) =>
            npc != null && npc.Realm == eRealm.None && (npc.Flags & GameNPC.eFlags.PEACE) == 0;

        public static bool IsMarked(GameNPC npc) => npc != null && npc.TempProperties.GetProperty<bool>(Property);

        /// <summary>Called from GameObject.LoadDataQuests: after start-up, a hostile realm-0 NPC that shares its name with a
        /// realm NPC of its region is marked and gets no data quests. Before start-up the sweep below decides.</summary>
        public static bool SkipQuestsFor(GameObject obj)
        {
            if (!_started || obj is not GameNPC npc || !CouldBe(npc))
                return false;
            if (IsMarked(npc))
                return true;
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                if (WorldMgr.GetNPCsByNameFromRegion(npc.Name, npc.CurrentRegionID, realm).Length > 0)
                {
                    npc.TempProperties.SetProperty(Property, true);
                    return true;
                }
            }
            return false;
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                Sweep();
            }
            catch (Exception ex)
            {
                Log.Error("Quest realm impostor sweep failed", ex);
            }
            finally
            {
                _started = true;
            }
        }

        /// <summary>One pass over the world's NPCs: the names of realm NPCs per region, then every hostile realm-0 NPC with
        /// one of those names loses its data quests and is marked.</summary>
        private static void Sweep()
        {
            var npcs = new List<GameNPC>();
            foreach (Region region in WorldMgr.GetAllRegions())
                npcs.AddRange(WorldMgr.GetNPCsFromRegion(region.ID));
            var realmNames = new HashSet<(ushort, string)>();
            foreach (GameNPC npc in npcs)
                if (npc != null && npc.Realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia && !string.IsNullOrEmpty(npc.Name))
                    realmNames.Add((npc.CurrentRegionID, npc.Name.ToLowerInvariant()));
            int marked = 0, quests = 0;
            foreach (GameNPC npc in npcs)
            {
                if (!CouldBe(npc) || string.IsNullOrEmpty(npc.Name) || !realmNames.Contains((npc.CurrentRegionID, npc.Name.ToLowerInvariant())))
                    continue;
                npc.TempProperties.SetProperty(Property, true);
                marked++;
                foreach (DataQuest quest in npc.DataQuestList.ToArray())
                {
                    npc.RemoveDataQuest(quest);
                    quests++;
                }
            }
            if (Log.IsInfoEnabled)
                Log.Info($"QUEST_REALM_IMPOSTORS marked={marked} questsRemoved={quests}");
        }
    }
}
