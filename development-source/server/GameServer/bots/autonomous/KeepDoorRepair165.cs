using System;
using System.Linq;
using DOL.Database;
using DOL.GS.Commands;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>
    /// Holders repair their keep's doors (veteran-bots review 2026-11-11, M10 / rvr-social G8). No gamebot ever repaired a
    /// door: a door broken in a repelled siege or in the fight for a retake healed only by the passive 5% per 30 minutes.
    /// A 2003 keep's holders loaded up on wood and had two people repair the doors, then held it for the retake. (A keep
    /// that changes hands already gets its doors back whole from the server, AbstractGameKeep.Reset.)
    /// Every 20 seconds - a player's repair time - each damaged door of a held keep that is not under attack may be
    /// repaired by up to two gamebot crafters of the holding realm standing near it, out of combat and still, by the
    /// player rules unchanged (RepairCommandHandler): woodworking skill 1 or more (a weaponcrafter, fletcher or
    /// siegecrafter - the crafts that carry woodworking), the player's success roll skill / ((door level + 1) * 50), 5% of
    /// the door a successful repair, the door level's wood for one repair (the /repair table) paid at the merchant price
    /// of rowan boards only on success (a player loses no wood on a failed repair). At most 8 doors a pulse.
    /// Keeps audit 2026-11-11 (L3): no repair while an enemy stands within 3,000 of the door, the test the door's own
    /// passive repair already makes. "Not under attack" alone meant five quiet minutes after the last hit on a door or the
    /// lord: with both gates down and the raid fighting guards inside, two woodworkers could bring a gate back over the
    /// 15% line, it shut behind the raid and the lord was shielded again. A player could not stand and hammer a gate for
    /// 20 seconds with the enemy army in the gateway either.
    /// Cost: a walk over the keeps every 20 s and two radius queries only at a damaged door.
    /// </summary>
    public static class KeepDoorRepair165
    {
        public const long PulseMilliseconds = 20_000;
        public const int RepairersPerDoor = 2;
        public const int MaxDoorsPerPulse = 8;
        public const int RepairerRadius = 600;
        public const double RepairShare = 0.05;

        private static long _next;
        private static long? _woodUnitCopper;

        /// <summary>The coin for one repair of a door of this level: its wood units at this price per unit.</summary>
        public static long RepairCost(int doorLevel, long woodUnitCopper) =>
            (long)RepairCommandHandler.GetNeededWoodForOneTick(doorLevel) * Math.Max(1, woodUnitCopper);

        /// <summary>A player needs woodworking skill 1 or more to repair (RepairCommandHandler.PreFireChecks).</summary>
        public static bool CanRepair(int woodworkingSkill) => woodworkingSkill >= 1;

        /// <summary>Whether a door is worth a repair now: below full, part of a held keep that is not under attack and has
        /// no enemy at the door.</summary>
        public static bool NeedsRepair(int healthPercent, eRealm keepRealm, bool keepInCombat, bool portalKeep, bool enemiesNear = false) =>
            healthPercent < 100 && keepRealm != eRealm.None && !keepInCombat && !portalKeep && !enemiesNear;

        private static long WoodUnitCopper()
        {
            if (_woodUnitCopper is { } known)
                return known;
            long price = 3;
            try
            {
                DbItemTemplate rowan = GameServer.Database.FindObjectByKey<DbItemTemplate>("rowan_wooden_boards");
                if (rowan != null && rowan.Price > 0)
                    price = Math.Max(1, rowan.Price / Math.Max(1, rowan.PackSize));
            }
            catch (Exception) { }
            _woodUnitCopper = price;
            return price;
        }

        /// <summary>AutonomousBotGroupCoordinator's service phase: at most once every 20 seconds.</summary>
        public static void Pulse(long now)
        {
            if (now < _next || GameServer.KeepManager == null)
                return;
            _next = now + PulseMilliseconds;
            try
            {
                int doors = 0;
                foreach (AbstractGameKeep keep in GameServer.KeepManager.GetAllKeeps())
                {
                    if (keep == null || keep.Doors.Count == 0 || !NeedsRepair(0, keep.Realm, keep.InCombat, keep.IsPortalKeep))
                        continue;
                    foreach (GameKeepDoor door in keep.Doors.Values)
                    {
                        if (door == null || door.ObjectState != GameObject.eObjectState.Active || door.HealthPercent >= 100 ||
                            !NeedsRepair(door.HealthPercent, keep.Realm, keep.InCombat, keep.IsPortalKeep, door.EnemiesNear()))
                            continue;
                        RepairDoor(keep, door);
                        if (++doors >= MaxDoorsPerPulse)
                            return;
                    }
                }
            }
            catch (Exception)
            {
                // a repair never breaks the service phase
            }
        }

        private static void RepairDoor(AbstractGameKeep keep, GameKeepDoor door)
        {
            long cost = RepairCost(door.Level, WoodUnitCopper());
            int repaired = 0;
            foreach (GameBot bot in door.GetNPCsInRadius(RepairerRadius).OfType<GameBot>()
                         .Where(b => b.Realm == keep.Realm && b.IsAlive && b.IsAutonomousWorldBot && !b.IsTemporaryGroupHelper &&
                                     !HiredCompanions165.IsHired(b) && !b.InCombat && !b.IsMoving && !b.IsCasting &&
                                     CanRepair(ItemWear165.RepairSkill(b, eCraftingSkill.WoodWorking))))
            {
                if (door.HealthPercent >= 100 || repaired >= RepairersPerDoor)
                    break;
                // the repair takes the crafter's time whether or not it works, as a player's 20-second /repair does
                repaired++;
                if (!Util.Chance(RepairCommandHandler.RepairChance(ItemWear165.RepairSkill(bot, eCraftingSkill.WoodWorking), door.Level)))
                {
                    FeatureCounters165.Add("bot_door_repair_failed");
                    continue;
                }
                if (!AutonomousBotEconomy.TrySpend(bot.DatabaseID, cost, BotMoneyLedger165.Kind.Siege))
                    continue;
                door.Repair((int)(door.MaxHealth * RepairShare));
                FeatureCounters165.Add("bot_door_repair");
            }
        }
    }
}
