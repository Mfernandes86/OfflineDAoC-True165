"""Cross-file integrity of everything in data/ (lane D-V).

    python -I source/verify_data.py [--db <world>] [--out <data dir>] [--summary <file.json>] [--no-js]

Prints one PASS / FAIL line per check (WARN = worth a look, does not fail) and exits non-zero when any check fails.
The data files are compared with each other and with the world database, through the verifier's own reading of the
server (source/verify_world.py), not through the builders.

  F  files      every data file is there, is  WG.provide("<its name>", ...) , under 15 MB, no placeholder text
  Z  zones      zone rows against the Zones table, no closed region, every picture file exists
  I  items      ids exist and are unique, the plain columns equal the ItemTemplate row, source bits agree with
                drops.js and vendoritems.js
  D  drops      item ids exist, monster keys match a kind in mobs.js or are counted, chances are 0..1
  K  kinds      monster kinds against the spawns as the server runs them: name, level range, count
  C  camps      camp -> kind -> zone links, positions inside the zone, points are real spawn positions, counts
  V  vendors    zone, position, list and item links
  R  routes     stable and path links, positions
  A  app        every script in app/manifest.js exists; every data file a page asks for exists
"""
import argparse, glob, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import wgcommon as wg  # noqa: E402
import verify_world as vw  # noqa: E402

SIZE_LIMIT = 15 * 1000 * 1000
EXPECTED = ["zones", "items", "itemmeta", "mobs", "camps", "drops", "vendors", "vendoritems", "routes", "quests"]
PLACEHOLDER = re.compile(r"\b(lorem|ipsum|placeholder|todo|tbd|fixme|foobar|sample row|example row|xxx+)\b", re.I)
EDGE = 64            # game units a pin may sit past its zone's edge (rounding)


class Report:
    def __init__(self):
        self.ok, self.lines, self.summary, self.warn = True, [], {}, 0

    def check(self, code, good, text, details=(), warn=False):
        if not good and warn:
            self.warn += 1
            tag = "WARN"
        else:
            tag = "PASS" if good else "FAIL"
            self.ok = self.ok and bool(good)
        self.lines.append("%s: %s %s" % (tag, code, text))
        print(self.lines[-1])
        for d in list(details)[:12]:
            print("      " + str(d)[:300])

    def note(self, text):
        self.lines.append("note: " + text)
        print(self.lines[-1])


def read_provide(path):
    s = open(path, encoding="utf-8").read().strip()
    m = re.match(r'WG\.provide\(\s*"([^"]+)"\s*,', s)
    if not m or not s.endswith(");"):
        raise ValueError("not of the form WG.provide(\"name\", ...);")
    return m.group(1), json.loads(s[m.end():-2])


def rows_of(t):
    if isinstance(t, dict) and "cols" in t and "rows" in t:
        cols = t["cols"]
        return [dict(zip(cols, r)) for r in t["rows"]]
    return list(t or [])


NL = chr(10)


def strip_js_comments(src):
    """JavaScript or HTML source with // and /* */ comments blanked (line numbers kept, strings left alone)."""
    out, i, n, quote = [], 0, len(src), None
    while i < n:
        ch = src[i]
        if quote:
            out.append(ch)
            if ch == "\\" and i + 1 < n:
                out.append(src[i + 1])
                i += 2
                continue
            if ch == quote or (ch == NL and quote != "`"):
                quote = None
            i += 1
        elif ch in "\"'`":
            quote = ch
            out.append(ch)
            i += 1
        elif src.startswith("//", i) and (i == 0 or src[i - 1] != ":"):
            while i < n and src[i] != NL:
                i += 1
        elif src.startswith("/*", i):
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(NL * src.count(NL, i, j))
            i = j
        else:
            out.append(ch)
            i += 1
    return "".join(out)


