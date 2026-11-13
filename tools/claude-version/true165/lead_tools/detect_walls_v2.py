"""Wall audit detector v2 (lane DETECT, 2026-11-12). Which spawns and points stand where a player cannot walk: behind the
client's one-sided zone walls (content/zones/zoneNNN/bound.json = the real 1.64 bound.csv).

Differences from detect_walls.py (v1):
- reachability from tools.reach_map(): 16 units/pixel, walls 1 px (v1: 32 units, 3 px - sealed gaps under ~100 units),
  per-zone rasters joined across borders, and the walls ONE-SIDED as zone_bounds.gd builds them (a player may walk in
  through a wall's back, never out through its face);
- seeds are player arrival points only - bind stones, zone-point and Teleport arrivals, StartupLocation, Keep rows (the
  battlegrounds' portal keeps), literal MoveTo / GameLocation arrivals in the server source (BGTeleporter: the only
  arrivals Abermenai has), client horse route ends. v1 also seeded from service NPCs, which hid a merchant standing in
  a pocket behind a wall (its own pocket became "reachable");
- "seeded pockets": a component reached only through a seed of its own that is tiny next to the region's main area is
  reported too (a bind stone or arrival behind a wall), with its mobs as status "seeded_pocket";
- a server navmesh cross-check for every flagged row and a random sample of the others: is the row on the navmesh
  (the server's own snap: 32 units across, 64 up/down) and is that navmesh piece connected to the seeds?
- non-Mob points: bind stones, zone-point / teleport arrivals, StartupLocations, quest markers / custom spots / quest
  spawns in classic-quests.json, PathPoints -> out/points_v2.json.

Usage: python -I detect_walls_v2.py [world.db] [--out DIR] [--quests classic-quests.json] [--no-nav]
Reads the world read-only. Writes <out>/flagged_v2.json, points_v2.json, navcheck_v2.json, summary_v2.json.
"""
import json, math, os, random, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tools
import numpy as np

args = [a for a in sys.argv[1:] if not a.startswith("--")]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
if "--out" in sys.argv:
    OUT = sys.argv[sys.argv.index("--out") + 1]
    args = [a for a in args if a != OUT]
NAV = "--no-nav" not in sys.argv
if args:
    tools.set_world(args[0])
os.makedirs(OUT, exist_ok=True)
QUESTS = tools.ROOT + "/runtime/server/classic-quests.json"     # the LIVE file (lanes edit it): --quests <file> for another
if "--quests" in sys.argv:
    QUESTS = sys.argv[sys.argv.index("--quests") + 1]
    args = [a for a in args if a != QUESTS]
POCKET_FRACTION = 0.02          # a seeded component under 2 % of the region's main area is a pocket
SERVICE = ("Merchant", "Trainer", "Healer", "StableMaster", "VaultKeeper", "Emblemeer", "GuildRegistrar", "Hastener",
           "Enchanter", "Smith", "Recharger")
c = tools.db()
t0 = time.time()


def log(*a):
    print("[%5.0fs]" % (time.time() - t0), *a, flush=True)


# ---------------------------------------------------------------- quest names (as v1)
quest_names = set()
qd = {}
try:
    qd = json.load(open(QUESTS, encoding="utf-8"))
    def walk(o):
        if isinstance(o, dict):
            for k, v in o.items():
                if k in ("Mob", "Giver", "Npc", "NPC", "Target", "Name", "TurnIn") and isinstance(v, str):
                    quest_names.add(v.lower())
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(qd)
except Exception as e:
    print("classic-quests.json not read:", e)
for r in c.execute("select StartName, SourceName, TargetName from DataQuest"):
    for col in r:
        for part in str(col or "").replace("|", ";").split(";"):
            part = part.strip()
            if part and not part.isdigit():
                quest_names.add(part.lower())
added = {r[0] for r in c.execute("select RowKey from offline_true165_added where SourceTable='Mob'")}
v1 = {}
try:
    v1 = {r["mob_id"]: r for r in json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "flagged.json"), encoding="utf-8"))}
except OSError:
    pass

regions = sorted(r for r in {z["region"] for z in tools.zones().values()} if tools.walled(r))


