using System;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.Keeps;

namespace DOL.GS
{
    public enum BotSiegeKind { Ram, Catapult, Trebuchet, Ballista }

    /// <summary>Bot-only native siege adapter. Real equipment purchases; ammunition-free artillery by design.</summary>
    public static class BotSiegeRuntime
    {
        public const string RepairKit = "offline_siege_field_repair_kit";
        // Inventory packets encode generic stack counts as one unsigned byte.
        public const int RepairStackLimit = byte.MaxValue;
        public const int RepairRestockTarget = 5;
        public static bool IsSupply(string id) => id == RepairKit ||
            id is "deploy_siege_ram" or "deploy_siege_ram2" or "deploy_siege_ram3" or
                "deploy_siege_catapult" or "deploy_siege_catapult2" or "deploy_siege_catapult3" or
                "deploy_siege_trebuchet" or "deploy_siege_trebuchet2" or "deploy_siege_trebuchet3" or
                "deploy_siege_ballista" or "deploy_siege_ballista2" or "deploy_siege_ballista3";
        public static string Kit(eRealm realm, BotSiegeKind kind) => "deploy_siege_" + kind.ToString().ToLowerInvariant() +
            (realm == eRealm.Midgard ? "2" : realm == eRealm.Hibernia ? "3" : "");
        public static BotSiegeKind Kind(GameSiegeWeapon w) => w is GameSiegeRam ? BotSiegeKind.Ram :
            w is GameSiegeTrebuchet ? BotSiegeKind.Trebuchet : w is GameSiegeBallista ? BotSiegeKind.Ballista : BotSiegeKind.Catapult;

        [GameServerStartedEvent]
        public static void SupplyCatalog(DOLEvent e, object sender, EventArgs args)
        {
            // Explicitly requested bot repair alternative; no ammo or crafting-skill grants.
            DbItemTemplate repair = GameServer.Database.FindObjectByKey<DbItemTemplate>(RepairKit);
            if (repair == null)
            {
                repair = new DbItemTemplate { Id_nb=RepairKit,Name="field siege repair kit",Level=1,Model=520,
                    Price=50_000,PackSize=1,MaxCount=RepairStackLimit,Weight=10,IsPickable=true,IsDropable=true,IsTradable=true };
                GameServer.Database.AddObject(repair);
            }
            else if (repair.MaxCount != RepairStackLimit)
            { repair.MaxCount=RepairStackLimit; GameServer.Database.SaveObject(repair); }
            // True 1.65 Mode: the bot-only kit is not put on the siege merchants players buy from (the battleground Siege
            // Masters, battlegrounds fix 2026-11-12); repairs follow the player rule there (HasRepairMeans).
            if (!Classic165Mode.IsOn)
                AddSupplyToMerchants(repair);
        }

        private static void AddSupplyToMerchants(DbItemTemplate template)
        {
            foreach (GameMerchant merchant in WorldMgr.GetAllRegions().Where(r => r != null).SelectMany(r => r.Merchants))
                if (merchant.TradeItems != null && new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
                    .Any(realm => AutonomousSiegePolicy.IsSiegeMerchant(realm, merchant.TradeItems.ItemsListID)) &&
                    !merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().Any(t => t.Id_nb == template.Id_nb))
                    merchant.TradeItems.AddTradeItem(0, eMerchantWindowSlot.FirstEmptyInPage, template);
        }

        public static DbInventoryItem Item(GameBot bot, string id) => id == null ? null : bot?.Inventory?.AllItems.FirstOrDefault(i =>
            i.Count > 0 && i.Id_nb == id && i.SlotPosition >= (int)eInventorySlot.FirstBackpack && i.SlotPosition <= (int)eInventorySlot.LastBackpack);

        public static int RepairRestockAmount(int held, long money, long unitPrice) => unitPrice<=0 ? 0 :
            (int)Math.Min(Math.Max(0,RepairRestockTarget-held),Math.Max(0,money)/unitPrice);

