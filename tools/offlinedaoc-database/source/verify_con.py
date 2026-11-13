"""Independent check of the con colours (lane D-V).

    python -I source/verify_con.py [--table] [--no-js] [--summary <file.json>]

The server decides a monster's colour in GameServer/gameutils/ConLevels.cs:
  :19-69   one row per player level 0..50: ordered pairs (colour number, how many monster levels it covers,
           counted upward from monster level 0); -3 grey, -2 green, -1 blue, 0 yellow, 1 orange, 2 red
  :74-77   levels above 50 reuse the level 50 row with the grey span one longer per level
  :79-92   the pairs are laid along monster level 0, 1, 2 ...; everything past them is 3 = purple
  :95-104  GetConLevel(user, target): out-of-range levels answer 0 (yellow)
  GameObject.cs:896-899 calls it with EffectiveLevel of both sides; GameLiving.cs:647-650: grey = value <= -3.

This script does NOT carry a copy of the table: it reads the rows out of the C# file itself and replays the three
steps above, so it stays right when the server's table changes. It then checks
  1. source/tests/con_vectors.json (M-U's own vectors), every one;
  2. app/lib/concolor.js as the browser runs it (source/tests/con_eval.html in headless Edge): every colour for my
     level -1..128 against monster level -1..128, the server numbers, levels(), bands(), span() and the hunting
     presets (each preset's levels must really have the colours it names, and "low" must start at the band's
     first level).
`--table` prints the table for player level 1..50 against monster level 0..75.
Exit code 0 = all agree, 1 = a difference, 2 = the C# file could not be read as expected.
"""
import argparse, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import wgcommon as wg  # noqa: E402

CS = wg.SERVER_SRC + "/GameServer/gameutils/ConLevels.cs"
NAMES = {-3: "grey", -2: "green", -1: "blue", 0: "yellow", 1: "orange", 2: "red", 3: "purple"}
LETTER = {"grey": "g", "green": "G", "blue": "b", "yellow": "y", "orange": "o", "red": "r", "purple": "p"}
MAX_LEVEL = 128                                          # ConLevels.cs:8  (127 + 1)


class Broken(Exception):
    pass


def server_table(path=CS):
    """[user level][target level] -> the server's number, rebuilt from the C# source text."""
    if not os.path.exists(path):
        raise Broken("server file not found: " + path)
    src = open(path, encoding="utf-8", errors="replace").read()
    m = re.search(r"MAX_LEVEL\s*=\s*(\d+)\s*\+\s*(\d+)", src)
    if not m or int(m.group(1)) + int(m.group(2)) != MAX_LEVEL:
        raise Broken("MAX_LEVEL is no longer 127 + 1")
    rows = []
    for line in src.splitlines():
        mm = re.match(r"\s*/\*\s*(\d+)\s*\*/\s*new\(\)\s*\{(.*)\}\s*,?", line)
        if not mm:
            continue
        if int(mm.group(1)) != len(rows):
            raise Broken("table rows out of order at level %s" % mm.group(1))
        pairs = [(int(a), int(b)) for a, b in re.findall(r"\{\s*(-?\d+)\s*,\s*(-?\d+)\s*\}", mm.group(2))]
        if not pairs:
            raise Broken("row %s has no pairs" % mm.group(1))
        rows.append(pairs)
    if len(rows) != 51:
        raise Broken("expected rows for level 0..50, found %d" % len(rows))
    # the three code steps this replay depends on must still be there, word for word
    for needle in ("lastKnownData[-3] + j", "_conLevels[i,index] = item.Key", "_conLevels[i,index] = 3",
                   "return _conLevels[userLevel,targetLevel]"):
        if needle not in src:
            raise Broken("ConLevels.cs changed (missing: %s): re-read it and update verify_con.py" % needle)
    last = dict(rows[-1])
    if list(last) != [-3, -2, -1, 0, 1, 2]:
        raise Broken("level 50 row is not the six colours in order")
    j = 1
    while len(rows) < MAX_LEVEL:                          # :74-77
        rows.append([(-3, last[-3] + j), (-2, last[-2]), (-1, last[-1]), (0, last[0]), (1, last[1]), (2, last[2])])
        j += 1
    table = []
    for pairs in rows:                                    # :79-92
        line, index = [3] * MAX_LEVEL, 0
        for key, span in pairs:
            for _ in range(span):
                if index >= MAX_LEVEL:
                    break
                line[index] = key
                index += 1
        table.append(line)
    return table


def con_value(table, user, target):                       # :95-104
    if user < 0 or user > MAX_LEVEL - 1 or target < 0 or target > MAX_LEVEL - 1:
        return 0
    return table[user][target]


def con_color(table, user, target):
    return NAMES[con_value(table, user, target)]


def band(table, user, color):
    lv = [t for t in range(MAX_LEVEL) if NAMES[table[user][t]] == color]
    if not lv:
        return None
    assert lv == list(range(lv[0], lv[-1] + 1)), "a colour band with a hole"
    return [lv[0], lv[-1]]


