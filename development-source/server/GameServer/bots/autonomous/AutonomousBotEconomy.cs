using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Persistence bridge for independent world bots. Copper is always read from and
/// written to offline_world_bots; exchange inventory is always a real Inventory row.
/// </summary>
    public static class AutonomousBotEconomy
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private const string OwnerPrefix = "offlinebot:";
        public const int MinimumEquipmentUpgrade = 8;
        // Owner 2026-10-29: 3 posts per bot (was 1; Codex's 10 made a launcher refresh take minutes before the launcher split
        // bot snapshots from the exchange). Players stay unlimited.
        public const int MaxBotRealmExchangeListings = 3;
        private const int ServiceDecisionSafetyRefreshMilliseconds = 5 * 60_000;
        private sealed class ServiceDecisionState
        {
            public readonly object Gate = new();
            public int Revision;
            public int EvaluatedRevision = -1;
            public long NextSafetyRefreshTick;
            public eWorldServiceKind? CachedService;
        }
        private static readonly ConcurrentDictionary<long, ServiceDecisionState> ServiceDecisions = new();
        /// <summary>A listing plan: the bag item and its ask, its gain over a merchant, the dearest copy it bumps at a name's
        /// copy limit, and the bot's own weakest listing it replaces when its slots are full.</summary>
        public sealed record ListingCandidate(DbInventoryItem Item, int PriceCopper)
        {
            public long Gain { get; init; }
            public MarketBook.Listing Bump { get; init; }
            public MarketBook.Listing Replace { get; init; }
            public bool OwnCrafted { get; init; }
            /// <summary>The cheapest copy of this name another seller lists (0: none).</summary>
            public long CheapestOther { get; init; }
            /// <summary>The least this listing may ask (a crafter's 1.1 x materials; 0: the ask floor only).</summary>
            public long MinimumAsk { get; init; }
        }
    public sealed record PurchaseCandidate(DbInventoryItem Item, int Utility, eInventorySlot EquipSlot, bool IsEquipmentUpgrade, bool IsPlayerListing);

    public static string GetOwnerId(long botId) => $"{OwnerPrefix}{botId}";

    /// <summary>
    /// Invalidates only this bot's cached appraisal. Loot, purchases, sales and
    /// equipment moves call this at the mutation site, so ordinary AI turns do
    /// not rescan the entire backpack and Realm Exchange every 1.5 seconds.
    /// </summary>
    public static void MarkInventoryChanged(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.DatabaseID <= 0)
            return;
        Interlocked.Increment(ref ServiceDecisions.GetOrAdd(bot.DatabaseID, _ => new ServiceDecisionState()).Revision);
    }

    public static void MarkEconomyChanged(long botId)
    {
        if (botId <= 0)
            return;
        Interlocked.Increment(ref ServiceDecisions.GetOrAdd(botId, _ => new ServiceDecisionState()).Revision);
    }

    public static void ForgetCachedDecision(long botId)
    {
        if (botId > 0)
            ServiceDecisions.TryRemove(botId, out _);
    }

    public static bool TryParseOwnerId(string ownerId, out long botId)
    {
        botId = 0;
        return ownerId != null && ownerId.StartsWith(OwnerPrefix, StringComparison.OrdinalIgnoreCase) &&
               long.TryParse(ownerId.AsSpan(OwnerPrefix.Length), out botId) && botId > 0;
    }

    public static long GetMoney(long botId) => FindLiveOrStored(botId)?.MoneyCopper ?? 0;

    public static bool TrySpend(long botId, long copper, BotMoneyLedger165.Kind kind = BotMoneyLedger165.Kind.Other)
    {
        if (copper < 0)
            return false;

        lock (typeof(AutonomousBotEconomy))
        {
            bool isLive = TryGetLiveOrLeaving(botId, out GameBot live);
            OfflineWorldBotRecord bot = isLive ? live.PersistentRecord : Find(botId);
            if (bot == null || bot.MoneyCopper < copper)
                return false;

            bot.MoneyCopper -= copper;
            bot.LastUpdateUtc = DateTime.UtcNow.ToString("O");
            bot.Dirty = true;
            SaveMoneyChange(bot, isLive ? live : null);
            BotMoneyLedger165.Debit(kind, copper);
            return true;
        }
    }

    public static bool AddMoney(long botId, long copper, BotMoneyLedger165.Kind kind = BotMoneyLedger165.Kind.Other)
    {
        if (copper < 0)
            return false;

        lock (typeof(AutonomousBotEconomy))
        {
            bool isLive = TryGetLiveOrLeaving(botId, out GameBot live);
            OfflineWorldBotRecord bot = isLive ? live.PersistentRecord : Find(botId);
            if (bot == null || bot.MoneyCopper > long.MaxValue - copper)
                return false;

            bot.MoneyCopper += copper;
            bot.LastUpdateUtc = DateTime.UtcNow.ToString("O");
            bot.Dirty = true;
            SaveMoneyChange(bot, isLive ? live : null);
            BotMoneyLedger165.Credit(kind, copper);
            return true;
        }
    }

    /// <summary>
    /// A live bot, or one whose logout save is still waiting: its in-memory record is the newest copy, so money changes go
    /// there (an offline write to the stored row would be overwritten by that pending save).
    /// </summary>
    internal static bool TryGetLiveOrLeaving(long botId, out GameBot bot)
    {
        if (AutonomousBotRegistry.TryGet(botId, out bot) && bot?.PersistentRecord != null)
            return true;
        return AutonomousBotStatusPersistence.TryGetLoggingOut(botId, out bot) && bot?.PersistentRecord != null;
    }

    /// <summary>Saves a money change: queued for a live or leaving bot, written now (with a revision bump) for an offline one.</summary>
    private static void SaveMoneyChange(OfflineWorldBotRecord record, GameBot live)
    {
        if (live != null)
        {
            if (live.ObjectState != GameObject.eObjectState.Active)
                record.InventoryRevision++;
            AutonomousBotStatusPersistence.Queue(live);
            return;
        }
        // The login path compares this revision under the economy lock and reloads a bot written to while offline.
        record.InventoryRevision++;
        lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            GameServer.Database.SaveObject(record);
    }

    /// <summary>An offline bot's items changed (a listing came back to its backpack): bump its revision so a login in
    /// flight reloads them.</summary>
    public static void BumpOfflineRevision(long botId)
    {
        lock (typeof(AutonomousBotEconomy))
        {
            if (TryGetLiveOrLeaving(botId, out GameBot live))
            {
                live.PersistentRecord.InventoryRevision++;
                live.PersistentRecord.Dirty = true;
                AutonomousBotStatusPersistence.Queue(live);
                return;
            }
            OfflineWorldBotRecord record = Find(botId);
            if (record == null)
                return;
            record.InventoryRevision++;
            record.Dirty = true;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                GameServer.Database.SaveObject(record);
        }
    }

    public static DbInventoryItem[] FindAffordableExchangeItems(GameBot bot, int maximumResults = 24)
    {
        if (bot == null || bot.DatabaseID <= 0 || maximumResults <= 0)
            return Array.Empty<DbInventoryItem>();

        ushort lot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
        long money = GetMoney(bot.DatabaseID);
        string ownerId = GetOwnerId(bot.DatabaseID);
        DateTime now = DateTime.UtcNow;
        var found = new List<DbInventoryItem>();
        MarketCache.ForEach(new ItemQuery { Realm = bot.Realm }, item =>
        {
            if (item.OwnerLot == lot && item.IsTradable && item.SellPrice > 0 && item.SellPrice <= money &&
                item.OwnerID != ownerId && !RealmExchangeExpiry.IsExpired(item, now))
                found.Add(item);
        });
        return found.OrderBy(item => item.SellPrice).Take(maximumResults).ToArray();
    }

    /// <summary>Most listings a bot scores in full at the broker (the rest are ranked by a cheap value guess first).</summary>
    public const int FullyScoredListings = 8;
    /// <summary>The exchange sells gear up to two levels above the bot (vendors one): higher gear is left to bots that
    /// can use its bonuses.</summary>
    public const int PurchaseLevelsAbove = 2;

    /// <summary>
    /// Copper a bot may spend at the exchange: its coin less the same reserve merchant shopping keeps for repairs, arrows
    /// and Constitution (2026-11-11 audit: 30 of the last 50 exchange buyers were left under 5,000 copper).
    /// </summary>
    public static long PurchaseBudget(GameBot bot) =>
        bot == null ? 0 : GetMoney(bot.DatabaseID) - AutonomousWorldBotController.ShoppingReserve(bot.Level);

    /// <summary>
    /// Pure rule: the share of its spare coin a bot pays for an upgrade - about a quarter for a small one, up to 80% for
    /// an empty slot or a big jump.
    /// </summary>
    public static bool PurseAllows(long budget, long price, int improvement, bool emptySlot)
    {
        if (budget <= 0 || price > budget)
            return false;
        double share = emptySlot || improvement >= 60 ? 0.80 : 0.25 + 0.55 * Math.Clamp(improvement - MinimumEquipmentUpgrade, 0, 52) / 52d;
        return price <= budget * share;
    }

    /// <summary>
    /// The best purchase for this bot at its realm's broker (2026-11-11 exchange plan, section 2.5). Instead of copying,
    /// sorting and scoring the whole realm every 3-7 s, the bot reads only the slot buckets it can wear within its level
    /// band (MarketBook), ranks those by a cheap value guess against what it wears there (its wish list, worked out only
    /// when its gear or level changes), and fully scores at most <see cref="FullyScoredListings"/>. Archers, assassins and
    /// crafters also look at their own supplies (ammunition, poisons, their recipes' materials).
    /// </summary>
    public static PurchaseCandidate FindBestUsefulExchangePurchase(GameBot bot, int maximumResults = 48)
    {
        if (bot?.Inventory == null || bot.DatabaseID <= 0)
            return null;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            long budget = PurchaseBudget(bot);
            if (budget <= 0)
                return null;
            string ownerId = GetOwnerId(bot.DatabaseID);
            DateTime now = DateTime.UtcNow;
            ushort lot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
            List<(DbInventoryItem Item, int Guess)> gear = GearCandidates(bot, budget, ownerId, lot, now);
            PurchaseCandidate best = null;
            foreach ((DbInventoryItem item, _) in gear.OrderByDescending(entry => entry.Guess).Take(FullyScoredListings))
                best = Better(best, ScorePurchase(bot, item, budget));
            foreach (DbInventoryItem item in SupplyCandidates(bot, budget, ownerId, lot, now))
                best = Better(best, ScorePurchase(bot, item, budget));
            return best;
        }
        finally
        {
            RealmExchangeStats.NoteScan(System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>The old full scan with the same scoring (tests compare the wish-list probe with it).</summary>
    public static PurchaseCandidate FindBestUsefulExchangePurchaseFullScan(GameBot bot)
    {
        if (bot?.Inventory == null || bot.DatabaseID <= 0)
            return null;
        long budget = PurchaseBudget(bot);
        PurchaseCandidate best = null;
        foreach (DbInventoryItem item in FindAffordableExchangeItems(bot, int.MaxValue))
            best = Better(best, ScorePurchase(bot, item, budget));
        return best;
    }

    private static PurchaseCandidate Better(PurchaseCandidate best, PurchaseCandidate candidate)
    {
        if (candidate == null || candidate.Utility <= 0)
            return best;
        if (best == null || candidate.Utility > best.Utility ||
            candidate.Utility == best.Utility && candidate.Item.SellPrice < best.Item.SellPrice)
            return candidate;
        return best;
    }

    // ---- the wish list: what the bot wears per exchange slot bucket, worked out only when its gear or level changes

    private sealed class WishList
    {
        public int Revision = -1;
        public int Level = -1;
        public readonly Dictionary<int, (eInventorySlot Slot, int Worn)> Buckets = new();
        /// <summary>Whether the bot can use a kind of item (object type, slot, damage or shield size), worked out once per
        /// kind until its gear or level changes.</summary>
        public readonly Dictionary<int, bool> Usable = new();
    }

    private static readonly ConcurrentDictionary<long, WishList> WishLists = new();

    // exchange client-slot buckets (MarketSearchEngine.GetClientSlot) and the equipment slot each fills
    private static readonly (int Bucket, eInventorySlot Slot)[] WishBuckets =
    [
        (1, eInventorySlot.HeadArmor), (2, eInventorySlot.HandsArmor), (3, eInventorySlot.FeetArmor), (5, eInventorySlot.TorsoArmor),
        (7, eInventorySlot.LegsArmor), (8, eInventorySlot.ArmsArmor), (9, eInventorySlot.Neck), (6, eInventorySlot.Cloak),
        (4, eInventorySlot.Jewelry), (12, eInventorySlot.Waist), (13, eInventorySlot.LeftBracer), (15, eInventorySlot.LeftRing),
        (100, eInventorySlot.RightHandWeapon), (101, eInventorySlot.LeftHandWeapon), (102, eInventorySlot.TwoHandWeapon),
        (103, eInventorySlot.DistanceWeapon), (104, eInventorySlot.DistanceWeapon), (105, eInventorySlot.LeftHandWeapon),
    ];

    private static WishList WishOf(GameBot bot)
    {
        int revision = ServiceDecisions.TryGetValue(bot.DatabaseID, out ServiceDecisionState state) ? Volatile.Read(ref state.Revision) : 0;
        WishList wish = WishLists.GetOrAdd(bot.DatabaseID, _ => new WishList());
        lock (wish)
        {
            if (wish.Revision == revision && wish.Level == bot.Level)
                return wish;
            wish.Buckets.Clear();
            wish.Usable.Clear();
            foreach ((int bucket, eInventorySlot slot) in WishBuckets)
            {
                int worn = slot switch
                {
                    eInventorySlot.LeftRing => Math.Min(FitValue(bot, bot.Inventory.GetItem(eInventorySlot.LeftRing), slot),
                        FitValue(bot, bot.Inventory.GetItem(eInventorySlot.RightRing), eInventorySlot.RightRing)),
                    eInventorySlot.LeftBracer => Math.Min(FitValue(bot, bot.Inventory.GetItem(eInventorySlot.LeftBracer), slot),
                        FitValue(bot, bot.Inventory.GetItem(eInventorySlot.RightBracer), eInventorySlot.RightBracer)),
                    _ => FitValue(bot, bot.Inventory.GetItem(slot), slot),
                };
                wish.Buckets[bucket] = (slot, worn);
            }
            wish.Revision = revision;
            wish.Level = bot.Level;
            if (WishLists.Count > 16_384)
                WishLists.Clear();
            return wish;
        }
    }

    /// <summary>
    /// Whether the bot can use this kind of item at all (the proficiency part of TryGetEquipmentUpgrade): a melee
    /// weapon's proficiency, otherwise the server's own use check (armour type, shield size, weapon line). Remembered per
    /// kind on the wish list; an item limited to some classes or of another realm is checked on its own.
    /// </summary>
    private static bool UsableKind(GameBot bot, WishList wish, DbInventoryItem item)
    {
        DbItemTemplate template = item?.Template;
        eObjectType type = (eObjectType)(item?.Object_Type ?? 0);
        if (template == null || type == eObjectType.GenericItem)
            return false;
        bool shared = string.IsNullOrEmpty(template.AllowedClasses) && (template.Realm == 0 || template.Realm == (int)bot.Realm) &&
                      type != eObjectType.Instrument;
        int key = (item.Object_Type << 16) | ((item.Item_Type & 0xFF) << 8) | (item.Type_Damage & 0xFF);
        if (shared)
            lock (wish)
                if (wish.Usable.TryGetValue(key, out bool known))
                    return known;
        bool usable = BotWeaponStats.IsMeleeWeapon(type)
            ? BotWeaponStats.HasConfiguredWeaponProficiency(bot, template)
            : GameServer.ServerRules.CheckAbilityToUseItem(bot, template);
        if (shared)
            lock (wish)
                wish.Usable[key] = usable;
        return usable;
    }

    /// <summary>
    /// Listings in the bot's wearable slot buckets and level band that it can use (review 2026-11-11: the value guess
    /// favours heavy armour and big weapons, so a caster's top guesses were plate it can't wear and a usable upgrade was
    /// never scored), with a cheap value guess against what it wears.
    /// </summary>
    private static List<(DbInventoryItem Item, int Guess)> GearCandidates(GameBot bot, long budget, string ownerId, ushort lot, DateTime now)
    {
        WishList wish = WishOf(bot);
        int bandLow = MarketBook.BandOf(Math.Max(0, bot.Level - 15));
        int bandHigh = MarketBook.BandOf(bot.Level + PurchaseLevelsAbove);
        long maxPrice = (long)(budget * 0.80);
        var result = new List<(DbInventoryItem, int)>();
        foreach ((int bucket, (eInventorySlot slot, int worn)) in wish.Buckets)
        {
            List<MarketBook.Listing> listings = MarketCache.ReadBook(book => book.Probe(bot.Realm, bucket, bandLow, bandHigh, maxPrice, ownerId));
            foreach (MarketBook.Listing listing in listings)
            {
                DbInventoryItem item = listing.Item;
                if (item.OwnerLot != lot || item.Level > bot.Level + PurchaseLevelsAbove || item.LevelRequirement > bot.Level ||
                    !item.IsTradable || RealmExchangeExpiry.IsExpired(item, now))
                    continue;
                int guess = EquipmentValue(item) + WeaponSpecBonus(bot, item.Object_Type, slot) - worn;
                if (worn == 0)
                    guess += 250;
                if (guess > MinimumEquipmentUpgrade && UsableKind(bot, wish, item))
                    result.Add((item, guess));
            }
        }
        return result;
    }

    /// <summary>Most supply listings a broker check scores.</summary>
    public const int SupplyListingsScored = 24;

    /// <summary>The bot's own supplies on the exchange: an archer's ammunition, an assassin's poisons, a crafter's
    /// materials for its next recipes (each only while it is short of them). A crafter looks up only the templates it
    /// is short of (MarketBook by template), never the whole materials category.</summary>
    private static List<DbInventoryItem> SupplyCandidates(GameBot bot, long budget, string ownerId, ushort lot, DateTime now)
    {
        var result = new List<DbInventoryItem>();
        bool ammo = WantsAmmo(bot), poison = WantsPoison(bot);
        List<string> materials = ShortMaterials(bot);
        if (!ammo && !poison && materials == null)
            return result;
        int bandHigh = MarketBook.BandOf(bot.Level + PurchaseLevelsAbove);
        // only the categories it wants (MarketSearchEngine.GetClientSlot): ammunition and poisons
        foreach (int bucket in SupplyBuckets(ammo, poison))
        {
            List<MarketBook.Listing> listings = MarketCache.ReadBook(book => book.Probe(bot.Realm, bucket, 0, bandHigh, budget, ownerId));
            foreach (MarketBook.Listing listing in listings)
            {
                DbInventoryItem item = listing.Item;
                if (item.OwnerLot != lot || !item.IsTradable || RealmExchangeExpiry.IsExpired(item, now))
                    continue;
                eObjectType type = (eObjectType)item.Object_Type;
                if (ammo && type is eObjectType.Arrow or eObjectType.Bolt && IsOwnSupply(bot, item) ||
                    poison && type == eObjectType.Poison)
                    result.Add(item);
                if (result.Count >= SupplyListingsScored)
                    return result;
            }
        }
        if (materials != null)
        {
            int room = SupplyListingsScored - result.Count;
            foreach (MarketBook.Listing listing in MarketCache.ReadBook(book => book.OfTemplates(bot.Realm, materials, budget, ownerId, room)))
            {
                DbInventoryItem item = listing.Item;
                if (item.OwnerLot == lot && item.IsTradable && !RealmExchangeExpiry.IsExpired(item, now))
                    result.Add(item);
            }
        }
        return result;
    }

    private static IEnumerable<int> SupplyBuckets(bool ammo, bool poison)
    {
        if (ammo)
            yield return MarketSearchEngine.AmmunitionSlot;
        if (poison)
            yield return MarketSearchEngine.PoisonSlot;
    }

    /// <summary>The materials a crafter is short of for its next recipes (CrafterBots165.WantedMaterials), or null.</summary>
    private static List<string> ShortMaterials(GameBot bot)
    {
        if (bot?.Inventory == null || CrafterBots165.WantedMaterials(bot.PersistentRecord, bot.Realm) is not { Count: > 0 } wanted)
            return null;
        List<string> missing = null;
        foreach ((string id, int count) in wanted)
            if (bot.Inventory.CountItemTemplate(id, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) < count)
                (missing ??= new List<string>()).Add(id);
        return missing;
    }

    private static eCraftingSkill CraftOf(GameBot bot) =>
        Classic165Mode.IsOn && bot?.PersistentRecord != null ? CrafterBots165.CraftOf(bot.PersistentRecord) : eCraftingSkill.NoCrafting;

    private static bool WantsAmmo(GameBot bot)
    {
        if (bot?.Inventory == null || Supplies165.AmmoType(bot.Inventory.GetItem(eInventorySlot.DistanceWeapon)) is not { } type)
            return false;
        return Supplies165.AmmoCount(bot, type) < Supplies165.KeepFor(bot);
    }

    /// <summary>Vials an assassin bot keeps; below this it buys poisons on the exchange when they are cheap.</summary>
    public const int PoisonVialsKept = 10;

    private static bool WantsPoison(GameBot bot)
    {
        if (bot?.CharacterClass == null || bot.Inventory == null || !BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID))
            return false;
        int vials = 0;
        for (eInventorySlot slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
            if (bot.Inventory.GetItem(slot) is { } item && item.Object_Type == (int)eObjectType.Poison)
                vials += Math.Max(1, item.Count);
        return vials < PoisonVialsKept;
    }

    /// <summary>
    /// One rule for "should I buy it" (2026-11-11 exchange plan, section 2.5): gear must pass the same check the equip
    /// step makes (TryGetEquipmentUpgrade: proficiency, a usable ranged weapon, shield/offhand, a slot its class keeps for
    /// another use) and beat every spare it owns; it must be at most two levels above the bot, priced at most 1.5 x its
    /// fair value (3 x for a player's listing, owner rule S26) and within the share of the bot's spare coin the upgrade
    /// earns. A bargain (asked below fair) ranks higher; a fairly priced player listing ranks highest.
    /// </summary>
    private static PurchaseCandidate ScorePurchase(GameBot bot, DbInventoryItem item, long budget)
    {
        if (item?.Template == null || item.SellPrice <= 0 || item.SellPrice > budget ||
            item.Level > bot.Level + PurchaseLevelsAbove || item.LevelRequirement > bot.Level)
            return null;
        bool isPlayerListing = IsPlayerListing(item.OwnerID);
        long fair = ItemValue165.Fair(item);
        // Bots prefer a player's goods over another bot's when the price is fair (owner 2026-11-05, reset-day S26): within
        // 25% of the fair value. A player's listing at three times that or more is not bought at all, so nobody can dump
        // an item on the bots for a fortune; a bot's listing above 1.5 x fair is not bought either.
        if (item.SellPrice > (isPlayerListing ? fair * 3L : fair * 3L / 2))
            return null;
        int bargain = fair > 0 ? (int)Math.Clamp(200 * (fair - item.SellPrice) / fair, 0, 200) : 0;
        int playerPreference = isPlayerListing && item.SellPrice <= fair * 1.25 ? 650 : 0;

        int supply = SupplyUtility(bot, item, fair);
        if (supply > 0)
            return new PurchaseCandidate(item, supply + bargain + playerPreference, eInventorySlot.Invalid, false, isPlayerListing);

        if (!TryGetEquipmentUpgrade(bot, item, out eInventorySlot equipSlot))
            return null;
        DbInventoryItem equipped = bot.Inventory.GetItem(equipSlot);
        int improvement;
        if ((eObjectType)item.Object_Type == eObjectType.Instrument)
            improvement = InstrumentGain(bot, item, equipSlot);
        else if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage && BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
            improvement = (int)Math.Clamp(Math.Ceiling(SavageBotWeaponPolicy.UpgradeGain(bot, item, equipSlot) * 100), 1, int.MaxValue - 10_000);
        else
            improvement = FitValue(bot, item, equipSlot) - FitValue(bot, equipped, equipSlot) + (equipped == null ? 250 : 0);
        if (improvement <= MinimumEquipmentUpgrade || !PurseAllows(budget, item.SellPrice, improvement, equipped == null))
            return null;
        // ... and nothing it already has as good in its bag or vault (checked last: it walks the inventory)
        return OwnsSpareAsGood(bot, item, equipSlot)
            ? null
            : new PurchaseCandidate(item, 1_000 + improvement + bargain + playerPreference, equipSlot, true, isPlayerListing);
    }

    /// <summary>
    /// A bot's own supplies bought from the exchange, only when cheaper than at a merchant: an archer its ammunition type
    /// while short of it, an assassin poisons up to its level while short of them, a crafter its own recipes'
    /// materials (CrafterBots165.CraftOf, not the empty SerializedCraftingSkills that kept every stack unsold).
    /// </summary>
    private static int SupplyUtility(GameBot bot, DbInventoryItem item, long fair)
    {
        eObjectType type = (eObjectType)item.Object_Type;
        long shelf = ItemValue165.Shelf(item);
        bool cheap = item.SellPrice <= (shelf > 0 ? shelf : fair);
        if (!cheap)
            return 0;
        if (type is eObjectType.Arrow or eObjectType.Bolt)
            return IsOwnSupply(bot, item) && WantsAmmo(bot) ? 400 : 0;
        if (type == eObjectType.Poison)
            return item.Level <= bot.Level && WantsPoison(bot) ? 350 : 0;
        // a crafter's materials for its next recipes, only while it has fewer than it uses (CrafterBots165 crafts with them)
        int wanted = CrafterBots165.WantedMaterial(bot.PersistentRecord, bot.Realm, item.Id_nb);
        if (wanted > 0 && item.Count > 0 &&
            bot.Inventory.CountItemTemplate(item.Id_nb, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) < wanted)
            return 300 + Math.Min(150, item.Count * 3);
        return 0;
    }

    // ---- window shopping (2026-11-11 exchange plan, section 2.5)

    private static readonly ConcurrentDictionary<long, (int Level, int Boundaries)> WindowShopping = new();
    /// <summary>Task boundaries between window-shopping checks when the bot has not levelled.</summary>
    public const int WindowShoppingEvery = 4;
    /// <summary>Most listings a window-shopping check looks at.</summary>
    public const int WindowShoppingLookups = 20;

    /// <summary>
    /// At a task boundary, after a level-up or every few boundaries, a bot with coin above its reserve looks for an
    /// upgrade on the exchange it can wear and afford (at most <see cref="WindowShoppingLookups"/> listings, no full
    /// scoring). True sends it on the town errand that already exists - no new timer.
    /// </summary>
    public static bool WantsWindowShopping(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null || bot.DatabaseID <= 0 ||
            !CanBuyOnExchange(bot) || HiredCompanions165.IsHired(bot))
            return false;
        (int level, int boundaries) = WindowShopping.GetValueOrDefault(bot.DatabaseID, (-1, 0));
        bool due = level != bot.Level || boundaries + 1 >= WindowShoppingEvery;
        if (WindowShopping.Count > 16_384)
            WindowShopping.Clear();
        WindowShopping[bot.DatabaseID] = due ? (bot.Level, 0) : (level, boundaries + 1);
        if (!due || IsBackpackFull(bot))
            return false;
        long budget = PurchaseBudget(bot);
        if (budget <= 0)
            return false;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            int looked = 0;
            foreach ((DbInventoryItem item, int guess) in GearCandidates(bot, budget, GetOwnerId(bot.DatabaseID),
                         RealmExchangeBroker.GetOwnerLot(bot.Realm), DateTime.UtcNow).OrderByDescending(entry => entry.Guess))
            {
                if (++looked > WindowShoppingLookups)
                    break;
                if (PurseAllows(budget, item.SellPrice, guess, false) || guess >= 250 && PurseAllows(budget, item.SellPrice, guess, true))
                    return true;
            }
            return false;
        }
        finally
        {
            RealmExchangeStats.NoteScan(System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }
    }

        public static int EquipmentValue(DbInventoryItem item)
    {
        if (item == null || !BotWeaponStats.HasFunctionalMeleeStats(item))
            return 0;
        // owner call (2026-11-11 exchange plan, valuation option 8): bonus points weighted by type as a period player
        // valued them (skills 5, resists and power 2, stats/focus/AF 1, hits 0.25; ItemValue165.Weight), without the
        // Bonus% field; bot_weighted_gear_ranking off brings back the flat count. Only the ranking changes, no combat number.
        int utility = WeightedGearRanking
            ? (int)Math.Round(ItemValue165.Utility(item) * 3)
            : 3 * (Math.Abs(item.Bonus) + Math.Abs(item.ExtraBonus) + Math.Abs(item.Bonus1) + Math.Abs(item.Bonus2) +
                   Math.Abs(item.Bonus3) + Math.Abs(item.Bonus4) + Math.Abs(item.Bonus5) + Math.Abs(item.Bonus6) +
                   Math.Abs(item.Bonus7) + Math.Abs(item.Bonus8) + Math.Abs(item.Bonus9) + Math.Abs(item.Bonus10));
        // Body armor counts its absorption too (owner 2026-10-31 runs: level 6-9 fighters wore cloth rags because a
        // level-7 cloth piece scored the same as level-7 chain, and died to single blue monsters).
        int absorb = (eObjectType)item.Object_Type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor
            ? Math.Max(0, item.SPD_ABS) * 2 : 0;
        return Math.Max(1, item.Level) * 10 + Math.Max(1, item.Quality) + Math.Max(0, item.DPS_AF) * 2 + utility + absorb;
    }

        [DOL.GS.ServerProperties.ServerProperty("bots", "bot_weighted_gear_ranking",
            "Owner 2026-11-11 (Realm Exchange plan): gamebots rank gear bonuses weighted by type (skills 5, resists and power 2, stats/focus/AF 1, hits 0.25, no Bonus% field) instead of counting every point the same. Ranking only, no combat number.", true)]
        public static bool WeightedGearRanking = true;

        public static bool IsPlayerListing(string ownerId) =>
            !string.IsNullOrWhiteSpace(ownerId) && !TryParseOwnerId(ownerId, out _);

        /// <summary>
        /// Returns the real equipment destination only when this bot can use the item and it is
        /// materially better than the item presently worn there.  This is shared by loot,
        /// merchant, and Realm Exchange paths so they cannot accidentally vendor a usable upgrade.
        /// </summary>
        /// <summary>
        /// Whether the bot can wear this item at all (class, level, weapon proficiency and its build's slots), and where.
        /// True 1.65 Mode's hired companions use it to wear what their player hands them, upgrade or not.
        /// </summary>
        public static bool CanWear(GameBot bot, DbInventoryItem item, out eInventorySlot equipSlot)
        {
            equipSlot = eInventorySlot.Invalid;
            if (bot?.Inventory == null || item?.Template == null ||
                (eObjectType)item.Object_Type == eObjectType.GenericItem ||
                // a player can put on an item above his level (it only works fully once he reaches it), so a hired
                // companion can too; a gamebot buying for itself still waits (audit 2026-11-01)
                item.LevelRequirement > bot.Level && !HiredCompanions165.IsHired(bot) ||
                (BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
                 (!BotWeaponStats.IsConfiguredMeleeWeapon(bot, item.Template) ||
                  !BotWeaponStats.HasConfiguredWeaponProficiency(bot, item.Template))) ||
                (BotRangedCombat.IsRangedWeaponType((eObjectType)item.Object_Type) && !BotRangedCombat.IsUsableWeapon(item)) ||
                (!BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
                 !GameServer.ServerRules.CheckAbilityToUseItem(bot, item.Template)))
                return false;
            equipSlot = ResolveEquipmentSlot(bot, item);
            return equipSlot != eInventorySlot.Invalid;
        }

        public static bool TryGetEquipmentUpgrade(GameBot bot, DbInventoryItem item, out eInventorySlot equipSlot)
        {
            equipSlot = eInventorySlot.Invalid;
            if (bot?.Inventory == null || item?.Template == null ||
                (eObjectType)item.Object_Type == eObjectType.GenericItem || item.LevelRequirement > bot.Level ||
                !BotWeaponStats.HasFunctionalMeleeStats(item) ||
                (BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
                 (!BotWeaponStats.IsConfiguredMeleeWeapon(bot, item.Template) ||
                  !BotWeaponStats.HasConfiguredWeaponProficiency(bot, item.Template))) ||
                (BotRangedCombat.IsRangedWeaponType((eObjectType)item.Object_Type) && !BotRangedCombat.IsUsableWeapon(item)) ||
                (!BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type) &&
                 !GameServer.ServerRules.CheckAbilityToUseItem(bot, item.Template)))
                return false;

            equipSlot = ResolveEquipmentSlot(bot, item);
            if (equipSlot == eInventorySlot.Invalid || KeepsSlotForOtherUse(bot, item, equipSlot))
                return false;

            DbInventoryItem equipped = bot.Inventory.GetItem(equipSlot);
            if (DisplacesOffhandWeapon(item, equipped))
                return false;
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
                return SavageBotWeaponPolicy.IsUpgrade(bot, item, equipSlot);
            if ((eObjectType)item.Object_Type == eObjectType.Instrument)
                return InstrumentGain(bot, item, equipSlot) > MinimumEquipmentUpgrade;
            int improvement = FitValue(bot, item, equipSlot) - FitValue(bot, equipped, equipSlot);
            return equipped == null || improvement > MinimumEquipmentUpgrade;
        }

        /// <summary>
        /// A slot the bot's class keeps empty or for one kind of item, whatever an upgrade check says (2026-11-11 audit:
        /// Gormienbjorn had 7 rowan round shields in his vault). A Savage fights with a two-hander or hand-to-hand weapons:
        /// GameBot.EnsureSavageWeaponReady moves anything else out of its left hand (all of it for a two-handed build), so
        /// the shop saw that hand empty and bought the next shield. The left hand of a Savage takes only a hand-to-hand
        /// weapon of a hand-to-hand build.
        /// </summary>
        public static bool KeepsSlotForOtherUse(GameBot bot, DbInventoryItem item, eInventorySlot slot) =>
            bot?.CharacterClass?.ID == (int)eCharacterClass.Savage && slot == eInventorySlot.LeftHandWeapon &&
            !(bot.BotSpec?.WeaponOneType == eObjectType.HandToHand && item?.Object_Type == (int)eObjectType.HandToHand);

        /// <summary>
        /// What an instrument adds over the best one of its kind (drum, lute, flute) the bot already owns, worn, carried or
        /// in the vault (2026-11-11 audit: Godalred had 15 ironwood flutes). A song puts the instrument it needs in the
        /// two-hand slot (BotBrain.TryEquipRealInstrument), so the shop compared a merchant's flute with the drum held
        /// there and bought it again and again. An instrument the bot already owns adds nothing: the song takes it from
        /// the bag when it is needed, and moving it into a slot would only push out the one another song uses.
        /// </summary>
        public static int InstrumentGain(GameBot bot, DbInventoryItem item, eInventorySlot slot)
        {
            if (bot?.Inventory == null || item == null || (eObjectType)item.Object_Type != eObjectType.Instrument)
                return int.MinValue;
            int best = int.MinValue;
            foreach (DbInventoryItem owned in bot.Inventory.AllItems)
            {
                if (ReferenceEquals(owned, item))
                    return int.MinValue;
                if (owned == null || owned.OwnerLot != 0 || owned.Object_Type != (int)eObjectType.Instrument ||
                    owned.DPS_AF != item.DPS_AF || !IsKept(owned))
                    continue;
                best = Math.Max(best, FitValue(bot, owned, slot));
            }
            // none of that kind yet: as for an empty slot
            return best == int.MinValue ? FitValue(bot, item, slot) + 250 : FitValue(bot, item, slot) - best;
        }

        /// <summary>Worn, carried or in the vault: where the bot's own things are.</summary>
        private static bool IsKept(DbInventoryItem item) =>
            item.SlotPosition is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable ||
            item.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack ||
            item.SlotPosition is >= (int)eInventorySlot.FirstVault and <= (int)eInventorySlot.LastVault;

        /// <summary>
        /// The bot already has, in its bag or vault, an item it could wear in that slot that the candidate does not beat by
        /// more than <see cref="MinimumEquipmentUpgrade"/>: the same item again, or one as good. A bot buys only what is
        /// better than what it owns, not a copy of a spare it keeps (2026-11-11 audit: shields and flutes bought again and
        /// again while the same ones sat in the vault).
        /// </summary>
        public static bool OwnsSpareAsGood(GameBot bot, DbInventoryItem item, eInventorySlot slot)
        {
            if (bot?.Inventory == null || item == null || slot == eInventorySlot.Invalid)
                return false;
            bool savageWeapon = bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                                BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type);
            int wanted = FitValue(bot, item, slot);
            foreach (DbInventoryItem owned in bot.Inventory.AllItems)
            {
                if (owned == null || ReferenceEquals(owned, item) || owned.OwnerLot != 0 || owned.Template == null ||
                    owned.SlotPosition is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable ||
                    !IsKept(owned))
                    continue;
                // the same item again: a copy is never better than the one in the bag (a Savage's weapons are rated by
                // its own policy, so only the copy rule applies to them)
                if (string.Equals(owned.Id_nb, item.Id_nb, StringComparison.OrdinalIgnoreCase) &&
                    owned.Quality >= item.Quality && (owned.Condition > 0 || !BotWeaponStats.IsMeleeWeapon((eObjectType)owned.Object_Type)))
                    return true;
                if (savageWeapon || owned.Object_Type != item.Object_Type && owned.Item_Type != item.Item_Type)
                    continue;
                if (CanWear(bot, owned, out eInventorySlot ownedSlot) && SameSlot(ownedSlot, slot) &&
                    BotWeaponStats.HasFunctionalMeleeStats(owned) && owned.LevelRequirement <= bot.Level &&
                    wanted - FitValue(bot, owned, slot) <= MinimumEquipmentUpgrade)
                    return true;
            }
            return false;

            static bool SameSlot(eInventorySlot a, eInventorySlot b) => a == b ||
                a is eInventorySlot.LeftRing or eInventorySlot.RightRing && b is eInventorySlot.LeftRing or eInventorySlot.RightRing ||
                a is eInventorySlot.LeftBracer or eInventorySlot.RightBracer && b is eInventorySlot.LeftBracer or eInventorySlot.RightBracer;
        }

        /// <summary>
        /// Whether a gamebot should buy this item (merchant, seal merchant, shopping trip): an upgrade where it would wear
        /// it (<see cref="TryGetEquipmentUpgrade"/>, which also keeps the slots its class leaves empty and compares an
        /// instrument with its own kind) that is also better than every spare it already owns for that slot.
        /// </summary>
        public static bool IsWorthBuying(GameBot bot, DbInventoryItem item, out eInventorySlot equipSlot) =>
            TryGetEquipmentUpgrade(bot, item, out equipSlot) && !OwnsSpareAsGood(bot, item, equipSlot);

        /// <summary>
        /// Value of a point of trained weapon spec, for comparing melee weapons. A player fights with the weapon line he
        /// trained: an untrained weapon misses more and hits for less, whatever its level (2026-11-01 run: 55% of the
        /// level 5-9 hybrid and melee bots that died held their starting weapon in a line at spec 0-1 - Shadowblades
        /// trained in Axe with a training sword, Hunters, Savages, Valewalkers, Bards, Champions - because a weapon was
        /// valued by level, quality and DPS only, so a same-level weapon of the trained line was never an upgrade).
        /// </summary>
        public const int WeaponSpecValue = 30;

        /// <summary>The bot's trained spec for a melee weapon of this type in this hand (left axe in the left hand), times
        /// <see cref="WeaponSpecValue"/>; 0 for anything else.</summary>
        public static int WeaponSpecBonus(GameBot bot, int objectType, eInventorySlot slot) =>
            bot != null && BotWeaponStats.IsMeleeWeapon((eObjectType)objectType)
                ? WeaponSpecValue * Math.Max(0, bot.TrainedWeaponSpec((eObjectType)objectType, (int)slot))
                : 0;

        /// <summary>EquipmentValue plus, for a melee weapon, what the bot's training in its line is worth in that hand, less
        /// the focus bonuses for spell lines the bot does not have (they do nothing for it).</summary>
        public static int FitValue(GameBot bot, DbInventoryItem item, eInventorySlot slot) =>
            item == null ? 0 : EquipmentValue(item) + WeaponSpecBonus(bot, item.Object_Type, slot) - 3 * WastedFocus(bot, item);

        /// <summary>As <see cref="WastedFocus(GameBot, (int Type, int Value)[])"/> for an item, without allocating (it runs
        /// in every appraisal).</summary>
        public static int WastedFocus(GameBot bot, DbInventoryItem item)
        {
            if (bot == null || item == null)
                return 0;
            return Wasted(bot, item.Bonus1Type, item.Bonus1) + Wasted(bot, item.Bonus2Type, item.Bonus2) +
                   Wasted(bot, item.Bonus3Type, item.Bonus3) + Wasted(bot, item.Bonus4Type, item.Bonus4) +
                   Wasted(bot, item.Bonus5Type, item.Bonus5) + Wasted(bot, item.Bonus6Type, item.Bonus6) +
                   Wasted(bot, item.Bonus7Type, item.Bonus7) + Wasted(bot, item.Bonus8Type, item.Bonus8) +
                   Wasted(bot, item.Bonus9Type, item.Bonus9) + Wasted(bot, item.Bonus10Type, item.Bonus10) +
                   Wasted(bot, item.ExtraBonusType, item.ExtraBonus);
        }

        private static int Wasted(GameBot bot, int type, int value) =>
            value == 0 || type == 0 || SkillBase.FocusToSpec((eProperty)type) is not string spec ||
            (bot.GetSpecializationByName(spec)?.Level ?? 0) > 0 ? 0 : Math.Abs(value);

        /// <summary>
        /// The focus bonus points (a staff's "+N to Darkness", ...) for spell lines the bot does not have. Item values add
        /// up every bonus point whatever it raises, so any caster rated a staff of another class's lines like its own
        /// (2026-11-05 audit: 16 Cabalists with a Wizard staff, 18 Necromancers with a Sorcerer staff, 14 Spiritmasters
        /// with Bonedancer staves).
        /// </summary>
        public static int WastedFocus(GameBot bot, (int Type, int Value)[] bonuses)
        {
            if (bot == null)
                return 0;
            int wasted = 0;
            foreach ((int type, int value) in bonuses)
            {
                if (value == 0 || type == 0 || SkillBase.FocusToSpec((eProperty)type) is not string spec)
                    continue;
                if ((bot.GetSpecializationByName(spec)?.Level ?? 0) <= 0)
                    wasted += Math.Abs(value);
            }
            return wasted;
        }

        public static int WastedFocus(GameBot bot, DbItemTemplate t) => t == null ? 0 : WastedFocus(bot,
            [(t.Bonus1Type, t.Bonus1), (t.Bonus2Type, t.Bonus2), (t.Bonus3Type, t.Bonus3), (t.Bonus4Type, t.Bonus4),
             (t.Bonus5Type, t.Bonus5), (t.Bonus6Type, t.Bonus6), (t.Bonus7Type, t.Bonus7), (t.Bonus8Type, t.Bonus8),
             (t.Bonus9Type, t.Bonus9), (t.Bonus10Type, t.Bonus10), (t.ExtraBonusType, t.ExtraBonus)]);

        /// <summary>
        /// Compares against what the move actually displaces (the idea behind the
        /// stefanrows/OfflineDAoC fork's shield/two-hander loop fix, 0b76744). A shield is
        /// never an upgrade over a weapon the build wields in its left hand: Savages swapped a
        /// shield and their hand-to-hand offhand back and forth every 11 seconds (up to 177
        /// times per bot in one log), saving inventory each time.
        /// </summary>
        public static bool DisplacesOffhandWeapon(DbInventoryItem item, DbInventoryItem equipped) =>
            item != null && equipped != null && (eObjectType)item.Object_Type == eObjectType.Shield &&
            BotWeaponStats.IsMeleeWeapon((eObjectType)equipped.Object_Type);

        /// <summary>
        /// Equips an already-owned backpack item. MoveItem swaps an existing equipped item back
        /// into the source backpack slot, so an upgrade is safe even when the backpack is full.
        /// </summary>
        public static bool TryEquipOwnedUpgrade(GameBot bot, DbInventoryItem item, string source = "inventory")
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
                item == null || !bot.Inventory.AllItems.Contains(item) ||
                item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack ||
                !TryGetEquipmentUpgrade(bot, item, out eInventorySlot equipSlot))
                return false;

            eInventorySlot backpackSlot = (eInventorySlot)item.SlotPosition;
            DbInventoryItem replaced = bot.Inventory.GetItem(equipSlot);
            if (!bot.Inventory.MoveItem(backpackSlot, equipSlot, Math.Max(1, item.Count)))
                return false;

            bot.RefreshItemBonuses();
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
                bot.EnsureBotWeaponReady();
            // The wielded weapon is cached: re-select the active slot so a new weapon there is the one used (2026-11-01
            // run: a Sorcerer bought an elm quarterstaff and still fought with the trimmed branch now in its bag).
            else if (equipSlot is eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or
                     eInventorySlot.TwoHandWeapon or eInventorySlot.DistanceWeapon && !bot.IsAttacking)
                bot.SwitchWeapon(bot.ActiveWeaponSlot);
            MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            LogEquipmentUpgrade(bot, item, replaced, equipSlot, source);
            return true;
        }

        /// <summary>
        /// Repairs an older, full backpack before any disposal decision. Equipping the best
        /// owned upgrade swaps the previous worn item into its backpack slot, where normal
        /// merchant appraisal can then decide whether it is actually surplus.
        /// </summary>
        public static bool TryEquipBestOwnedUpgrade(GameBot bot, out DbInventoryItem equipped)
        {
            equipped = null;
            if (bot?.IsAutonomousWorldBot != true || bot.Inventory == null)
                return false;

            DbInventoryItem candidate = bot.Inventory.AllItems
                .Where(item => item != null && item.OwnerLot == 0 &&
                               item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                               item.SlotPosition <= (int)eInventorySlot.LastBackpack &&
                               TryGetEquipmentUpgrade(bot, item, out _))
                .OrderByDescending(EquipmentValue)
                .ThenBy(item => item.SlotPosition)
                .FirstOrDefault();
            if (candidate == null || !TryEquipOwnedUpgrade(bot, candidate, "merchant-service"))
                return false;

            equipped = candidate;
            return true;
        }

        private static void LogEquipmentUpgrade(GameBot bot, DbInventoryItem item,
            DbInventoryItem replaced, eInventorySlot slot, string source)
        {
            if (!AutonomousDiagnosticsProperties.EquipmentUpgrades || bot?.IsAutonomousWorldBot != true ||
                bot.IsTemporaryGroupHelper) return;
            // The move has already succeeded. This is one outcome record, not
            // a scan or a second inventory transaction.
            try
            {
                Log.Info($"AUTONOMOUS_EQUIPMENT_UPGRADE bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                    $"class=\"{bot.ClassName}\" source={source} slot={slot} item=\"{item.Name}\" " +
                    $"itemId=\"{item.ObjectId}\" replaced=\"{replaced?.Name ?? "empty"}\" " +
                    $"oldValue={EquipmentValue(replaced)} newValue={EquipmentValue(item)}");
            }
            catch { }
        }

        /// <summary>
        /// Decides which real-world service should be routed to. This has no movement side effect;
        /// the world controller supplies the reachable NPC destination and BotBrain performs the
        /// transaction only after it is physically nearby.
        /// </summary>
        public static eWorldServiceKind? GetNeededService(GameBot bot)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
                !AutonomousObjectiveAssignments.WantsBetweenTaskInventory(bot))
                return null;

            ServiceDecisionState state = ServiceDecisions.GetOrAdd(bot.DatabaseID, _ => new ServiceDecisionState());
            long now = GameLoop.GameLoopTime;
            int revision = Volatile.Read(ref state.Revision);
            if (Volatile.Read(ref state.EvaluatedRevision) == revision && now < Volatile.Read(ref state.NextSafetyRefreshTick))
                return state.CachedService;

            lock (state.Gate)
            {
                revision = Volatile.Read(ref state.Revision);
                if (state.EvaluatedRevision == revision && now < state.NextSafetyRefreshTick)
                    return state.CachedService;

                state.CachedService = EvaluateNeededService(bot);
                state.EvaluatedRevision = revision;
                // Deterministic staggering prevents a server-wide safety sweep.
                state.NextSafetyRefreshTick = now + ServiceDecisionSafetyRefreshMilliseconds + bot.DatabaseID % 90_000;
                return state.CachedService;
            }
        }

        private static eWorldServiceKind? EvaluateNeededService(GameBot bot)
        {
            // Choose the single listing from the complete backpack before any
            // vendor sale changes that candidate pool. Once the listing slot is
            // occupied, finish clearing every disposable item before shopping.
            // True 1.65 Mode keeps its brokers at the traveler's inns, some in dangerous country (West Svealand's is
            // ringed by level-11 sveawolves): a bot below level 10 sells to a vendor instead (owner 2026-10-31 run).
            // owner 2026-11-08: no broker in reach for this bot right now - sell at a merchant instead of waiting.
            // 2026-11-11 exchange plan: the broker is worth the trip with a free slot or a swap worth making (a capped bot
            // with good loot keeps it for the exchange); bots buy from level 5 (owner call), sell from level 10.
            bool canBuy = CanBuyOnExchange(bot);
            if (CanSellOnExchange(bot) && FindValuableListingCandidate(bot) != null)
                return eWorldServiceKind.RealmExchange;

            if (FindVendorTrashCandidate(bot) != null)
                return eWorldServiceKind.Vendor;

            // A backpack filled entirely with quest/reputation tokens or other
            // non-vendorable objects still needs a real merchant visit. The
            // arrived-service path may clear that blocked backpack only after
            // confirming that no ordinary sale is possible.
            if (IsBackpackFull(bot))
                return eWorldServiceKind.Vendor;

            // The vendor phase has now made room. If the bot arrived with its
            // one listing slot already occupied, it naturally skipped the first
            // branch and reaches this realm-local upgrade appraisal directly.
            if (canBuy && !IsBackpackFull(bot) && FindBestUsefulExchangePurchase(bot) != null)
                return eWorldServiceKind.RealmExchange;

            // True 1.65 Mode: worn gear to repair, arrows to buy, a crafting session due (TownErrands165).
            if (!IsBackpackFull(bot) && TownErrands165.Reason(bot) != null)
                return eWorldServiceKind.Vendor;

            // A gear upgrade the bot can afford at a known merchant: go and buy it (owner 2026-10-31). Before the healer
            // (run 11, 2026-10-31: 320 healer trips to 15 shopping trips an hour; bots spent their coin on Constitution and
            // fought on in level-3 rags, so they died again): the gear keeps the bot alive, the healer gets what is left.
            if (!IsBackpackFull(bot) && AutonomousWorldBotController.ShoppingPlanFor(bot) != null)
                return eWorldServiceKind.Shop;

            // True 1.65 Mode: Constitution lost at death to buy back, or resurrection sickness to cure (DeathPenalty165).
            if (DeathPenalty165.WantsHealer(bot))
                return eWorldServiceKind.Healer;

            // In Darkness Falls, seals the bot has looted buy its realm's seal gear, or seal items to sell (owner 2026-10-30).
            return !IsBackpackFull(bot) && HasSealPurchase(bot) ? eWorldServiceKind.SealMerchant : null;
        }

        public const int NearlyFullFreeSlots = 6;

        /// <summary>
        /// Used only at a natural task boundary (never mid-grind): with this few
        /// free slots left the next task would fill the bag and leave loot on
        /// the ground, so the bot unloads now. Selling still never interrupts a
        /// grind and is not triggered by ordinary partially filled bags.
        /// </summary>
        public static bool IsBackpackNearlyFull(GameBot bot)
        {
            if (bot?.Inventory == null)
                return false;
            int free = 0;
            for (eInventorySlot slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
                if (bot.Inventory.GetItem(slot) == null && ++free > NearlyFullFreeSlots)
                    return false;
            return true;
        }

        public static bool IsBackpackFull(GameBot bot) =>
            bot?.Inventory != null &&
            bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid;

        /// <summary>
        /// Last-resort recovery for a persistent world bot that has physically
        /// reached a merchant with all 40 backpack slots occupied and nothing
        /// the merchant phase can sell. Only expendable backpack items are
        /// removed: worn and spare combat equipment (including bard/minstrel
        /// instruments), vaults, exchange listings, and saved copper remain.
        /// A caller must establish the live merchant/range and between-task
        /// service guards before passing arrivedAtMerchant=true.
        /// </summary>
        public static bool TryClearBlockedBackpackAtMerchant(GameBot bot, bool arrivedAtMerchant, out int removed)
        {
            removed = 0;
            if (!arrivedAtMerchant || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper ||
                bot.IsPlayerLedGroup || bot.Inventory == null || bot.DatabaseID <= 0 ||
                !IsBackpackFull(bot) || FindVendorTrashCandidate(bot) != null ||
                FindValuableListingCandidate(bot) != null)
                return false;

            DbInventoryItem[] backpack = bot.Inventory.AllItems
                .Where(item => item != null && item.OwnerLot == 0 &&
                               item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                               item.SlotPosition <= (int)eInventorySlot.LastBackpack)
                .ToArray();
            int capacity = (int)eInventorySlot.LastBackpack - (int)eInventorySlot.FirstBackpack + 1;
            if (backpack.Length != capacity || !IsBackpackFull(bot))
                return false;

            HashSet<DbInventoryItem> operationalLoadout = FindOperationalLoadoutItems(bot);
            foreach (DbInventoryItem item in backpack.Where(item =>
                         !operationalLoadout.Contains(item) && !TryGetEquipmentUpgrade(bot, item, out _)))
            {
                if (bot.Inventory.RemoveItem(item))
                {
                    Exchange165.Forget(item);
                    removed++;
                }
            }
            if (removed == 0)
                return false;

            bot.MarkAutonomousStateDirty();
            MarkInventoryChanged(bot);
            AutonomousBotStatusPersistence.Queue(bot, true);
            try
            {
                Log.Warn($"AUTONOMOUS_BLOCKED_BACKPACK_CLEARED bot=\"{bot.Name}\" id={bot.DatabaseID} removed={removed} capacity={capacity}");
            }
            catch { } // A diagnostics failure must not interrupt the persisted cleanup.
            return true;
        }

        /// <summary>
        /// Keep one best spare per usable weapon shape and one per instrument kind, not every
        /// usable weapon ever looted. A caster can otherwise retain forty obsolete focus
        /// staves, making both merchant sale and the blocked-backpack failsafe impossible.
        /// Equipped gear is outside the backpack and is never considered here.
        /// </summary>
        private static HashSet<DbInventoryItem> FindOperationalLoadoutItems(GameBot bot)
        {
            HashSet<DbInventoryItem> retained = new();
            if (bot?.Inventory == null)
                return retained;

            bool TryRole(DbInventoryItem item, out (int Kind, int Type, int Shape) role)
            {
                eObjectType type = (eObjectType)item.Object_Type;
                if (type == eObjectType.Instrument)
                    // Lute/drum/flute use DPS_AF as the instrument kind; songs may
                    // pull these from a backpack even when another is equipped.
                    role = (1, item.DPS_AF, 0);
                else if (type == eObjectType.Shield)
                    role = (2, item.Item_Type, item.Hand);
                else if (BotWeaponStats.IsMeleeWeapon(type) && BotWeaponStats.CanUseMelee(bot, item))
                    role = (3, item.Object_Type, item.Item_Type);
                else if (BotRangedCombat.IsRangedWeaponType(type) && BotRangedCombat.CanUse(bot, item))
                    role = (4, item.Object_Type, item.Item_Type);
                else
                {
                    role = default;
                    return false;
                }
                return true;
            }

            // Training gear is no spare once the bot owns a real item of that kind, worn or carried (owner 2026-11-06:
            // otherwise it takes a bag slot forever after the first upgrade); the vendor takes it for 1 copper.
            HashSet<(int Kind, int Type, int Shape)> realRoles = new();
            foreach (DbInventoryItem item in bot.Inventory.AllItems)
                if (item != null && item.OwnerLot == 0 && !BotRangedCombat.IsTrainingGear(item) && TryRole(item, out var realRole))
                    realRoles.Add(realRole);

            Dictionary<(int Kind, int Type, int Shape), DbInventoryItem> bestByRole = new();
            foreach (DbInventoryItem item in bot.Inventory.AllItems)
            {
                if (item == null || item.OwnerLot != 0 ||
                    item.SlotPosition < (int)eInventorySlot.FirstBackpack ||
                    item.SlotPosition > (int)eInventorySlot.LastBackpack)
                    continue;
                if (item is GameInventoryRelic || BotSiegeRuntime.IsSupply(item.Id_nb))
                {
                    retained.Add(item);
                    continue;
                }

                if (!TryRole(item, out var role) || BotRangedCombat.IsTrainingGear(item) && realRoles.Contains(role))
                    continue;

                if (!bestByRole.TryGetValue(role, out DbInventoryItem incumbent) ||
                    EquipmentValue(item) > EquipmentValue(incumbent) ||
                    EquipmentValue(item) == EquipmentValue(incumbent) && item.SlotPosition < incumbent.SlotPosition)
                    bestByRole[role] = item;
            }

            foreach (DbInventoryItem item in bestByRole.Values)
                retained.Add(item);
            return retained;
        }

    private static eInventorySlot ResolveEquipmentSlot(GameBot bot, DbInventoryItem item)
    {
        eInventorySlot requested = (eInventorySlot)item.Item_Type;
        if (BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
        {
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                bot.BotSpec?.WeaponOneType != eObjectType.HandToHand &&
                requested == eInventorySlot.LeftHandWeapon)
                requested = eInventorySlot.RightHandWeapon;
            // A left-axe build may use ordinary one-handed axes in its offhand.
            // Don't buy/equip one as an upgrade to a trained sword/hammer mainhand.
            if (bot.BotSpec?.SpecType == eSpecType.LeftAxe &&
                item.Object_Type is (int)eObjectType.Axe or (int)eObjectType.LeftAxe &&
                item.Object_Type != (int)bot.BotSpec.WeaponOneType && item.Item_Type != Slot.TWOHAND && item.Hand != 1)
                requested = eInventorySlot.LeftHandWeapon;
            // A left-hand weapon slot on an item means "either hand" (869 one-handers, e.g. the training mace); a class
            // that can't use a left-hand weapon wields it in the main hand, as a player would (owner 2026-11-01: a
            // Cleric companion always held its mace in the left hand).
            // A hired companion is placed like a player's auto-equip (PlayerMoveItemRequestHandler): an either-hand
            // weapon goes to the main hand when that hand is empty.
            if (requested == eInventorySlot.LeftHandWeapon &&
                (!CanUseOffhandWeapon(bot) ||
                 HiredCompanions165.IsHired(bot) &&
                 (bot.Inventory.GetItem(eInventorySlot.RightHandWeapon) == null || !LeftHandWeaponAllowed(bot, item))))
                requested = eInventorySlot.RightHandWeapon;
            // A gamebot that fights with two weapons holds the one it trained more in its main hand (2026-11-01 run:
            // Shadowblades trained in Axe put a new axe in the left hand and kept fighting with the training sword).
            // Left-axe builds and Savages keep their own hand rules above.
            else if (requested == eInventorySlot.LeftHandWeapon && !HiredCompanions165.IsHired(bot) &&
                     bot.BotSpec?.SpecType != eSpecType.LeftAxe && bot.CharacterClass?.ID != (int)eCharacterClass.Savage &&
                     bot.Inventory.GetItem(eInventorySlot.RightHandWeapon) is var mainHand &&
                     (mainHand == null || WeaponSpecBonus(bot, item.Object_Type, eInventorySlot.RightHandWeapon) >
                                          WeaponSpecBonus(bot, mainHand.Object_Type, eInventorySlot.RightHandWeapon)))
                requested = eInventorySlot.RightHandWeapon;
            if (!BotWeaponStats.FitsConfiguredSlot(bot, item, requested)) return eInventorySlot.Invalid;
        }
        if (requested is eInventorySlot.LeftRing or eInventorySlot.RightRing)
            return WorseSlot(bot, eInventorySlot.LeftRing, eInventorySlot.RightRing);
        if (requested is eInventorySlot.LeftBracer or eInventorySlot.RightBracer)
            return WorseSlot(bot, eInventorySlot.LeftBracer, eInventorySlot.RightBracer);
        return requested is >= eInventorySlot.MinEquipable and <= eInventorySlot.MaxEquipable
            ? requested
            : eInventorySlot.Invalid;
    }

        /// <summary>
        /// Whether an item the bot can wear (CanWear passed: class skill, level) may go in the slot the player dropped it
        /// on. The same slot rules as a player's own equipment (GamePlayerInventory): the main hand takes a one-handed
        /// weapon of either hand, never a shield; the left hand takes only an either-hand item - a shield, or a weapon
        /// for a class that fights with a left-hand weapon; the two-hand slot a two-hander or an instrument; the ranged
        /// slot a ranged weapon or an instrument; rings and bracers either side; armour only its own slot. (A player can
        /// also hold a one-hander in the two-hand slot; a companion doesn't, so its two weapon sets stay clear.)
        /// </summary>
        public static bool FitsChosenSlot(GameBot bot, DbInventoryItem item, int wanted)
        {
            if (bot == null || item == null || wanted < (int)eInventorySlot.MinEquipable || wanted > (int)eInventorySlot.MaxEquipable)
                return false;
            var slot = (eInventorySlot)wanted;
            var own = (eInventorySlot)item.Item_Type;
            var type = (eObjectType)item.Object_Type;
            bool fits = slot switch
            {
                eInventorySlot.RightHandWeapon => type != eObjectType.Shield && own is eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon,
                eInventorySlot.LeftHandWeapon => own == eInventorySlot.LeftHandWeapon && (type == eObjectType.Shield || LeftHandWeaponAllowed(bot, item)),
                eInventorySlot.TwoHandWeapon => type != eObjectType.Shield && (own == eInventorySlot.TwoHandWeapon || type == eObjectType.Instrument),
                eInventorySlot.DistanceWeapon => own == eInventorySlot.DistanceWeapon || type == eObjectType.Instrument,
                eInventorySlot.LeftRing or eInventorySlot.RightRing => own is eInventorySlot.LeftRing or eInventorySlot.RightRing,
                eInventorySlot.LeftBracer or eInventorySlot.RightBracer => own is eInventorySlot.LeftBracer or eInventorySlot.RightBracer,
                _ => own == slot,
            };
            if (!fits)
                return false;
            // a melee weapon also has to be one the bot can fight with in that hand (skill, level, condition)
            return !BotWeaponStats.IsMeleeWeapon(type) || BotWeaponStats.FitsConfiguredSlot(bot, item, slot);
        }

        /// <summary>Whether the bot's class fights with a weapon in its left hand (dual wield, celtic dual, left axe,
        /// hand to hand) - the same class flag that decides it for a player.</summary>
        public static bool CanUseOffhandWeapon(GameBot bot) => bot?.CharacterClass?.CanUseLefthandedWeapon == true;

        /// <summary>
        /// Whether this weapon can be the bot's left-hand weapon: its class fights left-handed, and with the left-hand
        /// line's weapons - Left Axe (Berserker, Shadowblade) only axes, Hand to Hand (Savage) only hand-to-hand
        /// weapons; Dual Wield and Celtic Dual any either-hand weapon.
        /// </summary>
        public static bool LeftHandWeaponAllowed(GameBot bot, DbInventoryItem item)
        {
            if (item == null || !CanUseOffhandWeapon(bot) || (eInventorySlot)item.Item_Type != eInventorySlot.LeftHandWeapon)
                return false;
            var type = (eObjectType)item.Object_Type;
            return bot.CharacterClass.ID switch
            {
                (int)eCharacterClass.Berserker or (int)eCharacterClass.Shadowblade => type is eObjectType.Axe or eObjectType.LeftAxe,
                (int)eCharacterClass.Savage => type == eObjectType.HandToHand,
                _ => true,
            };
        }

        /// <summary>
        /// A melee weapon left in the left hand of a class that can't use it (equipped there before the either-hand rule
        /// in ResolveEquipmentSlot) moves to the empty main hand, so the bot fights with it. True when it moved.
        /// </summary>
        public static bool MoveStrayOffhandWeapon(GameBot bot)
        {
            if (bot?.Inventory == null)
                return false;
            if (CanUseOffhandWeapon(bot))
                return SwapHandsForTrainedWeapon(bot);
            DbInventoryItem left = bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon);
            if (left == null || !BotWeaponStats.IsMeleeWeapon((eObjectType)left.Object_Type) ||
                left.Item_Type == Slot.TWOHAND || left.Hand == 1 ||
                bot.Inventory.GetItem(eInventorySlot.RightHandWeapon) != null)
                return false;
            if (!bot.Inventory.MoveItem(eInventorySlot.LeftHandWeapon, eInventorySlot.RightHandWeapon, 1))
                return false;
            bot.RefreshItemBonuses();
            if (!bot.IsAttacking && bot.ActiveWeaponSlot == eActiveWeaponSlot.Standard)
                bot.SwitchWeapon(eActiveWeaponSlot.Standard);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            return true;
        }

        /// <summary>
        /// A gamebot that fights with two weapons and holds the one it trained more in its left hand swaps hands, so its
        /// main hand swings the trained weapon (a player sets his hands himself; a hired companion's hands are its
        /// player's choice and stay as they are). Left-axe builds and Savages have their own hand rules.
        /// </summary>
        private static bool SwapHandsForTrainedWeapon(GameBot bot)
        {
            if (HiredCompanions165.IsHired(bot) || bot.BotSpec?.SpecType == eSpecType.LeftAxe ||
                bot.CharacterClass?.ID == (int)eCharacterClass.Savage)
                return false;
            DbInventoryItem left = bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon);
            DbInventoryItem right = bot.Inventory.GetItem(eInventorySlot.RightHandWeapon);
            if (left == null || right == null || !BotWeaponStats.IsMeleeWeapon((eObjectType)left.Object_Type) ||
                !BotWeaponStats.IsMeleeWeapon((eObjectType)right.Object_Type) ||
                WeaponSpecBonus(bot, left.Object_Type, eInventorySlot.RightHandWeapon) <=
                WeaponSpecBonus(bot, right.Object_Type, eInventorySlot.RightHandWeapon))
                return false;
            // A main-hand-only weapon can't take the left hand: it goes to the backpack (2026-11-01 run 2: a Shadowblade
            // kept its right-hand training sword because the swap needed the sword to fit the left hand).
            if ((eInventorySlot)right.Item_Type != eInventorySlot.LeftHandWeapon)
            {
                eInventorySlot free = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (free == eInventorySlot.Invalid || !bot.Inventory.MoveItem(eInventorySlot.RightHandWeapon, free, 1))
                    return false;
            }
            if (!bot.Inventory.MoveItem(eInventorySlot.LeftHandWeapon, eInventorySlot.RightHandWeapon, 1))
                return false;
            if (bot.Inventory.GetItem(eInventorySlot.RightHandWeapon) != left)
                return false;
            bot.RefreshItemBonuses();
            if (!bot.IsAttacking && bot.ActiveWeaponSlot == eActiveWeaponSlot.Standard)
                bot.SwitchWeapon(eActiveWeaponSlot.Standard);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            return true;
        }

        private static eInventorySlot WorseSlot(GameBot bot, eInventorySlot first, eInventorySlot second)
    {
        DbInventoryItem firstItem = bot.Inventory.GetItem(first);
        DbInventoryItem secondItem = bot.Inventory.GetItem(second);
        if (firstItem == null)
            return first;
        if (secondItem == null)
            return second;
        return EquipmentValue(firstItem) <= EquipmentValue(secondItem) ? first : second;
    }

    // ---- smart selling (2026-11-11 exchange plan, section 2.2): a veteran player's rules

    /// <summary>A realm holds at most this many bot copies of one item (players are never capped).</summary>
    public const int MaxRealmBotCopies = 6;
    /// <summary>One bot lists at most this many copies of the same item.</summary>
    public const int MaxBotCopiesPerName = 2;
    /// <summary>Above this many bot listings in a realm, a bot may only replace one of its own listings, never add one.</summary>
    public const int RealmSoftCeiling = 3_000;
    /// <summary>A crafter's extra listing slots, for its own crafted products only (owner call 2026-11-11).</summary>
    public const int CrafterExtraSlots = 2;
    /// <summary>A bag item replaces the bot's weakest listing when its gain is at least this many times that listing's.</summary>
    public const double SwapGainRatio = 1.5;
    /// <summary>A listing must have been up this long before it can be replaced.</summary>
    public static readonly TimeSpan SwapMinimumAge = TimeSpan.FromHours(2);

    /// <summary>A bot's listings by level (owner call 2026-11-11): 3 below 20, 4 at 20-34, 5 at 35+.</summary>
    public static int ListingCap(int level) => level >= 35 ? 5 : level >= 20 ? 4 : MaxBotRealmExchangeListings;

    /// <summary>The bot's own crafted products get its extra crafter slots.</summary>
    public static bool IsOwnCrafted(GameBot bot, DbInventoryItem item) =>
        bot != null && item != null && item.IsCrafted && !string.IsNullOrEmpty(item.Creator) &&
        string.Equals(item.Creator, bot.Name, StringComparison.Ordinal);

    public static int CraftedSlots(GameBot bot) => CraftOf(bot) != eCraftingSkill.NoCrafting ? CrafterExtraSlots : 0;

    /// <summary>A bot's listing room: general slots by level, plus crafted-only slots for a crafter's own products (its own
    /// crafted listings fill those first).</summary>
    public readonly record struct ListingRoom(int General, int Crafted, int GeneralUsed, int CraftedUsed)
    {
        public bool HasGeneral => GeneralUsed < General;
        public bool HasCrafted => CraftedUsed < Crafted;
        public bool For(bool ownCrafted) => HasGeneral || ownCrafted && HasCrafted;
        public int Free => Math.Max(0, General - GeneralUsed) + Math.Max(0, Crafted - CraftedUsed);
    }

    public static ListingRoom RoomOf(GameBot bot, MarketBook.Listing[] own)
    {
        int ownCrafted = 0;
        foreach (MarketBook.Listing listing in own)
            if (IsOwnCrafted(bot, listing.Item))
                ownCrafted++;
        int craftedSlots = CraftedSlots(bot);
        int craftedUsed = Math.Min(ownCrafted, craftedSlots);
        return new ListingRoom(ListingCap(bot.Level), craftedSlots, own.Length - craftedUsed, craftedUsed);
    }

    /// <summary>In True 1.65 Mode a bot sells on the exchange from level 10 (its brokers stand in dangerous country).</summary>
    public static bool CanSellOnExchange(GameBot bot) =>
        bot != null && (!Classic165Mode.IsOn || bot.Level >= 10) &&
        !AutonomousServiceFallback.IsExchangeUnreachable(bot.DatabaseID, GameLoop.GameLoopTime);

    /// <summary>Owner call 2026-11-11: bots buy from level 5 (selling stays level 10+), so the low-level stock new bots keep
    /// producing has buyers.</summary>
    public static bool CanBuyOnExchange(GameBot bot) =>
        bot != null && (!Classic165Mode.IsOn || bot.Level >= 5) &&
        !AutonomousServiceFallback.IsExchangeUnreachable(bot.DatabaseID, GameLoop.GameLoopTime);

    /// <summary>
    /// The crafter keeps the materials of its next recipes, as many as it uses (CrafterBots165.WantedMaterials): those
    /// stacks, in bag order up to that count, are never listed or sold; a surplus stack may be (review 2026-11-11).
    /// </summary>
    public static bool IsOwnCraftMaterial(GameBot bot, DbInventoryItem item)
    {
        if (bot?.Inventory == null || item?.Id_nb == null)
            return false;
        int wanted = CrafterBots165.WantedMaterial(bot.PersistentRecord, bot.Realm, item.Id_nb);
        if (wanted <= 0)
            return false;
        int before = 0;
        for (eInventorySlot slot = eInventorySlot.FirstBackpack; slot <= eInventorySlot.LastBackpack; slot++)
        {
            DbInventoryItem other = bot.Inventory.GetItem(slot);
            if (other == null)
                continue;
            if (ReferenceEquals(other, item))
                return before < wanted;
            if (string.Equals(other.Id_nb, item.Id_nb, StringComparison.OrdinalIgnoreCase))
                before += Math.Max(1, other.Count);
        }
        return before < wanted;
    }

    // a bot swaps at most one listing per exchange visit (its between-task errand)
    private static readonly ConcurrentDictionary<long, string> SwapVisits = new();

    private static string VisitKey(GameBot bot) => bot?.PersistentRecord?.ObjectiveAssignmentId ?? string.Empty;

    public static bool SwappedThisVisit(GameBot bot) =>
        bot != null && SwapVisits.TryGetValue(bot.DatabaseID, out string visit) && visit == VisitKey(bot);

    private static void NoteSwap(GameBot bot)
    {
        if (SwapVisits.Count > 16_384)
            SwapVisits.Clear();
        SwapVisits[bot.DatabaseID] = VisitKey(bot);
    }

    /// <summary>What the bot knows about its own listings and its bag when it decides what to list or keep.</summary>
    private sealed class SellContext
    {
        public string Owner;
        public MarketBook.Listing[] Own = Array.Empty<MarketBook.Listing>();
        public ListingRoom Room;
        public bool CeilingReached;
        public readonly List<ListingCandidate> Ranked = new();
    }

    /// <summary>A bag item a bot would list: never its own supplies or craft materials, never merchant stock asked at 90%
    /// of the merchant's price or more, never a small gain over the merchant, never grey loot unless its gain is high,
    /// never an item that already failed to sell.</summary>
    private static bool IsListable(GameBot bot, DbInventoryItem item, HashSet<DbInventoryItem> operationalLoadout)
    {
        if (item == null || !item.IsTradable || string.IsNullOrWhiteSpace(item.Name) || item.OwnerLot != 0 ||
            item.SlotPosition < (int)eInventorySlot.FirstBackpack || item.SlotPosition > (int)eInventorySlot.LastBackpack ||
            operationalLoadout.Contains(item) || BotSiegeRuntime.IsSupply(item.Id_nb) || item is GameInventoryRelic)
            return false;
        if (Classic165Mode.IsOn && !HasRealUse(item))
            return false;
        if (IsOwnSupply(bot, item) || IsOwnCraftMaterial(bot, item) || IsSealCurrency(item.Id_nb) || Exchange165.HasFailed(item))
            return false;
        if (!ItemValue165.WorthListing(item, bot.Level))
            return false;
        return !TryGetEquipmentUpgrade(bot, item, out _);
    }

    /// <summary>
    /// The candidate for one bag item against the market: copy limits (a realm's 6 bot copies, a bot's own 2) and the
    /// ask, undercutting the cheapest other copy. At the realm's copy limit it is listed only when it undercuts the dearest
    /// bot copy, which then goes back to its owner. Null when it should go to a merchant instead.
    /// </summary>
    private static ListingCandidate Appraise(GameBot bot, DbInventoryItem item, string owner, long materialCost, double mood)
    {
        MarketBook.NameStats stats = MarketCache.ReadBook(book => book.Name(bot.Realm, item.Name, owner));
        if (stats.OwnCopies >= MaxBotCopiesPerName)
            return null;
        int count = Math.Max(1, item.Count);
        long cheapestOther = stats.CheapestFor(count);
        long ask = ItemValue165.Ask(item, mood, cheapestOther, materialCost);
        MarketBook.Listing bump = null;
        if (stats.BotCopies >= MaxRealmBotCopies)
        {
            if (stats.DearestOtherBot == null || ask >= stats.DearestOtherBot.PriceFor(count))
                return null;
            bump = stats.DearestOtherBot;
        }
        long gain = Math.Max(0, ask - ItemValue165.Floor(item));
        return new ListingCandidate(item, (int)Math.Clamp(ask, 1, int.MaxValue))
        {
            Gain = gain, Bump = bump, OwnCrafted = IsOwnCrafted(bot, item), CheapestOther = cheapestOther,
        };
    }

    private static SellContext BuildSellContext(GameBot bot, HashSet<DbInventoryItem> operationalLoadout)
    {
        var context = new SellContext { Owner = GetOwnerId(bot.DatabaseID) };
        context.Own = MarketCache.ReadBook(book => book.OwnerListings(context.Owner));
        context.Room = RoomOf(bot, context.Own);
        context.CeilingReached = MarketCache.ReadBook(book => book.BotListings(bot.Realm)) >= RealmSoftCeiling;
        foreach (DbInventoryItem item in bot.Inventory.AllItems)
        {
            if (!IsListable(bot, item, operationalLoadout))
                continue;
            ListingCandidate candidate = Appraise(bot, item, context.Owner, 0, 0);
            if (candidate != null)
                context.Ranked.Add(candidate);
        }
        context.Ranked.Sort((a, b) => b.Gain != a.Gain ? b.Gain.CompareTo(a.Gain) : b.PriceCopper.CompareTo(a.PriceCopper));
        return context;
    }

    /// <summary>
    /// The bot's weakest listing this candidate may replace: one up at least two hours (an unknown age counts as old), and
    /// a slot the candidate may hold (a crafter's crafted-only slots take only its own products). Null when none.
    /// </summary>
    private static MarketBook.Listing WeakestReplaceable(GameBot bot, SellContext context, bool ownCrafted, DateTime utcNow)
    {
        int ownCraftedListed = 0;
        foreach (MarketBook.Listing listing in context.Own)
            if (IsOwnCrafted(bot, listing.Item))
                ownCraftedListed++;
        MarketBook.Listing weakest = null;
        foreach (MarketBook.Listing listing in context.Own)
        {
            if (listing.ListedTicks > 0 && utcNow.Ticks - listing.ListedTicks < SwapMinimumAge.Ticks)
                continue;
            // a non-crafted item may only take a crafted listing's slot when crafted listings overflow into general slots
            if (!ownCrafted && IsOwnCrafted(bot, listing.Item) && ownCraftedListed <= context.Room.Crafted)
                continue;
            if (weakest == null || listing.Gain < weakest.Gain)
                weakest = listing;
        }
        return weakest;
    }

    /// <summary>The listing plan for this context: list the best candidate that has room, else replace the weakest
    /// listing with the best candidate whose gain is at least 1.5 x its own (one swap per visit).</summary>
    private static ListingCandidate Plan(GameBot bot, SellContext context)
    {
        if (context.Ranked.Count == 0)
            return null;
        if (!context.CeilingReached)
            foreach (ListingCandidate candidate in context.Ranked)
                if (context.Room.For(candidate.OwnCrafted))
                    return candidate;
        if (SwappedThisVisit(bot))
            return null;
        DateTime now = DateTime.UtcNow;
        foreach (ListingCandidate candidate in context.Ranked)
        {
            MarketBook.Listing weakest = WeakestReplaceable(bot, context, candidate.OwnCrafted, now);
            if (weakest != null && candidate.Gain >= SwapGainRatio * Math.Max(1, weakest.Gain))
                return candidate with { Replace = weakest };
        }
        return null;
    }

    public static ListingCandidate FindValuableListingCandidate(GameBot bot)
    {
        if (bot?.Inventory == null || bot.DatabaseID <= 0 || !CanSellOnExchange(bot) ||
            (!IsBackpackFull(bot) && !AutonomousObjectiveAssignments.IsBetweenPveTasks(bot)))
            return null;
        return Plan(bot, BuildSellContext(bot, FindOperationalLoadoutItems(bot)));
    }

    /// <summary>
    /// The bag items kept for the exchange (the vendor phase sells the rest): the best candidates by gain, as many as the
    /// bot can list - its free room, one more when a swap would be worth it - plus one for its next visit. The choice and
    /// the sale use this same rule (owner 2026-10-31 run: 339 bots stood at vendors whose sale refused the item the choice
    /// had picked). A capped bot with good loot therefore keeps it for a swap instead of selling it to a merchant; with
    /// no room and no swap worth making, everything goes to the merchant.
    /// </summary>
    private static HashSet<DbInventoryItem> KeptForExchange(GameBot bot, HashSet<DbInventoryItem> operationalLoadout)
    {
        var kept = new HashSet<DbInventoryItem>();
        if (bot?.Inventory == null || bot.DatabaseID <= 0 || !CanSellOnExchange(bot))
            return kept;
        SellContext context = BuildSellContext(bot, operationalLoadout);
        ListingCandidate plan = Plan(bot, context);
        // one more for the next visit only while it still has room; a capped bot with no swap worth making sells
        int free = context.CeilingReached ? 0 : context.Room.Free;
        int keep = free + (plan?.Replace != null ? 1 : 0) + (free > 0 ? 1 : 0);
        if (plan != null)
            kept.Add(plan.Item);
        foreach (ListingCandidate candidate in context.Ranked)
        {
            if (kept.Count >= keep)
                break;
            kept.Add(candidate.Item);
        }
        return kept;
    }

    /// <summary>Whether the bot has listing room at all (and the level to sell): BotEnchant165 asks before enchanting.</summary>
    public static bool CanListOnExchange(GameBot bot)
    {
        if (bot == null || bot.DatabaseID <= 0 || Classic165Mode.IsOn && bot.Level < 10)
            return false;
        ListingRoom room = RoomOf(bot, MarketCache.ReadBook(book => book.OwnerListings(GetOwnerId(bot.DatabaseID))));
        return room.HasGeneral || room.HasCrafted;
    }

    /// <summary>The items the vendor phase keeps for the exchange: as <see cref="KeptForExchange"/>, except that a bot with
    /// no broker in reach sells them too while its bag is nearly full (owner 2026-11-08, AutonomousServiceFallback).</summary>
    private static HashSet<DbInventoryItem> KeepsForExchange(GameBot bot, HashSet<DbInventoryItem> operationalLoadout)
    {
        bool unreachable = AutonomousServiceFallback.IsExchangeUnreachable(bot.DatabaseID, GameLoop.GameLoopTime);
        if (!AutonomousServiceFallback.KeepsForExchange(true, unreachable, IsBackpackNearlyFull(bot)))
            return new HashSet<DbInventoryItem>();
        // the level rule applies, the reach rule above already decided (a bot that can't reach a broker keeps its loot
        // while the bag has room)
        if (bot == null || Classic165Mode.IsOn && bot.Level < 10)
            return new HashSet<DbInventoryItem>();
        if (unreachable)
        {
            var all = new HashSet<DbInventoryItem>();
            foreach (DbInventoryItem item in bot.Inventory.AllItems)
                if (IsListable(bot, item, operationalLoadout))
                    all.Add(item);
            return all;
        }
        return KeptForExchange(bot, operationalLoadout);
    }

    /// <summary>
    /// A crafter's new product (2026-11-11 exchange plan, section 2.3): listed (remotely, as before) when it is worth a
    /// slot and has room - its 2 crafted-only slots or a general one - or when it beats the weakest listing it may replace
    /// by 1.5 x; otherwise null and it goes to a merchant as today.
    /// </summary>
    public static ListingCandidate PlanCraftedListing(GameBot bot, DbInventoryItem product, long materialCost, double mood)
    {
        if (bot?.Inventory == null || product == null || bot.DatabaseID <= 0 || Classic165Mode.IsOn && bot.Level < 10 ||
            !product.IsTradable || !ItemValue165.WorthListing(product, bot.Level, materialCost))
            return null;
        var context = new SellContext { Owner = GetOwnerId(bot.DatabaseID) };
        context.Own = MarketCache.ReadBook(book => book.OwnerListings(context.Owner));
        context.Room = RoomOf(bot, context.Own);
        context.CeilingReached = MarketCache.ReadBook(book => book.BotListings(bot.Realm)) >= RealmSoftCeiling;
        ListingCandidate candidate = Appraise(bot, product, context.Owner, materialCost, mood);
        if (candidate == null)
            return null;
        // never listed at a loss: when 1.1 x its materials is over the merchant's ceiling, or can't undercut the cheapest
        // copy already listed, a merchant buys it (review 2026-11-11)
        long minimum = ItemValue165.CraftedFloor(materialCost);
        if (minimum > 0 && (minimum > ItemValue165.AskCeiling(product) || candidate.CheapestOther > 0 && minimum >= candidate.CheapestOther))
            return null;
        candidate = candidate with
        {
            Gain = Math.Max(0, ItemValue165.Fair(product, materialCost) - ItemValue165.Floor(product)), MinimumAsk = minimum,
        };
        if (!context.CeilingReached && context.Room.For(candidate.OwnCrafted))
            return candidate;
        if (SwappedThisVisit(bot))
            return null;
        MarketBook.Listing weakest = WeakestReplaceable(bot, context, candidate.OwnCrafted, DateTime.UtcNow);
        return weakest != null && candidate.Gain >= SwapGainRatio * Math.Max(1, weakest.Gain) ? candidate with { Replace = weakest } : null;
    }

        // owner 2026-11-08: when a gamebot's backpack first refused loot (AUTONOMOUS_LOOT_REJECTED backpack_full) since its
        // last unload. Cleared by an unload (a vendor sale, the end of the between-task errands) or when the bag has room
        // again, so only a fresh refusal ends a later hunt. The task allocator ends a solo hunt that keeps a full bag.
        private static readonly ConcurrentDictionary<long, long> BackpackFullSince = new();
        private static int _backpackFullCount;
        private const int MaxBackpackFullEntries = 16_384;

        public static void NoteBackpackFull(GameBot bot)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.DatabaseID <= 0 ||
                HiredCompanions165.IsHired(bot))
                return;
            NoteBackpackFull(bot.DatabaseID, GameLoop.GameLoopTime);
        }

        /// <summary>Records the first refusal since the last unload (later refusals keep the first tick).</summary>
        public static void NoteBackpackFull(long botId, long now)
        {
            if (botId <= 0 || !BackpackFullSince.TryAdd(botId, now))
                return;
            // the size is only looked at when an entry is added (Count locks the whole dictionary)
            if (Interlocked.Increment(ref _backpackFullCount) > MaxBackpackFullEntries)
            {
                BackpackFullSince.Clear();
                Interlocked.Exchange(ref _backpackFullCount, 0);
            }
        }

        /// <summary>An unload happened: the full-bag clock starts over at the next refusal.</summary>
        public static void ClearBackpackFull(long botId)
        {
            if (botId > 0 && BackpackFullSince.TryRemove(botId, out _))
                Interlocked.Decrement(ref _backpackFullCount);
        }

        /// <summary>The raw first-refusal tick since the last unload (tests and diagnostics).</summary>
        public static long? BackpackFullSinceTick(long botId) =>
            BackpackFullSince.TryGetValue(botId, out long since) ? since : null;

        /// <summary>The tick the bag first refused loot, while it is still nearly full; null otherwise (and forgotten).</summary>
        public static long? FullBackpackSince(GameBot bot)
        {
            if (bot == null || bot.DatabaseID <= 0 || !BackpackFullSince.TryGetValue(bot.DatabaseID, out long since))
                return null;
            if (IsBackpackNearlyFull(bot))
                return since;
            ClearBackpackFull(bot.DatabaseID);
            return null;
        }

        public static DbInventoryItem FindVendorTrashCandidate(GameBot bot)
        {
            if (bot?.Inventory == null)
                return null;

            HashSet<DbInventoryItem> operationalLoadout = FindOperationalLoadoutItems(bot);
            HashSet<DbInventoryItem> kept = KeepsForExchange(bot, operationalLoadout);
            return bot.Inventory.AllItems
                .Where(item => item != null && item.IsDropable && item.OwnerLot == 0 &&
                               item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                               item.SlotPosition <= (int)eInventorySlot.LastBackpack)
                .Where(item => !IsProtectedFromVendor(bot, item, kept, operationalLoadout))
                .Where(item => CalculateStandardVendorSaleCopper(item) > 0)
                .OrderBy(item => EquipmentValue(item))
                .ThenBy(item => item.Price)
                .FirstOrDefault();
        }

        public static bool IsOwnSupply(GameBot bot, DbInventoryItem item)
        {
            if (bot?.Inventory == null || item == null)
                return false;
            if ((eObjectType)item.Object_Type is eObjectType.Arrow or eObjectType.Bolt)
                return Supplies165.AmmoType(bot.Inventory.GetItem(eInventorySlot.DistanceWeapon)) == (eObjectType)item.Object_Type;
            if ((eObjectType)item.Object_Type == eObjectType.Poison)
                return bot.CharacterClass != null && BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID);
            // a heal, power or endurance draught the bot drinks (BotPotions165, veteran-bots review 2026-11-11 / O13)
            if (BotPotions165.IsPotion(item) && SkillBase.GetSpellLine(GlobalSpellsLines.Potions_Effects) is { } line &&
                SkillBase.FindSpell(item.SpellID, line) is { } spell)
                return BotPotions165.KindOf(spell.SpellType.ToString()) != BotPotions165.Kind.None;
            return false;
        }

        /// <summary>The three Darkness Falls seals (the MoneyKey of the seal merchants there).</summary>
        public static bool IsSealCurrency(string itemId) =>
            string.Equals(itemId, "EmeraldSeal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(itemId, "SapphireSeal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(itemId, "DiamondSeal", StringComparison.OrdinalIgnoreCase);

        private static bool IsProtectedFromVendor(GameBot bot, DbInventoryItem item,
            HashSet<DbInventoryItem> keptForExchange, HashSet<DbInventoryItem> operationalLoadout)
        {
            if (item is GameInventoryRelic || BotSiegeRuntime.IsSupply(item.Id_nb) ||
                operationalLoadout.Contains(item))
                return true;
            // The bot's own supplies are never trash (owner 2026-11-05 log audit: bots bought arrows and poisons and sold
            // them straight back on the next pulse, ~350,000 times in 31 h, until they were broke; archers then fought in
            // melee with their training dirk): ammunition for the bow or crossbow it carries, an assassin's poisons.
            if (IsOwnSupply(bot, item))
                return true;
            // Darkness Falls seals are what the seal merchants there are paid in (TryBuySealItem): never coin at a
            // merchant (owner 2026-11-09, when the seals got a merchant value)
            if (IsSealCurrency(item.Id_nb))
                return true;
            if (TryGetEquipmentUpgrade(bot, item, out _))
                return true;
            // a crafter keeps its own recipes' materials (CrafterBots165.CraftOf; the old SerializedCraftingSkills test was
            // empty for every bot)
            if (IsOwnCraftMaterial(bot, item))
                return true;
            // Only what the bot would list next is kept for the Realm Exchange (KeptForExchange: its best candidates by
            // gain, as many as it can list or swap); everything else, failed listings included, is sold. Never vendor an
            // equippable upgrade.
            return keptForExchange.Contains(item);
        }

        /// <summary>
        /// True 1.65 Mode (owner 2026-11-03): what a bot may put on the Realm Exchange - only items with a real use, never
        /// vendor trash (monster parts, loot gems and plain no-bonus drops are sold to a merchant instead): crafted
        /// items (armour, weapons, potions...), equipment with magic bonuses, a proc or charges, crafting and salvage
        /// materials (anything a recipe uses), tradable quest items, dyes and enamels, potions, poisons, ammunition,
        /// spellcraft gems, alchemy tinctures, siege equipment and supplies, and housing items.
        /// </summary>
        public static bool HasRealUse(DbInventoryItem item)
        {
            if (item == null)
                return false;
            eObjectType type = (eObjectType)item.Object_Type;
            if (item.IsCrafted)
                return true;
            bool magic = item.Bonus1 != 0 || item.Bonus2 != 0 || item.Bonus3 != 0 || item.Bonus4 != 0 || item.Bonus5 != 0 ||
                         item.Bonus6 != 0 || item.Bonus7 != 0 || item.Bonus8 != 0 || item.Bonus9 != 0 || item.Bonus10 != 0 ||
                         item.ExtraBonus != 0;
            bool gear = type is >= eObjectType.GenericWeapon and <= eObjectType._LastWeapon or
                >= eObjectType._FirstArmor and <= eObjectType._LastArmor or eObjectType.Magical or eObjectType.Shield or
                eObjectType.Instrument;
            if (gear && magic)
                return true;
            if (item.SpellID != 0 || item.SpellID1 != 0 || item.ProcSpellID != 0 || item.ProcSpellID1 != 0 || item.PoisonSpellID != 0)
                return true;
            if (type is eObjectType.Poison or eObjectType.Arrow or eObjectType.Bolt or eObjectType.AlchemyTincture or
                    eObjectType.SpellcraftGem or >= eObjectType._FirstHouse and <= eObjectType._LastHouse or
                    >= eObjectType.SiegeBalista and <= eObjectType.SiegeTrebuchet ||
                BotSiegeRuntime.IsSupply(item.Id_nb))
                return true;
            string name = (item.Name ?? string.Empty).ToLowerInvariant();
            string id = (item.Id_nb ?? string.Empty).ToLowerInvariant();
            if (name.Contains("dye") || name.Contains("enamel") || id.Contains("_dye") || id.Contains("enamel"))
                return true;
            return !string.IsNullOrEmpty(item.Id_nb) && RealUseIds.Value.Contains(item.Id_nb);
        }

        /// <summary>Crafting materials (every recipe ingredient, salvage results included) and the items a quest asks
        /// for, from the database once.</summary>
        private static readonly Lazy<HashSet<string>> RealUseIds = new(() =>
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (DbCraftedXItem part in GameServer.Database.SelectAllObjects<DbCraftedXItem>())
                    if (!string.IsNullOrEmpty(part.IngredientId_nb))
                        ids.Add(part.IngredientId_nb);
                foreach (DbDataQuest quest in GameServer.Database.SelectAllObjects<DbDataQuest>())
                    foreach (string list in new[] { quest.StepItemTemplates, quest.CollectItemTemplate })
                        foreach (string entry in (list ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries))
                            if (entry.Split(';')[0].Trim() is { Length: > 0 } questItem)
                                ids.Add(questItem);
            }
            catch (Exception)
            {
                // no database (tests): no materials or quest items known
            }
            return ids;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Performs the same appraisal and range checks as a real merchant, removes the exact
        /// persisted inventory row, then queues the bot's real copper ledger and inventory
        /// in the shared crash-safe persistence batch.
        /// It deliberately never sells equipped, usable upgrades, material stacks, or exchange goods.
        /// </summary>
        public static bool TrySellToVendor(GameBot bot, GameMerchant merchant, DbInventoryItem item, out long copper)
        {
            copper = 0;
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
                merchant == null || item == null || !item.IsDropable || item.OwnerLot != 0 ||
                !bot.Inventory.AllItems.Contains(item) ||
                item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack ||
                !merchant.IsWithinRadius(bot, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
                return false;

            HashSet<DbInventoryItem> operationalLoadout = FindOperationalLoadoutItems(bot);
            if (IsProtectedFromVendor(bot, item, KeepsForExchange(bot, operationalLoadout), operationalLoadout))
                return false;
            bool failedListing = Exchange165.RelistOf(item) != null;

            copper = CalculateStandardVendorSaleCopper(item);
            int originalSlot = item.SlotPosition;
            if (copper <= 0 || !bot.Inventory.RemoveItem(item))
            {
                copper = 0;
                return false;
            }

            if (!AddMoney(bot.DatabaseID, copper, BotMoneyLedger165.Kind.VendorSale))
            {
                // A failed durable credit must not destroy the item; restoring the exact slot
                // preserves the finite inventory invariant and makes the sale retryable.
                bot.Inventory.AddItem((eInventorySlot)originalSlot, item);
                AutonomousBotStatusPersistence.Queue(bot, true);
                copper = 0;
                return false;
            }

            // a pulled-back listing is gone to the merchant: its relist row goes too
            if (failedListing)
            {
                Exchange165.Forget(item);
                RealmExchangeStats.Note(bot.Realm, RealmExchangeStats.Counter.Vendored);
            }
            bot.MarkAutonomousStateDirty();
            MarkInventoryChanged(bot);
            ClearBackpackFull(bot.DatabaseID);   // owner 2026-11-08: an unload restarts the full-bag clock
            AutonomousBotStatusPersistence.Queue(bot, true);
            // a big vendor sale is logged (2026-11-05: level-3 bots sold level-49 "Gnawed Bones" for ~37,700 copper each
            // and nothing in the log showed where their money came from)
            if (copper >= 10_000)
                Log.Info($"BOT_VENDOR_SALE bot={bot.Name} id={bot.DatabaseID} level={bot.Level} item=\"{item.Name}\" " +
                         $"item_level={item.Level} copper={copper}");
            return true;
        }

        /// <summary>
        /// The ordinary GameMerchant appraisal formula, factored for GameBot because the legacy
        /// merchant packet entry point accepts GamePlayer rather than the server's NPC-backed
        /// autonomous IGamePlayer implementation.
        /// </summary>
        public static long CalculateStandardVendorSaleCopper(DbInventoryItem item)
        {
            if (item == null || !item.IsDropable)
                return 0;

            int itemCount = Math.Max(1, item.Count);
            int packSize = Math.Max(1, item.PackSize);
            long copper = item.Price * (long)itemCount / packSize * GS.ServerProperties.Properties.ITEM_SELL_RATIO / 100;
            return item.Price == 1 && copper == 0
                ? item.Price * (long)itemCount / packSize
                : copper;
        }

        /// <summary>
        /// Buys one legal, material equipment upgrade from the nearby merchant's real trade list.
        /// The item is first inserted into the finite backpack, charged from saved copper, and
        /// immediately equipped through the same owned-upgrade path used by dropped equipment.
        /// </summary>
        /// <summary>
        /// A merchant whose prices are in coin. Seal, glass, aurulite and other item-currency merchants, and the bounty
        /// point merchants, price in their own currency, but the vendor upgrade paid their price in copper: a bot next
        /// to a Darkness Falls seals merchant bought seal gear for 15 to 51 copper (owner 2026-10-30).
        /// </summary>
        public static bool SellsForCoin(GameMerchant merchant) =>
            merchant != null && merchant is not GameItemCurrencyMerchant && merchant is not GameBountyMerchant;

        public static bool TryBuyUsefulVendorUpgrade(GameBot bot, GameMerchant merchant, out DbInventoryItem purchased, out long copper)
        {
            purchased = null;
            copper = 0;
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
                !SellsForCoin(merchant) ||
                merchant?.TradeItems == null || !merchant.IsWithinRadius(bot, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE) ||
                bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid)
                return false;

            // Only what the bot can pay for: the best upgrade overall was chosen first and, when it cost more than the
            // bot had, nothing was bought although cheaper upgrades were on the same list (run 1c, 2026-10-30).
            // Within the shopping budget (a reserve stays for repairs and arrows) and at most one level above the bot,
            // whose bonuses would be capped anyway (owner 2026-10-31).
            long coin = AutonomousWorldBotController.ShoppingBudget(bot);
            DbInventoryItem[] upgrades = merchant.TradeItems.GetAllItems().Values
                .OfType<DbItemTemplate>()
                .Where(template => AutonomousWorldBotController.ShoppingConsiders(bot, template, coin) &&
                                   AutonomousWorldBotController.ShoppingAllowsWhileArmorMissing(bot, template, coin) &&
                                   AutonomousWorldBotController.VendorPieceAllowed(bot, template))
                .Select(GameInventoryItem.Create)
                .Where(item => item != null && IsWorthBuying(bot, item, out _))
                .OrderByDescending(EquipmentValue)
                .ToArray();
            DbInventoryItem selected = upgrades.FirstOrDefault();
            if (selected != null && bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
                BotWeaponStats.IsMeleeWeapon((eObjectType)selected.Object_Type))
                selected = upgrades
                    .Where(item => BotWeaponStats.IsMeleeWeapon((eObjectType)item.Object_Type))
                    .OrderByDescending(item => SavageBotWeaponPolicy.UpgradeGain(bot, item,
                        ResolveEquipmentSlot(bot, item)))
                    .ThenByDescending(EquipmentValue)
                    .First();
            DbItemTemplate template = selected?.Template;
            if (template == null || template.Price <= 0)
                return false;

            copper = template.Price;
            eInventorySlot backpackSlot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            purchased = GameInventoryItem.Create(template);
            bool spent = purchased != null && TrySpend(bot.DatabaseID, copper, BotMoneyLedger165.Kind.VendorGear);
            if (!spent || !bot.Inventory.AddItem(backpackSlot, purchased))
            {
                if (spent && bot.Inventory.GetItem(backpackSlot) != purchased)
                    AddMoney(bot.DatabaseID, copper, BotMoneyLedger165.Kind.VendorGear);
                purchased = null;
                copper = 0;
                return false;
            }

            TryEquipOwnedUpgrade(bot, purchased, "vendor");
            AutonomousWorldBotController.ClearShoppingPlan(bot);
            MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            return true;
        }

    /// <summary>
    /// Seal gear (owner 2026-10-30): a bot grinding Darkness Falls spends the seals it has looted at its own realm's seal
    /// merchants there, as a player would. Each merchant prices its stock in its own seal (Emerald, Sapphire or Diamond).
    /// An affordable upgrade is bought and worn. With no upgrade left to save that seal for, the seals buy the most
    /// valuable item they can, which the bot then sells or posts on the Realm Exchange like any other loot; while a
    /// dearer upgrade exists, it keeps saving for it.
    /// </summary>
    public readonly record struct SealPurchase(DbItemTemplate Template, bool Upgrade);

    public static int SealCount(GameBot bot, string sealId) =>
        bot?.Inventory == null || string.IsNullOrEmpty(sealId) ? 0 :
            bot.Inventory.CountItemTemplate(sealId, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);

    private static IEnumerable<GameDarknessFallsSealsMerchant> OwnSealMerchants(GameBot bot, string sealId) =>
        (bot?.CurrentRegion?.Merchants ?? []).OfType<GameDarknessFallsSealsMerchant>()
            .Where(merchant => merchant.ObjectState == GameObject.eObjectState.Active && merchant.MoneyKey == sealId &&
                               GameDarknessFallsSealsMerchant.CanTrade(merchant.Realm, bot.Realm));

    public static SealPurchase? FindSealPurchase(GameBot bot, GameDarknessFallsSealsMerchant merchant)
    {
        if (bot?.Inventory == null || merchant?.TradeItems == null ||
            !GameDarknessFallsSealsMerchant.CanTrade(merchant.Realm, bot.Realm))
            return null;
        int seals = SealCount(bot, merchant.MoneyKey);
        if (seals <= 0)
            return null;

        DbItemTemplate upgrade = null, resale = null;
        int upgradeValue = int.MinValue, resaleValue = int.MinValue;
        foreach (DbItemTemplate template in merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
        {
            if (template.Price <= 0 || template.Price > seals)
                continue;
            DbInventoryItem item = GameInventoryItem.Create(template);
            if (item == null)
                continue;
            if (IsWorthBuying(bot, item, out _))
            {
                int value = EquipmentValue(item);
                if (value > upgradeValue)
                {
                    upgrade = template;
                    upgradeValue = value;
                }
            }
            else if (item.IsTradable)
            {
                int value = RecommendListingPrice(item);
                if (value > resaleValue)
                {
                    resale = template;
                    resaleValue = value;
                }
            }
        }
        if (upgrade != null)
            return new SealPurchase(upgrade, true);
        if (resale == null || IsBackpackNearlyFull(bot) || SavingSealsForUpgrade(bot, merchant.MoneyKey, seals))
            return null;
        return new SealPurchase(resale, false);
    }

    /// <summary>Some merchant of this seal sells an upgrade the bot can't afford yet.</summary>
    private static bool SavingSealsForUpgrade(GameBot bot, string sealId, int seals)
    {
        foreach (GameDarknessFallsSealsMerchant merchant in OwnSealMerchants(bot, sealId))
            foreach (DbItemTemplate template in merchant.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>() ?? [])
                if (template.Price > seals && GameInventoryItem.Create(template) is DbInventoryItem item &&
                    IsWorthBuying(bot, item, out _))
                    return true;
        return false;
    }

    /// <summary>The bot is in Darkness Falls and one of its realm's seal merchants there has something to spend seals on.</summary>
    public static bool HasSealPurchase(GameBot bot)
    {
        if (bot?.Inventory == null || bot.CurrentRegionID != AutonomousDarknessFallsPolicy.RegionId || bot.CurrentRegion == null)
            return false;
        foreach (GameMerchant merchant in bot.CurrentRegion.Merchants)
            if (merchant is GameDarknessFallsSealsMerchant seals && seals.ObjectState == GameObject.eObjectState.Active &&
                FindSealPurchase(bot, seals) != null)
                return true;
        return false;
    }

    /// <summary>Buys the seal item chosen above at this merchant and pays in seals; an upgrade is worn at once.</summary>
    public static bool TryBuySealItem(GameBot bot, GameDarknessFallsSealsMerchant merchant, out DbInventoryItem purchased,
        out int paid, out bool upgrade)
    {
        purchased = null;
        paid = 0;
        upgrade = false;
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Inventory == null ||
            merchant == null || !merchant.IsWithinRadius(bot, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
            return false;
        SealPurchase? choice = FindSealPurchase(bot, merchant);
        eInventorySlot backpackSlot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
        if (choice == null || backpackSlot == eInventorySlot.Invalid)
            return false;
        DbItemTemplate template = choice.Value.Template;
        upgrade = choice.Value.Upgrade;

        lock (bot.Inventory.Lock)
        {
            int price = (int)template.Price;
            if (SealCount(bot, merchant.MoneyKey) < price)
                return false;
            DbInventoryItem item = GameInventoryItem.Create(template);
            if (item == null || !bot.Inventory.AddItem(backpackSlot, item))
                return false;
            int removed = 0;
            foreach (DbInventoryItem stack in bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).ToList())
            {
                if (stack == null || stack.Id_nb != merchant.MoneyKey)
                    continue;
                int take = Math.Min(stack.Count, price - removed);
                if (take > 0 && bot.Inventory.RemoveCountFromStack(stack, take))
                    removed += take;
                if (removed >= price)
                    break;
            }
            purchased = item;
            paid = removed;
        }

        Log.Info($"AUTONOMOUS_SEAL_PURCHASE bot={bot.Name} id={bot.DatabaseID} item=\"{purchased.Name}\" " +
                 $"seals={paid} seal={merchant.MoneyKey} use={(upgrade ? "upgrade" : "resale")} merchant=\"{merchant.Name}\"");
        if (upgrade)
            TryEquipOwnedUpgrade(bot, purchased, "seal merchant");
        MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        return true;
    }

    /// <summary>A seller's ask with its mood roll (-5%/+15%) against the copies already listed (ItemValue165).</summary>
    public static int RollListingPrice(DbInventoryItem item) => RecommendListingPrice(item, ItemValue165.RollMood());

    /// <summary>The ask for this item: its fair value x (1 + disposition), kept between 1.25 x the merchant value and
    /// 90% of a merchant's own price (ItemValue165). Listing plans also undercut the copies already listed.</summary>
    public static int RecommendListingPrice(DbInventoryItem item, double sellerDisposition = 0) =>
        item == null ? 0 : (int)Math.Clamp(ItemValue165.Ask(item, sellerDisposition), 1, int.MaxValue);

    /// <summary>A plan's ask with the seller's mood roll (-5%/+15%), still undercutting the cheapest other copy and, at a
    /// name's copy limit, under the copy it bumps.</summary>
    public static int RollPlannedPrice(ListingCandidate plan)
    {
        if (plan?.Item == null)
            return 0;
        long ask = Math.Max(ItemValue165.Ask(plan.Item, ItemValue165.RollMood(), plan.CheapestOther), plan.MinimumAsk);
        if (plan.Bump != null && ask >= plan.Bump.PriceFor(plan.Item.Count))
            ask = plan.PriceCopper;
        return (int)Math.Clamp(ask, 1, int.MaxValue);
    }

    public static bool TryList(GameBot bot, DbInventoryItem item, int priceCopper) => TryList(bot, item, priceCopper, out _);

    /// <summary>
    /// Lists one backpack item. Never waits for the exchange locks (2026-11-11 plan, phase 0): when they are busy it
    /// returns false with <paramref name="busy"/> set, and the bot tries again at its next broker check - a busy exchange
    /// is not a fruitless check.
    /// </summary>
    public static bool TryList(GameBot bot, DbInventoryItem item, int priceCopper, out bool busy)
    {
        busy = false;
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.DatabaseID <= 0 ||
            item == null || !item.IsTradable || priceCopper <= 0 || item.OwnerLot != 0 || BotSiegeRuntime.IsSupply(item.Id_nb) ||
            item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack)
            return false;
        if (!RealmExchangeLocks.TryEnter(0, true, out RealmExchangeLocks.Scope scope))
        {
            busy = true;
            return false;
        }
        using (scope)
        {
            if (MarketCache.ReadBook(book => book.BotListings(bot.Realm)) >= RealmSoftCeiling)
                return false;
            return TryListCore(bot, item, priceCopper, FindFreeListingSlot(bot, item));
        }
    }

    /// <summary>The listing write itself; the caller holds the exchange locks and chose the consignment slot.</summary>
    private static bool TryListCore(GameBot bot, DbInventoryItem item, int priceCopper, int slot)
    {
        string ownerId = GetOwnerId(bot.DatabaseID);
        if (slot < 0 || !bot.Inventory.AllItems.Contains(item))
            return false;

        // A retained inventory instance can carry a stale persistence flag.
        // Never INSERT its existing primary key again, and never adopt another
        // owner's row or an item already on sale. This runs only at listing,
        // not in the ordinary bot tick or inventory appraisal.
        if (!item.IsPersisted && !string.IsNullOrWhiteSpace(item.ObjectId))
        {
            DbInventoryItem stored = GameServer.Database.SelectObject<DbInventoryItem>(
                DB.Column("Inventory_ID").IsEqualTo(item.ObjectId));
            if (stored != null)
            {
                if (!CanReconcileListingItem(item, stored, ownerId))
                    return false;
                item.IsPersisted = true;
                item.Dirty = true;
            }
        }

        int originalSlot = item.SlotPosition;
        if (!bot.Inventory.RemoveItemWithoutDbDeletion(item))
            return false;

        item.OwnerID = ownerId;
        item.OwnerLot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
        item.SlotPosition = slot;
        item.SellPrice = priceCopper;
        item.RealmExchangeListedUtc = DateTime.UtcNow.ToString("O");
        bool saved = item.IsPersisted ? GameServer.Database.SaveObject(item) : GameServer.Database.AddObject(item);
        if (!saved)
        {
            item.OwnerLot = 0;
            item.SellPrice = 0;
            item.RealmExchangeListedUtc = string.Empty;
            bot.Inventory.AddItem((eInventorySlot)originalSlot, item);
            return false;
        }
        MarketCache.AddItem(item);
        MarkInventoryChanged(bot);
        RealmExchangeStats.Note(bot.Realm, RealmExchangeStats.Counter.Listed);
        return true;
    }

    /// <summary>
    /// Carries out a listing plan from <see cref="FindValuableListingCandidate"/> or <see cref="PlanCraftedListing"/>: a
    /// plain listing, a listing that bumps the dearest copy of a full name back to its owner, or a swap that pulls the
    /// bot's weakest listing back to its bag (marked to be sold to a merchant) and lists the better item in its place.
    /// Never waits for the exchange locks (<paramref name="busy"/>).
    /// </summary>
    public static bool TryListPlanned(GameBot bot, ListingCandidate plan, int priceCopper, out bool busy)
    {
        busy = false;
        if (plan?.Item == null || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.DatabaseID <= 0 ||
            priceCopper <= 0 || plan.Item.OwnerLot != 0 || !plan.Item.IsTradable || BotSiegeRuntime.IsSupply(plan.Item.Id_nb) ||
            plan.Item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack)
            return false;
        if (!RealmExchangeLocks.TryEnter(0, true, out RealmExchangeLocks.Scope scope))
        {
            busy = true;
            return false;
        }
        using (scope)
        {
            string ownerId = GetOwnerId(bot.DatabaseID);
            // the market may have moved since the plan: a name now at its copy limit needs a cheaper ask than its dearest copy
            MarketBook.NameStats stats = MarketCache.ReadBook(book => book.Name(bot.Realm, plan.Item.Name, ownerId));
            if (stats.OwnCopies >= MaxBotCopiesPerName)
                return false;
            MarketBook.Listing bump = null;
            if (stats.BotCopies >= MaxRealmBotCopies)
            {
                bump = stats.DearestOtherBot;
                if (bump == null)
                    return false;
                // per unit: the bumped copy's price for as many items as this listing holds
                long bumpPrice = bump.PriceFor(plan.Item.Count);
                if (priceCopper >= bumpPrice)
                    priceCopper = (int)Math.Max(Math.Max(ItemValue165.AskFloor(plan.Item), plan.MinimumAsk), bumpPrice - 1);
                if (priceCopper >= bumpPrice)
                    return false;
            }
            bool listed;
            if (plan.Replace != null)
                listed = TrySwapCore(bot, plan.Item, priceCopper, plan.Replace.Item, ownerId);
            else
            {
                listed = MarketCache.ReadBook(book => book.BotListings(bot.Realm)) < RealmSoftCeiling &&
                         TryListCore(bot, plan.Item, priceCopper, FindFreeListingSlot(bot, plan.Item));
            }
            if (listed && bump?.BotId > 0 && MarketCache.Contains(bump.Item) &&
                Exchange165.Pull(bump.Item, bump.BotId, (int)bump.Price, RealmExchangeStats.Counter.Bumped))
                MarkEconomyChanged(bump.BotId);
            return listed;
        }
    }

    /// <summary>The swap: the new item leaves the bag, the weak listing comes back into that bag slot (marked failed: it
    /// is sold to a merchant on this trip) and the new item takes the weak listing's consignment slot.</summary>
    private static bool TrySwapCore(GameBot bot, DbInventoryItem item, int priceCopper, DbInventoryItem weak, string ownerId)
    {
        if (weak == null || weak.OwnerID != ownerId || !MarketCache.Contains(weak) || !bot.Inventory.AllItems.Contains(item) ||
            SwappedThisVisit(bot))
            return false;
        int bagSlot = item.SlotPosition;
        int listingSlot = weak.SlotPosition;
        int weakPrice = weak.SellPrice;
        ushort lot = weak.OwnerLot;
        string weakListed = weak.RealmExchangeListedUtc;
        if (!bot.Inventory.RemoveItemWithoutDbDeletion(item))
            return false;
        MarketCache.RemoveItem(weak);
        weak.OwnerLot = 0;
        weak.SellPrice = 0;
        weak.RealmExchangeListedUtc = string.Empty;
        if (!bot.Inventory.AddItem((eInventorySlot)bagSlot, weak))
        {
            Restore();
            return false;
        }
        item.OwnerID = ownerId;
        item.OwnerLot = lot;
        item.SlotPosition = listingSlot;
        item.SellPrice = priceCopper;
        item.RealmExchangeListedUtc = DateTime.UtcNow.ToString("O");
        bool saved = item.IsPersisted ? GameServer.Database.SaveObject(item) : GameServer.Database.AddObject(item);
        if (!saved)
        {
            bot.Inventory.RemoveItemWithoutDbDeletion(weak);
            item.OwnerLot = 0;
            item.SellPrice = 0;
            item.RealmExchangeListedUtc = string.Empty;
            Restore();
            return false;
        }
        MarketCache.AddItem(item);
        Exchange165.MarkFailed(weak, weakPrice);
        NoteSwap(bot);
        MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        RealmExchangeStats.Note(bot.Realm, RealmExchangeStats.Counter.Swapped);
        RealmExchangeStats.Note(bot.Realm, RealmExchangeStats.Counter.Listed);
        return true;

        void Restore()
        {
            weak.OwnerID = ownerId;
            weak.OwnerLot = lot;
            weak.SlotPosition = listingSlot;
            weak.SellPrice = weakPrice;
            weak.RealmExchangeListedUtc = weakListed;
            MarketCache.AddItem(weak);
            bot.Inventory.AddItem((eInventorySlot)bagSlot, item);
        }
    }

    public static bool CanReconcileListingItem(DbInventoryItem item, DbInventoryItem stored, string ownerId) =>
        item != null && stored != null && !string.IsNullOrWhiteSpace(item.ObjectId) &&
        item.ObjectId == stored.ObjectId && stored.OwnerID == ownerId && stored.OwnerLot == 0 &&
        stored.SlotPosition >= (int)eInventorySlot.FirstBackpack && stored.SlotPosition <= (int)eInventorySlot.LastBackpack &&
        item.ITemplate_Id == stored.ITemplate_Id && item.UTemplate_Id == stored.UTemplate_Id && item.Count == stored.Count;

    /// <summary>A free consignment slot for an owner holding fewer than <paramref name="cap"/> listings (default: the base
    /// cap of 3), or -1. O(1) from the MarketBook's owner bitmap.</summary>
    public static int FindFreeListingSlot(string ownerId, int cap = MaxBotRealmExchangeListings) =>
        MarketCache.ReadBook(book => book.OwnerCount(ownerId) >= cap ? -1 : book.FreeSlot(ownerId));

    /// <summary>A free consignment slot when this bot has room for this item (its level's cap, plus a crafter's
    /// crafted-only slots for its own products), or -1.</summary>
    public static int FindFreeListingSlot(GameBot bot, DbInventoryItem item)
    {
        if (bot == null)
            return -1;
        string ownerId = GetOwnerId(bot.DatabaseID);
        MarketBook.Listing[] own = MarketCache.ReadBook(book => book.OwnerListings(ownerId));
        if (!RoomOf(bot, own).For(IsOwnCrafted(bot, item)))
            return -1;
        return MarketCache.ReadBook(book => book.FreeSlot(ownerId));
    }

    public static bool TryBuy(GameBot bot, DbInventoryItem item) => TryBuy(bot, item, out _);

    /// <summary>Buys a listing for a bot. Never waits for the exchange locks (<paramref name="busy"/>; the bot tries again
    /// at its next check). The sold item's relist and listing-hours rows are deleted in the sale transaction.</summary>
    public static bool TryBuy(GameBot bot, DbInventoryItem item, out bool busy)
    {
        busy = false;
        if (!RealmExchangeLocks.TryEnter(0, true, out RealmExchangeLocks.Scope scope))
        {
            busy = true;
            return false;
        }
        using (scope)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.PersistentRecord == null ||
                bot.DatabaseID <= 0 || !RealmExchangeBroker.IsAvailableListing(item, bot.Realm) ||
                item.OwnerID == GetOwnerId(bot.DatabaseID))
                return false;

            eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid || bot.PersistentRecord.MoneyCopper < item.SellPrice)
                return false;

            int price = item.SellPrice;
            string sellerId = item.OwnerID;
            int sellerSlot = item.SlotPosition;
            var payment = RealmExchangeBroker.PrepareSellerPayment(sellerId, price, bot.Realm);
            if (payment == null) return false;
            long originalMoney = bot.PersistentRecord.MoneyCopper;
            bot.PersistentRecord.MoneyCopper -= price;
            bot.PersistentRecord.Dirty = true;
            MarketCache.RemoveItem(item);
            item.OwnerLot = 0;
            item.SellPrice = 0;

            if (!bot.Inventory.AddItem(slot, item))
            {
                item.OwnerID = sellerId;
                item.SlotPosition = sellerSlot;
                item.OwnerLot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
                item.SellPrice = price;
                MarketCache.AddItem(item);
                bot.PersistentRecord.MoneyCopper = originalMoney;
                return false;
            }

            item.OwnerID = GetOwnerId(bot.DatabaseID);
            payment.Apply();
            OfflineTrue165RelistRecord relist = Exchange165.TakeForSale(item);
            OfflineTrue165ListingHours hours = RealmExchangeExpiry.TakeHoursForSale(item.ObjectId, sellerId);
            var sale = new RealmExchangeSaleLedger.Sale((int)bot.Realm, item.Name, item.Count, price,
                sellerId, payment.SellerName, GetOwnerId(bot.DatabaseID), bot.Name, DateTime.UtcNow);
            if (!RealmExchangeSaleLedger.CommitSale(GameServer.Database, sale, [item, bot.PersistentRecord, payment.Row],
                    SaleDeletes(relist, hours)))
            {
                payment.Restore();
                Exchange165.RestoreAfterFailedSale(relist);
                bot.Inventory.RemoveItemWithoutDbDeletion(item);
                item.OwnerID = sellerId;
                item.SlotPosition = sellerSlot;
                item.OwnerLot = RealmExchangeBroker.GetOwnerLot(bot.Realm);
                item.SellPrice = price;
                MarketCache.AddItem(item);
                bot.PersistentRecord.MoneyCopper = originalMoney;
                bot.PersistentRecord.Dirty = true;
                return false;
            }
            MarketCache.NoteSale(bot.Realm, item, price);
            RealmExchangeStats.Note(bot.Realm, RealmExchangeStats.Counter.Sold);
            RealmExchangeStats.Note(bot.Realm, RealmExchangeStats.Counter.Bought);
            MarkInventoryChanged(bot);
            return true;
        }
    }

    /// <summary>The bookkeeping rows a sale deletes in its own transaction (only rows that exist).</summary>
    internal static DataObject[] SaleDeletes(OfflineTrue165RelistRecord relist, OfflineTrue165ListingHours hours)
    {
        var rows = new List<DataObject>(2);
        if (relist?.IsPersisted == true)
            rows.Add(relist);
        if (hours != null)
            rows.Add(hours);
        return rows.ToArray();
    }

    public static bool TryEquipPurchasedUpgrade(GameBot bot, PurchaseCandidate purchase)
    {
        if (bot?.Inventory == null || purchase?.IsEquipmentUpgrade != true ||
            purchase.EquipSlot == eInventorySlot.Invalid || purchase.Item == null)
            return false;

        // Revalidate at the moment of the move.  A listing can remain in the
        // candidate cache while specs, level, or another equipment action
        // changes.  Without this guard a stale purchase could put a staff in
        // a Shadowblade/Hunter melee slot (or a non-bow in a distance slot),
        // leaving the bot looking armed but unable to deal damage.  Invalid
        // purchases stay in the real backpack and are never deleted.
        bool legal = purchase.EquipSlot switch
        {
            eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.TwoHandWeapon =>
                BotWeaponStats.CanUseMelee(bot, purchase.Item),
            eInventorySlot.DistanceWeapon => BotRangedCombat.CanUse(bot, purchase.Item),
            _ => true,
        };
        // the same slot rules as the buying check (2026-11-11 exchange plan: the scorer and the equip step agree): never a
        // slot its class keeps for another use, never a shield over a weapon it wields in the left hand
        if (!legal || KeepsSlotForOtherUse(bot, purchase.Item, purchase.EquipSlot) ||
            DisplacesOffhandWeapon(purchase.Item, bot.Inventory.GetItem(purchase.EquipSlot)))
            return false;

        eInventorySlot backpackSlot = (eInventorySlot)purchase.Item.SlotPosition;
        DbInventoryItem replaced = bot.Inventory.GetItem(purchase.EquipSlot);
        if (backpackSlot is < eInventorySlot.FirstBackpack or > eInventorySlot.LastBackpack ||
            !bot.Inventory.MoveItem(backpackSlot, purchase.EquipSlot, purchase.Item.Count))
            return false;

        bot.RefreshItemBonuses();
        if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage &&
            BotWeaponStats.IsMeleeWeapon((eObjectType)purchase.Item.Object_Type))
            bot.EnsureBotWeaponReady();
        MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        LogEquipmentUpgrade(bot, purchase.Item, replaced, purchase.EquipSlot, "realm_exchange");
        return true;
    }

    private static OfflineWorldBotRecord Find(long botId) =>
        DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(botId));

    internal static OfflineWorldBotRecord FindLiveOrStored(long botId) =>
        TryGetLiveOrLeaving(botId, out GameBot live) ? live.PersistentRecord : Find(botId);
}

