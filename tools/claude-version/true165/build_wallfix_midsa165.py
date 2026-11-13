"""Wall audit lane MIDSA (True 1.65 0.8, 2026-11-12): Shrouded Isles Midgard (region 151) spawns in Modernagrav (zone
158) and Munin Sound (zone 155) that stand outside the client's invisible zone walls, where a player can see them but
never walk or swim to them (owner: "move the npcs and the quest markers accordingly").

The walls: the client's content/zones/zoneNNN/bound.json (one-sided, the playable side is each line's right, x east,
y south; game/world/zone_bounds.gd). Each row below was flagged by <lead workspace>/wallaudit/detect_walls.py, checked
against the wall lines, the client ground (heights_2x.f32, metres x 40), the water surface (zone.json) and the server
navmesh (runtime/server/navmesh/zoneNNN.nav), and moved to the playable side with the period position first:
CapnBry's radar (development-source/research/period-data/archive/capnbry/sightings_index.json, zone-local units).
None of them is a quest NPC, quest target, merchant or named monster; all are ordinary monsters whose kin in the same
camp are reachable. Camps move as one group (same offset for every row, relative spacing kept). Levels, templates,
loot, factions, respawns and every other column are untouched.

Modernagrav (zone 158, region offset 425984, 327680):
- 5 sjoalf (breaker / waverunner) on the south-west shore: four stood on the cliff 2,000 units south of the south wall
  (y 60,300-60,700, Z 8,100-9,350: a water monster on a mountain top), one in the sea 287 outside it. CapnBry saw every
  sjoalf of this shore swimming at Z 6,370 in x 900-6,800, y 50,400-55,200. -> group moved 5,400 north into that
  cluster (5,400 rather than more so no row lands within 300 of a sjoalf already there), Z the water surface the world uses for the other sjoalfs (6,355, the navmesh's swim layer).
- 8 morvalt (vaktare / framkalla / blandare) on the ridge north of the north wall (y 6,300-7,350, Z 8,350-9,200). The
  morvalt camp CapnBry saw lies south of the wall (y 8,800-17,700 at these x). -> group moved 2,200 south onto the
  camp's north edge (no row within 160 of a morvalt already there), Z from the navmesh (= client ground within a few units).
- 2 bone-eater eviscerater 185 units east of the east wall. CapnBry saw this camp at and beyond the wall line (the
  later bound.csv cuts it); the one period spot inside (60700, 39940) is already held by a third eviscerater. ->
  group moved 400 west, ~210 inside, Z from the navmesh.
Munin Sound (zone 155, region offset 294912, 376832):
- 5 washed-up skeleton in the sea 220-565 east of the east wall (x 63,700-64,050, Z 3,085). The world's skeleton camp
  on that shore lies just west (x 62,400-63,350) and CapnBry's at x 61,400-62,850. -> group moved 1,000 west, 400+ inside (no row within 160
  of a skeleton already there),
  Z the water surface (3,085) or, where it lands on the beach, the navmesh ground.
- washed-up skeleton 100018798 63 outside the wall on the slope above the Knarr camp -> CapnBry spot 59168, 24975,
  Z 6006 (a washed-up skeleton seen there), 210 inside.
- washed-up skeleton 100019658 197 outside the wall north of the east beach -> CapnBry spot 62190, 35717, Z 4075, 284
  inside.
- drowning victim 100021383 260 outside the north wall -> CapnBry spot 15530, 5795, Z 5459 (the one drowning victim
  seen on that stretch), 468 inside.

Output: true165-rules.json (both copies, through <lead workspace>/gamefix/locks.py edit_rules), one UPDATE per row
("UPDATE Mob SET X, Y, Z WHERE Mob_ID = ... AND X = <old> AND Y = <old>", so a re-run or a moved-again row is a no-op),
every reason starting "True 1.65 0.8 wallfix MIDSA: "; a re-run replaces them. Reads the world read-only.
    python -I build_wallfix_midsa165.py <world db: clean v0.7 or a derived world> [--write]
"""
import json, math, os, sqlite3, struct, sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
NAV_DIR = os.path.join(ROOT, 'runtime', 'server', 'navmesh')
CLIENT = os.environ.get("OFFLINE_DAOC_GODOT_ZONES") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "source", "new-client", "godot", "content", "zones")
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks   # noqa: E402

