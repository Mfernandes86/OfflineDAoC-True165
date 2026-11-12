using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DOL.GS;

/// <summary>
/// Ram crews as a player raid manned them (keeps audit 2026-11-11, L5; owner: "make sure keep doors are handled like a
/// player RvR raid would handle them"). A ram's reload and damage depend on the crew at it (GameSiegeRam.Crew: riders,
/// or friendly gamebots within 300, up to its 12 places), and the bots stood at a ram only by accident: a ram alone needs
/// about 77 minutes for a 50,000 hit point door and dies of decay after 28, a full crew needs 5. Now a raid seats a crew
/// on purpose: every working ram of the raid's realm at a raid door of the target keep is registered by its operator,
/// and melee fighters of the army who hold no siege job take a seat at the ram with the most empty places, walk to their
/// own spot behind it and stay there; they fight only what strikes them in melee reach, as the operator does. Casters and
/// archers keep shooting the walls and defenders, healers heal, the other melee batter the door by hand.
/// Cost: a lock and a dictionary per ram; a seat lookup per crew turn; no area scans.
/// </summary>
public static class AutonomousRamCrews
{
    public const long SeatLeaseMilliseconds = 20_000;
    /// <summary>A crew spot lies this far behind the ram (GameSiegeRam.Crew counts bots within 300 of it).</summary>
    public const int CrewInnerRadius = 120, CrewRowDepth = 80;
    /// <summary>A raid brings its second ram for a door once it can crew both (two rams per door at most, GameSiegeRam).</summary>
    public const int SecondRamArmy = 16;

    /// <summary>Pure rule: rams a raid of this many bots at the keep brings for one door: none under four, one for a small
    /// raid that can crew only one, two (the most a door takes) for a raid that can crew both.</summary>
    public static int RamsForDoor(int present) => present < 4 ? 0 : present < SecondRamArmy ? 1 : 2;

    /// <summary>Pure rule: who crews a ram: a melee fighter. Casters and archers shoot the walls and defenders; the healers
    /// (support classes and shamans) keep the crew alive.</summary>
    public static bool IsCrewCandidate(bool rangedOrCaster, bool mainHealer) => !rangedOrCaster && !mainHealer;

    /// <summary>Pure rule: the seat's spot, behind the ram on the side away from the door, in rows of six across a 125-degree
    /// arc, every spot within the 300 the ram counts its crew in.</summary>
    public static Vector2 CrewSpot(Vector2 ram, Vector2 door, int seat)
    {
        Vector2 away = ram - door;
        away = away.LengthSquared() < 1 ? new Vector2(1, 0) : Vector2.Normalize(away);
        int row = Math.Max(0, seat) / 6, column = Math.Max(0, seat) % 6;
        float radius = CrewInnerRadius + row % 2 * CrewRowDepth;
        double angle = Math.Atan2(away.Y, away.X) + (column - 2.5) * (25 * Math.PI / 180) + row / 2 * 0.1;
        return ram + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
    }

    private sealed class Crew
    {
        public string KeepTargetId;
        public readonly Dictionary<int, (WeakReference<GameBot> Bot, long Until)> Seats = new();
    }

    private static readonly object Gate = new();
    private static readonly ConditionalWeakTable<GameSiegeRam, Crew> Crews = new();
    private static readonly Dictionary<string, List<WeakReference<GameSiegeRam>>> RamsByKeep = new(StringComparer.Ordinal);
    private const string SeatKey = "RamCrewSeat";

    public static bool Working(GameSiegeRam ram, eRealm realm) =>
        ram != null && ram.IsAlive && ram.ObjectState == GameObject.eObjectState.Active && ram.Realm == realm &&
        ram.Owner != null && ram.Health > ram.DecayedHp;

    /// <summary>The operator's turn: its ram works for the raid on this keep.</summary>
    public static void Register(string keepTargetId, GameSiegeRam ram)
    {
        if (string.IsNullOrEmpty(keepTargetId) || ram == null) return;
        lock (Gate)
        {
            Crew crew = Crews.GetValue(ram, _ => new Crew());
            if (crew.KeepTargetId == keepTargetId) return;
            crew.KeepTargetId = keepTargetId;
            if (!RamsByKeep.TryGetValue(keepTargetId, out var rams)) RamsByKeep[keepTargetId] = rams = new();
            rams.RemoveAll(entry => !entry.TryGetTarget(out GameSiegeRam known) || known == ram ||
                                    known.ObjectState != GameObject.eObjectState.Active);
            rams.Add(new(ram));
        }
    }

