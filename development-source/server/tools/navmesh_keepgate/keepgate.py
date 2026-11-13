"""Seal keep gatehouses in a server navmesh: disable the walkable polygons that lie inside the keep's solid client
collision (Recast walks the terrain under a keep's raised gate-passage floor and inside its closed blocks, so bots
pass shut gates; BG250 N1, Caer Caledon 2026-11-12).

    python keepgate.py <zone id> <in.nav> <out.nav> <world.sqlite3.db>      # find, patch, verify
    python keepgate.py <zone id> <in.nav> - <world.sqlite3.db>              # find + verify only, write nothing

A polygon is disabled when it is a ground polygon of the main walkable area (all doors open), not a door polygon,
and at least half of its 16-unit sample points are inside a keep's collision solid both 25 and 50 units above the
floor (four-direction ray parity on horizontal slices, keepsolid.Solid). Disabled = flags 0x10 only, exactly like
navmesh_shadow_islands/shadowfix.py: the server's default filter (include All ^ Disabled) then never uses it, and
the door code (Door mask) never registers it. Every other byte of the file is kept.

Verification (printed): main area loses exactly the disabled polygons (nothing stranded); every keep guard, the keep
row centre and the arrivals keep their open-door connectivity; with each keep's gate doors shut (the polygons the
server registers: Door-flag polygons in a 64 x 64 x 128 box round the door row, LocalPathfindingMgr.RegisterDoor),
which inside points are still reachable from the bot arrivals, before and after.
"""
import hashlib, os, sqlite3, sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import navkit
import keepsolid

ARRIVALS = {"Alb arrival": (36428, 51260, 3952), "Mid arrival": (53150, 26093, 4263), "Hib arrival": (19714, 19085, 4094),
            "Alb entry": (38113, 53507, 4160), "Mid entry": (53568, 23643, 4530), "Hib entry": (17367, 18248, 4320)}
DOOR_BOX, DOOR_Z = 64, 128
STRAND_AREA_MAX = 40000   # sq units: the largest dead-end patch a seal may cut off (about 200 x 200)


def md5(path):
    return hashlib.md5(open(path, "rb").read()).hexdigest()


def samples(nv, i, step=16):
    bb = nv.bbox[i]
    pts = [(x, y) for x in np.arange(bb[0] + step / 2, bb[2], step) for y in np.arange(bb[1] + step / 2, bb[3], step)
           if nv.contains(i, x, y)]
    return pts or [tuple(nv.polyverts[i].mean(0)[:2])]


