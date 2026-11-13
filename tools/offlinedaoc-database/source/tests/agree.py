"""Do the pages show the same number for the same thing? (lane D-V, part of the final sweep)

    python -I source/tests/agree.py

For a handful of real cases it renders the pages in headless Edge (source/shot.py --dom) and looks for the number
the data file holds, formatted the way app/core.js formats it:
  - a drop chance: on the item page and on the dropping monster's page, against data/drops.js;
  - a monster's level range: on the monster page, in the hunt finder (searched by name) and on a quest page that
    names the monster, against data/mobs.js.
Prints one line per page: found / NOT FOUND, and exits 1 when something is missing.
"""
import html, json, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.dirname(HERE)
ROOT = os.path.dirname(SRC)
OUT = os.path.join(SRC, "scratch", "D-V", "agree")


def read_provide(name):
    s = open(os.path.join(ROOT, "data", name + ".js"), encoding="utf-8").read().strip()
    a = s.index(",", s.index("WG.provide(")) + 1
    return json.loads(s[a:s.rindex(")")])


def pct(p):
    """WG.pct in app/core.js."""
    v = p * 100
    if v >= 99.95:
        return "100%"
    if v >= 10:
        return re.sub(r"\.0$", "", "%.1f" % v) + "%"
    if v >= 1:
        return "%.1f%%" % v
    if v >= 0.01:
        return "%.2f%%" % v
    return "<0.01%" if v > 0 else "0%"


def page_text(route, name):
    os.makedirs(OUT, exist_ok=True)
    dom_path = os.path.join(OUT, name + ".html")
    subprocess.run([sys.executable, "-I", os.path.join(SRC, "shot.py"), route, dom_path, "--dom", "--wait", "8000"], capture_output=True)
    dom = open(dom_path, encoding="utf-8", errors="replace").read()
    main_html = dom[dom.find('id="wg-main"'):]
    text = re.sub(r"<(script|style)[^>]*>.*?</\1>", " ", main_html, flags=re.S)
    text = html.unescape(re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", text))).strip()
    open(os.path.join(OUT, name + ".txt"), "w", encoding="utf-8").write(text)
    return text


def main():
    drops, mobs, items, quests = read_provide("drops"), read_provide("mobs"), read_provide("items"), read_provide("quests")
    kcols = mobs["kinds"]["cols"]
    kinds = {r[kcols.index("key")]: dict(zip(kcols, r)) for r in mobs["kinds"]["rows"]}
    icols = items["cols"]
    item_name = {r[icols.index("id")]: r[icols.index("name")] for r in items["rows"]}
    ok = True

    def report(good, what):
        nonlocal ok
        ok = ok and good
        print("%s  %s" % ("found    " if good else "NOT FOUND", what))

    # --- drop chances: three monster / item pairs with different sizes of chance, none realm-locked or quest-gated
    cases = []
    for key in sorted(kinds):
        rows_ = (drops["mobs"].get(key) or {}).get("d") or []
        for r in rows_:
            top = [x[0] for x in sorted(drops["byItem"].get(r[0], []), key=lambda x: -x[1])[:5]]    # the item page shows 8 rows a page
            plain = r[3] == 0 and not (len(r) > 4 and isinstance(r[4], list)) and r[0] in item_name and key in top
            if plain and len(cases) < 3 and all(abs(r[1] - c[2]) > 0.05 for c in cases) and 0.005 < r[1] < 0.9:
                cases.append((key, r[0], r[1]))
        if len(cases) == 3:
            break
    for key, iid, ch in cases:
        k = kinds[key]
        want = pct(ch)
        t_item = page_text("item/" + iid, "item_" + re.sub(r"\W+", "_", iid))
        t_mob = page_text("mob/%s?level=%s" % (k["id"], k["lmin"]), "mob_%s" % k["id"])
        # on the item page the chance stands in the row of that monster; on the monster page in the row of the item
        i = t_item.lower().find(k["name"].lower())
        report(i >= 0 and want in t_item[i:i + 200], "item page %s: %s drops it at %s (drops.js %.6f)" % (item_name[iid], k["name"], want, ch))
        j = t_mob.find(item_name[iid])
        report(j >= 0 and want in t_mob[j:j + 200], "monster page %s: %s at %s" % (k["name"], item_name[iid], want))

    # --- level ranges: a monster with a range that a quest names as a kill target
    target = None
    for q in quests["quests"]:
        for st in q.get("steps") or []:
            t = st.get("target") or {}
            k = kinds.get(t.get("key") or "")
            if t.get("k") == "mob" and k and k["lmax"] > k["lmin"] and k["spawns"] >= 10:
                target = (q, k)
                break
        if target:
            break
    if target:
        q, k = target
        rng = "%s to %s" % (k["lmin"], k["lmax"])
        t_mob = page_text("mob/%s?level=%s" % (k["id"], k["lmin"]), "lvl_mob_%s" % k["id"])
        report(("Level %s" % rng) in t_mob or rng in t_mob[:400], "monster page %s: level %s (mobs.js)" % (k["name"], rng))
        t_hunt = page_text("hunt?level=%s&con=custom&min=0&max=75&size=1&q=%s" % (k["lmin"], k["name"].replace(" ", "%20")), "lvl_hunt_%s" % k["id"])
        i = t_hunt.find(k["name"])
        seen = re.findall(r"level (\d+)(?: to (\d+))?", t_hunt[i:]) if i >= 0 else []
        lows = [int(a) for a, b in seen]
        highs = [int(b or a) for a, b in seen]
        inside = bool(seen) and min(lows) >= k["lmin"] and max(highs) <= k["lmax"]
        report(inside, "hunt finder, camps of %s: every camp's levels lie inside %s (camps shown: %d, lowest %s, highest %s)"
               % (k["name"], rng, len(seen), min(lows) if lows else "-", max(highs) if highs else "-"))
        t_quest = page_text("quest/%s" % q["id"], "lvl_quest_%s" % q["id"])
        i = t_quest.find(k["name"])
        report(i >= 0 and (rng in t_quest), "quest page %s (%s): names %s with level %s" % (q["id"], q["name"], k["name"], rng))
    print("agree: %s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
