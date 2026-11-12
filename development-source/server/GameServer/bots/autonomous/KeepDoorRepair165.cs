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
    /// player rules unchanged: a crafter (woodworking skill), 5% of the door a repair, the door level's wood for one repair
    /// (the /repair table) paid at the merchant price of rowan boards. At most 8 doors a pulse.
    /// Cost: a walk over the keeps every 20 s and a radius query only at a damaged door.
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

        /// <summary>Whether a door is worth a repair now: below full, part of a held keep that is not under attack.</summary>
        public static bool NeedsRepair(int healthPercent, eRealm keepRealm, bool keepInCombat, bool portalKeep) =>
            healthPercent < 100 && keepRealm != eRealm.None && !keepInCombat && !portalKeep;

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
                        if (door == null || door.ObjectState != GameObject.eObjectState.Active || door.HealthPercent >= 100)
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
                                     CrafterBots165.CraftOf(b.PersistentRecord) != eCraftingSkill.NoCrafting))
            {
                if (door.HealthPercent >= 100 || repaired >= RepairersPerDoor)
                    break;
                if (!AutonomousBotEconomy.TrySpend(bot.DatabaseID, cost, BotMoneyLedger165.Kind.Siege))
                    continue;
                door.Repair((int)(door.MaxHealth * RepairShare));
                repaired++;
                FeatureCounters165.Add("bot_door_repair");
            }
        }
    }
}
