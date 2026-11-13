"""OFFLINEDAOC Database - stable routes (lane V-D).

    python -I source/build_routes.py [--db <world>] [--out <data dir>]

Writes data/routes.js: every stable master, every ticket connection the server really offers (horses and the
Shrouded Isles flying mounts), the reputation rule of the faction stable masters, and two other kinds of fixed
travel that are cheap to get right (zone gates from ZonePoint, the Shrouded Isles teleporters).
Reads the world READ-ONLY and the server's C# as text. References are file:line under
development-source/server/GameServer/ as read on 2026-11-12.

HOW A TICKET BECOMES A RIDE
  gameobjects/CustomNPC/GameStableMaster.cs:46-103   buying costs number * ItemTemplate.Price (0 for every ticket
                                                     in the True 1.65 world: rides are free); a bought ticket is
                                                     handed straight back to the stable master (96-102)
  GameStableMaster.cs:128                            a ticket is an item with Item_Type 40 that is on this stable
                                                     master's own list
  GameStableMaster.cs:130                            the path is the one whose PathID is the ticket's Id_nb
  gameutils/MovementMgr.cs:19-49, 86-126             paths come from Path / PathPoints, keyed by the exact PathID
                                                     text, points in Step order; no Path row means type Once
  GameStableMaster.cs:132                            the ride starts only if the first point is less than 500 from
                                                     the stable master on both X and Y; otherwise nothing happens
  GameStableMaster.cs:137-158                        the mount is NPC template = the ticket's Color (301 dragonfly,
                                                     302 gryphon, 303 wyvern: templates with the FLYING flag;
                                                     311-313 horses; 0 = the default horse)
  GameStableMaster.cs:171-175                        the mount runs at 1000 the whole way (FixedSpeed), leaving 4
                                                     seconds after the purchase
  ECS-Components/NpcMovementComponent.cs:1233        a taxi keeps that one speed on every leg (the points' own
                                                     MaxSpeed is ignored for players' mounts)
  NpcMovementComponent.cs:670-678                    a leg takes its 3D length / speed
  NpcMovementComponent.cs:1167-1174                  a point with WaitTime stops the mount WaitTime * 0.1 seconds
  commands/playercommands/stables.cs:97-106          /stables lists a ticket when the path has a second point, is of
                                                     type Once and starts within 500 (X and Y)
  bots/autonomous/AutonomousStableRoutePlanner.cs:867-905   the bots' planner: type Once, start within 500 (straight
                                                     line), ride seconds = 2D leg length / min(point MaxSpeed, 1000)
                                                     + WaitTime * 0.1 (the cross-check; see botSeconds)
  gameobjects/CustomNPC/GameBoatStableMaster.cs      boats: the class exists, no Mob row uses it -> no boat routes

REPUTATION
  gameobjects/GameNPC.cs:2633-2641                   an NPC refuses to talk when it is not of the player's realm, has
                                                     a faction and that faction's standing is Hostile or worse
  serverrules/NormalServerRules.cs:115-117           an NPC with the PEACE flag counts as the player's realm
  gameutils/Faction.cs:184-194                       Hostile = aggro above 50; a character starts at BaseAggroLevel
  scripts/quests/Reputation/FactionEmissaryRuntime.cs:41, 48-62, 132-161   RequiredAggroForStables = 50; the three
                                                     emissaries (one per realm) and what the stable master says
  scripts/quests/Reputation/FactionReputationQuest.cs:18-20, 178-190       10 kills, +10 reputation (+5 after a
                                                     reroll), repeatable
  scripts/quests/Reputation/FactionReputationTargets.cs:42-44, 77-99, 101-170   which monsters the emissary sends you to
  gameutils/Faction.cs:82-89, 113-135                every kill: 20% chance of 1 point with each linked faction
  mode165/Classic165Mode.cs:56-76                    TRUE 1.65: FactionEmissaryRuntime is a "freeshard script" ...
  events/GameEventMgr.cs:115-126                     ... whose start-up hook does not run when classic165_mode is on
                                                     (ServerProperty classic165_mode = True in the True 1.65 world).
                                                     So the three emissaries are never spawned and the reputation hunt
                                                     cannot be taken: in True 1.65 the standing is earned by kills only.
                                                     The gate itself (GameNPC.cs:2633) and the stable master's refusal
                                                     text (GameStableMaster.cs:34, static) stay. The emissary code is
                                                     still read here: it gives the data for a world with the mode off,
                                                     and the name the refusal text mentions.
  serverrules/AbstractServerRules.cs:1091            ... for every player who damaged the monster
"""
import collections, json, math, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wgcommon as wg
import build_vendors as bv

GS = bv.GS
BOARD = 500                 # GameStableMaster.cs:132
SPEED = 1000                # GameStableMaster.cs:171
START_DELAY = 4             # GameStableMaster.cs:175 (HorseRideAction.Start(4000))
ONCE = 1                    # CoreDatabase/Enums/EPathType.cs
PATH_TYPE = {1: "Once", 2: "Path_Reverse", 3: "Loop"}
FLYING = 0x20               # GameNPC.cs:620
ARRIVE_MAX = 12000          # a route's end belongs to the nearest stable master of its region within this distance
                            # (our rule: the longest real gap is 9,686, Ludlow -> Castle Sauvage; the stable master
                            # of any other town is at least twice as far from the end of the ride)
TOWN = 3000                 # stable masters this close together are one stop on foot (our rule; pairs 1,364-2,243)
SIMPLIFY = 20               # the drawn line may leave the real path by at most this many units (our rule)
DUNGEON_REGIONS = None      # filled from world/Zone.cs
# whose land a region is: stables.cs:72-82 (classic + Shrouded Isles region of each realm) and the three housing
# regions (Regions.Description "Albion Housing" / "Midgard Housing" / "Hibernia Housing")
LAND = {1: 1, 51: 1, 2: 1, 100: 2, 151: 2, 102: 2, 200: 3, 181: 3, 202: 3}


