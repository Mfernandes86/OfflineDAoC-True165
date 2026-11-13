using System.Collections.Concurrent;

namespace DOL.GS
{
    /// <summary>Why a bot inside a battleground leaves it (or <see cref="Stay"/>).</summary>
    public enum eBattlegroundStay
    {
        Stay,
        /// <summary>Its level no longer fits the battleground's bracket (also after logging in above the bracket).</summary>
        LevelOut,
        /// <summary>Between tasks: selling, repairs, training, the healer or a town break are all outside.</summary>
        Errands,
        /// <summary>An RvR goal (the frontier) or a party whose business is elsewhere.</summary>
        BusinessElsewhere,
        /// <summary>Its next hunting camp is in another region.</summary>
        CampElsewhere,
        /// <summary>No progress inside for <see cref="AutonomousBattlegroundExit.StuckMilliseconds"/>.</summary>
        Stuck,
    }

    /// <summary>The next step of a bot walking out of a battleground.</summary>
    public enum eBattlegroundExitStep
    {
        /// <summary>Walk to the realm's exit spot (its field arrival point, where the exit portal is).</summary>
        WalkToExit,
        /// <summary>At the exit spot and out of combat: take the exit to the realm's border keep.</summary>
        TakeExit,
        /// <summary>At the exit spot but fighting: finish the fight first.</summary>
        FinishFight,
        /// <summary>The walk has failed for too long: take the exit from where the bot stands (guaranteed way out).</summary>
        ExitFromHere,
    }

    /// <summary>
    /// Owner 2026-11-12: "make sure the gamebots can get in and out of the battlegrounds after a grind task and dont get
    /// stuck forever". The pure rules for leaving a battleground, the exit fallback chain, the in-battleground stuck
    /// backstop and the re-entry block that stops a bot looping between the entry and the exit. Battlegrounds have no
    /// zone connections, so the ordinary route code finds no way out of one: every way out goes through these rules
    /// (AutonomousWorldBotController.LeaveBattlegroundWhenDone / LeaveBattleground / ExitBattlegroundNow).
    /// </summary>
    public static class AutonomousBattlegroundExit
    {
        /// <summary>A bot inside a battleground with no XP, realm points, kill, goal arrival or real movement for this long
        /// is taken out to its border keep (never the capital vault).</summary>
        public const long StuckMilliseconds = 20 * 60_000;
        /// <summary>Moving this far from the last progress spot counts as progress (a roamer crossing the field).</summary>
        public const int ProgressMoveDistance = 1_500;
        /// <summary>A walk to the exit spot that has not got there in this long ends with the exit taken where the bot stands.</summary>
        public const long ExitWalkMilliseconds = 4 * 60_000;
        /// <summary>Even a bot that keeps getting into fights at the exit leaves after this long.</summary>
        public const long ExitHardLimitMilliseconds = 10 * 60_000;
        /// <summary>A bot that has tried this long to walk out of an enemy portal keep's danger radius is taken out
        /// (ExitBattlegroundNow) instead: the retreat must not hold it there forever.</summary>
        public const long RetreatMilliseconds = 2 * 60_000;
        /// <summary>A bot forced out (stuck) cannot be sent back into any battleground for this long.</summary>
        public const long ReentryBlockMilliseconds = 30 * 60_000;
        /// <summary>A boarding at the portal keep teleporter that has not departed in this long enters directly.</summary>
        public const long BoardingMilliseconds = 3 * 60_000;
        public const int ExitRadius = 400;

