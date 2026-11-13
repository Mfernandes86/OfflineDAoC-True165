"""Gamefix lane ZONES (True 1.65 0.8, 2026-11-12): monster rows that stand outside every zone of their region.

The server finds a spot's zone with Region.GetZone (Zones row: OffsetX/OffsetY/Width/Height in units of 8192); a Mob
row outside every zone square never spawns (GameObject.AddToWorld logs "Couldn't find a zone" and returns false).
In the classic and Shrouded Isles regions that happens to 172 inherited rows, in three one-zone dungeons whose Zones
rows are right (offset 1,1, 8 x 8: the 1.64 client's zones.dat has the same for Tomb of Mithra, the later client's for
all three, and every navmesh lies inside the square). The rows are the error: each group sits an exact number of zone
blocks (8192) away from the dungeon, and moved back by that block shift every row of Iarnvidiur's Lair and Krondon and
103 of 113 rows of Tomb of Mithra land on the navmesh floor at their own height.

- Iarnvidiur's Lair (161), 42 rows (package Yggdrasil, X + 56 blocks): period spawns. Moved back they stand on the
  floor where CapnBry's radar saw the same monsters at the same levels (41 of 42 within 300 units, 9 within 2 units),
  where the world often has no spawn of that kind today, and every name is in Prima's Shrouded Isles "Iarnvidiur
  Monsters" table. -> MOVED: X - 458752 (absolute values per row; Y and Z unchanged).
- Krondon (61), 17 rows (package Yggdrasil, Y - 8 blocks): an old unscripted copy of the Krondon boss rooms. Drihten
  Elreden, Lurfos the Herald, Orylle and Orshom Brond (sic) stand in the zone already as their scripted encounter
  classes, 49 to 386 units from where the copies would land; Orshom's Fire is spawned by the Orylle/Orshom script;
  "Drihten's leornere" (10) has no period source (no CapnBry data for Krondon, not in Prima). Moving them would double
  four named bosses. -> ARCHIVED (rules "remove" -> "where", kept in offline_true165_removed).
- Tomb of Mithra (21), 113 rows (package Public_DB, level 0 rows on NPC templates, X + 8 blocks): an older copy of the
  dungeon's population. Every one of its 27 names is already in the zone (245 rows; "menacing presence" there as
  "menacing presense"), 100 of the 113 would land within 300 units of a spawn already there, and in the part of the
  tomb CapnBry covers the radar saw at most 172 distinct monster spots where the world has 143 today and would have
  207 with the copies (a starter dungeon 46 % fuller). -> ARCHIVED.
Region 233 (the New Frontiers Summoner's Hall, no Zones row, closed by disabled_regions) is later content: left alone.
Monster levels and numbers are not touched.

Output: true165-rules.json (both copies, through <lead workspace>/gamefix/locks.py edit_rules), entries whose reason starts
with "True 1.65 0.8 gamefix ZONES: "; a re-run replaces them. Read-only on the world it reads (mode=ro).
    python -I build_gamefix_zones165.py <clean v0.7 world db> [--write]
"""
import collections, json, os, sqlite3, struct, sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
NAV_DIRS = [os.path.join(ROOT, 'runtime', 'server', 'navmesh-165'), os.path.join(ROOT, 'runtime', 'server', 'navmesh')]
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks   # noqa: E402

LANE = 'ZONES'
TAG = locks.TAG % LANE
BLOCK = 8192
# region -> (block shift that puts the rows back on the dungeon's floor, action, expected row count)
PLAN = {161: ((-56, 0), 'move', 42), 61: ((0, 8), 'archive', 17), 21: ((-8, 0), 'archive', 113)}
FLOOR_TOL = 96


def read_nav(path):
    """RecastDemo tile-mesh set -> [(xs, ys, mean height)] per walkable polygon, region units."""
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
            polys.append((v[idx, 0].copy(), v[idx, 2].copy(), float(v[idx, 1].mean())))
        off += size
    return polys


def floor_dz(polys, x, y, z):
    """Smallest |floor - z| over the polygons that contain (x, y); None off the mesh."""
    best = None
    for xs, ys, hz in polys:
        if not (xs.min() <= x <= xs.max() and ys.min() <= y <= ys.max()):
            continue
        inside, j = False, len(xs) - 1
        for k in range(len(xs)):
            if (ys[k] > y) != (ys[j] > y) and x < (xs[j] - xs[k]) * (y - ys[k]) / (ys[j] - ys[k]) + xs[k]:
                inside = not inside
            j = k
        if inside and (best is None or abs(hz - z) < best):
            best = abs(hz - z)
    return best