def read(path):
    with open(GS + "/" + path, encoding="utf-8", errors="replace") as f:
        return f.read()


def const_int(text, name):
    m = re.search(r"\b%s\s*=\s*(-?\d+)" % re.escape(name), text)
    assert m, "constant %s not found" % name
    return int(m.group(1))


def emissaries():
    """FactionEmissaryRuntime.Definitions (lines 48-62): realm, faction, hunt region, name, position, host stable."""
    text = read("scripts/quests/Reputation/FactionEmissaryRuntime.cs")
    block = text[text.index("Definitions ="):text.index("private static readonly Dictionary<eRealm, FactionEmissaryNPC>")]
    block = "\n".join(l.split("//")[0] for l in block.split("\n"))
    out = []
    for m in re.finditer(r"new\(eRealm\.(\w+),(.*?)\)\s*,", block, re.S):
        body = m.group(2)
        strs = re.findall(r"\"([^\"]*)\"", body)
        ints = [int(x) for x in re.findall(r"(?<![\w\"])(\d+)(?![\w\"])", re.sub(r"\"[^\"]*\"", "", body))]
        assert len(strs) == 5 and len(ints) >= 8, "emissary definition not understood: " + body
        out.append(dict(realm=bv._REALM_ENUM[m.group(1)], faction=ints[0], factionName=strs[0], huntRegion=ints[1],
                        name=strs[1], region=ints[2], x=ints[3], y=ints[4], z=ints[5], host=strs[2], town=strs[3],
                        mount=strs[4]))
    assert len(out) == 3, "expected three emissaries, found %d" % len(out)
    need = const_int(text, "RequiredAggroForStables")
    return out, need


def dungeon_regions():
    text = read("world/Zone.cs")
    block = text[text.index("public bool IsDungeon"):]
    block = block[:block.index("return true")]
    block = "\n".join(l.split("//")[0] for l in block.split("\n"))
    ids = {int(x) for x in re.findall(r"case\s+(\d+)\s*:", block)}
    assert 249 in ids and len(ids) > 40, "Zone.IsDungeon not understood"
    return ids


def rdp(pts, eps):
    """Ramer-Douglas-Peucker on [(x, y), ...]."""
    if len(pts) < 3:
        return list(pts)
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        a, b = stack.pop()
        ax, ay = pts[a]
        bx, by = pts[b]
        dx, dy = bx - ax, by - ay
        n = math.hypot(dx, dy)
        worst, wi = 0.0, -1
        for i in range(a + 1, b):
            px, py = pts[i]
            d = abs(dx * (ay - py) - dy * (ax - px)) / n if n else math.hypot(px - ax, py - ay)
            if d > worst:
                worst, wi = d, i
        if worst > eps:
            keep[wi] = True
            stack.append((a, wi))
            stack.append((wi, b))
    return [p for p, k in zip(pts, keep) if k]


def split_by_zone(world, region, pts):
    """[(x, y, z), ...] in region units -> [{zone, pts: [[x, y], ...]}, ...] in ride order, zone-local units.
    The point across a zone border is repeated on both sides so the drawn line does not break.
    Returns (segments, number of points that lie in no zone)."""
    segs, cur, curz, lost = [], None, None, 0
    prev = None
    for p in pts:
        z = wg.zone_of(world["zbr"], region, p[0], p[1])
        if z is None:
            lost += 1
            z = curz
            if z is None:
                continue
        if curz is None or z["id"] != curz["id"]:
            if cur is not None:
                cur.append((p[0] - curz["ox"], p[1] - curz["oy"]))          # run out to the first point beyond
                segs.append((curz, cur))
            cur = []
            if prev is not None:
                cur.append((prev[0] - z["ox"], prev[1] - z["oy"]))          # come in from the last point before
            curz = z
        cur.append((p[0] - z["ox"], p[1] - z["oy"]))
        prev = p
    if cur:
        segs.append((curz, cur))
    return [dict(zone=z["id"], pts=[[int(x), int(y)] for x, y in rdp(c, SIMPLIFY)]) for z, c in segs], lost


def ticket_destination(name):
    """stables.cs:129-134 / AutonomousStableRoutePlanner.cs:907-914."""
    v = (name or "").strip()
    i = v.lower().find("ticket to")
    return v[i + len("ticket to"):].strip() if i >= 0 else v


HUMANIZE = {"AdribardsRetreat": "Adribard's Retreat", "Aegirhamm": "Aegirhamn", "Camp": "Lammia Camp",
            "CampStation": "Campacorentin Station", "CamelotNoth": "Camelot North", "Cotswold": "Cotswold Village",
            "FortGwyntel": "Fort Gwyntell", "GronyrsFarm": "Gronyr's Farm", "GroveofAalidFeie": "Grove of Aalid Feie",
            "IarnDwarf": "Iarn Dwarf Camp", "Mantid": "Mantid Town", "Moderna": "Modernagrav",
            "ParthananFarm": "East Lough Derg", "ParthenonFarm": "East Lough Derg", "TirNaNog": "Tir na Nog North",
            "TirNaNogNord": "Tir na Nog North", "TirnamBeo": "Tir na mBeo"}          # stables.cs:152-173


def source_name(path_id):
    """stables.cs:136-149 without the area lookup: the stop's name from the ticket id (HS_<from>_<to>).
    'Ticket_<from>_<to>' is read the same way (our addition, for Pallig Thorrison's two tickets)."""
    parts = (path_id or "").split("_")
    if len(parts) >= 3 and parts[0].lower() in ("hs", "hr", "dragonfly", "gryphon", "wyvern", "ticket"):
        return HUMANIZE.get(parts[1]) or re.sub(r"(?<=[a-z])(?=[A-Z])", " ", parts[1])
    return None


