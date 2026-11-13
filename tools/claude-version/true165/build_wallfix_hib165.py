"""Wall audit lane HIB (True 1.65 0.8, 2026-11-12): Hibernia (regions 200, 181, 202) spawns that stood behind the
client's invisible zone walls, where a player can see them but never walk to them (owner: "the quest npc spackit spawns
behind an invisible wall where the player can't walk ... move the npcs and the quest markers accordingly"). Brief:
<lead workspace>/wallaudit/WALLAUDIT-BRIEF.md; report <lead workspace>/wallaudit/reports/HIB.md.

The walls: the client's content/zones/zoneNNN/bound.json (the real 1.64 bound.csv; one-sided, the playable side is each
line's right, x east, y south; game/world/zone_bounds.gd). For every row: the nearest wall segment and its side
(zone_bounds.gd side()), a straight walk from the new spot to a camp-mate well inside crosses no wall at all, the new
spot is at least MIN_INSIDE from every wall, and the new Z is the server navmesh floor there (runtime/server/navmesh,
none of these zones has a navmesh-165 override) within Z_TOL of the client ground (heights_2x.f32), or the CapnBry radar
Z when the radar spot is used. Period position first: CapnBry radar
(development-source/research/period-data/archive/capnbry/sightings_index.json, zone-local units).

Moved (base rows: rules UPDATE "... WHERE Mob_ID = ... AND Region = 200 AND X = <old> AND Y = <old>", so a re-run or a
row moved again by someone else is a no-op):
  Lough Derg (zone 200), Spackit's spot north of the north wall (the wall runs at y ~2,100 there):
    Spackit 3f679c2d (quest kill target of "Spicket, Spackit and Spykit", drops Spraggon's Head; his bracer is the
      quest reward)  38203, 1123, Z 6865 (971 outside)  -> CapnBry radar 38341, 2574, Z 5948 (481 inside; his only
      sighting; the walkthrough: "the east-west valley to the northeast of Mag Mell")
    spraggon e8111270 and orchard nipper f9b91fe6, who stood with him (553 / 843 outside; no radar sighting of either
      kind behind the wall here; their kin are seen just south of it) -> moved with him by his offset (+138, +1451).
  Sheeroe Hills (zone 216), the slope north-east of the glimmer camp:
    glimmer ward 8ac4d300 22707, 15431, Z 4490 (336 outside), glimmer ward c33baff2 24378, 17523, Z 5399 (675 outside)
      -> the nearest spots 250 inside on the camp's side (the Prima Atlas puts glimmer wards NW, NC, WC, C; the 47
      other wards of the zone stand inside; no radar coverage of Sheeroe Hills).
    glimmer ardent 5d44668e 24117, 32600, Z 4228 (28 outside the east wall, missed by the first pass) -> 23858,
      32725, 251 inside.
  Lough Gur (zone 204), the melancholic fairies' hold (walkthrough: "/loc 23k,54k"):
    melancholic fairy 4deeb5fc 22326, 64226 (903 south of the south wall, the hold's southern tail; the fairies are
      the Melancholic Fairy Wings kill task's monster) -> the nearest spot 250 inside, 22166, 63053. The fairies fly
      (Flags 32: every one of the 56 hovers 120-1,470 above the ground); this one keeps its own height over the ground
      (521), so Z = ground 7052 + 521.
  Hy Brasil (region 181; lane DETECT's corrected reach map; no radar coverage; Illia's period bestiary lists each
  without a location) -> each to the nearest spot ~290 inside, Z the navmesh floor:
    Priestess of the Watch 100028768 (Domnann 20667, 1126; 2229 outside the north wall, alone; the Star Lake
      procession spawns its own priestesses) -> 21784, 3384.
    glutton 100025865 (Cothrom Gorge 6737, 13338; 320 outside) -> 7352, 13336.
    young botonid disperser 100027605 (Cothrom Gorge 56429, 8720; 445 outside) -> 56552, 9448.
    Tudeg 0e86fa24 (named, Humanoid 59 in Illia; 9520, 7834, 769 outside) and the writhing mass 100028345 beside him
      (9281, 8477, 385 outside) -> together by (+600, +878).
    volant manta 100024046 (Allta Fearann 25788, 23584; 22 outside, on the water) -> 25816, 23896, Z the swim layer
      2934 its 7 kin use.
Moved (row added by add-primaspawns165.json, true165-prima-new: changed in that add-file, as build_gamefix_spawns165.py
does, byte-for-byte round trip, both copies; the launcher updates the row in worlds that already have it):
    glimmer prophet 1a6ee36f 19280, 44774, Z 4053 (71 outside, on the slope; placed from a Prima Atlas map label
      estimate, whose note already says the floor there is a slope above the camp) -> 19609, 44899, 256 inside, Z the
      navmesh floor 3744 (the camp around sits at ~3,650-3,700).
Left as they are:
    grogan 1eb29590 (17512, 34843) and 7e66a15b (17814, 35350), Emain Macha (zone 214), 202 / 33 behind the wall of
      the hill west of the grogan camp: CapnBry saw grogans on that hilltop seven times (17512, 34843, Z 6661 is one of
      them, and 17857, 35393, Z 6863) - their rows are the radar spots (BestiaryG9); generic monsters with a 9-row camp
      at the hill's foot, no quest, no unique loot, and never aggressive (AggroLevel 0). The brief's rule 1 exception
      (period decoration a player was never meant to reach); the owner may still want them moved.

    python -I build_wallfix_hib165.py [--write]     checks against the clean v0.7 world; --write updates
                                                    true165-rules.json (through <lead workspace>/gamefix/locks.py) and the
                                                    prophet row of add-primaspawns165.json (both copies)
Rerun after anything rebuilds add-primaspawns165.json (build_prima165.py). Every reason starts
"True 1.65 0.8 wallfix HIB: "; a re-run replaces them. Levels, templates, loot, factions, respawns, paths untouched.
"""
import json, math, os, sqlite3, struct, sys
import numpy as np

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
FOLDERS = (os.path.join(ROOT, 'runtime', 'true165'), HERE)
NAV_DIRS = (os.path.join(ROOT, 'runtime', 'server', 'navmesh-165'), os.path.join(ROOT, 'runtime', 'server', 'navmesh'))
CLIENT = os.environ.get("OFFLINE_DAOC_GODOT_ZONES") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "source", "new-client", "godot", "content", "zones")
CLEAN = os.environ.get("OFFLINE_DAOC_CLEAN_DB") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "runtime", "data", "opendaoc-165.sqlite3.db")   # the clean world (database/unpack_db.py)
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks   # noqa: E402

