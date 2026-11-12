using System;
using System.Collections.Concurrent;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Owner decision O6 (veteran review 2026-11-11): experience loss on death and prayer at the grave for solo gamebots,
    /// behind the server property bot_death_xp_loss, OFF by default (it may count as a difficulty change). When on, a solo
    /// gamebot killed in PvE pays the player's own rule (GamePlayer.Die / OnRevive):
    /// <list type="bullet">
    /// <item>at death: (50 - level, at most 10 from level 40) per mille of the level's span, a third of it for the first
    /// death of the level and two thirds for the second; from pve_exp_loss_level; never below the level's floor (or its
    /// second stage once reached from level 40);</item>
    /// <item>at release: the same amount again, which is left at the grave (the death spot);</item>
    /// <item>standing within 2,000 units of its grave out of combat, the bot prays for 5 seconds and gets that part back.
    /// A new death's grave replaces the old one, as a player's does.</item>
    /// </list>
    /// RvR deaths, grouped bots and hired companions lose nothing, as before. No visible gravestone is placed.
    /// </summary>
    public static class DeathXp165
    {
        [ServerProperty("bots", "bot_death_xp_loss",
            "Owner decision O6 (2026-11-11): solo gamebots lose experience on a PvE death and pray at their grave for it, by the player rule. OFF by default.", false)]
        public static bool Enabled = false;

        public const int PrayRadius = 2_000;
        public const long PrayMilliseconds = 5_000;

        private sealed class State
        {
            public long PendingReleaseLoss;
            public ushort GraveRegion;
            public int GraveX, GraveY;
            public long GraveXp;
            public long PrayUntilTick;
        }

        private static readonly ConcurrentDictionary<long, State> States = new();

        /// <summary>Pure rule: GamePlayer's death loss in per mille of the level span for a level and the deaths before.</summary>
        public static int LossPerMille(int level, int deathsBeforeThisLevel)
        {
            int perMille = level < 40 ? GamePlayer.MAX_LEVEL - level : GamePlayer.MAX_LEVEL - 40;
            if (deathsBeforeThisLevel <= 0)
                perMille /= 3;
            else if (deathsBeforeThisLevel == 1)
                perMille = perMille * 2 / 3;
            return Math.Max(0, perMille);
        }

        /// <summary>Pure rule: the experience a death costs (the level span times the per mille).</summary>
        public static long LossAtDeath(int level, int deathsBeforeThisLevel)
        {
            if (level < 1 || level >= GamePlayer.MAX_LEVEL)
                return 0;
            long span = GamePlayer.GetExperienceAmountForLevel(level) - GamePlayer.GetExperienceAmountForLevel(level - 1);
            return span * LossPerMille(level, deathsBeforeThisLevel) / 1000;
        }

        /// <summary>Pure rule: the lowest experience a loss may leave (the level's start, or its second stage from 40).</summary>
        public static long Floor(int level, long experience)
        {
            if (level < 1)
                return 0;
            long current = GamePlayer.GetExperienceAmountForLevel(level - 1);
            long next = GamePlayer.GetExperienceAmountForLevel(level);
            long secondStage = 1 + current + (next - current) / 2;
            return level >= 40 && level < GamePlayer.MAX_LEVEL && experience >= secondStage ? secondStage : current;
        }

        private static long Take(GameBot bot, long loss)
        {
            if (loss <= 0)
                return 0;
            return bot.TakeExperience165(loss, Floor(bot.Level, bot.Experience));
        }

        private static bool Applies(GameBot bot) =>
            Enabled && Classic165Mode.IsOn && bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
            !HiredCompanions165.IsHired(bot) && bot.Group == null && bot.DatabaseID > 0 &&
            bot.Level >= Properties.PVE_EXP_LOSS_LEVEL && bot.Level < GamePlayer.MAX_LEVEL;

        /// <summary>At death (after DeathPenalty165.OnDeath): the first part of the loss.</summary>
        public static void OnDeath(GameBot bot, bool rvrDeath, int deathsBeforeThisLevel)
        {
            if (rvrDeath || !Applies(bot))
                return;
            long loss = LossAtDeath(bot.Level, deathsBeforeThisLevel);
            long taken = Take(bot, loss);
            State state = States.GetOrAdd(bot.DatabaseID, _ => new State());
            lock (state)
                state.PendingReleaseLoss = loss;
            if (taken > 0)
                Log.Info($"BOT_DEATH_XP_165 bot={bot.Name} id={bot.DatabaseID} level={bot.Level} lost={taken}");
        }

        /// <summary>Back alive. A release pays the second part and leaves it at the grave; a raise pays nothing more.</summary>
        public static void OnRevive(GameBot bot, bool released, ushort deathRegion, int deathX, int deathY)
        {
            if (bot == null || !States.TryGetValue(bot.DatabaseID, out State state))
                return;
            long pending;
            lock (state)
            {
                pending = state.PendingReleaseLoss;
                state.PendingReleaseLoss = 0;
            }
            if (!released || pending <= 0 || !Enabled)
                return;
            long lost = Take(bot, pending);
            if (lost <= 0)
                return;
            lock (state)
            {
                state.GraveRegion = deathRegion;
                state.GraveX = deathX;
                state.GraveY = deathY;
                state.GraveXp = lost;
                state.PrayUntilTick = 0;
            }
            Log.Info($"BOT_GRAVE_165 bot={bot.Name} id={bot.DatabaseID} level={bot.Level} xp={lost} at={deathRegion}:{deathX},{deathY}");
        }

        /// <summary>Pure rule: the bot stands close enough to its grave to pray.</summary>
        public static bool AtGrave(ushort graveRegion, int graveX, int graveY, ushort region, int x, int y)
        {
            if (graveRegion == 0 || graveRegion != region)
                return false;
            long dx = (long)x - graveX, dy = (long)y - graveY;
            return dx * dx + dy * dy <= (long)PrayRadius * PrayRadius;
        }

        /// <summary>
        /// True while the bot prays at its grave (the caller keeps it standing): starts when it stands within 2,000 units
        /// of its grave out of combat, and after 5 seconds gives the grave's experience back (eXPSource.Praying, as a
        /// player's prayer).
        /// </summary>
        public static bool TryPray(GameBot bot)
        {
            if (!Enabled || bot == null || !bot.IsAlive || bot.InCombat || !States.TryGetValue(bot.DatabaseID, out State state))
                return false;
            long now = GameLoop.GameLoopTime;
            long xp;
            lock (state)
            {
                if (state.GraveXp <= 0 || !AtGrave(state.GraveRegion, state.GraveX, state.GraveY, bot.CurrentRegionID, bot.X, bot.Y))
                {
                    state.PrayUntilTick = 0;
                    return false;
                }
                if (state.PrayUntilTick == 0)
                {
                    state.PrayUntilTick = now + PrayMilliseconds;
                    return true;
                }
                if (now < state.PrayUntilTick)
                    return true;
                xp = state.GraveXp;
                state.GraveXp = 0;
                state.GraveRegion = 0;
                state.PrayUntilTick = 0;
            }
            bot.GainExperience(eXPSource.Praying, xp);
            Log.Info($"BOT_PRAYED_165 bot={bot.Name} id={bot.DatabaseID} level={bot.Level} xp={xp}");
            return false;
        }

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(DeathXp165));
    }
}
