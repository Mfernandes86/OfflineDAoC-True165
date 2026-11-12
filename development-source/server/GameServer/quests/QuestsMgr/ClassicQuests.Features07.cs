using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DOL.Database;
using DOL.Events;

namespace DOL.GS.Quests
{
    /// <summary>
    /// Quest features the 0.7 quest audit could not write as data (owner 2026-11-11: "make those now"). All are switched
    /// on per quest in classic-quests.json and cost nothing where they are not used:
    /// <list type="bullet">
    /// <item>a whisper step that takes any of several words (the Arawn Commander's riddle, [knowledge] or [power]);</item>
    /// <item>side turn-ins: an optional item handed to an NPC while the quest runs, for a bonus item, without moving the
    /// quest on (the lieutenants' heads of Commander Slor'vid's quest);</item>
    /// <item>a step that only counts while the player wears certain items ("wearing the four relics");</item>
    /// <item>a kill step that only counts the quest's own event monster, not a permanent monster of the same name;</item>
    /// <item>event monsters that walk a path with their escort instead of appearing beside the player;</item>
    /// <item>mid-quest items handed out in the player's class version, remembered across server restarts;</item>
    /// <item>the hours of an hour-window spawn as data.</item>
    /// </list>
    /// </summary>
    public static partial class ClassicQuests
    {
        // ------------------------------------------------------------------------------------------------ data

        /// <summary>
        /// An optional hand-in while a quest runs: giving <see cref="Item"/> to <see cref="Target"/> between
        /// <see cref="FromStep"/> and <see cref="ToStep"/> (0: to the end) takes it and gives <see cref="Reward"/> (the
        /// player's class version when there is one), once per quest. The quest's step does not change.
        /// <see cref="DropFrom"/>: the monster that carries the item for a player on the quest who has not handed it in.
        /// </summary>
        public sealed class SideTurnIn
        {
            public string Item { get; set; }
            public string Target { get; set; }
            public string Reward { get; set; }
            public int FromStep { get; set; } = 1;
            public int ToStep { get; set; }
            public string DropFrom { get; set; }
            public int DropChance { get; set; } = 100;
            /// <summary>What the NPC says when it takes the item (a recorded line; empty: only the system message).</summary>
            public string Thanks { get; set; }
        }

        /// <summary>The game hours [Start, End) an HourWindowSpawn of that name is up (End may be 24; Start above End wraps).</summary>
        public sealed class HourWindow
        {
            public int Start { get; set; }
            public int End { get; set; }
        }

        /// <summary>Goes up each time classic-quests.json is (re)loaded: readers that cache something from it look again.</summary>
        public static int ConfigVersion => Volatile.Read(ref _configVersion);
        private static int _configVersion;
        private static HashSet<string> _sideDropMobs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Called by Reload with the new configuration, before it is published.</summary>
        private static void PrepareFeatures(Config config)
        {
            config.HourWindows = new Dictionary<string, HourWindow>(config.HourWindows ?? new(), StringComparer.OrdinalIgnoreCase);
            var mobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestInfo info in config.Quests?.Values ?? Enumerable.Empty<QuestInfo>())
                foreach (SideTurnIn side in info?.SideTurnIns ?? Enumerable.Empty<SideTurnIn>())
                    if (!string.IsNullOrWhiteSpace(side?.DropFrom))
                        mobs.Add(side.DropFrom);
            _sideDropMobs = mobs;
            Interlocked.Increment(ref _configVersion);
        }

        /// <summary>The data hours of an hour-window spawn of this name, if classic-quests.json gives any.</summary>
        public static bool TryHourWindow(string name, out (int Start, int End) window)
        {
            window = default;
            if (string.IsNullOrEmpty(name) || _config.HourWindows == null || !_config.HourWindows.TryGetValue(name, out HourWindow hours) || hours == null)
                return false;
            window = (hours.Start, hours.End);
            return true;
        }

