"""Wall audit lane MIDSB (True 1.65 0.8, 2026-11-12): Shrouded Isles Midgard (region 151) spawns in Aegir's Landing
(zone 151), Gripklosa Mountains (zone 152) and the rest of region 151 except Modernagrav / Munin Sound (lane MIDSA)
that stand outside the client's invisible zone walls, where a player can see them but never walk to them (owner: "move
the npcs and the quest markers accordingly").

The walls: the client's content/zones/zoneNNN/bound.json (one-sided, the playable side is each line's right, x east,
y south; game/world/zone_bounds.gd). Rows from <lead workspace>/wallaudit/out/flagged.json (+ flagged_v2.json), each checked
with lane DETECT's <lead workspace>/wallaudit/tools.py (reach raster from player arrival points, wall side, server navmesh
floor, client ground) and moved to the playable side, the period position first: CapnBry's radar
(development-source/research/period-data/archive/capnbry/sightings_index.json, zone-local units). Levels, templates,
loot, factions, respawns, headings and every other column are untouched. Camps move as one group (same offset).

Aegir's Landing (zone 151, region offset 245760, 311296):
- 3 scimitar kit + Enraged Scimitar Kit on the plateau south of the south rim wall (y 60,000-61,560, 880-2,045
  outside). Period: CapnBry saw kits and the Enraged Scimitar Kit there too, and a Freddyshouse post (April 2003)
  complains that the Enraged Scimitar Kit kill task target stood "on top of the zone wall" where it could not be
  pulled: the same bug in the period. The kits are a quest target (DataQuest 20340-20343 "Helping Dalea": the gilded fang
  from a scimitar kit) and the Enraged Scimitar Kit a named kill-task monster, so both must be reachable. -> two pairs moved straight
  north just inside the rim (300 inside, spacing kept), Z the navmesh ground.
- biting insect 100020534 (flying, from flagged_v2) 39 outside the south rim at 61799, 58930 -> CapnBry spot 61592,
  59018, Z 4599 (a biting insect seen there, 168 inside; navmesh 4608).
- mygga 100020556 (flying) 149 outside a lake-shore wall -> CapnBry spot 41210, 41526, Z 3662 (a mygga seen there,
  313 inside).
Gripklosa Mountains (zone 152, region offset 278528, 245760):
- 2 morvalt beskydda + 3 myling on top of the walled ridge east of the morvalt valley (Z 7,900-9,050; the valley camp
  CapnBry saw is at Z 3,950-4,250, all 148 beskydda and 105 myling sightings on the playable side). morvalt beskydda is
  a quest target (DataQuest 21246 "Morvalt Crystal", three kills). -> 37b2b9ef to the CapnBry beskydda spot 40001, 39740,
  Z 4079 (valley floor, 1,440 inside); the other four to the nearest playable point 300 inside the ridge wall next to
  their camp-mates on the slopes (Z the navmesh ground).
- young mammoth 100021188 on the mountain in the north-east corner, 697 outside (no young mammoth seen there; the one
  CapnBry sighting in this zone is in Mammoth Valley) -> nearest playable point 300 inside, navmesh ground.
- griffon glider 100021643 (flying, 649 above the ground) in a sealed mountain pocket where no glider was seen (182 of
  185 sightings playable); it drops Griffon_Glider_Head -> nearest playable point 300 inside, the same height above
  the navmesh ground.
Left alone (reason in the report): 3 griffon scout on the peaks around the griffon camp - CapnBry saw 7 of the 51
griffon scouts in that same out-of-bounds mountain mass (flying griffons), no loot, no quest role.

Output: true165-rules.json (both copies, through <lead workspace>/gamefix/locks.py edit_rules), one UPDATE per row
("UPDATE Mob SET X, Y, Z WHERE Mob_ID = ... AND Region = 151 AND X = <old> AND Y = <old>", so a re-run or a moved-again
row is a no-op), every reason starting "True 1.65 0.8 wallfix MIDSB: "; a re-run replaces them. World read-only.
    python -I build_wallfix_midsb165.py <world db: clean v0.7 or a derived world> [--write]
"""
import os, sqlite3, sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks   # noqa: E402
import tools   # noqa: E402  lane DETECT: reach raster, wall side, navmesh floor, client ground

LANE = 'MIDSB'
TAG = 'True 1.65 0.8 wallfix %s: ' % LANE
REGION = 151
ZNAME = {151: "Aegir's Landing", 152: 'Gripklosa Mountains'}
MIN_INSIDE = 150
Z_TOL = 64