def nav_for(zone):
    for d in NAV_DIRS:
        p = os.path.join(d, 'zone%03d.nav' % zone)
        if os.path.exists(p):
            return read_nav(p)
    raise SystemExit('no navmesh for zone %d' % zone)


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    con = sqlite3.connect('file:' + sys.argv[1].replace(chr(92), '/') + '?mode=ro', uri=True)
    zones = collections.defaultdict(list)
    for zid, rid, ox, oy, w, h in con.execute('select ZoneID, RegionID, OffsetX, OffsetY, Width, Height from Zones'):
        zones[rid].append((zid, ox * BLOCK, oy * BLOCK, w * BLOCK, h * BLOCK))

    def zone_of(rid, x, y):
        return next((z[0] for z in zones.get(rid, []) if z[1] <= x < z[1] + z[3] and z[2] <= y < z[2] + z[4]), None)

    rules_where, rules_updates = [], []
    for rid, ((bx, by), action, expect) in sorted(PLAN.items()):
        name = con.execute('select Description from Regions where RegionID=?', (rid,)).fetchone()[0]
        rows = [r for r in con.execute('select Mob_ID, Name, X, Y, Z from Mob where Region=? order by Mob_ID', (rid,))
                if zone_of(rid, r[2], r[3]) is None]
        assert len(rows) == expect, (rid, len(rows), expect)
        polys = nav_for(zones[rid][0][0])
        fit = 0
        for mid, n, x, y, z in rows:
            nx, ny = x + bx * BLOCK, y + by * BLOCK
            assert zone_of(rid, nx, ny) == zones[rid][0][0], (mid, nx, ny)
            dz = floor_dz(polys, nx, ny, z)
            fit += dz is not None and dz < FLOOR_TOL
        print('%3d %-18s %3d rows outside every zone, %3d on the floor after a (%d, %d) block shift -> %s'
              % (rid, name, len(rows), fit, bx, by, action))
        ids = ','.join("'%s'" % r[0] for r in rows)
        if action == 'move':
            assert fit == len(rows), 'every moved row must land on the floor'
            case = ' '.join("WHEN '%s' THEN %d" % (r[0], r[2] + bx * BLOCK) for r in rows)
            sql = ('UPDATE Mob SET X = CASE Mob_ID %s END WHERE Region = %d AND X >= %d AND Mob_ID IN (%s)'
                   % (case, rid, zones[rid][0][1] + zones[rid][0][3], ids))
            rules_updates.append({'sql': sql, 'reason': TAG + (
                "%s: %d period spawns (cadby, syver, animus syndig, captured iarn dwarf; package Yggdrasil) stood 56 "
                "zone blocks (458,752 units) east of the dungeon, outside its zone, so the server never spawned them; "
                "moved back onto the dungeon floor where CapnBry's radar saw them (Prima SI Iarnvidiur Monsters); "
                "levels unchanged (build_gamefix_zones165.py)" % (name, len(rows)))})
        else:
            why = {61: "an old unscripted copy of the Krondon boss rooms (Drihten Elreden, Lurfos the Herald, Orylle, "
                       "Orshom: their scripted encounters stand in the zone; Orshom's Fire comes from the script; "
                       "Drihten's leornere has no period source) parked 8 zone blocks south of the dungeon, never "
                       "spawned; moving them would double four named bosses",
                   21: "an older copy of the tomb's population (every name already in the zone, 100 of 113 within 300 "
                       "units of a spawn there; CapnBry's radar shows no room for a 46 % fuller starter dungeon) "
                       "parked 8 zone blocks east of the dungeon, never spawned"}[rid]
            rules_where.append({'table': 'Mob', 'key': 'Mob_ID',
                                'where': 'Region = %d AND Mob_ID IN (%s)' % (rid, ids),
                                'reason': TAG + '%s: %d rows outside every zone square: %s (build_gamefix_zones165.py)'
                                % (name, len(rows), why)})

    if '--write' not in sys.argv:
        print('dry run: %d where rules, %d updates (add --write)' % (len(rules_where), len(rules_updates)))
        return

    def edit(rules):
        rules['remove']['where'] = [w for w in rules['remove']['where'] if not w.get('reason', '').startswith(TAG)]
        rules['remove']['where'].extend(rules_where)
        rules['updates'] = [u for u in rules['updates'] if not u.get('reason', '').startswith(TAG)]
        rules['updates'].extend(rules_updates)

    size = locks.edit_rules(LANE, edit)
    print('true165-rules.json written (both copies, %d bytes): %d where rules, %d updates'
          % (size, len(rules_where), len(rules_updates)))


if __name__ == '__main__':
    main()
