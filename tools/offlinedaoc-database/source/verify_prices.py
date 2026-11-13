"""Independent check of the merchants, their prices and the stable routes (lane D-V).

    python -I source/verify_prices.py [--db <world>] [--out <data dir>] [--summary <file.json>]

The rules are read from the server (see source/verify_world.py for the file:line list) and applied to the world
tables here, separately from source/build_vendors.py and source/build_routes.py. Then:

 merchants   data/vendors.js + data/vendoritems.js
   M1  every merchant in the file is a spawn in an open region, at that zone position, selling that list
   M2  every spawn of a merchant class with a list that holds at least one existing item is in the file
       (script merchants that pick their list in code, OFMerchant*, are counted apart)
   M3  every row of every list: the item exists, page and slot are the MerchantItem row's, and the price is
       ItemTemplate.Price (what one purchase costs, GameMerchant.cs:112)
   M4  no row of a used list is missing from the file
   M5  the currency column agrees with the merchant's class (bounty points, seals)
 stable routes   data/routes.js
   R1  every route in the file is one the server honours: the ticket is in that stable master's list, it is a
       ticket (Item_Type 40), PathPoints with the ticket's id exist (a Path row is optional, MovementMgr.cs:102-112) and start
       within 500 of the master on X and Y
       (GameStableMaster.cs:128-136); the price is the ticket's ItemTemplate.Price
   R2  every (stable master, ticket) pair the server honours is in the file
   R3  where the route says it arrives is where the path really ends

Exit code 0 = all pass, 1 = a difference.
"""
import argparse, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import wgcommon as wg  # noqa: E402
import verify_world as vw  # noqa: E402


class Report:
    def __init__(self):
        self.ok, self.lines, self.summary = True, [], {}

    def check(self, code, good, text, details=()):
        self.ok = self.ok and bool(good)
        self.lines.append("%s: %s %s" % ("PASS" if good else "FAIL", code, text))
        print(self.lines[-1])
        for d in list(details)[:15]:
            print("      " + str(d))

    def note(self, text):
        self.lines.append("note: " + text)
        print(self.lines[-1])


def read_provide(path):
    s = open(path, encoding="utf-8").read().strip()
    a = s.index(",", s.index("WG.provide(")) + 1
    return json.loads(s[a:s.rindex(")")])


def rows_of(t):
    if isinstance(t, dict) and "cols" in t and "rows" in t:
        return [dict(zip(t["cols"], r)) for r in t["rows"]]
    return list(t or [])


