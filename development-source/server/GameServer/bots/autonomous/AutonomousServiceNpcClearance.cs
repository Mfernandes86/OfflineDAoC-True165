using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// GitHub #48 (2026-11-12): bots stood on and around stable masters, merchants, trainers, healers and vault
    /// keepers, so the player could not click them. NPCs are not in the navmesh, so nothing kept a bot off them.
    /// These rules keep the spots bots choose for meetups, landings and town breaks clear of the NPCs players use.
    /// Every caller decides once (a party forms, a rider lands, a break starts), never per tick.
    /// </summary>
    public static class AutonomousServiceNpcClearance
    {
        /// <summary>A party meetup centre stays at least this far from a service NPC (its formation ring is 64 + 3n
        /// across, so members stand 200+ units clear).</summary>
        public const float MeetClearRadius = 300f;

        /// <summary>The outer edge of the band a meetup centre beside a service NPC is taken from.</summary>
        public const float MeetOuterRadius = 450f;

        /// <summary>A town break spot stays at least this far from a service NPC.</summary>
        public const float TownBreakClearRadius = 250f;

        /// <summary>The NPCs players walk up to and click: merchants (stable masters are merchants), trainers,
        /// healers, vault keepers and the realm exchange. A gamebot is never one.</summary>
        public static bool IsServiceNpc(GameNPC npc) =>
            npc != null && npc is not GameBot &&
            npc is GameMerchant or GameTrainer or GameHealer or GameVaultKeeper or RealmExchangeBroker;

        /// <summary>Pure: true when no position in <paramref name="services"/> lies within <paramref name="radius"/>
        /// (across the ground, ignoring height within 300) of <paramref name="point"/>.</summary>
        public static bool IsClearOf(Vector3 point, IEnumerable<Vector3> services, float radius)
        {
            if (services == null)
                return true;
            foreach (Vector3 service in services)
                if (MathF.Abs(service.Z - point.Z) <= 300 &&
                    Vector2.DistanceSquared(new(point.X, point.Y), new(service.X, service.Y)) < radius * radius)
                    return false;
            return true;
        }

        /// <summary>The positions of the service NPCs within <paramref name="radius"/> of <paramref name="near"/>
        /// (one radius query; called once when a party forms or a break spot is chosen).</summary>
        public static List<Vector3> ServicePositionsNear(GameObject near, ushort radius)
        {
            var found = new List<Vector3>();
            if (near?.CurrentRegion == null)
                return found;
            foreach (GameNPC npc in near.GetNPCsInRadius(radius))
                if (npc.ObjectState == GameObject.eObjectState.Active && IsServiceNpc(npc))
                    found.Add(new(npc.X, npc.Y, npc.Z));
            return found;
        }
    }
}
