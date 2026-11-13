"""Rebuild the whole OFFLINEDAOC Database from the game's data, then check it (lane D-V).

    python -I source/build_all.py                     everything: builders, checks, data/meta.js
    python -I source/build_all.py --only drops,mobs   just these builders (then the checks)
    python -I source/build_all.py --skip-build        only the checks and data/meta.js
    python -I source/build_all.py --db <world.sqlite3.db> --out <data dir>     passed through to every builder
    python -I source/build_all.py --no-browser        skip the checks that need Edge (con.js, prices in the page)
    python -I source/build_all.py --keep-going        do not stop at the first builder that fails

It reads the game READ-ONLY (see wgcommon.open_db) and writes only inside this tool's folder. Builders run one
after another, never in parallel (the game may be running on this PC).

Order: a builder may read data files made by an earlier one, so the order below matters:
    maps  -> zones.js and the map images        (everything else places things in zones)
    mobs  -> mobs.js, camps.js                  (the monsters that are really in the world, and the script-made ones)
    drops -> drops.js                           (reads mobs.js for the script-made monsters)
    vendors, routes
    items -> items.js, itemmeta.js, icons       (reads mobs.js; compares its source flags with drops.js, vendoritems.js)
    quests -> quests.js                         (last: it may point at monsters, items and zones made above)
Any other source/build_<name>.py found is run after these, in alphabetical order.

Exit code 0 = built and every check passed, 1 = a builder or a check failed (data/meta.js says which).
"""
import argparse, glob, json, os, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import wgcommon as wg  # noqa: E402

ORDER = ["maps", "mobs", "drops", "vendors", "routes", "items", "quests"]
CHECKS = [                       # (name, script, extra args, needs a browser)
    ("drops", "verify_drops.py", [], False),
    ("con", "verify_con.py", [], True),
    ("prices", "verify_prices.py", [], False),
    ("data", "verify_data.py", [], False),
]
SIZE_LIMIT = 15 * 1000 * 1000    # CONTRACT section 6: keep each data file under about 15 MB


def builders():
    found = {os.path.basename(p)[len("build_"):-3]: p for p in glob.glob(os.path.join(HERE, "build_*.py"))}
    found.pop("all", None)
    names = [n for n in ORDER if n in found] + sorted(n for n in found if n not in ORDER)
    return [(n, found[n]) for n in names]


def run(script, args, log_dir, tag):
    """Run one script with `python -I`, show its output as it comes, keep a copy in the log folder."""
    t0 = time.time()
    cmd = [sys.executable, "-I", script] + args
    log_path = os.path.join(log_dir, tag + ".log")
    tail = []
    with open(log_path, "w", encoding="utf-8", newline="\n") as log:
        p = subprocess.Popen(cmd, cwd=wg.ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                             text=True, encoding="utf-8", errors="replace")
        for line in p.stdout:
            log.write(line)
            tail.append(line.rstrip("\n"))
            if len(tail) > 400:
                tail.pop(0)
            try:
                print("   | " + line.rstrip("\n"))
            except UnicodeEncodeError:
                print("   | " + line.rstrip("\n").encode("ascii", "replace").decode("ascii"))
        p.wait()
    return {"code": p.returncode, "seconds": round(time.time() - t0, 1), "tail": tail, "log": log_path}


def read_provide(path):
    s = open(path, encoding="utf-8").read().strip()
    a = s.index(",", s.index("WG.provide(")) + 1
    return json.loads(s[a:s.rindex(")")])


def count_value(v):
    """A short description of what a data file holds: {part: count}."""
    def n(x):
        if isinstance(x, dict) and "cols" in x and "rows" in x and isinstance(x["rows"], list):
            return len(x["rows"])
        if isinstance(x, (list, dict)):
            return len(x)
        return None
    if isinstance(v, dict) and not ("cols" in v and "rows" in v):
        out = {}
        for k, x in v.items():
            c = n(x)
            if c is not None and (c > 1 or isinstance(x, list)):
                out[k] = c
        if not out or len(out) > 12:           # a plain map (for example drops by monster name)
            return {"entries": len(v)}
        return out
    return {"rows": n(v)}


def data_files(out_dir):
    files = []
    for path in sorted(glob.glob(os.path.join(out_dir, "*.js"))):
        name = os.path.basename(path)[:-3]
        if name == "meta":
            continue
        entry = {"name": name, "bytes": os.path.getsize(path),
                 "modified": time.strftime("%Y-%m-%d %H:%M", time.localtime(os.path.getmtime(path)))}
        try:
            entry["counts"] = count_value(read_provide(path))
        except Exception as e:                  # a broken file is reported by verify_data.py; keep going here
            entry["error"] = str(e)[:200]
        files.append(entry)
    return files


def folder_size(path):
    total = n = 0
    for dp, _, fn in os.walk(path):
        for f in fn:
            total += os.path.getsize(os.path.join(dp, f))
            n += 1
    return n, total


def db_info(db):
    info = {"path": db.replace("\\", "/"), "bytes": os.path.getsize(db),
            "modified": time.strftime("%Y-%m-%d %H:%M", time.localtime(os.path.getmtime(db)))}
    con = wg.open_db(db)
    try:
        for r in con.execute("select Key, Value from offline_true165_content"):
            info[{"stamp": "contentStamp", "applied": "contentApplied"}.get(r["Key"], r["Key"])] = r["Value"]
    except Exception as e:
        info["contentStamp"] = None
        info["note"] = "no offline_true165_content table: %s" % e
    info["rows"] = {}
    for t in ("Mob", "NpcTemplate", "ItemTemplate", "LootTemplate", "MobXLootTemplate", "LootOTD", "MerchantItem",
              "Zones", "Regions", "Path", "PathPoints", "Faction"):
        try:
            info["rows"][t] = con.execute("select count(*) from `%s`" % t).fetchone()[0]
        except Exception:
            info["rows"][t] = None
    con.close()
    return info