def check_merchants(con, out, rep, sps, zbr):
    vpath, ipath = os.path.join(out, "vendors.js"), os.path.join(out, "vendoritems.js")
    if not (os.path.exists(vpath) and os.path.exists(ipath)):
        rep.check("M0", False, "data/vendors.js or data/vendoritems.js is missing")
        return
    v, vi = read_provide(vpath), read_provide(ipath)
    vendors = rows_of(v["vendors"])
    lists = vi["lists"]
    lists_l = {k.lower(): r for k, r in lists.items()}
    items = {r["Id_nb"].lower(): r for r in con.execute("select Id_nb, Price, PackSize, Name from ItemTemplate")}
    world_rows = {}
    for r in con.execute("select ItemListID, ItemTemplateID, PageNumber, SlotPosition from MerchantItem"):
        world_rows.setdefault((r["ItemListID"] or "").lower(), []).append(r)

    by_place = {}
    for s in sps:
        if vw.merchant_list(s):
            by_place.setdefault(vw.place(zbr, s), []).append(s)
    # M1
    bad, script, matched = [], 0, set()
    for r in vendors:
        cands = by_place.get((r["zone"], r["x"], r["y"]), [])
        hit = [s for s in cands if s["name"] == r["name"] and vw.merchant_list(s).lower() == str(r["list"]).lower()]
        if hit:
            matched.add(hit[0]["mob_id"])
            continue
        scripted = [s for s in cands if "OFMerchant" in s["eff_cls"]]
        if scripted:                               # scripts/teleporters/OFMerchant.cs chooses name and list in code
            script += 1
            matched.add(scripted[0]["mob_id"])
            continue
        bad.append("%s (list %s) at zone %s %s,%s: no such spawn; there: %s"
                   % (r["name"], r["list"], r["zone"], r["x"], r["y"], [(s["name"], vw.merchant_list(s)) for s in cands]))
    rep.check("M1", not bad, "%d merchants in the file, %d are spawns in open regions with that list at that place, "
              "%d script merchants (OFMerchant) taken on trust, %d wrong" % (len(vendors), len(vendors) - len(bad) - script, script, len(bad)), bad)
    # M2
    missing, empty, twins = [], 0, 0
    in_file = {((r["zone"], r["x"], r["y"]), r["name"], str(r["list"]).lower()) for r in vendors}
    for s in sps:
        lst = vw.merchant_list(s)
        if not lst or not vw.is_a(s["eff_cls"], "GameMerchant") or s["mob_id"] in matched:
            continue
        if vw.place(zbr, s) is None:
            continue
        if (vw.place(zbr, s), s["name"], lst.lower()) in in_file:
            twins += 1                             # a second Mob row of the same merchant on the same spot
            continue
        sellable = [r for r in world_rows.get(lst.lower(), []) if (r["ItemTemplateID"] or "").lower() in items]
        if not sellable:
            empty += 1
            continue
        missing.append("%s (%s, list %s, %d rows) at %s" % (s["name"], s["eff_cls"], lst, len(sellable), vw.place(zbr, s)))
    rep.check("M2", not missing, "merchant spawns with something to sell that are not in the file: %d "
              "(left out rightly: %d with an empty list, %d second rows of a merchant already listed on the same spot)"
              % (len(missing), empty, twins), missing)
    # M3 + M4
    bad, n_rows, missing_rows = [], 0, []
    used = {str(r["list"]).lower() for r in vendors}
    for lst in sorted(used):
        got = lists_l.get(lst)
        if got is None:
            bad.append("list %s is used by a merchant but not in vendoritems.js" % lst)
            continue
        want = {}
        for r in world_rows.get(lst, []):
            iid = (r["ItemTemplateID"] or "").lower()
            if iid in items:
                want[(r["PageNumber"], r["SlotPosition"])] = iid
        seen = set()
        for row in got:
            n_rows += 1
            iid, price, page, slot = str(row[0]).lower(), row[1], row[2], row[3]
            seen.add((page, slot))
            if iid not in items:
                bad.append("list %s: item %s does not exist" % (lst, row[0]))
            elif want.get((page, slot)) != iid:
                bad.append("list %s page %s slot %s: file says %s, MerchantItem says %s" % (lst, page, slot, row[0], want.get((page, slot))))
            elif int(price) != int(items[iid]["Price"] or 0):
                bad.append("list %s item %s: file price %s, ItemTemplate.Price %s" % (lst, row[0], price, items[iid]["Price"]))
        for k, iid in want.items():
            if k not in seen:
                missing_rows.append("list %s page %s slot %s: %s is not in the file" % (lst, k[0], k[1], iid))
    rep.check("M3", not bad, "%d list rows on %d lists: item exists, page and slot match MerchantItem, price = ItemTemplate.Price; %d wrong"
              % (n_rows, len(used), len(bad)), bad)
    rep.check("M4", not missing_rows, "rows of used lists missing from the file: %d" % len(missing_rows), missing_rows)
    # M5
    bad = []
    by_id = {s["mob_id"]: s for s in sps}
    for r in vendors:
        cands = [s for s in by_place.get((r["zone"], r["x"], r["y"]), []) if s["name"] == r["name"]]
        if not cands or "currency" not in r:
            continue
        cls = cands[0]["eff_cls"]
        want = "coin"
        if vw.is_a(cls, "GameBountyMerchant"):
            want = "bp"
        elif vw.is_a(cls, "GameItemCurrencyMerchant"):
            want = "item"
        if r["currency"] != want:
            bad.append("%s (%s): file says %s, the class pays in %s" % (r["name"], cls, r["currency"], want))
    rep.check("M5", not bad, "currency by merchant class (coin / bounty points / an item such as seals): %d wrong" % len(bad), bad)
    rep.summary["merchants"] = {"merchants": len(vendors), "lists": len(used), "rows": n_rows, "script": script}


def find_routes(data):
    """routes.js -> (stables by id, routes list). Follows source/contract/routes.md; tolerant of the table form."""
    stables = rows_of(data.get("stables"))
    routes = rows_of(data.get("routes"))
    return {s.get("id"): s for s in stables}, routes


