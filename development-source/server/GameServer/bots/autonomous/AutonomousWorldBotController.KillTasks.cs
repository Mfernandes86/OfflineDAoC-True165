using System;
using System.Collections.Generic;
using System.Threading;
using DOL.GS.Quests;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        /// <summary>
        /// Owner decision O5: on a town or service visit the bot makes anyway, a solo gamebot collects the reward of a
        /// finished kill task given near here, or takes a new one from a named guard standing by (BotKillTasks165).
        /// One NPC scan per visit at most; no special trips.
        /// </summary>
        private static void TryKillTaskTownVisit(GameBot bot)
        {
            if (!BotKillTasks165.Enabled || !Classic165Mode.IsOn || bot?.IsAutonomousWorldBot != true || bot.Group != null ||
                bot.IsTemporaryGroupHelper || HiredCompanions165.IsHired(bot) || !bot.IsAlive || bot.DatabaseID <= 0)
                return;
            long now = GameLoop.GameLoopTime;
            BotKillTasks165.Task done = BotKillTasks165.TakeFinished(bot.DatabaseID, bot.CurrentRegionID, bot.X, bot.Y, now, bot.Level);
            if (done != null)
            {
                // the player's reward: KillTask's money and PeriodKillTaskXP for the bot's level, XP unmultiplied as a
                // player's task XP is (AbstractTask.FinishTask)
                long money = BotKillTasks165.Money(bot.Level);
                long xp = BotKillTasks165.Experience(bot.Level);
                if (money > 0)
                    AutonomousBotEconomy.AddMoney(bot.DatabaseID, money);
                if (xp > 0)
                    bot.GainExperience(eXPSource.Task, xp);
                Log.Info($"BOT_KILL_TASK_DONE bot={bot.Name} id={bot.DatabaseID} level={bot.Level} guard=\"{done.GuardName}\" " +
                         $"monster=\"{done.MonsterName}\" copper={money} xp={xp}");
                return;
            }
            if (!BotKillTasks165.MayTake(true, true, true, bot.Level, BotKillTasks165.Current(bot.DatabaseID) != null,
                    BotKillTasks165.LastTaskLevel(bot.DatabaseID), Random.Shared.Next(100)))
                return;
            GameNPC guard = null;
            foreach (GameNPC npc in bot.GetNPCsInRadius(BotKillTasks165.GuardSearchRadius))
                if (npc != null && npc is not GameBot && npc.IsAlive && npc.Realm == bot.Realm && KillTask.CheckNamedGuard(npc))
                {
                    guard = npc;
                    break;
                }
            if (guard?.CurrentZone is not Zone zone)
                return;
            string monster = ChooseKillTaskMonster(bot, zone);
            if (monster == null)
                return;
            BotKillTasks165.Give(bot.DatabaseID, new BotKillTasks165.Task
            {
                MonsterName = monster,
                GuardName = guard.Name,
                Region = guard.CurrentRegionID,
                X = guard.X,
                Y = guard.Y,
                Level = bot.Level,
                TakenTick = now,
            });
            Log.Info($"BOT_KILL_TASK_TAKEN bot={bot.Name} id={bot.DatabaseID} level={bot.Level} guard=\"{guard.Name}\" " +
                     $"zone=\"{zone.Description}\" monster=\"{monster}\"");
        }

        /// <summary>
        /// A common monster of the guard's zone for the task (KillTask.GetRandomMob's levels, L-3 to L+1), taken from the
        /// camp catalog so the bot's camp draw can find it: outdoors, not Darkness Falls, at least one spawn level that
        /// cons green or harder and no harder than the bot's solo ceiling.
        /// </summary>
        private static string ChooseKillTaskMonster(GameBot bot, Zone zone)
        {
            (int minLevel, int maxLevel) = BotKillTasks165.MonsterLevels(bot.Level);
            ConColor ceiling = SoloCeiling(bot, false);
            var names = new List<string>();
            foreach (CampCatalogCell cell in Volatile.Read(ref _campCatalog))
            {
                if (cell == null || cell.IsDungeon || cell.Zone != zone || cell.LiveMobCount <= 0 ||
                    cell.RegionId == AutonomousDarknessFallsPolicy.RegionId || string.IsNullOrWhiteSpace(cell.MonsterName) ||
                    names.Contains(cell.MonsterName))
                    continue;
                foreach (int level in cell.Levels ?? Array.Empty<int>())
                {
                    if (level < minLevel || level > maxLevel)
                        continue;
                    ConColor con = ConLevels.GetConColor(ConLevels.GetConLevel(bot.EffectiveLevel, level));
                    if (con >= ConColor.GREEN && con <= ceiling)
                    {
                        names.Add(cell.MonsterName);
                        break;
                    }
                }
            }
            return names.Count == 0 ? null : names[Random.Shared.Next(names.Count)];
        }

        /// <summary>The camp draw multiplier for the monster of a solo bot's live kill task.</summary>
        public const int KillTaskCampWeight = 10;
    }
}