# (zone, group label, source note, [(Mob_ID, name, old lx, old ly, old Z)], move)
#   move = ('shift', dx, dy)        every row by the same offset; Z the navmesh ground (+ the old height above the
#                                   ground for flying rows, Flags & 32)
#   move = ('spot', lx, ly, z)      one row to a CapnBry radar spot (Z = the radar's Z, checked against the ground)
PLAN = [
    (151, 'kit pair at the south rim (west)',
     "kits and the Enraged Scimitar Kit stood on the plateau behind the south rim wall; CapnBry saw them there too and "
     "a period forum post (Freddyshouse, April 2003) calls the Enraged Scimitar Kit kill task 'on top of the zone "
     "wall', unpullable - a period bug; the kit is the quest target of Helping Dalea (DataQuest 20340-20343) and the Enraged kit a "
     "named kill-task monster, so moved together 2351 north to 300 inside the rim",
     [('100019367', 'scimitar kit', 15652, 61556, 5552),
      ('100020430', 'Enraged Scimitar Kit', 15764, 61565, 5525)],
     ('shift', -52, -2351)),
    (151, 'kit pair at the south rim (east)',
     "two kits stood on the plateau behind the south rim wall (CapnBry saw kits there too, out of reach as in the "
     "period); the kit is the quest target of Helping Dalea (DataQuest 20340-20343; loc 28k, 57k in the quest text; the zone's kit camps "
     "at x 17-19k / 26-29k are inside); moved together 1264 north to 300 inside the rim",
     [('100021197', 'scimitar kit', 18349, 60005, 5795),
      ('100021721', 'scimitar kit', 17759, 60019, 5764)],
     ('shift', -210, -1264)),
    (151, 'mygga at the lake shore',
     "stood 149 outside a shore wall; CapnBry saw a mygga at 41210, 41526, Z 3662 (313 inside)",
     [('100020556', 'mygga', 41167, 41026, 4214)],
     ('spot', 41210, 41526, 3662)),
    (151, 'biting insect at the south-east rim',
     "flew 39 outside the south rim wall (found by DETECT's flagged_v2); CapnBry saw a biting insect at 61592, 59018, "
     "Z 4599 (168 inside); 98 of the zone's 99 biting insects are playable",
     [('100020534', 'biting insect', 61799, 58930, 5240)],
     ('spot', 61592, 59018, 4599)),
    (152, 'morvalt beskydda on the ridge west of the pocket',
     "stood on top of the walled ridge east of the morvalt valley (Z 9046, valley floor 4100); all 148 CapnBry "
     "sightings of morvalt beskydda are in the valley; quest target of Morvalt Crystal (DataQuest 21246); moved to the CapnBry spot 40001, 39740, Z 4079",
     [('37b2b9ef-5981-4fca-9554-100944a1b0ff', 'morvalt beskydda', 42142, 41120, 9046)],
     ('spot', 40001, 39740, 4079)),
    (152, 'morvalt beskydda at the ridge tip',
     "stood inside the walled ridge tip (278 outside); its camp-mate beskydda stands 660 east on the playable slope "
     "(CapnBry: every beskydda sighting playable); moved to the nearest point 300 inside",
     [('5466afdf-affa-4ff9-9b43-a402b4d82b12', 'morvalt beskydda', 44985, 37998, 8706)],
     ('shift', 143, -575)),
    (152, 'myling on the ridge (west)',
     "stood on top of the walled ridge (330 outside); all 105 CapnBry myling sightings are playable (valley to the "
     "west); moved to the nearest point 300 inside, on the valley side",
     [('921fb9d2-74e6-4fd9-a291-fed569f44922', 'myling', 41342, 40895, 7897)],
     ('shift', -550, -328)),
    (152, 'myling on the ridge (south)',
     "stood on top of the walled ridge (773 outside); all 105 CapnBry myling sightings are playable; moved to the "
     "nearest point 300 inside (south-east slope, by the bergklan camp)",
     [('b2d4c716-97b1-477f-b955-bd51aaceaea1', 'myling', 42090, 41778, 8925)],
     ('shift', 887, 630)),
    (152, 'myling at the ridge tip',
     "stood inside the walled ridge tip (165 outside), 360 from a playable beskydda of its camp; all 105 CapnBry "
     "myling sightings are playable; moved to the nearest point 300 inside",
     [('d9977a02-bf91-4c4c-b868-be5b861dee1f', 'myling', 45522, 38079, 8675)],
     ('shift', 202, -435)),
    (152, 'young mammoth on the north-east mountain',
     "stood on the mountain behind the north-east wall (697 outside); CapnBry saw no young mammoth there (the one "
     "sighting in this zone is in Mammoth Valley, 56215, 9087); moved to the nearest point 300 inside",
     [('100021188', 'young mammoth', 62975, 18157, 9880)],
     ('shift', -615, 799)),
    (152, 'griffon glider in a sealed mountain pocket',
     "flew in a small walled pocket on the western mountains (648 outside) where CapnBry saw no glider (182 of 185 "
     "sightings playable); drops Griffon_Glider_Head; moved to the nearest point 300 inside at the same height above "
     "the ground",
     [('100021643', 'griffon glider', 17287, 19752, 10146)],
     ('shift', 461, 842)),
]