LANE = 'HIB'
TAG = 'True 1.65 0.8 wallfix %s: ' % LANE
OFF = {200: (39 * 8192, 59 * 8192), 204: (35 * 8192, 83 * 8192), 214: (51 * 8192, 35 * 8192), 216: (43 * 8192, 79 * 8192),
       181: (48 * 8192, 48 * 8192), 184: (34 * 8192, 32 * 8192), 187: (42 * 8192, 20 * 8192)}
ZNAME = {200: 'Lough Derg', 204: 'Lough Gur', 214: 'Emain Macha', 216: 'Sheeroe Hills', 181: 'Domnann',
         184: 'Cothrom Gorge', 187: 'Allta Fearann'}
REGION_OF = {200: 200, 204: 200, 214: 200, 216: 200, 181: 181, 184: 181, 187: 181}
MIN_INSIDE = 150
Z_TOL = 64

# (Mob_ID, name, zone, old lx, ly, Z, new lx, ly, z-rule, camp-mate (lx, ly) well inside, why)
#   z-rule: ('radar', z) the radar Z; ('z', z) the server navmesh floor there as <lead workspace>/wallaudit/tools.py
#   ground_z reads it (Detour detail mesh); ('hover', h) client ground + h (a flying monster keeps its height over the
#   ground). Each is checked here against the client ground and this file's own coarse navmesh read (Z_TOL).
SPACKIT_DX, SPACKIT_DY = 38341 - 38203, 2574 - 1123
BASE_MOVES = [
    ('3f679c2d-d3e4-4886-8b5a-be271dc2d3a7', 'Spackit', 200, 38203, 1123, 6865, 38341, 2574, ('radar', 5948), (38233, 2666),
     "quest kill target of 'Spicket, Spackit and Spykit' (drops Spraggon's Head; Spackit's Bracer is the quest's "
     "reward); CapnBry's radar saw him at 38341, 2574, Z 5948 (his only sighting), on the playable side; the "
     "walkthrough puts him in the east-west valley to the north-east of Mag Mell"),
    ('e8111270-c4f9-4ac9-99aa-ee7457f28add', 'spraggon', 200, 37520, 1546, 6556,
     37520 + SPACKIT_DX, 1546 + SPACKIT_DY, ('z', 5888), (38233, 2666),
     "stood with Spackit behind the north wall (no radar sighting of a spraggon there; CapnBry saw them just south of "
     "it); moved with him by his offset (+%d, +%d)" % (SPACKIT_DX, SPACKIT_DY)),
    ('f9b91fe6-b484-4464-848a-7b208a841a27', 'orchard nipper', 200, 36329, 1264, 6721,
     36329 + SPACKIT_DX, 1264 + SPACKIT_DY, ('z', 5836), (38233, 2666),
     "stood with Spackit behind the north wall (no radar sighting of an orchard nipper there; CapnBry saw them just "
     "south of it); moved with him by his offset (+%d, +%d)" % (SPACKIT_DX, SPACKIT_DY)),
    ('8ac4d300-ba69-4e1e-8f5f-16a4ec092df7', 'glimmer ward', 216, 22707, 15431, 4490, 22480, 15995, ('z', 3906), (20594, 16388),
     "stood on the slope north-east of the glimmer camp; the Prima Atlas puts glimmer wards in the NW, NC, WC and C of "
     "the zone and the zone's 47 other wards stand inside (no radar coverage of Sheeroe Hills); moved to the nearest "
     "spot 250 inside on the camp's side"),
    ('c33baff2-8d7e-4f66-83d6-470f1c1cc531', 'glimmer ward', 216, 24378, 17523, 5399, 23477, 17745, ('z', 4114), (20594, 16388),
     "stood on the slope north-east of the glimmer camp; the Prima Atlas puts glimmer wards in the NW, NC, WC and C of "
     "the zone and the zone's 47 other wards stand inside (no radar coverage of Sheeroe Hills); moved to the nearest "
     "spot 250 inside on the camp's side"),
    ('5d44668e-c474-4dd0-91df-088b980c633d', 'glimmer ardent', 216, 24117, 32600, 4228, 23858, 32725, ('z', 3956),
     (22169, 33943),
     "stood 28 outside the east wall on the slope above the glimmer camp (the first-pass detector's thick walls hid "
     "it; found by this lane's nearest-segment scan, and lane DETECT's reach map puts it in the unreached outside); "
     "the Prima Atlas puts glimmer ardents in the NW, NC, WC and C of the zone; moved to the nearest spot 250 inside"),
    ('4deeb5fc-1a3b-4607-888d-7029212e7038', 'melancholic fairy', 204, 22326, 64226, 7610, 22166, 63053, ('hover', 521),
     (22389, 52923),
     "the southern tail of the melancholic fairies' hold (the Melancholic Fairy Wings kill task's monster; the "
     "walkthrough: Lough Gur /loc 23k,54k; no radar coverage of Lough Gur), south of the south wall; moved to the "
     "nearest spot 250 inside; a flying monster (Flags 32, its 55 kin hover 120-1470 over the ground), it keeps its "
     "own 521 over the ground"),
    # Hy Brasil (region 181; walls from the later client's bound.csv, checked by lane INTEG). No CapnBry radar coverage of
    # these zones; Illia's period bestiary lists every one of these monsters without a location.
    ('100028768', 'Priestess of the Watch', 181, 20667, 1126, 6968, 21784, 3384, ('z', 5443), (20806, 6006),
     "stood alone 2229 north of Domnann's north wall (Illia's period bestiary: an NPC of level 19-20; the Star Lake "
     "procession, PrimaEncounters165b.cs, spawns its own priestesses and does not use this row); moved to the nearest "
     "spot 290 inside"),
    ('100025865', 'glutton', 184, 6737, 13338, 3137, 7352, 13336, ('z', 2474), (9496, 13471),
     "stood 320 outside the west wall on the slope (8 other gluttons in Hy Brasil); moved to the nearest spot 295 "
     "inside"),
    ('100027605', 'young botonid disperser', 184, 56429, 8720, 3028, 56552, 9448, ('z', 2461), (56393, 11244),
     "stood 445 outside the north wall (46 other young botonid dispersers, the nearest camp just south); moved to the "
     "nearest spot 291 inside"),
    ('0e86fa24-9197-44c0-a0ef-dd8e1642d1fb', 'Tudeg', 184, 9520, 7834, 3129, 10120, 8712, ('z', 2417), (11017, 12528),
     "a named monster (Illia's period bestiary: Tudeg, Humanoid 59; his own template) 769 outside the north-west wall "
     "with the writhing mass beside him; the pair moved together by (+600, +878) to 294 inside"),
    ('100028345', 'writhing mass', 184, 9281, 8477, 2709, 9881, 9355, ('z', 2371), (11017, 12528),
     "the zone's only writhing mass (Illia: Plant 60-64), 385 outside the north-west wall beside Tudeg; moved with "
     "him by (+600, +878) to 678 inside"),
    ('100024046', 'volant manta', 187, 25788, 23584, 2937, 25816, 23896, ('swim', 2934), (27375, 23529),
     "stood 22 outside the wall of the manta line on the water (its 7 kin hold the same line 29-226 inside); moved "
     "to the nearest spot 291 inside"),
]
ADDFILE = 'add-primaspawns165.json'
ADDFILE_MOVES = [
    ('1a6ee36f-4989-5f91-ac9e-0514ecfad9ae', 'glimmer prophet', 216, 19280, 44774, 4053, 19609, 44899, ('z', 3744), (19385, 46046)),
]


