using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode death penalties for bots, as for players (owner 2026-10-31: "they will visit a healer and pay after
    /// death to get rid of those like a player or wait it out or skip if broke"). A bot killed in PvE that releases loses
    /// Constitution like a player (1, 2, then 3 points for its first, second and later deaths at a level, from
    /// pve_con_loss_level); a raise costs no Constitution. Coming back to life, by release or a raise, gives resurrection
    /// sickness (from ress_sickness_level; RvR deaths the RvR kind). A gamebot then visits a healer between tasks and pays
    /// to restore its Constitution as far as its coin goes (the sickness is cured there too), or waits the sickness out.
    /// Hired companions don't release; their player's visit to a healer cures their sickness.
    /// </summary>
    public static class DeathPenalty165
    {
        private const int PveSicknessSpell = 2435, RvrSicknessSpell = 8181;

        /// <summary>At death: whether it was an RvR death, and the Constitution a release would cost.</summary>
        public static (bool RvR, int ConLoss) OnDeath(GameBot bot, GameObject killer, ref byte deathsLevel, ref int deathsThisLevel)
        {
            if (!Classic165Mode.IsOn || bot == null)
                return (false, 0);
            GameLiving source = killer as GameLiving;
            for (int depth = 0; depth < 8 && source is GameNPC { Brain: IControlledBrain controlled } && controlled.Owner != null; depth++)
                source = controlled.Owner;
            bool rvr = source != null && source.Realm != eRealm.None && source.Realm != bot.Realm;
            if (rvr)
                return (true, 0);
            if (deathsLevel != bot.Level)
            {
                deathsLevel = bot.Level;
                deathsThisLevel = 0;
            }
            deathsThisLevel++;
            // One loss at a time (owner 2026-10-31: level 6-7 bots had lost ~22 Constitution, up to 111 deaths): a bot that
            // still has Constitution to buy back loses no more until a healer restores it.
            if (bot.TotalConstitutionLostAtDeath > 0)
                return (false, 0);
            return (false, bot.Level >= ServerProperties.Properties.PVE_CON_LOSS_LEVEL ? Math.Clamp(deathsThisLevel, 1, 3) : 0);
        }

        /// <summary>Back alive (released or raised): resurrection sickness, as a player gets in GamePlayer.OnRevive.</summary>
        public static void OnRevive(GameBot bot, bool rvrDeath)
        {
            if (!Classic165Mode.IsOn || bot == null || !bot.IsAlive || bot.Level < ServerProperties.Properties.RESS_SICKNESS_LEVEL)
                return;
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Realm_Spells);
            Spell spell = line == null ? null : SkillBase.FindSpell(rvrDeath ? RvrSicknessSpell : PveSicknessSpell, line);
            if (spell == null)
                return;
            ScriptMgr.CreateSpellHandler(bot, spell, line)?.StartSpell(bot);
        }

        public static bool HasCurableSickness(GameLiving living) =>
            EffectListService.GetEffectOnTarget(living, eEffect.ResurrectionIllness) != null ||
            EffectListService.GetEffectOnTarget(living, eEffect.RvrResurrectionIllness) != null;

        public static long CostPerConPoint(int level) =>
            GamePlayer.prcRestore[Math.Clamp(level, 0, GamePlayer.prcRestore.Length - 1)];

        /// <summary>Constitution owed that makes a healer worth its own trip (veteran review 2026-11-11, Q29: 4,838 healer
        /// visits in 4.2 h, each a trip, and trips are where bots die). A smaller loss is bought back when the bot is in
        /// town anyway (<see cref="WantsHealer"/>). A bot never owes more than 3 points (one loss at a time, OnDeath).</summary>
        public const int TripConstitutionLost = 3;

        /// <summary>Sickness time left that is worth a healer trip (unchanged: more than two minutes).</summary>
        public const long TripSicknessMilliseconds = 120_000;

        /// <summary>
        /// The healer rule as plain numbers (unit-tested). <paramref name="alreadyInTown"/>: the bot is on a town or
        /// service visit anyway, so any Constitution it can afford a point of is bought back. Otherwise only a big loss
        /// (3 points) or long sickness is worth the trip. Costs are unchanged.
        /// </summary>
        public static bool ShouldVisitHealer(int constitutionLost, bool canAffordPoint, long sicknessRemainingMilliseconds,
            bool alreadyInTown)
        {
            if (constitutionLost > 0 && canAffordPoint && (alreadyInTown || constitutionLost >= TripConstitutionLost))
                return true;
            return sicknessRemainingMilliseconds > TripSicknessMilliseconds;
        }

        /// <summary>A gamebot on a town or service visit anyway that should also see the healer: Constitution to buy back
        /// that it can afford at least a point of, or resurrection sickness with more than two minutes left. Hired
        /// companions go with their player.</summary>
        public static bool WantsHealer(GameBot bot) => WantsHealer(bot, alreadyInTown: true);

        /// <summary>A gamebot that should make a trip for the healer (task boundaries, ending a hunt): 3 points of
        /// Constitution owed, or more than two minutes of sickness left.</summary>
        public static bool WantsHealerTrip(GameBot bot) => WantsHealer(bot, alreadyInTown: false);

        private static bool WantsHealer(GameBot bot, bool alreadyInTown)
        {
            if (!Classic165Mode.IsOn || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup ||
                HiredCompanions165.IsHired(bot) || !bot.IsAlive)
                return false;
            int lost = bot.TotalConstitutionLostAtDeath;
            bool afford = lost > 0 && AutonomousBotEconomy.GetMoney(bot.DatabaseID) >= CostPerConPoint(bot.Level);
            ECSGameEffect sickness = EffectListService.GetEffectOnTarget(bot, eEffect.ResurrectionIllness);
            return ShouldVisitHealer(lost, afford, sickness?.GetRemainingTimeForClient() ?? 0, alreadyInTown);
        }

        /// <summary>At the healer: the sickness is cured and lost Constitution bought back as far as the coin goes.</summary>
        public static bool Visit(GameBot bot, GameHealer healer)
        {
            if (bot == null || healer == null)
                return false;
            bool done = false;
            foreach (eEffect type in new[] { eEffect.ResurrectionIllness, eEffect.RvrResurrectionIllness })
            {
                ECSGameEffect effect = EffectListService.GetEffectOnTarget(bot, type);
                if (effect != null)
                {
                    effect.End();
                    done = true;
                }
            }
            long cost = Math.Max(1, CostPerConPoint(bot.Level));
            int points = (int)Math.Min(bot.TotalConstitutionLostAtDeath, AutonomousBotEconomy.GetMoney(bot.DatabaseID) / cost);
            if (points > 0 && AutonomousBotEconomy.TrySpend(bot.DatabaseID, points * cost, BotMoneyLedger165.Kind.Healer))
            {
                bot.TotalConstitutionLostAtDeath -= points;
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
                done = true;
            }
            if (done)
                Log.Info($"BOT_HEALER_165 bot={bot.Name} id={bot.DatabaseID} level={bot.Level} healer=\"{healer.Name}\" con_restored={points} " +
                         $"copper={points * cost} con_still_lost={bot.TotalConstitutionLostAtDeath}");
            return done;
        }

        /// <summary>The player's visit to a healer cures the sickness of the companions with them too.</summary>
        public static void CureCompanions(GamePlayer player)
        {
            if (!Classic165Mode.IsOn || player == null)
                return;
            foreach (GameBot bot in CompanionWindows165.WithPlayer(player) ?? new List<GameBot>())
                foreach (eEffect type in new[] { eEffect.ResurrectionIllness, eEffect.RvrResurrectionIllness })
                    EffectListService.GetEffectOnTarget(bot, type)?.End();
        }

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(DeathPenalty165));
    }
}