# ---------------------------------------------------------------- main area / pockets per region
def main_comps(region):
    R = tools.reach_map(region)
    if not R.seed_comps:
        return set(), R
    big = max(R.seed_comps, key=lambda k: R.comp_size[k])
    main = {big}
    todo = [big]
    while todo:
        k = todo.pop()
        for n in R.adj.get(k, ()):
            if n not in main:
                main.add(n); todo.append(n)
    area = sum(R.comp_size[k] for k in main)
    # other seeded components that are big (a separate island with its own arrivals) count as main too
    for k in R.seed_comps:
        if k not in main and R.comp_size[k] >= POCKET_FRACTION * area:
            main.add(k)
            todo = [k]
            while todo:
                q = todo.pop()
                for n in R.adj.get(q, ()):
                    if n not in main:
                        main.add(n); todo.append(n)
    R.main = main
    R.main_area = area
    return main, R


def status_of(region, x, y):
    """'ok' / 'unreachable' / 'seeded_pocket' / None (outside every zone or zone without walls -> 'ok')."""
    z = tools.zone_at(region, x, y)
    if z is None:
        return None, None
    if not tools.wall_lines(z["id"]):
        return "ok", None
    R = tools.reach_map(region)
    k = tools._comp_at(R, region, x, y)
    if k is None or k < 0:
        return None, None
    if k in R.main:
        return "ok", k
    if k in R.reached_comps:
        return "seeded_pocket", k
    return "unreachable", k


# ---------------------------------------------------------------- navmesh graph per region
_navg = {}


def nav_graph(region):
    """Union of per-zone navmesh components across zone borders; returns (find(zone, comp) -> root, seeded roots)."""
    if region in _navg:
        return _navg[region]
    parent = {}

    def find(a):
        while parent.setdefault(a, a) != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb

    rz = [z for z in tools.region_zones(region) if tools.navmesh(z["id"]) is not None]
    for z1 in rz:
        m1 = tools.navmesh(z1["id"])
        for z2 in rz:
            m2 = tools.navmesh(z2["id"])
            pairs = []
            if z2["ox"] == z1["ox"] + z1["w"]:
                y0, y1 = max(z1["oy"], z2["oy"]), min(z1["oy"] + z1["h"], z2["oy"] + z2["h"])
                bx = z2["ox"]
                pairs = [((bx - 24, y), (bx + 24, y)) for y in range(y0 + 16, y1, 48)]
            elif z2["oy"] == z1["oy"] + z1["h"]:
                x0, x1 = max(z1["ox"], z2["ox"]), min(z1["ox"] + z1["w"], z2["ox"] + z2["w"])
                by = z2["oy"]
                pairs = [((x, by - 24), (x, by + 24)) for x in range(x0 + 16, x1, 48)]
            for (ax, ay), (bx_, by_) in pairs:
                fa = tools._floors_in_zone(z1["id"], ax, ay) or []
                fb = tools._floors_in_zone(z2["id"], bx_, by_) or []
                for za, pa in fa:
                    for zb, pb in fb:
                        if abs(za - zb) <= 48:
                            union((z1["id"], int(m1["comp"][pa])), (z2["id"], int(m2["comp"][pb])))
    seeded = set()
    for x, y, kind, label in tools.seeds(region):
        z = tools.zone_at(region, x, y)
        if z is None or tools.navmesh(z["id"]) is None:
            continue
        ref = tools.terrain_z(region, x, y)
        hit = None
        for dx, dy in ((0, 0), (32, 0), (-32, 0), (0, 32), (0, -32), (64, 64), (-64, -64), (64, -64), (-64, 64)):
            f = tools._floors_in_zone(z["id"], x + dx, y + dy)
            if f:
                zz, pid = min(f, key=lambda q: abs(q[0] - ref)) if ref is not None else f[-1]
                hit = (z["id"], int(tools.navmesh(z["id"])["comp"][pid]))
                break
        if hit:
            seeded.add(find(hit))
    # main nav piece = the seeded piece with most polys (approximate size: count of polys per root)
    _navg[region] = (find, seeded)
    return _navg[region]


def nav_check(region, x, y, z):
    zz = tools.zone_at(region, x, y)
    if zz is None or tools.navmesh(zz["id"]) is None:
        return dict(nav="no navmesh")
    hit = tools.nav_poly(region, x, y, z)
    if hit is None:
        # NPCs are not snapped at spawn; a wider look tells "near the mesh" from "far off it"
        f = tools.nav_floors(region, x, y) or []
        return dict(nav="off mesh", floors=[round(q[0]) for q in f][:4])
    find, seeded = nav_graph(region)
    zid, pid, fz, off = hit
    root = find((zid, int(tools.navmesh(zid)["comp"][pid])))
    return dict(nav="connected" if root in seeded else "isolated", floor_z=round(fz), dz=round(fz - z))


