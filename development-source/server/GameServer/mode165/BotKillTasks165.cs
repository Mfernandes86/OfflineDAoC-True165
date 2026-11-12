using System;
using System.Collections.Concurrent;
using System.Reflection;
using DOL.GS.Quests;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Owner decision O5 (veteran review 2026-11-11): solo gamebots take kill tasks from named guards at levels 4-19, the
    /// period's fastest way to level 20 (Prima R&amp;E; FreddysHouse "/whisper task to a named guard"). Light version, the
    /// player rules otherwise:
    /// <list type="bullet">
    /// <item>a task is taken only on a town or service visit the bot makes anyway, from a named guard standing there
    /// (KillTask.CheckNamedGuard), at most one per level, about 40% of visits;</item>
    /// <item>the monster is a common one of the guard's zone the bot may hunt solo (levels L-3 to L+1 as KillTask picks,
    /// never grey, never above the bot's solo ceiling);</item>
    /// <item>the bot's camp draw favours that monster until it is killed (any normal kill counts, as for players);</item>
    /// <item>the reward is the player's (KillTask's money table and PeriodKillTaskXP) and is paid on the bot's next visit
    /// near that guard, never on a special trip; a task left 2 hours, or at level 20, expires like a player's.</item>
    /// </list>
    /// </summary>
    public static class BotKillTasks165
    {
        [ServerProperty("bots", "bot_kill_tasks",
            "Owner decision O5 (2026-11-11): solo gamebots at levels 4-19 take kill tasks from named guards on town visits they make anyway and are paid the player's task reward on a later visit there.", true)]
        public static bool Enabled = true;

        public const int MinimumLevel = 4;
        public static int MaximumLevel => AbstractTask.MaxTaskLevel;
        public const int TakeChancePercent = 40;
        public const long TaskLifetimeMilliseconds = 2 * 60 * 60_000L;   // KillTask.TimeOut: two hours
        /// <summary>The bot is "back at the guard" within this distance of where the task was given.</summary>
        public const int ReturnRadius = 6_000;
        /// <summary>A named guard this close to the service the bot is visiting can give it a task.</summary>
        public const ushort GuardSearchRadius = 3_000;

        public sealed class Task
        {
            public string MonsterName;
            public string GuardName;
            public ushort Region;
            public int X, Y;
            public byte Level;
            public long TakenTick;
            public bool Killed;
        }

        private sealed class State
        {
            public Task Current;
            public byte LastTaskLevel;
        }

        private static readonly ConcurrentDictionary<long, State> States = new();

        /// <summary>Pure rule: the bot may take a task now.</summary>
        public static bool MayTake(bool enabled, bool classic165, bool solo, int level, bool hasTask, int lastTaskLevel, int roll100) =>
            enabled && classic165 && solo && level >= MinimumLevel && level <= MaximumLevel && !hasTask &&
            lastTaskLevel != level && roll100 < TakeChancePercent;

        /// <summary>Pure rule: a task has expired (two hours, or the bot passed the task levels).</summary>
        public static bool Expired(long takenTick, long now, int level) =>
            now < takenTick || now - takenTick > TaskLifetimeMilliseconds || level > MaximumLevel;

        /// <summary>Pure rule: the monster levels a task may name for a bot of this level (KillTask.GetRandomMob).</summary>
        public static (int Min, int Max) MonsterLevels(int level) => (Math.Max(1, level - 3), level + 1);

        public static Task Current(long botId)
        {
            if (!States.TryGetValue(botId, out State state))
                return null;
            lock (state)
                return state.Current;
        }

        public static int LastTaskLevel(long botId)
        {
            if (!States.TryGetValue(botId, out State state))
                return 0;
            lock (state)
                return state.LastTaskLevel;
        }

        /// <summary>The monster a live, unfinished task names (null when none).</summary>
        public static string HuntedMonster(long botId, long now, int level)
        {
            Task task = Current(botId);
            return task == null || task.Killed || Expired(task.TakenTick, now, level) ? null : task.MonsterName;
        }

        public static void Give(long botId, Task task)
        {
            State state = States.GetOrAdd(botId, _ => new State());
            lock (state)
            {
                state.Current = task;
                state.LastTaskLevel = task.Level;
            }
        }

        /// <summary>A kill credited to the bot: the task's monster marks it done (any kill of that name, as for players).</summary>
        public static void RecordKill(GameBot bot, GameNPC killed)
        {
            if (bot == null || killed == null || !States.TryGetValue(bot.DatabaseID, out State state))
                return;
            lock (state)
            {
                Task task = state.Current;
                if (task == null || task.Killed || !string.Equals(task.MonsterName, killed.Name, StringComparison.Ordinal))
                    return;
                task.Killed = true;
            }
        }

        /// <summary>Removes and returns the finished task when the bot is back near its guard (null otherwise); drops an
        /// expired one.</summary>
        public static Task TakeFinished(long botId, ushort region, int x, int y, long now, int level)
        {
            if (!States.TryGetValue(botId, out State state))
                return null;
            lock (state)
            {
                Task task = state.Current;
                if (task == null)
                    return null;
                if (Expired(task.TakenTick, now, level))
                {
                    state.Current = null;
                    return null;
                }
                if (!task.Killed || task.Region != region)
                    return null;
                long dx = (long)x - task.X, dy = (long)y - task.Y;
                if (dx * dx + dy * dy > (long)ReturnRadius * ReturnRadius)
                    return null;
                state.Current = null;
                return task;
            }
        }

        // ---------------------------------------------------------------- the player's reward

        private static int[] _moneyTable;

        /// <summary>
        /// KillTask's own money for a level, read from the server's table (KillTask.MoneyReward, the same +-3% spread as a
        /// player's RewardMoney). The quest code is not changed here; the table is read once.
        /// </summary>
        public static long Money(int level, Random random = null)
        {
            _moneyTable ??= typeof(KillTask).GetField("MoneyReward", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as int[]
                            ?? Array.Empty<int>();
            return MoneyFrom(_moneyTable, level, random ?? Random.Shared);
        }

        /// <summary>Pure rule: KillTask.RewardMoney for a table, level and roll (inclusive spread, integer maths as there).</summary>
        public static long MoneyFrom(int[] table, int level, Random random)
        {
            if (table == null || level < 1 || level > table.Length)
                return 0;
            int baseMoney = table[level - 1];
            int spread = baseMoney / 100 * 3;
            return random.Next(baseMoney - spread, baseMoney + spread + 1);
        }

        public static long Experience(int level) => KillTask.PeriodKillTaskXP(level);
    }
}