[DataTable(TableName = "offline_world_bots")]
public sealed class OfflineWorldBotRecord : DataObject
{
    [PrimaryKey(AutoIncrement = true)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false)] public string Name { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false)] public int ClassId { get; set; }
    [DataElement(AllowDbNull = false)] public string ClassName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int RaceId { get; set; }
    [DataElement(AllowDbNull = false)] public string RaceName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Gender { get; set; }
    [DataElement(AllowDbNull = false)] public int Level { get; set; }
    [DataElement(AllowDbNull = false)] public long Experience { get; set; }
    [DataElement(AllowDbNull = false)] public long RealmPoints { get; set; }
    [DataElement(AllowDbNull = false)] public int ZoneId { get; set; }
    [DataElement(AllowDbNull = true)] public string ZoneName { get; set; }
    [DataElement(AllowDbNull = false)] public string Activity { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public bool IsOnline { get; set; }
    [DataElement(AllowDbNull = false)] public bool IsAlive { get; set; }
    [DataElement(AllowDbNull = false)] public string LastUpdateUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public long MoneyCopper { get; set; }
    [DataElement(AllowDbNull = false)] public int InventoryRevision { get; set; }
    [DataElement(AllowDbNull = false)] public int X { get; set; }
    [DataElement(AllowDbNull = false)] public int Y { get; set; }
    [DataElement(AllowDbNull = false)] public int Z { get; set; }
    [DataElement(AllowDbNull = false)] public int RegionId { get; set; }
    [DataElement(AllowDbNull = false)] public int Health { get; set; }
    [DataElement(AllowDbNull = false)] public int Mana { get; set; }
    [DataElement(AllowDbNull = false)] public int Endurance { get; set; }
    [DataElement(AllowDbNull = false)] public int BindRegionId { get; set; }
    [DataElement(AllowDbNull = false)] public int BindX { get; set; }
    [DataElement(AllowDbNull = false)] public int BindY { get; set; }
    [DataElement(AllowDbNull = false)] public int BindZ { get; set; }
    [DataElement(AllowDbNull = false)] public string SerializedSpecs { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string SerializedBuildPlan { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int UnspentSpecPoints { get; set; }
    [DataElement(AllowDbNull = false)] public string SerializedAbilities { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string SerializedCraftingSkills { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string CurrentCampId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ItineraryJson { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int DeathCount { get; set; }
    [DataElement(AllowDbNull = false)] public bool IsRetired { get; set; }
    [DataElement(AllowDbNull = false)] public string LastSavedUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int PersistedStateVersion { get; set; } = 1;
    [DataElement(AllowDbNull = false)] public string CurrentGoal { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TargetName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TravelDestination { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectiveProgress { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastMeaningfulProgressUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int RecoveryCount { get; set; }
    // Durable autonomous population allocation.  These are deliberately separate
    // from the human-readable Activity/CurrentGoal fields so restart/reload does
    // not turn a presentation string into control state.
    [DataElement(AllowDbNull = false)] public string ObjectiveKind { get; set; } = "SoloPve";
    [DataElement(AllowDbNull = false)] public string ObjectiveAssignmentId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectiveAssignedUtc { get; set; } = string.Empty;
    // The object database schema reconciler adds these columns to existing
    // offline_world_bots tables during startup, just as it does every
    // DataElement on this durable record.
    [DataElement(AllowDbNull = false)] public string ObjectiveExpiresUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectiveRvrEligibleUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectivePhase { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ObjectivePveMode { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int ObjectivePveKillTarget { get; set; }
    [DataElement(AllowDbNull = false)] public int ObjectivePveKills { get; set; }
    // True 1.65 Mode: the advanced class a bot that started as a base class will train into at level 5 (0 = none;
    // BaseClassBots165).
    [DataElement(AllowDbNull = false)] public int PlannedClassId { get; set; }
    // True 1.65 Mode crafter bots (CrafterBots165): the primary craft (eCraftingSkill; 0 = not given yet, -1 = none)
    // and its skill.
    [DataElement(AllowDbNull = false)] public int CraftSkill { get; set; }
    [DataElement(AllowDbNull = false)] public int CraftLevel { get; set; }
}