# ---------------------------------------------------------------- run
if NAV:
    log("warming navmesh caches for", len(regions), "regions")
    for r in regions:
        for z in tools.region_zones(r):
            tools.navmesh(z["id"])
    log("navmeshes ready")

summary, report, sample_ok = {}, [], []
random.seed(1165)
pockets = []
for region in regions:
    main, R = main_comps(region)
    for k, ss in R.seed_comps.items():
        if k not in main:
            pockets.append(dict(region=region, component=int(k), area=float(R.comp_size[k]) * tools.SCALE ** 2,
                                main_area=float(R.main_area) * tools.SCALE ** 2,
                                seeds=[dict(kind=s[2], label=str(s[3]), x=s[0], y=s[1],
                                            local=tools.local(region, s[0], s[1])) for s in ss]))
    rows = [dict(m) for m in c.execute(
        "select Mob_ID, Name, Guild, ClassType, Level, Region, X, Y, Z, PackageID, NPCTemplateID, Flags, AggroLevel, "
        "PathID, RespawnInterval from Mob where Region=?", (region,))]
    nflag = npocket = 0
    ok_rows = []
    for m in rows:
        st, k = status_of(region, m["X"], m["Y"])
        if st is None:
            continue
        if st == "ok":
            ok_rows.append(m)
            continue
        z = tools.zone_at(region, m["X"], m["Y"])
        ws = tools.wall_side(region, m["X"], m["Y"])
        name = m["Name"] or ""
        sug = tools.nearest_reachable(region, m["X"], m["Y"], 150)
        period = [dict(zone=s["zone"], lx=s["lx"], ly=s["ly"], z=s["z"], level=s["level"], reachable=s["reachable"])
                  for s in tools.period_sightings(name) if s["region"] == region]
        row = dict(
            status=st, mob_id=m["Mob_ID"], name=name, guild=m["Guild"], cls=m["ClassType"], level=m["Level"],
            region=region, zone=z["id"], zone_name=z["name"], x=m["X"], y=m["Y"], z=m["Z"], lx=m["X"] - z["ox"],
            ly=m["Y"] - z["oy"], wall_dist=round(ws[0]) if ws else None, wall_zone=ws[2] if ws else None,
            wrong_side=(not ws[1]) if ws else None, component=int(k),
            component_area=round(float(R.comp_size[k]) * tools.SCALE ** 2),
            package=m["PackageID"], template=m["NPCTemplateID"], flags=m["Flags"], aggro=m["AggroLevel"],
            path=m["PathID"], respawn=m["RespawnInterval"], added_by_true165=m["Mob_ID"] in added,
            quest_npc=name.lower() in quest_names, service=any(s in (m["ClassType"] or "") for s in SERVICE),
            in_v1=m["Mob_ID"] in v1,
            nearest_inside=None if sug is None else dict(
                x=sug[0], y=sug[1], dist=round(sug[2]), lx=sug[0] - z["ox"], ly=sug[1] - z["oy"],
                ground_z=None if tools.ground_z(region, sug[0], sug[1], m["Z"]) is None
                else round(tools.ground_z(region, sug[0], sug[1], m["Z"]))),
            period=period)
        if NAV:
            row.update(nav_check(region, m["X"], m["Y"], m["Z"]))
        report.append(row)
        nflag += st == "unreachable"
        npocket += st == "seeded_pocket"
    if NAV:
        for m in random.sample(ok_rows, min(60, len(ok_rows))):
            e = dict(mob_id=m["Mob_ID"], name=m["Name"], region=region, x=m["X"], y=m["Y"], z=m["Z"])
            e.update(nav_check(region, m["X"], m["Y"], m["Z"]))
            sample_ok.append(e)
    summary[region] = dict(zones=len(tools.region_zones(region)), seeds=len(tools.seeds(region)),
                           seeded_components=len(R.seed_comps), mobs=len(rows), unreachable=nflag,
                           seeded_pocket=npocket)
    log("region", region, summary[region])