# ---------------------------------------------------------------- walls, ground, navmesh

_segs, _hgt, _nav = {}, {}, {}


def segs(zone):
    if zone not in _segs:
        out = []
        for ln in json.load(open(os.path.join(CLIENT, 'zone%03d' % zone, 'bound.json'), encoding='utf-8'))['lines']:
            p = list(zip(ln[::2], ln[1::2]))
            out += [(a, b) for a, b in zip(p, p[1:]) if a != b]
        _segs[zone] = out
    return _segs[zone]


def wall_side(zone, lx, ly):
    """(distance to the nearest wall segment, True on its playable (right) side) - zone_bounds.gd side()."""
    best = (float('inf'), True)
    for a, b in segs(zone):
        dx, dy = b[0] - a[0], b[1] - a[1]
        L = dx * dx + dy * dy
        t = max(0.0, min(1.0, ((lx - a[0]) * dx + (ly - a[1]) * dy) / L))
        d = math.hypot(lx - a[0] - t * dx, ly - a[1] - t * dy)
        if d < best[0] - 1e-6:
            best = (d, dx * (ly - a[1]) - dy * (lx - a[0]) > 0)
    return int(round(best[0])), best[1]


def walls_crossed(zone, p, q):
    ccw = lambda A, B, C: (C[1] - A[1]) * (B[0] - A[0]) - (B[1] - A[1]) * (C[0] - A[0])
    return sum(1 for a, b in segs(zone)
               if (ccw(p, a, b) > 0) != (ccw(q, a, b) > 0) and (ccw(p, q, a) > 0) != (ccw(p, q, b) > 0))


