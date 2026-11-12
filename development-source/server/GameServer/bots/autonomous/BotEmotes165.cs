using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Gamebot emotes (veteran-bots review 2026-11-11, M10 / rvr-social G3). The only emote a bot ever made was sit/stand,
    /// though emotes were the cross-realm language of 2003 (enemy /say came through garbled): /salute or /bow to a
    /// worthy enemy, /laugh or /rude over a corpse, /cheer when a keep fell, /wave back at someone who waves.
    /// - An RvR kill (an enemy player or gamebot) with a real player in sight: by its own manner (from its id, as the
    ///   stealthers' pick) a polite bot may /salute or /bow, a rude one /laugh or /rude; most do nothing (about 12%).
    /// - A keep taken: up to six winning gamebots near its doors /cheer or /victory, staggered over 1-4 seconds.
    /// - A player's /wave, /bow, /salute, /cheer, /rude or /laugh at a gamebot or companion out of combat: it answers in
    ///   kind (a rude one with /laugh) a moment later, at most once per 10 seconds per bot.
    /// All event-driven: one radius query at a kill or capture, one packet per emote; nothing runs between events.
    /// </summary>
    public static class BotEmotes165
    {
        private sealed class Box
        {
            public long Next;
        }

        private static readonly ConditionalWeakTable<GameBot, Box> NextEmote = new();

        public const long AnswerCooldownMilliseconds = 10_000;
        public const long KillCooldownMilliseconds = 30_000;
        public const int MaxCheering = 6;

        public enum Manner { Quiet, Polite, Rude }

        /// <summary>A bot's manner over a fallen enemy, fixed by its id: about 6% polite, 6% rude, the rest quiet.</summary>
        public static Manner MannerOf(long botId)
        {
            ulong mixed = (ulong)botId * 0x9E3779B97F4A7C15UL;
            int roll = (int)((mixed >> 40) % 100);
            return roll < 6 ? Manner.Polite : roll < 12 ? Manner.Rude : Manner.Quiet;
        }

        /// <summary>The emote a bot of this manner makes over a fallen enemy, or null.</summary>
        public static eEmote? KillEmote(Manner manner, int coin) => manner switch
        {
            Manner.Polite => coin % 2 == 0 ? eEmote.Salute : eEmote.Bow,
            Manner.Rude => coin % 2 == 0 ? eEmote.Laugh : eEmote.Rude,
            _ => null,
        };

        /// <summary>The emote a bot answers a player's emote with, or null for one it ignores.</summary>
        public static eEmote? AnswerTo(eEmote emote, Manner manner) => emote switch
        {
            eEmote.Wave => eEmote.Wave,
            eEmote.Bow => eEmote.Bow,
            eEmote.Salute => eEmote.Salute,
            eEmote.Cheer => eEmote.Cheer,
            eEmote.Rude => manner == Manner.Rude ? eEmote.Rude : eEmote.Laugh,
            eEmote.Laugh => eEmote.Laugh,
            _ => null,
        };

        private static bool Ready(GameBot bot, long cooldown)
        {
            long now = GameLoop.GameLoopTime;
            Box box = NextEmote.GetValue(bot, _ => new Box { Next = long.MinValue });
            if (box.Next != long.MinValue && now < box.Next)
                return false;
            box.Next = now + cooldown;
            return true;
        }

        private static void Later(GameBot bot, eEmote emote, int delay) =>
            _ = new ECSGameTimer(bot, _ =>
            {
                if (bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active)
                {
                    bot.Emote(emote);
                    FeatureCounters165.Add("bot_emote");
                }
                return 0;
            }, delay);

        /// <summary>GameBot.EnemyKilled: an emote over an RvR kill, when a real player can see it.</summary>
        public static void OnKill(GameBot bot, GameLiving enemy)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || enemy == null ||
                enemy.Realm == bot.Realm || enemy.Realm == eRealm.None || enemy is not (GamePlayer or GameBot))
                return;
            eEmote? emote = KillEmote(MannerOf(bot.DatabaseID), Random.Shared.Next(2));
            if (emote == null || !Ready(bot, KillCooldownMilliseconds) || !bot.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE).Any())
                return;
            Later(bot, emote.Value, 800 + Random.Shared.Next(1_200));
        }

        /// <summary>The emote command: a player emoted at a gamebot or companion.</summary>
        public static void OnPlayerEmote(GamePlayer player, GameObject target, eEmote emote)
        {
            if (player == null || target is not GameBot bot || !bot.IsAlive || bot.InCombat || bot.IsCasting)
                return;
            eEmote? answer = AnswerTo(emote, MannerOf(bot.DatabaseID));
            if (answer == null || !Ready(bot, AnswerCooldownMilliseconds))
                return;
            Later(bot, answer.Value, 600 + Random.Shared.Next(900));
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args) =>
            GameEventMgr.AddHandler(KeepEvent.KeepTaken, OnKeepTaken);

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args) =>
            GameEventMgr.RemoveHandler(KeepEvent.KeepTaken, OnKeepTaken);

        /// <summary>A keep fell: up to six winning gamebots near its doors cheer.</summary>
        private static void OnKeepTaken(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                if (args is not KeepEventArgs { Keep: { } keep })
                    return;
                GameKeepDoor door = keep.Doors.Values.FirstOrDefault(d => d?.ObjectState == GameObject.eObjectState.Active);
                if (door == null || !door.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE).Any())
                    return;
                int index = 0;
                foreach (GameBot bot in door.GetNPCsInRadius(2_000).OfType<GameBot>())
                {
                    if (bot.Realm != keep.Realm || !bot.IsAlive || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper)
                        continue;
                    Later(bot, Random.Shared.Next(2) == 0 ? eEmote.Cheer : eEmote.Victory, 1_000 + Random.Shared.Next(3_000));
                    if (++index >= MaxCheering)
                        break;
                }
            }
            catch (Exception)
            {
                // flavour never breaks a capture
            }
        }
    }
}
