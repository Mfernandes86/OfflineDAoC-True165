"""Shared helpers for the OFFLINEDAOC Database builders (owned by the lead; ask before changing).

Every builder is  source/build_<domain>.py  and is run as
    python -I source/build_<domain>.py [--db <world.sqlite3.db>] [--out <data dir>]
It reads the game READ-ONLY and writes only data/<name>.js files (and, for maps, files under maps/).
"""
import argparse, json, os, sqlite3, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                      # the tool folder
# Where the game lives. Each path comes from (first match): an environment variable, source/paths.local.json (a small
# per-PC file, never shared), or the default layout: this tool at <repo>/tools/offlinedaoc-database inside a checkout of
# the OfflineDAoC-True165 repository (server C# source) plus a True 1.65 game install (OFFLINEDAOC_INSTALL: the folder
# holding runtime/ and new-client/). See README-SETUP.md.
#   OFFLINEDAOC_INSTALL     a True 1.65 install: runtime/data/opendaoc-165.sqlite3.db, runtime/server, new-client/godot
#   OFFLINEDAOC_DB          the world database to read (read only)   default: <install>/runtime/data/opendaoc-165.sqlite3.db
#   OFFLINEDAOC_SERVER_SRC  the server C# source (read only)         default: <repo>/development-source/server
#   OFFLINEDAOC_RUNTIME     runtime folder (true165 JSON, quests, navmesh)   default: <install>/runtime
#   OFFLINEDAOC_CLIENT      the new client's godot folder (maps, icons)      default: <install>/new-client/godot


def _local_paths():
    p = os.path.join(HERE, "paths.local.json")
    try:
        with open(p, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


_LOCAL = _local_paths()


def _path(key, env, default):
    return os.path.normpath(os.environ.get(env) or _LOCAL.get(key) or default).replace("\\", "/")


REPO = os.path.normpath(os.path.join(ROOT, "..", ".."))
INSTALL = _path("install", "OFFLINEDAOC_INSTALL", REPO)
DEFAULT_DB = _path("db", "OFFLINEDAOC_DB", os.path.join(INSTALL, "runtime", "data", "opendaoc-165.sqlite3.db"))
SERVER_SRC = _path("server_src", "OFFLINEDAOC_SERVER_SRC", os.path.join(REPO, "development-source", "server"))
RUNTIME = _path("runtime", "OFFLINEDAOC_RUNTIME", os.path.join(INSTALL, "runtime"))
CLIENT = _path("client", "OFFLINEDAOC_CLIENT", os.path.join(INSTALL, "new-client", "godot"))

FORBIDDEN = ("account", "dolcharacters", "dolcharactersbackup", "accountxcustomparam", "dolcharactersxcustomparam")


def args(description=""):
    p = argparse.ArgumentParser(description=description)
    p.add_argument("--db", default=DEFAULT_DB)
    p.add_argument("--out", default=os.path.join(ROOT, "data"))
    return p.parse_args()


def open_db(path=DEFAULT_DB):
    """Open the world read-only. Player tables are refused: this tool never reads accounts or characters."""
    con = sqlite3.connect("file:" + path.replace("\\", "/").replace(" ", "%20") + "?mode=ro", uri=True)
    con.row_factory = sqlite3.Row

    def guard(action, a1, a2, dbname, source):
        if action == sqlite3.SQLITE_READ and a1 and a1.lower() in FORBIDDEN:
            return sqlite3.SQLITE_DENY
        return sqlite3.SQLITE_OK
    con.set_authorizer(guard)
    return con


def table(cols, rows):
    """The compact table form the client reads with WG.rows(): {"cols": [...], "rows": [[...], ...]}."""
    return {"cols": list(cols), "rows": [list(r) for r in rows]}


def write_data(name, value, out_dir=None, note=""):
    """Write data/<name>.js = WG.provide("<name>", <json>);  Returns the size in bytes."""
    out_dir = out_dir or os.path.join(ROOT, "data")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".js")
    body = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write('WG.provide(%s,%s);\n' % (json.dumps(name), body))
    size = os.path.getsize(path)
    print("wrote data/%s.js  %.2f MB  %s" % (name, size / 1e6, note))
    return size


def stamp():
    return time.strftime("%Y-%m-%d %H:%M")


def zones(con):
    """ZoneID -> dict(id, region, name, ox, oy, w, h) with ox/oy/w/h in game units (8192 per zone tile)."""
    out = {}
    for r in con.execute("select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height, Realm from Zones"):
        out[r["ZoneID"]] = dict(id=r["ZoneID"], region=r["RegionID"], name=r["Name"], realm=r["Realm"],
                                ox=r["OffsetX"] * 8192, oy=r["OffsetY"] * 8192, w=r["Width"] * 8192, h=r["Height"] * 8192)
    return out


def zone_of(zs_by_region, region, x, y):
    """Zone containing a region position, or None. zs_by_region = {region: [zone dicts]} (see zones_by_region)."""
    for z in zs_by_region.get(region, ()):
        if z["ox"] <= x < z["ox"] + z["w"] and z["oy"] <= y < z["oy"] + z["h"]:
            return z
    return None


def zones_by_region(zs):
    out = {}
    for z in zs.values():
        out.setdefault(z["region"], []).append(z)
    return out