def main():
    ap = argparse.ArgumentParser(description="Rebuild every data file of the OFFLINEDAOC Database and check it.")
    ap.add_argument("--db", default=wg.DEFAULT_DB)
    ap.add_argument("--out", default=os.path.join(wg.ROOT, "data"))
    ap.add_argument("--only", default="", help="comma list of builders to run, for example drops,mobs")
    ap.add_argument("--skip-build", action="store_true")
    ap.add_argument("--no-verify", action="store_true")
    ap.add_argument("--no-browser", action="store_true")
    ap.add_argument("--keep-going", action="store_true")
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(line_buffering=True)      # show progress as it happens, also when piped to a file
    except Exception:
        pass

    if not os.path.exists(a.db):
        print("The world database is not there: %s" % a.db)
        return 1
    os.makedirs(a.out, exist_ok=True)
    log_dir = os.path.join(HERE, "scratch", "D-V", "build-logs")
    os.makedirs(log_dir, exist_ok=True)
    t_start = time.time()
    steps, ok = [], True
    pass_args = ["--db", a.db, "--out", a.out]

    todo = builders()
    if a.only:
        want = [x.strip() for x in a.only.split(",") if x.strip()]
        unknown = [w for w in want if w not in dict(todo)]
        if unknown:
            print("No such builder: %s (there are: %s)" % (", ".join(unknown), ", ".join(n for n, _ in todo)))
            return 1
        todo = [(n, p) for n, p in todo if n in want]
    if a.skip_build:
        todo = []
    for name, path in todo:
        print("== build %s  (source/%s)" % (name, os.path.basename(path)))
        r = run(path, pass_args, log_dir, "build_" + name)
        steps.append({"step": "build " + name, "ok": r["code"] == 0, "seconds": r["seconds"]})
        print("== build %s: %s in %.0f s" % (name, "ok" if r["code"] == 0 else "FAILED (exit %d)" % r["code"], r["seconds"]))
        if r["code"] != 0:
            ok = False
            if not a.keep_going:
                print("Stopped: fix source/build_%s.py (its output is in %s) or run again with --keep-going." % (name, r["log"]))
                break

    checks = {}
    if not a.no_verify and (ok or a.keep_going):
        for name, script, extra, needs_browser in CHECKS:
            path = os.path.join(HERE, script)
            if not os.path.exists(path):
                continue
            args = list(extra)
            summary = os.path.join(log_dir, "summary_%s.json" % name)
            if os.path.exists(summary):
                os.remove(summary)
            if name in ("drops", "prices", "data"):
                args += pass_args
            if needs_browser and a.no_browser:
                args += ["--no-js"]
            if name == "data" and a.no_browser:
                args += ["--no-js"]
            args += ["--summary", summary]
            print("== check %s  (source/%s)" % (name, script))
            r = run(path, args, log_dir, "verify_" + name)
            verdict = [ln for ln in r["tail"] if ln.startswith(("PASS", "FAIL", "WARN", "note", "skipped"))]
            checks[name] = {"ok": r["code"] == 0, "seconds": r["seconds"], "lines": verdict[-60:]}
            if os.path.exists(summary):
                try:
                    checks[name]["summary"] = json.load(open(summary, encoding="utf-8"))
                except Exception:
                    pass
            steps.append({"step": "check " + name, "ok": r["code"] == 0, "seconds": r["seconds"]})
            print("== check %s: %s in %.0f s" % (name, "PASS" if r["code"] == 0 else "FAIL", r["seconds"]))
            ok = ok and r["code"] == 0

    files = data_files(a.out)
    for f in files:
        if f["bytes"] > SIZE_LIMIT:
            print("WARNING: data/%s.js is %.1f MB (limit about 15 MB)" % (f["name"], f["bytes"] / 1e6))
    maps_n, maps_bytes = folder_size(os.path.join(wg.ROOT, "maps"))
    icons_n, icons_bytes = folder_size(os.path.join(wg.ROOT, "icons"))
    meta = {
        "built": wg.stamp(),
        "ok": ok,
        "partial": bool(a.only or a.skip_build),
        "seconds": round(time.time() - t_start),
        "db": db_info(a.db),
        "serverSource": wg.SERVER_SRC,
        "files": files,
        "images": {"maps": {"files": maps_n, "bytes": maps_bytes}, "icons": {"files": icons_n, "bytes": icons_bytes}},
        "steps": steps,
        "checks": checks,
    }
    wg.write_data("meta", meta, a.out, note="(%d data files, %s)" % (len(files), "all checks passed" if ok else "WITH FAILURES"))
    print()
    print("%-22s %10s  %s" % ("data file", "size", "holds"))
    for f in files:
        print("%-22s %8.2f MB  %s" % (f["name"] + ".js", f["bytes"] / 1e6,
                                      f.get("error") or ", ".join("%s %s" % (v, k) for k, v in f.get("counts", {}).items())))
    print()
    for s in steps:
        print("%-16s %-6s %6.0f s" % (s["step"], "ok" if s["ok"] else "FAILED", s["seconds"]))
    print()
    print("OFFLINEDAOC Database: %s (%.0f s). Open index.html, or the .bat next to it."
          % ("built and checked" if ok else "NOT clean, see above", time.time() - t_start))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
