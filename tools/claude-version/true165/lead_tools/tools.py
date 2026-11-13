"""Shared helpers for the wall audit (lane DETECT, 2026-11-12). Pure Python 3.10 + numpy / Pillow / scipy, read-only on
worlds and client files. See TOOLS.md for usage.

    import sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import tools
    tools.is_reachable(200, x, y)            # region coordinates (the Mob table's X, Y)
    tools.nearest_reachable(200, x, y, 150)  # -> (x, y, distance) on the playable side, >= 150 units from every wall
    tools.ground_z(200, x, y, near_z=None)   # -> Z in game units (server navmesh floor, else client terrain)
    tools.period_sightings("Spackit")        # -> CapnBry radar entries with zone-local + region coordinates

Walls: the client's bound.json per zone (the real 1.64 bound.csv), one-sided as game/world/zone_bounds.gd builds them:
the face looks to the playable side (the line's right, x east / y south); walking out through the face is stopped,
walking in from the back passes. Reachability: each zone rasterised at SCALE units per pixel with every wall drawn
1 pixel (an 8-connected line: watertight for the 4-connected labelling; it seals gaps narrower than ~SCALE units, the
player's capsule is 20 units wide), 4-connected components labelled, components joined across zone borders, then a
directed pass: from a component on a wall's back side you may cross to the component on its face side, never the
other way. A component is reachable when a player arrival point (seeds(), no NPCs) lies in it or it can be entered
from one that is.
"""
import glob, json, math, os, re, sqlite3, struct, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.environ.get("OFFLINE_DAOC_ROOT") or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))
CLIENT = os.environ.get("OFFLINE_DAOC_GODOT_CONTENT") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."), "source", "new-client", "godot", "content")
SERVER_SRC = ROOT + "/development-source/server/GameServer"
NAV_DIRS = [ROOT + "/runtime/server/navmesh-165", ROOT + "/runtime/server/navmesh"]   # the server's own order (165 mode)
SIGHTINGS = ROOT + "/development-source/research/period-data/archive/capnbry/sightings_index.json"
CACHE = os.path.join(HERE, "out", "cache")
WORLD = os.environ.get("OFFLINE_DAOC_TEST_WORLD") or os.path.join(ROOT, "runtime", "data", "opendaoc-165.sqlite3.db")
SCALE = 16            # game units per pixel
EDGE_SNAP = 1024      # a wall ending this close to an edge of the world is drawn on to it
SEAM = 64             # a gap this narrow where two zones' walls meet on a border counts as closed (data rounding)
U = 0.025             # game units -> metres (client)
NAV_F = 1.0 / 32.0    # game units -> recast units (server PathfindingMgrBase.CONVERSION_FACTOR); recast = (x, z, y)

_db = None
_zones = None
_byregion = None


def set_world(path):
    """Read another derived world (read-only). Clears every cache that depends on the world."""
    global WORLD, _db, _zones, _byregion
    WORLD = path
    _db = None; _zones = None; _byregion = None
    _reach.clear(); _seeds.clear()


def db():
    global _db
    if _db is None:
        _db = sqlite3.connect("file:" + WORLD.replace(" ", "%20") + "?mode=ro", uri=True)
        _db.row_factory = sqlite3.Row
    return _db


# ---------------------------------------------------------------- zones and walls

def zones():
    global _zones, _byregion
    if _zones is None:
        _zones, _byregion = {}, {}
        for z in db().execute("select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height from Zones"):
            d = dict(id=z["ZoneID"], region=z["RegionID"], name=z["Name"], ox=z["OffsetX"] * 8192,
                     oy=z["OffsetY"] * 8192, w=z["Width"] * 8192, h=z["Height"] * 8192)
            _zones[d["id"]] = d
            _byregion.setdefault(d["region"], []).append(d)
    return _zones


def region_zones(region):
    zones()
    return _byregion.get(region, [])


def zone_at(region, x, y):
    for z in region_zones(region):
        if z["ox"] <= x < z["ox"] + z["w"] and z["oy"] <= y < z["oy"] + z["h"]:
            return z
    return None


_lines = {}


def wall_lines(zone_id):
    """The zone's wall polylines, zone-local: [[x1, y1, x2, y2, ...], ...] ([] without a bound.json)."""
    if zone_id not in _lines:
        p = os.path.join(CLIENT, "zones", "zone%03d" % zone_id, "bound.json")
        _lines[zone_id] = json.load(open(p, encoding="utf-8")).get("lines", []) if os.path.exists(p) else []
    return _lines[zone_id]


def wall_segments(region):
    """Every wall segment of the region in region coordinates: [((ax, ay), (bx, by), zone_id), ...]."""
    out = []
    for z in region_zones(region):
        for ln in wall_lines(z["id"]):
            pts = [(z["ox"] + ln[i], z["oy"] + ln[i + 1]) for i in range(0, len(ln) - 1, 2)]
            for a, b in zip(pts, pts[1:]):
                if a != b:
                    out.append((a, b, z["id"]))
    return out


def walled(region):
    return any(wall_lines(z["id"]) for z in region_zones(region))


_segarr = {}


