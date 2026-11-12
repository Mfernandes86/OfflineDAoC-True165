using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Relic carrier events and relic campaigns across restarts (relic audit 2026-10-31: L5, L6, B5).
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    /// <summary>A carrier event's own window from the moment the relic leaves its pad (it no longer keeps the siege's leftover time).</summary>
    public static long CarrierEventMilliseconds => Classic165Mode.TravelTime(2 * 60 * 60_000L);
    /// <summary>While the relic is carried or on the ground the carrier event stays open at least this long ahead.</summary>
    public const long CarrierKeepOpenMilliseconds = 15 * 60_000;
    /// <summary>A relic seen back on a pad this long without its RelicMounted/RelicReturned event ends the carrier event anyway.</summary>
    public const long CarrierMountedGraceMilliseconds = 2_000;

    /// <summary>The relic keep a carrier event came from, the realm that took the relic and the realm that held it
    /// (owner 2026-11-10, round 3).</summary>
    private static readonly Dictionary<string, (string KeepId, eRealm Attacker, eRealm Defender)> CarrierOrigins = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> CarrierMountedSeen = new(StringComparer.Ordinal);

    /// <summary>
    /// Relic audit L6: a relic that kept being picked up (dropped at a bad spot, lifted again) held its carrier event open
    /// without limit, and while it lasted the realms in it started no siege. The road struggle now has a hard end:
    /// <see cref="CarrierEventMilliseconds"/> from the first pickup plus one extension for each region the relic really
    /// crossed (at most <see cref="MaxCarrierCrossingExtensions"/>). Then the escorts and interceptors stand down and the
    /// relic is left to its own return timer; a carrier still on its way goes on alone.
    /// </summary>
    public static long CarrierCrossingExtensionMilliseconds => Classic165Mode.TravelTime(30 * 60_000L);
    public const int MaxCarrierCrossingExtensions = 3;

    /// <summary>Pure rule (L6): when a relic's road struggle ends at the latest.</summary>
    public static long CarrierHardEnd(long firstPickupTick, int regionsCrossed) =>
        firstPickupTick + CarrierEventMilliseconds + Math.Clamp(regionsCrossed, 0, MaxCarrierCrossingExtensions) * CarrierCrossingExtensionMilliseconds;

    /// <summary>Pure rule (L6): a road struggle holds back the new sieges of the realms in it only (the carrying realm, the
    /// realm it was taken from, and any realm with forces in it), not of every realm.</summary>
    public static bool CarrierBlocksSiege(eRealm realm, eRealm taker, eRealm takenFrom, IEnumerable<eRealm> realmsWithForces) =>
        realm != eRealm.None && (realm == taker || realm == takenFrom || (realmsWithForces ?? []).Contains(realm));

    /// <summary>Called under Sync from ChooseCore: whether a live road struggle holds back this realm's new siege.</summary>
    private static bool CarrierBlocksSiegeFor(eRealm realm)
    {
        foreach (var (id, carrier) in CarrierEvents)
        {
            CarrierOrigins.TryGetValue(id, out var origin);
            if (CarrierBlocksSiege(realm, origin.Attacker, origin.Defender,
                    carrier.Participants.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key)))
                return true;
        }
        return false;
    }

    /// <summary>One relic off its pad, from its first pickup until it is mounted again.</summary>
    private sealed class CarrierRun
    {
        public long FirstTick;
        public ushort Region;
        public int Crossings;
        public bool Lapsed;
    }
    private static readonly Dictionary<string, CarrierRun> CarrierRuns = new(StringComparer.Ordinal);

    private static ushort RelicRegion(GameRelic relic) => relic.CurrentCarrier?.CurrentRegionID ?? relic.CurrentRegionID;

    /// <summary>
    /// Called under Sync from Expire. A relic that went back onto a pad by itself (return timer, lost item) never fired
    /// RelicMounted, so its carrier event lived on for hours and ChooseCore refused every new siege of every realm (L5).
    /// While the relic is still carried or on the ground the event stays open (L6), up to its hard end (CarrierHardEnd).
    /// </summary>
    private static void RefreshCarrierEvents(long nowTick)
    {
        if (CarrierEvents.Count == 0 && CarrierRuns.Count == 0) return;
        GameRelic[] relics = RelicMgr.GetRelics();
        if (relics.Length == 0) return;
        // A relic back on a pad (or gone) ends its run, lapsed or not.
        foreach (string id in CarrierRuns.Keys.ToArray())
            if (relics.FirstOrDefault(r => RelicCarrierTargetId(r) == id) is not { IsMounted: false })
                CarrierRuns.Remove(id);
        foreach (string id in CarrierEvents.Keys.ToArray())
        {
            GameRelic relic = relics.FirstOrDefault(r => RelicCarrierTargetId(r) == id);
            if (relic == null) continue;
            if (relic.IsMounted)
            {
                if (!CarrierMountedSeen.TryGetValue(id, out long seen)) { CarrierMountedSeen[id] = nowTick; continue; }
                if (nowTick - seen < CarrierMountedGraceMilliseconds) continue;
                CarrierMountedSeen.Remove(id);
                eRealm taker = CarrierOrigins.TryGetValue(id, out var origin) ? origin.Attacker
                    : CarrierTargets.GetValueOrDefault(id)?.OwningRealm ?? eRealm.None;
                bool captured = taker != eRealm.None && relic.Realm == taker;
                EndTarget(id, nowTick, captured ? relic.Realm : eRealm.None, captured ? "captured" : "returned");
                continue;
            }
            CarrierMountedSeen.Remove(id);
            var entry = CarrierEvents[id];
            ushort region = RelicRegion(relic);
            if (!CarrierRuns.TryGetValue(id, out CarrierRun run))
                CarrierRuns[id] = run = new CarrierRun { FirstTick = nowTick, Region = region };
            else if (region != 0 && region != run.Region)
            {
                if (run.Region != 0) run.Crossings++;
                run.Region = region;
            }
            var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
            if (run.Lapsed)
            {
                // An event joined again after the hard end (TryJoinCarrier) ends at once, quietly: the relic is on its own timer.
                RealmEventRecords.Finish(id, "Timed out", "Relic escort/interception: the road struggle had already run its course.");
                foreach (string force in entry.Participants.Values.SelectMany(realm => realm.Keys))
                    ReleasedForces[force] = "Relic event ended: the road struggle had already run its course";
                CarrierEvents.Remove(id);
                CarrierTargets.Remove(id);
                CarrierOrigins.Remove(id);
                Cooldowns[id] = nowTick + TargetCooldownMilliseconds;
                continue;
            }
            long hardEnd = CarrierHardEnd(run.FirstTick, run.Crossings);
            if (nowTick >= hardEnd)
            {
                // Expire's timeout below ends it this pass, with its notices and its release of every force.
                run.Lapsed = true;
                CarrierEvents[id] = (nowTick, entry.Participants);
                if (log.IsInfoEnabled) log.Info($"RVR_RELIC_CARRIER_LAPSED carrier={id} relic=\"{relic.Name}\" minutes={(nowTick - run.FirstTick) / 60_000} " +
                    $"crossings={run.Crossings} carried={relic.CurrentCarrier != null} region={region}");
                continue;
            }
            long keepOpen = Math.Min(nowTick + CarrierKeepOpenMilliseconds, hardEnd);
            long expires = Math.Min(Math.Max(entry.ExpiresTick, keepOpen), hardEnd);
            if (expires != entry.ExpiresTick) CarrierEvents[id] = (expires, entry.Participants);
        }
    }

    private static void LogCarrierEnded(string carrierId, string outcome, eRealm winner)
    {
        CarrierMountedSeen.Remove(carrierId);
        var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
        if (log.IsInfoEnabled) log.Info($"RVR_RELIC_CARRIER_ENDED carrier={carrierId} outcome={outcome} winner={GlobalConstants.RealmToName(winner)}");
    }

    /// <summary>The live keep behind a "rvr-keep-N" objective id, or null (other ids, no keep manager in tests).</summary>
    private static DOL.GS.Keeps.AbstractGameKeep KeepOfObjective(string id) =>
        id != null && id.StartsWith("rvr-keep-", StringComparison.Ordinal) && int.TryParse(id["rvr-keep-".Length..], out int keepId)
            ? LiveKeeps?.GetKeepByID(keepId) : null;

    /// <summary>The keep manager, or null when no server runs (unit tests: GameServer.KeepManager throws there).</summary>
    private static DOL.GS.Keeps.IKeepManager LiveKeeps => GameServer.Instance == null ? null : GameServer.KeepManager;

    /// <summary>Whether a relic raid by this realm on this relic keep could place its prize on its own pad (L3).</summary>
    private static bool RelicRaidCanPlace(LiveObjective relicKeep, eRealm attacker) =>
        relicKeep?.IsRelicKeep != true || KeepOfObjective(relicKeep.Id) is not { IsRelic: true } keep ||
        AutonomousRelicShrine.RaidCanPlace(keep, attacker);

    /// <summary>
    /// Relic audit L3: a realm that lost its own relic of a type can lift no enemy relic of that type (only its own), and
    /// its army used to march, break in and be refused at the pad for the rest of a four-hour battle. A relic raid (forced,
    /// a campaign step, or a resumed one) that cannot place its prize is refused before anything starts, and an open
    /// campaign for it ends. Called under Sync from ForceStart. True when refused.
    /// </summary>
    private static bool RefuseUnplaceableRelicRaid(LiveObjective target, eRealm attacker, out string reason)
    {
        reason = null;
        if (RelicRaidCanPlace(target, attacker)) return false;
        reason = $"{GlobalConstants.RealmToName(attacker)} cannot take a relic from {target.Name}: a realm must hold its own relic of that kind to lift another's.";
        if (Campaigns.TryGetValue(target.Id, out RelicCampaign campaign) && campaign.Attacker == attacker)
        {
            Campaigns.Remove(target.Id);
            var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
            if (log.IsInfoEnabled) log.Info($"RVR_RELIC_CAMPAIGN_ENDED relic={target.Id} attacker={attacker} reason=\"no relic there this realm may lift\"");
        }
        return true;
    }

    // ---- automatic relic raids (relic audit B3) ----------------------------------------------------------------------
    // The automatic path had no memory: it picked one enemy relic keep at random, dropped it when it was not open, sent the
    // next siege to one of its sentinel keeps only 12 % of the time and took an open relic keep only 12 % of the time. A
    // realm now plans as a veteran realm did: an open relic keep it can raid is its next siege; otherwise it picks the relic
    // keep it can open with the fewest sentinel keeps left (its "aim"), sends its sieges to those keeps one by one, and keeps
    // at it until the relic siege starts, the aim makes no progress, or nothing there can be lifted any more. All of it is
    // decided in ChooseCore, at the realm's own siege decision, behind the existing once-a-minute start roll, the one
    // attack per realm and the cooldowns. A launcher campaign (ForceStart) for the realm leads instead of an aim.

    /// <summary>One relic keep as the planner ranks it.</summary>
    public readonly record struct RelicCandidate(string Id, int Sentinels, int SentinelsWithoutLord, bool Retake);

    /// <summary>Pure rule: a relic siege may start once at most <see cref="MaxSentinelsForRelicAssault"/> sentinel keeps stand.</summary>
    public static bool RelicKeepOpen(int sentinels) => sentinels <= MaxSentinelsForRelicAssault;

    /// <summary>Pure rule: a relic keep can be opened when the sentinel keeps that cannot change hands (no lord) are few
    /// enough on their own; every other held sentinel keep can be taken.</summary>
    public static bool RelicKeepOpenable(int sentinels, int sentinelsWithoutLord) =>
        Math.Min(sentinels, sentinelsWithoutLord) <= MaxSentinelsForRelicAssault;

    /// <summary>Pure rule: among open relic keeps, the realm's own lost relic first, then the fewest sentinels.</summary>
    public static string ChooseOpenRelic(IEnumerable<RelicCandidate> open) =>
        (open ?? []).Where(c => RelicKeepOpen(c.Sentinels))
            .OrderByDescending(c => c.Retake).ThenBy(c => c.Sentinels).ThenBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => c.Id).FirstOrDefault();

    /// <summary>Pure rule: the relic keep a realm sets out to open: one that can be opened and is not yet open, its own lost
    /// relic first, then the fewest sentinel keeps left.</summary>
    public static string ChooseRelicAim(IEnumerable<RelicCandidate> candidates) =>
        (candidates ?? []).Where(c => !RelicKeepOpen(c.Sentinels) && RelicKeepOpenable(c.Sentinels, c.SentinelsWithoutLord))
            .OrderByDescending(c => c.Retake).ThenBy(c => c.Sentinels).ThenBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => c.Id).FirstOrDefault();

    /// <summary>Pure rule: how a sentinel-keep step ended: taken when the keep is no longer its defender's.</summary>
    public static bool RelicStepTaken(eRealm defenderWhenOpened, eRealm ownerNow) =>
        ownerNow != eRealm.None && ownerNow != defenderWhenOpened;

    private sealed class RelicAim
    {
        public required string RelicId;
        public required string RelicName;
        public long DeadlineTick;
        public int StepsWithoutProgress;
        /// <summary>The sentinel keep whose siege this aim opened last, and who held it then.</summary>
        public string StepId;
        public eRealm StepDefender;
        /// <summary>The sentinel keep ChooseRelicSiege last named; it becomes the step once its siege really opens.</summary>
        public string PendingStepId;
        /// <summary>Sentinel keeps whose siege ended without a capture: not tried again for this aim (T3).</summary>
        public readonly HashSet<string> Skipped = new(StringComparer.Ordinal);
    }
    private static readonly Dictionary<eRealm, RelicAim> RelicAims = new();
    /// <summary>An aim that ended without its relic siege is not taken up again by that realm for a while.</summary>
    private static readonly Dictionary<(eRealm Realm, string RelicId), long> RelicAimCooldowns = new();
    /// <summary>Whether a keep has a lord (and so can change hands), cached for a minute per keep.</summary>
    private static readonly Dictionary<int, (bool HasLord, long Until)> KeepLordCache = new();
    public const long KeepLordCacheMilliseconds = 60_000;

    private static bool KeepCanChangeHands(DOL.GS.Keeps.AbstractGameKeep keep, long nowTick)
    {
        if (keep == null) return false;
        if (KeepLordCache.TryGetValue(keep.KeepID, out var cached) && cached.Until > nowTick && cached.Until - nowTick <= KeepLordCacheMilliseconds)
            return cached.HasLord;
        bool hasLord = keep.Guards.Values.OfType<DOL.GS.Keeps.GuardLord>().Any();
        KeepLordCache[keep.KeepID] = (hasLord, nowTick + KeepLordCacheMilliseconds);
        return hasLord;
    }

    private static int SentinelKeepsWithoutLord(int[] held, long nowTick) =>
        LiveKeeps is not { } keeps ? 0 : held.Count(id => !KeepCanChangeHands(keeps.GetKeepByID(id), nowTick));

    /// <summary>
    /// Called under Sync from ChooseCore once a realm with no attack under way may open a siege. Returns the relic keep to
    /// assault now (open, and one whose relic this realm may lift and place), or null; with an aim it may replace
    /// <paramref name="keep"/> (the ordinary keep choice) by the aim's next sentinel keep.
    /// </summary>
    private static LiveObjective ChooseRelicSiege(Force force, IReadOnlyCollection<LiveObjective> objectives, long nowTick, ref LiveObjective keep)
    {
        if (force == null || objectives == null) return null;
        var relicKeeps = objectives.Where(o => o.Kind == Intent.AssaultRelicKeep && o.IsRelicKeep && o.OwningRealm != force.Realm &&
                                               o.OwningRealm != eRealm.None)
            .Select(o => (Objective: o, Held: HeldSentinelKeeps(o), CanPlace: RelicRaidCanPlace(o, force.Realm)))
            .ToArray();
        // 1. An open relic keep is the reason the sentinel keeps were taken: it is the next siege.
        string openId = ChooseOpenRelic(relicKeeps
            .Where(r => r.CanPlace && !Events.ContainsKey(r.Objective.Id) && !OnCooldown(r.Objective.Id, nowTick))
            .Select(r => new RelicCandidate(r.Objective.Id, r.Held.Length, 0, HoldsRelicOf(r.Objective, force.Realm))));
        if (openId != null)
            return relicKeeps.First(r => r.Objective.Id == openId).Objective;
        // 2. Otherwise the realm's aim names the next sentinel keep.
        RelicAim aim = CurrentRelicAim(force.Realm, relicKeeps.Select(r => (r.Objective, r.Held, r.CanPlace)).ToArray(), nowTick);
        if (aim == null) return null;
        var target = relicKeeps.First(r => r.Objective.Id == aim.RelicId);
        LiveObjective step = objectives
            .Where(o => o.Kind == Intent.AssaultKeep && !o.IsRelicKeep && o.OwningRealm == target.Objective.OwningRealm &&
                        !Events.ContainsKey(o.Id) && !OnCooldown(o.Id, nowTick) && !aim.Skipped.Contains(o.Id) &&
                        target.Held.Any(id => o.Id == $"rvr-keep-{id}") &&
                        (LiveKeeps == null || KeepCanChangeHands(KeepOfObjective(o.Id), nowTick)))
            .OrderBy(o => (long)(o.X - target.Objective.X) * (o.X - target.Objective.X) + (long)(o.Y - target.Objective.Y) * (o.Y - target.Objective.Y))
            .FirstOrDefault();
        aim.PendingStepId = step?.Id;
        if (step != null) keep = step;
        return null;
    }

    /// <summary>The realm's relic aim after judging its last step, ending it or taking a new one up (under Sync).</summary>
    private static RelicAim CurrentRelicAim(eRealm realm, (LiveObjective Objective, int[] Held, bool CanPlace)[] relicKeeps, long nowTick)
    {
        var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
        foreach (var expired in RelicAimCooldowns.Where(pair => pair.Value <= nowTick).Select(pair => pair.Key).ToArray())
            RelicAimCooldowns.Remove(expired);
        if (RelicAims.TryGetValue(realm, out RelicAim aim))
        {
            var target = relicKeeps.FirstOrDefault(r => r.Objective.Id == aim.RelicId);
            string end = target.Objective == null ? "relic keep not available" : !target.CanPlace ? "no relic there this realm may lift" : null;
            // Judge the step this aim opened last; its siege has ended (the realm has no attack under way).
            if (end == null && aim.StepId != null && !Events.ContainsKey(aim.StepId))
            {
                var stepKeep = KeepOfObjective(aim.StepId);
                if (stepKeep != null && RelicStepTaken(aim.StepDefender, stepKeep.Realm))
                {
                    aim.StepsWithoutProgress = 0;
                    aim.DeadlineTick = nowTick + CampaignIdleMilliseconds;
                    if (log.IsInfoEnabled) log.Info($"RVR_RELIC_AIM_PROGRESS realm={realm} relic={aim.RelicId} step={aim.StepId} sentinels={target.Held.Length}");
                }
                else
                {
                    aim.StepsWithoutProgress++;
                    aim.Skipped.Add(aim.StepId);
                    if (log.IsInfoEnabled) log.Info($"RVR_RELIC_AIM_STEP_FAILED realm={realm} relic={aim.RelicId} step={aim.StepId} " +
                        $"failedSteps={aim.StepsWithoutProgress} sentinels={target.Held.Length}");
                }
                aim.StepId = null;
            }
            if (end == null && nowTick > aim.DeadlineTick) end = $"no progress for {CampaignIdleMilliseconds / 3_600_000d:0.#} hours";
            if (end == null && aim.StepsWithoutProgress >= CampaignMaxStepsWithoutProgress) end = $"no sentinel keep taken in {aim.StepsWithoutProgress} steps";
            if (end == null && !RelicKeepOpenable(target.Held.Length, SentinelKeepsWithoutLord(target.Held, nowTick)))
                end = "too many sentinel keeps cannot change hands";
            if (end == null) return aim;
            RelicAims.Remove(realm);
            RelicAimCooldowns[(realm, aim.RelicId)] = nowTick + CampaignIdleMilliseconds;
            if (log.IsInfoEnabled) log.Info($"RVR_RELIC_AIM_ENDED realm={realm} relic={aim.RelicId} reason=\"{end}\"");
        }
        // A launcher campaign of this realm leads; no second plan beside it.
        if (Campaigns.Values.Any(campaign => campaign.Attacker == realm)) return null;
        var candidates = relicKeeps
            .Where(r => r.CanPlace && !Events.ContainsKey(r.Objective.Id) && !Campaigns.ContainsKey(r.Objective.Id) &&
                        !RelicAimCooldowns.ContainsKey((realm, r.Objective.Id)) && !RelicKeepOpen(r.Held.Length))
            .Select(r => new RelicCandidate(r.Objective.Id, r.Held.Length, SentinelKeepsWithoutLord(r.Held, nowTick), HoldsRelicOf(r.Objective, realm)))
            .ToArray();
        string id = ChooseRelicAim(candidates);
        if (id == null) return null;
        var chosen = relicKeeps.First(r => r.Objective.Id == id);
        RelicAims[realm] = aim = new RelicAim { RelicId = id, RelicName = chosen.Objective.Name, DeadlineTick = nowTick + CampaignIdleMilliseconds };
        if (log.IsInfoEnabled) log.Info($"RVR_RELIC_AIM realm={realm} relic={id} (\"{chosen.Objective.Name}\") sentinels={chosen.Held.Length} " +
            $"withoutLord={candidates.First(c => c.Id == id).SentinelsWithoutLord}");
        return aim;
    }

    /// <summary>Called under Sync from ChooseCore when it opened an automatic siege: the aim's step, or the aim fulfilled.</summary>
    private static void NoteRelicSiegeOpened(eRealm realm, LiveObjective selected, long nowTick)
    {
        if (selected == null || !RelicAims.TryGetValue(realm, out RelicAim aim)) return;
        if (selected.Id == aim.RelicId)
        {
            RelicAims.Remove(realm);
            var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
            if (log.IsInfoEnabled) log.Info($"RVR_RELIC_AIM_SIEGE realm={realm} relic={aim.RelicId} (\"{aim.RelicName}\")");
            return;
        }
        if (selected.Id != aim.PendingStepId) return;
        aim.PendingStepId = null;
        aim.StepId = selected.Id;
        aim.StepDefender = selected.OwningRealm;
    }

    /// <summary>Sentinel keeps a launcher campaign tried without taking them (T3), kept with the campaign itself.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RelicCampaign, HashSet<string>> CampaignSkips = new();

    /// <summary>Pure rule (T3): a campaign step that ended with the keep still in its defender's hands is not tried again by
    /// that campaign; the 12-minute target cooldown used to send it back to the same lost siege every second step.</summary>
    public static bool SkipFailedStep(bool stepEventRunning, eRealm defender, eRealm ownerNow) =>
        !stepEventRunning && ownerNow != eRealm.None && ownerNow == defender;

    /// <summary>
    /// The next sentinel keep of a campaign (called under Sync from ForceStart): held by the relic keep's realm, not under
    /// siege, not on cooldown, able to change hands (it has a lord: a lordless keep is a four-hour siege that cannot be won,
    /// relic audit B1) and not already lost once by this campaign (T3); the nearest to the relic keep first.
    /// </summary>
    private static DOL.GS.Keeps.AbstractGameKeep NextRelicStep(LiveObjective relic, eRealm attacker, long now)
    {
        HashSet<string> skipped = null;
        if (Campaigns.TryGetValue(relic.Id, out RelicCampaign campaign) && campaign.Attacker == attacker)
        {
            skipped = CampaignSkips.GetOrCreateValue(campaign);
            if (campaign.StepId != null && KeepOfObjective(campaign.StepId) is { } last &&
                SkipFailedStep(Events.ContainsKey(campaign.StepId), relic.OwningRealm, last.Realm) && skipped.Add(campaign.StepId))
            {
                var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
                if (log.IsInfoEnabled) log.Info($"RVR_RELIC_CAMPAIGN_STEP_SKIPPED relic={relic.Id} attacker={attacker} step={campaign.StepId} reason=\"ended without a capture\"");
            }
        }
        return HeldSentinelKeeps(relic)
            .Select(id => LiveKeeps?.GetKeepByID(id))
            .Where(k => k != null && !Events.ContainsKey($"rvr-keep-{k.KeepID}") && !OnCooldown($"rvr-keep-{k.KeepID}", now) &&
                        KeepCanChangeHands(k, now) && skipped?.Contains($"rvr-keep-{k.KeepID}") != true)
            .OrderBy(k => (long)(k.X - relic.X) * (k.X - relic.X) + (long)(k.Y - relic.Y) * (k.Y - relic.Y))
            .FirstOrDefault();
    }

    /// <summary>The relic keep a realm's automatic plan is working toward, or null (tests, dashboard).</summary>
    public static string RelicAimOf(eRealm realm)
    {
        lock (Sync) return RelicAims.TryGetValue(realm, out RelicAim aim) ? aim.RelicId : null;
    }

    /// <summary>Whether a relic campaign for this relic keep is open (tests, dashboard).</summary>
    public static bool HasRelicCampaign(string relicKeepId)
    {
        lock (Sync) return Campaigns.ContainsKey(relicKeepId);
    }

    /// <summary>One relic raid in progress, as saved across a restart.</summary>
    /// <param name="Source">"campaign" (taking the sentinel keeps), "siege" (the relic siege itself) or "carrier" (the relic was
    /// being carried home: after a restart it is back on its pad, see AutonomousRvrRelicPersistence).</param>
    public sealed record RelicState(string RelicKeepId, eRealm Attacker, string Source, long IdleRemainingMilliseconds, int StepsWithoutProgress);

    /// <summary>The relic raids in progress: open campaigns, running relic sieges and relic carrier events.</summary>
    public static RelicState[] RelicStateSnapshot(long nowTick)
    {
        lock (Sync)
        {
            var result = new List<RelicState>();
            var seen = new HashSet<(string, eRealm)>();
            void Add(RelicState state)
            {
                if ((state.Attacker is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) && seen.Add((state.RelicKeepId, state.Attacker)))
                    result.Add(state);
            }
            foreach ((string id, RelicCampaign campaign) in Campaigns)
                Add(new(id, campaign.Attacker, "campaign", Math.Max(0, campaign.DeadlineTick - nowTick), campaign.StepsWithoutProgress));
            foreach (ActiveEvent active in Events.Values.Where(e => e.RelicKeep && !e.DefenseReaction && !e.Retake))
                Add(new(active.TargetId, active.AttackerRealm, "siege", CampaignIdleMilliseconds, 0));
            foreach (var origin in CarrierOrigins.Values)
                Add(new(origin.KeepId, origin.Attacker, "carrier", CampaignIdleMilliseconds, 0));
            return result.ToArray();
        }
    }

    /// <summary>
    /// Re-plans saved relic raids after a restart (B5): each becomes a relic campaign that continues after
    /// <paramref name="resumeDelayMilliseconds"/> (the next sentinel keep, or the relic siege itself), with a fresh muster.
    /// </summary>
    public static int RestoreRelicState(IEnumerable<RelicState> states, long nowTick, long resumeDelayMilliseconds)
    {
        int restored = 0;
        foreach (RelicState state in states ?? Array.Empty<RelicState>())
        {
            if (state?.RelicKeepId == null || !state.RelicKeepId.StartsWith("rvr-keep-", StringComparison.Ordinal) ||
                !int.TryParse(state.RelicKeepId["rvr-keep-".Length..], out int keepId)) continue;
            var keep = GameServer.KeepManager?.GetKeepByID(keepId);
            if (keep == null || !keep.IsRelic) continue;
            if (RestoreRelicCampaign(ObjectiveForKeep(keep), state.Attacker, nowTick, resumeDelayMilliseconds,
                    state.IdleRemainingMilliseconds, state.StepsWithoutProgress, state.Source))
                restored++;
        }
        return restored;
    }

    public static bool RestoreRelicCampaign(LiveObjective relic, eRealm attacker, long nowTick, long resumeDelayMilliseconds,
        long idleRemainingMilliseconds, int stepsWithoutProgress, string source)
    {
        if (relic?.IsRelicKeep != true || relic.OwningRealm == attacker ||
            attacker is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia)) return false;
        lock (Sync)
        {
            if (Campaigns.ContainsKey(relic.Id)) return false;
            // L3: a raid saved before its realm lost its own relic of that kind cannot lift anything there any more.
            if (!RelicRaidCanPlace(relic, attacker)) return false;
            int held = HeldSentinelKeeps(relic).Length;
            Campaigns[relic.Id] = new RelicCampaign
            {
                Relic = relic, Attacker = attacker,
                ContinueAt = nowTick + Math.Max(1, resumeDelayMilliseconds),
                // The downtime does not count; a raid saved just before giving up still gets one more try.
                DeadlineTick = nowTick + Math.Max(idleRemainingMilliseconds, CampaignRetryMaximumMilliseconds) + resumeDelayMilliseconds,
                StepsWithoutProgress = Math.Clamp(stepsWithoutProgress, 0, CampaignMaxStepsWithoutProgress - 1),
                LastHeld = held,
            };
            var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
            if (log.IsInfoEnabled) log.Info($"RVR_RELIC_CAMPAIGN_RESUMED relic={relic.Id} (\"{relic.Name}\") attacker={attacker} source={source} " +
                $"sentinels={held} resumeMinutes={resumeDelayMilliseconds / 60_000d:0.#}");
            return true;
        }
    }
}
