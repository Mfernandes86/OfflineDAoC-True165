using System.Linq;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>Portal keeps are protected travel hubs, never siege objectives.
    /// Relic keeps remain valid raid objectives even though the relic, rather
    /// than the keep itself, is the capturable objective.</summary>
    public static class AutonomousRvrKeepPolicy
    {
        public static bool IsSiegeObjective(AbstractGameKeep keep)
        {
            // Battleground central keeps start neutral (realm none) and are taken from that state.
            return keep != null && !keep.IsPortalKeep &&
                   (keep.Realm != eRealm.None || BattlegroundBrackets.IsBattlegroundRegion(keep.Region));
        }

        /// <summary>
        /// Whether an attack on a keep in this region may open a frontier defense or a forced siege event (battlegrounds
        /// fix 2026-11-12, owner: "this is wrong"). Those events recruit level-50 RvR warbands, which can never enter a
        /// battleground (BattlegroundBrackets.Allows): a player hitting a bot-held battleground keep tied up to 240 of them
        /// for four hours and sent realm-wide rally calls whatever the launcher's Battlegrounds % said. A battleground keep
        /// is held and retaken only by its own bracket's battleground bots (ExecuteBattleground), whose share the launcher
        /// sets (0 = none).
        /// </summary>
        public static bool RaisesRealmEvent(ushort keepRegion) => !BattlegroundBrackets.IsBattlegroundRegion(keepRegion);

        /// <summary>
        /// A keep a bot siege can win (keeps audit 2026-11-11, B1 safety net): a frontier keep changes hands only when its
        /// lord dies (Lord.cs, NormalServerRules), so a keep whose lord is missing from the world data cannot be taken and
        /// a siege on it would grind for four hours for nothing. 13 of the 21 keeps lost their lord rows on 2026-10-30
        /// (the content lane restores them). A relic keep has no lord: its relic is the prize.
        /// </summary>
        public static bool IsCapturable(bool relicKeep, bool hasLord) => relicKeep || hasLord;

        public static bool IsCapturable(AbstractGameKeep keep)
        {
            if (keep == null) return false;
            bool capturable = IsCapturable(keep.IsRelic, keep.Guards.Values.Any(guard => guard is GuardLord));
            if (!capturable && ReportedLordless.TryAdd(keep.KeepID, 0))
            {
                var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrKeepPolicy));
                if (log.IsWarnEnabled) log.Warn($"RVR_KEEP_NOT_CAPTURABLE keep={keep.KeepID} name=\"{keep.Name}\" reason=\"no lord in the world data; bots will not siege it\"");
            }
            return capturable;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> ReportedLordless = new();

        /// <summary>The hastener at a keep's gate (1.52b) is a peaceful helper, not a defender: it adds nothing to a
        /// keep's guard strength and no defending bot takes its spot outside the gate.</summary>
        public static bool IsDefendingGuard(GameKeepGuard guard) => guard != null && IsDefendingGuardType(guard.GetType());

        public static bool IsDefendingGuardType(System.Type type) => !typeof(FrontierHastener).IsAssignableFrom(type);
    }
}
