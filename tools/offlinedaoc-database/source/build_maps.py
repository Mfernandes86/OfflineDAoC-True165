"""P-D: zones, map pictures and the coordinate rule for the OFFLINEDAOC Database.

    python -I source/build_maps.py [--db <world>] [--out <data dir>] [--force] [--no-images]

Reads (read only): the world database (Zones, Regions, ServerProperty, Mob, ZonePoint, Teleport, Battleground),
the converted client content (maps/maps.json, maps/x4plus/*.png, zones/zoneNNN/map.png, ui/warmap/warmap.json)
and the server's navigation meshes (runtime/server/navmesh-165, navmesh).
Writes: data/zones.js and the pictures under maps/. Pictures are only re-made when their source is newer
(--force re-makes all), so a second run takes a few seconds.

THE COORDINATE RULE (proved with overlays, see source/contract/zones.md):
    every picture has  rect = [x0, y0, w, h]  in zone-local game units (the /loc numbers) and  px = [W, H]
        pixel_x = (x - x0) * W / w        pixel_y = (y - y0) * H / h
    x grows to the right (east), y grows DOWN (south): no flip, no rotation, no margin.
    Overview pictures use the same rule with REGION coordinates (zone-local + the zone's ox / oy).

Where the facts come from:
  - which regions exist in True 1.65: the server's own switches, ServerProperty disabled_regions and
    disabled_expansions, applied as GameServer/world/Region.cs:234-264 applies them (a region's Expansion + 1 is
    the client expansion number that disabled_expansions lists).
  - zone squares: Zones.OffsetX / OffsetY / Width / Height in 8192-unit tiles (wgcommon.zones).
  - the game's own map of a zone covers the whole zone square, except capitals and dungeons, where the client's
    regions.dat gives the square (maps.json "map_rect"; client pipeline/build_maps.py:94-101, game/ui/map_window.gd:153).
  - a dungeon floor map (maps.json "areas"): its rect is inside that square, z / depth give the floor's height band
    (game/ui/map_window.gd:173-189).
  - the top-down zone picture (zones/zoneNNN/map.png): an orthographic render of the whole zone square, north up
    (client tests/zone_map_render.gd:28-41).
  - navigation meshes: RecastDemo tile sets, vertices = (x, height, y) / 32 in REGION coordinates
    (tools/OpenDAoC-BuildNav/World/NavmeshMgr.cs:9, GameServer/world/Pathfinding/LocalPathfindingMgr.cs:282-285, 509).
  - a spawn outside every zone square of its region is not in the world: GameServer/gameobjects/GameObject.cs:601-608.
  - client zone 245 / 247 are the built copies of the server's 277 / 276 (client pipeline/build_zone.py:716-717).
"""
import argparse, json, os, struct, sys, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wgcommon as wg

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

CONTENT = wg.CLIENT + "/content"
NAV_DIRS = [wg.RUNTIME + "/server/navmesh-165", wg.RUNTIME + "/server/navmesh"]   # LocalPathfindingMgr.cs:282-285
MAPS = os.path.join(wg.ROOT, "maps")

ZONE_COPIES = {277: 245, 276: 247}        # server zone -> the client zone folder / map entry that holds its content
CAPITALS = {10: 1, 101: 2, 201: 3}        # TeleportToExchangeCommand.cs:9-15 CapitalRegion
REALM_WORDS = (("albion", 1), ("midgard", 2), ("hibernia", 3))

AERIAL_PX, AERIAL_Q = 2048, 84            # top-down zone picture: kept at its full 2048 x 2048
GAME_PX, GAME_Q = 1024, 88                # the game's painted map (the client's 256 px art, 4x as the game shows it)
PLAN_PX = 2048                            # floor plan drawn from the navigation mesh (PNG, 64 colours)
THUMB_PX, THUMB_Q = 256, 80
OVERVIEW_ZONE_PX, OVERVIEW_Q = 384, 82    # one zone square on a realm overview (48 px per 8192-unit tile)
BARE_RENDER = 0.65                        # a whole-zone render with this share of one flat colour has no ground: refused
ART_FIT = 0.9                             # the game's map is used only when this share of the zone's spawns is on it
FLOOR_SLACK = 48                          # game units of height a floor's mesh may sit outside the client's band
ART_FLOORS = 0.5                          # ... and, where it has floor maps, this share stands inside a floor's height band

WATER = (70, 104, 146)                    # water on the top-down pictures (see water_blue)
BG = (20, 22, 26)                         # the page background (app.css --bg)


# ---------------------------------------------------------------------------------------------------------------
def parse_args():
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--db", default=wg.DEFAULT_DB)
    p.add_argument("--out", default=os.path.join(wg.ROOT, "data"))
    p.add_argument("--force", action="store_true", help="re-make every picture")
    p.add_argument("--no-images", action="store_true", help="write data/zones.js only (pictures must already exist)")
    return p.parse_args()