def print_table(table):
    print("player level down, monster level 0..75 across  (g grey, G green, b blue, y yellow, o orange, r red, p purple)")
    print("     " + "".join(str(t // 10) if t % 10 == 0 else " " for t in range(76)))
    print("     " + "".join(str(t % 10) for t in range(76)))
    for u in range(1, 51):
        print("%3d  %s" % (u, "".join(LETTER[con_color(table, u, t)] for t in range(76))))
    print()
    print("level: grey | green | blue | yellow | orange | red | purple from")
    for u in range(1, 51):
        parts = []
        for c in ("grey", "green", "blue", "yellow", "orange", "red"):
            b = band(table, u, c)
            parts.append("-" if not b else (str(b[0]) if b[0] == b[1] else "%d-%d" % (b[0], b[1])))
        parts.append("%d+" % band(table, u, "purple")[0])
        print("%3d: %s" % (u, " | ".join(parts)))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--table", action="store_true")
    ap.add_argument("--no-js", action="store_true")
    ap.add_argument("--summary", default="")
    a = ap.parse_args()
    table = server_table()
    if a.table:
        print_table(table)
    ok, summary = True, {"cells_1_50_x_0_75": 50 * 76}
    print("server table rebuilt from ConLevels.cs: %d x %d" % (len(table), len(table[0])))

    # 1. M-U's vectors
    vec_path = os.path.join(HERE, "tests", "con_vectors.json")
    if os.path.exists(vec_path):
        vec = json.load(open(vec_path, encoding="utf-8"))["vectors"]
        bad = [(u, t, c, con_color(table, u, t)) for u, t, c in vec if con_color(table, u, t) != c]
        summary["vectors"] = len(vec)
        summary["vectors_bad"] = len(bad)
        print("%s: source/tests/con_vectors.json  %d vectors, %d differ" % ("PASS" if not bad else "FAIL", len(vec), len(bad)))
        for b in bad[:20]:
            print("   my %d, monster %d: vector says %s, server table says %s" % b)
        ok = ok and not bad
    else:
        print("note: source/tests/con_vectors.json not there (M-U has not written vectors)")

    # 2. the real con.js, run by the browser
    con_js = os.path.join(wg.ROOT, "app", "lib", "concolor.js")
    if a.no_js:
        print("skipped: the browser run of app/lib/concolor.js (--no-js)")
    elif not os.path.exists(con_js):
        print("FAIL: app/lib/concolor.js does not exist")
        ok = False
    else:
        from verify_jsrun import run_page
        js = run_page("con_eval.html")
        if js.get("error"):
            print("FAIL: con.js threw: %s" % js["error"])
            ok = False
        else:
            cells = diff = 0
            first = []
            for u in range(-1, 129):
                row = js["rows"][str(u)]
                for i, t in enumerate(range(-1, 129)):
                    cells += 1
                    want = LETTER[con_color(table, u, t)]
                    if row[i] != want:
                        diff += 1
                        if len(first) < 20:
                            first.append((u, t, row[i], want))
                vals = js["values"][str(u)]
                for t in range(128):
                    if vals[t] != con_value(table, u, t):
                        diff += 1
                        if len(first) < 20:
                            first.append((u, t, vals[t], con_value(table, u, t)))
            print("%s: app/lib/concolor.js colour and value, my level -1..128 x monster level -1..128: %d cells, %d differ"
                  % ("PASS" if not diff else "FAIL", cells, diff))
            for f in first:
                print("   my %d, monster %d: con.js %r, server %r" % f)
            ok = ok and not diff
            summary["js_cells"], summary["js_cells_bad"] = cells, diff

            bad = []
            for u in range(128):
                for c in NAMES.values():
                    got, want = js["levels"][str(u)][c], band(table, u, c)
                    if got != want:
                        bad.append("levels(%d, %s) = %r, server band %r" % (u, c, got, want))
                want_bands = [{"color": c, "min": band(table, u, c)[0], "max": band(table, u, c)[1]}
                              for c in NAMES.values() if band(table, u, c)]
                got_bands = [{"color": b.get("color"), "min": b.get("min"), "max": b.get("max")} for b in js["bands"][str(u)]]
                if got_bands != want_bands:
                    bad.append("bands(%d) = %r, server %r" % (u, got_bands, want_bands))
                for p in js["presets"]:
                    r = js["presetLevels"][str(u)][p["id"]]
                    cols = set(p["colors"])
                    full = [band(table, u, c) for c in p["colors"] if band(table, u, c)]
                    if not full:
                        if r is not None:
                            bad.append("presetLevels(%d, %s) = %r but no such colour exists" % (u, p["id"], r))
                        continue
                    lo, hi = min(b[0] for b in full), max(b[1] for b in full)
                    if r is None or r[0] != lo or r[1] > hi or r[1] < r[0]:
                        bad.append("presetLevels(%d, %s) = %r, the colours span %d..%d" % (u, p["id"], r, lo, hi))
                        continue
                    if p.get("part") != "low" and r[1] != hi:
                        bad.append("presetLevels(%d, %s) = %r stops before %d" % (u, p["id"], r, hi))
                    wrong = [t for t in range(r[0], r[1] + 1) if con_color(table, u, t) not in cols]
                    if wrong:
                        bad.append("presetLevels(%d, %s) = %r includes levels of another colour: %r" % (u, p["id"], r, wrong[:5]))
            for (args, got) in js["span"]:
                u, lo, hi = args
                lo, hi = min(lo, hi), max(lo, hi)
                want = {"low": con_color(table, u, lo), "high": con_color(table, u, hi)}
                if got.get("low") != want["low"] or got.get("high") != want["high"]:
                    bad.append("span(%r) = %r, server %r" % (args, got, want))
            print("%s: con.js levels(), bands(), span() and the %d hunting presets for my level 0..127: %d problems"
                  % ("PASS" if not bad else "FAIL", len(js["presets"]), len(bad)))
            for b in bad[:20]:
                print("   " + b)
            ok = ok and not bad
            summary["js_derived_bad"] = len(bad)
            summary["presets"] = [p["id"] for p in js["presets"]]
    if a.summary:
        json.dump(summary, open(a.summary, "w", encoding="utf-8"), indent=1)
    print("verify_con: %s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Broken as e:
        print("BROKEN ASSUMPTION: %s" % e)
        sys.exit(2)