def _segs_np(region):
    if region not in _segarr:
        s = wall_segments(region)
        a = np.array([[p[0][0], p[0][1], p[1][0], p[1][1]] for p in s], dtype=np.float64).reshape(-1, 4)
        _segarr[region] = (a, [p[2] for p in s])
    return _segarr[region]


def wall_side(region, x, y):
    """(distance to the nearest wall, True when on its playable side, wall zone id) - zone_bounds.gd side(): the nearest
    segment decides, at a shared corner (equal distance) the normals add. None in a region without walls."""
    a, zids = _segs_np(region)
    if not len(a):
        return None
    ax, ay, bx, by = a[:, 0], a[:, 1], a[:, 2], a[:, 3]
    dx, dy = bx - ax, by - ay
    L = dx * dx + dy * dy
    t = np.clip(((x - ax) * dx + (y - ay) * dy) / np.where(L == 0, 1, L), 0, 1)
    qx, qy = ax + t * dx, ay + t * dy
    d = np.hypot(x - qx, y - qy)
    i = int(np.argmin(d))
    near = np.abs(d - d[i]) <= 0.001
    nx = (-dy[near] / np.sqrt(L[near])).sum()
    ny = (dx[near] / np.sqrt(L[near])).sum()
    playable = (x - qx[i]) * nx + (y - qy[i]) * ny >= 0
    return float(d[i]), bool(playable), zids[i]


# ---------------------------------------------------------------- seeds: where a player is known to stand

_hard = None


def hardcoded_arrivals():
    """(region, x, y, source) for every literal MoveTo(region, x, y, z ...) / new GameLocation(name, region, x, y, z ...)
    in the server source (battleground teleporters, scripted ports...)."""
    global _hard
    if _hard is None:
        _hard = []
        pats = [re.compile(r"MoveTo\(\s*(?:\(ushort\))?\s*(\d{1,3})\s*,\s*(\d{3,6})\s*,\s*(\d{3,6})\s*,\s*-?\d{1,6}"),
                re.compile(r"new\s+GameLocation\(\s*\"[^\"]*\"\s*,\s*(?:\(ushort\))?\s*(\d{1,3})\s*,\s*(\d{3,6})\s*,\s*(\d{3,6})\s*,\s*-?\d{1,6}")]
        for f in glob.glob(SERVER_SRC + "/**/*.cs", recursive=True):
            if os.sep + "obj" + os.sep in f or "/obj/" in f.replace("\\", "/"):
                continue
            try:
                txt = open(f, encoding="utf-8", errors="replace").read()
            except OSError:
                continue
            for p in pats:
                for m in p.finditer(txt):
                    line = txt.count("\n", 0, m.start()) + 1
                    _hard.append((int(m.group(1)), int(m.group(2)), int(m.group(3)),
                                  os.path.relpath(f, SERVER_SRC).replace("\\", "/") + ":%d" % line))
    return _hard


_routes = None


def horse_route_ends():
    """(region, x, y, route name) for the start and end of every client horse route (content/world/routes.json; region
    from the stable master nearest the route's first point)."""
    global _routes
    if _routes is None:
        _routes = []
        p = os.path.join(CLIENT, "world", "routes.json")
        if os.path.exists(p):
            routes = json.load(open(p, encoding="utf-8")).get("routes", {})
            stables = [(r[0], r[1], r[2]) for r in db().execute(
                "select Region, X, Y from Mob where ClassType like '%Stable%' or ClassType like '%Taxi%'")]
            for key, r in routes.items():
                pts = r.get("points") or []
                if not pts or not stables:
                    continue
                sx, sy = pts[0][0], pts[0][1]
                reg = min(stables, key=lambda s: (s[1] - sx) ** 2 + (s[2] - sy) ** 2 if zone_at(s[0], sx, sy) else 1e30)
                if (reg[1] - sx) ** 2 + (reg[2] - sy) ** 2 > 3000 ** 2:
                    continue
                _routes.append((reg[0], pts[0][0], pts[0][1], key + " start"))
                _routes.append((reg[0], pts[-1][0], pts[-1][1], key + " end"))
    return _routes


_seeds = {}


def seeds(region):
    """Points a player is placed at or starts from in the region: bind stones, zone-point arrivals, Teleport rows,
    StartupLocation rows, Keep rows (portal keeps), hard-coded arrivals in the server source, horse route ends.
    [(x, y, kind, label)]. NPCs are never seeds (a merchant behind a wall must not make its own pocket reachable)."""
    if region in _seeds:
        return _seeds[region]
    c = db()
    s = []
    for r in c.execute("select X, Y, BindPoint_ID from BindPoint where Region=?", (region,)):
        s.append((r[0], r[1], "bind", r[2]))
    for r in c.execute("select TargetX, TargetY, Id from ZonePoint where TargetRegion=?", (region,)):
        s.append((r[0], r[1], "zonepoint", "ZonePoint %s" % r[2]))
    for r in c.execute("select X, Y, TeleportID from Teleport where RegionID=?", (region,)):
        s.append((r[0], r[1], "teleport", r[2]))
    for r in c.execute("select XPos, YPos, StartupLoc_ID from StartupLocation where Region=?", (region,)):
        s.append((r[0], r[1], "startup", "StartupLocation %s" % r[2]))
    for r in c.execute("select X, Y, Name from Keep where Region=?", (region,)):
        s.append((r[0], r[1], "keep", r[2]))            # keeps and portal keeps (battleground arrivals stand beside them)
    for reg, x, y, src in hardcoded_arrivals():
        if reg == region:
            s.append((x, y, "code", src))
    for reg, x, y, name in horse_route_ends():
        if reg == region:
            s.append((x, y, "horse", name))
    s = [t for t in s if t[0] is not None and t[1] is not None]
    _seeds[region] = s
    return s