def clean(s):
    """Client text is cp1252 read as latin-1: put the quotes back, drop control characters."""
    if not s:
        return ""
    for a, b in (("\x92", "'"), ("\x91", "'"), ("\x93", '"'), ("\x94", '"'), ("\x96", "-"), ("\x97", "-"), ("\x85", "...")):
        s = s.replace(a, b)
    return " ".join("".join(c for c in s if c >= " " and not ("\x7f" <= c <= "\x9f")).split())


def split_list(text):
    """'27;165;233-242;244' -> {27, 165, 233..242, 244} (Util.SplitCSV with ranges)."""
    out = set()
    for part in (text or "").replace(",", ";").split(";"):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            a, b = part.split("-", 1)
            out.update(range(int(a), int(b) + 1))
        else:
            out.add(int(part))
    return out


def stale(dst, *srcs):
    return FORCE or not os.path.exists(dst) or any(os.path.getmtime(s) > os.path.getmtime(dst) for s in srcs)


def rel(path):
    return os.path.relpath(path, wg.ROOT).replace("\\", "/")


def square(x0, y0, x1, y1, margin=0.0, minimum=0):
    """The square around a box, grown by a margin share, whole units."""
    side = max(x1 - x0, y1 - y0, 1)
    side = max(side * (1 + 2 * margin), minimum)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    return [int(round(cx - side / 2)), int(round(cy - side / 2)), int(round(side)), int(round(side))]


def union(a, b):
    x0, y0 = min(a[0], b[0]), min(a[1], b[1])
    x1, y1 = max(a[0] + a[2], b[0] + b[2]), max(a[1] + a[3], b[1] + b[3])
    return [x0, y0, x1 - x0, y1 - y0]


# ---------------------------------------------------------------------------------------------------------------
# navigation mesh -> floor plan
def read_nav(path):
    """A RecastDemo tile-mesh set -> [(verts float32 [n, 3] in recast space, polys record array)] per tile."""
    d = open(path, "rb").read()
    magic, ver, ntiles = struct.unpack_from("<iii", d, 0)
    assert magic == 0x4D534554 and ver == 1, "not a navmesh set: %s" % path           # 'MSET' version 1
    off = 12 + 28                                                                      # + dtNavMeshParams
    poly_t = np.dtype([("first", "<u4"), ("v", "<u2", 6), ("n", "<u2", 6), ("flags", "<u2"), ("vc", "u1"), ("at", "u1")])
    tiles = []
    for _ in range(ntiles):
        ref, size = struct.unpack_from("<Qi", d, off)
        off += 16
        if not ref or not size:
            break
        h = struct.unpack_from("<15i", d, off)
        assert h[0] == 0x444E4156 and h[1] == 7, "navmesh tile version: %s" % path     # 'DNAV' version 7
        polyc, vertc = h[6], h[7]
        v = np.frombuffer(d, "<f4", vertc * 3, off + 100).reshape(-1, 3)
        p = np.frombuffer(d, poly_t, polyc, off + 100 + vertc * 12)
        tiles.append((v, p))
        off += size
    return tiles


def nav_polys(zone):
    """-> (list of (height, [(x, y), ...] zone-local units, [border edge flags]), source path) or (None, None)."""
    for d in NAV_DIRS:
        path = "%s/zone%03d.nav" % (d, zone["id"])
        if os.path.exists(path):
            break
    else:
        return None, None
    out = []
    for verts, polys in read_nav(path):
        if not len(polys):
            continue
        g = verts.astype(np.float64) * 32.0                       # recast (x, height, y) / 32 -> region units
        for p in polys:
            if p["at"] >> 6:                                      # an off-mesh connection (a jump / ladder link)
                continue
            n = int(p["vc"])
            idx = p["v"][:n]
            pts = [(float(g[i, 0]) - zone["ox"], float(g[i, 2]) - zone["oy"]) for i in idx]
            hgt = float(g[idx, 1].mean())
            border = [int(p["n"][k]) == 0 for k in range(n)]      # no neighbour on this edge: a wall or a drop
            out.append((hgt, pts, border))
    return out, path


