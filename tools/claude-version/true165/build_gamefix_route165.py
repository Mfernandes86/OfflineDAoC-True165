"""Gamefix lane ROUTE (True 1.65 0.8, 2026-11-12): horse routes that stop short of their destination stable.

HS_Ludlow_CastleSauvage (Yaren, Ludlow -> "Ticket to Castle Sauvage") has ended 9,686 units short of Uliam, the Castle
Sauvage stable master, since the inherited OpenDAoC rows (53 points, 2018): the last point is at the Veteran Guardsman
post on the Camelot Hills road (584252, 488597), and the rider was set down there. The period sources put the ride's end
at Uliam (Prima's Atlas stable table "Castle Sauvage - Uliam"; CapnBry's radar has Uliam in Forest Sauvage, zone 11, at
20361, 61188, Z 2600 = world 585609, 478980, 944 units east of the world row, in the same stable yard), and every other
route into Castle Sauvage ends 180-227 from the world row. No period
source records path points, so the missing stretch is the reverse route's own road: HS_CastleSauvage_Ludlow (Uliam ->
Ludlow) runs from Uliam down that road and passes 189 units from the short route's end. Its points (already on the floor,
build_routefloor165 + refine_routefloor165) are taken in reverse, from that pass-by point up to the stable yard, and the
ride ends on the spot the other Castle Sauvage arrivals use (the last point of HS_AdribardsRetreat_CastleSauvage). Each
new leg is checked against the server's navmeshes (groundz165) with the route-floor rules (never under the floor, at
most FLOAT over it, the clients' floor pick on a bridge); a leg that fails is filled from the floor and thinned again.

Output: add-gamefix-route165.json (table PathPoints, key PathPoints_ID), rows gamefix-route-<path>-<step>, steps after
the route's last existing step; both content copies, identical. No rows are removed: the existing points stay.
How it lives with build_routefloor165.py: that pass's rule archives every point of its routes whose id does not start
with p165floor-, so it carries "AND PathPoints_ID NOT LIKE 'gamefix-route-%'" (this generator adds it to the rule if it is
missing, through locks.edit_rules; build_routefloor165.py writes it itself and skips these rows when it reads a world).
refine_routefloor165.py's rule only touches p165floor- rows and is not affected.

Read-only on the world it reads (mode=ro). Usage:
    python -I build_gamefix_route165.py <clean v0.7 world db> [--write]
"""
import json, math, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import groundz165 as g   # noqa: E402

PREFIX = 'gamefix-route-'
OUT = 'add-gamefix-route165.json'
REASON = ('True 1.65 0.8 gamefix ROUTE: horse routes that stopped short of their destination stable ride on to it '
          '(build_gamefix_route165.py)')
NOW = '2026-11-12 12:00:00'
SPEED = 1000          # the stable horse's own speed (GameStableMaster.cs MaxSpeedBase = 1000; the planner clamps to it)
LIFT = 8              # route points sit this far over the floor (build_routefloor165.py)
SINK = 2              # the line may run at most this far under floor + LIFT (build_routefloor165.py ABOVE)
FLOAT = 40            # ... and at most this far over it (refine_routefloor165.py)
CLIENT_RISE = 40      # on a spot with more than one floor a leg climbs at most this far over its start (refine_routefloor165)
SAMPLE = 20
FILL = 40             # floor samples when a failing leg is filled
XY_TOL = 24
REACH = 120           # a floor further than this from the line is another surface (refine_routefloor165.py)

ROUTES = [
    dict(path='HS_Ludlow_CastleSauvage',          # the short route
         via='HS_CastleSauvage_Ludlow',           # the destination's own route back along the same road
         land='HS_AdribardsRetreat_CastleSauvage',  # the landing spot the other arrivals there use (its last point)
         expect_end=(584252, 488597),             # guard: the short end as it stands in the v0.7 world
         master='Uliam', region=1),
]


def world_zones(db):
    """groundz165 reads the zones zones.dat lacks from the played world; use the world given here instead."""
    def zones():
        return {int(z): dict(name=n, region=int(r), x=int(ox) * 8192, y=int(oy) * 8192, w=int(w) * 8192, h=int(h) * 8192)
                for z, r, n, ox, oy, w, h in db.execute('select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height from Zones')}
    return zones


def points(db, pid):
    return [dict(Step=s, X=x, Y=y, Z=z, MaxSpeed=ms, WaitTime=w) for s, x, y, z, ms, w in db.execute(
        "SELECT Step, X, Y, Z, MaxSpeed, WaitTime FROM PathPoints WHERE PathID = ? AND PathPoints_ID NOT LIKE ? "
        "ORDER BY Step", (pid, PREFIX + '%'))]


def riding_floor(region, x, y, z):
    fl = g.floors(region, int(round(x)), int(round(y)))
    if not fl:
        return None, 0
    return min(fl, key=lambda h: abs(h - (z - LIFT))), len(fl)


