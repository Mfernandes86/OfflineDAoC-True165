using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Craft orders and milestone crafted sets (veteran-bots review 2026-11-11, M9 / economy G5). A veteran bought a
    /// crafted 94-99% set from a realm crafter around levels 20, 30 and 40 (FreddysHouse: "Get saving for your next SC";
    /// a level-32 Cleric with 200 gold is told to buy crafted gear), and crafters mostly made gear to order, priced as a
    /// multiple of the materials. Gamebots had no such arc: gear came only from vendors, loot and single listings, and
    /// crafters had no buyer for their work.
    /// - Buyer: a gamebot that has reached 20, 30 or 40 works out once (cached) the crafted set it would wear at that
    ///   milestone - its heaviest usable armour for the six body slots, and its trained main weapon - skipping slots whose
    ///   worn piece is already as good. When its coin covers the whole set over its reserve, it posts one order per slot
    ///   into its realm's queue. Done on a town errand (CrafterBots165.Work), never mid-grind.
    /// - Crafter: a crafter of the matching craft on its own errand takes at most two orders it has the skill for, pays
    ///   the materials, crafts the exact recipe (the same skill gain and failure chance as any combine) and delivers it:
    ///   the buyer pays materials x 1.75 (the period's 1.5-2), the piece goes into the buyer's bag and is worn when it is
    ///   an upgrade. No listing slot is used.
    /// - An order nobody fills in two hours lapses; the buyer may post again on a later errand.
    /// Light at 6,000 bots: one plan per bot per milestone, one queue per realm, at most 8 queue entries looked at per
    /// crafter errand, no scans.
    /// </summary>
    public static class CraftOrders165
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(CraftOrders165));

        public static readonly int[] Milestones = [20, 30, 40];
        public const double Markup = 1.75;
        public const long OrderLifetimeMilliseconds = 2 * 60 * 60_000;
        public const int MaxFilledPerErrand = 2;
        public const int MaxScannedPerErrand = 8;

        /// <summary>One slot of a milestone set: the recipe and craft that make it, and what the buyer pays.</summary>
        public sealed record Piece(eCraftingSkill Craft, CrafterBots165.Recipe Recipe, eInventorySlot Slot, long Price);

        public sealed record Order(long BuyerId, eRealm Realm, Piece Piece, int Milestone, long PostedTick);

        private sealed class BuyerState
        {
            public int Milestone;
            public Piece[] Plan;
            public int Outstanding;
            public long LastPostTick = long.MinValue;
        }

        private static readonly ConcurrentDictionary<eRealm, ConcurrentQueue<Order>> Queues = new();
        private static readonly ConcurrentDictionary<long, BuyerState> Buyers = new();

        /// <summary>The highest milestone at or below the level, 0 below the first.</summary>
        public static int MilestoneFor(int level)
        {
            int found = 0;
            foreach (int m in Milestones)
                if (level >= m)
                    found = m;
            return found;
        }

        /// <summary>Price a buyer pays for a piece: its materials x 1.75.</summary>
        public static long PriceOf(long materialCost) => Math.Max(1, (long)Math.Ceiling(materialCost * Markup));

        /// <summary>Whether a buyer with this coin and reserve can pay for the whole set.</summary>
        public static bool CanAfford(long coin, long reserve, IEnumerable<Piece> plan) =>
            plan != null && coin - reserve >= plan.Sum(p => p.Price);

        /// <summary>Whether a crafter of this skill can make the recipe (the same reach as its own combines).</summary>
        public static bool CanMake(int crafterSkill, CrafterBots165.Recipe recipe) =>
            recipe != null && recipe.Level <= Math.Max(1, crafterSkill) + 10;

        /// <summary>Tests: forget every order and plan.</summary>
        public static void Reset()
        {
            Queues.Clear();
            Buyers.Clear();
        }

        /// <summary>The bot left the world: its plan goes (its orders lapse on their own).</summary>
        public static void Forget(long botId) => Buyers.TryRemove(botId, out _);

        public static int QueuedCount(eRealm realm) => Queues.TryGetValue(realm, out var q) ? q.Count : 0;

        /// <summary>Tests and the crafter side: put an order in its realm's queue.</summary>
        public static void Enqueue(Order order)
        {
            if (order != null)
                Queues.GetOrAdd(order.Realm, _ => new ConcurrentQueue<Order>()).Enqueue(order);
        }

        #region Buyer

        /// <summary>
        /// On a town errand: a gamebot at a milestone posts its crafted set's orders once it can pay for all of them.
        /// Returns the number of orders posted.
        /// </summary>
        public static int PostMilestoneOrders(GameBot bot)
        {
            if (!Classic165Mode.IsOn || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper ||
                bot.Inventory == null || bot.DatabaseID <= 0 || HiredCompanions165.IsHired(bot))
                return 0;
            int milestone = MilestoneFor(bot.Level);
            if (milestone == 0)
                return 0;
            BuyerState state = Buyers.GetOrAdd(bot.DatabaseID, _ => new BuyerState());
            long now = GameLoop.GameLoopTime;
            lock (state)
            {
                if (state.Outstanding > 0 || state.LastPostTick != long.MinValue && now - state.LastPostTick < OrderLifetimeMilliseconds)
                    return 0;
                if (state.Milestone != milestone || state.Plan == null)
                {
                    state.Milestone = milestone;
                    state.Plan = BuildPlan(bot, milestone);
                }
                // slots whose worn piece is now as good (a drop, a filled order) are left out
                Piece[] wanted = state.Plan.Where(p => WantsPiece(bot, p)).ToArray();
                if (wanted.Length == 0)
                    return 0;
                long coin = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
                if (!CanAfford(coin, AutonomousWorldBotController.ShoppingReserve(bot.Level), wanted))
                    return 0;
                foreach (Piece piece in wanted)
                    Enqueue(new Order(bot.DatabaseID, bot.Realm, piece, milestone, now));
                state.Outstanding = wanted.Length;
                state.LastPostTick = now;
                Log.Info($"BOT_CRAFT_ORDER_165 posted buyer={bot.Name} id={bot.DatabaseID} level={bot.Level} milestone={milestone} " +
                         $"pieces={wanted.Length} total={wanted.Sum(p => p.Price)} coin={coin}");
                return wanted.Length;
            }
        }

        /// <summary>The crafted set this bot would wear at the milestone: heaviest usable armour per body slot, its main weapon.</summary>
        private static Piece[] BuildPlan(GameBot bot, int milestone)
        {
            var plan = new List<Piece>();
            try
            {
                var armor = new List<(eCraftingSkill Craft, CrafterBots165.Recipe Recipe, DbItemTemplate Template, eInventorySlot Slot)>();
                foreach (eCraftingSkill craft in new[] { eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring, eCraftingSkill.WeaponCrafting })
                {
                    foreach (CrafterBots165.Recipe recipe in CrafterBots165.RecipesOf(craft, bot.Realm))
                    {
                        if (recipe.ProductLevel < milestone - 2 || recipe.ProductLevel > Math.Min(milestone + 1, bot.Level))
                            continue;
                        DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(recipe.ProductId);
                        if (template == null || template.IsStackable)
                            continue;
                        DbInventoryItem probe = GameInventoryItem.Create(template);
                        if (probe == null || !AutonomousBotEconomy.CanWear(bot, probe, out eInventorySlot slot))
                            continue;
                        bool body = slot is eInventorySlot.HeadArmor or eInventorySlot.TorsoArmor or eInventorySlot.LegsArmor or
                            eInventorySlot.ArmsArmor or eInventorySlot.HandsArmor or eInventorySlot.FeetArmor;
                        bool weapon = craft == eCraftingSkill.WeaponCrafting && slot is eInventorySlot.RightHandWeapon or eInventorySlot.TwoHandWeapon &&
                                      BotWeaponStats.IsMeleeWeapon((eObjectType)template.Object_Type) &&
                                      bot.BotSpec?.WeaponOneType == (eObjectType)template.Object_Type;
                        if (body || weapon)
                            armor.Add((craft, recipe, template, slot));
                    }
                }
                // per slot: the heaviest armour (absorb), then the level nearest the milestone; the weapon of the main hand
                foreach (var group in armor.GroupBy(a => a.Slot))
                {
                    var best = group.OrderByDescending(a => a.Template.SPD_ABS * (GlobalConstants.IsArmor(a.Template.Object_Type) ? 1 : 0))
                        .ThenBy(a => Math.Abs(a.Recipe.ProductLevel - milestone))
                        .ThenByDescending(a => a.Template.DPS_AF)
                        .First();
                    plan.Add(new Piece(best.Craft, best.Recipe, best.Slot, PriceOf(best.Recipe.MaterialCost)));
                }
            }
            catch (Exception e)
            {
                Log.Warn($"BOT_CRAFT_ORDER_165 plan failed bot={bot.Name} id={bot.DatabaseID}: {e.Message}");
            }
            return plan.ToArray();
        }

        /// <summary>Whether the bot still wants this piece: its worn one is not already as good as a 97% crafted copy.</summary>
        private static bool WantsPiece(GameBot bot, Piece piece)
        {
            DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(piece.Recipe.ProductId);
            if (template == null)
                return false;
            DbInventoryItem probe = GameInventoryItem.Create(template);
            if (probe == null)
                return false;
            probe.Quality = 97;
            DbInventoryItem worn = bot.Inventory.GetItem(piece.Slot);
            return worn == null || AutonomousBotEconomy.FitValue(bot, probe, piece.Slot) - AutonomousBotEconomy.FitValue(bot, worn, piece.Slot) >
                AutonomousBotEconomy.MinimumEquipmentUpgrade;
        }

        #endregion

        #region Crafter

        /// <summary>
        /// On a crafter's town errand: take up to two orders of its craft and realm it has the skill for, craft and deliver
        /// them. Returns the number delivered.
        /// </summary>
        public static int FillOrders(GameBot crafter, OfflineWorldBotRecord record, eCraftingSkill craft) =>
            crafter == null ? 0 : FillOrders(crafter, crafter.Realm, crafter.DatabaseID, record, craft);

        /// <summary>FillOrders for a crafter of this realm and id (tests give them without a live bot).</summary>
        public static int FillOrders(GameBot crafter, eRealm realm, long crafterId, OfflineWorldBotRecord record, eCraftingSkill craft)
        {
            if (!Classic165Mode.IsOn || crafter?.Inventory == null || record == null ||
                !Queues.TryGetValue(realm, out ConcurrentQueue<Order> queue))
                return 0;
            long now = GameLoop.GameLoopTime;
            int delivered = 0;
            var putBack = new List<Order>();
            for (int scanned = 0; scanned < MaxScannedPerErrand && delivered < MaxFilledPerErrand && queue.TryDequeue(out Order order); scanned++)
            {
                if (now - order.PostedTick > OrderLifetimeMilliseconds)
                {
                    Settle(order.BuyerId);
                    continue;
                }
                if (order.Piece.Craft != craft || !CanMake(record.CraftLevel, order.Piece.Recipe) || order.BuyerId == crafterId)
                {
                    putBack.Add(order);
                    continue;
                }
                switch (TryFill(crafter, record, order))
                {
                    case FillResult.Delivered:
                        delivered++;
                        Settle(order.BuyerId);
                        break;
                    case FillResult.Retry:
                        putBack.Add(order);
                        break;
                    default:
                        Settle(order.BuyerId);
                        break;
                }
            }
            foreach (Order order in putBack)
                queue.Enqueue(order);
            return delivered;
        }

        private enum FillResult { Delivered, Retry, Dropped }

        private static FillResult TryFill(GameBot crafter, OfflineWorldBotRecord record, Order order)
        {
            if (!AutonomousBotRegistry.TryGet(order.BuyerId, out GameBot buyer) || buyer?.Inventory == null)
                return FillResult.Retry;   // offline for now
            CrafterBots165.Recipe recipe = order.Piece.Recipe;
            // still wanted (a drop or an earlier order may have filled the slot since) and affordable
            if (AutonomousBotEconomy.GetMoney(buyer.DatabaseID) < order.Piece.Price || !WantsPiece(buyer, order.Piece))
                return FillResult.Dropped;
            eInventorySlot bag = buyer.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (bag == eInventorySlot.Invalid)
                return FillResult.Retry;
            DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(recipe.ProductId);
            if (template == null)
                return FillResult.Dropped;
            if (!AutonomousBotEconomy.TrySpend(crafter.DatabaseID, recipe.MaterialCost, BotMoneyLedger165.Kind.CraftMaterials))
                return FillResult.Retry;
            int skill = Math.Max(1, record.CraftLevel);
            CrafterBots165.Gain(record, recipe.Level - skill);
            if (Util.Chance(Math.Clamp((recipe.Level - skill) * 2, 0, 40)))
                return FillResult.Retry;   // failed combine: the crafter tries again on a later errand
            DbInventoryItem product = GameInventoryItem.Create(new DbItemUnique(template));
            product.IsCrafted = true;
            product.Creator = crafter.Name;
            product.Quality = Util.Chance(2) ? 100 : 94 + Util.Random(5);
            if (!AutonomousBotEconomy.TrySpend(buyer.DatabaseID, order.Piece.Price, BotMoneyLedger165.Kind.CraftOrder))
                return FillResult.Dropped;   // the crafter keeps the materials spent, as on a failed combine
            if (!buyer.Inventory.AddItem(bag, product))
            {
                AutonomousBotEconomy.AddMoney(buyer.DatabaseID, order.Piece.Price, BotMoneyLedger165.Kind.CraftOrder);
                return FillResult.Retry;
            }
            AutonomousBotEconomy.AddMoney(crafter.DatabaseID, order.Piece.Price, BotMoneyLedger165.Kind.CraftOrder);
            AutonomousBotEconomy.MarkInventoryChanged(buyer);
            // worn at once when it is an upgrade and the buyer isn't fighting; otherwise at its next errand
            bool worn = !buyer.InCombat && AutonomousBotEconomy.TryEquipOwnedUpgrade(buyer, product, "craft-order");
            buyer.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(buyer, true);
            crafter.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(crafter, true);
            Log.Info($"BOT_CRAFT_ORDER_165 delivered crafter={crafter.Name} id={crafter.DatabaseID} skill={record.CraftLevel} " +
                     $"buyer={buyer.Name} buyerId={buyer.DatabaseID} item=\"{template.Name}\" quality={product.Quality} " +
                     $"price={order.Piece.Price} materials={recipe.MaterialCost} worn={worn}");
            return FillResult.Delivered;
        }

        /// <summary>One of the buyer's orders is done (delivered, dropped or lapsed).</summary>
        private static void Settle(long buyerId)
        {
            if (!Buyers.TryGetValue(buyerId, out BuyerState state))
                return;
            lock (state)
                state.Outstanding = Math.Max(0, state.Outstanding - 1);
        }

        /// <summary>Tests: the outstanding orders of a buyer.</summary>
        public static int OutstandingOf(long buyerId) => Buyers.TryGetValue(buyerId, out BuyerState state) ? Volatile.Read(ref state.Outstanding) : 0;

        #endregion
    }
}