def draw_plan(polys, rect, size, dst, band=None):
    """Top-down floor plan: lower floors darker and drawn first, walls as dark edges. -> (z low, z high).
    With band = (z0, z1) only the floor in that height band is lit; the rest of the dungeon is drawn dim under it."""
    ss = 2
    S = size * ss
    im = Image.new("RGB", (S, S), BG)
    d = ImageDraw.Draw(im)
    k = S / rect[2]
    polys = sorted(polys, key=lambda p: p[0])
    c0, c1 = np.array((62, 72, 92.0)), np.array((226, 214, 184.0))
    if band:
        def inside(h):
            return band[0] - FLOOR_SLACK < h <= band[1] + FLOOR_SLACK
        for hgt, pts, border in polys:                               # the other floors, dim
            if not inside(hgt):
                d.polygon([((x - rect[0]) * k, (y - rect[1]) * k) for x, y in pts], fill=(38, 42, 50))
        polys = [p for p in polys if inside(p[0])]
        c0 = np.array((120, 124, 132.0))                             # one floor: keep it light, shade only gently
    if not polys:
        return None
    hs = np.array([p[0] for p in polys])
    lo, hi = float(np.percentile(hs, 1)), float(np.percentile(hs, 99))
    span = max(hi - lo, 1.0)
    for hgt, pts, border in polys:
        t = min(1.0, max(0.0, (hgt - lo) / span))
        col = tuple(int(v) for v in (c0 + (c1 - c0) * t))
        q = [((x - rect[0]) * k, (y - rect[1]) * k) for x, y in pts]
        d.polygon(q, fill=col)
        for i, b in enumerate(border):
            if b:
                d.line([q[i], q[(i + 1) % len(q)]], fill=(12, 13, 16), width=ss * 2)
    im = im.resize((size, size), Image.LANCZOS)
    im.quantize(96, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).save(dst, optimize=True)
    return int(round(lo)), int(round(hi))


