using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode crafter gamebots (owner 2026-10-30, plan section 6: crafting for bots, a 1.65-Mode-only feature).
    /// - About one gamebot in five has a primary craft (record CraftSkill / CraftLevel): weapon, armour, tailoring,
    ///   fletching, spellcraft, alchemy or siegecraft.
    /// - On a town errand (between tasks, out of combat) a crafter buys the materials of a real recipe of its craft near
    ///   its skill (the recipe's own ingredient prices), crafts it at 94-99% quality, gains skill like a player (more
    ///   for harder recipes, none for grey ones), and puts the product on the Realm Exchange.
    /// - A siegecrafter builds siege kits instead (ram first, catapult and trebuchet as its skill grows) and keeps a few;
    ///   siege crews buy them from it (SupplySiege). In 1.65 Mode there are no free siege engines and no frontier siege
    ///   merchants: period siege equipment was crafted (the 1.64 client's Siegecraft recipes).
    /// </summary>
    public static class CrafterBots165
    {
        public const int MaxSkill = 1100;
        public const int KitsKept = 2;
        private const long CraftEveryMilliseconds = 10 * 60_000;

        private static readonly eCraftingSkill[] Crafts =
        [
            eCraftingSkill.WeaponCrafting, eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring, eCraftingSkill.Fletching,
            eCraftingSkill.SpellCrafting, eCraftingSkill.Alchemy, eCraftingSkill.SiegeCrafting, eCraftingSkill.SiegeCrafting,
        ];

        /// <summary>Siege kits a siegecrafter can build, by the skill it needs.</summary>
        private static readonly (BotSiegeKind Kind, int Skill)[] SiegeKits =
            [(BotSiegeKind.Ram, 1), (BotSiegeKind.Catapult, 300), (BotSiegeKind.Trebuchet, 600)];

        /// <summary>A recipe of a craft: its product (template id, object type, slot, item level), recipe level and the
        /// merchant price of its materials.</summary>
        public sealed record Recipe(string ProductId, int Level, long MaterialCost, int ObjectType = 0, int ItemType = 0,
            int ProductLevel = 0);
        // per craft AND realm: an Albion crafter made Midgard leather and listed it on the Albion exchange, where nobody could
        // wear it (owner 2026-11-03: "bots across all crafting can only make gear from their faction")
        private static readonly ConcurrentDictionary<(eCraftingSkill, eRealm), Recipe[]> Recipes = new();
        private static readonly ConcurrentDictionary<long, long> NextCraft = new();

        /// <summary>A gamebot's craft, given once (fresh bots at creation; older bots the first time they work).</summary>
        public static eCraftingSkill CraftOf(OfflineWorldBotRecord record)
        {
            if (record == null || HiredCompanions165.IsHired(record.BotId))
                return eCraftingSkill.NoCrafting;
            if (record.CraftSkill == 0)
            {
                // one bot in five, spread evenly by id; siegecraft twice as likely (every siege needs it)
                record.CraftSkill = record.BotId % 5 == 0 ? (int)Crafts[(int)(record.BotId / 5 % Crafts.Length)] : -1;
                record.CraftLevel = record.CraftSkill > 0 ? 1 : 0;
                record.Dirty = true;
            }
            return record.CraftSkill > 0 ? (eCraftingSkill)record.CraftSkill : eCraftingSkill.NoCrafting;
        }

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(CrafterBots165));

        /// <summary>A crafter goes to town for a session when its last one was 30 minutes or more ago (TownErrands165).</summary>
        public static bool WantsSession(GameBot bot) =>
            bot?.PersistentRecord is { } record && CraftOf(record) != eCraftingSkill.NoCrafting &&
            (!NextCraft.TryGetValue(bot.DatabaseID, out long next) || GameLoop.GameLoopTime >= next + 20 * 60_000);

        /// <summary>On a town errand: craft one thing (at most every ten minutes).</summary>
        public static void Work(GameBot bot)
        {
            if (!Classic165Mode.IsOn || bot?.PersistentRecord is not { } record || bot.Inventory == null)
                return;
            // any gamebot at level 20, 30 or 40 may order its crafted set from the realm's crafters (CraftOrders165)
            CraftOrders165.PostMilestoneOrders(bot);
            eCraftingSkill craft = CraftOf(record);
            if (craft == eCraftingSkill.NoCrafting)
                return;
            long now = GameLoop.GameLoopTime;
            if (NextCraft.TryGetValue(bot.DatabaseID, out long next) && now < next)
                return;
            NextCraft[bot.DatabaseID] = now + CraftEveryMilliseconds;
            // orders first: a crafter's real income (veteran-bots review 2026-11-11, economy G5)
            if (craft is eCraftingSkill.ArmorCrafting or eCraftingSkill.Tailoring or eCraftingSkill.WeaponCrafting)
                CraftOrders165.FillOrders(bot, record, craft);
            // A session is several combines, as a player's is (run 5, 2026-10-30: one combine per 30-minute errand left the
            // best bot crafter at skill 5 after five hours, so siege kits beyond rams and real crafted gear never came).
            // It stops when the bot can't pay for the next materials or has no room.
            var products = new List<Made>();
            for (int i = 0; i < CombinesPerSession(craft); i++)
            {
                long before = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
                if (craft == eCraftingSkill.SiegeCrafting)
                    BuildSiegeKit(bot, record);
                else if (CraftRecipe(bot, record, craft) is { } made)
                    products.Add(made);
                if (AutonomousBotEconomy.GetMoney(bot.DatabaseID) >= before)
                    break;   // nothing bought: out of coin, room or recipes
            }
            if (products.Count > 0)
                FinishSession(bot, record, craft, products);
        }

        /// <summary>
        /// Combines per session: 10, alchemy 3 (veteran-bots review 2026-11-11, economy G2: an alchemist's potions and
        /// tinctures can't be salvaged, so every product beyond the one it lists went to a merchant at a loss).
        /// </summary>
        public static int CombinesPerSession(eCraftingSkill craft) => craft == eCraftingSkill.Alchemy ? 3 : 10;

        /// <summary>A crafted product waiting for the end of the session (not in the bag yet).</summary>
        private sealed record Made(DbInventoryItem Item, DbItemTemplate Template, Recipe Recipe);

        /// <summary>
        /// The recipe for the next combine (veteran-bots review 2026-11-11, economy G2): a random one in the band
        /// [skill - 3, skill + 10], weighted toward the top, instead of always the single hardest one - every crafter of a
        /// realm at a skill made the same item (49 "minor weakening poison", 31 "bronze dagger" listings). The band is never
        /// grey (no skill-up lost). With nothing in the band: the hardest recipe it can make, else the line's easiest (a new
        /// weapon, armour or tailoring crafter starts at 15, beyond skill 1 + 10).
        /// </summary>
        public static Recipe PickRecipe(Recipe[] all, int skill, Random random)
        {
            if (all == null || all.Length == 0)
                return null;
            int low = skill - 3, high = skill + 10;
            int total = 0;
            foreach (Recipe r in all)
                if (r.Level >= low && r.Level <= high)
                    total += 1 + r.Level - low;
            if (total == 0)
                return all.Where(r => r.Level <= high).OrderByDescending(r => r.Level).FirstOrDefault() ?? all[0];
            int roll = (random ?? Random.Shared).Next(total);
            foreach (Recipe r in all)
            {
                if (r.Level < low || r.Level > high)
                    continue;
                roll -= 1 + r.Level - low;
                if (roll < 0)
                    return r;
            }
            return all[0];
        }

        /// <summary>
        /// The product of a session that is listed (veteran-bots review 2026-11-11, economy G2): only one per session, the
        /// best - a masterpiece, else the highest quality, gear before stackables - so the realm's listings get the 99%s and
        /// masterpieces players sold, and an enchant (which needs a free listing) goes on that one piece.
        /// </summary>
        public static int BestProductIndex(IReadOnlyList<(bool Stackable, int Quality)> products)
        {
            int best = -1;
            for (int i = 0; i < products.Count; i++)
            {
                if (best < 0 || products[best].Stackable && !products[i].Stackable ||
                    products[best].Stackable == products[i].Stackable && products[i].Quality > products[best].Quality)
                    best = i;
            }
            return best;
        }

        /// <summary>
        /// What an unlisted product brings back (veteran-bots review 2026-11-11, economy G2): the better of a merchant's price
        /// and its salvage (the server's crafted-item salvage, SalvageCalculator: materials worth 45% of its value, halved
        /// for crafted items), and never less than half its materials (today's floor). Players salvaged skill-up items or
        /// sold them, whichever paid.
        /// </summary>
        public static long DisposalValue(long materialCost, long vendorSale, long itemPrice, bool stackable)
        {
            long salvage = stackable ? 0 : (long)(itemPrice * 0.45 / 2);
            return Math.Max(materialCost / 2, Math.Max(vendorSale, salvage));
        }

        private static Made CraftRecipe(GameBot bot, OfflineWorldBotRecord record, eCraftingSkill craft)
        {
            Recipe[] all = Recipes.GetOrAdd((craft, bot.Realm), key => LoadRecipes(key.Item1, key.Item2));
            int skill = Math.Max(1, record.CraftLevel);
            Recipe recipe = PickRecipe(all, skill, Random.Shared);
            // a crafter keeps the coin it needs for repairs and arrows, as a shopper does (veteran-bots review 2026-11-11:
            // crafters averaged 0.56 gold at levels 15-29, below the non-crafters' 1.97)
            if (recipe == null || bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid ||
                AutonomousBotEconomy.GetMoney(bot.DatabaseID) - recipe.MaterialCost < AutonomousWorldBotController.ShoppingReserve(bot.Level) ||
                !AutonomousBotEconomy.TrySpend(bot.DatabaseID, recipe.MaterialCost, BotMoneyLedger165.Kind.CraftMaterials))
                return null;
            Gain(bot, record, recipe.Level - skill);
            if (Util.Chance(Math.Clamp((recipe.Level - skill) * 2, 0, 40)))
                return null;   // failed: the materials are spent
            DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(recipe.ProductId);
            if (template == null)
                return null;
            return new Made(MakeProduct(bot, template), template, recipe);
        }

        /// <summary>
        /// A crafted product as a player's (AbstractCraftingSkill.BuildCraftedItem): gear is a unique copy whose quality and
        /// any enchant are saved with it and stay on the Realm Exchange and with the buyer (setting the quality on the
        /// shared template changed every copy of that item); 94-99% quality, 2% a masterpiece.
        /// </summary>
        private static DbInventoryItem MakeProduct(GameBot bot, DbItemTemplate template)
        {
            DbInventoryItem product = template.IsStackable ? GameInventoryItem.Create(template) : GameInventoryItem.Create(new DbItemUnique(template));
            product.IsCrafted = true;
            product.Creator = bot.Name;
            if (!template.IsStackable)
                product.Quality = Util.Chance(2) ? 100 : 94 + Util.Random(5);
            // the realm's spellcrafter / alchemist bots may enchant it first, paid by this crafter (owner 2026-11-08)
            BotEnchant165.Result enchant = BotEnchant165.TryEnchant(bot, product);
            eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid || !bot.Inventory.AddItem(slot, product))
                return;
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            // 2026-11-11 exchange plan: the product is listed only when worth a slot - its two crafted-only slots, a free
            // general one, or replacing the weakest listing it may replace by 1.5 x its gain - at a fair ask (at least
            // 1.1 x its materials) that undercuts the copies already listed; otherwise a merchant buys it, as before
            long materials = recipe.MaterialCost + (enchant.Done ? enchant.Cost : 0);
            AutonomousBotEconomy.ListingCandidate plan = AutonomousBotEconomy.PlanCraftedListing(bot, product, materials, ItemValue165.RollMood());
            // (an enchanted piece's materials include the enchant: gems, tincture and fees are not given away)
            int price = plan?.PriceCopper ?? 0;
            bool listed = plan != null && AutonomousBotEconomy.TryListPlanned(bot, plan, price, out _);
            if (!listed)
            {
                Made made = products[i];
                DbInventoryItem product = made.Item;
                int price = 0;
                bool listed = false;
                BotEnchant165.Result enchant = new();
                if (i == best)
                {
                    // the realm's spellcrafter / alchemist bots may enchant it first, paid by this crafter (owner 2026-11-08)
                    enchant = BotEnchant165.TryEnchant(bot, product);
                    eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    if (slot != eInventorySlot.Invalid && bot.Inventory.AddItem(slot, product))
                    {
                        AutonomousBotEconomy.MarkInventoryChanged(bot);
                        price = Math.Max(1, AutonomousBotEconomy.RecommendListingPrice(product, Util.Random(30) / 100.0));
                        // an enchanted piece asks at least what the enchant cost on top (gems, tincture and fees are not given away)
                        if (enchant.Done)
                            price = (int)Math.Min(int.MaxValue, price + enchant.Cost);
                        listed = AutonomousBotEconomy.TryList(bot, product, price);
                        if (!listed)
                            bot.Inventory.RemoveItem(product);
                    }
                }
                if (!listed)
                {
                    // no exchange space, or not the session's best: salvaged or sold for what it fetches (at least half the
                    // materials back - half-materials-only made crafting a money sink, owner 2026-11-05 audit)
                    long value = DisposalValue(made.Recipe.MaterialCost, AutonomousBotEconomy.CalculateStandardVendorSaleCopper(product),
                        product.Price, made.Template.IsStackable);
                    AutonomousBotEconomy.AddMoney(bot.DatabaseID, value, BotMoneyLedger165.Kind.CraftSale);
                }
                Log.Info($"BOT_CRAFTED_165 bot={bot.Name} id={bot.DatabaseID} craft={craft} skill={record.CraftLevel} " +
                         $"item=\"{made.Template.Name}\" recipe={made.Recipe.Level} listed={listed} price={price} enchanted={enchant.Done}");
            }
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
        }

        private static void BuildSiegeKit(GameBot bot, OfflineWorldBotRecord record)
        {
            int skill = Math.Max(1, record.CraftLevel);
            foreach ((BotSiegeKind kind, int need) in SiegeKits.Reverse())
            {
                if (skill < need)
                    continue;
                string kitId = BotSiegeRuntime.Kit(bot.Realm, kind);
                int have = bot.Inventory.AllItems.Where(i => i != null && i.Id_nb == kitId).Sum(i => Math.Max(1, i.Count));
                if (have >= KitsKept)
                    continue;
                DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(kitId);
                long cost = template == null ? 0 : Math.Max(1, template.Price / 5);   // the wood, metal, leather and parts
                eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (template == null || slot == eInventorySlot.Invalid || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, cost, BotMoneyLedger165.Kind.CraftMaterials))
                    return;
                Gain(bot, record, need + 100 - skill);
                DbInventoryItem kit = GameInventoryItem.Create(template);
                kit.IsCrafted = true;
                kit.Creator = bot.Name;
                if (!bot.Inventory.AddItem(slot, kit))
                {
                    AutonomousBotEconomy.AddMoney(bot.DatabaseID, cost, BotMoneyLedger165.Kind.CraftMaterials);
                    return;
                }
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
                Log.Info($"BOT_CRAFTED_165 bot={bot.Name} id={bot.DatabaseID} craft=SiegeCrafting skill={record.CraftLevel} kit={kind}");
                return;
            }
            // then the realm's catapult stones and ballista bolts for its siege crews (owner 2026-11-06: "they will use ammo and
            // craft ammo and restock ammo before a siege")
            if (skill >= SiegeAmmo165.BasicRecipeSkill && CraftAmmoStock(bot, record, skill))
                return;
            // nothing to build: practise on the cheapest kit's parts for skill (materials only)
            if (skill < MaxSkill && AutonomousBotEconomy.TrySpend(bot.DatabaseID, 5_000, BotMoneyLedger165.Kind.CraftMaterials))
                Gain(bot, record, 0);
        }

        /// <summary>Pieces of each merchant ammunition (stone, bolt) a siegecrafter keeps for crews: one period stack.</summary>
        public const int AmmoKept = 20;

        private static readonly ConcurrentDictionary<string, long> AmmoMaterialCost = new();

        /// <summary>The materials of one piece by its 1.64 recipe (each ingredient's price x count), else the item's price.</summary>
        public static long AmmoMaterials(DbItemTemplate ammo) => AmmoMaterialCost.GetOrAdd(ammo.Id_nb, id =>
        {
            long cost = 0;
            foreach (DbCraftedXItem part in DOLDB<DbCraftedXItem>.SelectObjects(DB.Column("CraftedItemId_nb").IsEqualTo(id)))
                cost += (GameServer.Database.FindObjectByKey<DbItemTemplate>(part.IngredientId_nb)?.Price ?? 0) * Math.Max(1, part.Count);
            return cost > 0 ? cost : Math.Max(1, ammo.Price);
        });

        /// <summary>A siegecrafter tops up its stock of the realm's stones or bolts in one batch (one combine).</summary>
        private static bool CraftAmmoStock(GameBot bot, OfflineWorldBotRecord record, int skill)
        {
            foreach (SiegeAmmoKind kind in new[] { SiegeAmmoKind.CatapultStone, SiegeAmmoKind.BallistaBolt })
            {
                DbItemTemplate ammo = SiegeAmmo165.Template(SiegeAmmo165.AmmoId(bot.Realm, kind));
                if (ammo == null)
                    continue;
                long unit = AmmoMaterials(ammo);
                int have = bot.Inventory.CountItemTemplate(ammo.Id_nb, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                int count = SiegeAmmo165.RestockCount(have, AutonomousBotEconomy.GetMoney(bot.DatabaseID), unit, AmmoKept);
                if (count <= 0)
                    continue;
                long cost = checked(unit * count);
                if (!AutonomousBotEconomy.TrySpend(bot.DatabaseID, cost, BotMoneyLedger165.Kind.CraftMaterials))
                    return false;
                if (!SiegeAmmo165.Give(bot, ammo, count))
                {
                    AutonomousBotEconomy.AddMoney(bot.DatabaseID, cost, BotMoneyLedger165.Kind.CraftMaterials);
                    return false;
                }
                Gain(bot, record, SiegeAmmo165.BasicRecipeSkill - skill);
                Log.Info($"BOT_CRAFTED_165 bot={bot.Name} id={bot.DatabaseID} craft=SiegeCrafting skill={record.CraftLevel} ammo={ammo.Id_nb} count={count}");
                return true;
            }
            return false;
        }

        /// <summary>A siegecrafter crew makes its own stones or bolts, paying the recipe's materials. Returns the pieces made.</summary>
        public static int CraftOwnAmmo(GameBot crew, DbItemTemplate ammo, int want, SiegeAmmo165.Purse purse)
        {
            if (!Classic165Mode.IsOn || ammo == null || want <= 0 || crew?.PersistentRecord is not { } record ||
                CraftOf(record) != eCraftingSkill.SiegeCrafting || record.CraftLevel < SiegeAmmo165.BasicRecipeSkill)
                return 0;
            long unit = AmmoMaterials(ammo);
            int count = SiegeAmmo165.RestockCount(0, purse.Money(), unit, want);
            if (count <= 0)
                return 0;
            long cost = checked(unit * count);
            if (!purse.Spend(cost))
                return 0;
            if (!SiegeAmmo165.Give(crew, ammo, count))
            {
                purse.Refund(cost);
                return 0;
            }
            Gain(crew, record, SiegeAmmo165.BasicRecipeSkill - record.CraftLevel);
            return count;
        }

        /// <summary>A crew buys stones or bolts from the stock of a siegecrafter gamebot of its realm (at the merchant price,
        /// handed over as SupplySiege hands over kits). Returns the pieces bought.</summary>
        public static int SellAmmo(GameBot crew, DbItemTemplate ammo, int want, SiegeAmmo165.Purse purse)
        {
            if (!Classic165Mode.IsOn || ammo == null || want <= 0 || crew?.Inventory == null)
                return 0;
            long unit = Math.Max(1, ammo.Price);
            foreach (GameBot crafter in AutonomousBotRegistry.Snapshot())
            {
                if (crafter == crew || crafter.Realm != crew.Realm || crafter.Inventory == null ||
                    CraftOf(crafter.PersistentRecord) != eCraftingSkill.SiegeCrafting)
                    continue;
                int stock = crafter.Inventory.CountItemTemplate(ammo.Id_nb, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (stock <= 0)
                    continue;
                int count = SiegeAmmo165.RestockCount(0, purse.Money(), unit, Math.Min(want, stock));
                if (count <= 0)
                    return 0;
                long cost = checked(unit * count);
                lock (crafter.Inventory.Lock)
                {
                    if (!crafter.Inventory.RemoveTemplate(ammo.Id_nb, count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                        continue;
                }
                if (!purse.Spend(cost))
                {
                    SiegeAmmo165.Give(crafter, ammo, count);
                    return 0;
                }
                if (!SiegeAmmo165.Give(crew, ammo, count))
                {
                    purse.Refund(cost);
                    SiegeAmmo165.Give(crafter, ammo, count);
                    return 0;
                }
                AutonomousBotEconomy.AddMoney(crafter.DatabaseID, cost, BotMoneyLedger165.Kind.Siege);
                AutonomousBotEconomy.MarkInventoryChanged(crafter);
                crafter.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(crafter, true);
                return count;
            }
            return 0;
        }

        /// <summary>Skill gain like a player's: likelier the harder the recipe compared to the skill, none when grey.</summary>
        private static void Gain(GameBot bot, OfflineWorldBotRecord record, int difficulty) => Gain(record, difficulty);

        /// <summary>The same gain for a bot's record (BotEnchant165: an enchanter bot crafting the gems / tincture).</summary>
        internal static void Gain(OfflineWorldBotRecord record, int difficulty)
        {
            if (record == null)
                return;
            int chance = difficulty >= 0 ? 80 : difficulty >= -15 ? 60 : difficulty >= -30 ? 35 : difficulty >= -50 ? 15 : 0;
            if (record.CraftLevel < MaxSkill && Util.Chance(chance))
            {
                record.CraftLevel++;
                record.Dirty = true;
            }
        }

        // the ingredients of each craft's recipes (a crafter keeps and buys only these)
        private static readonly ConcurrentDictionary<eCraftingSkill, HashSet<string>> Ingredients = new();

        /// <summary>Whether this template is an ingredient of one of the craft's recipes (built once per craft).</summary>
        public static bool UsesIngredient(eCraftingSkill craft, string templateId) =>
            craft != eCraftingSkill.NoCrafting && !string.IsNullOrEmpty(templateId) &&
            Ingredients.GetOrAdd(craft, LoadIngredients).Contains(templateId);

        private static HashSet<string> LoadIngredients(eCraftingSkill craft)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var products = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DbCraftedItem crafted in DOLDB<DbCraftedItem>.SelectObjects(DB.Column("CraftingSkillType").IsEqualTo((int)craft)) ?? [])
                    if (!string.IsNullOrEmpty(crafted?.Id_nb))
                        products.Add(crafted.Id_nb);
                if (products.Count > 0)
                    foreach (DbCraftedXItem part in GameServer.Database.SelectAllObjects<DbCraftedXItem>() ?? [])
                        if (part != null && !string.IsNullOrEmpty(part.IngredientId_nb) && products.Contains(part.CraftedItemId_nb ?? string.Empty))
                            ids.Add(part.IngredientId_nb);
            }
            catch (Exception)
            {
                // no database (tests): no ingredients known
            }
            return ids;
        }

        /// <summary>The recipes of a craft with their material cost (each ingredient's template price x count).</summary>
        // Only recipes whose product is of the crafter's realm or of none (a player of that realm crafts only those).
        private static Recipe[] LoadRecipes(eCraftingSkill craft, eRealm realm)
        {
            var list = new List<Recipe>();
            foreach (DbCraftedItem crafted in DOLDB<DbCraftedItem>.SelectObjects(DB.Column("CraftingSkillType").IsEqualTo((int)craft)))
            {
                DbItemTemplate product = GameServer.Database.FindObjectByKey<DbItemTemplate>(crafted.Id_nb);
                if (product == null || product.Realm != 0 && product.Realm != (int)realm)
                    continue;
                long cost = 0;
                foreach (DbCraftedXItem part in DOLDB<DbCraftedXItem>.SelectObjects(DB.Column("CraftedItemId_nb").IsEqualTo(crafted.Id_nb)))
                {
                    DbItemTemplate material = GameServer.Database.FindObjectByKey<DbItemTemplate>(part.IngredientId_nb);
                    cost += (material?.Price ?? 0) * Math.Max(1, part.Count);
                }
                if (cost > 0)
                    list.Add(new Recipe(crafted.Id_nb, crafted.CraftingLevel, cost, product.Object_Type, product.Item_Type, product.Level));
            }
            return list.OrderBy(r => r.Level).ToArray();
        }

        /// <summary>
        /// A siege crew buys a kit from a siegecrafter of its realm (the realm's crafters, wherever they are; the kit is
        /// handed over as a trade by courier would be). Returns true when the crew now holds the kit.
        /// </summary>
        /// <summary>Whether SupplySiege could hand this crew the kit now: a siegecrafter of its realm holds one, the crew
        /// can pay a quarter of its price and has a free backpack slot.</summary>
        public static bool CanSupplySiege(GameBot crew, string kitId)
        {
            if (!Classic165Mode.IsOn || crew?.Inventory == null || string.IsNullOrEmpty(kitId))
                return false;
            if (crew.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid)
                return false;
            long price = Math.Max(1, (GameServer.Database.FindObjectByKey<DbItemTemplate>(kitId)?.Price ?? 50_000) / 4);
            if (AutonomousBotEconomy.GetMoney(crew.DatabaseID) < price)
                return false;
            foreach (GameBot crafter in AutonomousBotRegistry.Snapshot())
                if (crafter != crew && crafter.Realm == crew.Realm && crafter.Inventory != null &&
                    CraftOf(crafter.PersistentRecord) == eCraftingSkill.SiegeCrafting && BotSiegeRuntime.Item(crafter, kitId) != null)
                    return true;
            return false;
        }

        public static bool SupplySiege(GameBot crew, string kitId)
        {
            if (!Classic165Mode.IsOn || crew?.Inventory == null || BotSiegeRuntime.Item(crew, kitId) != null)
                return BotSiegeRuntime.Item(crew, kitId) != null;
            eInventorySlot slot = crew.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid)
                return false;
            foreach (GameBot crafter in AutonomousBotRegistry.Snapshot())
            {
                if (crafter == crew || crafter.Realm != crew.Realm || crafter.Inventory == null ||
                    CraftOf(crafter.PersistentRecord) != eCraftingSkill.SiegeCrafting)
                    continue;
                DbInventoryItem kit = BotSiegeRuntime.Item(crafter, kitId);
                if (kit == null)
                    continue;
                long price = Math.Max(1, (GameServer.Database.FindObjectByKey<DbItemTemplate>(kitId)?.Price ?? 50_000) / 4);
                if (!AutonomousBotEconomy.TrySpend(crew.DatabaseID, price, BotMoneyLedger165.Kind.Siege))
                    return false;
                lock (crafter.Inventory.Lock)
                {
                    if (!crafter.Inventory.RemoveItemWithoutDbDeletion(kit))
                    {
                        AutonomousBotEconomy.AddMoney(crew.DatabaseID, price, BotMoneyLedger165.Kind.Siege);
                        continue;
                    }
                }
                kit.OwnerID = AutonomousBotEconomy.GetOwnerId(crew.DatabaseID);
                if (!crew.Inventory.AddItem(slot, kit))
                {
                    crafter.Inventory.AddItem(crafter.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), kit);
                    AutonomousBotEconomy.AddMoney(crew.DatabaseID, price, BotMoneyLedger165.Kind.Siege);
                    return false;
                }
                AutonomousBotEconomy.AddMoney(crafter.DatabaseID, price, BotMoneyLedger165.Kind.Siege);
                AutonomousBotEconomy.MarkInventoryChanged(crafter);
                AutonomousBotEconomy.MarkInventoryChanged(crew);
                crafter.MarkAutonomousStateDirty();
                crew.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(crafter, true);
                AutonomousBotStatusPersistence.Queue(crew, true);
                return true;
            }
            return false;
        }
    }
}