def samples(a, b, step=SAMPLE):
    L = math.hypot(b['X'] - a['X'], b['Y'] - a['Y'])
    n = max(1, int(L // step))
    for k in range(1, n):
        t = k / n
        yield t, a['X'] + (b['X'] - a['X']) * t, a['Y'] + (b['Y'] - a['Y']) * t, a['Z'] + (b['Z'] - a['Z']) * t


def leg_faults(region, a, b):
    """(under the floor, floating, client dips, samples without navmesh) along a->b."""
    under = floating = dips = holes = 0
    for t, x, y, z in samples(a, b):
        f, n = riding_floor(region, x, y, z)
        if f is None:
            holes += 1
            continue
        if abs(f + LIFT - z) > REACH:
            continue
        if f + LIFT - z > SINK:
            under += 1
        if z - (f + LIFT) > FLOAT:
            floating += 1
        if n > 1 and f - a['Z'] > CLIENT_RISE:
            dips += 1
    return under, floating, dips, holes


def leg_ok(region, a, b, dropped=()):
    for p in dropped:
        dx, dy = b['X'] - a['X'], b['Y'] - a['Y']
        L2 = dx * dx + dy * dy or 1
        t = max(0.0, min(1.0, ((p['X'] - a['X']) * dx + (p['Y'] - a['Y']) * dy) / L2))
        if math.hypot(a['X'] + dx * t - p['X'], a['Y'] + dy * t - p['Y']) > XY_TOL:
            return False
    u, f, d, _ = leg_faults(region, a, b)
    return u == f == d == 0


def fill(region, a, b):
    """A failing leg: points on the floor every FILL units (following the surface it starts on), thinned again."""
    dense, prev = [a], a['Z'] - LIFT
    for t, x, y, z in samples(a, b, FILL):
        fl = [h for h in g.floors(region, int(round(x)), int(round(y))) if abs(h - prev) <= 300]
        if not fl:
            continue
        prev = min(fl, key=lambda h: abs(h - prev))
        dense.append(dict(X=int(round(x)), Y=int(round(y)), Z=int(prev + LIFT), MaxSpeed=SPEED, WaitTime=0))
    dense.append(b)
    out, i = [dense[0]], 0
    while i < len(dense) - 1:
        best = i + 1
        for j in range(i + 2, len(dense)):
            if not leg_ok(region, dense[i], dense[j], dense[i + 1:j]):
                break
            best = j
        out.append(dense[best])
        i = best
    return out[1:-1]


def build(db, spec):
    fwd, via, land = points(db, spec['path']), points(db, spec['via']), points(db, spec['land'])
    end = fwd[-1]
    assert (end['X'], end['Y']) == spec['expect_end'], f"{spec['path']} no longer ends at {spec['expect_end']}: re-check it"
    master = db.execute("SELECT X, Y, Z FROM Mob WHERE Name = ? AND Region = ? AND ClassType LIKE '%StableMaster%'",
                        (spec['master'], spec['region'])).fetchone()
    # the reverse route's point nearest the short end, then its points back up to the stable yard (its step 3; steps 1-2
    # are the departure spot, 20 units apart)
    k = min(range(len(via)), key=lambda i: math.hypot(via[i]['X'] - end['X'], via[i]['Y'] - end['Y']))
    tail = [dict(p) for p in reversed(via[2:k + 1])] + [dict(land[-1])]
    region, out, fixed = spec['region'], [], 0
    for p in tail:
        p['MaxSpeed'], p['WaitTime'] = SPEED, 0
        # a point under the floor at its own spot sits on it (build_routefloor165.py: the landing copied from the
        # Adribard's Retreat route is 12 under the yard's floor + LIFT)
        f, _ = riding_floor(region, p['X'], p['Y'], p['Z'])
        if f is not None and abs(f + LIFT - p['Z']) <= REACH and f + LIFT - p['Z'] > SINK:
            p['Z'] = f + LIFT
    prev = end
    for b in tail:
        if not leg_ok(region, prev, b):
            extra = fill(region, prev, b)
            out.extend(extra)
            fixed += 1
        out.append(b)
        prev = b
    rows = [{'PathPoints_ID': f"{PREFIX}{spec['path']}-{end['Step'] + i}", 'PathID': spec['path'], 'Step': end['Step'] + i,
             'X': int(p['X']), 'Y': int(p['Y']), 'Z': int(p['Z']), 'MaxSpeed': SPEED, 'WaitTime': 0,
             'LastTimeRowUpdated': NOW} for i, p in enumerate(out, 1)]
    full = fwd + out
    report = dict(path=spec['path'], via_step=via[k]['Step'], join_gap=round(math.hypot(via[k]['X'] - end['X'], via[k]['Y'] - end['Y'])),
                  added=len(rows), legs_filled=fixed,
                  gap_before=round(math.hypot(end['X'] - master[0], end['Y'] - master[1])),
                  gap_after=round(math.hypot(out[-1]['X'] - master[0], out[-1]['Y'] - master[1])),
                  faults_new_legs=[sum(x) for x in zip(*(leg_faults(region, a, b) for a, b in zip([end] + out, out)))])
    return rows, report, full


def rule_fix(rules):
    """build_routefloor165's archive rule must leave this lane's rows alone (see the module notes)."""
    tag = 'True 1.65 horse routes follow the floor'
    extra = f" AND PathPoints_ID NOT LIKE '{PREFIX}%'"
    for w in rules['remove']['where']:
        if w.get('reason', '').startswith(tag) and w['table'] == 'PathPoints' and extra not in w['where']:
            w['where'] += extra


def main():
    db_path = sys.argv[1]
    db = sqlite3.connect(f"file:{db_path.replace(os.sep, '/')}?mode=ro", uri=True)
    g._server_zones = world_zones(db)
    rows = []
    for spec in ROUTES:
        r, report, _ = build(db, spec)
        rows += r
        print(json.dumps(report))
    if '--write' not in sys.argv:
        return
    data = json.dumps({'table': 'PathPoints', 'key': 'PathPoints_ID', 'reason': REASON, 'rows': rows}, indent=1) + '\n'
    for folder in (HERE, os.path.normpath(os.path.join(HERE, '..', '..', '..', 'runtime', 'true165'))):
        with open(os.path.join(folder, OUT), 'w', encoding='utf-8', newline='\n') as f:
            f.write(data)
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
    import locks
    locks.edit_rules('ROUTE', rule_fix)
    print('written', len(rows), 'rows to both copies of', OUT)


if __name__ == '__main__':
    main()
