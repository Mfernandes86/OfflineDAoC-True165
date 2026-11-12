using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Quests
{
    /// <summary>
    /// True 1.65 Mode (plan section 4, epic quests): helpers for the three level 50 finales that run as the period quests
    /// in 1.65 Mode, rebuilt from the period walkthroughs (Camelot Warcry spoilers, Allakhazam quest pages):
    /// Shadows50 = Lord of Deceit (Captain Rhodri, Lord Elidyn), Academy50 = Symbol of the Broken (started by Veteran
    /// Guardsman Esmond at Cornwall Station), Harmony50 = Last Heir (Cailean, Donnchadh, Chieftess Niamh, Talriese).
    /// Normal mode never calls these.
    /// </summary>
    public static class Epics165
    {
        /// <summary>The experience every scripted level 50 finale already gives (kept as the server's number).</summary>
        public const long FinaleXp = 1937768448;

        /// <summary>Has the player finished the earlier part of the epic line, the classic data quest of that name and level
        /// (the 43/45/47-48 parts are DataQuest rows that all carry the line's name)?</summary>
        public static bool FinishedPart(GamePlayer player, string questName, int minLevel)
        {
            foreach (AbstractQuest quest in player.GetFinishedQuests())
            {
                if (quest is DataQuest dataQuest && dataQuest.DBDataQuest != null && dataQuest.DBDataQuest.MinLevel == minLevel &&
                    IsPartOf(dataQuest.DBDataQuest.Name, questName))
                    return true;
            }
            return false;
        }

        /// <summary>The title a scripted level 50 finale has in True 1.65 Mode: the line's name with "(level 50)", as
        /// Allakhazam names the parts, so it never shares a name with the line's data quest parts (their name-based
        /// dependencies, the journal and the Quest Guide told them apart by name only; quest audit 2026-11-11).</summary>
        public static string FinaleTitle(string lineName) => lineName + " (level 50)";

        /// <summary>Is a data quest row's name a part of this epic line: the line's name itself, or the line's name with
        /// "(level N)" (quest fix 0.7 named the parts of The Moonstone Twin "(level 45)" and "(level 47)")?</summary>
        public static bool IsPartOf(string rowName, string lineName)
        {
            if (string.IsNullOrEmpty(rowName) || string.IsNullOrEmpty(lineName))
                return false;
            if (string.Equals(rowName, lineName, StringComparison.OrdinalIgnoreCase))
                return true;
            string prefix = lineName + " (level ";
            return rowName.Length > prefix.Length + 1 && rowName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   rowName.EndsWith(")", StringComparison.Ordinal) &&
                   int.TryParse(rowName.AsSpan(prefix.Length, rowName.Length - prefix.Length - 1), out _);
        }

        /// <summary>
        /// Has the player finished the last part of the epic line before the level 50 finale, whatever its level in this
        /// world (War Concluded's last data quest part is level 45, the others 47 or 48)? The highest MinLevel below 50
        /// among the line's data quest rows the player's class may take is the part needed. A line with no such row in
        /// the world cannot be gated and is allowed. The rows come from the cached data quest list (QuestDependencyRule).
        /// </summary>
        public static bool FinishedLastPart(GamePlayer player, string lineName)
        {
            int classId = player.CharacterClass.ID;
            int required = -1;
            foreach (QuestDependencyRule.Row row in QuestDependencyRule.RowsOfLine(lineName))
            {
                if (row.MinLevel >= 50 || row.Classes.Count > 0 && !row.Classes.Contains(classId))
                    continue;
                required = Math.Max(required, row.MinLevel);
            }
            if (required < 0)
                return true;
            foreach (AbstractQuest quest in player.GetFinishedQuests())
            {
                if (quest is DataQuest dataQuest && dataQuest.DBDataQuest != null && dataQuest.DBDataQuest.MinLevel >= required &&
                    dataQuest.DBDataQuest.MinLevel < 50 && IsPartOf(dataQuest.DBDataQuest.Name, lineName))
                    return true;
            }
            return false;
        }

        /// <summary>The named NPC of a realm in a region (the first one), or null.</summary>
        public static GameNPC FindNpc(string name, eRealm realm, ushort region)
        {
            foreach (GameNPC npc in WorldMgr.GetNPCsByName(name, realm))
                if (npc.CurrentRegionID == region)
                    return npc;
            return null;
        }

        /// <summary>The trainers who give the earlier part of an epic line (the StartName of its data quest rows), so the
        /// finale is offered by the same trainers the period journal sends the player to.</summary>
        public static List<GameNPC> TrainersOf(string questName, int minLevel, eRealm realm)
        {
            var found = new List<GameNPC>();
            foreach (DbDataQuest row in GameServer.Database.SelectAllObjects<DbDataQuest>())
            {
                if (row.MinLevel != minLevel || !string.Equals(row.Name, questName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (GameNPC npc in WorldMgr.GetNPCsByName(row.StartName, realm))
                    if (npc.CurrentRegionID == row.StartRegionID && npc is GameTrainer && !found.Contains(npc))
                        found.Add(npc);
            }
            return found;
        }

        /// <summary>Is this the player's own class trainer (the "return to your trainer" steps)?</summary>
        public static bool IsOwnTrainer(GamePlayer player, GameObject target) =>
            target is GameTrainer trainer && trainer.Realm == player.Realm && trainer.TrainedClass != eCharacterClass.Unknown &&
            trainer.CanTrain(player);

        /// <summary>Is the target the named NPC of the player's realm?</summary>
        public static bool IsNamed(GamePlayer player, GameObject target, string name) =>
            target is GameNPC npc && npc.Realm == player.Realm && npc.Name == name;

        /// <summary>The six free backpack slots a full suit of epic armour needs; tells the player when they are missing.</summary>
        public static bool HasRoomForArmour(GamePlayer player)
        {
            if (player.Inventory.IsSlotsFree(6, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                return true;
            player.Out.SendMessage("You do not have enough free space in your inventory!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            return false;
        }
    }
}
