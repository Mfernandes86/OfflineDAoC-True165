namespace DOL.GS
{
    /// <summary>
    /// Explicit offline encounter adaptations. Bot raids cannot recover safely from
    /// scripted jail/air teleports or satisfy player-only split-position puzzles.
    /// Normal attacks, adds, health, resistances, and death/respawn chains remain active.
    /// </summary>
    public static class EpicEncounterMechanics
    {
        public static bool AllowDisplacement => false;
        public static bool AllowPositionPuzzle => false;
        public static bool AllowInvulnerableHazards => false;
        public static bool AllowAddShield => false;
        // Torst/Hurika keep their original database perches, attacks and loot.
        // Their unsupported aerial patrol/pull puzzle is separate from dragons'
        // existing fly/land cycle, which is deliberately not affected here.
        public static bool AllowGlacierGriffonPatrol => false;

        /// <summary>
        /// The scripted bosses of the epic dungeons, Summoner's Hall and Darkness Falls reset (walk home, full health,
        /// adds and phases cleared) on any brain turn with an empty aggro list. In a raid of 200 the list can be empty
        /// for a moment while the boss is still being hit, and Hurionthex in Galladoria went from 31% back to full
        /// that way, again and again (2026-10-14). The reset now waits until the boss has been out of combat for
        /// <see cref="ResetQuietMilliseconds"/>; a wiped or departed raid still resets it as before.
        /// </summary>
        public const int ResetQuietMilliseconds = 8_000;

        /// <summary>
        /// Princes Ba'alorien and Asmoien in Darkness Falls devour pets near them and heal by the pet's health. They ate
        /// every pet within 2,000 units on every brain turn, so a raid with pet classes could never wound them
        /// (Asmoien skipped at 100% with raiders beside him, 2026-10-15). Mechanic change (owner, 2026-10-14: change
        /// mechanics rather than health): one pet every five seconds, the nearest; the heal per pet is unchanged.
        /// </summary>
        public const int PetDevourIntervalMilliseconds = 5_000;

        /// <summary>
        /// What the princes may devour: a living pet. A gamebot's own brain also implements IControlledBrain (it can
        /// follow a player), and a gamebot is an NPC to the server, so the old test ate the raiders themselves, one every
        /// five seconds, usually the tank (Darkness Falls audit L1, 2026-11-11). Gamebots are never prey; their pets are.
        /// </summary>
        public static bool IsDevourablePet(GameNPC candidate) =>
            candidate != null && IsDevourablePet(candidate is GameBot, candidate.Brain is DOL.AI.Brain.IControlledBrain, candidate.IsAlive);

        public static bool IsDevourablePet(bool isGameBot, bool hasControlledBrain, bool isAlive) => isAlive && hasControlledBrain && !isGameBot;

        /// <summary>
        /// Who a boss's call for help reaches: its own kind, a living monster of its realm that is nobody's pet. High Lord
        /// Baln's first hit ordered every idle NPC within 3,000 to attack, so a raid's own gamebots and pets were told to
        /// turn on the raider who struck him (Darkness Falls audit M1, 2026-11-11).
        /// </summary>
        public static bool IsOwnKind(GameNPC boss, GameNPC mob) =>
            boss != null && mob != null && mob != boss &&
            IsOwnKind(mob.Realm == boss.Realm, mob is GameBot, mob.Brain is DOL.AI.Brain.IControlledBrain, mob.IsAlive,
                (mob.Flags & GameNPC.eFlags.PEACE) != 0);

        public static bool IsOwnKind(bool sameRealm, bool isGameBot, bool hasControlledBrain, bool isAlive, bool isPeaceful) =>
            sameRealm && isAlive && !isGameBot && !hasControlledBrain && !isPeaceful;

        /// <summary>
        /// Encounters of several bodies that share one health pool (Olcasgean and his second body, the Giant Sporite
        /// Cluster and its copies) die together, but each body paid out its own loot and experience to whoever had hit
        /// that body, so a raid that hit two bodies was paid twice (epic-bosses M5). The body that pays takes over the
        /// others' attackers with this before it dies; the others are left with none and pay nothing.
        /// </summary>
        public static void TransferDamageCredit(GameLiving from, GameLiving into)
        {
            if (from == null || into == null || from == into)
                return;
            System.Collections.Generic.Dictionary<GameLiving, double> taken = new();
            lock (from.XpGainersLock)
            {
                MergeDamageCredit(from.XPGainers, taken);
                from.XPGainers.Clear();
            }
            lock (into.XpGainersLock)
                MergeDamageCredit(taken, into.XPGainers);
        }

        public static void MergeDamageCredit<T>(System.Collections.Generic.IDictionary<T, double> from, System.Collections.Generic.IDictionary<T, double> into)
        {
            foreach (var pair in from)
                into[pair.Key] = into.TryGetValue(pair.Key, out double already) ? already + pair.Value : pair.Value;
        }

        public static bool FightIsOver(GameNPC boss) => boss == null || !boss.IsAlive || !boss.InCombatInLast(ResetQuietMilliseconds);
    }
}
