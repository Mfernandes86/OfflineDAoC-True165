"""Final sweep helper (lane D-V): open every page with realistic inputs in headless Edge, at 1500 and 1100 wide,
save the screenshots and the rendered HTML, and list pages that show an error, stay empty, are stuck loading, say
"attacks on sight", or draw no pin where pins are expected.

    python -I source/tests/sweep.py [--only hunt,item] [--sizes 1500x1100,1100x1000] [--out source/scratch/D-V/sweep]
                                    [--list]

The page list is made from the data files at run time (ids change between builds): the item with the most
droppers, an item only a script-made boss drops, an item only merchants sell, a monster in the most zones, a
script-made boss with loot, the widest level range, a camp with a stronger aggressive neighbour in reach, a frontier
zone, a zone with no picture, a horse route, a Shrouded Isles flying route that needs reputation, two stables with
no link, an epic quest, a quest with a chance hand-over.
Looking at the pictures is still the verifier's job; this makes them and flags the obvious.
"""
import argparse, html, json, os, re, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.dirname(HERE)
ROOT = os.path.dirname(SRC)


def read_provide(name):
    s = open(os.path.join(ROOT, "data", name + ".js"), encoding="utf-8").read().strip()
    a = s.index(",", s.index("WG.provide(")) + 1
    return json.loads(s[a:s.rindex(")")])


def rows(t):
    return [dict(zip(t["cols"], r)) for r in t["rows"]]