def ground(zone, lx, ly):
    """Client terrain height (heights_2x.f32, 513 x 513 metres, 128 units a cell), bilinear, in game units."""
    if zone not in _hgt:
        _hgt[zone] = np.fromfile(os.path.join(CLIENT, 'zone%03d' % zone, 'heights_2x.f32'), '<f4').reshape(513, 513)
    h = _hgt[zone]
    fx, fy = lx / 128.0, ly / 128.0
    i, j = min(int(fx), 511), min(int(fy), 511)
    tx, ty = fx - i, fy - j
    v = h[j, i] * (1 - tx) * (1 - ty) + h[j, i + 1] * tx * (1 - ty) + h[j + 1, i] * (1 - tx) * ty + h[j + 1, i + 1] * tx * ty
    return int(round(v * 40))


def read_nav(path):
    """RecastDemo tile-mesh set -> [(xs, ys, zs)] per ground polygon, region units (as build_wallfix_midsa165.py)."""
    d = open(path, 'rb').read()
    magic, ver, ntiles = struct.unpack_from('<iii', d, 0)
    assert magic == 0x4D534554 and ver == 1, path
    off = 40
    pt = np.dtype([('first', '<u4'), ('v', '<u2', 6), ('n', '<u2', 6), ('flags', '<u2'), ('vc', 'u1'), ('at', 'u1')])
    polys = []
    for _ in range(ntiles):
        ref, size = struct.unpack_from('<Qi', d, off)
        off += 16
        if not ref or not size:
            break
        h = struct.unpack_from('<15i', d, off)
        assert h[0] == 0x444E4156 and h[1] == 7, path
        v = np.frombuffer(d, '<f4', h[7] * 3, off + 100).reshape(-1, 3).astype(np.float64) * 32.0
        for q in np.frombuffer(d, pt, h[6], off + 100 + h[7] * 12):
            if q['at'] >> 6:
                continue
            idx = q['v'][:int(q['vc'])]
            polys.append((v[idx, 0].copy(), v[idx, 2].copy(), v[idx, 1].copy()))
        off += size
    return polys