    /// <summary>A free seat at the raid's working ram with the most empty places (the nearest on a tie).</summary>
    public static bool TryTakeSeat(GameBot bot, string keepTargetId, out GameSiegeRam ram, out int seat)
    {
        ram = null; seat = -1;
        if (bot == null || string.IsNullOrEmpty(keepTargetId)) return false;
        long now = GameLoop.GameLoopTime;
        lock (Gate)
        {
            if (!RamsByKeep.TryGetValue(keepTargetId, out var rams)) return false;
            rams.RemoveAll(entry => !entry.TryGetTarget(out GameSiegeRam known) || known.ObjectState != GameObject.eObjectState.Active);
            if (rams.Count == 0) { RamsByKeep.Remove(keepTargetId); return false; }
            int bestFree = 0;
            long bestDistance = long.MaxValue;
            foreach (var entry in rams)
            {
                if (!entry.TryGetTarget(out GameSiegeRam candidate) || !Working(candidate, bot.Realm) ||
                    candidate.CurrentRegion != bot.CurrentRegion || !Crews.TryGetValue(candidate, out Crew crew)) continue;
                Prune(crew, now);
                int free = candidate.MAX_PASSENGERS - crew.Seats.Count;
                long distance = bot.GetDistanceTo(candidate);
                if (free > bestFree || free == bestFree && free > 0 && distance < bestDistance)
                {
                    ram = candidate; bestFree = free; bestDistance = distance;
                }
            }
            if (ram == null || !Crews.TryGetValue(ram, out Crew chosen)) return false;
            for (seat = 0; chosen.Seats.ContainsKey(seat); seat++) { }
            chosen.Seats[seat] = (new WeakReference<GameBot>(bot), now + SeatLeaseMilliseconds);
            bot.TempProperties.SetProperty(SeatKey, ram);
            return true;
        }
    }

    /// <summary>The ram this bot holds a live seat at, else null (the lease is renewed).</summary>
    public static GameSiegeRam KeepSeat(GameBot bot, out int seat)
    {
        seat = -1;
        GameSiegeRam ram = bot?.TempProperties.GetProperty<GameSiegeRam>(SeatKey);
        if (ram == null) return null;
        long now = GameLoop.GameLoopTime;
        lock (Gate)
        {
            if (Crews.TryGetValue(ram, out Crew crew))
            {
                foreach (var (index, entry) in crew.Seats)
                    if (entry.Bot.TryGetTarget(out GameBot seated) && seated == bot)
                    {
                        seat = index;
                        break;
                    }
                if (seat >= 0)
                {
                    crew.Seats[seat] = crew.Seats[seat] with { Until = now + SeatLeaseMilliseconds };
                    return ram;
                }
            }
        }
        bot.TempProperties.RemoveProperty(SeatKey);
        return null;
    }

    public static void Leave(GameBot bot)
    {
        GameSiegeRam ram = bot?.TempProperties.GetProperty<GameSiegeRam>(SeatKey);
        if (ram == null) return;
        bot.TempProperties.RemoveProperty(SeatKey);
        lock (Gate)
            if (Crews.TryGetValue(ram, out Crew crew))
                foreach (int index in crew.Seats.Where(pair => !pair.Value.Bot.TryGetTarget(out GameBot seated) || seated == bot)
                             .Select(pair => pair.Key).ToArray())
                    crew.Seats.Remove(index);
    }

    /// <summary>A crew member standing at its working ram (it holds its position like the operator).</summary>
    public static bool AtSeat(GameBot bot) =>
        bot?.TempProperties.GetProperty<GameSiegeRam>(SeatKey) is { } ram && Working(ram, bot.Realm) &&
        ram.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(ram, 300);

    private static void Prune(Crew crew, long now)
    {
        foreach (int index in crew.Seats.Where(pair => pair.Value.Until < now || !pair.Value.Bot.TryGetTarget(out GameBot seated) ||
                         !seated.IsAlive || seated.ObjectState != GameObject.eObjectState.Active)
                     .Select(pair => pair.Key).ToArray())
            crew.Seats.Remove(index);
    }
}