        /// <summary>
        /// Pure: whether a bot inside a battleground stays. A PvP-focus bot on its own tour is ExecuteBattleground's (it
        /// ends its tour at a level-out and leaves on the next travel), so it stays here unless stuck or out-levelled.
        /// Any other bot stays only while it grinds there: its level fits, it is not between tasks, it has no RvR goal or
        /// party business elsewhere, and its camp (when it has one) is in this battleground.
        /// </summary>
        public static eBattlegroundStay Decide(bool pvpTour, bool levelFits, bool betweenTasks, bool rvrGoal,
            bool inParty, bool partyBusinessHere, bool hasCamp, bool campHere, bool stuck)
        {
            if (!levelFits) return eBattlegroundStay.LevelOut;
            if (stuck) return eBattlegroundStay.Stuck;
            // A PvP-focus bot (alone or in its battleground warband) stays: its tour and its warband run inside.
            if (pvpTour) return eBattlegroundStay.Stay;
            if (betweenTasks) return eBattlegroundStay.Errands;
            if (rvrGoal || inParty && !partyBusinessHere) return eBattlegroundStay.BusinessElsewhere;
            if (!inParty && hasCamp && !campHere) return eBattlegroundStay.CampElsewhere;
            return eBattlegroundStay.Stay;
        }

        /// <summary>Pure: the next step out, from the distance to the exit spot, combat and how long the bot has been leaving.</summary>
        public static eBattlegroundExitStep ExitStep(float distanceToExit, bool inCombat, long leavingMilliseconds)
        {
            if (leavingMilliseconds >= ExitHardLimitMilliseconds) return eBattlegroundExitStep.ExitFromHere;
            if (distanceToExit <= ExitRadius) return inCombat ? eBattlegroundExitStep.FinishFight : eBattlegroundExitStep.TakeExit;
            if (!inCombat && leavingMilliseconds >= ExitWalkMilliseconds) return eBattlegroundExitStep.ExitFromHere;
            return eBattlegroundExitStep.WalkToExit;
        }

        /// <summary>Pure: no progress inside for <see cref="StuckMilliseconds"/> (0 = not yet measured).</summary>
        public static bool IsStuck(long lastProgressTick, long now) =>
            lastProgressTick > 0 && now - lastProgressTick >= StuckMilliseconds;

        /// <summary>Pure: the walk out of an enemy portal keep's danger radius has failed for too long (0 = not retreating).</summary>
        public static bool RetreatFailed(long retreatingSince, long now) =>
            retreatingSince > 0 && now - retreatingSince >= RetreatMilliseconds;

        /// <summary>
        /// Pure (review 2026-11-12): whether a bot's party has its business in this battleground. Its camp decides once it
        /// has one; while the party is assembling or regrouping, its meeting point decides; between camps (the leader is
        /// choosing the next one) the party waits and plans where it is rather than walking out and back.
        /// </summary>
        public static bool PartyBusinessHere(bool hasCamp, bool campHere, bool assemblingOrRegrouping, bool rendezvousHere) =>
            hasCamp ? campHere : !assemblingOrRegrouping || rendezvousHere;

        /// <summary>Pure: the portal keep boarding has waited too long; the bot enters directly.</summary>
        public static bool BoardingTimedOut(long boardingSince, long now) =>
            boardingSince > 0 && now - boardingSince >= BoardingMilliseconds;

        /// <summary>This many deaths inside battlegrounds with no XP or realm points gained in between block re-entry (a
        /// bot dying over and over, to portal keep guards or anything else, is not making progress).</summary>
        public const int DeathStrikeLimit = 3;

        public static bool DeathStrikesBlock(int deathsWithoutGain) => deathsWithoutGain >= DeathStrikeLimit;

        /// <summary>
        /// Pure: progress inside a battleground. XP or realm points GAINED (a death's XP loss is not progress); moving far
        /// counts only for a PvP-focus bot, whose job is roaming the field (a grinder's job is kills, and walking back in
        /// after every death must not hide a death loop).
        /// </summary>
        public static bool IsProgress(long experience, long lastExperience, long realmPoints, long lastRealmPoints,
            bool movedFar, bool pvpTour) =>
            experience > lastExperience || realmPoints > lastRealmPoints || pvpTour && movedFar;

        private static readonly ConcurrentDictionary<long, long> ReentryBlockedUntil = new();