def emissaries_spawn(con):
    """False when True 1.65 Mode keeps FactionEmissaryRuntime from starting (see the header)."""
    mode_on = str(bv.server_property(con, "classic165_mode", "False")).lower() == "true"
    c = read("mode165/Classic165Mode.cs")
    block = c[c.index("FreeshardScriptTypes ="):c.index("public static bool IsFreeshardScript")]
    listed = '"FactionEmissaryRuntime"' in block
    g = read("events/GameEventMgr.cs")
    gated = "Classic165Mode.IsFreeshardScript(type)" in g and "if (!DOL.GS.Classic165Mode.IsOn)" in g
    assert "public static bool IsOn => ServerProperties.Properties.CLASSIC165_MODE;" in c, "Classic165Mode.IsOn changed"
    r = read("scripts/quests/Reputation/FactionEmissaryRuntime.cs")
    assert "[ScriptLoadedEvent]" in r and r.count("AddEmissary(definition)") == 1, "emissary start-up changed"
    return not (mode_on and listed and gated), mode_on


def species_of_factions(con, world, templates, faction_ids):
    """Every no-realm monster kind whose (runtime) faction is one of faction_ids, per zone: what to kill."""
    groups = collections.defaultdict(list)
    for m in con.execute("select Mob_ID, Name, Level, X, Y, Region, Realm, FactionID, NPCTemplateID from Mob"):
        if m["Realm"] != 0 or m["Region"] in world["off"]:
            continue
        t = templates.get(m["NPCTemplateID"])
        owns = t is not None and bool(t["ReplaceMobValues"])
        fac = (t["FactionID"] if owns else m["FactionID"]) or 0
        if fac not in faction_ids:
            continue
        z = wg.zone_of(world["zbr"], m["Region"], m["X"], m["Y"])
        if z is None:
            continue
        name = ((t["Name"] if owns else m["Name"]) or "").strip()
        if not name:
            continue
        levels = []
        if owns and (t["Level"] or "").strip():                      # GameNPC.cs:1361-1390, Util.cs:86-116
            for part in str(t["Level"]).split(";"):
                part = part.strip()
                r = re.fullmatch(r"(\d+)-(\d+)", part)
                if r:
                    a, b = sorted((int(r.group(1)), int(r.group(2))))
                    levels += list(range(a, b + 1))
                elif part.isdigit():
                    levels.append(int(part))
            levels = [l for l in levels if 0 < l <= 255]
        if not levels:
            levels = [m["Level"]]
        groups[(name.lower(), z["id"])].append((m, z, name, levels, fac))
    out = []
    for (_, zid), members in groups.items():
        cx = sum(x[0]["X"] for x in members) / len(members)
        cy = sum(x[0]["Y"] for x in members) / len(members)
        rep_ = min(members, key=lambda x: (abs(x[0]["X"] - cx) + abs(x[0]["Y"] - cy), x[0]["Mob_ID"]))
        levels = sorted(l for x in members for l in x[3])
        z = rep_[1]
        out.append(dict(name=rep_[2], zone=zid, x=rep_[0]["X"] - z["ox"], y=rep_[0]["Y"] - z["oy"],
                        levelMin=levels[0], levelMax=levels[-1], n=len(members),
                        faction=world["factions"][rep_[4]]["Name"]))
    out.sort(key=lambda c: (c["levelMin"], c["levelMax"], c["name"].lower(), c["zone"]))
    return out


