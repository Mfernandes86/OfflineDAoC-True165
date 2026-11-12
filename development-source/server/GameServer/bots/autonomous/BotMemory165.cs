using System;
using System.Collections.Concurrent;

namespace DOL.GS
{
    /// <summary>
    /// Gamebots remember who killed them (veteran-bots review 2026-11-11, M10 / rvr-social G13). A death kept only its time
    /// and place; a 2003 player kept a KOS list, posted "that bloody Infil again" and went back for revenge.
    /// - The last enemy player to kill a gamebot (his pet or companion counts as him) is remembered for two hours: one
    ///   name per bot, overwritten.
    /// - When that player attacks it again, the bot says so on the realm chat group, at most once per 10 minutes:
    ///   "Aldric again, that Infiltrator is camping Pennine Mountains".
    /// - A stealther's target pick leans one rank toward its last killer (never more), and killing him is a /laugh and the
    ///   grudge is settled.
    /// No stat, damage or chance changes. One small entry per bot that died to a player; lookups only on hits and kills.
    /// </summary>
    public static class BotMemory165
    {
        public const long GrudgeMilliseconds = 2 * 60 * 60_000;
        public const long CallOutIntervalMilliseconds = 10 * 60_000;
        public const int MaxEntries = 16_384;

        public sealed class Grudge
        {
            public string Name;
            public string ClassName;
            public long Tick;
            public long NextCallOut;
        }

        private static readonly ConcurrentDictionary<long, Grudge> Grudges = new();

        /// <summary>Whether a remembered killer still counts at this time.</summary>
        public static bool Fresh(Grudge grudge, long now) => grudge != null && now - grudge.Tick < GrudgeMilliseconds;

        /// <summary>The line a bot says when its last killer turns up again.</summary>
        public static string CallOut(string name, string className, string zone, Random random)
        {
            string where = string.IsNullOrWhiteSpace(zone) ? string.Empty : $" camping {zone}";
            string cls = string.IsNullOrWhiteSpace(className) ? "one" : className;
            return (random ?? Random.Shared).Next(3) switch
            {
                0 => $"{name} again, that {cls} is{(where.Length > 0 ? where : " back")}",
                1 => $"watch out for {name}, {cls}{where}",
                _ => $"{name} is back{where}, kos",
            };
        }

        public static void Forget(long botId) => Grudges.TryRemove(botId, out _);

        public static Grudge Of(long botId) => Grudges.TryGetValue(botId, out Grudge grudge) ? grudge : null;

        /// <summary>GameBot.ProcessDeath: remember the enemy player behind the killing blow.</summary>
        public static void NoteDeath(GameBot bot, GameObject killer)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.DatabaseID <= 0)
                return;
            GamePlayer player = AutonomousRvrEventLayer.PlayerInstigator(killer);
            if (player == null || player.Realm == bot.Realm)
                return;
            if (Grudges.Count >= MaxEntries)
                Grudges.Clear();
            Grudges[bot.DatabaseID] = new Grudge
            {
                Name = player.Name, ClassName = player.CharacterClass?.Name, Tick = GameLoop.GameLoopTime,
            };
        }

        /// <summary>GameBot.OnAttackedByEnemy: its last killer is back.</summary>
        public static void OnAttacked(GameBot bot, GameObject attacker)
        {
            if (bot == null || !Grudges.TryGetValue(bot.DatabaseID, out Grudge grudge))
                return;
            long now = GameLoop.GameLoopTime;
            if (!Fresh(grudge, now))
            {
                Grudges.TryRemove(bot.DatabaseID, out _);
                return;
            }
            if (now < grudge.NextCallOut || AutonomousRvrEventLayer.PlayerInstigator(attacker) is not { } player ||
                !string.Equals(player.Name, grudge.Name, StringComparison.Ordinal))
                return;
            grudge.NextCallOut = now + CallOutIntervalMilliseconds;
            BotChatChannels165.Send(BotChatChannels165.PeriodChannels ? BotChatChannels165.Channel.RvR : BotChatChannels165.Channel.Faction,
                bot, CallOut(grudge.Name, grudge.ClassName, bot.CurrentZone?.Description, Random.Shared));
            FeatureCounters165.Add("bot_grudge_callout");
        }

        /// <summary>Whether this living is the bot's remembered killer (a stealther's pick leans one rank toward him).</summary>
        public static bool IsKiller(GameBot bot, GameLiving target) =>
            bot != null && target is GamePlayer player && Grudges.TryGetValue(bot.DatabaseID, out Grudge grudge) &&
            Fresh(grudge, GameLoop.GameLoopTime) && string.Equals(player.Name, grudge.Name, StringComparison.Ordinal);

        /// <summary>The bot killed its remembered killer: the grudge is settled. True when it was him.</summary>
        public static bool SettleOnKill(GameBot bot, GameLiving enemy)
        {
            if (!IsKiller(bot, enemy))
                return false;
            Grudges.TryRemove(bot.DatabaseID, out _);
            FeatureCounters165.Add("bot_grudge_settled");
            return true;
        }
    }
}