        // ------------------------------------------------------------------------------------------------ whisper step

        /// <summary>The words a whisper step takes ("knowledge|power").</summary>
        public static string[] WhisperOptions(string words) =>
            (words ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>The option the player said (case and a closing ., ! or ? do not matter), or null.</summary>
        public static string WhisperChoice(string words, string said) =>
            WhisperOptions(words).FirstOrDefault(option => string.Equals(Keyword(option), Keyword(said), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// What the step's NPC shows when talked to: the journal step without the generator's "(whisper: ...)" note, with
        /// every option as a link to click. Options the text does not show as links are added at the end.
        /// </summary>
        public static string WhisperChoicePrompt(string stepText, string words)
        {
            string text = System.Text.RegularExpressions.Regex.Replace(stepText ?? string.Empty, @"\s*\(whisper:[^)]*\)\s*$", "").Trim();
            string[] missing = WhisperOptions(words)
                .Where(option => text.IndexOf("[" + option + "]", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            if (missing.Length == 0)
                return text;
            string links = string.Join(" or ", missing.Select(option => "[" + option + "]"));
            return text.Length == 0 ? links : text + " " + links;
        }

        private static CustomStep WhisperStepAt(DataQuest quest, GameNPC npc)
        {
            if (!_config.Quests.TryGetValue(quest.ID, out QuestInfo info) || quest.Step <= 0 || quest.Step >= info.Steps.Count)
                return null;
            CustomStep custom = info.Steps[quest.Step]?.Custom;
            return custom?.Kind == "whisper" && !string.IsNullOrWhiteSpace(custom.Words) &&
                   string.Equals(custom.Target, npc.Name, StringComparison.OrdinalIgnoreCase) ? custom : null;
        }

        /// <summary>A whisper step: any of its words whispered (or clicked) to its NPC completes it.</summary>
        private static bool TryWhisperStep(GamePlayer player, GameNPC npc, string key)
        {
            foreach (DataQuest quest in player.QuestList.Keys.OfType<DataQuest>().ToArray())
            {
                if (WhisperStepAt(quest, npc) is not { } custom || WhisperChoice(custom.Words, key) == null)
                    continue;
                quest.AdvanceByScript(npc);
                return true;
            }
            return false;
        }

        /// <summary>Talking to a whisper step's NPC shows the step with its words as links.</summary>
        private static void OfferWhisperChoice(GamePlayer player, GameNPC npc)
        {
            foreach (DataQuest quest in player.QuestList.Keys.OfType<DataQuest>().ToArray())
                if (WhisperStepAt(quest, npc) is { } custom)
                    npc.SayTo(player, PacketHandler.eChatLoc.CL_PopupWindow, WhisperChoicePrompt(quest.CurrentStepText, custom.Words));
        }

        // ------------------------------------------------------------------------------------------------ worn items

        /// <summary>The first of the wanted items that is not worn, or null when all are (or none is wanted).</summary>
        public static string MissingWorn(IReadOnlyList<string> wearing, Func<string, bool> worn) =>
            wearing?.FirstOrDefault(id => !string.IsNullOrEmpty(id) && !worn(id));

        private static bool IsWorn(GamePlayer player, string id)
        {
            if (player.Inventory.GetFirstItemByID(id, eInventorySlot.MinEquipable, eInventorySlot.MaxEquipable) != null)
                return true;
            // the player's class version of a reward counts as the reward
            return _config.ClassRewards != null && _config.ClassRewards.TryGetValue(id, out Dictionary<string, string> byClass) &&
                   byClass.TryGetValue(player.CharacterClass.ID.ToString(), out string own) &&
                   player.Inventory.GetFirstItemByID(own, eInventorySlot.MinEquipable, eInventorySlot.MaxEquipable) != null;
        }

        /// <summary>
        /// May the quest's current step advance or finish now? False while the step's <see cref="StepInfo.Wearing"/> items
        /// are not all worn (the player is told which one is missing). Asked by the data quest before every step change.
        /// </summary>
        public static bool StepAllowed(DataQuest quest, GamePlayer player)
        {
            if (quest == null || player == null || !_config.Quests.TryGetValue(quest.ID, out QuestInfo info) ||
                quest.Step <= 0 || quest.Step >= info.Steps.Count)
                return true;
            List<string> wearing = info.Steps[quest.Step]?.Wearing;
            if (wearing == null || wearing.Count == 0)
                return true;
            string missing = MissingWorn(wearing, id => IsWorn(player, id));
            if (missing == null)
                return true;
            string name = GameServer.Database.FindObjectByKey<DbItemTemplate>(missing)?.Name ?? missing;
            player.Out.SendMessage($"Nothing happens. You must be wearing the {name}.", PacketHandler.eChatType.CT_System, PacketHandler.eChatLoc.CL_SystemWindow);
            return false;
        }

        // ------------------------------------------------------------------------------------------------ own spawn only

        /// <summary>Pure: a monster spawned for a quest (for anyone on it: a group shares the fight) is the quest's own.</summary>
        public static bool IsOwnSpawn(string spawnedFor) => !string.IsNullOrEmpty(spawnedFor);

        /// <summary>
        /// Does this kill count for the quest's current kill step? Always, unless the step is <see cref="StepInfo.OwnSpawnOnly"/>:
        /// then only the quest's event monster counts, not a permanent monster of the same name elsewhere in the world.
        /// </summary>
        public static bool KillCounts(int questId, int step, GameLiving killed)
        {
            if (!_config.Quests.TryGetValue(questId, out QuestInfo info) || step <= 0 || step >= info.Steps.Count ||
                info.Steps[step]?.OwnSpawnOnly != true)
                return true;
            return IsOwnSpawn((killed as GameNPC)?.TempProperties.GetProperty<string>(EventSpawnProperty));
        }

        // ------------------------------------------------------------------------------------------------ side turn-ins

        /// <summary>Side turn-ins already made: character, quest, item. Saved (classic-quest-memory.json).</summary>
        private static readonly ConcurrentDictionary<(string Character, int Quest, string Item), byte> SideTurnInsDone = new();

        /// <summary>Pure: the hand-in is open on this step.</summary>
        public static bool SideTurnInOpen(SideTurnIn side, int step) =>
            side != null && step >= Math.Max(1, side.FromStep) && (side.ToStep <= 0 || step <= side.ToStep);

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>An item handed to an NPC that one of the player's quests takes on the side. True when it was handled.</summary>
        private static bool TrySideTurnIn(GamePlayer player, GameNPC npc, DbInventoryItem item)
        {
            foreach (DataQuest quest in player.QuestList.Keys.OfType<DataQuest>().ToArray())
            {
                if (!_config.Quests.TryGetValue(quest.ID, out QuestInfo info) || info.SideTurnIns == null)
                    continue;
                foreach (SideTurnIn side in info.SideTurnIns)
                {
                    if (!SideTurnInOpen(side, quest.Step) || !Same(side.Target, npc.Name) || !Same(side.Item, item.Id_nb))
                        continue;
                    var done = (player.InternalID ?? player.Name, quest.ID, side.Item.ToLowerInvariant());
                    if (SideTurnInsDone.ContainsKey(done))
                    {
                        player.Out.SendMessage($"{npc.GetName(0, true)} has already rewarded you for that.", PacketHandler.eChatType.CT_System, PacketHandler.eChatLoc.CL_SystemWindow);
                        return true;
                    }
                    DbItemTemplate reward = string.IsNullOrEmpty(side.Reward) ? null
                        : RewardFor(player, GameServer.Database.FindObjectByKey<DbItemTemplate>(side.Reward));
                    if (reward != null && !player.Inventory.IsSlotsFree(1, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    {
                        player.Out.SendMessage("Your backpack is full.", PacketHandler.eChatType.CT_System, PacketHandler.eChatLoc.CL_SystemWindow);
                        return true;
                    }
                    player.Inventory.RemoveCountFromStack(item, 1);
                    SideTurnInsDone[done] = 0;
                    MarkMemoryDirty();
                    if (!string.IsNullOrWhiteSpace(side.Thanks))
                        npc.SayTo(player, PacketHandler.eChatLoc.CL_PopupWindow, DOL.GS.Behaviour.BehaviourUtils.GetPersonalizedMessage(side.Thanks, player));
                    if (reward != null && player.Inventory.AddTemplate(GameInventoryItem.Create(reward), 1, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    {
                        player.Out.SendMessage($"{npc.GetName(0, true)} gives you {reward.GetName(1, false)}.", PacketHandler.eChatType.CT_System, PacketHandler.eChatLoc.CL_SystemWindow);
                        InventoryLogging.LogInventoryAction(npc, player, eInventoryActionType.Quest, reward);
                    }
                    if (Log.IsInfoEnabled)
                        Log.Info($"CLASSIC_QUEST_SIDE_TURNIN quest={quest.ID} step={quest.Step} player={player.Name} item={side.Item} reward={reward?.Id_nb}");
                    return true;
                }
            }
            return false;
        }

        /// <summary>A side turn-in's item drops for a player on the quest who has not handed it in and holds none.</summary>
        private static void DropSideItems(GameNPC mob, DyingEventArgs slain)
        {
            if (!_sideDropMobs.Contains(mob.Name ?? string.Empty))
                return;
            var killers = new HashSet<GamePlayer>();
            if (slain.PlayerKillers != null) killers.UnionWith(slain.PlayerKillers);
            if (slain.Killer is GamePlayer direct) killers.Add(direct);
            else if (slain.Killer is GameNPC pet && pet.Brain is DOL.AI.Brain.IControlledBrain controlled && controlled.GetPlayerOwner() is GamePlayer owner) killers.Add(owner);
            foreach (GamePlayer player in killers)
            {
                if (!player.IsWithinRadius(mob, WorldMgr.VISIBILITY_DISTANCE)) continue;
                foreach (DataQuest quest in player.QuestList.Keys.OfType<DataQuest>().ToArray())
                {
                    if (!_config.Quests.TryGetValue(quest.ID, out QuestInfo info) || info.SideTurnIns == null) continue;
                    foreach (SideTurnIn side in info.SideTurnIns)
                    {
                        if (!Same(side?.DropFrom, mob.Name) || string.IsNullOrEmpty(side.Item) || !SideTurnInOpen(side, quest.Step) ||
                            SideTurnInsDone.ContainsKey((player.InternalID ?? player.Name, quest.ID, side.Item.ToLowerInvariant())) ||
                            player.Inventory.GetFirstItemByID(side.Item, eInventorySlot.Min_Inv, eInventorySlot.Max_Inv) != null ||
                            !Util.Chance(side.DropChance))
                            continue;
                        DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(side.Item);
                        if (template != null && player.Inventory.AddTemplate(GameInventoryItem.Create(template), 1, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                            player.Out.SendMessage($"You take the {template.Name} from {mob.GetName(0, false)}.", PacketHandler.eChatType.CT_Loot, PacketHandler.eChatLoc.CL_SystemWindow);
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ memory file

        private const string MemoryFileName = "classic-quest-memory.json";
        private static int _memoryDirty;
        private static readonly object MemoryLock = new();

        private sealed class MemoryFile
        {
            public List<string> Grants { get; set; } = new();
            public List<string> SideTurnIns { get; set; } = new();
        }

        private static void MarkMemoryDirty() => Volatile.Write(ref _memoryDirty, 1);

        /// <summary>Pure: has this quest already granted the character this real item, whatever the step?</summary>
        public static bool GrantedInQuest(IEnumerable<(string Character, int Quest, int Step, string Item)> given, string character, int quest, string item)
        {
            foreach (var g in given)
                if (g.Quest == quest && g.Character == character && string.Equals(g.Item, item, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>One saved line of a real item granted mid-quest: character, quest, step, item.</summary>
        public static string GrantLine(string character, int quest, int step, string item) => $"{character}|{quest}|{step}|{item}";

        public static bool TryParseGrantLine(string line, out (string Character, int Quest, int Step, string Item) grant)
        {
            grant = default;
            string[] parts = (line ?? string.Empty).Split('|');
            if (parts.Length != 4 || parts[0].Length == 0 || parts[3].Length == 0 ||
                !int.TryParse(parts[1], out int quest) || !int.TryParse(parts[2], out int step))
                return false;
            grant = (parts[0], quest, step, parts[3]);
            return true;
        }

        /// <summary>One saved line of a side turn-in made: character, quest, item.</summary>
        public static string SideLine(string character, int quest, string item) => $"{character}|{quest}|{item}";

        public static bool TryParseSideLine(string line, out (string Character, int Quest, string Item) side)
        {
            side = default;
            string[] parts = (line ?? string.Empty).Split('|');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[2].Length == 0 || !int.TryParse(parts[1], out int quest))
                return false;
            side = (parts[0], quest, parts[2]);
            return true;
        }

        /// <summary>
        /// What must outlive a restart: the real items already granted mid-quest and the side turn-ins already made. Quest
        /// audit 2026-11-11 (P09-D7): the grants were remembered only while the server ran, so after a restart a sold
        /// mid-quest reward was handed out again.
        /// </summary>
        private static void LoadMemory()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, MemoryFileName);
                if (!File.Exists(path)) return;
                MemoryFile file = JsonSerializer.Deserialize<MemoryFile>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                foreach (string line in file?.Grants ?? new())
                    if (TryParseGrantLine(line, out var grant)) RealGrantsGiven[grant] = 0;
                foreach (string line in file?.SideTurnIns ?? new())
                    if (TryParseSideLine(line, out var side)) SideTurnInsDone[side] = 0;
                if (Log.IsInfoEnabled)
                    Log.Info($"CLASSIC_QUEST_MEMORY loaded grants={RealGrantsGiven.Count} sideTurnIns={SideTurnInsDone.Count}");
            }
            catch (Exception ex)
            {
                Log.Error("Could not load " + MemoryFileName, ex);
            }
        }

        /// <summary>Writes the memory file when something changed (on the 5-second refresh and at shutdown).</summary>
        private static void SaveMemoryIfDirty()
        {
            if (Interlocked.Exchange(ref _memoryDirty, 0) == 0) return;
            try
            {
                var file = new MemoryFile
                {
                    Grants = RealGrantsGiven.Keys.Select(k => GrantLine(k.Character, k.Quest, k.Step, k.Item)).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                    SideTurnIns = SideTurnInsDone.Keys.Select(k => SideLine(k.Character, k.Quest, k.Item)).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                };
                string path = Path.Combine(AppContext.BaseDirectory, MemoryFileName);
                lock (MemoryLock)
                {
                    string temp = path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temp, path, true);
                }
            }
            catch (Exception ex)
            {
                MarkMemoryDirty();   // try again on the next refresh
                Log.Error("Could not save " + MemoryFileName, ex);
            }
        }

        /// <summary>A character seen online without a quest forgets that quest's side turn-ins (given up, or finished).</summary>
        private static void ForgetSideTurnIns(GamePlayer player, DataQuest[] active)
        {
            if (SideTurnInsDone.IsEmpty) return;
            string character = player.InternalID ?? player.Name;
            foreach (var key in SideTurnInsDone.Keys.Where(k => k.Character == character && active.All(q => q.ID != k.Quest)).ToArray())
                if (SideTurnInsDone.TryRemove(key, out _))
                    MarkMemoryDirty();
        }
    }
}