# ---------------------------------------------------------------------------------------------------------------
def water_blue(im, zone_dir):
    """The client's top-down render shows water as flat pale grey (it mirrors the sky), which reads as snow or sand.
    Paint it water blue. Where: the client's own water map of the zone (water.png: 256 x 256 cells, the water body's
    number, 255 = none), and inside each body only the pixels that have that body's flat water colour, so shores,
    bridges and islands keep their own look. Nothing outside the client's water map is touched. -> (picture, share)"""
    path = zone_dir + "/water.png"
    if not os.path.exists(path):
        return im, 0.0
    im = im.convert("RGB")
    ids = Image.open(path).convert("L").resize(im.size, Image.NEAREST)
    idm = np.asarray(ids)
    a = np.asarray(im).astype(np.int16)
    out = a.copy()
    lum = a.sum(axis=2) / 3.0
    done = np.zeros(idm.shape, bool)
    for wid in np.unique(idm):
        if wid == 255:
            continue
        m = idm == wid
        px = a[m]
        if len(px) < 400:
            continue
        key = (px[:, 0] >> 3) * 1024 + (px[:, 1] >> 3) * 32 + (px[:, 2] >> 3)
        vals, counts = np.unique(key, return_counts=True)
        top = vals[counts.argmax()]
        if counts.max() < 0.08 * len(px):
            continue                                                  # no flat colour here: not open water from above
        c = np.array([(top // 1024) * 8 + 4, ((top // 32) % 32) * 8 + 4, (top % 32) * 8 + 4])
        if c.max() - c.min() > 70 or c.sum() < 240:
            continue                                                  # a strong or dark colour is ground, not mirrored sky
        near = m & (np.abs(a - c).max(axis=2) <= 20)
        done |= near
    if done.any():
        # one cell of the water map is 8 pixels: take in the flat-coloured pixels that touch what was found
        grown = np.asarray(Image.fromarray((done * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))) > 0
        ref = a[done].mean(axis=0)
        done = done | (grown & (np.abs(a - ref).max(axis=2) <= 20))
        shade = np.clip(lum[done] / max(lum[done].mean(), 1.0), 0.85, 1.15)[:, None]
        out[done] = np.clip(np.array(WATER) * shade, 0, 255)
    return Image.fromarray(out.astype(np.uint8)), float(done.mean())


def bare_share(src):
    """Share of a top-down render, outside the zone's water, that is one flat colour. A whole zone of land is never
    flat; a render that came out without its ground is (Abermenai, zone 253: trees and walls on plain sky colour)."""
    im = Image.open(src).convert("RGB").resize((256, 256), Image.BILINEAR)
    a = np.asarray(im).astype(np.int32)
    wpath = os.path.dirname(src) + "/water.png"
    land = np.ones((256, 256), bool)
    if os.path.exists(wpath):
        land = np.asarray(Image.open(wpath).convert("L").resize((256, 256), Image.NEAREST)) == 255
    px = a[land]
    if len(px) < 1000:
        return 0.0
    key = (px[:, 0] >> 3) * 1024 + (px[:, 1] >> 3) * 32 + (px[:, 2] >> 3)
    return float(np.unique(key, return_counts=True)[1].max() / len(px))


def save_jpeg(im, dst, q):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    im.convert("RGB").save(dst, "JPEG", quality=q, optimize=True, progressive=True)


def main():
    global FORCE
    a = parse_args()
    FORCE = a.force
    t0 = time.time()
    con = wg.open_db(a.db)
    allz = wg.zones(con)
    by_region = wg.zones_by_region(allz)
    regions = {r["RegionID"]: dict(r) for r in con.execute("select * from Regions")}
    props = {r["Key"]: r["Value"] for r in con.execute("select Key, Value from ServerProperty where Key in ('disabled_regions', 'disabled_expansions')")}
    assert "disabled_regions" in props and "disabled_expansions" in props, "the world has no region switches"
    off_regions, off_exp = split_list(props["disabled_regions"]), split_list(props["disabled_expansions"])

    maps = json.load(open(CONTENT + "/maps/maps.json", encoding="utf-8"))
    mzones = {int(k): v for k, v in maps["zones"].items()}
    warmap = json.load(open(CONTENT + "/ui/warmap/warmap.json", encoding="utf-8"))
    frontier = {t["zone"] for p in warmap["pages"].values() for t in p["tiles"]}
    battle = {r["RegionID"]: (r["MinLevel"], r["MaxLevel"]) for r in con.execute("select RegionID, MinLevel, MaxLevel from Battleground")}

    # ---- spawns per zone (every Mob row: monsters, merchants, guards) and their box
    spawns, box, no_zone, spots = {}, {}, {}, {}
    for r in con.execute("select Region, X, Y, Z from Mob"):
        z = wg.zone_of(by_region, r[0], r[1], r[2])
        if not z:
            no_zone[r[0]] = no_zone.get(r[0], 0) + 1
            continue
        zid, x, y = z["id"], r[1] - z["ox"], r[2] - z["oy"]
        spawns[zid] = spawns.get(zid, 0) + 1
        spots.setdefault(zid, []).append((x, y, r[3]))
        b = box.get(zid)
        box[zid] = [x, y, x, y] if not b else [min(b[0], x), min(b[1], y), max(b[2], x), max(b[3], y)]
    spots = {k: np.array(v, dtype=np.float64) for k, v in spots.items()}
    region_spawns = {}
    for zid, n in spawns.items():
        region_spawns[allz[zid]["region"]] = region_spawns.get(allz[zid]["region"], 0) + n
    links = [(r[0], r[1]) for r in con.execute("select SourceRegion, TargetRegion from ZonePoint")]
    reach = {t for _, t in links} | {r[0] for r in con.execute("select RegionID from Teleport")}

    # ---- which zones exist in True 1.65
    dropped = {"expansion": [], "disabled": [], "unused": [], "no_region": []}
    keep = []
    for zid in sorted(allz):
        z = allz[zid]
        rg = regions.get(z["region"])
        if not rg:
            dropped["no_region"].append(zid)
        elif (rg["Expansion"] + 1) in off_exp:                       # Region.cs:255-264
            dropped["expansion"].append(zid)
        elif z["region"] in off_regions:                             # Region.cs:234-242
            dropped["disabled"].append(zid)
        elif not region_spawns.get(z["region"]) and z["region"] not in reach:
            dropped["unused"].append(zid)                            # nothing lives there and nothing leads there
        else:
            keep.append(zid)
    kept_regions = sorted({allz[z]["region"] for z in keep})

    # ---- realm of a region: the client names the nine realm regions; the rest take the realm of the regions
    #      whose zone points lead into them (three realms lead in, or none: realm 0)
    realm = {}
    for rid, r in maps["regions"].items():
        for word, n in REALM_WORDS:
            if word in r["name"].lower():
                realm[int(rid)] = n
    assert len(realm) == 9, realm
    for _ in range(6):                                               # one step further from the realm regions each time
        step = {}
        for rid in kept_regions:
            if rid not in realm:
                src = {realm[s] for s, t in links if t == rid and s in realm}
                if src:
                    step[rid] = src.pop() if len(src) == 1 else 0    # entered from one realm's land, or from several
        realm.update(step)
    for rid in kept_regions:
        realm.setdefault(rid, 0)
    for rid, n in CAPITALS.items():
        assert realm.get(rid) == n, "capital realm mismatch %s" % rid

    def kind_of(z):
        rg = regions[z["region"]]
        if rg["HousingEnabled"]:
            return "housing"
        if z["region"] in CAPITALS:
            return "city"
        if z["region"] in battle:
            return "battleground"
        if z["id"] in frontier:
            return "frontier"
        if len(by_region[z["region"]]) == 1:
            return "dungeon"                                         # its own region, entered through a zone point
        return "outdoor"

    os.makedirs(os.path.join(MAPS, "t"), exist_ok=True)
    os.makedirs(os.path.join(MAPS, "overview"), exist_ok=True)
    made = {"game": 0, "aerial": 0, "plan": 0, "floor": 0, "floorplan": 0, "thumb": 0, "overview": 0, "skipped": 0}

    def picture(kind, dst, srcs, make):
        """Run make() when dst is older than its sources; count it either way."""
        if a.no_images:
            return os.path.exists(dst)
        if stale(dst, *srcs):
            make()
            made[kind] += 1
        else:
            made["skipped"] += 1
        return True

    out_zones, no_picture, plan_only, art_rejected, fit = [], [], [], [], {}
    notes_path = os.path.join(MAPS, "build-notes.json")              # the builder's own memory between runs
    notes = json.load(open(notes_path)) if os.path.exists(notes_path) else {}
    notes.setdefault("renders", {})                                  # zone -> the flat-colour share of its top-down render
    render_refused = []
    notes.setdefault("plans", {})                                    # picture -> the height band and square it was drawn with
    thumb_src = notes.setdefault("thumbs", {})                       # zone -> the picture its thumbnail was made from

    def share_inside(zid, rect, bands=None):
        """Share of the zone's spawns inside a picture square (and, with bands, inside one of the floors' height bands)."""
        p = spots.get(zid)
        if p is None or not len(p):
            return None
        ok = (p[:, 0] >= rect[0]) & (p[:, 0] < rect[0] + rect[2]) & (p[:, 1] >= rect[1]) & (p[:, 1] < rect[1] + rect[3])
        if bands:
            inband = np.zeros(len(p), bool)
            for lo, hi in bands:
                inband |= (p[:, 2] > lo) & (p[:, 2] <= hi)
            ok &= inband
        return float(ok.mean())
    for zid in keep:
        z = allz[zid]
        cid = ZONE_COPIES.get(zid, zid)                              # where the client keeps this zone
        m = mzones.get(cid, {})
        kind = kind_of(z)
        full = [0, 0, z["w"], z["h"]]
        layers, floors = [], []

        # -- the game's own painted map
        art = CONTENT + "/maps/x4plus/" + m["map"] if m.get("map") else ""
        if art and not os.path.exists(art):
            art = CONTENT + "/maps/original/" + m["map"]
        game_rect = [int(v) for v in m["map_rect"]] if m.get("map_rect") else full
        if art and os.path.exists(art):
            # is the client's picture really this zone? (client zone 245's map is the later Labyrinth, not the 1.65
            # Hall of the Corrupt: its square and floor heights hold none of the zone's spawns)
            bands = [(ar["z"], ar["z"] + ar["depth"]) for ar in m.get("areas", []) if ar.get("z", -1) != -1 and ar.get("depth", -1) != -1]
            s1 = share_inside(zid, game_rect)
            s2 = share_inside(zid, game_rect, bands) if bands else None
            if (s1 is not None and s1 < ART_FIT) or (s2 is not None and s2 < ART_FLOORS):
                art_rejected.append((zid, m["map"], round(s1, 3), None if s2 is None else round(s2, 3)))
                art = ""
            else:
                fit[zid] = (s1, s2)
        if art and os.path.exists(art):
            dst = os.path.join(MAPS, "z%03d.jpg" % zid)
            if picture("game", dst, [art], lambda: save_jpeg(Image.open(art).convert("RGB").resize((GAME_PX, GAME_PX), Image.LANCZOS), dst, GAME_Q)):
                layers.append({"id": "game", "label": "Game map", "src": rel(dst), "px": [GAME_PX, GAME_PX], "rect": game_rect})
            for i, ar in enumerate(m.get("areas", [])):
                fsrc = CONTENT + "/maps/x4plus/" + ar["map"]
                if not os.path.exists(fsrc):
                    continue
                fdst = os.path.join(MAPS, "z%03d_%02d.jpg" % (zid, i))
                if picture("floor", fdst, [fsrc], lambda: save_jpeg(Image.open(fsrc).convert("RGB").resize((GAME_PX, GAME_PX), Image.LANCZOS), fdst, GAME_Q)):
                    r = ar["rect"]
                    f = {"name": clean(ar.get("name")) or "Floor %d" % (i + 1), "src": rel(fdst), "px": [GAME_PX, GAME_PX],
                         "rect": [game_rect[0] + r[0], game_rect[1] + r[1], r[2], r[3]]}
                    f["layers"] = [{"id": "game", "label": "Game map", "src": f["src"], "px": f["px"], "rect": f["rect"]}]
                    if ar.get("z", -1) != -1 and ar.get("depth", -1) != -1:
                        f["z"] = [ar["z"], ar["z"] + ar["depth"]]     # a spot is on this floor when z0 < Z <= z1
                    floors.append(f)

        # -- where things are in this zone: the picture square and every spawn
        view = list(game_rect) if layers else None
        if zid in box:
            b = box[zid]
            sb = [b[0], b[1], max(b[2] - b[0], 1), max(b[3] - b[1], 1)]
            view = union(view, sb) if view else sb
        polys, nav_path = (None, None)
        if kind in ("dungeon", "city"):
            polys, nav_path = nav_polys(z)
            if polys:
                xs = [x for _, pts, _ in polys for x, _ in pts]
                ys = [y for _, pts, _ in polys for _, y in pts]
                nb = [int(min(xs)), int(min(ys)), int(max(xs) - min(xs)) + 1, int(max(ys) - min(ys)) + 1]
                view = union(view, nb) if view else nb
        if kind in ("outdoor", "frontier", "housing", "battleground") or not view:
            view = full
        else:
            view = square(view[0], view[1], view[0] + view[2], view[1] + view[3], margin=0.03, minimum=4096)

        # -- the zone seen from above (the client's own top-down render of the real terrain)
        aerial = "%s/zones/zone%03d/map.png" % (CONTENT, cid)
        if os.path.exists(aerial) and view == full:
            rec = notes["renders"].get(str(zid))
            if not rec or rec["mtime"] != os.path.getmtime(aerial):
                rec = notes["renders"][str(zid)] = {"mtime": os.path.getmtime(aerial), "bare": round(bare_share(aerial), 3)}
            if rec["bare"] > BARE_RENDER:
                render_refused.append((zid, rec["bare"]))
                aerial = ""
        if aerial and os.path.exists(aerial):
            dst = os.path.join(MAPS, "a%03d.jpg" % zid)
            if view == full:
                if picture("aerial", dst, [aerial], lambda: save_jpeg(water_blue(Image.open(aerial), os.path.dirname(aerial))[0], dst, AERIAL_Q)):
                    layers.append({"id": "aerial", "label": "From above", "src": rel(dst), "px": [AERIAL_PX, AERIAL_PX], "rect": full})
            else:
                k = AERIAL_PX / z["w"]
                crop = [max(0, view[0]), max(0, view[1]), min(z["w"], view[0] + view[2]), min(z["h"], view[1] + view[3])]
                cpx = [int(round(v * k)) for v in crop]
                side = max(cpx[2] - cpx[0], cpx[3] - cpx[1])          # the same 32 units a pixel as every zone: just cut out
                cpx = [cpx[0], cpx[1], cpx[0] + side, cpx[1] + side]
                assert cpx[2] <= AERIAL_PX and cpx[3] <= AERIAL_PX, "top-down crop of zone %d leaves the picture" % zid

                def mk():
                    save_jpeg(water_blue(Image.open(aerial), os.path.dirname(aerial))[0].crop(cpx), dst, AERIAL_Q)
                if picture("aerial", dst, [aerial], mk):
                    layers.append({"id": "aerial", "label": "From above", "src": rel(dst), "px": [side, side],
                                   "rect": [int(cpx[0] / k), int(cpx[1] / k), int(side / k), int(side / k)]})

        # -- a floor plan from the server's navigation mesh (dungeons and capitals)
        if polys:
            dst = os.path.join(MAPS, "p%03d.png" % zid)
            prect = square(nb[0], nb[1], nb[0] + nb[2], nb[1] + nb[3], margin=0.03)
            name = os.path.basename(dst)

            def mk():
                notes["plans"][name] = {"z": draw_plan(polys, prect, PLAN_PX, dst), "rect": prect}
            if name not in notes["plans"] and os.path.exists(dst) and not a.no_images:
                os.remove(dst)                                       # its height band is not on record: draw it again
            if picture("plan", dst, [nav_path], mk):
                info = notes["plans"][name]
                layers.append({"id": "plan", "label": "Floor plan", "src": rel(dst), "px": [PLAN_PX, PLAN_PX], "rect": info["rect"], "z": info["z"]})
                if len(layers) == 1:
                    plan_only.append(zid)
            # the same plan for each floor the client names: that floor lit, the rest of the dungeon dim
            for i, f in enumerate(floors):
                if "z" not in f:
                    continue
                fdst = os.path.join(MAPS, "p%03d_%02d.png" % (zid, i))
                fname = os.path.basename(fdst)

                def mkf():
                    notes["plans"][fname] = {"z": draw_plan(polys, prect, PLAN_PX, fdst, band=f["z"]), "rect": prect}
                if fname not in notes["plans"] and os.path.exists(fdst) and not a.no_images:
                    os.remove(fdst)
                if picture("floorplan", fdst, [nav_path], mkf) and notes["plans"].get(fname, {}).get("z"):
                    f["layers"].insert(0, {"id": "plan", "label": "Floor plan", "src": rel(fdst), "px": [PLAN_PX, PLAN_PX],
                                           "rect": notes["plans"][fname]["rect"], "z": notes["plans"][fname]["z"]})

        # the order is the recommended one, sharpest truthful picture first: outdoors the top-down picture, in a
        # dungeon the floor plan; a capital keeps the game's labelled map first (it names the buildings)
        order = {"city": ("game", "plan", "aerial"), "dungeon": ("plan", "game", "aerial"), "housing": ("game", "aerial", "plan")}
        order = order.get(kind, ("aerial", "game", "plan"))
        layers.sort(key=lambda l: order.index(l["id"]))
        if not layers:
            no_picture.append(zid)

        thumb = None
        if layers:
            tsrc = os.path.join(wg.ROOT, layers[0]["src"])
            tdst = os.path.join(MAPS, "t", "z%03d.jpg" % zid)

            def mk():
                im = Image.open(tsrc).convert("RGB")
                im.thumbnail((THUMB_PX, THUMB_PX), Image.LANCZOS)
                save_jpeg(im, tdst, THUMB_Q)
            if thumb_src.get(str(zid)) != layers[0]["src"] and os.path.exists(tdst) and not a.no_images:
                os.remove(tdst)                                      # the leading picture changed: make it again
            if picture("thumb", tdst, [tsrc], mk):
                thumb = rel(tdst)
                thumb_src[str(zid)] = layers[0]["src"]

        rg = regions[z["region"]]
        e = {"id": zid, "region": z["region"], "name": clean(z["name"]), "realm": realm[z["region"]], "kind": kind,
             "expansion": "si" if rg["Expansion"] == 1 else "classic",
             "w": z["w"], "h": z["h"], "ox": z["ox"], "oy": z["oy"],
             "map": layers[0]["src"] if layers else None, "px": layers[0]["px"] if layers else None,
             "rect": layers[0]["rect"] if layers else None, "thumb": thumb, "layers": layers, "view": view,
             "spawns": spawns.get(zid, 0), "desc": clean(m.get("desc"))}
        if floors:
            e["floors"] = floors
        if z["region"] in battle:
            e["bg"] = list(battle[z["region"]])
        out_zones.append(e)

    if not a.no_images:
        json.dump(notes, open(notes_path, "w"), indent=0, sort_keys=True)

    # ---- realm overviews: every region with more than one zone, its zones tiled by their real offsets
    zmap = {e["id"]: e for e in out_zones}
    overviews = []
    for rid in kept_regions:
        zs = [zmap[z["id"]] for z in by_region[rid] if z["id"] in zmap]
        if len(zs) < 2:
            continue
        x0, y0 = min(z["ox"] for z in zs), min(z["oy"] for z in zs)
        x1, y1 = max(z["ox"] + z["w"] for z in zs), max(z["oy"] + z["h"] for z in zs)
        k = OVERVIEW_ZONE_PX / 65536.0
        W, H = int(round((x1 - x0) * k)), int(round((y1 - y0) * k))
        dst = os.path.join(MAPS, "overview", "r%03d.jpg" % rid)
        placed, srcs = [], []
        for z in zs:
            whole = [l for l in z["layers"] if l["rect"] == [0, 0, z["w"], z["h"]]]
            whole.sort(key=lambda l: {"aerial": 0, "game": 1}.get(l["id"], 2))
            src = os.path.join(wg.ROOT, whole[0]["src"]) if whole else None
            if src:
                srcs.append(src)
            placed.append((z, src, [int(round((z["ox"] - x0) * k)), int(round((z["oy"] - y0) * k)),
                                    int(round(z["w"] * k)), int(round(z["h"] * k))]))

        def mk():
            im = Image.new("RGB", (W, H), (16, 24, 34))
            d = ImageDraw.Draw(im)
            for z, src, r in placed:
                if src:
                    im.paste(Image.open(src).convert("RGB").resize((r[2], r[3]), Image.LANCZOS), (r[0], r[1]))
                else:
                    d.rectangle([r[0], r[1], r[0] + r[2] - 1, r[1] + r[3] - 1], fill=(30, 34, 42), outline=(52, 58, 70))
            save_jpeg(im, dst, OVERVIEW_Q)
        if picture("overview", dst, srcs, mk):
            overviews.append({"id": rid, "name": clean(regions[rid]["Description"]), "realm": realm[rid],
                              "expansion": "si" if regions[rid]["Expansion"] == 1 else "classic",
                              "src": rel(dst), "px": [W, H], "rect": [x0, y0, x1 - x0, y1 - y0],
                              "zones": [{"id": z["id"], "px": r, "pictured": bool(src)} for z, src, r in placed]})

    out_regions = [{"id": rid, "name": clean(regions[rid]["Description"]), "realm": realm[rid],
                    "expansion": "si" if regions[rid]["Expansion"] == 1 else "classic",
                    "zones": [z["id"] for z in sorted(by_region[rid], key=lambda z: z["id"]) if z["id"] in zmap]}
                   for rid in kept_regions]

    # ---- checks
    assert len(out_zones) == len(keep) and len({e["id"] for e in out_zones}) == len(keep)
    for e in out_zones:
        assert e["w"] == 65536 and e["h"] == 65536, "zone %s is not 8 x 8 tiles" % e["id"]
        for l in e["layers"] + e.get("floors", []) + [l for f in e.get("floors", []) for l in f["layers"]]:
            assert l["rect"][2] > 0 and l["rect"][3] > 0 and l["px"][0] > 0
            assert abs(l["rect"][2] / l["px"][0] - l["rect"][3] / l["px"][1]) < 0.02 * l["rect"][2] / l["px"][0], "picture of zone %s is not square-scaled" % e["id"]
            assert a.no_images or os.path.exists(os.path.join(wg.ROOT, l["src"])), l["src"]
    kinds, with_pic = {}, 0
    for e in out_zones:
        kinds[e["kind"]] = kinds.get(e["kind"], 0) + 1
        with_pic += bool(e["layers"])

    value = {
        "built": wg.stamp(),
        "rule": "pixel = ((x - rect[0]) * px[0] / rect[2], (y - rect[1]) * px[1] / rect[3]); x east, y south, zone-local "
                "game units (the /loc numbers). Overviews: the same with region coordinates (zone ox + x, oy + y).",
        "zones": out_zones, "regions": out_regions, "overviews": overviews,
        "dropped": {"total": len(allz) - len(keep), "later_expansion": len(dropped["expansion"]),
                    "region_switched_off": len(dropped["disabled"]), "unused_region": len(dropped["unused"]),
                    "no_region_row": len(dropped["no_region"]),
                    "switches": {"disabled_regions": props["disabled_regions"], "disabled_expansions": props["disabled_expansions"]},
                    "spawns_outside_every_zone": sum(no_zone.values())},
    }
    wg.write_data("zones", value, a.out, "%d zones, %d regions, %d overviews" % (len(out_zones), len(out_regions), len(overviews)))

    # ---- pictures no zone uses any more (a refused render, a renamed file) are removed: maps/ holds only what is shown
    used = {o["src"] for o in overviews}
    for e in out_zones:
        used.update(l["src"] for l in e["layers"])
        used.update(l["src"] for f in e.get("floors", []) for l in f["layers"])
        if e["thumb"]:
            used.add(e["thumb"])
    orphans = []
    if not a.no_images:
        for dp, _, fs in os.walk(MAPS):
            for f in fs:
                if f.lower().endswith((".jpg", ".png")) and rel(os.path.join(dp, f)) not in used:
                    os.remove(os.path.join(dp, f))
                    orphans.append(f)
    print("unused pictures removed from maps/:", orphans)

    # ---- report
    size = sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(MAPS) for f in fs)
    print("zones in the world table: %d   kept: %d   left out: %d (later expansion %d, region switched off %d, unused region %d, no region row %d)"
          % (len(allz), len(keep), len(allz) - len(keep), len(dropped["expansion"]), len(dropped["disabled"]), len(dropped["unused"]), len(dropped["no_region"])))
    print("unused regions left out:", sorted({allz[z]["region"] for z in dropped["unused"]}))
    print("kinds:", dict(sorted(kinds.items())))
    print("with a picture: %d   without: %d %s" % (with_pic, len(no_picture), no_picture))
    print("floor plan is the only picture:", plan_only)
    print("client map pictures refused (zone, file, share of spawns in its square, share on its floors):", art_rejected)
    low = sorted((round(min(v for v in s if v is not None), 3), z) for z, s in fit.items() if s[0] is not None)[:8]
    print("top-down renders refused, no ground in them (zone, share of one flat colour):", render_refused)
    print("top-down renders kept, the 3 flattest (share, zone):", sorted(((r["bare"], int(z)) for z, r in notes["renders"].items() if r["bare"] <= BARE_RENDER), reverse=True)[:3])
    print("game map fit, the 8 lowest shares of spawns inside the picture (share, zone):", low)
    # how many spawns stand on the drawn floor of each floor plan (the plan is the navigation mesh, so nearly all must)
    on_floor = []
    for e in out_zones:
        for l in e["layers"]:
            if l["id"] == "plan" and e["id"] in spots and not a.no_images:
                im = np.asarray(Image.open(os.path.join(wg.ROOT, l["src"])).convert("RGB")).astype(int)
                p = spots[e["id"]]
                px = ((p[:, 0] - l["rect"][0]) * l["px"][0] / l["rect"][2]).astype(int)
                py = ((p[:, 1] - l["rect"][1]) * l["px"][1] / l["rect"][3]).astype(int)
                ok = (px >= 0) & (px < l["px"][0]) & (py >= 0) & (py < l["px"][1])
                hit = np.zeros(len(p), bool)
                hit[ok] = np.abs(im[py[ok], px[ok]] - np.array(BG)).sum(axis=1) > 24
                on_floor.append((round(float(hit.mean()), 3), e["id"], len(p)))
    print("floor plans, share of spawns standing on drawn floor (share, zone, spawns):", sorted(on_floor))
    assert not on_floor or min(s for s, _, _ in on_floor) > 0.80, "a floor plan does not hold its zone's spawns"
    print("spawn rows outside every zone square (the server cannot place them):", sum(no_zone.values()), dict(sorted(no_zone.items())))
    print("pictures made this run:", made)
    print("maps/ folder: %.1f MB   time: %.1f s" % (size / 1e6, time.time() - t0))
    assert size < 150e6, "maps/ is over 150 MB"


if __name__ == "__main__":
    main()