def nav_floors(zone, x, y):
    if zone not in _nav:
        path = next(os.path.join(d, 'zone%03d.nav' % zone) for d in NAV_DIRS
                    if os.path.exists(os.path.join(d, 'zone%03d.nav' % zone)))
        _nav[zone] = read_nav(path)
    out = []
    for xs, ys, zs in _nav[zone]:
        if not (xs.min() <= x <= xs.max() and ys.min() <= y <= ys.max()):
            continue
        inside, j = False, len(xs) - 1
        for k in range(len(xs)):
            if (ys[k] > y) != (ys[j] > y) and x < (xs[j] - xs[k]) * (y - ys[k]) / (ys[j] - ys[k]) + xs[k]:
                inside = not inside
            j = k
        if inside:
            c = np.linalg.lstsq(np.c_[xs, ys, np.ones(len(xs))], zs, rcond=None)[0]
            out.append(int(round(c[0] * x + c[1] * y + c[2])))
    return out


def place(zone, olx, oly, lx, ly, zrule, mate):
    """New Z with every check a move must pass; returns (z, note)."""
    od, oin = wall_side(zone, olx, oly)
    assert not oin or walls_crossed(zone, (olx, oly), mate), ('old spot not behind a wall', zone, olx, oly)
    d, ins = wall_side(zone, lx, ly)
    assert ins and d >= MIN_INSIDE, ('new spot not inside', zone, lx, ly, d, ins)
    md, mins = wall_side(zone, *mate)
    assert mins and walls_crossed(zone, (lx, ly), mate) == 0, ('wall between the new spot and its camp', zone, lx, ly)
    ox, oy = OFF[zone]
    g = ground(zone, lx, ly)
    nav = nav_floors(zone, ox + lx, oy + ly)
    assert nav, ('off the navmesh', zone, lx, ly)
    if zrule[0] == 'swim':      # in the water: the navmesh swim layer its kin use, not the sea floor
        z = zrule[1]
        assert g < z and min(abs(z - v) for v in nav) <= Z_TOL, (zone, lx, ly, z, g, nav)
        return z, 'navmesh swim layer %d as its kin (sea floor %d), %d inside the wall (was %d outside)' % (z, g, d, od)
    n = min(nav, key=lambda v: abs(v - g))
    assert abs(n - g) <= Z_TOL, (zone, lx, ly, g, nav)
    if zrule[0] == 'radar':
        z = zrule[1]
        assert abs(z - g) <= Z_TOL and abs(z - n) <= Z_TOL, (zone, lx, ly, z, g, nav)
        note = 'radar Z %d (client ground %d, on the navmesh)' % (z, g)
    elif zrule[0] == 'hover':
        z = g + zrule[1]
        note = 'client ground %d + its own %d hover (navmesh below)' % (g, zrule[1])
    else:
        z = zrule[1]
        assert abs(z - g) <= Z_TOL and abs(z - n) <= Z_TOL, (zone, lx, ly, z, g, nav)
        note = 'navmesh floor %d (client ground %d)' % (z, g)
    return z, '%s, %d inside the wall (was %d outside)' % (note, d, od)


