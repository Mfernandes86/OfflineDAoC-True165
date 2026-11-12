using System;
using System.Numerics;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private long _expeditionRouteRetry;
    private long _expeditionOrdinaryUntil;
    private int _expeditionRouteFailures;
    private Vector3[] _expeditionSeams;
    private int _expeditionLeg;
    private string _expeditionEvent;
    private ushort _expeditionRegion;
    private Vector3 _expeditionGoal;
    private Vector3? _expeditionLastPosition;
    private RvrPlanningNavigation _dragonRallyPlanning;
    private Vector3[] _dragonRallyRoute;
    private Vector3 _dragonRallyOrigin, _dragonRallyGoal;
    private int _dragonRallyLeg;
    private string _dragonRallyEvent;
    private long _dragonRallyRetry;
    private int _dragonRallyFailures;
    private Vector3? _dragonRallyFallback;
    private long _dragonRallyFallbackUntil;
    private bool TravelRealmExpedition(GameBot bot, AutonomousRealmRaid.View order)
    {
        if (GameLoop.GameLoopTime < _expeditionRouteRetry) return true;
        var c = order.Camp;
        if (_camp == null || _camp.RegionId != c.RegionId || _camp.X != c.X || _camp.Y != c.Y || _camp.Z != c.Z)
            _camp = FromSharedCamp(c);
        if (AutonomousRealmRaid.TryPendingDragonRally(bot,out var home) && bot.CurrentRegionID == c.RegionId &&
            Vector2.DistanceSquared(new(bot.X,bot.Y),new(home.X,home.Y)) <= DragonRallyRoute.ApproachRadius*DragonRallyRoute.ApproachRadius &&
            Vector2.DistanceSquared(new(c.X,c.Y),new(home.X,home.Y)) <= DragonRallyRoute.ApproachRadius*DragonRallyRoute.ApproachRadius)
            return TravelDragonRally(bot,order,home);
        _dragonRallyPlanning=null; _dragonRallyRoute=null; _dragonRallyEvent=null;
        // A raider coming back after the raid went in gathers outside and goes back in with a wave (see
        // AutonomousRealmRaid.Reinforcements).
        if (AutonomousRealmRaid.TryReinforcementHold(bot, out var wave))
            return TravelToReinforcementWave(bot, order, wave);
        if (AutonomousRealmRaid.TryIndependentApproach(bot, out var exterior, out var via) &&
            FollowExpeditionCorridor(bot, order, exterior, via, "Traveling to expedition",
                "Following the validated exterior corridor; no town attendance required"))
            return true;
        // Released/late members own their trip to the encounter. No follower
        // gate, all-eight meetup or dead leader can hold them in another region.
        if (bot.CurrentRegionID != c.RegionId) return TravelAcrossRegions(bot);
        Vector3 post = new(c.X,c.Y,c.Z);
        int arrival = order.Crossing ? 32 : 175;
        if (Vector3.DistanceSquared(new(bot.X,bot.Y,bot.Z),post) > arrival * arrival)
        {
            if (FollowDarknessFallsRaidChain(bot, post, c.MonsterName)) return true;
            if (!TryBeginFasterStableRoute(bot,post,c.ZoneName))
                IssuePath(bot,post,preciseArrival:order.Crossing);
            SetStatus(bot,order.State,c.MonsterName,"Traveling to the expedition; late members rejoin without town staging");
            return true;
        }
        if (!order.Hold) return false;
        bot.StopMovingOnPath();
        bot.StopMoving();
        SetStatus(bot,order.State,c.MonsterName,"Present at expedition staging; waiting for the expedition order");
        return true;
    }

    /// <summary>A trip this long on foot first asks the stable planner for a faster ride (late and returning raiders
    /// used to walk the whole way; audits 2026-11-11). Raiders already at the fight never ask.</summary>
    public const float ExpeditionRideDistance = 16_000;
    public static bool ShouldTryExpeditionRide(float remaining) => remaining > ExpeditionRideDistance;

    // Walks the validated exterior corridor (zone seams) toward goal. True while it is handling the trip (walking,
    // riding, retrying); false once the corridor is done, or while ordinary travel has taken over after failures.
    private bool FollowExpeditionCorridor(GameBot bot, AutonomousRealmRaid.View order, Vector3 goal, Vector3? via,
        string state, string detail)
    {
        // After repeated exterior-corridor failures from one spot (a stable landing whose
        // corridor never proves, Vindsaul Faste 2026-10-06) the ordinary region travel
        // takes over for a while; it has the usual route and pocket recovery.
        if (GameLoop.GameLoopTime < _expeditionOrdinaryUntil) return false;
        var c = order.Camp;
        Vector3 position = new(bot.X, bot.Y, bot.Z);
        if (_expeditionEvent != order.EventId || _expeditionRegion != bot.CurrentRegionID ||
            Vector3.DistanceSquared(goal, _expeditionGoal) > 512 * 512 ||
            _expeditionLastPosition.HasValue && Vector3.DistanceSquared(position, _expeditionLastPosition.Value) > 2048 * 2048)
        {
            _expeditionSeams = null;
            _expeditionEvent = order.EventId; _expeditionRegion = bot.CurrentRegionID; _expeditionGoal = goal;
        }
        _expeditionLastPosition = position;
        if (ShouldTryExpeditionRide(Vector3.Distance(position, goal)) && TryBeginFasterStableRoute(bot, goal, c.ZoneName))
        {
            SetStatus(bot, state, c.MonsterName, "Riding toward the expedition; a stable route is faster than walking");
            return true;
        }
        if (_expeditionSeams == null)
        {
            if (!RealmRaidMuster.TryRoute(bot.CurrentRegion, PathfindingProvider.Instance, bot.Realm,
                position, goal, out _expeditionSeams, via))
            {
                _expeditionSeams = null;
                if (++_expeditionRouteFailures >= 4)
                {
                    _expeditionRouteFailures = 0;
                    _expeditionOrdinaryUntil = GameLoop.GameLoopTime + 120_000;
                    Log.Warn($"REALM_EXPEDITION_ORDINARY_TRAVEL bot={bot.Name} id={bot.DatabaseID} region={bot.CurrentRegionID} " +
                        $"position={bot.X},{bot.Y},{bot.Z} event={order.EventId}");
                    return true;
                }
                _expeditionRouteRetry = GameLoop.GameLoopTime + 15_000;
                bot.StopMovingOnPath(); bot.StopMoving();
                SetStatus(bot, "Expedition route retry", c.MonsterName, "No connected exterior corridor; retaining assignment and retrying safely");
                return true;
            }
            _expeditionRouteFailures = 0;
            _expeditionLeg = 0;
        }
        while (_expeditionLeg < _expeditionSeams.Length &&
            Vector3.DistanceSquared(position, _expeditionSeams[_expeditionLeg]) <= 64 * 64)
            _expeditionLeg++;
        if (_expeditionLeg >= _expeditionSeams.Length) return false;
        if (!IssuePath(bot, _expeditionSeams[_expeditionLeg], preciseArrival: true))
        { _expeditionSeams = null; _expeditionRouteRetry = GameLoop.GameLoopTime + 15_000; }
        SetStatus(bot, state, c.MonsterName, detail);
        return true;
    }

    private bool TravelToReinforcementWave(GameBot bot, AutonomousRealmRaid.View order, AutonomousRealmRaid.ReinforcementOrder wave)
    {
        string name = order.Camp.MonsterName;
        if (wave.Arrived)
        {
            bot.StopMovingOnPath(); bot.StopMoving();
            SetStatus(bot, "Waiting for reinforcements", name,
                $"Gathered outside with {wave.Gathered} of {Math.Min(wave.Expected, AutonomousRealmRaid.ReinforcementWaveSize)}; going back in together");
            return true;
        }
        const string state = "Joining reinforcements", detail = "Going to the raid's gathering point to go back in with the others";
        if (FollowExpeditionCorridor(bot, order, wave.Point, null, state, detail)) return true;
        if (!TryBeginFasterStableRoute(bot, wave.Point, order.Camp.ZoneName))
            IssuePath(bot, wave.Point, preciseArrival: false);
        SetStatus(bot, state, name, detail);
        return true;
    }

    private bool TravelDragonRally(GameBot bot, AutonomousRealmRaid.View order, Vector3 home)
    {
        long now=GameLoop.GameLoopTime;
        Vector3 current=new(bot.X,bot.Y,bot.Z), goal=new(order.Camp.X,order.Camp.Y,order.Camp.Z);
        if (_dragonRallyEvent!=order.EventId || _dragonRallyGoal!=goal ||
            _dragonRallyPlanning!=null && Vector3.DistanceSquared(current,_dragonRallyOrigin)>96*96)
        {
            _dragonRallyEvent=order.EventId; _dragonRallyGoal=goal;
            _dragonRallyPlanning=null; _dragonRallyRoute=null; _dragonRallyRetry=0;
            _dragonRallyFailures=0; _dragonRallyFallback=null;
        }
        if(_dragonRallyFallback is Vector3 fallback)
        {
            if(now<_dragonRallyFallbackUntil && Vector3.DistanceSquared(current,fallback)>300*300 && IssuePath(bot,fallback,preciseArrival:false))
            {
                SetStatus(bot,"Closing in on the dragon rally",order.Camp.MonsterName,"No checked route from here; walking to the ring outside the dragon's reach to plan again");
                return true;
            }
            _dragonRallyFallback=null; _dragonRallyRetry=0;
        }
        if(now<_dragonRallyRetry) return true;
        if(_dragonRallyRoute==null)
        {
            if(_dragonRallyPlanning==null)
            {
                bot.StopMovingOnPath();bot.StopMoving();
                _dragonRallyOrigin=current;
                _dragonRallyPlanning=new RvrPlanningNavigation(PathfindingProvider.Instance);
            }
            var work=_dragonRallyPlanning; work.BeginSlice();
            bool found=false;
            try
            {
                found=DragonRallyRoute.TryBuild(work,bot.CurrentZone,_dragonRallyOrigin,goal,home,out var route);
                if(found) _dragonRallyRoute=route;
            }
            catch(RvrPlanningNavigation.Yield)
            { _dragonRallyRetry=now+250; SetStatus(bot,"Planning safe dragon rally route",order.Camp.MonsterName,"Routing around the dragon; normal defense remains active"); return true; }
            catch(RvrPlanningNavigation.Limit) { }
            finally { work.EndSlice(); }
            _dragonRallyPlanning=null;
            if(!found)
            {
                _dragonRallyRoute=null;_dragonRallyRetry=now+30_000;
                if(DragonRallyRoute.UseFallback(++_dragonRallyFailures) &&
                   PathfindingProvider.Instance.GetClosestPoint(bot.CurrentZone,DragonRallyRoute.FallbackRingPoint(current,home),96,96,4096,
                       PathfindingProvider.Instance.DefaultFilters) is Vector3 ring && DragonRallyRoute.Outside(ring,home))
                {
                    _dragonRallyFailures=0;_dragonRallyFallback=ring;_dragonRallyFallbackUntil=now+180_000;_dragonRallyRetry=0;
                    Log.Warn($"DRAGON_RALLY_ROUTE_FALLBACK bot={bot.Name} event={order.EventId} from={current} ring={ring}");
                    return true;
                }
                SetStatus(bot,"Dragon rally route retry",order.Camp.MonsterName,"No verified approach outside dragon aggro; not charging through the boss");
                Log.Warn($"DRAGON_RALLY_ROUTE_RETRY bot={bot.Name} event={order.EventId} from={current} goal={goal} queries={work.Queries}");
                return true;
            }
            _dragonRallyLeg=0;
            _dragonRallyFailures=0;
        }
        while(_dragonRallyLeg<_dragonRallyRoute.Length && Vector3.DistanceSquared(current,_dragonRallyRoute[_dragonRallyLeg])<=80*80)
            _dragonRallyLeg++;
        if(_dragonRallyLeg==_dragonRallyRoute.Length)
        {
            bot.StopMovingOnPath();bot.StopMoving();
            SetStatus(bot,order.State,order.Camp.MonsterName,"At safe dragon staging; waiting for the expedition order");
            return true;
        }
        if(!IssuePath(bot,_dragonRallyRoute[_dragonRallyLeg],preciseArrival:true))
        { _dragonRallyRoute=null;_dragonRallyRetry=now+15_000;bot.StopMovingOnPath();bot.StopMoving(); }
        else SetStatus(bot,"Traveling to dragon rally",order.Camp.MonsterName,"Following a checked route outside the dragon's aggro area");
        return true;
    }

    // Consecutive expedition route failures from (nearly) the same floor point.
    // A member on a disconnected navmesh island (a ledge, a fall, a bad respawn
    // landing) cannot path to any route stage; it used to retry every 30 seconds
    // for the whole four-hour expedition.
    public const int StrandedExpeditionFailureLimit = 3;
    public const float StrandedExpeditionRadius = 400;
    private int _strandedExpeditionFailures;
    private Vector3 _strandedExpeditionPosition;

    public static bool ShouldRegroupStrandedMember(int failures, bool alive, bool inCombat) =>
        alive && !inCombat && failures >= StrandedExpeditionFailureLimit;

    private void TryRegroupStrandedExpeditionMember(GameBot bot)
    {
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        _strandedExpeditionFailures = Vector2.DistanceSquared(new(here.X, here.Y),
            new(_strandedExpeditionPosition.X, _strandedExpeditionPosition.Y)) <= StrandedExpeditionRadius * StrandedExpeditionRadius
            ? _strandedExpeditionFailures + 1 : 1;
        _strandedExpeditionPosition = here;
        if (!ShouldRegroupStrandedMember(_strandedExpeditionFailures, bot.IsAlive, bot.InCombat) ||
            !AutonomousRealmRaid.TryDungeonRegroupPoint(bot, out ushort region, out Vector3 front) ||
            Vector3.DistanceSquared(here, front) <= 300 * 300)
            return;
        _strandedExpeditionFailures = 0;
        if (!bot.MoveInRegion(region, (int)front.X, (int)front.Y, (int)front.Z, bot.Heading, true))
            return;
        _expeditionRouteRetry = 0;
        AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Recovery);
        Log.Warn($"REALM_EXPEDITION_STRANDED_REGROUP bot={bot.Name} id={bot.DatabaseID} region={region} " +
                 $"from={(int)here.X},{(int)here.Y},{(int)here.Z} to={(int)front.X},{(int)front.Y},{(int)front.Z}");
    }
}