        // Opportunistic only: this never assigns a destination or interrupts a fight.
        public static int TryRestockRepairKits(GameBot bot, GameMerchant merchant)
        {
            if (bot?.IsAutonomousWorldBot!=true || bot.Inventory==null || bot.InCombat || bot.IsAttacking ||
                merchant?.TradeItems==null || merchant.CurrentRegion!=bot.CurrentRegion ||
                !merchant.IsWithinRadius(bot,GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE) ||
                merchant.Realm!=eRealm.None && merchant.Realm!=bot.Realm) return 0;
            DbItemTemplate template=merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().FirstOrDefault(t=>t.Id_nb==RepairKit && t.Price>0);
            if (template==null) return 0;
            lock (bot.Inventory)
            {
                var stacks=bot.Inventory.AllItems.Where(i=>i.Id_nb==RepairKit && i.Count>0 &&
                    i.SlotPosition >= (int)eInventorySlot.FirstBackpack && i.SlotPosition <= (int)eInventorySlot.LastBackpack).ToArray();
                int count=RepairRestockAmount(stacks.Sum(i=>i.Count),AutonomousBotEconomy.GetMoney(bot.DatabaseID),template.Price);
                if (count==0) return 0;
                DbInventoryItem stack=stacks.FirstOrDefault(i=>i.MaxCount-i.Count>=count);
                var slot=bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack,eInventorySlot.LastBackpack);
                if (stack==null && slot==eInventorySlot.Invalid) return 0;
                DbInventoryItem item=stack==null ? GameInventoryItem.Create(template) : null;
                if (stack==null && item==null) return 0;
                if (item!=null) item.Count=count;
                long cost=checked(template.Price*count);
                if (!AutonomousBotEconomy.TrySpend(bot.DatabaseID,cost, BotMoneyLedger165.Kind.Siege)) return 0;
                bool added=stack!=null ? bot.Inventory.AddCountToStack(stack,count) : bot.Inventory.AddItem(slot,item);
                if (!added) { AutonomousBotEconomy.AddMoney(bot.DatabaseID,cost, BotMoneyLedger165.Kind.Siege); return 0; }
                AutonomousBotEconomy.MarkInventoryChanged(bot); bot.MarkAutonomousStateDirty(); AutonomousBotStatusPersistence.Queue(bot,true);
                return count;
            }
        }

        public static bool Consume(GameBot bot, string id, int count)
        {
            if (bot?.Inventory == null || count <= 0) return false;
            lock (bot.Inventory)
            {
                DbInventoryItem item = Item(bot, id);
                if (item == null || item.Count < count) return false;
                if (!bot.Inventory.RemoveCountFromStack(item, count)) return false;
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
                return true;
            }
        }

        public static bool LegalEnemy(GameLiving owner, GameLiving target) => owner?.IsAlive == true && target?.IsAlive == true &&
            target.ObjectState == GameObject.eObjectState.Active && owner.CurrentRegion == target.CurrentRegion &&
            (target.Realm != eRealm.None || NeutralBattlegroundKeep(target)) && target.Realm != owner.Realm &&
            GameServer.ServerRules.IsAllowedToAttack(owner, target, true);

        // Battleground central keeps start unclaimed (realm none); their doors and guards are still fair siege targets.
        private static bool NeutralBattlegroundKeep(GameLiving target) =>
            BattlegroundBrackets.IsBattlegroundRegion(target.CurrentRegionID) && target is Keeps.GameKeepDoor or Keeps.GameKeepGuard;

        public static bool CanDamage(GameSiegeWeapon weapon, GameLiving target)
        {
            if (weapon?.Owner is not GameBot bot || !weapon.IsAlive || weapon.ObjectState != GameObject.eObjectState.Active ||
                weapon.IsMoving || !weapon.CanUse() || !LegalEnemy(bot, target) || BotPvpCrowdControl.Protected(bot, target)) return false;
            int range = weapon.GetDistanceTo(target);
            if (weapon is GameSiegeRam)
                return target is GameKeepDoor { State: eDoorState.Closed } or GameRealmGate { State: eDoorState.Closed } && range <= weapon.attackComponent.AttackRange;
            if (range < weapon.MinAttackRange || range > weapon.MaxAttackRange) return false;
            // Catapults and trebuchets lob onto a ground target in an arc, as players used them: no line of sight.
            // A ballista fires directly: it needs a clear shot, or a target standing up on a wall.
            if (weapon is GameSiegeCatapult) return true;
            return target.Z - weapon.Z > 150 || Visible(weapon, target);
        }

        public static bool Visible(GameLiving source, GameLiving target)
        {
            var nav = PathfindingProvider.Instance;
            return source?.CurrentZone != null && target != null && nav.IsAvailable && nav.HasNavmesh(source.CurrentZone) &&
                nav.HasLineOfSight(source.CurrentZone, new(source.X, source.Y, source.Z + 48),
                    new(target.X, target.Y, target.Z + 48), nav.DefaultFilters);
        }

        public static bool CanRepair(GameBot bot, GameSiegeWeapon w) => bot?.IsAlive == true && w?.IsAlive == true &&
            w.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegion == w.CurrentRegion && bot.Realm == w.Realm &&
            !bot.InCombat && !w.InCombat && !w.IsMoving && !bot.IsMoving && !bot.IsCasting && !bot.IsIncapacitated && w.TimesRepaired <= 3 &&
            bot.IsWithinRadius(w, WorldMgr.INTERACT_DISTANCE) && HasRepairMeans(bot, w);