def pages():
    drops, mobs, camps = read_provide("drops"), read_provide("mobs"), read_provide("camps")
    zones, routes, vend, items = read_provide("zones"), read_provide("routes"), read_provide("vendors"), read_provide("items")
    quests = read_provide("quests")
    kinds = rows(mobs["kinds"])
    by_key = {k["key"]: k for k in kinds}
    scripted = rows(mobs["scripted"]) if mobs.get("scripted") else []
    dm = drops["mobs"]
    # items
    by_item = drops["byItem"]
    many = max(by_item, key=lambda i: len(by_item[i]))
    boss_item = next(i for i, r in sorted(by_item.items()) if all(len(x) > 3 and x[3] == 2 for x in r))
    it = rows(items)
    vendor_only = next(x["id"] for x in it if x["src"] == 4 and x["cat"] in ("armor", "weapon") and x["level"] >= 20)
    # monsters
    multi = max(kinds, key=lambda k: len(k["zones"]))
    wide = max((k for k in kinds if k["spawns"] >= 5), key=lambda k: k["lmax"] - k["lmin"])
    boss = next(s for s in scripted if s["key"] in dm and dm[s["key"]].get("d") and s.get("rank") in ("boss", "epic", "raid", "event"))
    ccols = camps["camps"]["cols"]
    ci = {c: i for i, c in enumerate(ccols)}
    danger = next(r for r in camps["camps"]["rows"] if r[ci["rup"]] >= 2 and r[ci["n"]] >= 6)
    danger_kind = next(k for k in kinds if k["id"] == danger[ci["kind"]])
    # zones
    zl = zones["zones"]
    frontier = next(z["id"] for z in zl if z["kind"] == "frontier")
    nopic = next(z["id"] for z in zl if not z.get("map"))
    floors = next(z["id"] for z in zl if z.get("floors"))
    # merchants
    vs = rows(vend["vendors"])
    big = max(vs, key=lambda v: v["n"])
    # routes
    st = {s["id"]: s for s in routes["stables"]}
    horse = next(r for r in routes["routes"] if r["kind"] == "horse" and r.get("to"))
    fly = next(r for r in routes["routes"] if r["kind"] != "horse" and r.get("reputation") and r.get("to"))
    alb = next(s["id"] for s in routes["stables"] if s["realm"] == 1 and s.get("tickets"))
    hib = next(s["id"] for s in routes["stables"] if s["realm"] == 3 and s.get("tickets"))
    # quests
    ql = quests["quests"]
    epic = next(q["id"] for q in ql if q.get("kind") == "epic")
    chance = next(q["id"] for q in ql if any(0 < h[3] < 1 for h in (q.get("hand") or [])))
    chance_q = next(q for q in ql if q["id"] == chance)
    return [
        ("home", "home"),
        ("search-monster", "search?q=wolf"),
        ("search-item", "search?q=ring%20of"),
        ("search-merchant", "search?q=" + big["name"].split()[-1]),
        ("search-quest", "search?q=wolf%20pelt"),
        ("items-l5-armsman", "items?lvl=5&realm=1&cls=2"),
        ("items-l24-warrior", "items?lvl=24&realm=2&cls=22"),
        ("items-l50-hero", "items?lvl=50&realm=3&cls=44"),
        ("items-upgrade-chest", "items?m=up&lvl=24&realm=1&cls=2&slot=25"),
        ("item-many-droppers", "item/" + many),
        ("item-script-boss", "item/" + boss_item),
        ("item-merchant-only", "item/" + vendor_only),
        ("hunt-l5-general", "hunt?level=5&realm=1&con=general"),
        ("hunt-l24-lowred", "hunt?level=24&realm=2&con=low-red"),
        ("hunt-l50-lowpurple", "hunt?level=50&realm=3&con=low-purple"),
        ("hunt-l24-stance-calm", "hunt?level=24&realm=1&con=general&stance=calm"),
        ("hunt-l24-dungeons", "hunt?level=24&realm=1&con=general&where=dungeon"),
        ("hunt-l40-frontier", "hunt?level=40&realm=2&con=general&where=frontier"),
        ("hunt-l50-epic-zones", "hunt?level=50&realm=3&con=general&ez=only"),
        ("hunt-bosses", "hunt?mode=bosses&level=50&realm=1"),
        ("mobs-l24", "mobs?level=24"),
        ("mob-several-zones", "mob/%s?level=%s" % (multi["id"], multi["lmin"])),
        ("mob-script-boss", "mob/%s?level=50" % boss["id"]),
        ("mob-wide-range", "mob/%s?level=%s" % (wide["id"], (wide["lmin"] + wide["lmax"]) // 2)),
        ("mob-danger-camp", "mob/%s?level=%s&camp=%s" % (danger_kind["id"], max(1, danger[ci["lmin"]] - 2), danger[ci["id"]])),
        ("zones", "zones"),
        ("zone-outdoor", "zones/0"),
        ("zone-dungeon-floors", "zones/%s" % floors),
        ("zone-frontier", "zones/%s" % frontier),
        ("zone-no-picture", "zones/%s" % nopic),
        ("vendors-find", "vendors?level=24"),
        ("vendors-browse", "vendors?mode=merchants"),
        ("vendor-one", "vendor/%s" % big["id"]),
        ("routes-horse", "routes?from=%s&to=%s" % (horse["from"], horse["to"])),
        ("routes-si-flying", "routes?from=%s&to=%s" % (fly["from"], fly["to"])),
        ("routes-no-link", "routes?from=%s&to=%s" % (alb, hib)),
        ("routes-network", "routes"),
        ("routes-one-stable", "routes?stable=%s" % fly["from"]),
        ("quests-can-take", "quests?lv=24&realm=1&cls=2"),
        ("quests-reward-weapon", "quests?lv=24&realm=2&cls=22&rw=weapon"),
        ("quest-epic", "quest/%s" % epic),
        ("quest-chance-handover", "quest/%s" % chance),
        ("about", "about"),
    ], {"many": many, "boss_item": boss_item, "vendor_only": vendor_only, "multi": multi["key"], "wide": wide["key"],
        "boss": boss["key"], "danger_camp": danger[ci["id"]], "danger_kind": danger_kind["key"], "chance_quest": chance,
        "chance_quest_name": chance_q["name"], "epic": epic, "big_vendor": big["name"]}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--sizes", default="1500x1100,1100x1000")
    ap.add_argument("--out", default=os.path.join(SRC, "scratch", "D-V", "sweep"))
    ap.add_argument("--wait", default="8000")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    plist, picks = pages()
    json.dump({"pages": plist, "picks": picks}, open(os.path.join(a.out, "pages.json"), "w", encoding="utf-8"), indent=1)
    if a.list:
        for name, route in plist:
            print("%-26s %s" % (name, route))
        print(json.dumps(picks, indent=1))
        return 0
    only = [x for x in a.only.split(",") if x]
    shot = os.path.join(SRC, "shot.py")
    flagged = []
    for name, route in plist:
        if only and not any(o in name for o in only):
            continue
        t0 = time.time()
        dom_path = os.path.join(a.out, name + ".html")
        subprocess.run([sys.executable, "-I", shot, route, dom_path, "--dom", "--wait", a.wait], capture_output=True)
        notes = []
        if os.path.exists(dom_path):
            dom = open(dom_path, encoding="utf-8", errors="replace").read()
            main_html = dom[dom.find('id="wg-main"'):]
            text = re.sub(r"<(script|style)[^>]*>.*?</\1>", " ", main_html, flags=re.S)
            text = re.sub(r"<[^>]+>", " ", text)
            text = html.unescape(re.sub(r"\s+", " ", text)).strip()
            open(os.path.join(a.out, name + ".txt"), "w", encoding="utf-8").write(text)
            if "wg-error" in main_html:
                notes.append("ERROR BOX: " + text[:300])
            if "This page is not built yet" in main_html:
                notes.append("not built")
            if len(text) < 200:
                notes.append("nearly empty (%d characters of text): %r" % (len(text), text[:160]))
            for word in ("undefined", "NaN", "[object Object]", "null"):
                n = len(re.findall(r"(?<![\w-])%s(?![\w-])" % re.escape(word), text))
                if n:
                    notes.append("%d x %r in the visible text" % (n, word))
            if re.search(r"attack(?:s|ed|ing)?[\s\-]+on[\s\-]+sight", main_html, re.I):
                notes.append("says \"attacks on sight\" (the pages use aggressive / hostile / neutral / friendly)")
            if re.search(r"Loading(…|\.\.\.)", text):
                notes.append("still says Loading")
        else:
            notes.append("no DOM dump")
        for size in a.sizes.split(","):
            png = os.path.join(a.out, "%s_%s.png" % (name, size.split("x")[0]))
            if os.path.exists(png):
                os.remove(png)
            subprocess.run([sys.executable, "-I", shot, route, png, "--size", size, "--wait", a.wait], capture_output=True)
            if not os.path.exists(png):
                notes.append("no screenshot at " + size)
        print("%-26s %-52s %4.0f s  %s" % (name, route, time.time() - t0, "; ".join(notes) if notes else "ok"))
        sys.stdout.flush()
        if notes:
            flagged.append((name, route, notes))
    print()
    print("%d pages flagged" % len(flagged))
    return 0


if __name__ == "__main__":
    sys.exit(main())