def walk_strings(v, path=""):
    if isinstance(v, str):
        yield path, v
    elif isinstance(v, list):
        for i, x in enumerate(v):
            yield from walk_strings(x, path)
    elif isinstance(v, dict):
        for k, x in v.items():
            if isinstance(k, str):
                yield path + "/" + k[:40], k
            yield from walk_strings(x, path + "/" + str(k)[:40] if len(path) < 80 else path)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--db", default=wg.DEFAULT_DB)
    ap.add_argument("--out", default=os.path.join(wg.ROOT, "data"))
    ap.add_argument("--summary", default="")
    ap.add_argument("--no-js", action="store_true")
    a = ap.parse_args()
    rep = Report()
    con = wg.open_db(a.db)

    # ------------------------------------------------------------------ F files
    data, sizes, bad = {}, {}, []
    for name in EXPECTED:
        path = os.path.join(a.out, name + ".js")
        if not os.path.exists(path):
            bad.append("data/%s.js is missing" % name)
            continue
        sizes[name] = os.path.getsize(path)
        try:
            got, value = read_provide(path)
            if got != name:
                bad.append("data/%s.js provides %r" % (name, got))
            data[name] = value
        except Exception as e:
            bad.append("data/%s.js cannot be read: %s" % (name, e))
    others = [os.path.basename(p)[:-3] for p in glob.glob(os.path.join(a.out, "*.js"))
              if os.path.basename(p)[:-3] not in EXPECTED + ["meta"]]
    for name in others:
        path = os.path.join(a.out, name + ".js")
        sizes[name] = os.path.getsize(path)
        try:
            got, value = read_provide(path)
            data[name] = value
            if got != name:
                bad.append("data/%s.js provides %r" % (name, got))
        except Exception as e:
            bad.append("data/%s.js cannot be read: %s" % (name, e))
    rep.check("F1", not bad, "%d data files read (%s)%s" % (len(data), ", ".join(sorted(data)),
              "" if not others else "; beyond the contract's list: " + ", ".join(others)), bad)
    big = ["data/%s.js is %.1f MB" % (n, s / 1e6) for n, s in sizes.items() if s > SIZE_LIMIT]
    rep.check("F2", not big, "every data file is under 15 MB (largest: %s, %.1f MB; total %.1f MB)"
              % (max(sizes, key=sizes.get) if sizes else "-", max(sizes.values()) / 1e6 if sizes else 0, sum(sizes.values()) / 1e6), big)
    game_text = set()
    for t, c in (("ItemTemplate", "Name"), ("Mob", "Name"), ("NpcTemplate", "Name"), ("Zones", "Name"), ("DataQuest", "Name")):
        for r in con.execute("select distinct `%s` from `%s`" % (c, t)):
            if r[0] and PLACEHOLDER.search(r[0]):
                game_text.add(r[0].lower())
    hits = []
    for name, value in data.items():
        for path, s in walk_strings(value, name):
            if len(s) < 400 and PLACEHOLDER.search(s) and s.lower() not in game_text:
                hits.append("%s: %r" % (path, s[:120]))
                if len(hits) > 200:
                    break
    rep.check("F3", not hits, "no placeholder or sample text in the data (%d suspicious strings; %d game names that only "
              "look like it are allowed)" % (len(hits), len(game_text)), hits)

    # world model
    sps = vw.spawns(con)
    closed = vw.closed_regions(con)
    zs_db = wg.zones(con)
    zbr = wg.zones_by_region(zs_db)
    names_world = {}
    for s in sps:
        for v in s["variants"]:
            names_world.setdefault(v["name"].lower(), []).append((s, v))
    items_db = {r["Id_nb"].lower(): r for r in con.execute(
        "select Id_nb, Name, Level, Realm, Price, Object_Type, Item_Type, DPS_AF, SPD_ABS, Quality, Bonus, Hand, "
        "MaxCount, PackSize, Model, Weight, LevelRequirement from ItemTemplate")}

    service_classes = {c for _, _, c in vw.classes()[1]}

    def huntable(s):
        """A spawn a player can hunt: no realm, stands in a zone, not peaceful (0x10), can be targeted (0x08 off), not a
        training dummy (cannot die), guard, keep guard or service NPC (the class is the effective one, Region.cs:745-768).
        GameNPC.eFlags; the same lines mobs.js draws (its meta.excluded lists the rest)."""
        if not (s["realm"] == 0 and vw.place(zbr, s) is not None and not (s["flags"] & 0x10) and not (s["flags"] & 0x08)):
            return False
        c = s["eff_cls"]
        if c.split(".")[-1] in service_classes:
            return False                           # an NPC run by an NPC guild script: merchant, trainer, stable master ...
        return not any(vw.is_a(c, base) for base in ("GameTrainingDummy", "GameKeepGuard", "GameGuard", "GameMerchant", "GameTrainer"))

    # ------------------------------------------------------------------ Z zones
    zones = {}
    if "zones" in data:
        zl = data["zones"]["zones"]
        zones = {z["id"]: z for z in zl}
        bad = []
        for z in zl:
            d = zs_db.get(z["id"])
            if d is None:
                bad.append("zone %s is not in the Zones table" % z["id"])
                continue
            if d["region"] in closed:
                bad.append("zone %s (%s) is in closed region %s" % (z["id"], z["name"], d["region"]))
            for k_file, k_db in (("region", "region"), ("ox", "ox"), ("oy", "oy"), ("w", "w"), ("h", "h"), ("name", "name")):
                if k_file in z and z[k_file] != d[k_db]:
                    bad.append("zone %s: %s is %r, the Zones table says %r" % (z["id"], k_file, z[k_file], d[k_db]))
        open_with_spawns = {}
        for s in sps:
            pl = vw.place(zbr, s)
            if pl:
                open_with_spawns[pl[0]] = open_with_spawns.get(pl[0], 0) + 1
        missing = ["zone %s (%s): %d spawns stand in it" % (zid, zs_db[zid]["name"], n)
                   for zid, n in sorted(open_with_spawns.items()) if zid not in zones]
        rep.check("Z1", not bad and not missing, "%d zones: all in the Zones table with the same region, corner and size, none in a "
                  "closed region; open zones with spawns but not in the file: %d" % (len(zl), len(missing)), bad + missing)
        bad, n_pics = [], 0

        def pic(src, what):
            nonlocal n_pics
            if not src:
                return
            n_pics += 1
            if not os.path.exists(os.path.join(wg.ROOT, src)):
                bad.append("%s: %s does not exist" % (what, src))
        for z in zl:
            pic(z.get("map"), "zone %s map" % z["id"])
            pic(z.get("thumb"), "zone %s thumb" % z["id"])
            for layer in z.get("layers") or []:
                pic(layer.get("src"), "zone %s layer %s" % (z["id"], layer.get("id")))
                px, rect = layer.get("px"), layer.get("rect")
                if not (px and rect and px[0] > 0 and px[1] > 0 and rect[2] > 0 and rect[3] > 0):
                    bad.append("zone %s layer %s: px %r rect %r" % (z["id"], layer.get("id"), px, rect))
            for fl in z.get("floors") or []:
                pic(fl.get("src"), "zone %s floor %s" % (z["id"], fl.get("name")))
        for o in data["zones"].get("overviews") or []:
            pic(o.get("src"), "overview %s" % o.get("id"))
            for oz in o.get("zones") or []:
                if oz["id"] not in zones:
                    bad.append("overview %s names zone %s, not in the file" % (o.get("id"), oz["id"]))
        for r in data["zones"].get("regions") or []:
            for zid in r.get("zones") or []:
                if zid not in zones:
                    bad.append("region %s names zone %s, not in the file" % (r.get("id"), zid))
        nomap = sum(1 for z in zl if not z.get("map"))
        rep.check("Z2", not bad, "%d picture files named by zones.js all exist; %d zones have no picture" % (n_pics, nomap), bad)

    def in_zone(zid, x, y, margin=EDGE):
        z = zones.get(zid)
        if z is None:
            return False
        return -margin <= x < z["w"] + margin and -margin <= y < z["h"] + margin

    # ------------------------------------------------------------------ I items
    item_ids = set()
    items = []
    if "items" in data:
        items = rows_of(data["items"])
        bad, seen = [], set()
        cols = {"level": "Level", "realm": "Realm", "price": "Price", "obj": "Object_Type", "slot": "Item_Type",
                "quality": "Quality", "bonus": "Bonus", "hand": "Hand", "stack": "MaxCount", "pack": "PackSize",
                "model": "Model", "weight": "Weight", "levelReq": "LevelRequirement", "name": "Name"}
        present = [c for c in cols if items and c in items[0]]
        cells = 0
        for it in items:
            iid = str(it["id"])
            d = items_db.get(iid.lower())
            if d is None:
                bad.append("item %s is not in ItemTemplate" % iid)
                continue
            if iid.lower() in seen:
                bad.append("item %s is twice in items.js" % iid)
            seen.add(iid.lower())
            if d["Id_nb"] != iid:
                bad.append("item id %r is spelled %r in ItemTemplate" % (iid, d["Id_nb"]))
            for c in present:
                cells += 1
                want = d[cols[c]]
                if c != "name":
                    want = int(want or 0)
                if c in ("stack", "pack"):
                    want = max(1, want)            # items.md: a stored 0 means "one", as the server treats it
                if it[c] != want and not (c == "name" and (it[c] or "") == (want or "")):
                    bad.append("item %s: %s is %r, ItemTemplate.%s is %r" % (iid, c, it[c], cols[c], want))
        item_ids = seen
        rep.check("I1", not bad, "%d items: every id is an ItemTemplate row, none twice; %d plain cells (%s) equal the row; %d wrong"
                  % (len(items), cells, ", ".join(present), len(bad)), bad)

    # ------------------------------------------------------------------ K kinds
    kinds, kind_by_id, scripted_kinds = {}, {}, {}
    if "mobs" in data:
        kl = rows_of(data["mobs"]["kinds"])
        bad = []
        for k in kl:
            if k["key"] in kinds:
                bad.append("kind key %r twice" % k["key"])
            if k["id"] in kind_by_id:
                bad.append("kind id %r twice" % k["id"])
            if k["key"] != str(k["name"]).lower():
                bad.append("kind %r: key is not the lower-case name %r" % (k["key"], k["name"]))
            kinds[k["key"]] = k
            kind_by_id[k["id"]] = k
        rep.check("K1", not bad, "%d monster kinds: key = lower-case name, keys and ids unique" % len(kl), bad)
        bad, wide, narrow, not_world = [], [], [], []
        exact = 0
        for key, k in kinds.items():
            w = names_world.get(key)
            if not w:
                not_world.append("%s: no spawn in an open region has this name at run time" % key)
                continue
            lv_all = set()
            for s, v in w:
                lv_all |= v["levels"]
            lv_mon = set()
            for s, v in w:
                if huntable(s):
                    lv_mon |= v["levels"]
            lo, hi = k["lmin"], k["lmax"]
            if lv_mon and (lo, hi) == (min(lv_mon), max(lv_mon)):
                exact += 1
            elif (lo, hi) == (min(lv_all), max(lv_all)):
                exact += 1
            elif lo < min(lv_all) or hi > max(lv_all):
                wide.append("%s: file %s to %s, the spawns can only be %s to %s" % (key, lo, hi, min(lv_all), max(lv_all)))
            else:
                narrow.append("%s: file %s to %s, the huntable spawns of that name span %s to %s"
                              % (key, lo, hi, min(lv_mon) if lv_mon else "-", max(lv_mon) if lv_mon else "-"))
        rep.check("K2", not not_world and not wide, "level range of each kind against Mob.Level and the template's Level list "
                  "(GameNPC.cs:1360-1390): %d equal, %d reach outside what the spawns can be, %d narrower (see K3), "
                  "%d names with no spawn" % (exact, len(wide), len(narrow), len(not_world)), not_world + wide)
        rep.check("K3", not narrow, "kinds whose range is not the range of their huntable spawns (no realm, in a zone, not "
                  "peaceful, can be targeted): %d" % len(narrow), narrow)
        bad = []
        for zr in rows_of(data["mobs"].get("zones")):
            if zones and zr.get("zone") not in zones:
                bad.append("mobs.js zone summary names zone %s, not in zones.js" % zr.get("zone"))
            for kk in zr.get("kindList") or []:
                if kk[0] not in kind_by_id:
                    bad.append("mobs.js zone %s lists kind id %s, not a kind" % (zr.get("zone"), kk[0]))
        for pr in rows_of(data["mobs"].get("paths")):
            if pr.get("kind") not in kind_by_id or (zones and pr.get("zone") not in zones):
                bad.append("mobs.js patrol path: kind %s / zone %s unknown" % (pr.get("kind"), pr.get("zone")))
        # levels a monster's own script sets (they overrule the table: GameNPC.cs:3270-3284)
        theirs_sl = (data["mobs"].get("meta") or {}).get("scriptLevels")
        if theirs_sl is not None:
            mine_sl = vw.script_levels()
            used = {s["eff_cls"].split(".")[-1] for s in sps if s["script_level"] and huntable(s)}
            bad_sl = ["%s: mobs.js says %s, the class's AddToWorld says %s" % (c, r, list(mine_sl[c]) if c in mine_sl else None)
                      for c, r in sorted(theirs_sl.items()) if c not in mine_sl or list(mine_sl[c]) != list(r)]
            bad_sl += ["%s: a huntable spawn runs as this class and its AddToWorld sets level %s to %s, not in mobs.meta.scriptLevels"
                       % (c, mine_sl[c][0], mine_sl[c][1]) for c in sorted(used - set(theirs_sl))]
            rep.check("K6", not bad_sl, "levels set by a monster's own script (Level = ... in AddToWorld): %d classes in mobs.js, each equal "
                      "to my reading of the C#, none missing; %d wrong" % (len(theirs_sl), len(bad_sl)), bad_sl)
        # the script-made kinds listed apart (no Mob row, no camps)
        scripted_rows = rows_of(data["mobs"].get("scripted"))
        if scripted_rows:
            bad_sc, n_where, no_level = [], 0, 0
            seen_ids = set(kind_by_id)
            for sk in scripted_rows:
                if sk["id"] in seen_ids:
                    bad_sc.append("scripted kind id %s is used twice" % sk["id"])
                seen_ids.add(sk["id"])
                if sk["key"] != str(sk["name"]).lower():
                    bad_sc.append("scripted kind %r: key is not the lower-case name" % sk["key"])
                if sk["key"] in kinds:
                    bad_sc.append("scripted kind %r is also a world kind" % sk["key"])
                if sk["key"] in scripted_kinds:
                    bad_sc.append("scripted kind %r twice" % sk["key"])
                scripted_kinds[sk["key"]] = sk
                if sk["lmin"] is None or sk["lmax"] is None:
                    no_level += 1                      # the script gives no level the builder could read: said, not guessed
                elif not (0 <= sk["lmin"] <= sk["lmax"] <= 255):
                    bad_sc.append("scripted kind %r: levels %s to %s" % (sk["key"], sk["lmin"], sk["lmax"]))
                for w in sk.get("where") or []:
                    n_where += 1
                    if zones and (w[0] not in zones or not in_zone(w[0], w[1], w[2])):
                        bad_sc.append("scripted kind %r: place zone %s %s,%s is not inside a zone of zones.js" % (sk["key"], w[0], w[1], w[2]))
                for by in sk.get("by") or []:
                    if by not in kind_by_id and by not in seen_ids:
                        bad_sc.append("scripted kind %r: summoner id %s is not a kind" % (sk["key"], by))
            rep.check("S1", not bad_sc, "%d script-made kinds: ids and keys unique, not doubling a world kind, %d places inside their zones, "
                      "summoners are kinds, %d with no level known; %d wrong" % (len(scripted_rows), n_where, no_level, len(bad_sc)), bad_sc)
        rep.check("K4", not bad, "mobs.js zone summaries and patrol paths point at real zones and kinds: %d wrong" % len(bad), bad)
        rep.summary["kinds"] = len(kl)

    # ------------------------------------------------------------------ C camps
    camps = []
    if "camps" in data and kinds:
        camps = rows_of(data["camps"]["camps"])
        bad, outside, n_pts = [], [], 0
        per_kind_n, per_kind_c, ids = {}, {}, set()
        pos_world = {}
        for s in sps:
            pl = vw.place(zbr, s)
            if pl:
                for v in s["variants"]:
                    pos_world.setdefault((pl[0], pl[1], pl[2]), set()).add(v["name"].lower())
        ghost, lvl_bad, far = [], [], []
        for c in camps:
            if c["id"] in ids:
                bad.append("camp id %s twice" % c["id"])
            ids.add(c["id"])
            k = kind_by_id.get(c["kind"])
            if k is None:
                bad.append("camp %s: kind id %s is not in mobs.js" % (c["id"], c["kind"]))
                continue
            if c["zone"] not in zones:
                bad.append("camp %s: zone %s is not in zones.js" % (c["id"], c["zone"]))
                continue
            if not in_zone(c["zone"], c["x"], c["y"]):
                outside.append("camp %s (%s) centre %s,%s is outside zone %s" % (c["id"], k["name"], c["x"], c["y"], c["zone"]))
            per_kind_n[c["kind"]] = per_kind_n.get(c["kind"], 0) + c["n"]
            per_kind_c[c["kind"]] = per_kind_c.get(c["kind"], 0) + 1
            lo = hi = None
            for p in c["pts"]:
                n_pts += 1
                x, y = p[0], p[1]
                if not in_zone(c["zone"], x, y):
                    outside.append("camp %s (%s) point %s,%s is outside zone %s" % (c["id"], k["name"], x, y, c["zone"]))
                names_here = pos_world.get((c["zone"], x, y))
                if names_here is None or k["key"] not in names_here:
                    ghost.append("camp %s: no %s spawn stands at zone %s %s,%s (there: %s)" % (c["id"], k["name"], c["zone"], x, y, sorted(names_here or [])))
                plo, phi = (p[2], p[3]) if len(p) > 3 else (p[2], p[2])
                lo = plo if lo is None else min(lo, plo)
                hi = phi if hi is None else max(hi, phi)
                d = ((x - c["x"]) ** 2 + (y - c["y"]) ** 2) ** 0.5
                if d > c["r"] + 2:
                    far.append("camp %s: point %s,%s is %d from the centre, radius %s" % (c["id"], x, y, d, c["r"]))
            if c["pts"] and (lo, hi) != (c["lmin"], c["lmax"]):
                lvl_bad.append("camp %s (%s): levels %s to %s, its points say %s to %s" % (c["id"], k["name"], c["lmin"], c["lmax"], lo, hi))
            if c["lmin"] < k["lmin"] or c["lmax"] > k["lmax"]:
                lvl_bad.append("camp %s: levels %s to %s outside its kind's %s to %s" % (c["id"], c["lmin"], c["lmax"], k["lmin"], k["lmax"]))
            for nb in c.get("near") or []:
                if nb[0] not in kind_by_id:
                    bad.append("camp %s: neighbour kind id %s is not in mobs.js" % (c["id"], nb[0]))
        rep.check("C1", not bad, "%d camps: ids unique, every kind, zone and neighbour kind exists" % len(camps), bad)
        rep.check("C2", not outside, "camp centres and %d camp points lie inside their zone: %d outside" % (n_pts, len(outside)), outside)
        rep.check("C3", not ghost, "every camp point is the position of a real spawn of that kind in an open region: %d are not"
                  % len(ghost), ghost)
        rep.check("C4", not lvl_bad, "camp level range = the range of its points, and inside its kind's range: %d wrong" % len(lvl_bad), lvl_bad)
        rep.check("C5", not far, "every point of a camp is within the camp's radius of its centre: %d are not" % len(far), far)
        bad = []
        for k in kinds.values():
            if per_kind_c.get(k["id"], 0) != k["camps"]:
                bad.append("%s: kind says %s camps, camps.js has %s" % (k["key"], k["camps"], per_kind_c.get(k["id"], 0)))
            if per_kind_n.get(k["id"], 0) != k["spawns"]:
                bad.append("%s: kind says %s spawns, its camps hold %s" % (k["key"], k["spawns"], per_kind_n.get(k["id"], 0)))
            zl = sorted(z[0] if isinstance(z, list) else z for z in (k.get("zones") or []))
            cz = sorted({c["zone"] for c in camps if c["kind"] == k["id"]}) if len(camps) < 0 else None
        kz = {}
        for c in camps:
            kz.setdefault(c["kind"], set()).add(c["zone"])
        for k in kinds.values():
            zl = {z[0] if isinstance(z, list) else z for z in (k.get("zones") or [])}
            if zl != kz.get(k["id"], set()):
                bad.append("%s: kind zones %s, its camps are in %s" % (k["key"], sorted(zl), sorted(kz.get(k["id"], set()))))
        rep.check("C6", not bad, "per kind: number of camps, number of spawns and zone list agree between mobs.js and camps.js: %d wrong"
                  % len(bad), bad)
        # coverage against the world: spawn rows of the kinds in the file
        covered = {}
        for c in camps:
            for p in c["pts"]:
                covered[(c["zone"], p[0], p[1], kind_by_id[c["kind"]]["key"])] = 1
        total_rows = rows_in = 0
        uncovered = {}
        for s in sps:
            if not huntable(s):
                continue
            pl = vw.place(zbr, s)
            hit_kind = [v["name"].lower() for v in s["variants"] if v["name"].lower() in kinds]
            if not hit_kind or not pl:
                continue
            total_rows += 1
            if any((pl[0], pl[1], pl[2], n) in covered for n in hit_kind):
                rows_in += 1
            else:
                uncovered[hit_kind[0]] = uncovered.get(hit_kind[0], 0) + 1
        camp_spawns = sum(c["n"] for c in camps)
        meta_counts = (data["mobs"].get("meta") or {}).get("counts") or {}
        rep.check("C7", total_rows - rows_in == 0,
                  "coverage: %d huntable Mob rows in open regions carry the name of a kind in mobs.js; %d of them stand on a camp point, "
                  "%d do not. Camps hold %d spawn points in all (file says monsterRows %s, spawnPoints %s)"
                  % (total_rows, rows_in, total_rows - rows_in, camp_spawns, meta_counts.get("monsterRows"), meta_counts.get("spawnPoints")),
                  ["%s: %d rows not on any camp point" % kv for kv in sorted(uncovered.items(), key=lambda kv: -kv[1])], warn=True)
        # every zone that has monsters in the world has camps, and every huntable spawn's name is a kind
        zone_rows, zone_names, not_kind = {}, {}, {}
        for s in sps:
            if not huntable(s):
                continue
            pl = vw.place(zbr, s)
            zone_rows[pl[0]] = zone_rows.get(pl[0], 0) + 1
            if not any(v["name"].lower() in kinds for v in s["variants"]):
                not_kind[s["key"]] = not_kind.get(s["key"], 0) + 1
                zone_names.setdefault(pl[0], set()).add(s["name"])
        camp_zones = {}
        for c in camps:
            camp_zones[c["zone"]] = camp_zones.get(c["zone"], 0) + 1
        bad = ["zone %s (%s): %d huntable spawn rows in the world, no camp in camps.js" % (zid, zs_db[zid]["name"], n)
               for zid, n in sorted(zone_rows.items()) if zid not in camp_zones]
        rep.check("C8", not bad, "every zone that has monsters in the world has camps: %d zones with huntable spawns, %d zones with camps, "
                  "%d zones without" % (len(zone_rows), len(camp_zones), len(bad)), bad)
        rep.check("K5", not not_kind, "every huntable spawn (no realm, in a zone of an open region, not peaceful, can be targeted, can die) "
                  "is of a kind in mobs.js: %d names are not (%d rows)" % (len(not_kind), sum(not_kind.values())),
                  ["%s: %d rows" % kv for kv in sorted(not_kind.items(), key=lambda kv: -kv[1])], warn=True)
        rep.summary["zones_with_monsters"] = len(zone_rows)
        rep.summary["zones_with_camps"] = len(camp_zones)
        rep.summary["camps"] = len(camps)
        rep.summary["camp_points"] = n_pts
        rep.summary["monster_rows_world"] = total_rows
        rep.summary["monster_rows_on_camps"] = rows_in

    # ------------------------------------------------------------------ D drops
    if "drops" in data:
        dr = data["drops"]
        mobs_d, unspawned = dr.get("mobs", {}), dr.get("unspawned", {})
        bad, n_rows, not_items = [], 0, set()
        for src in (mobs_d, unspawned):
            for key, kind in src.items():
                for r in kind.get("d") or []:
                    n_rows += 1
                    if str(r[0]).lower() not in items_db:
                        bad.append("%s drops %s: no such item" % (key, r[0]))
                    elif src is mobs_d and item_ids and str(r[0]).lower() not in item_ids:
                        not_items.add(r[0])
                    if not (0.0 <= r[1] <= 1.0):
                        bad.append("%s / %s: chance %r is not 0..1" % (key, r[0], r[1]))
                    by = r[4] if len(r) > 4 and isinstance(r[4], list) else []
                    if any(not (0.0 <= x <= 1.0) for x in by):
                        bad.append("%s / %s: byRealm %r" % (key, r[0], by))
                for r in kind.get("otd") or []:
                    if str(r[0]).lower() not in items_db:
                        bad.append("%s one-time drop %s: no such item" % (key, r[0]))
                    elif src is mobs_d and item_ids and str(r[0]).lower() not in item_ids:
                        not_items.add(r[0])
                for r in kind.get("q") or []:
                    if str(r[0]).lower() not in items_db:
                        bad.append("%s quest hand-over %s: no such item" % (key, r[0]))
        for iid in dr.get("byItem", {}):
            if iid.lower() not in items_db:
                bad.append("byItem %s: no such item" % iid)
        rep.check("D1", not bad, "%d drop rows: every item id is an ItemTemplate row, every chance is 0..1" % n_rows, bad)
        rep.check("D2", not not_items, "every item a spawned monster drops is in items.js: %d are not" % len(not_items), sorted(not_items))
        if kinds:
            with_loot = [k for k, v in mobs_d.items() if v.get("d") or v.get("otd")]
            orphan = sorted(k for k in with_loot if k not in kinds and k not in scripted_kinds)
            reasons = {}
            for k in orphan:
                w = names_world.get(k)
                if not w:
                    why = "no spawn in an open region"
                elif not any(huntable(s) for s, v in w):
                    why = "not huntable (realm NPC, peaceful, cannot be targeted or outside every zone)"
                else:
                    why = "huntable spawns exist"
                reasons.setdefault(why, []).append(k)
            real = reasons.get("huntable spawns exist", [])
            closed_only = reasons.get("no spawn in an open region", [])
            rep.check("D3", not real and not closed_only,
                      "monsters with loot in drops.js `mobs`: %d; matching a kind in mobs.js: %d; not matching: %d (%s); "
                      "counted as unspawned by drops.js: %d"
                      % (len(with_loot), len(with_loot) - len(orphan), len(orphan),
                         ", ".join("%s: %d" % (w, len(v)) for w, v in sorted(reasons.items())) or "none", len(unspawned)),
                      ["%s: %s" % (w, ", ".join(v[:12])) for w, v in sorted(reasons.items())])
            no_coin = sorted(k for k in list(kinds) + list(scripted_kinds) if k not in mobs_d)
            rep.check("D4", not no_coin, "every kind in mobs.js has an entry in drops.js (coin at least): %d have none" % len(no_coin), no_coin)
        # every kind that has a loot table in the world (scripted bosses included) has its loot joined in drops.js
        if kinds:
            tables = {}
            for r in con.execute("select TemplateName, count(*) n from LootTemplate group by TemplateName"):
                tables[(r["TemplateName"] or "").lower()] = r["n"]
            bad, with_table, scripted_n, scripted_loot = [], 0, 0, 0
            flag_bits = ((data["mobs"].get("meta") or {}).get("flagBits") or {})
            script_bit = flag_bits.get("scripted", 0)
            joined = dict(kinds)
            joined.update(scripted_kinds)              # the script-made kinds join drops.js by key like any other
            for key, k in joined.items():
                is_script = key in scripted_kinds or bool(script_bit and (k.get("f") or 0) & script_bit)
                scripted_n += is_script
                if key in tables:
                    with_table += 1
                    got = len((mobs_d.get(key) or {}).get("d") or [])
                    scripted_loot += bool(is_script and got)
                    if got != tables[key]:
                        bad.append("%s%s: LootTemplate has %d rows under this name, drops.js `mobs` has %d"
                                   % (key, " (scripted)" if is_script else "", tables[key], got))
            rep.check("D5", not bad, "every kind in mobs.js (script-made ones included) whose name has a loot table has that loot joined under the same "
                      "key in drops.js: %d kinds with a table, %d wrong; scripted kinds: %d, of them with loot joined: %d"
                      % (with_table, len(bad), scripted_n, scripted_loot), bad)
        # item source bits against drops and merchants
        if items and "src" in items[0]:
            dropped = {str(r[0]).lower() for k, v in mobs_d.items() if not v.get("s") for r in (v.get("d") or [])}
            flagged = {str(it["id"]).lower() for it in items if it["src"] & 1}
            by_script = {str(r[0]).lower() for k, v in mobs_d.items() if v.get("s") for r in (v.get("d") or [])}
            flagged_s = {str(it["id"]).lower() for it in items if it["src"] & 128}
            sa, sb = sorted(by_script - flagged_s), sorted(flagged_s - by_script)
            rep.check("I5", not sa and not sb, "items.js source bit `script` (128) = on the table of a script-made monster in drops.js: "
                      "%d items; %d not flagged, %d flagged without one" % (len(by_script), len(sa), len(sb)), sa[:6] + sb[:6])
            a_, b_ = sorted(dropped - flagged), sorted(flagged - dropped)
            rep.check("I2", not a_ and not b_, "items.js source bit `drop` = on the table of a monster with a spawn row in drops.js: %d items; "
                      "%d dropped but not flagged, %d flagged but not dropped" % (len(dropped), len(a_), len(b_)), a_[:6] + b_[:6])
            otd_items = {str(r[0]).lower() for k, v in mobs_d.items() for r in (v.get("otd") or [])}
            flagged = {str(it["id"]).lower() for it in items if it["src"] & 2}
            a_, b_ = sorted(otd_items - flagged), sorted(flagged - otd_items)
            rep.check("I4", not a_ and not b_, "items.js source bit `otd` = a one-time drop of a spawned monster in drops.js: %d items; "
                      "%d not flagged, %d flagged without one" % (len(otd_items), len(a_), len(b_)), a_[:6] + b_[:6])
        rep.summary["drop_rows"] = n_rows

    # ------------------------------------------------------------------ V vendors
    if "vendors" in data and "vendoritems" in data:
        vend = rows_of(data["vendors"]["vendors"])
        lists = data["vendoritems"]["lists"]
        bad, n_rows, sold = [], 0, set()
        for v in vend:
            if v["zone"] not in zones:
                bad.append("merchant %s: zone %s is not in zones.js" % (v["name"], v["zone"]))
            elif not in_zone(v["zone"], v["x"], v["y"]):
                bad.append("merchant %s at %s,%s is outside zone %s" % (v["name"], v["x"], v["y"], v["zone"]))
            if v["list"] not in lists:
                bad.append("merchant %s: list %s is not in vendoritems.js" % (v["name"], v["list"]))
        used = {v["list"] for v in vend}
        for lst, rws in lists.items():
            if lst not in used:
                bad.append("list %s is in vendoritems.js but no merchant uses it" % lst)
            for r in rws:
                n_rows += 1
                sold.add(str(r[0]).lower())
                if str(r[0]).lower() not in items_db:
                    bad.append("list %s: item %s does not exist" % (lst, r[0]))
        rep.check("V1", not bad, "%d merchants, %d lists, %d rows: zone and position valid, every list used and present, every item exists"
                  % (len(vend), len(lists), n_rows), bad)
        if item_ids:
            miss = sorted(sold - item_ids)
            rep.check("V2", not miss, "every item a merchant sells is in items.js: %d are not" % len(miss), miss)
            if items and "src" in items[0]:
                flagged = {str(it["id"]).lower() for it in items if it["src"] & 4}
                a_, b_ = sorted(sold - flagged), sorted(flagged - sold)
                rep.check("I3", not a_ and not b_, "items.js source bit `vendor` = sold on a list in vendoritems.js: %d items; "
                          "%d sold but not flagged, %d flagged but not sold" % (len(sold), len(a_), len(b_)), a_[:6] + b_[:6])

    # ------------------------------------------------------------------ R routes
    if "routes" in data:
        ro = data["routes"]
        stables = {s["id"]: s for s in rows_of(ro.get("stables"))}
        routes = rows_of(ro.get("routes"))
        bad, n_pts, off = [], 0, 0
        for s in stables.values():
            if s["zone"] not in zones:
                bad.append("stable %s: zone %s is not in zones.js" % (s["name"], s["zone"]))
            elif not in_zone(s["zone"], s["x"], s["y"]):
                bad.append("stable %s at %s,%s is outside zone %s" % (s["name"], s["x"], s["y"], s["zone"]))
        for r in routes:
            if r.get("from") not in stables or (r.get("to") is not None and r.get("to") not in stables):
                bad.append("route %s: from %s / to %s is not a stable id" % (r.get("id"), r.get("from"), r.get("to")))
            t = r.get("ticket")
            if t and str(t).lower() not in items_db:
                bad.append("route %s: ticket %s is not an item" % (r.get("id"), t))
            segs = r.get("path") or []
            for si, seg in enumerate(segs):
                if seg["zone"] not in zones:
                    bad.append("route %s: path zone %s is not in zones.js" % (r.get("id"), seg["zone"]))
                    continue
                z = zones[seg["zone"]]
                beside = {segs[j]["zone"] for j in (si - 1, si + 1) if 0 <= j < len(segs)}
                for p in seg["pts"]:
                    n_pts += 1
                    if in_zone(seg["zone"], p[0], p[1], margin=0):
                        continue
                    # a segment may carry the first point of the next zone (or the last of the one before) so the
                    # line reaches the edge: such a point must really stand in that neighbouring zone
                    real = wg.zone_of(zbr, zs_db[seg["zone"]]["region"], z["ox"] + p[0], z["oy"] + p[1])
                    if real is not None and real["id"] in beside:
                        off += 1
                    elif real is None and in_zone(seg["zone"], p[0], p[1], margin=8192):
                        off += 1
                    else:
                        bad.append("route %s: path point %s,%s filed under zone %s stands in %s" % (
                            r.get("id"), p[0], p[1], seg["zone"], "zone %s" % real["id"] if real else "no zone"))
        rep.check("R1", not bad, "%d stables, %d routes, %d path points: every stable and zone exists, positions inside the zone "
                  "(%d path points are the first point across a zone edge, kept so the line reaches the edge)" % (len(stables), len(routes), n_pts, off), bad)
        for g in ro.get("gates") or []:
            for end in ("from", "to"):
                e = g.get(end) or {}
                if e.get("zone") not in zones:
                    bad.append("gate %s %s: zone %s is not in zones.js" % (g.get("id"), end, e.get("zone")))
        rep.summary["routes"] = len(routes)

    # ------------------------------------------------------------------ Q quests
    if "quests" in data:
        qd = data["quests"]
        quests = qd.get("quests") or []
        dq_ids = {r[0] for r in con.execute("select ID from DataQuest")}
        bad, seen_q = [], set()
        n_data = 0
        for q in quests:
            if q["id"] in seen_q:
                bad.append("quest id %r twice" % (q["id"],))
            seen_q.add(q["id"])
            if q.get("src") == "data":
                n_data += 1
                if q["id"] not in dq_ids:
                    bad.append("quest %r (%s) is marked as a data quest but is not a DataQuest row" % (q["id"], q.get("name")))
            for nx in q.get("next") or []:
                pass
        for q in quests:
            for nx in q.get("next") or []:
                if nx not in seen_q:
                    bad.append("quest %r: next quest %r is not in the file" % (q["id"], nx))
        left = sum(((qd.get("meta") or {}).get("counts") or {}).get("leftOut", {}).values())
        rep.check("Q1", not bad and n_data + left == len(dq_ids),
                  "%d quests: ids unique, the %d data quests are DataQuest rows, follow-up quests exist; %d in the file + %d left out "
                  "(reasons in quests.meta) = %d DataQuest rows in the world: %s"
                  % (len(quests), n_data, n_data, left, len(dq_ids), "equal" if n_data + left == len(dq_ids) else "NOT equal"), bad)

        # places
        bad, n_place = [], 0

        def place_ok(what, zid, x, y):
            nonlocal n_place
            n_place += 1
            if zid not in zones:
                bad.append("%s: zone %s is not in zones.js" % (what, zid))
            elif not in_zone(zid, x, y):
                bad.append("%s: %s,%s is outside zone %s" % (what, x, y, zid))
        for q in quests:
            tag = "quest %s (%s)" % (q["id"], q.get("name"))
            for g in ([q["giver"]] if q.get("giver") else []) + list(q.get("givers") or []):
                if isinstance(g, dict) and g.get("zone") is not None:
                    place_ok(tag + " giver " + str(g.get("name")), g["zone"], g["x"], g["y"])
            for st in q.get("steps") or []:
                if st.get("at"):
                    place_ok("%s step %s" % (tag, st.get("n")), st["at"][0], st["at"][1], st["at"][2])
                t = st.get("target")
                if isinstance(t, dict) and t.get("zone") is not None:
                    place_ok("%s step %s target %s" % (tag, st.get("n"), t.get("name")), t["zone"], t["x"], t["y"])
                sp = st.get("spawn")
                if isinstance(sp, dict) and sp.get("zone") is not None:
                    place_ok("%s step %s spawn %s" % (tag, st.get("n"), sp.get("name")), sp["zone"], sp["x"], sp["y"])
                for fld in ("place", "area"):
                    v = st.get(fld)
                    if isinstance(v, list) and len(v) >= 3 and all(isinstance(c, (int, float)) for c in v[:3]):
                        place_ok("%s step %s %s" % (tag, st.get("n"), fld), v[0], v[1], v[2])
        rep.check("Q2", not bad, "%d quest places (givers, step spots, targets, event spawns): zone is in zones.js and the spot is inside it; "
                  "%d wrong" % (n_place, len(bad)), bad)

        # monsters named by quests
        known = set(kinds) | set(scripted_kinds) | set(names_world)
        if "drops" in data:
            known |= set(data["drops"].get("mobs", {})) | set(data["drops"].get("unspawned", {}))
        refs = {}

        def mob_ref(where, name):
            if isinstance(name, str) and name:
                refs.setdefault(name.lower(), where)
        for q in quests:
            tag = "quest %s (%s)" % (q["id"], q.get("name"))
            for hrow in q.get("hand") or []:
                mob_ref(tag + " hand-over", hrow[1])
            for srow in q.get("side") or []:
                mob_ref(tag + " side turn-in", srow.get("mob"))
            for frow in ((q.get("turnin") or {}).get("from") or []):
                mob_ref(tag + " turn-in", frow[0])
            for st in q.get("steps") or []:
                t = st.get("target")
                if isinstance(t, dict) and t.get("k") == "mob":
                    mob_ref("%s step %s" % (tag, st.get("n")), t.get("key"))
                    for kk in t.get("keys") or []:
                        mob_ref("%s step %s" % (tag, st.get("n")), kk)
                for drow in st.get("drops") or []:
                    mob_ref("%s step %s drop" % (tag, st.get("n")), drow[0])
                for frow in st.get("from") or []:
                    mob_ref("%s step %s" % (tag, st.get("n")), frow[0])
                for nm in st.get("alt") or []:
                    mob_ref("%s step %s" % (tag, st.get("n")), nm)
        unknown = sorted((k, w) for k, w in refs.items() if k not in known)
        no_page = sorted(k for k in refs if k in known and k not in kinds and k not in scripted_kinds)
        rep.check("Q3", not unknown, "%d different monsters named by quests (kill targets, hand-overs, drops): %d are kinds or script-made "
                  "kinds of mobs.js, %d are NPCs of the world without a monster page, %d are unknown names"
                  % (len(refs), len(refs) - len(unknown) - len(no_page), len(no_page), len(unknown)),
                  ["%s (%s)" % kw for kw in unknown])

        # items named by quests
        item_refs = {}

        def item_ref(where, iid):
            if isinstance(iid, str) and iid:
                item_refs.setdefault(iid, where)
            elif isinstance(iid, list):
                for one in iid:
                    item_ref(where, one)
        reward_items = set()
        for q in quests:
            tag = "quest %s (%s)" % (q["id"], q.get("name"))
            for hrow in q.get("hand") or []:
                item_ref(tag + " hand-over", hrow[2])
            for r_ in q.get("rw") or []:
                item_ref(tag + " reward", r_[0])
                reward_items.add(str(r_[0]).lower())
            for cls_items in (q.get("rwClass") or {}).values():
                item_ref(tag + " class reward", cls_items)
            item_ref(tag + " start item", q.get("startGives"))
            item_ref(tag + " turn-in", (q.get("turnin") or {}).get("item"))
            for srow in q.get("side") or []:
                item_ref(tag + " side turn-in", [srow.get("item"), srow.get("reward")])
            for st in q.get("steps") or []:
                stag = "%s step %s" % (tag, st.get("n"))
                item_ref(stag, st.get("item"))
                item_ref(stag, st.get("col"))
                item_ref(stag, st.get("use"))
                item_ref(stag, st.get("takes"))
                for g in st.get("gets") or []:
                    item_ref(stag, g[0])
                for drow in st.get("drops") or []:
                    item_ref(stag, drow[1])
                for trow in st.get("trades") or []:
                    item_ref(stag, list(trow))
                for crow in st.get("choices") or []:
                    item_ref(stag, crow[1] if len(crow) > 1 else None)
                nd = st.get("needs")
                if isinstance(nd, list) and nd:
                    item_ref(stag, nd[0])
                gr = st.get("grant")
                if isinstance(gr, list) and gr:
                    item_ref(stag, gr[0])
        for base, per_class in (qd.get("classRewards") or {}).items():
            item_ref("classRewards", base)
            item_ref("classRewards " + base, list(per_class.values()))
        missing = sorted((i, w) for i, w in item_refs.items() if i.lower() not in items_db)
        wrong_name = sorted("%s: quests.js names it %r, ItemTemplate says %r" % (i, nme, items_db[i.lower()]["Name"])
                            for i, nme in (qd.get("names") or {}).items()
                            if i.lower() in items_db and (items_db[i.lower()]["Name"] or "") != nme)
        no_name = sorted(i for i in (qd.get("names") or {}) if i.lower() not in items_db)
        rep.check("Q4", not missing and not wrong_name and not no_name,
                  "%d different items named by quests (rewards, step items, hand-overs, trades): every one is an ItemTemplate row; "
                  "%d missing; the %d names in quests.names equal the item table: %d differ, %d ids unknown"
                  % (len(item_refs), len(missing), len(qd.get("names") or {}), len(wrong_name), len(no_name)),
                  ["%s (%s)" % iw for iw in missing] + wrong_name + no_name)
        if item_ids:
            not_listed = sorted(i for i in reward_items if i in items_db and i not in item_ids)
            rep.check("Q5", not not_listed, "every quest reward item is in items.js: %d reward items, %d are not" % (len(reward_items), len(not_listed)),
                      not_listed, warn=True)
        # chances
        bad = []
        for q in quests:
            for hrow in q.get("hand") or []:
                if not (isinstance(hrow[3], (int, float)) and 0.0 <= hrow[3] <= 1.0):
                    bad.append("quest %s hand-over %s: chance %r" % (q["id"], hrow[2], hrow[3]))
        # the classic quests' own kill drops must be the ones drops.js lists as `q`
        if "drops" in data:
            dq = set()
            for src in (data["drops"].get("mobs", {}), data["drops"].get("unspawned", {})):
                for key, kind in src.items():
                    for r_ in kind.get("q") or []:
                        dq.add((key, str(r_[0]).lower(), r_[2], round(r_[1], 6)))
            mine_h, any_h = set(), set()
            for q in quests:
                for hrow in q.get("hand") or []:
                    row = (str(hrow[1]).lower(), str(hrow[2]).lower(), q["id"], round(hrow[3], 6))
                    any_h.add(row)                     # a drop on another monster of a kill step is filed as "kill" (it advances the step)
                    if len(hrow) > 4 and hrow[4] in ("drop", "side"):
                        mine_h.add(row)
            only_d, only_q = sorted(dq - any_h), sorted(mine_h - dq)
            rep.check("Q6", not bad and not only_d and not only_q, "quest hand-overs: every chance is 0..1; the classic quests' own kill drops "
                      "and side turn-ins are the same (monster, item, quest, chance) in quests.js and drops.js `q`: %d and %d, "
                      "%d only in drops.js, %d only in quests.js" % (len(mine_h), len(dq), len(only_d), len(only_q)),
                      bad + [str(x) for x in only_d[:6]] + [str(x) for x in only_q[:6]])
        rep.summary["quests"] = len(quests)

    # ------------------------------------------------------------------ T wording
    # Owner 2026-11-12: the pages use the game's stance words (aggressive, hostile, neutral, friendly), never
    # "attacks on sight". Checked in everything a page can show: the scripts, index.html and every string in data/.
    phrase = re.compile(r"attack(?:s|ed|ing)?[\s\-]+on[\s\-]+sight", re.I)
    bad = []
    page_files = [os.path.join(wg.ROOT, "index.html")] + sorted(glob.glob(os.path.join(wg.ROOT, "app", "**", "*.js"), recursive=True))
    for p in page_files:
        if not os.path.exists(p):
            continue
        for i, line in enumerate(strip_js_comments(open(p, encoding="utf-8", errors="replace").read()).split(NL), 1):
            if phrase.search(line):
                bad.append("%s:%d  %s" % (os.path.relpath(p, wg.ROOT).replace("\\", "/"), i, line.strip()[:140]))
    n_files = len(page_files)
    for name, value in data.items():
        hits = 0
        for path, s in walk_strings(value, name):
            if phrase.search(s):
                hits += 1
                if hits <= 2:
                    bad.append("data/%s.js %s: %r" % (name, path, s[:140]))
        if hits > 2:
            bad.append("data/%s.js: %d more strings" % (name, hits - 2))
    rep.check("T1", not bad, "stance wording: no page or data text uses the old \"on sight\" phrase for monsters that come at you; "
              "the pages say aggressive, hostile, neutral, friendly (%d page files and %d data files searched): %d places"
              % (n_files, len(data), len(bad)), bad)

    # ------------------------------------------------------------------ A app
    bad = []
    man = os.path.join(wg.ROOT, "app", "manifest.js")
    scripts = re.findall(r'"(app/[^"]+\.js)"', open(man, encoding="utf-8").read()) if os.path.exists(man) else []
    for sc in scripts:
        if not os.path.exists(os.path.join(wg.ROOT, sc)):
            bad.append("%s is in the manifest but does not exist" % sc)
    asked = {}
    for sc in scripts + ["app/core.js"]:
        p = os.path.join(wg.ROOT, sc)
        if os.path.exists(p):
            src = open(p, encoding="utf-8", errors="replace").read()
            for m in re.finditer(r"WG\.load\(([^)]*)\)", src):
                for nm in re.findall(r"[\"']([a-z][a-zA-Z0-9_]*)[\"']", m.group(1)):
                    asked.setdefault(nm, set()).add(sc)
    for nm, where in sorted(asked.items()):
        if not os.path.exists(os.path.join(a.out, nm + ".js")):
            bad.append("data/%s.js is asked for by %s but does not exist" % (nm, ", ".join(sorted(where))))
    rep.check("A1", not bad, "%d scripts in app/manifest.js exist; the %d data files the pages ask for by name exist (%s)"
              % (len(scripts), len(asked), ", ".join(sorted(asked))), bad)

    rep.summary["warnings"] = rep.warn
    if a.summary:
        json.dump(rep.summary, open(a.summary, "w", encoding="utf-8"), indent=1)
    print("verify_data: %s%s" % ("PASS" if rep.ok else "FAIL", "" if not rep.warn else " (%d warnings)" % rep.warn))
    return 0 if rep.ok else 1


if __name__ == "__main__":
    sys.exit(main())