# ---------------------------------------------------------------- non-Mob points
points = []


def add_point(kind, label, region, x, y, z=None, extra=None):
    if region is None or x is None or y is None:
        return
    if not tools.walled(region):
        return
    st, k = status_of(region, x, y)
    if st in (None, "ok"):
        return
    zz = tools.zone_at(region, x, y)
    ws = tools.wall_side(region, x, y)
    sug = tools.nearest_reachable(region, x, y, 150)
    e = dict(kind=kind, label=label, status=st, region=region, zone=zz["id"], x=x, y=y, z=z, lx=x - zz["ox"],
             ly=y - zz["oy"], wall_dist=round(ws[0]) if ws else None, wrong_side=(not ws[1]) if ws else None,
             nearest_inside=None if sug is None else dict(x=sug[0], y=sug[1], dist=round(sug[2]),
                                                          ground_z=tools.ground_z(region, sug[0], sug[1], z)))
    if extra:
        e.update(extra)
    points.append(e)


for r in c.execute("select X, Y, Z, Region, BindPoint_ID from BindPoint"):
    add_point("bind", r[4], r[3], r[0], r[1], r[2])
for r in c.execute("select TargetX, TargetY, TargetZ, TargetRegion, Id, SourceRegion from ZonePoint"):
    add_point("zonepoint_arrival", "ZonePoint %s (from region %s)" % (r[4], r[5]), r[3], r[0], r[1], r[2])
for r in c.execute("select X, Y, Z, RegionID, TeleportID, Type from Teleport"):
    add_point("teleport_arrival", "%s (%s)" % (r[4], r[5]), r[3], r[0], r[1], r[2])
for r in c.execute("select XPos, YPos, ZPos, Region, StartupLoc_ID from StartupLocation"):
    add_point("startup", r[4], r[3], r[0], r[1], r[2])
for reg, x, y, src in tools.hardcoded_arrivals():
    add_point("code_arrival", src, reg, x, y)
for reg, x, y, name in tools.horse_route_ends():
    add_point("horse_route", name, reg, x, y)
for qkey, q in (qd.get("Quests") or {}).items():
    for i, st in enumerate(q.get("Steps") or []):
        if not isinstance(st, dict):
            continue
        for fld in ("Marker", "Custom", "Spawn"):
            p = st.get(fld)
            if isinstance(p, dict) and "X" in p:
                add_point("quest_" + fld.lower(), "%s step %d%s" % (qkey, i, (" " + p["Name"]) if p.get("Name") else ""),
                          p.get("Region"), p["X"], p["Y"], p.get("Z"))
path_region = {r[0]: r[1] for r in c.execute("select PathID, RegionID from Path")}
for r in c.execute("select PathID, Step, X, Y, Z from PathPoints"):
    add_point("pathpoint", "%s step %s" % (r[0], r[1]), path_region.get(r[0]), r[2], r[3], r[4])

json.dump(report, open(os.path.join(OUT, "flagged_v2.json"), "w", encoding="utf-8"), indent=1)
json.dump(dict(points=points, seeded_pockets=pockets), open(os.path.join(OUT, "points_v2.json"), "w", encoding="utf-8"), indent=1)
if NAV:
    json.dump(dict(flagged=[{k: r[k] for k in ("mob_id", "name", "region", "status", "nav") if k in r} for r in report],
                   sample_reachable=sample_ok), open(os.path.join(OUT, "navcheck_v2.json"), "w", encoding="utf-8"), indent=1)
json.dump(summary, open(os.path.join(OUT, "summary_v2.json"), "w", encoding="utf-8"), indent=1)
unr = [r for r in report if r["status"] == "unreachable"]
log("flagged rows:", len(unr), "unreachable +", len(report) - len(unr), "in seeded pockets | quest NPCs:",
    sum(r["quest_npc"] for r in report), "| service:", sum(r["service"] for r in report),
    "| in v1:", sum(r["in_v1"] for r in report), "of v1", len(v1), "| points:", len(points), "| pockets:", len(pockets))
gone = [m for m in v1 if m not in {r["mob_id"] for r in report}]
log("v1 rows no longer flagged:", len(gone))
for r in report:
    if r["name"].lower() == "spackit":
        log("Spackit:", r["status"], r["lx"], r["ly"], "->", r["nearest_inside"], "period", r["period"], r.get("nav"))