LANE = 'MIDSA'
TAG = 'True 1.65 0.8 wallfix %s: ' % LANE
REGION = 151
OFF = {155: (36 * 8192, 46 * 8192), 158: (52 * 8192, 40 * 8192)}
ZNAME = {155: 'Munin Sound', 158: 'Modernagrav'}
MIN_INSIDE = 150
Z_TOL = 64

# (zone, group label, source note, [(Mob_ID, name, old lx, old ly, old Z)], move)
#   move = ('shift', dx, dy)          every row by the same offset, Z from the water surface / navmesh ground
#   move = ('spot', lx, ly, z)        one row to a CapnBry radar spot (Z = the radar's Z, checked against the ground)
PLAN = [
    (158, 'sjoalf group, south-west shore',
     "CapnBry saw every sjoalf breaker / waverunner of this shore swimming (Z 6370) at x 900-6800, y 50400-55200; "
     "four of these stood on the cliff behind the south wall (a water monster at Z 8100-9350), one in the sea 287 "
     "outside it; moved together 5400 north into the period cluster",
     [('100019509', 'sjoalf breaker', 7007, 60375, 8405),
      ('100020195', 'sjoalf waverunner', 7104, 60719, 9347),
      ('100020529', 'sjoalf breaker', 6644, 60320, 8121),
      ('100021550', 'sjoalf waverunner', 6306, 60323, 8107),
      ('100018899', 'sjoalf waverunner', 5944, 58655, 6355)],
     ('shift', 0, -5400)),
    (158, 'morvalt group, ridge north of the camp',
     "the morvalt camp CapnBry saw lies south of the north wall (y 8800-17700 at these x); these stood on the ridge "
     "outside it; moved together 2200 south onto the camp's north edge",
     [('1cc1e137-e900-4afc-bee7-1a65c377509d', 'morvalt vaktare', 19563, 7123, 8528),
      ('e6b793e9-cf45-4796-b3f2-bb14222b1b55', 'morvalt vaktare', 20919, 6595, 9190),
      ('48561158-cec6-4765-b41e-a254777bca2d', 'morvalt framkalla', 21812, 6306, 8953),
      ('6c03cd54-6229-4572-9d4f-46a76b49a852', 'morvalt framkalla', 21467, 7239, 8362),
      ('b3932124-b2d5-4b6f-8b22-af754f50a6dd', 'morvalt framkalla', 19547, 6786, 8884),
      ('d251a890-130d-4f0f-8fc6-7613fb0f8af0', 'morvalt framkalla', 21923, 6544, 8657),
      ('d6d5be38-7179-48fb-a03f-550fe4d543cd', 'morvalt blandare', 20151, 6934, 8864),
      ('ff262da1-0017-416c-a950-88ec83c0c8b6', 'morvalt blandare', 20861, 7345, 8454)],
     ('shift', 0, 2200)),
    (158, 'bone-eater eviscerater pair, east wall',
     "CapnBry saw this camp at and beyond the east wall line; the one period spot inside (60700, 39940) already holds "
     "a third eviscerater; moved together 400 west to ~210 inside",
     [('b51f5b2b-f74e-5283-a04a-46f9b7a8f573', 'bone-eater eviscerater', 60926, 39793, 7672),
      ('3b04e58a-6cd1-5390-a60a-27098c22893a', 'bone-eater eviscerater', 61006, 39320, 7790)],
     ('shift', -400, 0)),
    (155, 'washed-up skeleton group, east shore',
     "stood in the sea 220-565 east of the east wall; the world's skeleton camp on this shore lies just west "
     "(x 62400-63350) and CapnBry's at x 61400-62850; moved together 1000 west",
     [('3bb2ce37-d6ea-4333-b2fa-390fe97b9479', 'washed-up skeleton', 63817, 36528, 3086),
      ('595338ad-a383-4da9-a67c-3aa290e1b271', 'washed-up skeleton', 63962, 36931, 3085),
      ('c5a97e78-bace-4198-99fa-7dd25926bbf6', 'washed-up skeleton', 63721, 37742, 3085),
      ('c2669d97-908a-474e-8ef8-2d76efa568e4', 'washed-up skeleton', 64053, 38331, 3085),
      ('424f012c-5662-4e23-8bcd-598b65740658', 'washed-up skeleton', 63708, 38854, 3085)],
     ('shift', -1000, 0)),
    (155, 'washed-up skeleton above the Knarr camp',
     "stood 63 outside the east wall on the slope; CapnBry saw a washed-up skeleton at 59168, 24975, Z 6006",
     [('100018798', 'washed-up skeleton', 59492, 24585, 6278)],
     ('spot', 59168, 24975, 6006)),
    (155, 'washed-up skeleton north of the east beach',
     "stood 197 outside the east wall; CapnBry saw a washed-up skeleton at 62190, 35717, Z 4075",
     [('100019658', 'washed-up skeleton', 62360, 35247, 4942)],
     ('spot', 62190, 35717, 4075)),
    (155, 'drowning victim, north shore',
     "stood 260 outside the north wall; CapnBry saw a drowning victim at 15530, 5795, Z 5459 (the only one on this "
     "stretch of shore)",
     [('100021383', 'drowning victim', 16642, 5175, 5788)],
     ('spot', 15530, 5795, 5459)),
]