def hunt_targets(con, world, templates, faction_id, region_id, cap, window, min_spawns):
    """Port of FactionReputationTargets.Build (lines 101-170): the species the emissary can send a player to."""
    enemy = {r[0] for r in con.execute("select FactionID from LinkedFaction where LinkedFactionID = ? and IsFriend = 0",
                                       (faction_id,))} - {faction_id}
    groups = collections.defaultdict(list)
    if region_id in DUNGEON_REGIONS:
        return [], enemy
    for m in con.execute("select Mob_ID, Name, Level, X, Y, Z, Realm, RespawnInterval, FactionID, NPCTemplateID, ClassType"
                         " from Mob where Region = ?", (region_id,)):
        name = m["Name"] or ""
        if m["ClassType"] != bv.DEFAULT_NPC_CLASS or m["Realm"] != 0 or m["RespawnInterval"] <= 0 or not name.strip() \
                or name != name.lower():
            continue
        z = wg.zone_of(world["zbr"], region_id, m["X"], m["Y"])
        if z is None:
            continue
        t = templates.get(m["NPCTemplateID"])
        owns = t is not None and bool(t["ReplaceMobValues"])
        fac = (t["FactionID"] if owns else m["FactionID"]) or 0
        if fac not in enemy:
            continue
        levels = []
        if owns and (t["Level"] or "").strip():
            for part in str(t["Level"]).split(";"):
                part = part.strip()
                r = re.fullmatch(r"(\d+)-(\d+)", part)
                if r:
                    a, b = sorted((int(r.group(1)), int(r.group(2))))
                    levels += list(range(a, b + 1))
                elif part.isdigit():
                    levels.append(int(part))
            levels = [l for l in levels if 0 < l <= 255]
        if not levels:
            levels = [m["Level"]]
        groups[(name, z["id"])].append((m, z, fac, levels))
    out = []
    for (name, zid), members in groups.items():
        cx = sum(x[0]["X"] for x in members) / len(members)
        cy = sum(x[0]["Y"] for x in members) / len(members)
        rep = min(members, key=lambda x: (abs(x[0]["X"] - cx) + abs(x[0]["Y"] - cy), x[0]["Mob_ID"]))
        levels = sorted(l for x in members for l in x[3])
        z = rep[1]
        out.append(dict(name=name, zone=zid, x=rep[0]["X"] - z["ox"], y=rep[0]["Y"] - z["oy"],
                        level=levels[len(levels) // 2], levelMin=levels[0], levelMax=levels[-1], n=len(members),
                        common=len(members) >= min_spawns))
    out.sort(key=lambda c: (c["level"], c["name"]))
    return out, enemy


def main():
    global DUNGEON_REGIONS
    a = wg.args(__doc__.split("\n")[0])
    con = wg.open_db(a.db)
    world = bv.collect(con)
    kept_vendors = bv.assign_ids(world)                 # the same ids as data/vendors.js
    vendor_id = {v["mob"]: v["id"] for v in kept_vendors}
    items, zs, factions, templates = world["items"], world["zones"], world["factions"], world["templates"]
    DUNGEON_REGIONS = dungeon_regions()
    em_list, need_aggro = emissaries()
    assert need_aggro == 50, "RequiredAggroForStables changed: %d" % need_aggro
    q = read("scripts/quests/Reputation/FactionReputationQuest.cs")
    kills, gain, gain_reroll = const_int(q, "RequiredKillCount"), const_int(q, "FullReputationGain"), \
        const_int(q, "RerolledReputationGain")
    tq = read("scripts/quests/Reputation/FactionReputationTargets.cs")
    cap, window, min_spawns = const_int(tq, "LevelCap"), const_int(tq, "LevelWindow"), const_int(tq, "MinimumSpawns")
    sm = read("gameobjects/CustomNPC/GameStableMaster.cs")
    assert "mount.MaxSpeedBase = 1000;" in sm and "HorseRideAction(mount).Start(4000)" in sm, "stable master speed changed"
    assert re.search(r"Math\.Abs\(path\.X - this\.X\)\) < 500", sm), "boarding distance changed"

    # ---- paths, exactly as MovementMgr caches them (ordinal keys, first row of a duplicated step wins)
    path_type = {}
    for r in con.execute("select PathID, PathType from Path order by Path_ID"):
        path_type.setdefault(r["PathID"], r["PathType"])
    pts_by_path = collections.defaultdict(dict)
    for r in con.execute("select PathID, Step, X, Y, Z, MaxSpeed, WaitTime from PathPoints order by PathPoints_ID"):
        pts_by_path[r["PathID"]].setdefault(r["Step"], (r["X"], r["Y"], r["Z"], r["MaxSpeed"], r["WaitTime"]))
    ci_paths = collections.defaultdict(set)
    for k in pts_by_path:
        ci_paths[k.lower()].add(k)

    # ---- stables
    masters = [v for v in world["merchants"] if v["is_stable"]]
    left = collections.Counter()
    stables = []
    for v in masters:
        if v["off"]:
            left["stable master left out: " + v["off"]] += 1
            continue
        if v["zone"] is None:
            left["stable master left out: position in no zone"] += 1
            continue
        stables.append(v)
    stables.sort(key=lambda v: (v["region"], v["name"].lower(), v["rx"], v["ry"], v["mob"]))
    for i, v in enumerate(stables, 1):
        v["sid"] = i
    by_region = collections.defaultdict(list)
    for v in stables:
        by_region[v["region"]].append(v)

    em_live, mode165 = emissaries_spawn(con)
    em_defined = {e["faction"]: e for e in em_list}              # as the code defines them
    em_by_faction = em_defined if em_live else {}                 # as they exist in this world
    KILL_CHANCE = 0.2                                             # Faction.cs:115 Util.Chance(20)
    assert "Util.Chance(20)" in read("gameutils/Faction.cs"), "faction kill chance changed"
    help_ids, hurt_ids = {}, {}
    for fid in {v["fac"]["ID"] for v in stables if v["gated"]} | set(em_defined):
        help_ids[fid] = {r[0] for r in con.execute(
            "select LinkedFactionID from LinkedFaction where FactionID = ? and IsFriend = 0", (fid,))} - {fid}
        hurt_ids[fid] = {r[0] for r in con.execute(
            "select LinkedFactionID from LinkedFaction where FactionID = ? and IsFriend = 1", (fid,))} | {fid}

    def reputation(v):
        if not v["gated"]:
            return None
        f = v["fac"]
        e = em_by_faction.get(f["ID"])
        starts = f["BaseAggroLevel"] > need_aggro
        text = "%s reputation %d or better (a new character starts at %d: %s)" % (
            f["Name"], -need_aggro, -f["BaseAggroLevel"], "refused" if starts else "allowed")
        if e:
            how = ("Hunt for %s (%s Emissary), beside %s in %s: %d kills of the named monster, then return, for +%d "
                   "(+%d if you rerolled the target). Killing the faction's enemies also has a 20%% chance of +1 a kill."
                   % (e["name"], e["factionName"], e["host"], e["town"], kills, gain, gain_reroll))
        else:
            enemies = sorted(factions[x]["Name"] for x in help_ids[f["ID"]] if x in factions)
            avg = int(round(max(0, f["BaseAggroLevel"] - need_aggro) / KILL_CHANCE))
            how = ("Kills only: each kill of a monster of %s has a 20%% chance of +1 reputation%s. "
                   "Killing the faction's own members costs reputation the same way."
                   % (", ".join(enemies) if enemies else "a faction that is its enemy (none is linked)",
                      " (about %d kills from a new character)" % avg if avg else ""))
            if f["ID"] in em_defined and not em_live:
                how += (" The stable master's refusal names an emissary, %s; that NPC and its hunt are switched off "
                        "in True 1.65." % em_defined[f["ID"]]["name"])
        return dict(faction=f["ID"], factionName=f["Name"], base=f["BaseAggroLevel"], maxAggro=need_aggro,
                    need=-need_aggro, start=-f["BaseAggroLevel"], startsRefused=starts, text=text, how=how,
                    emissary=e["name"] if e else None)

    # ---- routes
    routes, broken, arrive_far = [], [], []
    landings = []                                     # ends with no stable master near
    check = collections.Counter()
    lost_points = 0
    for v in stables:
        rows = world["lists"].get((v["list"] or "").lower(), [])
        v["tickets"] = 0
        for it, page, slot in sorted(rows, key=lambda r: (r[1], r[2])):
            if it["Item_Type"] != 40:                                                   # GameStableMaster.cs:128
                check["stable list rows that are not tickets"] += 1
                continue
            v["tickets"] += 1
            tid = it["Id_nb"]
            steps = pts_by_path.get(tid)
            base = dict(**{"from": v["sid"]}, ticket=tid, name=it["Name"], toName=ticket_destination(it["Name"]),
                        price=int(it["Price"] or 0))
            if not steps:
                near = sorted(ci_paths.get(tid.lower(), ()))
                why = "no path points for this ticket" + (" (a path exists under another spelling: %s)" % near[0] if near else "")
                broken.append(dict(base, why=why))
                continue
            pts = [steps[k] for k in sorted(steps)]
            ptype = path_type.get(tid, ONCE)                                            # MovementMgr.cs:97-100
            dx, dy = abs(pts[0][0] - v["rx"]), abs(pts[0][1] - v["ry"])
            if not (dx < BOARD and dy < BOARD):
                broken.append(dict(base, why="the path starts %d units from the stable master (limit %d on X and Y): "
                                             "the ticket is taken back and nothing happens" % (int(math.hypot(dx, dy)), BOARD)))
                continue
            if len(pts) < 2:
                broken.append(dict(base, why="the path has a single point"))
                continue
            if ptype != ONCE:
                broken.append(dict(base, why="the path is of type %s, not a one-way path; /stables and the bots skip it"
                                             % PATH_TYPE.get(ptype, ptype)))
                continue
            planner_ok = math.hypot(pts[0][0] - v["rx"], pts[0][1] - v["ry"]) <= BOARD   # planner line 876
            if not planner_ok:
                check["routes a player can ride that the bots' planner skips (start beyond 500 in a straight line)"] += 1
            length3 = sum(math.dist(pts[i][:3], pts[i + 1][:3]) for i in range(len(pts) - 1))
            length2 = sum(math.dist(pts[i][:2], pts[i + 1][:2]) for i in range(len(pts) - 1))
            waits = sum(max(0, p[4] or 0) for p in pts[1:]) * 0.1
            seconds = length3 / SPEED + waits
            bot = sum(math.dist(pts[i][:2], pts[i + 1][:2]) / max(1, min(pts[i + 1][3] or 0, SPEED)) +
                      max(0, pts[i][4] or 0) * 0.1 for i in range(len(pts) - 1))          # planner lines 895-901
            end = pts[-1]
            cands = sorted((math.hypot(s["rx"] - end[0], s["ry"] - end[1]), s["sid"], s) for s in by_region[v["region"]])
            to, gap = None, None
            if cands and cands[0][0] <= ARRIVE_MAX:
                to, gap = cands[0][2], int(round(cands[0][0]))
                if gap > 2500:
                    arrive_far.append((tid, gap, to["name"]))
            mount_t = templates.get(it["Color"]) if it["Color"] else None
            flying = bool(mount_t is not None and (int(mount_t["Flags"] or 0) & FLYING))
            word = it["Name"].split()[0].lower() if flying else "horse"
            path, lost = split_by_zone(world, v["region"], pts)
            lost_points += lost
            zend = wg.zone_of(world["zbr"], v["region"], end[0], end[1])
            r = dict(base, to=to["sid"] if to else None, gap=gap, kind="flying" if flying else "horse",
                     mount=word, realm=v["realm"], seconds=round(seconds, 1), botSeconds=round(bot, 1),
                     length=int(round(length3)), points=len(pts), reputation=reputation(v), path=path,
                     end=dict(zone=zend["id"], x=end[0] - zend["ox"], y=end[1] - zend["oy"]) if zend else None,
                     _end=end, _region=v["region"], _len2=length2)
            if zend is None:
                broken.append(dict(base, why="the path ends outside every zone"))
                continue
            routes.append(r)

    # ---- arrival points with no stable master: one node per place
    for r in routes:
        if r["to"] is not None:
            continue
        hit = None
        for L in landings:
            if L["region"] == r["_region"] and math.hypot(L["rx"] - r["_end"][0], L["ry"] - r["_end"][1]) <= ARRIVE_MAX:
                hit = L
                break
        if hit is None:
            z = wg.zone_of(world["zbr"], r["_region"], r["_end"][0], r["_end"][1])
            hit = dict(sid=len(stables) + len(landings) + 1, region=r["_region"], rx=r["_end"][0], ry=r["_end"][1],
                       zone=z, name=None, place=r["toName"], realm=r["realm"])
            landings.append(hit)
        r["to"] = hit["sid"]

    # ---- names of the stops
    arrive_names = collections.defaultdict(collections.Counter)
    for r in routes:
        arrive_names[r["to"]][r["toName"]] += 1
    first_ticket = {}
    for r in routes + broken:
        first_ticket.setdefault(r["from"], r["ticket"])
    areas = [r for r in con.execute("select Description, X, Y, Radius, Region from Area where ClassType = 'DOL.GS.Area+Circle'"
                                    " and Description is not null and trim(Description) <> '' order by Area_ID")]

    def area_of(v):          # stables.cs:138-141: the described area the stable master stands in
        hit = [r["Description"] for r in areas if r["Region"] == v["region"] and
               math.hypot(r["X"] - v["rx"], r["Y"] - v["ry"]) <= r["Radius"]]
        return hit[0] if len(hit) == 1 else None

    # towns: stable masters within TOWN of each other in one region (single link); the id is the smallest stable id
    town = {v["sid"]: v["sid"] for v in stables}
    for region, vs in by_region.items():
        for i, p in enumerate(vs):
            for o in vs[i + 1:]:
                if math.hypot(p["rx"] - o["rx"], p["ry"] - o["ry"]) <= TOWN:
                    a_, b_ = town[p["sid"]], town[o["sid"]]
                    lo = min(a_, b_)
                    for k in town:
                        if town[k] in (a_, b_):
                            town[k] = lo
    stable_rows = []
    for v in stables:
        names = arrive_names.get(v["sid"])
        area = area_of(v)
        place = names.most_common(1)[0][0] if names else (area or source_name(first_ticket.get(v["sid"])) or v["zone"]["name"])
        z = v["zone"]
        stable_rows.append(dict(id=v["sid"], name=v["name"], place=place, zone=z["id"], x=v["rx"] - z["ox"],
                                y=v["ry"] - z["oy"], realm=v["realm"], land=LAND.get(v["region"], 0), master=True, town=town[v["sid"]], area=area,
                                vendor=vendor_id.get(v["mob"]),
                                tickets=v["tickets"], guild=v["guild"], reputation=reputation(v),
                                faction=v["faction"] or None))
    for L in landings:
        z = L["zone"]
        stable_rows.append(dict(id=L["sid"], name=None, place=L["place"], zone=z["id"], x=L["rx"] - z["ox"],
                                y=L["ry"] - z["oy"], realm=L["realm"], land=LAND.get(L["region"], 0), master=False, town=L["sid"], area=None, vendor=None, tickets=0,
                                guild=None, reputation=None, faction=None))
    sid_place = {s["id"]: s["place"] for s in stable_rows}
    route_ends = [(r["_end"][0], r["_end"][1], r["_region"]) for r in routes]
    for i, r in enumerate(routes, 1):
        r["id"] = i
        r["fromName"] = sid_place[r["from"]]
        for k in ("_end", "_region", "_len2"):
            r.pop(k)
    for b in broken:
        b["fromName"] = sid_place[b["from"]]

    # ---- factions behind the gates
    fac_out = {}
    gated_ids = sorted({v["fac"]["ID"] for v in stables if v["gated"]} | set(em_defined))
    for fid in gated_ids:
        f = factions[fid]
        e = em_by_faction.get(fid)
        helps = [factions[x]["Name"] for x in help_ids[fid] if x in factions]
        hurts = [factions[x]["Name"] for x in hurt_ids[fid] if x in factions]
        entry = dict(id=fid, name=f["Name"], base=f["BaseAggroLevel"], start=-f["BaseAggroLevel"], need=-need_aggro,
                     startsRefused=f["BaseAggroLevel"] > need_aggro,
                     huntsNeeded=max(0, -(-(f["BaseAggroLevel"] - need_aggro) // gain)) if e else None,
                     stables=[v["sid"] for v in stables if v["gated"] and v["fac"]["ID"] == fid],
                     openStables=[v["sid"] for v in stables if not v["gated"] and v["faction"] == fid],
                     killsHelp=sorted(set(helps)), killsHurt=sorted(set(hurts)), emissary=None, hunts=[],
                     killChance=KILL_CHANCE, killPoints=max(0, f["BaseAggroLevel"] - need_aggro),
                     killsNeeded=int(round(max(0, f["BaseAggroLevel"] - need_aggro) / KILL_CHANCE)),
                     killMobs=species_of_factions(con, world, templates, help_ids[fid]),
                     emissaryOff=None)
        d = em_defined.get(fid)
        if d and not em_live:
            entry["emissaryOff"] = dict(name=d["name"], host=d["host"], town=d["town"],
                                        why="True 1.65 Mode does not start the reputation emissaries "
                                            "(Classic165Mode.cs:56-60, GameEventMgr.cs:115-126); the stable master's "
                                            "refusal still names this NPC")
        if e:
            ez = wg.zone_of(world["zbr"], e["region"], e["x"], e["y"])
            assert ez is not None, "emissary %s stands in no zone" % e["name"]
            host = [v for v in stables if v["name"].lower() == e["host"].lower() and v["region"] == e["region"]]
            assert host, "emissary host stable master %s not found" % e["host"]
            entry["emissary"] = dict(name=e["name"], title="%s Emissary" % f["Name"], realm=e["realm"], zone=ez["id"],
                                     x=e["x"] - ez["ox"], y=e["y"] - ez["oy"], host=e["host"], hostStable=host[0]["sid"],
                                     town=e["town"], mounts=e["mount"])
            hunts, enemy = hunt_targets(con, world, templates, fid, e["huntRegion"], cap, window, min_spawns)
            entry["hunts"] = hunts
            entry["huntEnemies"] = sorted(factions[x]["Name"] for x in enemy if x in factions)
            entry["huntZoneNote"] = "Only monsters outdoors in the emissary's own island count."
        fac_out[str(fid)] = entry
    for r in routes:
        if r["reputation"]:
            em = fac_out[str(r["reputation"]["faction"])]["emissary"]
            r["reputation"]["emissary"] = dict(name=em["name"], zone=em["zone"], x=em["x"], y=em["y"]) if em else None
    for s in stable_rows:
        if s["reputation"]:
            em = fac_out[str(s["reputation"]["faction"])]["emissary"]
            s["reputation"]["emissary"] = dict(name=em["name"], zone=em["zone"], x=em["x"], y=em["y"]) if em else None

    # ---- other fixed travel: zone gates (ZonePoint) and the Shrouded Isles teleporters
    off = world["off"]
    gates, gates_left = [], collections.Counter()
    for r in con.execute("select * from ZonePoint order by Id"):
        if r["SourceRegion"] in off or r["TargetRegion"] in off:
            gates_left["zone point left out: a side is in a disabled region"] += 1
            continue
        sz = wg.zone_of(world["zbr"], r["SourceRegion"], r["SourceX"], r["SourceY"])
        tz = wg.zone_of(world["zbr"], r["TargetRegion"], r["TargetX"], r["TargetY"])
        if sz is None or tz is None:
            gates_left["zone point left out: a side is in no zone"] += 1
            continue
        g = {"id": r["Id"], "realm": r["Realm"],
             "from": dict(zone=sz["id"], x=r["SourceX"] - sz["ox"], y=r["SourceY"] - sz["oy"]),
             "to": dict(zone=tz["id"], x=r["TargetX"] - tz["ox"], y=r["TargetY"] - tz["oy"])}
        if r["ClassType"]:
            g["rule"] = r["ClassType"].split(".")[-1]
        gates.append(g)

    tele_classes = {"DOL.GS.AlbionSITeleporter": "gameobjects/CustomNPC/Teleporters/AlbionSITeleporter.cs",
                    "DOL.GS.MidgardSITeleporter": "gameobjects/CustomNPC/Teleporters/MidgardSITeleporter.cs",
                    "DOL.GS.HiberniaSITeleporter": "gameobjects/CustomNPC/Teleporters/HiberniaSITeleporter.cs"}
    tports = collections.defaultdict(dict)                 # WorldMgr: realm -> "Type:TeleportID" -> row
    for r in con.execute("select * from Teleport order by Teleport_ID"):
        tports[r["Realm"]].setdefault("%s:%s" % (r["Type"] or "", r["TeleportID"]), r)
    teleporters = []
    for cls, src in tele_classes.items():
        text = read(src)
        m = re.search(r"m_destination\s*=\s*\{(.*?)\}", text, re.S)
        assert m, "destinations of %s not found" % cls
        dests = re.findall(r"\"([^\"]+)\"", m.group(1))
        for mob in con.execute("select * from Mob where ClassType = ? order by Region, X, Y", (cls,)):
            if mob["Region"] in off:
                continue
            z = wg.zone_of(world["zbr"], mob["Region"], mob["X"], mob["Y"])
            if z is None:
                continue
            to = []
            for d in dests:
                t = tports.get(mob["Realm"], {}).get(":" + d)                     # GameTeleporter.cs:221
                if t is None or t["RegionID"] in off:
                    continue
                tz = wg.zone_of(world["zbr"], t["RegionID"], t["X"], t["Y"])
                if tz is not None:
                    to.append(dict(name=d, zone=tz["id"], x=t["X"] - tz["ox"], y=t["Y"] - tz["oy"]))
            teleporters.append(dict(name=mob["Name"], guild=mob["Guild"], realm=mob["Realm"], zone=z["id"],
                                    x=mob["X"] - z["ox"], y=mob["Y"] - z["oy"], to=to))
    other_tele = [tuple(r) for r in con.execute(
        "select ClassType, count(*) from Mob where ClassType like '%Teleporter%' group by 1 order by 1")]

    # ---- cross-check against the True 1.65 route content files
    content = {}
    for name in ("add-housingroutes165", "add-housingpaths165", "add-housingtickets165", "add-housingstables165",
                 "add-routefloor165"):
        p = wg.RUNTIME + "/true165/" + name + ".json"
        d = json.load(open(p, encoding="utf-8"))
        ids = {row.get("PathID") or row.get("Id_nb") or row.get("ItemTemplateID") for row in d["rows"]}
        ridden = {r["ticket"] for r in routes}
        content[name] = dict(rows=len(d["rows"]), ids=len(ids), ridden=len(ids & ridden),
                             notRidden=sorted(ids - ridden)[:12])

    # ---- checks
    sids = {s["id"] for s in stable_rows}
    assert all(r["from"] in sids and r["to"] in sids for r in routes), "a route points at an unknown stable"
    assert len({r["ticket"] for r in routes}) == len(routes), "one ticket id twice in the routes"
    for r in routes:
        assert r["seconds"] > 0 and r["length"] > 0 and r["path"] and r["path"][0]["pts"], "empty route " + r["ticket"]
        assert r["kind"] in ("horse", "flying")
        assert abs(r["seconds"] - r["length"] / SPEED) < 0.11 or r["seconds"] > r["length"] / SPEED
    flying = [r for r in routes if r["kind"] == "flying"]
    assert {r["mount"] for r in flying} <= {"dragonfly", "gryphon", "wyvern"}, {r["mount"] for r in flying}
    assert len(routes) > 150, "too few routes: %d" % len(routes)
    n_tickets = sum(v["tickets"] for v in stables)
    assert {s["land"] for s in stable_rows} <= {1, 2, 3}, "a stable stands in a region with no realm in LAND"
    assert n_tickets == len(routes) + len(broken), "tickets %d != routes %d + broken %d" % (n_tickets, len(routes), len(broken))
    free = all(r["price"] == 0 for r in routes)
    town_of = {s["id"]: s["town"] for s in stable_rows}
    for r, (ex, ey, rg) in zip(routes, route_ends):          # the destination is never a close call
        if stable_rows[r["to"] - 1]["master"]:
            other = min((math.hypot(o["rx"] - ex, o["ry"] - ey) for o in by_region[rg]
                         if town_of[o["sid"]] != town_of[r["to"]]), default=1e12)
            assert other >= 2 * max(r["gap"], 1), "ambiguous destination for %s: %d against %d" % (r["ticket"], r["gap"], other)
    zfile = os.path.join(a.out, "zones.js")
    if os.path.exists(zfile):                      # P-D's zone list, when it is there: every zone we use must be in it
        t = open(zfile, encoding="utf-8").read()
        known = {z["id"] for z in json.loads(t[t.index(",") + 1:t.rindex(")")])["zones"]}
        used = {s["zone"] for s in stable_rows} | {p["zone"] for r in routes for p in r["path"]} |             {g[k]["zone"] for g in gates for k in ("from", "to")} | {t_["zone"] for t_ in teleporters}
        assert used <= known, "zones not in zones.js: %s" % sorted(used - known)

    notes = [
        "Every ticket costs %s." % ("nothing: the ticket items have price 0 and the stable master charges the item price "
                                    "(GameStableMaster.cs:63)" if free else "its item price"),
        "A ride starts %d seconds after you buy the ticket; the mount moves at %d units a second the whole way. "
        "seconds is the ride itself (3D path length / %d + stops)." % (START_DELAY, SPEED, SPEED),
        "One-way: a connection exists only in the direction listed. Routes of a faction stable master need that "
        "faction's reputation (see reputation); realm stable masters of your own realm never ask.",
        ("Reputation is earned through the faction's emissary hunt and by kills." if em_live else
         "Reputation is earned by kills only: True 1.65 does not have the reputation emissaries or their hunt."),
        "No boat routes: no boat stable master stands in the world.",
        "gates are the server's zone points (walk-through doors between regions); teleporters are the Shrouded Isles "
        "teleporter NPCs. The old-frontier and battleground teleporters and the medallion merchants are not covered.",
    ]
    meta = dict(built=wg.stamp(), stables=len(stables), landings=len(landings), routes=len(routes), broken=len(broken),
                flying=len(flying), free=free, mode165=mode165, emissaries=em_live, speed=SPEED, startDelay=START_DELAY, boardDistance=BOARD,
                arriveMax=ARRIVE_MAX, simplify=SIMPLIFY, leftOut=dict(left), gatesLeftOut=dict(gates_left),
                pathPointsInNoZone=lost_points, checks=dict(check), content=content,
                otherTeleporterNpcs=[list(x) for x in other_tele if x[0] not in tele_classes],
                quest=dict(kills=kills, gain=gain, gainRerolled=gain_reroll, levelCap=cap, levelWindow=window,
                           minSpawns=min_spawns, killChance=20))
    wg.write_data("routes", dict(meta=meta, notes=notes, stables=stable_rows, routes=routes, broken=broken,
                                 factions=fac_out, gates=gates, teleporters=teleporters), a.out,
                  "%d stables, %d routes (%d flying), %d broken" % (len(stable_rows), len(routes), len(flying), len(broken)))

    print("stable masters: %d (+%d arrival points with no stable master); tickets on their lists: %d"
          % (len(stables), len(landings), n_tickets))
    print("routes: %d  (horse %d, flying %d)   broken: %d   free: %s" % (len(routes), len(routes) - len(flying), len(flying),
                                                                       len(broken), free))
    for b in broken:
        print("  BROKEN %-38s from %-22s %s" % (b["ticket"], b["fromName"], b["why"]))
    print("masters with no working ticket:", [v["name"] for v in stables if not any(r["from"] == v["sid"] for r in routes)])
    print("left out:", dict(left), dict(gates_left), "path points in no zone:", lost_points, dict(check))
    print("routes that end more than 2,500 from their stable master:", arrive_far)
    sid_town = {s["id"]: s["town"] for s in stable_rows}
    print("towns with more than one stable master:", [[s["name"] for s in stable_rows if s["town"] == t]
          for t in sorted({s["town"] for s in stable_rows}) if sum(1 for s in stable_rows if s["town"] == t) > 1])
    norm = lambda x: re.sub(r"[^a-z]", "", (x or "").lower())
    print("ticket name differs from the stop's name:", sorted({(r["toName"], sid_place[r["to"]]) for r in routes
          if norm(r["toName"]) != norm(sid_place[r["to"]])}))
    print("arrival points with no stable master:", [(L["place"], L["zone"]["name"]) for L in landings])
    back = {(r["from"], r["to"]) for r in routes}
    back = {(sid_town[r["from"]], sid_town[r["to"]]) for r in routes}
    oneway = [(r["fromName"], r["toName"]) for r in routes if (sid_town[r["to"]], sid_town[r["from"]]) not in back]
    print("one-way connections (%d): %s" % (len(oneway), oneway))
    print("reputation: need aggro <= %d;" % need_aggro,
          {f["name"]: dict(base=f["base"], gated=[s for s in f["stables"]], hunts=len(f["hunts"]),
                           emissary=f["emissary"] and f["emissary"]["name"],
                           off=f["emissaryOff"] and f["emissaryOff"]["name"], killsNeeded=f["killsNeeded"],
                           killMobs=len(f["killMobs"]), killsHelp=f["killsHelp"]) for f in fac_out.values()})
    print("emissaries spawn in this world:", em_live, " classic165_mode:", mode165)
    print("gated routes:", [(r["fromName"], r["toName"], r["reputation"]["factionName"]) for r in routes if r["reputation"]])
    d = sorted(abs(r["seconds"] - r["botSeconds"]) for r in routes)
    print("ride seconds: min %.0f  median %.0f  max %.0f;  |player - bot planner| median %.1f max %.1f" % (
        min(r["seconds"] for r in routes), sorted(r["seconds"] for r in routes)[len(routes) // 2],
        max(r["seconds"] for r in routes), d[len(d) // 2], d[-1]))
    print("gates: %d   SI teleporters: %d %s" % (len(gates), len(teleporters),
                                                 [(t["name"], [x["name"] for x in t["to"]]) for t in teleporters]))
    print("content cross-check:", {k: (v["rows"], v["ids"], v["ridden"]) for k, v in content.items()})
    return 0


if __name__ == "__main__":
    sys.exit(main())