def find_inside(nv, solid, lo, hi, main_mask):
    out = {}
    cx, cy, r = (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, max(hi[0] - lo[0], hi[1] - lo[1]) / 2 + 20
    for i in nv.near(cx, cy, r):
        if nv.ptype[i] != 0 or not main_mask[i] or nv.flags[i] & navkit.DOOR:
            continue
        pts = samples(nv, i)
        ins = sum(1 for x, y in pts if solid.inside(x, y, nv.height(i, x, y) + 25) and
                  solid.inside(x, y, nv.height(i, x, y) + 50))
        if ins * 2 >= len(pts):
            out[int(i)] = ins / len(pts)
    return out


def registered_door_polys(nv, x, y, z):
    return [int(i) for i in np.nonzero(nv.flags & navkit.DOOR)[0]
            if nv.bbox[i][0] <= x + DOOR_BOX and nv.bbox[i][2] >= x - DOOR_BOX and nv.bbox[i][1] <= y + DOOR_BOX and
            nv.bbox[i][3] >= y - DOOR_BOX and nv.zmin[i] <= z + DOOR_Z and nv.zmax[i] >= z - DOOR_Z]


def door_polys_near(nv, x, y, z, half=320):
    """Every Door-flag polygon within half of the door row: what a doorway-wide registration would take (the server
    does this for GameRealmGate only, GameRealmGate.DoorwayHalfWidth 320)."""
    return [int(i) for i in np.nonzero(nv.flags & navkit.DOOR)[0]
            if np.hypot(nv.cent[i][0] - x, nv.cent[i][1] - y) <= half and abs(nv.cent[i][2] - z) <= DOOR_Z]


def door_cases(kdoors, polys):
    """(label, blocked polys) per test: every door shut, each door alone, each 01/02 style pair (same id // 100)."""
    allp = sorted(set(sum((polys[d[0]] for d in kdoors), [])))
    cases = [("all doors shut", allp)] + [(f"only {d[0]} shut", polys[d[0]]) for d in kdoors]
    if len(kdoors) > 2:
        # the server keeps every keep door shut until a raid breaks one: each door open with all the others shut
        # (Castle Myrddin 12086801, Grallarhorn 112179302: a sliver walk round the one shut gate of the opened pair)
        cases += [(f"all but {d[0]} shut", sorted(set(sum((polys[e[0]] for e in kdoors if e[0] != d[0]), []))))
                  for d in kdoors]
    pairs = {}
    for d in kdoors:
        pairs.setdefault(d[0] // 100, []).append(d[0])
    for base, ids in sorted(pairs.items()):
        if 1 < len(ids) < len(kdoors):
            cases.append((f"pair {base}xx shut", sorted(set(sum((polys[i] for i in ids), [])))))
    return cases


def main(argv):
    if len(argv) != 5:
        sys.exit(__doc__)
    zone, src, dst, dbp = int(argv[1]), argv[2], argv[3], argv[4]
    db = sqlite3.connect("file:" + dbp.replace(" ", "%20") + "?mode=ro", uri=True)
    region, oy, ox, zw, zh = db.execute("select RegionID, OffsetY, OffsetX, Width, Height from Zones where ZoneID=?",
                                        (zone,)).fetchone()
    offset = (ox * 8192, oy * 8192)
    zx0, zy0, zx1, zy1 = offset[0], offset[1], offset[0] + zw * 8192, offset[1] + zh * 8192
    print(f"zone {zone} region {region} offset {offset}")
    print(f"in  {src} md5 {md5(src)}")
    nv = navkit.Nav(src)
    nv.links = "union"   # find leaks on Detour's tile links plus the looser rule's; verify on Detour's own at the end
    usable0 = nv.usable()
    area = lambda ps: sum(navkit.poly_area(nv.polyverts[i]) for i in ps)

    def largest(u):
        """Component labels and the label of the biggest walkable area: the open field. With doors shut a keep's inside
        separates from it; whatever still shares it is reachable from outside."""
        c = nv.components(u)
        return c, np.argmax(np.bincount(c[u]))

    comp0, big0 = largest(usable0)
    main0 = usable0 & (comp0 == big0)
    print(f"polys {nv.n}, main area {int(main0.sum())}, off-mesh links {len(nv.offmesh)}")
    # realm gates (border keeps, milegates: Realm 1-3) register doorway-wide; UT_GateNavmeshInstalled165 checks them
    doors = db.execute("select InternalID, X, Y, Z from Door where InternalID/1000000=? and Realm not in (1, 2, 3)",
                       (zone,)).fetchall()
    keeps = db.execute("select KeepID, Name, X, Y, Z from Keep where Region=?", (region,)).fetchall()
    guards = db.execute("select Mob_ID, Name, X, Y, Z from Mob where Region=? and ClassType like 'DOL.GS.Keeps.%'",
                        (region,)).fetchall()
    inzone = lambda x, y: zx0 <= x < zx1 and zy0 <= y < zy1
    # every other point that must keep its open-door connectivity: all Mob rows (merchants, quest NPCs, monsters),
    # zone point and teleport arrivals, bind points, and the battleground arrivals
    others = {f"mob {r[0]} {r[1]}": (r[2], r[3], r[4]) for r in
              db.execute("select Mob_ID, Name, X, Y, Z from Mob where Region=?", (region,)) if inzone(r[2], r[3])}
    others.update({f"zonepoint {r[0]}": (r[1], r[2], r[3]) for r in
                   db.execute("select Id, TargetX, TargetY, TargetZ from ZonePoint where TargetRegion=?", (region,))
                   if inzone(r[1], r[2])})
    others.update({f"teleport {r[0]}": (r[1], r[2], r[3]) for r in
                   db.execute("select TeleportID, X, Y, Z from Teleport where RegionID=?", (region,)) if inzone(r[1], r[2])})
    others.update({f"bind {r[0]}": (r[1], r[2], r[3]) for r in
                   db.execute("select BindPoint_ID, X, Y, Z from BindPoint where Region=?", (region,)) if inzone(r[1], r[2])})
    if 250 <= region <= 253:
        others.update(ARRIVALS)

    def reach(blocked, pts):
        u = nv.usable(blocked)
        c, big = largest(u)
        out = set()
        for k, v in pts.items():
            p = nv.locate(v, usable=u)
            if p is not None and c[p] == big:
                out.add(k)
        return out

    def main_after(blocked):
        u = nv.usable(blocked)
        c, big = largest(u)
        return u & (c == big)

    D, rows, ok = [], [], True
    for model, fx in keepsolid.keep_fixtures(zone):
        tris = keepsolid.load_collision(zone, model, fx, offset)
        if tris is None:
            print()
            print(f"== {model} fixture {fx.get('id')}: no collision in the client model, left alone")
            continue
        lo, hi = tris.reshape(-1, 3).min(0), tris.reshape(-1, 3).max(0)
        inb = lambda x, y: lo[0] - 100 <= x <= hi[0] + 100 and lo[1] - 100 <= y <= hi[1] + 100
        kdoors = [d for d in doors if inb(d[1], d[2])]
        kpts = {f"keep {k[0]} {k[1]} centre": (k[2], k[3], k[4]) for k in keeps if inb(k[2], k[3])}
        kpts.update({f"{g[0]} {g[1]}": (g[2], g[3], g[4]) for g in guards if inb(g[2], g[3])})
        kname = next((k for k in kpts if k.startswith("keep ")), f"{model} fixture {fx.get('id')}")
        print()
        print(f"== {model} fixture {fx.get('id')} ({kname}) pos {fx['pos']} yaw {fx['yaw']}: "
              f"bbox {lo[:2].round(0)}-{hi[:2].round(0)}, doors {[d[0] for d in kdoors]}")
        if not kdoors:
            print("   no doors: left alone")
            continue
        found = find_inside(nv, keepsolid.Solid(tris), lo, hi, main0)
        reg = {d[0]: registered_door_polys(nv, d[1], d[2], d[3]) for d in kdoors}
        # half the gap to the nearest other door at most (Thidranki's two gates stand 289 apart)
        gap = {d[0]: min([np.hypot(d[1] - e[1], d[2] - e[2]) / 2 for e in kdoors if e[0] != d[0]] + [320]) for d in kdoors}
        wide = {d[0]: door_polys_near(nv, d[1], d[2], d[3], gap[d[0]]) for d in kdoors}
        for d in kdoors:
            miss = sorted(set(wide[d[0]]) - set(reg[d[0]]))
            print(f"   door {d[0]}: registered polys {reg[d[0]]}" + (f"; Door-flag polys in its doorway NOT registered {miss}"
                                                                 if miss else ""))
        cand = sorted(found)
        print(f"   inside-solid main-area polys: {len(cand)}")
        for i in cand:
            c = nv.cent[i]
            print(f"      {i}: centre {c[0]:.0f},{c[1]:.0f},{c[2]:.0f} inside {found[i]:.2f} flags {nv.flags[i]}")
        open0 = reach([], kpts)

        def stranded_of(extra):
            """Main-area polygons (besides extra) that lose the field when extra is disabled."""
            return sorted(set(np.nonzero(main0 & ~main_after(extra))[0].tolist()) - set(extra)) if extra else []

        def trial(extra, base=()):
            """(stranded polys, open-door reach unchanged, {case: (with base disabled, with extra disabled)})."""
            results = {}
            for label, polys in (("registered", reg), ("doorway-wide", wide)):
                for name, bl in door_cases(kdoors, polys):
                    results[(label, name)] = (reach(list(base) + bl, kpts), reach(list(extra) + bl, kpts))
            return stranded_of(extra), reach(list(extra), kpts) == open0, results

        def sealed_by(extra):
            return {k: v[1] for k, v in trial(extra)[2].items()}

        def decide(what, extra, base=()):
            """Accept extra when it closes a leak, keeps every open-door walk of the keep's points, and cuts off at most
            a small dead-end patch (whose only way to the field ran through the disabled polygons; it is disabled too,
            so no floor snap can land a bot on an island)."""
            if not extra:
                return False, list(extra)
            st, same_open, results = trial(extra, base)
            fixes = any(b1 < b0 for b0, b1 in results.values())
            if fixes and area(st) > STRAND_AREA_MAX:
                # Keep only the polygons the seal needs: put each back unless that reopens a case.
                target = sealed_by(extra)
                keep = list(extra)
                for i in sorted(set(extra) - set(base), key=lambda i: -nv.cent[i][2]):
                    trial_set = [j for j in keep if j != i]
                    if sealed_by(trial_set) == target:
                        keep = trial_set
                print(f"   {what}: {len(set(extra) - set(base))} found, {len(set(keep) - set(base))} needed for the seal "
                      f"(all of them would cut off {len(st)} polygons, {area(st):.0f} sq units)")
                extra = keep
                st, same_open, results = trial(extra, base)
                fixes = any(b1 < b0 for b0, b1 in results.values())
            accept = fixes and same_open and area(st) <= STRAND_AREA_MAX
            print(f"   {what} disabled: dead-end patch {len(st)} polys {area(st):.0f} sq units {st}, open-door reach unchanged "
                  f"{same_open}, closes a leak {fixes} -> {'DISABLE' if accept else 'leave'}")
            return accept, sorted(set(extra) | set(st))

        # 1. walkable polygons inside the keep's solid (Caer Caledon: under the gatehouse floor plate)
        acc1, set1 = decide("inside-solid polys", cand)
        chosen = list(set1) if acc1 else []
        # 2. Door-flag slivers at the side of a doorway that the server's door box does not register (the Hfrontkeep
        #    gates): shut, the door's registered polygons stand but a bot walks round them on the sliver. Disabled, the
        #    open doorway still passes through the registered polygons (open-door reach unchanged).
        slivers = sorted(set(sum(wide.values(), [])) - set(sum(reg.values(), [])))
        acc2, set2 = decide(f"unregistered door slivers {slivers}", chosen + slivers, chosen) if slivers else (False, [])
        if acc2:
            chosen = list(set2)
        _, _, final = trial(chosen)
        for (label, name), (b0, b1) in final.items():
            print(f"   [{label}] {name}: reachable from the field {sorted(b1)}" +
                  (f"  (before: {len(b0)} points)" if b0 != b1 else ""))
        # A leak: a shut-door case (registered polys) reaches a keep point that the ideal seal of the same case holds
        # (every inside-solid polygon, every sliver and the doorway-wide door polygons out). A point the ideal seal
        # still reaches stands outside, or behind another open gate of the keep (relic keeps).
        ideal = sorted(set(cand) | set(slivers) | set(chosen))
        leaks = []
        wide_cases = dict(door_cases(kdoors, wide))
        for name, bl in door_cases(kdoors, reg):
            got = final[("registered", name)][1]
            held = reach(ideal + wide_cases[name], kpts)
            if got - held:
                leaks.append(f"{name}: {len(got - held)} points")
        inside = set(kpts) - reach(ideal + wide_cases["all doors shut"], kpts)
        print(f"   inside points (held by the ideal seal with every door shut): {len(inside)} of {len(kpts)}; "
              f"leaks left: {leaks or 'none'}")
        rows.append((kname, [d[0] for d in kdoors], chosen, leaks, (kdoors, reg, wide, kpts, cand, slivers)))
        D += chosen

    # Verification on the links Detour itself makes at load (navkit._overlap_slabs).
    nv.links = "detour"
    D = sorted(set(D))
    u0 = nv.usable()
    c0, b0 = largest(u0)
    main0 = u0 & (c0 == b0)
    dead = sorted(set(np.nonzero(main0 & ~main_after(D))[0].tolist()) - set(D)) if D else []
    if dead and area(dead) <= STRAND_AREA_MAX:
        print(f"dead-end patch on Detour's links: {len(dead)} polys {area(dead):.0f} sq units {dead}: disabled too")
        D = sorted(set(D) | set(dead))
    main1 = main_after(D)
    notD = np.ones(nv.n, bool)
    notD[D] = False
    stranded = int((main0 & ~main1 & notD).sum())
    o0, o1 = reach([], others), reach(D, others)
    changed = sorted(o0 ^ o1)
    ok = stranded == 0 and not changed
    print()
    print(f"VERIFY (Detour links) TOTAL disabled {len(D)}: {D}")
    print(f"main area {int(main0.sum())} -> {int(main1.sum())}; stranded besides them {stranded}; "
          f"{len(others)} other points (mobs, zone points, teleports, binds) on the field before {len(o0)} after {len(o1)}"
          + (f"; CHANGED {changed[:10]}" if changed else ""))
    for kname, ids, chosen, leaks, (kdoors, reg, wide, kpts, cand, slivers) in rows:
        wide_cases = dict(door_cases(kdoors, wide))
        ideal = sorted(set(cand) | set(slivers) | set(D))
        before, after = [], []
        for name, bl in door_cases(kdoors, reg):
            held = reach(ideal + wide_cases[name], kpts)
            got0, got1 = reach(bl, kpts) - held, reach(D + bl, kpts) - held
            if got0:
                before.append(f"{name}: {len(got0)}")
            if got1:
                after.append(f"{name}: {len(got1)}")
        open_same = reach([], kpts) == reach(D, kpts)
        ok &= not after and open_same
        print(f"ROW zone {zone} | {kname} | doors {ids} | disabled {len(chosen)} {chosen} | leaks before (Detour links): "
              f"{before or 'none'} | after: {after or 'none'} | open-door reach unchanged {open_same}")
    if dst != "-":
        if not D:
            print("nothing to change: no file written")
        else:
            changes = {int(nv.flag_off[i]): (int(nv.flags[i]), navkit.DISABLED) for i in D}
            navkit.write_flags(src, dst, changes)
            a, b = open(src, "rb").read(), open(dst, "rb").read()
            diff = sum(1 for x, y in zip(a, b) if x != y)
            print(f"out {dst} md5 {md5(dst)}; size {len(b)} (same size: {len(a) == len(b)}); bytes changed {diff} "
                  f"(at most {2 * len(D)}: the flag words)")
            chk = navkit.Nav(dst)
            ok &= all(chk.flags[i] == navkit.DISABLED for i in D) and int((chk.flags != nv.flags).sum()) == len(D)
            print("re-read: flags differ on exactly the disabled polygons", ok)
    print()
    print("RESULT", "OK" if ok else "CHECK THE LINES ABOVE")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