def read_nav(path):
    """RecastDemo tile-mesh set -> [(xs, ys, zs, area)] per polygon (region units; ground polygons only)."""
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
            polys.append((v[idx, 0].copy(), v[idx, 2].copy(), v[idx, 1].copy(), int(q['at'] & 63)))
        off += size
    return polys


_nav, _hgt, _lines, _water = {}, {}, {}, {}


def nav_heights(zone, x, y):
    """[(height at x,y, area)] for every navmesh polygon under the region point (plane through its vertices)."""
    if zone not in _nav:
        _nav[zone] = read_nav(os.path.join(NAV_DIR, 'zone%03d.nav' % zone))
    out = []
    for xs, ys, zs, area in _nav[zone]:
        if not (xs.min() <= x <= xs.max() and ys.min() <= y <= ys.max()):
            continue
        inside, j = False, len(xs) - 1
        for k in range(len(xs)):
            if (ys[k] > y) != (ys[j] > y) and x < (xs[j] - xs[k]) * (y - ys[k]) / (ys[j] - ys[k]) + xs[k]:
                inside = not inside
            j = k
        if inside:
            c = np.linalg.lstsq(np.c_[xs, ys, np.ones(len(xs))], zs, rcond=None)[0]
            out.append((int(round(c[0] * x + c[1] * y + c[2])), area))
    return out


def ground(zone, lx, ly):
    """Client terrain height (heights_2x.f32, 513 x 513 metres, 128 units a cell), bilinear, in game units."""
    if zone not in _hgt:
        _hgt[zone] = np.fromfile(os.path.join(CLIENT, 'zone%03d' % zone, 'heights_2x.f32'), '<f4').reshape(513, 513)
    h = _hgt[zone]
    fx, fy = lx / 128.0, ly / 128.0
    i, j = min(int(fx), 511), min(int(fy), 511)
    tx, ty = fx - i, fy - j
    v = (h[j, i] * (1 - tx) * (1 - ty) + h[j, i + 1] * tx * (1 - ty) + h[j + 1, i] * (1 - tx) * ty
         + h[j + 1, i + 1] * tx * ty)
    return int(round(v * 40))


def water(zone):
    if zone not in _water:
        zj = json.load(open(os.path.join(CLIENT, 'zone%03d' % zone, 'zone.json'), encoding='utf-8'))
        _water[zone] = int(round(max(w['height'] for w in zj['water']) * 40))
    return _water[zone]


def wall_side(zone, lx, ly):
    """(distance to the nearest wall segment, True on its playable (right) side) - zone_bounds.gd side()."""
    if zone not in _lines:
        _lines[zone] = json.load(open(os.path.join(CLIENT, 'zone%03d' % zone, 'bound.json'), encoding='utf-8'))['lines']
    best_d, ok = float('inf'), None
    for ln in _lines[zone]:
        for i in range(0, len(ln) - 3, 2):
            ax, ay, bx, by = ln[i:i + 4]
            dx, dy = bx - ax, by - ay
            L = dx * dx + dy * dy
            if L == 0:
                continue
            t = max(0.0, min(1.0, ((lx - ax) * dx + (ly - ay) * dy) / L))
            d = math.hypot(lx - (ax + t * dx), ly - (ay + t * dy))
            if d < best_d:
                best_d, ok = d, dx * (ly - ay) - dy * (lx - ax) >= 0
    return int(round(best_d)), ok


