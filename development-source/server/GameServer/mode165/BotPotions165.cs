using System;
using System.Collections.Concurrent;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Potions for bots (veteran-bots review 2026-11-11, M9 / economy G10 and owner decision O13). No bot code ever drank a
    /// potion, though the companion bag is documented as holding potions for the companion "to use", and merchants sell
    /// the Draughts of Healing, Power and Endurance (level 20, 9 charges, a 3-second drink, one a minute).
    /// - Drinking, the same for hired companions and gamebots: a heal draught at 35% health or less when hit while it is
    ///   neither casting nor swinging (a player can't drink a cast potion in attack mode: GamePlayer.UseSlot), and right
    ///   after a kill while another monster is still on it at 50% health or less; a power draught after a kill with an add
    ///   on it at 20% power or less (casters), an endurance draught at 20% endurance or less (fighters). The item's own
    ///   shared reuse timer is kept, a charge is used (the last one uses the item up), as a player's.
    /// - Buying (O13): a gamebot of level 20 or more with no heal draught buys one at a town errand from a potion merchant
    ///   near its merchant only when its coin is at least five times its reserve after paying; a second one when it is
    ///   twenty times. Hired companions don't shop: their player gives them potions.
    /// Cost: one health comparison per hit taken (already computed), a 40-slot bag look only when a threshold is crossed
    /// and the reuse timer is free; buying is errand-time only.
    /// </summary>
    public static class BotPotions165
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BotPotions165));

        public const int HealWhenHitPercent = 35;
        public const int HealAfterKillPercent = 50;
        public const int PowerPercent = 20;
        public const int EndurancePercent = 20;
        public const int MinBuyLevel = 20;
        public const int RichReserveMultiple = 5;
        public const int VeryRichReserveMultiple = 20;

        public enum Kind { None, Heal, Power, Endurance }

        // bot id -> (timer group -> tick the next drink of that group is allowed)
        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<int, long>> NextDrink = new();

        /// <summary>Which drink a spell type is ("Heal", "PowerHeal", "EnduranceHeal"; anything else is not drunk).</summary>
        public static Kind KindOf(string spellType) => spellType switch
        {
            "Heal" => Kind.Heal,
            "PowerHeal" => Kind.Power,
            "EnduranceHeal" => Kind.Endurance,
            _ => Kind.None,
        };

        /// <summary>A drinkable potion: a backpack magical item with a spell and charges (GamePlayer.UseSlot's potion test).</summary>
        public static bool IsPotion(DbInventoryItem item) =>
            item != null && item.Object_Type == (int)eObjectType.Magical && item.Item_Type == (int)eInventorySlot.FirstBackpack &&
            item.SpellID > 0 && item.Charges > 0;

        /// <summary>When hit: a heal draught at 35% or less while neither casting nor swinging.</summary>
        public static bool WantsHealWhenHit(int healthPercent, bool casting, bool attacking) =>
            healthPercent > 0 && healthPercent <= HealWhenHitPercent && !casting && !attacking;

        /// <summary>After a kill with an add still on it: which drink, if any (heal first).</summary>
        public static Kind WantsAfterKill(int healthPercent, int powerPercent, bool usesPower, int endurancePercent, bool addsLeft)
        {
            if (!addsLeft)
                return Kind.None;
            if (healthPercent > 0 && healthPercent <= HealAfterKillPercent)
                return Kind.Heal;
            if (usesPower && powerPercent <= PowerPercent)
                return Kind.Power;
            if (!usesPower && endurancePercent <= EndurancePercent)
                return Kind.Endurance;
            return Kind.None;
        }

        /// <summary>How many heal draughts a gamebot wants to own at this level and coin (O13): 0, 1 or 2.</summary>
        public static int HealDraughtsWanted(int level, long coin, long reserve, long price)
        {
            if (level < MinBuyLevel || price <= 0)
                return 0;
            if (coin - 2 * price >= VeryRichReserveMultiple * reserve)
                return 2;
            return coin - price >= RichReserveMultiple * reserve ? 1 : 0;
        }

        private static bool Eligible(GameBot bot) =>
            Classic165Mode.IsOn && bot?.Inventory != null && bot.IsAlive && bot.DatabaseID > 0 &&
            (HiredCompanions165.IsHired(bot) || bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper);

        /// <summary>GameBot.TakeDamage: an emergency heal draught.</summary>
        public static void OnDamaged(GameBot bot)
        {
            if (bot == null || bot.HealthPercent > HealWhenHitPercent || !Eligible(bot))
                return;
            if (WantsHealWhenHit(bot.HealthPercent, bot.IsCasting, bot.attackComponent?.AttackState == true))
                TryDrink(bot, Kind.Heal);
        }

        /// <summary>GameBot.EnemyKilled: a draught between a kill and the add still on the bot.</summary>
        public static void AfterKill(GameBot bot)
        {
            if (bot == null || !Eligible(bot) || bot.IsCasting)
                return;
            bool addsLeft = (bot.attackComponent?.AttackerTracker?.Count ?? 0) > 0;
            if (!addsLeft)
                return;
            bool usesPower = bot.MaxMana > 0 && bot.CharacterClass?.ManaStat is not null and not eStat.UNDEFINED;
            Kind kind = WantsAfterKill(bot.HealthPercent, bot.ManaPercent, usesPower, bot.EndurancePercent, addsLeft);
            if (kind != Kind.None)
                TryDrink(bot, kind);
        }

        /// <summary>Drinks the first potion of this kind in the bag when its reuse timer is free. True when drunk.</summary>
        public static bool TryDrink(GameBot bot, Kind kind)
        {
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Potions_Effects);
            if (line == null)
                return false;
            long now = GameLoop.GameLoopTime;
            ConcurrentDictionary<int, long> timers = NextDrink.GetOrAdd(bot.DatabaseID, _ => new ConcurrentDictionary<int, long>());
            for (eInventorySlot slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
            {
                DbInventoryItem item = bot.Inventory.GetItem(slot);
                if (!IsPotion(item))
                    continue;
                int required = item.Template?.LevelRequirement > 0 ? item.Template.LevelRequirement : Math.Min(50, item.Level);
                if (required > bot.Level)
                    continue;
                Spell spell = SkillBase.FindSpell(item.SpellID, line);
                if (spell == null || KindOf(spell.SpellType.ToString()) != kind)
                    continue;
                if (timers.TryGetValue(spell.SharedTimerGroup, out long next) && now < next)
                    return false;   // the timer is shared: no other potion of this group either
                if (!bot.CastSpell(spell, line))
                    return false;
                timers[spell.SharedTimerGroup] = now + Math.Max(1, item.CanUseEvery) * 1000L;
                UseCharge(bot, item);
                FeatureCounters165.Add($"bot_potion_{kind.ToString().ToLowerInvariant()}");
                return true;
            }
            return false;
        }

        /// <summary>A charge used, as a player's: a stack loses one, the last charge uses the item up.</summary>
        private static void UseCharge(GameBot bot, DbInventoryItem item)
        {
            if (item.Count > 1)
                bot.Inventory.RemoveCountFromStack(item, 1);
            else if (--item.Charges < 1)
                bot.Inventory.RemoveCountFromStack(item, 1);
            if (bot.IsAutonomousWorldBot)
                AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
        }

        /// <summary>The bot left the world: its potion timers go.</summary>
        public static void Forget(long botId) => NextDrink.TryRemove(botId, out _);

        /// <summary>
        /// On a town errand (O13): a rich gamebot of level 20+ buys a heal draught from a potion merchant near its merchant.
        /// </summary>
        public static void RestockHealDraughts(GameBot bot, GameMerchant at)
        {
            if (!Classic165Mode.IsOn || bot?.Inventory == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper ||
                HiredCompanions165.IsHired(bot) || bot.Level < MinBuyLevel)
                return;
            long reserve = AutonomousWorldBotController.ShoppingReserve(bot.Level);
            long coin = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
            // the cheapest a draught can be: nothing to look at while the bot is far from rich
            if (HealDraughtsWanted(bot.Level, coin, reserve, 1) == 0)
                return;
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Potions_Effects);
            if (line == null)
                return;
            int owned = 0;
            for (eInventorySlot slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
                if (bot.Inventory.GetItem(slot) is { } item && IsPotion(item) &&
                    SkillBase.FindSpell(item.SpellID, line) is { } owns && KindOf(owns.SpellType.ToString()) == Kind.Heal)
                    owned++;
            DbItemTemplate draught = new[] { at }.Concat(bot.GetNPCsInRadius(2500).OfType<GameMerchant>())
                .Where(m => m != null && AutonomousBotEconomy.SellsForCoin(m) && m.TradeItems != null &&
                            (m.Realm == eRealm.None || m.Realm == bot.Realm))
                .SelectMany(m => m.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
                .Where(t => t.Price > 0 && t.Object_Type == (int)eObjectType.Magical && t.Item_Type == (int)eInventorySlot.FirstBackpack &&
                            t.SpellID > 0 && t.MaxCharges > 0 && (t.LevelRequirement > 0 ? t.LevelRequirement : t.Level) <= bot.Level &&
                            SkillBase.FindSpell(t.SpellID, line) is { } s && KindOf(s.SpellType.ToString()) == Kind.Heal)
                .OrderBy(t => t.Price)
                .FirstOrDefault();
            if (draught == null)
                return;
            int wanted = HealDraughtsWanted(bot.Level, coin, reserve, draught.Price);
            for (; owned < wanted; owned++)
            {
                eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (slot == eInventorySlot.Invalid || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, draught.Price, BotMoneyLedger165.Kind.Potion))
                    return;
                DbInventoryItem bought = GameInventoryItem.Create(draught);
                if (bought == null || !bot.Inventory.AddItem(slot, bought))
                {
                    AutonomousBotEconomy.AddMoney(bot.DatabaseID, draught.Price, BotMoneyLedger165.Kind.Potion);
                    return;
                }
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
                Log.Info($"BOT_POTION_165 bot={bot.Name} id={bot.DatabaseID} level={bot.Level} item=\"{draught.Name}\" copper={draught.Price}");
            }
        }
    }
}