        /// <summary>
        /// Keeps audit 2026-11-11 (L5): True 1.65 Mode repairs a siege engine by the player rule (GameSiegeWeapon.TryRepair,
        /// RepairCommandHandler): woodworking 301 or more and the wood for one repair (the engine level's /repair table),
        /// paid at the board price on success. Any crafter of the engine's realm standing at it may repair it, as any player
        /// could. The bot-only "field siege repair kit" is not sold in True 1.65 Mode (SupplyCatalog), so no bot could
        /// ever keep a ram alive with it; it stays the rule outside True 1.65 Mode (the operator only).
        /// </summary>
        public static bool HasRepairMeans(GameBot bot, GameSiegeWeapon w) => Classic165Mode.IsOn
            ? MayRepairSiege(ItemWear165.RepairSkill(bot, eCraftingSkill.WoodWorking), w.TimesRepaired,
                AutonomousBotEconomy.GetMoney(bot.DatabaseID), SiegeRepairCost(w.Level))
            : w.Owner == bot && Item(bot, RepairKit) != null;

        public const int SiegeRepairWoodworking = 301;

        /// <summary>Pure rule: a player may repair a siege engine with woodworking 301+, the wood for one repair, and an
        /// engine repaired at most four times.</summary>
        public static bool MayRepairSiege(int woodworkingSkill, int timesRepaired, long money, long woodCost) =>
            woodworkingSkill >= SiegeRepairWoodworking && timesRepaired <= 3 && money >= woodCost;

        /// <summary>The coin for one siege repair: the engine level's wood units at the board price.</summary>
        public static long SiegeRepairCost(int engineLevel) => KeepDoorRepair165.RepairCost(engineLevel, KeepDoorRepair165.WoodUnitCopper());

        /// <summary>
        /// One finished 20-second repair: in True 1.65 Mode the player's roll (woodworking / ((level + 1) x 50)), 15% of the
        /// engine on success and the wood paid only then (a player loses no wood on a failure); otherwise one kit.
        /// </summary>
        public static bool RepairOnce(GameBot bot, GameSiegeWeapon w)
        {
            if (!CanRepair(bot, w)) return false;
            if (!Classic165Mode.IsOn)
                return w.Repair(w.MaxHealth * 15 / 100, () => Consume(bot, RepairKit, 1));
            if (!Util.Chance(DOL.GS.Commands.RepairCommandHandler.RepairChance(ItemWear165.RepairSkill(bot, eCraftingSkill.WoodWorking), w.Level)))
                return false;
            long cost = SiegeRepairCost(w.Level);
            return w.Repair(w.MaxHealth * 15 / 100, () => AutonomousBotEconomy.TrySpend(bot.DatabaseID, cost, BotMoneyLedger165.Kind.Siege));
        }

        /// <summary>An engine's operator at its engine, or a ram crew member at its ram (AutonomousRamCrews).</summary>
        public static bool HoldingPosition(GameBot bot) => AutonomousSiegeOwnership.All(bot).Any(w =>
            bot.IsWithinRadius(w, w.SIEGE_WEAPON_CONTROLE_DISTANCE)) || AutonomousRamCrews.AtSeat(bot);

        /// <summary>An enemy at melee reach is striking this bot (AutonomousWorldBotController.IsSiegeOperatorThreat): the
        /// one thing that takes an operator or a ram crew member off its post.</summary>
        public static bool StruckInMeleeReach(GameBot bot)
        {
            if (bot == null) return false;
            foreach (GameNPC npc in bot.GetNPCsInRadius(350))
                if (npc.IsAlive && npc.TargetObject == bot && npc.IsAttacking && npc.Realm != bot.Realm &&
                    AutonomousWorldBotController.IsSiegeOperatorThreat(System.Numerics.Vector2.Distance(new(bot.X, bot.Y), new(npc.X, npc.Y)), npc.Z - bot.Z))
                    return true;
            foreach (GamePlayer player in bot.GetPlayersInRadius(350))
                if (player.IsAlive && player.TargetObject == bot && player.IsAttacking && player.Realm != bot.Realm &&
                    AutonomousWorldBotController.IsSiegeOperatorThreat(System.Numerics.Vector2.Distance(new(bot.X, bot.Y), new(player.X, player.Y)), player.Z - bot.Z))
                    return true;
            return false;
        }
        public static bool Assigned(GameBot bot) => bot?.TempProperties.GetProperty<long>("SiegeJobUntil",0) > GameLoop.GameLoopTime;
    }
}
