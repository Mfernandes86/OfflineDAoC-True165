"""Run keepgate.py on every keep zone of a navmesh folder: the step to run after ANY navmesh rebuild or splice (BuildNav
output, navmesh_splice, a new nav164-style keep rebuild) and before navmesh_splice/roadgraph.py, so a rebuilt map never
brings a keep gate leak back (2026-11-12: Caer Caledon, Caer Berkstead, the Albion portal keeps, Hfrontkeep slivers).

    python seal_all.py <navmesh folder> <out folder> <world.sqlite3.db> [zone ...]

Zones: given, or every zoneNNN.nav in the folder whose client zone has a keep model with doors in the world. A zone that
needs nothing gets no output file; copy the written files over the folder's own (server stopped). Each zone's full
keepgate log goes to <out folder>/logs. Exit code 1 when any zone's verification fails.
"""
import glob, os, re, sqlite3, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import keepsolid


def keep_zones(navdir, dbp):
    db = sqlite3.connect("file:" + dbp.replace(" ", "%20") + "?mode=ro", uri=True)
    door_zones = {r[0] for r in db.execute("select distinct InternalID/1000000 from Door")}
    out = []
    for p in sorted(glob.glob(os.path.join(navdir, "zone*.nav"))):
        z = int(re.findall(r"zone(\d+)\.nav$", p)[0])
        if z not in door_zones or not os.path.exists(os.path.join(keepsolid.GODOT, "zone%03d" % z, "zone.json")):
            continue
        if keepsolid.keep_fixtures(z):
            out.append(z)
    return out


def main(argv):
    if len(argv) < 4:
        sys.exit(__doc__)
    navdir, outdir, dbp = argv[1], argv[2], argv[3]
    zones = [int(z) for z in argv[4:]] or keep_zones(navdir, dbp)
    os.makedirs(os.path.join(outdir, "logs"), exist_ok=True)
    bad = []
    for z in zones:
        log = os.path.join(outdir, "logs", "keepgate-zone%03d.txt" % z)
        with open(log, "w", encoding="utf-8") as f:
            rc = subprocess.call([sys.executable, "-B", os.path.join(HERE, "keepgate.py"), str(z),
                                  os.path.join(navdir, "zone%03d.nav" % z), os.path.join(outdir, "zone%03d.nav" % z), dbp],
                                 stdout=f, stderr=subprocess.STDOUT)
        text = open(log, encoding="utf-8").read()
        total = re.search(r"VERIFY \(Detour links\) TOTAL disabled (\d+)", text)
        print(f"zone {z:3d}: {'OK ' if rc == 0 else 'BAD'} disabled {total.group(1) if total else '?'}"
              + ("" if "nothing to change" in text or not total or total.group(1) == "0" else f" -> zone{z:03d}.nav"))
        if rc != 0:
            bad.append(z)
    print("RESULT", "OK" if not bad else f"CHECK zones {bad}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