        /// <summary>Blocks every battleground for this bot for <see cref="ReentryBlockMilliseconds"/> (after a forced exit).</summary>
        public static void BlockReentry(long botId, long now)
        {
            if (ReentryBlockedUntil.Count > 8_192)
                foreach (var pair in ReentryBlockedUntil)
                    if (pair.Value <= now) ReentryBlockedUntil.TryRemove(pair.Key, out _);
            ReentryBlockedUntil[botId] = now + ReentryBlockMilliseconds;
        }

        public static bool ReentryBlocked(long botId, long now) =>
            ReentryBlockedUntil.TryGetValue(botId, out long until) && now < until;

        public static void Clear() => ReentryBlockedUntil.Clear();
    }
}

namespace DOL.GS
{
    /// <summary>
    /// The realms' portal keeps inside the battlegrounds (the same spots in all four: Albion 37301,52362, Midgard
    /// 54053,24680, Hibernia 18362,18257). Battlegrounds fix 2026-11-12 (lane BGCONTENT): their period guards are back,
    /// level 90-95 GameGuards with about 2,000 aggro range, so a bot of another realm near one is shot dead (period:
    /// "you hide behind your PK guards"). Bots keep out: no hunting camp, roam point or chased enemy inside the danger
    /// radius of another realm's portal keep, and a bot that finds itself inside one walks back out.
    /// </summary>
    public static class AutonomousBattlegroundPortalKeeps
    {
        public const int GuardAggroRange = 2_000;
        /// <summary>Roaming, chasing and standing: the guards' aggro range plus a margin.</summary>
        public const int AvoidRadius = GuardAggroRange + 600;
        /// <summary>A hunting camp's anchor: its patrol (850) and target search stay outside <see cref="AvoidRadius"/> too.</summary>
        public const int CampAvoidRadius = AvoidRadius + 900;
        /// <summary>A bot walking out of the danger stops this far beyond <see cref="AvoidRadius"/>.</summary>
        public const int RetreatMargin = 300;

        public static System.Numerics.Vector2? Centre(eRealm realm) => realm switch
        {
            eRealm.Albion => new System.Numerics.Vector2(37301, 52362),
            eRealm.Midgard => new System.Numerics.Vector2(54053, 24680),
            eRealm.Hibernia => new System.Numerics.Vector2(18362, 18257),
            _ => null,
        };

        /// <summary>Pure: the realm whose portal keep (not the bot's own) is within <paramref name="radius"/> of x,y, or None.</summary>
        public static eRealm EnemyKeepNear(eRealm botRealm, float x, float y, int radius, out System.Numerics.Vector2 centre)
        {
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                if (realm == botRealm || Centre(realm) is not { } keep) continue;
                if (System.Numerics.Vector2.DistanceSquared(keep, new(x, y)) <= (float)radius * radius)
                {
                    centre = keep;
                    return realm;
                }
            }
            centre = default;
            return eRealm.None;
        }

        /// <summary>Pure: x,y in a battleground region lies inside another realm's portal keep danger radius.</summary>
        public static bool InEnemyDanger(eRealm botRealm, ushort regionId, float x, float y, int radius = AvoidRadius) =>
            BattlegroundBrackets.IsBattlegroundRegion(regionId) && EnemyKeepNear(botRealm, x, y, radius, out _) != eRealm.None;

        /// <summary>Pure: the spot straight away from the keep's centre, <see cref="AvoidRadius"/> + <see cref="RetreatMargin"/> out.</summary>
        public static System.Numerics.Vector2 RetreatPoint(System.Numerics.Vector2 keepCentre, System.Numerics.Vector2 from)
        {
            System.Numerics.Vector2 away = from - keepCentre;
            away = away.LengthSquared() < 1 ? System.Numerics.Vector2.UnitX : System.Numerics.Vector2.Normalize(away);
            return keepCentre + away * (AvoidRadius + RetreatMargin);
        }
    }
}
