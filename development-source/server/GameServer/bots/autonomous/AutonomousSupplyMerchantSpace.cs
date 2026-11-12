using System;
using System.Numerics;

namespace DOL.GS
{
    public static class AutonomousSupplyMerchantSpace
    {
        public const int ClearRadius = 100;

        /// <summary>
        /// Interaction spots are resolved only this close to the merchant; farther away the bot walks toward it.
        /// Issue #25 (2026-10-27): since a distant warband walks to its medallion merchant (7ef2d66), each bot
        /// 20,000-110,000 units out ran up to 18 multi-zone HasRoute probes every 15 seconds, all failing, and
        /// held NpcService ticks at 1-1.3 s. Up close the probes are single-zone corridor checks.
        /// </summary>
        public const int SpotSearchRange = 2_000;

        /// <summary>A failed search (all 18 candidates) waits this long before the next one.</summary>
        public const int RetryAfterFailureMs = 30_000;

        /// <summary>Whether to look for an interaction spot now. Pure.</summary>
        public static bool ShouldResolveSpot(float distanceToMerchant) => distanceToMerchant <= SpotSearchRange;

        public static bool IsInteractionSpot(Vector3 center, Vector3 position, int interactionRadius)
        {
            return float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
                Vector2.DistanceSquared(new(center.X, center.Y), new(position.X, position.Y)) >= ClearRadius * ClearRadius &&
                Vector3.DistanceSquared(center, position) <= interactionRadius * interactionRadius;
        }

        public static Vector3 Offset(long id, int attempt, int interactionRadius)
        {
            float radius = Math.Min(interactionRadius - 32, 160 + attempt % 3 * 24);
            float angle = ((unchecked((ulong)id * 2654435761UL) % 360) + attempt * 137.50776f) * MathF.PI / 180;
            return new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
        }

        /// <summary>GitHub #48: a bot walking to its own spot beside an ordinary service gives up the walk after this
        /// long and uses the service where it stands (it is already inside the interaction range).</summary>
        public const int SpotWalkLimitMs = 20_000;

        /// <summary>Pure: the bot was last asked for its spot at this NPC long enough ago that this is another visit.</summary>
        public static bool IsNewVisit(long now, long lastAsked) => now - lastAsked > NewVisitGapMs;

        /// <summary>A service stop asks every AI turn while it lasts; a gap this long means the bot left and came back.</summary>
        public const int NewVisitGapMs = 60_000;

        /// <summary>How close to its own spot counts as there.</summary>
        public const int SpotArrivalRadius = 32;

        /// <summary>Pure: the bot stands at its resolved spot (across the ground) and the spot is a valid one.</summary>
        public static bool IsAtOwnSpot(Vector3 center, Vector3 spot, Vector3 position, int interactionRadius) =>
            Vector2.DistanceSquared(new(spot.X, spot.Y), new(position.X, position.Y)) <= SpotArrivalRadius * SpotArrivalRadius &&
            IsInteractionSpot(center, position, interactionRadius);

        public static bool TryResolve(IPathfindingMgr nav, Region region, eRealm realm, Vector3 actor,
            Vector3 center, long botId, int interactionRadius, out Vector3 point) =>
            TryResolve(nav, region, realm, actor, center, botId, interactionRadius, null, out point);