def check_routes(con, out, rep, sps, zs, zbr):
    path = os.path.join(out, "routes.js")
    if not os.path.exists(path):
        rep.check("R0", False, "data/routes.js is missing")
        return
    data = read_provide(path)
    stables, routes = find_routes(data)
    mine, near = vw.stable_routes(con, sps)
    by_key = {}
    for m in mine:
        pl = vw.place(zbr, m)
        by_key[(pl, m["ticket"].lower())] = m
    seen, bad, end_bad, checked, gaps = set(), [], [], 0, []
    other = 0
    for r in routes:
        st = stables.get(r.get("from"))
        ticket = r.get("ticket") or r.get("ticketId") or r.get("item")
        if st is None or not ticket:
            other += 1                              # a connection that is not a ticket route (boat, portal...)
            continue
        checked += 1
        key = ((st["zone"], st["x"], st["y"]), str(ticket).lower())
        m = by_key.get(key)
        if m is None:
            bad.append("%s -> %s with ticket %s: the server would not honour this ticket there"
                       % (st.get("name"), (stables.get(r.get("to")) or {}).get("name"), ticket))
            continue
        seen.add(key)
        if r.get("price") is not None and int(r["price"]) != int(m["price"] or 0):
            bad.append("%s ticket %s: file price %s, ItemTemplate.Price %s" % (st.get("name"), ticket, r["price"], m["price"]))
        to = stables.get(r.get("to"))
        if to is not None and "zone" in to and to["zone"] in zs and st["zone"] in zs:
            ex, ey = m["path_end"][0], m["path_end"][1]
            region = zs[st["zone"]]["region"]

            def dist(s2):
                z2 = zs.get(s2.get("zone"))
                if z2 is None or z2["region"] != region:
                    return None
                return ((z2["ox"] + s2["x"] - ex) ** 2 + (z2["oy"] + s2["y"] - ey) ** 2) ** 0.5
            d_to = dist(to)
            if d_to is None:
                end_bad.append("%s -> %s (ticket %s): the file's destination is in another region than the ride"
                               % (st.get("name"), to.get("name"), ticket))
            else:
                gaps.append(d_to)
                nearest = min((s2 for s2 in stables.values() if dist(s2) is not None), key=dist)
                if nearest.get("id") != to.get("id") and dist(nearest) < d_to - 1:
                    end_bad.append("%s -> %s (ticket %s): the path ends %d from that stable but %d from %s"
                                   % (st.get("name"), to.get("name"), ticket, d_to, dist(nearest), nearest.get("name")))
                if r.get("gap") is not None and abs(r["gap"] - d_to) > 60:
                    end_bad.append("%s -> %s (ticket %s): gap %s in the file, %d by my measure"
                                   % (st.get("name"), to.get("name"), ticket, r["gap"], d_to))
    rep.check("R1", not bad, "%d ticket routes in the file: ticket in the master's list, Item_Type 40, path points exist and "
              "starts within 500, price = ItemTemplate.Price; %d wrong (%d other connections not checked here)"
              % (checked, len(bad), other), bad)
    missing = ["%s (%s) ticket %s" % (m["name"], k[0], m["ticket"]) for k, m in by_key.items() if k not in seen]
    rep.check("R2", not missing, "ticket routes the server honours: %d, missing from the file: %d" % (len(by_key), len(missing)), missing)
    rep.check("R3", not end_bad, "arrival: the stable the file names is the stable nearest to the path's last point, and the "
              "file's gap is that distance: %d wrong (largest gap %d units)" % (len(end_bad), max(gaps) if gaps else 0), end_bad)
    if near:
        rep.note("tickets on a stable master's list that cannot be used there (no path or it starts elsewhere): %d, e.g. %s"
                 % (len(near), near[0]))
    rep.summary["routes"] = {"routes": checked, "server": len(by_key), "other": other}


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--db", default=wg.DEFAULT_DB)
    ap.add_argument("--out", default=os.path.join(wg.ROOT, "data"))
    ap.add_argument("--summary", default="")
    a = ap.parse_args()
    con = wg.open_db(a.db)
    rep = Report()
    sps = vw.spawns(con)
    zs = wg.zones(con)
    zbr = wg.zones_by_region(zs)
    check_merchants(con, a.out, rep, sps, zbr)
    check_routes(con, a.out, rep, sps, zs, zbr)
    if a.summary:
        json.dump(rep.summary, open(a.summary, "w", encoding="utf-8"), indent=1)
    print("verify_prices: %s" % ("PASS" if rep.ok else "FAIL"))
    return 0 if rep.ok else 1


if __name__ == "__main__":
    sys.exit(main())
