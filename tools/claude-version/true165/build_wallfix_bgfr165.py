"""Wall audit lane BGFR (True 1.65 0.8, 2026-11-12): battlegrounds 250-253 and the housing regions. Brief:
<lead workspace>/wallaudit/WALLAUDIT-BRIEF.md; report <lead workspace>/wallaudit/reports/BGFR.md.

The wall audit itself found nothing to move in these regions. All 98 Abermenai flags were false (no seed point), and
every battleground and housing spawn is inside its zone's walls. This file holds the one follow-up fix the owner asked
for:

Caer Caledon keep Wizard (caledon-guard-25, DOL.GS.Keeps.GuardStaticCaster, Caledonia region 250).
  Its row came from the base world at 33185, 37386, heading 2005. That is the same spot and heading as the Atlas
  Thidranki caster 2b5ff25d (build_bgkeeps165.py ATLAS_GUARDS), which stands on Thidranki Faste's wall-walk at Z 3975.
  It is one spot made for a different keep layout. The real 1.64 client places a different keep in each battleground
  (zones/zone25x/csv25x.mpk fixtures.csv, fixture 3):
    250 bfrontkeep1 (Albion) at 24930, 29908, A 230
    251 Hfrontkeep (Hibernia) at 25216, 30080, A 320
    252 nfrontkeep1 (Norse) at 25088, 30080, A 90
    253 Hfrontkeep (Hibernia) at 24960, 30208, A 160
  The Godot client's zone250 zone.json has exactly these values. In Caledon's Albion keep, 33185 / 33217, 37386 lies
  inside the closed base of the big north tower. The collision faces of bfrontkeep1.gltf, sliced at 3760, 3790, 3830
  and 4150, show a closed outline at every height, and navmesh-165 zone250.nav has a sealed 0.49 M sq pocket at Z
  ~3720 there. The older later-client mesh (navmesh/zone250.nav) has the same pocket, so the navmesh is right and the
  row is wrong. The 2026-11 "True 1.65 BG keeps" nudge (X 33185 -> 33217, "navmesh floor starts at X 33217") only put
  it onto the floor of that sealed pocket.
  A player can never walk to it. The server asks the player's client for a line-of-sight check before it aggroes or
  casts, and the tower walls block that, so the guard can never fight or be fought.
  New spot: the nearest wall-walk floor on the main navmesh, 352 units east, on the north-east wall beside the tower.
  It is 33554, 37488, Z 4106 (navmesh-165 floor 4105.6; at least 48 units of floor all round; at least 200 units from
  every other Caledon keep guard). The other Caledon keep casters stand on the wall-walk the same way (Renegade Wizards
  37 / 46 / 48 and Renegade Runemaster 23 at Z 4106-4125); the north-east wall had none. No period radar or Prima
  record of battleground keep guard spots exists (CapnBry has no battleground sightings). Heading, level, name, model
  and stats are untouched.

    python -I build_wallfix_bgfr165.py [--write]   checks; --write replaces this lane's true165-rules.json entries
                                                   through <lead workspace>/gamefix/locks.py (both copies)
Every reason starts "True 1.65 0.8 wallfix BGFR: "; a re-run replaces them. The update runs after the older
"True 1.65 BG keeps" nudge (rules apply in file order, and this entry is appended), and its WHERE matches the nudged
spot, so it is a no-op on a world where the row has moved elsewhere.
"""
import os, sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks   # noqa: E402

LANE = 'BGFR'
TAG = 'True 1.65 0.8 wallfix %s: ' % LANE
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
NAV = os.path.join(ROOT, 'runtime', 'server', 'navmesh-165', 'zone250.nav')

# (Mob_ID, region, old X, old Y, new X, new Y, new Z, why)
MOVES = [
    ('caledon-guard-25', 250, 33217, 37386, 33554, 37488, 4106,
     "Caer Caledon keep Wizard: its spot (33185/33217, 37386; the Atlas Thidranki caster's spot, made for another keep "
     "layout) is inside the closed base of the north tower of Caledon's 1.64 bfrontkeep1 (csv250.mpk fixtures.csv: "
     "24930, 29908, A 230), a sealed navmesh pocket no player can enter; moved to the nearest wall-walk floor on the "
     "main navmesh, the north-east wall beside the tower (navmesh-165 floor 4106), like the keep's other casters "
     "on the wall-walk"),
]


def check():
    sys.path.insert(0, os.path.join(ROOT, 'development-source', 'server', 'tools', 'navmesh_splice'))
    from navcomp import NavAreas
    m = NavAreas(NAV)
    big = max(m.area, key=m.area.get)
    for mid, reg, ox, oy, nx, ny, nz, _ in MOVES:
        i = m.locate(nx, ny, nz, tolerance=64)
        assert i is not None, (mid, 'new spot not on navmesh-165')
        assert m.root[i] == big, (mid, 'new spot not in the main walkable area')
        assert abs(m.height(i, nx, ny) - nz) <= 16, (mid, m.height(i, nx, ny))
        j = m.locate(ox, oy, 3722, tolerance=64)
        print(mid, 'old spot in main area:', j is not None and m.root[j] == big, '| new spot floor',
              round(m.height(i, nx, ny)), 'main area: True')


def entries():
    return [{'sql': "UPDATE Mob SET X = %d, Y = %d, Z = %d WHERE Mob_ID = '%s' AND Region = %d AND X = %d AND Y = %d"
                    % (nx, ny, nz, mid, reg, ox, oy),
             'reason': TAG + why} for mid, reg, ox, oy, nx, ny, nz, why in MOVES]


def write():
    def fn(rules):
        rules['updates'] = [u for u in rules['updates'] if not u.get('reason', '').startswith(TAG)] + entries()
    n = locks.edit_rules(LANE, fn)
    print('true165-rules.json written (both copies), %d bytes, %d BGFR entries' % (n, len(MOVES)))


if __name__ == '__main__':
    check()
    if '--write' in sys.argv:
        write()
    else:
        for e in entries():
            print(e['sql'])