        /// <summary>As above; <paramref name="accept"/> can reject a spot (the realm exchange wants a clear view of
        /// its broker).</summary>
        public static bool TryResolve(IPathfindingMgr nav, Region region, eRealm realm, Vector3 actor,
            Vector3 center, long botId, int interactionRadius, Func<Vector3, bool> accept, out Vector3 point)
        {
            point = default;
            Zone zone = region?.GetZone((int)center.X, (int)center.Y);
            if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone) || interactionRadius <= ClearRadius + 32) return false;
            for (int attempt = 0; attempt < 18; attempt++)
            {
                Vector3 desired = center + Offset(botId, attempt, interactionRadius);
                Vector3? floor = nav.GetClosestPoint(zone, desired, 24, 24, 128, nav.DefaultFilters);
                if (!floor.HasValue || !IsInteractionSpot(center, floor.Value, interactionRadius - 8) ||
                    accept != null && !accept(floor.Value) ||
                    !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value) ||
                    !AutonomousRvrRally.HasRoute(region, nav, realm, actor, floor.Value)) continue;
                point = floor.Value;
                return true;
            }
            return false;
        }
    }

    public sealed partial class AutonomousWorldBotController
    {
        private GameNPC _supplySpaceMerchant;
        private Vector3 _supplySpaceCenter;
        private Vector3? _supplySpacePoint;
        private Zone _supplySpaceOriginZone;
        private long _nextSupplySpaceSearch;
        private long _supplySpaceWalkUntil;
        private bool _supplySpaceGaveUp;
        private long _supplySpaceLastAsked;

        // Returns true only when a real transaction is in range and the bot
        // is clear of the NPC. Cached per visit; no population scan per AI turn.
        private bool ApproachSupplyMerchant(GameBot bot, GameMerchant merchant) => ApproachServiceSpot(bot, merchant, strict: true);

        /// <summary>
        /// strict (frontier supply, medallion and siege merchants): the bot waits for a spot clear of the merchant.
        /// Otherwise (GitHub #48: every merchant, stable masters included, healers and the exchange reached through
        /// TryRouteToNeededService): the bot, already inside the interaction range, walks once to its own spot 100+
        /// units from the NPC (angle from its id) instead of standing on the shared approach point; with no spot
        /// found, or not reached within 20 s, it uses the service where it stands, as before. One spot search per
        /// visit.
        /// </summary>
        private bool ApproachServiceSpot(GameBot bot, GameNPC merchant, bool strict)
        {
            Vector3 center = new(merchant.X, merchant.Y, merchant.Z);
            int radius = ServerProperties.Properties.WORLD_PICKUP_DISTANCE;
            Vector3 position = new(bot.X, bot.Y, bot.Z);
            if (strict && AutonomousSupplyMerchantSpace.IsInteractionSpot(center, position, radius))
            {
                bot.StopMovingOnPath();
                bot.StopMoving();
                return true;
            }
            if (_supplySpaceMerchant != merchant || _supplySpaceCenter != center || _supplySpaceOriginZone != bot.CurrentZone)
            {
                _supplySpaceMerchant = merchant;
                _supplySpaceCenter = center;
                _supplySpaceOriginZone = bot.CurrentZone;
                _supplySpacePoint = null;
                _nextSupplySpaceSearch = 0;
                _supplySpaceWalkUntil = 0;
                _supplySpaceGaveUp = false;
            }
            if (!strict)
            {
                // A new visit to the same NPC starts over: the spot is kept, the walk limit and the "use it where I
                // stand" answer are not (they were per bot for good, so only a bot's first visit used its own spot).
                long asked = GameLoop.GameLoopTime;
                if (AutonomousSupplyMerchantSpace.IsNewVisit(asked, _supplySpaceLastAsked))
                {
                    _supplySpaceGaveUp = false;
                    _supplySpaceWalkUntil = asked + AutonomousSupplyMerchantSpace.SpotWalkLimitMs;
                }
                _supplySpaceLastAsked = asked;
                if (_supplySpaceGaveUp)
                    return true;
                if (!_supplySpacePoint.HasValue)
                {
                    if (!AutonomousSupplyMerchantSpace.TryResolve(PathfindingProvider.Instance, bot.CurrentRegion, bot.Realm,
                            position, center, bot.DatabaseID, radius,
                            merchant is RealmExchangeBroker
                                ? AutonomousZonePointApproach.ClearSightOf(PathfindingProvider.Instance, merchant.CurrentZone, center)
                                : null, out Vector3 own))
                    {
                        _supplySpaceGaveUp = true;
                        return true;
                    }
                    _supplySpacePoint = own;
                    _supplySpaceWalkUntil = GameLoop.GameLoopTime + AutonomousSupplyMerchantSpace.SpotWalkLimitMs;
                }
                if (AutonomousSupplyMerchantSpace.IsAtOwnSpot(center, _supplySpacePoint.Value, position, radius))
                {
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    return true;
                }
                if (GameLoop.GameLoopTime >= _supplySpaceWalkUntil)
                {
                    _supplySpaceGaveUp = true;
                    return true;
                }
                IssuePath(bot, _supplySpacePoint.Value, preciseArrival: true);
                return false;
            }
            int distance = bot.GetDistanceTo(merchant);
            if (!_supplySpacePoint.HasValue && AutonomousSupplyMerchantSpace.ShouldResolveSpot(distance) &&
                GameLoop.GameLoopTime >= _nextSupplySpaceSearch)
            {
                if (AutonomousSupplyMerchantSpace.TryResolve(PathfindingProvider.Instance, bot.CurrentRegion, bot.Realm,
                    new(bot.X, bot.Y, bot.Z), center, bot.DatabaseID, radius, out var point)) _supplySpacePoint = point;
                else
                    _nextSupplySpaceSearch = GameLoop.GameLoopTime + AutonomousSupplyMerchantSpace.RetryAfterFailureMs;
            }
            if (_supplySpacePoint.HasValue)
                IssuePath(bot, _supplySpacePoint.Value, preciseArrival: true);
            else if (!AutonomousSupplyMerchantSpace.ShouldResolveSpot(distance))
                IssuePath(bot, center); // no interaction spot resolved from far away yet: head toward the merchant, never stand still
            else
            {
                bot.StopMovingOnPath();
                bot.StopMoving();
            }
            return false;
        }
    }
}
