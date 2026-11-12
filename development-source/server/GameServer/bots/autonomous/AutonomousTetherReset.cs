using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Tethered bosses (Summoner Roesia and about 50 other scripted encounters) take no damage while they are
    /// outside their tether range. Players stop hitting such a boss and the one it is chasing runs back toward
    /// its lair so it follows them home, then the fight resumes. Gamebots kept fighting out there: Summoner
    /// Roesia chased a raider 3,200 units from her spawn and stayed immune at 28% until the raid skipped her
    /// (run 16, 2026-10-07). This does the player thing; the boss and its mechanics are unchanged, except that a boss
    /// left stranded outside its tether with nobody fighting it is leashed home (<see cref="LeashIfStranded"/>).
    /// </summary>
    public static class AutonomousTetherReset
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousTetherReset));
        private static long _nextLog;

        /// <summary>
        /// Run 2026-10-13: Aidon the Archwizard (Hall of the Corrupt, on the way to Summoner's Hall) stood 4,126 units
        /// from his spawn, outside his 1,500 tether, for over an hour. Out there he is immune and only goes home when
        /// nothing is within his aggro range, but the Albion raid's 150 parties stood around him (each bot correctly
        /// stopped attacking), so he kept his aggro list and the raid never got past him. An epic encounter that has
        /// been out of its tether for 30 seconds while nobody is fighting it in reach is leashed: its aggro list is
        /// cleared and it walks home (its own script restores its health there, as on any reset).
        /// </summary>
        public const long StrandedLeashMilliseconds = 30_000;
        // Out of its tether this long, it goes home even while fighting (run 6: Princess Nahemah fought raiders at the
        // edge of her tether for over twenty minutes, immune, while the raid tried to draw her home).
        public const long OutOfTetherLeashMilliseconds = 90_000;
        private static readonly TetherExcursionClock<GameNPC> Excursions = new();

        private static void LeashIfStranded(GameNPC boss)
        {
            long now = GameLoop.GameLoopTime;
            if (boss.IsReturningToSpawnPoint)
            {
                Excursions.Forget(boss);
                return;
            }
            bool engaged = boss.TargetObject is GameLiving foe && foe.IsAlive && boss.IsWithinRadius(foe, boss.attackComponent.AttackRange + 100);
            if (!Excursions.DueForLeash(boss, now, engaged))
                return;
            (boss.Brain as StandardMobBrain)?.ClearAggroList();
            boss.StopAttack();
            boss.TargetObject = null;
            boss.ReturnToSpawnPoint(boss.MaxSpeed);
            Log.Info($"AUTONOMOUS_TETHER_LEASH boss=\"{boss.Name}\" region={boss.CurrentRegionID} fromSpawn={(int)boss.GetDistanceTo(boss.SpawnPoint)} " +
                     $"tether={boss.TetherRange} reason=\"out of its tether {StrandedLeashMilliseconds / 1000} s with nobody fighting it in reach\"");
        }

        // Epic encounters and scripted named monsters: ordinary monsters also carry a 4,500 tether but simply walk home
        // when they leave it (run 18 led a small hill cat home). Run of 2026-10-29: Lieutenant Meade (Hall of the Corrupt,
        // on Albion's way to Summoner's Hall) is a scripted GameNPC, not an epic NPC, immune out of his tether; the Albion
        // raid fought him there until its four hours ran out.
        public static bool IsOutOfTether(GameNPC npc) =>
            IsTetheredEncounter(npc) && npc is { IsAlive: true, TetherRange: > 0 } && npc.Brain is not IControlledBrain && npc.IsOutOfTetherRange;

        /// <summary>An epic NPC, or a scripted named monster (its own class, a capitalised name).</summary>
        public static bool IsTetheredEncounter(GameNPC npc) =>
            npc is IGameEpicNpc || npc != null && npc.GetType() != typeof(GameNPC) && npc is not GameBot &&
            !string.IsNullOrEmpty(npc.Name) && char.IsUpper(npc.Name[0]);

        /// <summary>
        /// For an autonomous gamebot whose target is a boss outside its tether: stop attacking it, and if the
        /// boss is after this bot, lead it back toward its spawn point. True when the bot's turn is used.
        /// </summary>
        public static bool TryHandle(GameBot bot, GameNPC boss)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup || !IsTetheredEncounter(boss)) return false;
            if (!IsOutOfTether(boss))
            {
                // Back inside its tether: the next trip out starts its clocks afresh. They used to be cleared only by a
                // leash or a walk home, so a boss that stepped out again 90-180 s after its first trip was leashed at
                // once (dragon-fight T1).
                Excursions.BackInside(boss);
                return false;
            }
            if (bot.IsCasting && bot.castingComponent?.SpellHandler?.Spell?.IsHarmful == true) bot.StopCurrentSpellcast();
            bot.StopAttack();
            (bot.Brain as BotBrain)?.RemoveFromAggroList(boss);
            bot.TargetObject = null;
            // The pet backs off too, or it keeps the boss busy out there.
            if (bot.ControlledBrain?.Body?.TargetObject == boss) bot.ControlledBrain.Disengage();
            bool chased = boss.TargetObject == bot;
            LeashIfStranded(boss);
            if (chased)
            {
                Point3D home = boss.SpawnPoint;
                bot.PathTo(new Point3D(home.X, home.Y, home.Z), bot.MaxSpeed);
            }
            if (Log.IsInfoEnabled && GameLoop.GameLoopTime >= _nextLog)
            {
                _nextLog = GameLoop.GameLoopTime + 10_000;
                Log.Info($"AUTONOMOUS_TETHER_RESET boss=\"{boss.Name}\" region={boss.CurrentRegionID} bot={bot.Name} " +
                         $"leading={chased} fromSpawn={(int)boss.GetDistanceTo(boss.SpawnPoint)} tether={boss.TetherRange}");
            }
            return true;
        }
    }

    /// <summary>
    /// The two clocks of a boss outside its tether (<see cref="AutonomousTetherReset"/>): how long it has been out, and how
    /// long it has been out with nobody fighting it in reach. Pure bookkeeping keyed by the boss, so the rule can be tested.
    /// </summary>
    public sealed class TetherExcursionClock<TKey> where TKey : class
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<TKey, long> _outSince = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<TKey, long> _strandedSince = new();

        public bool IsTracking(TKey boss) => _outSince.ContainsKey(boss) || _strandedSince.ContainsKey(boss);

        /// <summary>The boss is inside its tether again. Lock-free reads first: this runs on bot thinks.</summary>
        public void BackInside(TKey boss)
        {
            if (_outSince.ContainsKey(boss))
                _outSince.TryRemove(boss, out _);
            if (_strandedSince.ContainsKey(boss))
                _strandedSince.TryRemove(boss, out _);
        }

        public void Forget(TKey boss)
        {
            _outSince.TryRemove(boss, out _);
            _strandedSince.TryRemove(boss, out _);
        }

        /// <summary>
        /// Called while the boss is outside its tether. True when it is to be leashed now: out for
        /// <see cref="AutonomousTetherReset.OutOfTetherLeashMilliseconds"/> whatever it is doing, or out with nobody
        /// fighting it in reach for <see cref="AutonomousTetherReset.StrandedLeashMilliseconds"/>. A leash forgets both clocks.
        /// </summary>
        public bool DueForLeash(TKey boss, long now, bool engaged)
        {
            long outSince = _outSince.GetOrAdd(boss, now);
            if (now - outSince > 2 * AutonomousTetherReset.OutOfTetherLeashMilliseconds)
                _outSince[boss] = outSince = now; // a stale entry from an earlier trip nobody saw end
            bool longOut = now - outSince >= AutonomousTetherReset.OutOfTetherLeashMilliseconds;
            if (engaged && !longOut)
            {
                _strandedSince.TryRemove(boss, out _);
                return false;
            }
            if (!longOut)
            {
                long since = _strandedSince.GetOrAdd(boss, now);
                if (now - since < AutonomousTetherReset.StrandedLeashMilliseconds)
                    return false;
            }
            Forget(boss);
            return true;
        }
    }
}