def place(zone, lx, ly, old_lx, old_ly, old_z, flying, radar_z=None):
    """New Z for a spot, with the checks every move must pass. Returns (z, note)."""
    _, x, y = tools.world(zone, lx, ly)
    dist, ok, _wz = tools.wall_side(REGION, x, y)
    assert ok and dist >= MIN_INSIDE, (zone, lx, ly, dist, ok)
    assert tools.is_reachable(REGION, x, y), ('not reachable', zone, lx, ly)
    terr = tools.terrain_z(REGION, x, y)
    if radar_z is not None:
        g = tools.ground_z(REGION, x, y, radar_z)
        if flying:   # a flyer's radar Z may be a little above the ground, never under it
            assert -Z_TOL <= radar_z - terr <= 2000 and abs(radar_z - g) <= 2 * Z_TOL, (zone, lx, ly, radar_z, g, terr)
        else:
            assert abs(radar_z - g) <= Z_TOL and abs(radar_z - terr) <= Z_TOL, (zone, lx, ly, radar_z, g, terr)
        assert tools.nav_poly(REGION, x, y, radar_z) is not None, ('off the navmesh', zone, lx, ly, radar_z)
        return radar_z, 'radar Z %d (navmesh %d, ground %d), %d inside the wall' % (radar_z, g, terr, dist)
    g = int(round(tools.ground_z(REGION, x, y, terr)))
    assert abs(g - terr) <= Z_TOL, (zone, lx, ly, g, terr)
    assert tools.nav_poly(REGION, x, y, g) is not None, ('off the navmesh', zone, lx, ly, g)
    if flying:
        _, ox, oy = tools.world(zone, old_lx, old_ly)
        above = max(0, int(round(old_z - tools.terrain_z(REGION, ox, oy))))
        return g + above, 'flying: navmesh ground %d + its old %d above the ground, %d inside the wall' % (g, above, dist)
    return g, 'navmesh ground %d (client ground %d), %d inside the wall' % (g, int(round(terr)), dist)


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    world = sys.argv[1]
    tools.set_world(world)
    con = sqlite3.connect('file:' + world.replace(chr(92), '/').replace(' ', '%20') + '?mode=ro', uri=True)
    updates, seen = [], set()
    for zone, label, why, rows, move in PLAN:
        print('%s, %s (%d rows): %s' % (ZNAME[zone], label, len(rows), move))
        for mid, name, lx, ly, oz in rows:
            assert mid not in seen, mid
            seen.add(mid)
            cur = con.execute('select Name, Region, X, Y, Z, Flags from Mob where Mob_ID = ?', (mid,)).fetchone()
            assert cur is not None, mid
            assert cur[0] == name and cur[1] == REGION, (mid, cur)
            _, old_x, old_y = tools.world(zone, lx, ly)
            d0, ok0, _ = tools.wall_side(REGION, old_x, old_y)
            if (cur[2], cur[3]) != (old_x, old_y):
                print('  %-22s %s already at %d, %d (not %d, %d): the rule will be a no-op' % (name, mid, cur[2], cur[3], old_x, old_y))
            else:
                assert not tools.is_reachable(REGION, old_x, old_y), (mid, 'reachable today')
            flying = bool((cur[5] or 0) & 32)
            if move[0] == 'shift':
                nx, ny = lx + move[1], ly + move[2]
                nz, note = place(zone, nx, ny, lx, ly, oz, flying)
            else:
                nx, ny = move[1], move[2]
                nz, note = place(zone, nx, ny, lx, ly, oz, flying, move[3])
            _, wx, wy = tools.world(zone, nx, ny)
            print('  %-22s %-38s %6d,%6d Z%5d (%4d %s) -> %6d,%6d Z%5d  %s'
                  % (name, mid, lx, ly, oz, d0, 'in' if ok0 else 'out', nx, ny, nz, note))
            sql = ('UPDATE Mob SET X = %d, Y = %d, Z = %d WHERE Mob_ID = \'%s\' AND Region = %d AND X = %d AND Y = %d'
                   % (wx, wy, nz, mid, REGION, old_x, old_y))
            updates.append({'sql': sql, 'reason': TAG + (
                '%s (%s) %s stood behind the invisible zone wall at %s %d, %d, Z %d (%d %s the wall), out of a '
                'player\'s reach; %s; now %d, %d, Z %d (%s); nothing else changed (build_wallfix_midsb165.py)'
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