def place(zone, lx, ly, radar_z=None):
    """New Z for a spot, with the checks every move must pass. Returns (z, note)."""
    ox, oy = OFF[zone]
    dist, ok = wall_side(zone, lx, ly)
    assert ok and dist >= MIN_INSIDE, (zone, lx, ly, dist, ok)
    g, w = ground(zone, lx, ly), water(zone)
    nav = nav_heights(zone, ox + lx, oy + ly)
    assert nav, ('off the navmesh', zone, lx, ly)
    if radar_z is not None:
        assert abs(radar_z - g) <= Z_TOL and min(abs(radar_z - n) for n, _ in nav) <= Z_TOL, (zone, lx, ly, radar_z, g, nav)
        return radar_z, 'radar Z %d (ground %d, navmesh %s), %d inside the wall' % (radar_z, g, [n for n, _ in nav], dist)
    if g < w:   # in the sea: the swim layer at the surface, as the world places its other swimmers (water - 15)
        z = min((n for n, _ in nav), key=lambda n: abs(n - (w - 15)))
        assert abs(z - (w - 15)) <= 32, (zone, lx, ly, z, w, nav)
        return z, 'navmesh at the water surface %d (sea floor %d), %d inside the wall' % (z, g, dist)
    z = min((n for n, _ in nav), key=lambda n: abs(n - g))
    assert abs(z - g) <= Z_TOL, (zone, lx, ly, z, g, nav)
    return z, 'navmesh ground %d (client ground %d), %d inside the wall' % (z, g, dist)


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    con = sqlite3.connect('file:' + sys.argv[1].replace(chr(92), '/').replace(' ', '%20') + '?mode=ro', uri=True)
    updates, seen = [], set()
    for zone, label, why, rows, move in PLAN:
        ox, oy = OFF[zone]
        print('%s, %s (%d rows): %s' % (ZNAME[zone], label, len(rows), move))
        for mid, name, lx, ly, oz in rows:
            assert mid not in seen, mid
            seen.add(mid)
            cur = con.execute('select Name, Region, X, Y, Z from Mob where Mob_ID = ?', (mid,)).fetchone()
            assert cur is not None, mid
            assert cur[0] == name and cur[1] == REGION, (mid, cur)
            old_x, old_y = ox + lx, oy + ly
            d0, ok0 = wall_side(zone, lx, ly)
            if (cur[2], cur[3]) != (old_x, old_y):
                print('  %-24s %s already at %d, %d (not %d, %d): the rule will be a no-op' % (name, mid, cur[2], cur[3], old_x, old_y))
            else:
                assert not ok0 or d0 < 20, (mid, d0, ok0)   # it really is outside (or on) the wall today
            if move[0] == 'shift':
                nx, ny = lx + move[1], ly + move[2]
                nz, note = place(zone, nx, ny)
            else:
                nx, ny = move[1], move[2]
                nz, note = place(zone, nx, ny, move[3])
            print('  %-24s %-38s %6d,%6d Z%5d (%4d %s) -> %6d,%6d Z%5d  %s'
                  % (name, mid, lx, ly, oz, d0, 'in' if ok0 else 'out', nx, ny, nz, note))
            sql = ('UPDATE Mob SET X = %d, Y = %d, Z = %d WHERE Mob_ID = \'%s\' AND Region = %d AND X = %d AND Y = %d'
                   % (ox + nx, oy + ny, nz, mid, REGION, old_x, old_y))
            updates.append({'sql': sql, 'reason': TAG + (
                '%s (%s) %s stood behind the invisible zone wall at %s %d, %d, Z %d (%d %s the wall), out of a '
                'player\'s reach; %s; now %d, %d, Z %d (%s); nothing else changed (build_wallfix_midsa165.py)'
                % (name, mid, label, ZNAME[zone], lx, ly, oz, d0, 'inside' if ok0 else 'outside', why, nx, ny, nz,
                   note))})
    print('%d updates' % len(updates))
    if '--write' not in sys.argv:
        print('dry run (add --write)')
        return

    def edit(rules):
        rules['updates'] = [u for u in rules['updates'] if not u.get('reason', '').startswith(TAG)]
        rules['updates'].extend(updates)

    size = locks.edit_rules(LANE, edit)
    print('true165-rules.json written (both copies, %d bytes): %d updates tagged %r' % (size, len(updates), TAG))


if __name__ == '__main__':
    main()