# ---------------------------------------------------------------- reachability raster

class _Reach:
    pass


_reach = {}


def _px(z, x, y):
    return int((x - z["ox"]) // SCALE), int((y - z["oy"]) // SCALE)


def reach_map(region):
    """Builds (once per region) the labelled zone rasters and the reachable-component set. Returns an object with
    .zones {zone_id: (zone, labels ndarray, label offset)}, .reached (bool ndarray by global label), .size (pixels by
    label), .seed_labels {label: [seed, ...]}."""
    if region in _reach:
        return _reach[region]
    from scipy import ndimage
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    R = _Reach()
    R.zones = {}
    off = 1
    rz = region_zones(region)
    snaps = []
    R.edge_snaps = snaps
    for z in rz:
        W, H = z["w"] // SCALE, z["h"] // SCALE
        img = Image.new("L", (W, H), 255)
        d = ImageDraw.Draw(img)
        for ln in wall_lines(z["id"]):
            pts = [(ln[i] / SCALE, ln[i + 1] / SCALE) for i in range(0, len(ln) - 1, 2)]
            if len(pts) >= 2:
                d.line(pts, fill=0, width=1)
            # a wall that stops just short of a zone edge with no zone beyond (the edge of the world): the strip between
            # its end and the edge would let the flood walk round it; drawn on to the edge (R.edge_snaps lists them)
            for ex, ey in ((ln[0], ln[1]), (ln[-2], ln[-1])):
                for side, dist, tx, ty in (("W", ex, 0, ey), ("E", z["w"] - ex, z["w"], ey),
                                           ("N", ey, ex, 0), ("S", z["h"] - ey, ex, z["h"])):
                    if 1 < dist <= EDGE_SNAP and not _neighbour(rz, z, side, tx, ty):
                        d.line([(ex / SCALE, ey / SCALE), (min(tx, z["w"] - 1) / SCALE, min(ty, z["h"] - 1) / SCALE)],
                               fill=0, width=1)
                        snaps.append(dict(zone=z["id"], end=(ex, ey), edge=side, gap=dist))
        a = np.asarray(img) > 0
        lab, n = ndimage.label(a)
        lab = lab.astype(np.int32)
        lab[lab > 0] += off - 1
        R.zones[z["id"]] = (z, lab, off)
        off += n
    N = off
    R.size = np.zeros(N, dtype=np.int64)
    for z, lab, o in R.zones.values():
        R.size += np.bincount(lab.ravel(), minlength=N)[:N]
    # undirected joins across zone borders. A wall that ends on a zone edge is meant to go on in the next zone; where
    # the two zones' ends miss each other by a few units (data rounding: a seam of 1-3 pixels) the join is closed for
    # SEAM units either side of every wall end on that edge (R.seams lists the seams that this closes).
    ends = {}                                                       # (edge kind, edge coord) -> [along coords]
    for z in rz:
        for ln in wall_lines(z["id"]):
            for i in range(0, len(ln) - 1, 2):
                lx, ly = ln[i], ln[i + 1]
                gx, gy = z["ox"] + lx, z["oy"] + ly
                if lx <= 1 or lx >= z["w"] - 1:
                    ends.setdefault(("x", z["ox"] if lx <= 1 else z["ox"] + z["w"]), []).append(gy)
                if ly <= 1 or ly >= z["h"] - 1:
                    ends.setdefault(("y", z["oy"] if ly <= 1 else z["oy"] + z["h"]), []).append(gx)
    R.seams = []
    for key, al in ends.items():
        al = sorted(set(al))
        for a1, a2 in zip(al, al[1:]):
            if 0 < a2 - a1 <= SEAM:
                R.seams.append(dict(edge=key[0], at=key[1], from_=a1, to=a2, gap=a2 - a1))

    def _closed(key, along):                                        # along: region coords of the border pixels
        e = np.array(ends.get(key, []), dtype=np.float64)
        if not len(e):
            return np.zeros(len(along), bool)
        return (np.abs(along[:, None] - e[None, :]) <= SEAM).any(axis=1)

    ea, eb = [], []
    for z1, l1, _ in R.zones.values():
        for z2, l2, _ in R.zones.values():
            if z2["ox"] == z1["ox"] + z1["w"]:                      # z2 east of z1
                y0, y1 = max(z1["oy"], z2["oy"]), min(z1["oy"] + z1["h"], z2["oy"] + z2["h"])
                if y1 > y0:
                    a = l1[(y0 - z1["oy"]) // SCALE:(y1 - z1["oy"]) // SCALE, -1]
                    b = l2[(y0 - z2["oy"]) // SCALE:(y1 - z2["oy"]) // SCALE, 0]
                    along = y0 + (np.arange(len(a)) + 0.5) * SCALE
                    m = (a > 0) & (b > 0) & ~_closed(("x", z2["ox"]), along); ea.append(a[m]); eb.append(b[m])
            if z2["oy"] == z1["oy"] + z1["h"]:                      # z2 south of z1
                x0, x1 = max(z1["ox"], z2["ox"]), min(z1["ox"] + z1["w"], z2["ox"] + z2["w"])
                if x1 > x0:
                    a = l1[-1, (x0 - z1["ox"]) // SCALE:(x1 - z1["ox"]) // SCALE]
                    b = l2[0, (x0 - z2["ox"]) // SCALE:(x1 - z2["ox"]) // SCALE]
                    along = x0 + (np.arange(len(a)) + 0.5) * SCALE
                    m = (a > 0) & (b > 0) & ~_closed(("y", z2["oy"]), along); ea.append(a[m]); eb.append(b[m])
    ea = np.concatenate(ea) if ea else np.zeros(0, np.int32)
    eb = np.concatenate(eb) if eb else np.zeros(0, np.int32)
    g = coo_matrix((np.ones(len(ea)), (ea, eb)), shape=(N, N))
    ncomp, comp = connected_components(g, directed=False)
    comp[0] = -1
    R.comp = comp
    # directed crossings: back side (left) -> face side (right) of a wall segment, allowed only when the short walk
    # from the back point to the face point meets no other wall face-first (a line drawn twice, once each way, stops
    # both ways; a parallel wall close behind, or the next segment round a corner, stops it too)
    segs_all, _z = _segs_np(region)
    sax, say, sbx, sby = (segs_all[:, i] if len(segs_all) else np.zeros(0) for i in range(4))
    sdx, sdy = sbx - sax, sby - say
    da, db_ = [], []
    o = 2.5 * SCALE
    for z, lab, _ in R.zones.values():
        H, W = lab.shape
        for ln in wall_lines(z["id"]):
            for i in range(0, len(ln) - 3, 2):
                ax, ay, bx, by = ln[i], ln[i + 1], ln[i + 2], ln[i + 3]
                L = math.hypot(bx - ax, by - ay)
                if L == 0:
                    continue
                nx, ny = -(by - ay) / L, (bx - ax) / L               # toward the playable side
                k = max(1, int(L / (SCALE * 2)))
                ts = (np.arange(k) + 0.5) / k
                px, py = ax + ts * (bx - ax), ay + ts * (by - ay)
                fxw, fyw = px + nx * o, py + ny * o
                bxw, byw = px - nx * o, py - ny * o
                fx = (fxw // SCALE).astype(int); fy = (fyw // SCALE).astype(int)
                bx_ = (bxw // SCALE).astype(int); by_ = (byw // SCALE).astype(int)
                ok = (fx >= 0) & (fx < W) & (fy >= 0) & (fy < H) & (bx_ >= 0) & (bx_ < W) & (by_ >= 0) & (by_ < H)
                if not ok.any():
                    continue
                # region segments near this one
                gx0, gy0 = z["ox"] + min(ax, bx) - 2 * o, z["oy"] + min(ay, by) - 2 * o
                gx1, gy1 = z["ox"] + max(ax, bx) + 2 * o, z["oy"] + max(ay, by) + 2 * o
                near = (np.maximum(sax, sbx) >= gx0) & (np.minimum(sax, sbx) <= gx1) &                        (np.maximum(say, sby) >= gy0) & (np.minimum(say, sby) <= gy1)
                if near.any():
                    P0x = (bxw + z["ox"])[:, None]; P0y = (byw + z["oy"])[:, None]
                    P1x = (fxw + z["ox"])[:, None]; P1y = (fyw + z["oy"])[:, None]
                    Ax, Ay, Dx, Dy = sax[near][None, :], say[near][None, :], sdx[near][None, :], sdy[near][None, :]
                    c0 = Dx * (P0y - Ay) - Dy * (P0x - Ax)               # > 0: walk start on that wall's face side
                    c1 = Dx * (P1y - Ay) - Dy * (P1x - Ax)
                    ex, ey = P1x - P0x, P1y - P0y
                    e0 = ex * (Ay - P0y) - ey * (Ax - P0x)
                    e1 = ex * (Ay + Dy - P0y) - ey * (Ax + Dx - P0x)
                    crosses = (c0 * c1 < 0) & (e0 * e1 <= 0)
                    blocked = (crosses & (c0 > 0)).any(axis=1)          # meets a wall face-first
                    ok &= ~blocked
                f = lab[fy[ok], fx[ok]]; b = lab[by_[ok], bx_[ok]]
                m = (f > 0) & (b > 0)
                da.append(comp[b[m]]); db_.append(comp[f[m]])
    da = np.concatenate(da) if da else np.zeros(0, np.int64)
    db_ = np.concatenate(db_) if db_ else np.zeros(0, np.int64)
    adj = {}
    for a_, b_ in set(zip(da.tolist(), db_.tolist())):
        if a_ != b_:
            adj.setdefault(a_, set()).add(b_)
    R.adj = adj
    # seeds
    R.seed_comps = {}
    for s in seeds(region):
        c_ = _comp_at(R, region, s[0], s[1])
        if c_ is not None and c_ >= 0:
            R.seed_comps.setdefault(c_, []).append(s)
    reached = set(R.seed_comps)
    todo = list(reached)
    while todo:
        c_ = todo.pop()
        for n_ in adj.get(c_, ()):
            if n_ not in reached:
                reached.add(n_); todo.append(n_)
    R.reached_comps = reached
    R.comp_size = np.bincount(comp[1:][comp[1:] >= 0], weights=R.size[1:][comp[1:] >= 0], minlength=ncomp)
    _reach[region] = R
    return R


def _neighbour(rz, z, side, lx, ly):
    """Is there a zone of the region across this zone's edge at zone-local (lx, ly)?"""
    gx, gy = z["ox"] + lx, z["oy"] + ly
    probe = {"W": (gx - 1, gy), "E": (gx + 1, gy), "N": (gx, gy - 1), "S": (gx, gy + 1)}[side]
    return any(o is not z and o["ox"] <= probe[0] < o["ox"] + o["w"] and o["oy"] <= probe[1] < o["oy"] + o["h"] for o in rz)


def _label_at(R, region, x, y):
    z = zone_at(region, x, y)
    if z is None or z["id"] not in R.zones:
        return None
    _, lab, _ = R.zones[z["id"]]
    ix, iy = _px(z, x, y)
    if 0 <= iy < lab.shape[0] and 0 <= ix < lab.shape[1]:
        return int(lab[iy, ix])
    return None


def _comp_at(R, region, x, y):
    """Component under a point; on a wall pixel, the component just beside it on the point's own side of the wall."""
    l = _label_at(R, region, x, y)
    if l is None:
        return None
    if l > 0:
        return int(R.comp[l])
    ws = wall_side(region, x, y)
    if ws is None:
        return None
    a, _ = _segs_np(region)
    # step away from the nearest wall on the point's side until an open pixel
    ax, ay, bx, by = a[:, 0], a[:, 1], a[:, 2], a[:, 3]
    dx, dy = bx - ax, by - ay
    Lq = dx * dx + dy * dy
    t = np.clip(((x - ax) * dx + (y - ay) * dy) / np.where(Lq == 0, 1, Lq), 0, 1)
    qx, qy = ax + t * dx, ay + t * dy
    d = np.hypot(x - qx, y - qy)
    i = int(np.argmin(d))
    L = math.sqrt(Lq[i]) or 1
    nx, ny = -dy[i] / L, dx[i] / L
    sgn = 1 if ws[1] else -1
    for k in range(1, 6):
        l2 = _label_at(R, region, x + sgn * nx * k * SCALE, y + sgn * ny * k * SCALE)
        if l2:
            return int(R.comp[l2])
    return None


def is_reachable(region, x, y):
    """True when a player can walk to (x, y) from a player arrival point without crossing a wall's face; False when
    not; None outside every zone of the region. In a region without walls: True."""
    if not walled(region):
        return True if zone_at(region, x, y) else None
    R = reach_map(region)
    c_ = _comp_at(R, region, x, y)
    if c_ is None:
        return None
    return c_ in R.reached_comps


def component_info(region, x, y):
    """Debug: (component id, its area in game units^2, reached?, seeds inside it)."""
    R = reach_map(region)
    c_ = _comp_at(R, region, x, y)
    if c_ is None or c_ < 0:
        return None
    return c_, float(R.comp_size[c_]) * SCALE * SCALE, c_ in R.reached_comps, R.seed_comps.get(c_, [])


def nearest_reachable(region, x, y, min_inside=150, max_radius=8000):
    """The nearest point to (x, y) that is reachable and at least min_inside units from every wall:
    (x, y, distance) or None within max_radius. Searched on the zone raster (distance transform of the walls in a
    window round the point), every candidate confirmed with is_reachable() and the exact wall_side()."""
    if is_reachable(region, x, y) and (wall_side(region, x, y) or (1e9,))[0] >= min_inside:
        return (x, y, 0.0)
    if not walled(region):
        return None
    from scipy import ndimage
    R = reach_map(region)
    if not hasattr(R, "reached_label"):
        R.reached_label = np.array([c_ in R.reached_comps for c_ in R.comp.tolist()], dtype=bool)
        R.reached_label[0] = False
    z = zone_at(region, x, y)
    if z is None:
        return None
    _, lab, _ = R.zones[z["id"]]
    H, W = lab.shape
    ix, iy = _px(z, x, y)
    for rad in (600, 1500, 3000, max_radius):
        rp = int((rad + min_inside) // SCALE) + 3
        x0, x1 = max(0, ix - rp), min(W, ix + rp + 1)
        y0, y1 = max(0, iy - rp), min(H, iy + rp + 1)
        win = lab[y0:y1, x0:x1]
        dist = ndimage.distance_transform_edt(win > 0) * SCALE
        good = R.reached_label[win] & (dist >= min_inside + SCALE)
        gy, gx = np.nonzero(good)
        if not len(gx):
            continue
        cx = z["ox"] + (gx + x0 + 0.5) * SCALE
        cy = z["oy"] + (gy + y0 + 0.5) * SCALE
        d = np.hypot(cx - x, cy - y)
        order = np.argsort(d)
        for k in order[:200]:
            if d[k] > rad:
                break
            px, py = float(cx[k]), float(cy[k])
            ws = wall_side(region, px, py)
            if (ws is None or ws[0] >= min_inside) and is_reachable(region, px, py):
                return (int(round(px)), int(round(py)), float(d[k]))
    return None


# ---------------------------------------------------------------- ground height

_heights = {}


def terrain_z(region, x, y):
    """Client terrain height (heights_2x.f32, metres -> game units) under (x, y), or None. Terrain only: no buildings,
    bridges or city floors."""
    z = zone_at(region, x, y)
    if z is None:
        return None
    zid = z["id"]
    if zid not in _heights:
        d = os.path.join(CLIENT, "zones", "zone%03d" % zid)
        try:
            info = json.load(open(os.path.join(d, "zone.json"), encoding="utf-8"))
            f = info.get("heights_2x", info["heights"]); n = int(info.get("heights_2x_size", info["heights_size"]))
            h = np.fromfile(os.path.join(d, f), dtype="<f4").reshape(n, n)
            _heights[zid] = (h, float(info["size_m"]) / (n - 1), bool(info.get("city", False)))
        except (OSError, KeyError, ValueError):
            _heights[zid] = None
    if _heights[zid] is None:
        return None
    h, cell, _city = _heights[zid]
    n = h.shape[0]
    gx = min(max((x - z["ox"]) * U / cell, 0.0), n - 1.001)
    gy = min(max((y - z["oy"]) * U / cell, 0.0), n - 1.001)
    ix, iy = int(gx), int(gy)
    fx, fy = gx - ix, gy - iy
    top = h[iy, ix] * (1 - fx) + h[iy, ix + 1] * fx
    bot = h[iy + 1, ix] * (1 - fx) + h[iy + 1, ix + 1] * fx
    return float(top * (1 - fy) + bot * fy) / U


# -- server navmesh (Detour tiled mesh, "TESM" set file written by OpenDAoC-BuildNav; 64-bit poly refs)

_nav = {}


def nav_path(zone_id):
    for d in NAV_DIRS:
        p = os.path.join(d, "zone%03d.nav" % zone_id)
        if os.path.exists(p):
            return p
    return None


def _parse_nav(path):
    """-> dict: tris (T,3,3) float32 in game units (x, y, z), tri_poly (T,) int32, tile_key -> (start, end) ranges,
    orig/tile size, poly component labels (within this zone file), poly count."""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    buf = open(path, "rb").read()
    magic, ver, ntiles = struct.unpack_from("<4sii", buf, 0)
    if magic != b"TESM":
        raise ValueError("not a Detour set: %s" % path)
    ox, oy, oz, tw, th, maxt, maxp = struct.unpack_from("<5fii", buf, 12)
    pos = 40
    tri_list, poly_of_tri, tkeys = [], [], []
    ext = []          # (tx, ty, side, gpoly, a_along, b_along, ymean)
    ia, ib = [], []
    gbase = 0
    poly_dt = np.dtype([("firstLink", "<u4"), ("verts", "<u2", 6), ("neis", "<u2", 6), ("flags", "<u2"),
                        ("vc", "u1"), ("at", "u1")])
    det_dt = np.dtype([("vb", "<u4"), ("tb", "<u4"), ("vc", "u1"), ("tc", "u1"), ("pad", "u1", 2)])
    for _ in range(ntiles):
        ref, size = struct.unpack_from("<Qi", buf, pos)
        pos += 16
        if not ref or not size:
            continue
        h = struct.unpack_from("<4s14i3f7f", buf, pos)
        p = pos + 100
        tx, ty = h[2], h[3]
        npoly, nvert, nlink, ndet, ndv, ndt, nbv, noff = h[6], h[7], h[8], h[9], h[10], h[11], h[12], h[13]
        verts = np.frombuffer(buf, "<f4", nvert * 3, p).reshape(-1, 3); p += nvert * 12
        polys = np.frombuffer(buf, poly_dt, npoly, p); p += npoly * 32
        p += nlink * 16
        dets = np.frombuffer(buf, det_dt, ndet, p); p += ndet * 12
        dverts = np.frombuffer(buf, "<f4", ndv * 3, p).reshape(-1, 3); p += ndv * 12
        dtris = np.frombuffer(buf, "u1", ndt * 4, p).reshape(-1, 4); p += ndt * 4
        pos += size
        ground = (polys["at"] >> 6) == 0
        # detail triangles of the ground polys
        if ndet:
            pidx = np.nonzero(ground)[0]
            pidx = pidx[pidx < ndet]
            tc = dets["tc"][pidx].astype(np.int64)
            if tc.sum():
                rp = np.repeat(pidx, tc)
                tb = np.repeat(dets["tb"][pidx].astype(np.int64), tc)
                within = np.arange(tc.sum()) - np.repeat(np.cumsum(tc) - tc, tc)
                tri = dtris[tb + within, :3].astype(np.int64)        # (T,3) local indices
                vc = polys["vc"][rp].astype(np.int64)[:, None]
                pv = np.take_along_axis(polys["verts"][rp].astype(np.int64), np.minimum(tri, 5), axis=1)
                dv = dets["vb"][rp].astype(np.int64)[:, None] + tri - vc
                usep = tri < vc
                P = np.where(usep[..., None], verts[np.clip(pv, 0, nvert - 1)], dverts[np.clip(dv, 0, max(ndv - 1, 0))] if ndv else 0)
                tri_list.append(P.astype(np.float32))
                poly_of_tri.append((rp + gbase).astype(np.int32))
                tkeys.append(np.full(len(rp), tx * 100000 + ty, np.int64))
        # connectivity: internal neighbours + external portal edges
        for j in range(6):
            nj = polys["neis"][:, j]
            valid = ground & (polys["vc"] > j)
            inn = valid & (nj > 0) & ((nj & 0x8000) == 0)
            if inn.any():
                src = np.nonzero(inn)[0]
                ia.append(src + gbase); ib.append(nj[inn].astype(np.int64) - 1 + gbase)
            ex = valid & ((nj & 0x8000) != 0)
            if ex.any():
                src = np.nonzero(ex)[0]
                va = verts[polys["verts"][src, j]]
                nxt = np.where(polys["vc"][src] > j + 1, j + 1, 0)
                vb = verts[polys["verts"][src, nxt]]
                side = (nj[ex] & 0xff).astype(np.int64)
                for k in range(len(src)):
                    s = side[k]
                    if s in (0, 4):
                        lo, hi = sorted((va[k, 2], vb[k, 2]))
                    elif s in (2, 6):
                        lo, hi = sorted((va[k, 0], vb[k, 0]))
                    else:
                        continue
                    ext.append((tx, ty, s, src[k] + gbase, lo, hi, (va[k, 1] + vb[k, 1]) * 0.5))
        gbase += npoly
    # join external edges between neighbour tiles
    groups = {}
    for e in ext:
        tx, ty, s = e[0], e[1], e[2]
        if s == 0: key = (tx, ty, 0)
        elif s == 4: key = (tx - 1, ty, 0)
        elif s == 2: key = (tx, ty, 2)
        else: key = (tx, ty - 1, 2)
        groups.setdefault(key, ([], []))[0 if s in (0, 2) else 1].append(e)
    for A, B in groups.values():
        for a in A:
            for b in B:
                if min(a[5], b[5]) - max(a[4], b[4]) > 0.01 and abs(a[6] - b[6]) < 2.0:
                    ia.append(np.array([a[3]])); ib.append(np.array([b[3]]))
    ia = np.concatenate(ia) if ia else np.zeros(0, np.int64)
    ib = np.concatenate(ib) if ib else np.zeros(0, np.int64)
    g = coo_matrix((np.ones(len(ia)), (ia, ib)), shape=(gbase, gbase))
    _, comp = connected_components(g, directed=False)
    tris = np.concatenate(tri_list) if tri_list else np.zeros((0, 3, 3), np.float32)
    tp = np.concatenate(poly_of_tri) if poly_of_tri else np.zeros(0, np.int32)
    tk = np.concatenate(tkeys) if tkeys else np.zeros(0, np.int64)
    order = np.argsort(tk, kind="stable")
    tris, tp, tk = tris[order], tp[order], tk[order]
    # recast (x, up, z) / NAV_F -> game (x, y, z)
    tris = tris[:, :, [0, 2, 1]] / NAV_F
    return dict(tris=tris.astype(np.float32), tri_poly=tp, tile_key=tk, orig=np.array([ox, oy, oz]), tw=tw, th=th,
                comp=comp.astype(np.int32), npoly=gbase)


def navmesh(zone_id):
    """Parsed (and cached under out/cache) server navmesh of a zone, or None without a .nav file."""
    if zone_id in _nav:
        return _nav[zone_id]
    p = nav_path(zone_id)
    if p is None:
        _nav[zone_id] = None
        return None
    os.makedirs(CACHE, exist_ok=True)
    cp = os.path.join(CACHE, "nav%03d_%s_%d.npz" % (zone_id, os.path.basename(os.path.dirname(p)), int(os.path.getmtime(p))))
    if os.path.exists(cp):
        d = dict(np.load(cp))
    else:
        d = _parse_nav(p)
        tmp = cp[:-4] + ".%d.tmp.npz" % os.getpid()
        np.savez(tmp, **d)
        try:
            os.replace(tmp, cp)              # atomic: several lanes may parse the same zone at once
        except OSError:
            pass
    keys = d["tile_key"]
    uk, start = np.unique(keys, return_index=True)
    end = np.append(start[1:], len(keys))
    d["index"] = {int(k): (int(s), int(e)) for k, s, e in zip(uk, start, end)}
    _nav[zone_id] = d
    return d


def _floors_in_zone(zid, x, y):
    m = navmesh(zid)
    if m is None:
        return None
    tx = int(math.floor((x * NAV_F - m["orig"][0]) / m["tw"]))
    ty = int(math.floor((y * NAV_F - m["orig"][2]) / m["th"]))
    rng = m["index"].get(tx * 100000 + ty)
    if rng is None:
        return []
    t = m["tris"][rng[0]:rng[1]]
    a, b, c = t[:, 0], t[:, 1], t[:, 2]
    v0x, v0y = c[:, 0] - a[:, 0], c[:, 1] - a[:, 1]
    v1x, v1y = b[:, 0] - a[:, 0], b[:, 1] - a[:, 1]
    v2x, v2y = x - a[:, 0], y - a[:, 1]
    d00 = v0x * v0x + v0y * v0y; d01 = v0x * v1x + v0y * v1y; d11 = v1x * v1x + v1y * v1y
    d20 = v2x * v0x + v2y * v0y; d21 = v2x * v1x + v2y * v1y
    den = d00 * d11 - d01 * d01
    den = np.where(np.abs(den) < 1e-9, 1e-9, den)
    u = (d11 * d20 - d01 * d21) / den
    v = (d00 * d21 - d01 * d20) / den
    ok = (u >= -1e-4) & (v >= -1e-4) & (u + v <= 1 + 1e-4)
    zs = a[ok, 2] + u[ok] * (c[ok, 2] - a[ok, 2]) + v[ok] * (b[ok, 2] - a[ok, 2])
    polys = m["tri_poly"][rng[0]:rng[1]][ok]
    return sorted(((float(zz), int(pp)) for zz, pp in zip(zs, polys)))


def nav_floors(region, x, y):
    """Server navmesh floor heights under (x, y): [(z, poly id), ...] sorted low to high; [] when the mesh has no floor
    there; None when the zone has no navmesh."""
    z = zone_at(region, x, y)
    if z is None:
        return None
    return _floors_in_zone(z["id"], x, y)


def nav_poly(region, x, y, z, h=32, v=64):
    """The navmesh poly the server would snap (x, y, z) to with its default half extents (32, 32, 64 game units,
    LocalPathfindingMgr._defaultHalfExtents): (zone id, poly id, floor z, horizontal offset) or None."""
    zz = zone_at(region, x, y)
    if zz is None or navmesh(zz["id"]) is None:
        return None
    best = None
    for dx in (0, -h, h):
        for dy in (0, -h, h):
            for fz, pid in _floors_in_zone(zz["id"], x + dx, y + dy) or ():
                if abs(fz - z) <= v:
                    k = (math.hypot(dx, dy), abs(fz - z))
                    if best is None or k < best[0]:
                        best = (k, (zz["id"], pid, fz, k[0]))
    return best[1] if best else None


def ground_z(region, x, y, near_z=None):
    """Ground Z (game units) at (x, y): the server navmesh floor nearest near_z (default: the client terrain height),
    else the client terrain height; None outside every zone."""
    t = terrain_z(region, x, y)
    f = nav_floors(region, x, y)
    ref = near_z if near_z is not None else t
    if f:
        if ref is None:
            return f[-1][0]
        return min(f, key=lambda q: abs(q[0] - ref))[0]
    return t


def ground_info(region, x, y):
    """Everything ground_z looks at: {'terrain': z, 'navmesh': [z, ...] or None, 'zone': id}."""
    z = zone_at(region, x, y)
    f = nav_floors(region, x, y)
    return dict(zone=z["id"] if z else None, terrain=terrain_z(region, x, y),
                navmesh=None if f is None else [q[0] for q in f])


# ---------------------------------------------------------------- period sightings

_sight = None


def period_sightings(name):
    """CapnBry radar sightings of every mob with this name (case-insensitive): [{'mob_id', 'name', 'zone', 'lx', 'ly',
    'z', 'level', 'region', 'x', 'y', 'reachable'}] - lx/ly zone-local as recorded, x/y region coordinates through this
    world's Zones table (None when the zone is not in the world)."""
    global _sight
    if _sight is None:
        _sight = {}
        for mid, v in json.load(open(SIGHTINGS, encoding="utf-8")).items():
            _sight.setdefault((v.get("name") or "").strip().lower(), []).append((mid, v))
    out = []
    zs = zones()
    for mid, v in _sight.get(name.strip().lower(), []):
        for zone, lx, ly, zz, lvl in v["seen"]:
            zi = zs.get(zone)
            e = dict(mob_id=mid, name=v["name"], zone=zone, lx=lx, ly=ly, z=zz, level=lvl,
                     region=zi["region"] if zi else None, x=zi["ox"] + lx if zi else None, y=zi["oy"] + ly if zi else None)
            e["reachable"] = is_reachable(e["region"], e["x"], e["y"]) if zi else None
            out.append(e)
    return out


def local(region, x, y):
    """Region -> (zone id, zone-local x, y)."""
    z = zone_at(region, x, y)
    return (z["id"], x - z["ox"], y - z["oy"]) if z else None


def world(zone_id, lx, ly):
    """Zone-local -> (region, x, y)."""
    z = zones()[zone_id]
    return z["region"], z["ox"] + lx, z["oy"] + ly


if __name__ == "__main__":
    print("Spackit:", period_sightings("Spackit"))
