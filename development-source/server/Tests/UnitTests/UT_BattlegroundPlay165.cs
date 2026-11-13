using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Battlegrounds fix 2026-11-12, lane BGPLAY: any bot of the bracket may grind a battleground (the launcher's
    /// Battlegrounds % is the PvP focus, a share of the eligible bots, spread and capped per battleground); bots get in
    /// and out without getting stuck; grinders fight enemy realms there as in DF, SH and the frontier; nobody hunts,
    /// roams or chases under an enemy portal keep's guards; battleground bots heal and raise each other.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BattlegroundPlay165
    {
        [SetUp]
        public void Reset()
        {
            AutonomousBattlegroundSlots.Clear();
            AutonomousBattlegroundExit.Clear();
        }

        [TearDown]
        public void Clean() => Reset();

        // ---- (1) grinding: level only, never the goal or the Battlegrounds %

        [TestCase(14, null)] [TestCase(15, "Abermenai")] [TestCase(22, "Thidranki")] [TestCase(27, "Murdaigean")]
        [TestCase(35, "Caledonia")] [TestCase(36, null)] [TestCase(50, null)]
        public void AnyBotOfTheBracketMayGrindItsBattleground(int level, string expected) =>
            Assert.That(AutonomousBattlegroundSlots.GrindBracket(level, wayIn: true, grindEnabled: true, reentryBlocked: false)?.Name,
                Is.EqualTo(expected));

        [Test]
        public void GrindingNeedsAWayInTheSwitchAndNoRecentForcedExit()
        {
            Assert.That(AutonomousBattlegroundSlots.GrindBracket(22, wayIn: false, grindEnabled: true, reentryBlocked: false), Is.Null);
            Assert.That(AutonomousBattlegroundSlots.GrindBracket(22, wayIn: true, grindEnabled: false, reentryBlocked: false), Is.Null);
            Assert.That(AutonomousBattlegroundSlots.GrindBracket(22, wayIn: true, grindEnabled: true, reentryBlocked: true), Is.Null);
            Assert.That(AutonomousBattlegroundSlots.GrindEnabled, Is.True, "grinding is on by default (owner 2026-11-12)");
        }

        // ---- (2) the launcher %: a share of the eligible bots (BG253 F1)

        [TestCase(65, 10, 7)]   // Abermenai at 1,000 a realm: 10% is about 7 of the 65 level 15-19 bots, not 25 (10% of 250)
        [TestCase(65, 0, 0)]
        [TestCase(65, 100, 65)]
        [TestCase(0, 50, 0)]
        [TestCase(3, 20, 1)]
        public void BattlegroundsPercentIsAShareOfTheEligibleBots(int eligible, int percent, int expected) =>
            Assert.That(AutonomousBattlegroundSlots.Slots(eligible, percent), Is.EqualTo(expected));

        [Test]
        public void ThinBracketsGetAWaveFirstAndTheRestFollowsBracketSize()
        {
            // band 20-49 of one realm: Thidranki 300, Murdaigean 47, Caledonia 3 eligible (the live roster, BG252)
            int[] slots = AutonomousBattlegroundSlots.Spread(new[] { 0, 300, 47, 3 }, 35, AutonomousBattlegroundSlots.ThinBracketFloor, 60);
            Assert.That(slots, Is.EqualTo(new[] { 0, 23, 12, 0 }));
            Assert.That(slots[2], Is.GreaterThanOrEqualTo(AutonomousBattlegroundWave.MinimumWave), "Murdaigean can reach the assault wave");
        }

        [Test]
        public void TheThinnestBracketIsServedFirstWhenSlotsAreShort() =>
            Assert.That(AutonomousBattlegroundSlots.Spread(new[] { 0, 300, 47, 0 }, 15, 10, 60), Is.EqualTo(new[] { 0, 5, 10, 0 }));

        [Test]
        public void ABracketTooSmallForAWaveGetsOnlyItsProportionalShare() =>
            Assert.That(AutonomousBattlegroundSlots.Spread(new[] { 0, 300, 5, 0 }, 31, 10, 60), Is.EqualTo(new[] { 0, 31, 0, 0 }));

        [Test]
        public void OneRealmPutsAtMostTheCapIntoOneBattleground()
        {
            // BG252: about 450 bots in Thidranki at 1,000 a realm; the cap keeps 60 a realm and the rest does PvE
            int[] slots = AutonomousBattlegroundSlots.Spread(new[] { 0, 1000, 0, 0 }, 200, 10, 60);
            Assert.That(slots, Is.EqualTo(new[] { 0, 60, 0, 0 }));
            Assert.That(AutonomousBattlegroundSlots.Spread(new[] { 0, 1000, 0, 0 }, 200, 10, 0)[1], Is.EqualTo(200), "0 = no cap");
        }

        [Test]
        public void SpreadNeverExceedsTheTotalTheEligibleOrTheCap()
        {
            var random = new Random(1165);
            for (int run = 0; run < 2_000; run++)
            {
                int[] eligible = Enumerable.Range(0, 4).Select(_ => random.Next(0, 400)).ToArray();
                int total = random.Next(0, 500), floor = random.Next(0, 15), cap = random.Next(0, 120);
                int[] slots = AutonomousBattlegroundSlots.Spread(eligible, total, floor, cap);
                Assert.That(slots.Sum(), Is.LessThanOrEqualTo(total));
                for (int index = 0; index < 4; index++)
                {
                    Assert.That(slots[index], Is.InRange(0, eligible[index]));
                    if (cap > 0) Assert.That(slots[index], Is.LessThanOrEqualTo(cap));
                }
                int room = Enumerable.Range(0, 4).Sum(index => cap > 0 ? Math.Min(cap, eligible[index]) : eligible[index]);
                Assert.That(slots.Sum(), Is.EqualTo(Math.Min(total, room)), "slots are only left over when no bracket has room");
            }
        }

        [Test]
        public void EveryNewBattlegroundGoalTakesASlotOfItsBracket()
        {
            Assert.That(AutonomousBattlegroundSlots.TryTake(eRealm.Albion, 22), Is.True, "before the first pass the allocator's cap applies later");
            AutonomousBattlegroundSlots.Publish(eRealm.Albion, 1, target: 2, taken: 0);
            Assert.That(AutonomousBattlegroundSlots.Free(eRealm.Albion, 22), Is.EqualTo(2));
            Assert.That(AutonomousBattlegroundSlots.TryTake(eRealm.Albion, 22), Is.True);
            Assert.That(AutonomousBattlegroundSlots.TryTake(eRealm.Albion, 24), Is.True);
            Assert.That(AutonomousBattlegroundSlots.TryTake(eRealm.Albion, 23), Is.False, "Thidranki is full for Albion");
            Assert.That(AutonomousBattlegroundSlots.Free(eRealm.Midgard, 22), Is.EqualTo(int.MaxValue), "each realm has its own slots");
            Assert.That(AutonomousBattlegroundSlots.TryTake(eRealm.Albion, 40), Is.False, "no bracket, no slot");
        }

        [TestCase(15, 0)] [TestCase(19, 0)] [TestCase(20, 1)] [TestCase(29, 2)] [TestCase(35, 3)] [TestCase(36, -1)] [TestCase(14, -1)]
        public void BracketIndexFollowsTheLevel(int level, int expected) =>
            Assert.That(AutonomousBattlegroundSlots.BracketIndex(level), Is.EqualTo(expected));

        // ---- in and out without getting stuck (owner 2026-11-12)

        [Test]
        public void AGrinderStaysOnlyWhileItsBusinessIsInside()
        {
            Assert.That(Decide(), Is.EqualTo(eBattlegroundStay.Stay), "grinding a camp here");
            Assert.That(Decide(hasCamp: false, campHere: false), Is.EqualTo(eBattlegroundStay.Stay), "choosing its next camp");
            Assert.That(Decide(levelFits: false), Is.EqualTo(eBattlegroundStay.LevelOut));
            Assert.That(Decide(betweenTasks: true), Is.EqualTo(eBattlegroundStay.Errands), "sell, repair, train, healer, town break");
            Assert.That(Decide(rvrGoal: true), Is.EqualTo(eBattlegroundStay.BusinessElsewhere));
            Assert.That(Decide(inParty: true, partyBusinessHere: false), Is.EqualTo(eBattlegroundStay.BusinessElsewhere), "a party meeting elsewhere");
            Assert.That(Decide(inParty: true, partyBusinessHere: true), Is.EqualTo(eBattlegroundStay.Stay), "a party grinding here");
            Assert.That(Decide(campHere: false), Is.EqualTo(eBattlegroundStay.CampElsewhere));
            Assert.That(Decide(stuck: true), Is.EqualTo(eBattlegroundStay.Stuck));
        }

        [Test]
        public void APartyBetweenCampsInsidePlansThereInsteadOfWalkingOut()
        {
            // review 2026-11-12: a PvE party grinding a battleground with its next camp not chosen yet stays
            Assert.That(AutonomousBattlegroundExit.PartyBusinessHere(hasCamp: false, campHere: false, assemblingOrRegrouping: false,
                rendezvousHere: false), Is.True);
            Assert.That(AutonomousBattlegroundExit.PartyBusinessHere(true, true, false, false), Is.True, "its camp is here");
            Assert.That(AutonomousBattlegroundExit.PartyBusinessHere(true, false, false, true), Is.False, "its camp is elsewhere");
            Assert.That(AutonomousBattlegroundExit.PartyBusinessHere(false, false, true, false), Is.False, "meeting in a town");
            Assert.That(AutonomousBattlegroundExit.PartyBusinessHere(false, false, true, true), Is.True, "meeting here");
            Assert.That(Decide(inParty: true, partyBusinessHere: AutonomousBattlegroundExit.PartyBusinessHere(false, false, false, false),
                hasCamp: false, campHere: false), Is.EqualTo(eBattlegroundStay.Stay));
        }

        [Test]
        public void AFailedRetreatFromAnEnemyPortalKeepHandsOverToTheExit()
        {
            Assert.That(AutonomousBattlegroundExit.RetreatFailed(0, 99_000_000), Is.False, "not retreating");
            Assert.That(AutonomousBattlegroundExit.RetreatFailed(1_000, 1_000 + AutonomousBattlegroundExit.RetreatMilliseconds - 1), Is.False);
            Assert.That(AutonomousBattlegroundExit.RetreatFailed(1_000, 1_000 + AutonomousBattlegroundExit.RetreatMilliseconds), Is.True);
            Assert.That(AutonomousBattlegroundExit.RetreatMilliseconds, Is.LessThan(AutonomousBattlegroundExit.StuckMilliseconds),
                "long before the 20-minute backstop");
        }

        [Test]
        public void APvpTourStaysUntilItLevelsOutOrIsStuck()
        {
            Assert.That(Decide(pvpTour: true, hasCamp: false, campHere: false), Is.EqualTo(eBattlegroundStay.Stay));
            Assert.That(Decide(pvpTour: true, inParty: true, partyBusinessHere: false, hasCamp: false, campHere: false),
                Is.EqualTo(eBattlegroundStay.Stay), "a battleground warband has no camp and stays");
            Assert.That(Decide(pvpTour: true, levelFits: false), Is.EqualTo(eBattlegroundStay.LevelOut), "also when it logs in above the bracket");
            Assert.That(Decide(pvpTour: true, stuck: true), Is.EqualTo(eBattlegroundStay.Stuck));
        }

        [Test]
        public void TheWayOutAlwaysEnds()
        {
            const float Far = 20_000, AtExit = 100;
            Assert.That(AutonomousBattlegroundExit.ExitStep(Far, false, 0), Is.EqualTo(eBattlegroundExitStep.WalkToExit));
            Assert.That(AutonomousBattlegroundExit.ExitStep(AtExit, false, 30_000), Is.EqualTo(eBattlegroundExitStep.TakeExit));
            Assert.That(AutonomousBattlegroundExit.ExitStep(AtExit, true, 30_000), Is.EqualTo(eBattlegroundExitStep.FinishFight));
            Assert.That(AutonomousBattlegroundExit.ExitStep(Far, false, AutonomousBattlegroundExit.ExitWalkMilliseconds),
                Is.EqualTo(eBattlegroundExitStep.ExitFromHere), "a failed walk takes the exit where the bot stands");
            Assert.That(AutonomousBattlegroundExit.ExitStep(Far, true, AutonomousBattlegroundExit.ExitWalkMilliseconds),
                Is.EqualTo(eBattlegroundExitStep.WalkToExit), "a fight on the way is finished first");
            Assert.That(AutonomousBattlegroundExit.ExitStep(AtExit, true, AutonomousBattlegroundExit.ExitHardLimitMilliseconds),
                Is.EqualTo(eBattlegroundExitStep.ExitFromHere), "even fights cannot hold a bot in forever");
            Assert.That(AutonomousBattlegroundExit.ExitStep(Far, true, AutonomousBattlegroundExit.ExitHardLimitMilliseconds),
                Is.EqualTo(eBattlegroundExitStep.ExitFromHere));
        }

        [Test]
        public void TwentyMinutesWithoutProgressIsStuck()
        {
            Assert.That(AutonomousBattlegroundExit.IsStuck(0, 99_000_000), Is.False, "not measured yet");
            Assert.That(AutonomousBattlegroundExit.IsStuck(1_000, 1_000 + AutonomousBattlegroundExit.StuckMilliseconds - 1), Is.False);
            Assert.That(AutonomousBattlegroundExit.IsStuck(1_000, 1_000 + AutonomousBattlegroundExit.StuckMilliseconds), Is.True);
        }

        [Test]
        public void OnlyGainsCountAsProgress()
        {
            Assert.That(AutonomousBattlegroundExit.IsProgress(1_001, 1_000, 0, 0, false, false), Is.True, "XP from a kill");
            Assert.That(AutonomousBattlegroundExit.IsProgress(1_000, 1_000, 5, 0, false, false), Is.True, "realm points");
            Assert.That(AutonomousBattlegroundExit.IsProgress(900, 1_000, 0, 0, false, false), Is.False, "a death's XP loss");
            Assert.That(AutonomousBattlegroundExit.IsProgress(1_000, 1_000, 0, 0, true, false), Is.False,
                "a grinder walking (back in after a death) is not progress");
            Assert.That(AutonomousBattlegroundExit.IsProgress(1_000, 1_000, 0, 0, true, true), Is.True, "a PvP roamer crossing the field");
            Assert.That(AutonomousBattlegroundExit.DeathStrikesBlock(AutonomousBattlegroundExit.DeathStrikeLimit - 1), Is.False);
            Assert.That(AutonomousBattlegroundExit.DeathStrikesBlock(AutonomousBattlegroundExit.DeathStrikeLimit), Is.True,
                "dying over and over (portal keep guards included) is not progress");
        }

        [Test]
        public void AForcedExitBlocksReentryForAWhileNoLoop()
        {
            const long Now = 10_000_000;
            Assert.That(AutonomousBattlegroundExit.ReentryBlocked(7, Now), Is.False);
            AutonomousBattlegroundExit.BlockReentry(7, Now);
            Assert.That(AutonomousBattlegroundExit.ReentryBlocked(7, Now + 1), Is.True);
            Assert.That(AutonomousBattlegroundSlots.GrindBracket(22, true, true,
                AutonomousBattlegroundExit.ReentryBlocked(7, Now + 1)), Is.Null, "no battleground camp while blocked");
            Assert.That(AutonomousBattlegroundExit.ReentryBlocked(8, Now + 1), Is.False, "only that bot");
            Assert.That(AutonomousBattlegroundExit.ReentryBlocked(7, Now + AutonomousBattlegroundExit.ReentryBlockMilliseconds), Is.False);
        }

        [Test]
        public void AnOutlevelledBotHasNoBattlegroundToReenter()
        {
            // a level 25 bot leaving Thidranki is offered Murdaigean, never Thidranki again; at 36 none at all
            Assert.That(AutonomousBattlegroundSlots.GrindBracket(25, true, true, false)?.RegionId, Is.EqualTo((ushort)251));
            Assert.That(BattlegroundBrackets.Allows(BattlegroundBrackets.ForRegion(252), 25), Is.False);
            Assert.That(AutonomousBattlegroundSlots.GrindBracket(36, true, true, false), Is.Null);
            Assert.That(Decide(levelFits: false), Is.EqualTo(eBattlegroundStay.LevelOut));
        }

        [Test]
        public void ABoardingThatNeverDepartsEntersDirectly()
        {
            Assert.That(AutonomousBattlegroundExit.BoardingTimedOut(0, 99_000_000), Is.False);
            Assert.That(AutonomousBattlegroundExit.BoardingTimedOut(1_000, 1_000 + AutonomousBattlegroundExit.BoardingMilliseconds - 1), Is.False);
            Assert.That(AutonomousBattlegroundExit.BoardingTimedOut(1_000, 1_000 + AutonomousBattlegroundExit.BoardingMilliseconds), Is.True);
        }

        [TestCase(eRealm.Albion)] [TestCase(eRealm.Midgard)] [TestCase(eRealm.Hibernia)]
        public void BotsBoardThePortalKeepTeleporterWithTheBattlegroundsMedallion(eRealm realm)
        {
            for (int index = 0; index < BattlegroundBrackets.Count; index++)
            {
                BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.Get(index);
                AutonomousFrontierTransport.Passage passage = AutonomousFrontierTransport.BattlegroundPassage(realm, bracket);
                Assert.That(passage.Medallion, Is.EqualTo("battlegrounds_necklace"));
                Assert.That(passage.Region, Is.EqualTo(bracket.RegionId));
                Assert.That(passage.Location.RegionID, Is.EqualTo(bracket.RegionId));
                Assert.That(passage.Location.X, Is.EqualTo(BattlegroundBrackets.BotArrival(bracket, realm).X));
                Assert.That(AutonomousFrontierTransport.IsBattlegroundPassage(passage), Is.True);
                foreach (eAutonomousObjectiveKind goal in Enum.GetValues<eAutonomousObjectiveKind>())
                    Assert.That(AutonomousFrontierTransport.CanBoardForObjective(realm, goal, passage), Is.True, "grinders and the PvP focus");
            }
            Assert.That(AutonomousFrontierTransport.BattlegroundPassage(eRealm.None, BattlegroundBrackets.Get(0)), Is.Null);
            ushort enemy = realm == eRealm.Albion ? (ushort)100 : (ushort)1;
            Assert.That(AutonomousFrontierTransport.CanBoardForObjective(realm, eAutonomousObjectiveKind.SoloPve,
                AutonomousFrontierTransport.Destination(realm, enemy)), Is.False, "frontier medallions stay RvR-only");
            Assert.That(AutonomousFrontierTransport.IsBattlegroundPassage(AutonomousFrontierTransport.Destination(realm, enemy)), Is.False);
        }

        // ---- grinders fight enemy realms as in DF, SH and the frontier (one rule: IsPvpGround)

        [TestCase((ushort)250)] [TestCase((ushort)251)] [TestCase((ushort)252)] [TestCase((ushort)253)]
        public void ABattlegroundIsPvpGroundLikeDarknessFalls(ushort battleground)
        {
            Assert.That(AutonomousWorldBotController.IsPvpGround(battleground, null), Is.True);
            Assert.That(AutonomousWorldBotController.IsPvpGround(battleground, (ushort)(battleground * 10)), Is.True);
            Assert.That(AutonomousWorldBotController.IsPvpGround(RealmRaidNeutralEvents.DarknessFalls, null), Is.True, "Darkness Falls");
            Assert.That(AutonomousWorldBotController.IsPvpGround(RealmRaidNeutralEvents.SummonersHall, null), Is.True, "Summoner's Hall");
            Assert.That(AutonomousWorldBotController.IsPvpGround(1, 11), Is.True, "Albion's frontier");
            Assert.That(AutonomousWorldBotController.IsPvpGround(1, 0), Is.False, "Albion's home zones");
            Assert.That(AutonomousWorldBotController.IsPvpGround(10, null), Is.False, "Camelot");
        }

        // ---- enemy portal keeps (their period guards are back: level 90-95, about 2,000 aggro)

        [Test]
        public void NobodyGoesUnderAnEnemyPortalKeepsGuards()
        {
            Vector2 mid = AutonomousBattlegroundPortalKeeps.Centre(eRealm.Midgard).Value;
            int avoid = AutonomousBattlegroundPortalKeeps.AvoidRadius;
            Assert.That(avoid, Is.GreaterThan(AutonomousBattlegroundPortalKeeps.GuardAggroRange));
            Assert.That(AutonomousBattlegroundPortalKeeps.EnemyKeepNear(eRealm.Albion, mid.X + avoid - 1, mid.Y, avoid, out _),
                Is.EqualTo(eRealm.Midgard));
            Assert.That(AutonomousBattlegroundPortalKeeps.EnemyKeepNear(eRealm.Albion, mid.X + avoid + 1, mid.Y, avoid, out _),
                Is.EqualTo(eRealm.None));
            Assert.That(AutonomousBattlegroundPortalKeeps.EnemyKeepNear(eRealm.Midgard, mid.X, mid.Y, avoid, out _),
                Is.EqualTo(eRealm.None), "its own portal keep is safe");
            foreach (ushort region in new ushort[] { 250, 251, 252, 253 })
                Assert.That(AutonomousBattlegroundPortalKeeps.InEnemyDanger(eRealm.Hibernia, region, mid.X, mid.Y), Is.True);
            Assert.That(AutonomousBattlegroundPortalKeeps.InEnemyDanger(eRealm.Hibernia, 1, mid.X, mid.Y), Is.False, "battlegrounds only");
            Assert.That(AutonomousBattlegroundPortalKeeps.InEnemyDanger(eRealm.Hibernia, 252, mid.X + avoid + 500, mid.Y), Is.False);
            Assert.That(AutonomousBattlegroundPortalKeeps.InEnemyDanger(eRealm.Hibernia, 252, mid.X + avoid + 500, mid.Y,
                AutonomousBattlegroundPortalKeeps.CampAvoidRadius), Is.True, "a camp keeps a wider berth (patrol and target search)");
        }

        [Test]
        public void TheBotsOwnArrivalAndExitAreNeverInDanger()
        {
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                for (int index = 0; index < BattlegroundBrackets.Count; index++)
                {
                    GameLocation arrival = BattlegroundBrackets.BotArrival(BattlegroundBrackets.Get(index), realm);
                    Assert.That(AutonomousBattlegroundPortalKeeps.InEnemyDanger(realm, arrival.RegionID, arrival.X, arrival.Y,
                        AutonomousBattlegroundPortalKeeps.CampAvoidRadius), Is.False);
                }
        }

        [Test]
        public void ARetreatEndsOutsideTheDanger()
        {
            Vector2 hib = AutonomousBattlegroundPortalKeeps.Centre(eRealm.Hibernia).Value;
            foreach (Vector2 from in new[] { hib + new Vector2(300, 0), hib + new Vector2(-1000, 1500), hib })
            {
                Vector2 away = AutonomousBattlegroundPortalKeeps.RetreatPoint(hib, from);
                Assert.That(Vector2.Distance(away, hib), Is.EqualTo(AutonomousBattlegroundPortalKeeps.AvoidRadius +
                    AutonomousBattlegroundPortalKeeps.RetreatMargin).Within(0.5));
                Assert.That(AutonomousBattlegroundPortalKeeps.InEnemyDanger(eRealm.Albion, 252, away.X, away.Y), Is.False);
            }
        }

        // ---- (3) heals between battleground bots rank with the guards (lowest health first)

        [Test]
        public void BattlegroundComradesRankWithTheGuards() =>
            Assert.That(BotBrain.BattlegroundComradeRank, Is.EqualTo(BotBrain.RvrSupportRank(carriesRelic: false, keepLord: false)));

        // ---- (3) real warbands for the PvP focus, by the frontier RvR warband rules

        [Test]
        public void BattlegroundWarbandsUseTheFrontierWarbandRules()
        {
            Assert.That(AutonomousBotGroupCoordinator.UsesWarbandRules(eAutonomousObjectiveKind.Battleground), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.UsesWarbandRules(eAutonomousObjectiveKind.RvR), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.UsesWarbandRules(eAutonomousObjectiveKind.SoloPve), Is.False, "grinders unchanged");
            Assert.That(AutonomousBotGroupCoordinator.UsesWarbandRules(eAutonomousObjectiveKind.GroupPve), Is.False, "PvE parties unchanged");
            foreach (int roster in new[] { 1, 3, 10, 57, 400 })
                Assert.That(AutonomousBotGroupCoordinator.MaximumGroupedForObjective(eAutonomousObjectiveKind.Battleground, roster),
                    Is.EqualTo(AutonomousBotGroupCoordinator.MaximumGroupedForObjective(eAutonomousObjectiveKind.RvR, roster)),
                    "the same 35% solo reserve as frontier warbands");
            Assert.That(AutonomousBotGroupCoordinator.ShouldContinueFormationSearch(eAutonomousObjectiveKind.Battleground, 0), Is.True);
        }

        // ---- (2) keep-guard targets: shared per keep, held per bot, few raycasts

        [Test]
        public void KeepCandidatesAreBuiltOncePerSecondAndShared()
        {
            var cache = new AutonomousTtlCache<int, int>(AutonomousRvrGuardTargets.CandidateTtlMilliseconds);
            int builds = 0;
            int Build(int key) { builds++; return key * 10; }
            for (long now = 1_000; now < 2_000; now += 50)          // twenty bots' thinks within one second
                Assert.That(cache.Get(11, now, Build), Is.EqualTo(110));
            Assert.That(builds, Is.EqualTo(1));
            cache.Get(12, 1_500, Build);
            Assert.That(builds, Is.EqualTo(2), "each keep (and realm) has its own entry");
            cache.Get(11, 2_000, Build);
            Assert.That(builds, Is.EqualTo(3), "rebuilt after the TTL");
        }

        [Test]
        public void ABotsCheckedTargetsAreHeldBriefly()
        {
            long until = 10_000;
            Assert.That(AutonomousRvrGuardTargets.HoldValid(9_999, until, 11, 11, 0), Is.True);
            Assert.That(AutonomousRvrGuardTargets.HoldValid(10_000, until, 11, 11, 0), Is.False, "hold over");
            Assert.That(AutonomousRvrGuardTargets.HoldValid(9_000, until, 31, 11, 0), Is.False, "another keep");
            float far = (AutonomousRvrGuardTargets.HoldMoveDistance + 1f) * (AutonomousRvrGuardTargets.HoldMoveDistance + 1f);
            Assert.That(AutonomousRvrGuardTargets.HoldValid(9_000, until, 11, 11, far), Is.False, "moved: its line of sight changed");
        }

        [Test]
        public void ARebuildRaycastsTheLordAndTheNearestFewOnly()
        {
            float[] distances = { 900, 100, 4_000, 300, 2_500, 50, 700, 1_200, 1_900, 3_100, 2_200, 600 };
            bool[] lords = new bool[distances.Length];
            lords[2] = true;
            int[] picked = AutonomousRvrGuardTargets.Shortlist(distances, lords, 4);
            Assert.That(picked, Is.EqualTo(new[] { 2, 5, 1, 3, 11 }), "the lord, then the four nearest");
            Assert.That(AutonomousRvrGuardTargets.Shortlist(distances, lords, AutonomousRvrGuardTargets.MaxLineChecks).Length,
                Is.EqualTo(1 + AutonomousRvrGuardTargets.MaxLineChecks));
            Assert.That(AutonomousRvrGuardTargets.Shortlist(new float[] { 5, 1 }, new[] { false, false }, 8), Is.EqualTo(new[] { 1, 0 }));
        }

        private static eBattlegroundStay Decide(bool pvpTour = false, bool levelFits = true, bool betweenTasks = false,
            bool rvrGoal = false, bool inParty = false, bool partyBusinessHere = false, bool hasCamp = true, bool campHere = true,
            bool stuck = false) =>
            AutonomousBattlegroundExit.Decide(pvpTour, levelFits, betweenTasks, rvrGoal, inParty, partyBusinessHere, hasCamp, campHere, stuck);
    }
}