# ---------------------------------------------------------------- outputs

def plan(world):
    c = sqlite3.connect('file:' + world.replace(' ', '%20') + '?mode=ro', uri=True)
    updates, report = [], []
    for mid, name, zone, olx, oly, oz, lx, ly, zrule, mate, why in BASE_MOVES:
        ox, oy = OFF[zone]
        row = c.execute('SELECT Name, X, Y, Z, Region FROM Mob WHERE Mob_ID = ?', (mid,)).fetchone()
        region = REGION_OF[zone]
        assert row == (name, ox + olx, oy + oly, oz, region), (mid, row)
        z, note = place(zone, olx, oly, lx, ly, zrule, mate)
        sql = ("UPDATE Mob SET X = %d, Y = %d, Z = %d WHERE Mob_ID = '%s' AND Region = %d AND X = %d AND Y = %d"
               % (ox + lx, oy + ly, z, mid, region, ox + olx, oy + oly))
        reason = (TAG + "%s (%s) stood behind the invisible zone wall at %s %d, %d, Z %d, out of a player's reach; %s; "
                  "now %d, %d, Z %d (%s); nothing else changed (build_wallfix_hib165.py)"
                  % (name, mid, ZNAME[zone], olx, oly, oz, why, lx, ly, z, note))
        updates.append({'sql': sql, 'reason': reason})
        report.append((name, mid, zone, (olx, oly, oz), (lx, ly, z), note))
    fixes = {}
    for mid, name, zone, olx, oly, oz, lx, ly, zrule, mate in ADDFILE_MOVES:
        ox, oy = OFF[zone]
        z, note = place(zone, olx, oly, lx, ly, zrule, mate)
        fixes[mid] = {'Name': (name, name), 'X': (ox + olx, ox + lx), 'Y': (oy + oly, oy + ly), 'Z': (oz, z)}
        report.append((name, mid, zone, (olx, oly, oz), (lx, ly, z), note + ' [' + ADDFILE + ']'))
    return updates, fixes, report


def dump(data, newline, final):
    out = json.dumps(data, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', newline)
    return out + newline if final else out


def fix_addfile(name, fixes):
    raws = [open(os.path.join(f, name), 'rb').read() for f in FOLDERS]
    assert raws[0] == raws[1], 'the two copies of %s differ: stop and tell the lead' % name
    raw = raws[0]
    nl = b'\r\n' if b'\r\n' in raw else b'\n'
    data = json.loads(raw.decode('utf-8'))
    final = raw.endswith(nl)
    assert dump(data, nl, final) == raw, '%s does not round-trip byte for byte; not touching it' % name
    key, done, changed = data['key'], set(), 0
    for row in data['rows']:
        want = fixes.get(str(row[key]))
        if not want:
            continue
        done.add(str(row[key]))
        for col, (before, after) in want.items():
            assert row.get(col) in (before, after), '%s %s %s = %r, expected %r' % (name, row[key], col, row.get(col), before)
            if row[col] != after:
                row[col] = after
                changed += 1
    assert done == set(fixes), '%s: rows not found: %s' % (name, set(fixes) - done)
    if changed:
        out = dump(data, nl, final)
        for f in FOLDERS:
            open(os.path.join(f, name), 'wb').write(out)
    return changed


def main():
    updates, fixes, report = plan(CLEAN)
    for name, mid, zone, old, new, note in report:
        print('%-18s %s %-13s %s -> %s  %s' % (name, mid[:8], ZNAME[zone], old, new, note))
    if '--write' not in sys.argv:
        print('(dry run: --write to update the content files)')
        return
    with locks.lock('rules'):
        print('%s: %d values changed' % (ADDFILE, fix_addfile(ADDFILE, fixes)))
    mine = lambda e: str(e.get('reason', '')).startswith(TAG)

    def apply(rules):
        rules['updates'] = [u for u in rules['updates'] if not mine(u)] + updates
    size = locks.edit_rules(LANE, apply)
    print('true165-rules.json: %d updates tagged %r (%d bytes)' % (len(updates), TAG, size))


if __name__ == '__main__':
    main()
