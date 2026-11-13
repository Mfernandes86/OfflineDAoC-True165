"""Independent check of the drop rates (lane D-V).

    python -I source/verify_drops.py [--db <world>] [--out <data dir>] [--kills 20000] [--seed 165]
                                     [--dump <file.json>] [--no-mc] [--tol 0.0005]

This file was written from the server's C# WITHOUT looking at source/build_drops.py, so that two separate readings
of the code meet in the numbers. It does three things:

 1. builds its own model of what one kill drops (the rules below, each with the file:line it was read from);
 2. proves its exact formula against a seeded Monte Carlo that plays the server's procedure roll by roll;
 3. compares every monster and every item with data/drops.js and prints each difference.

Exit code 0 = everything agrees, 1 = differences (or drops.js missing), 2 = a broken assumption about the data.

THE RULES (server source = wgcommon.SERVER_SRC, paths relative to GameServer/)

 A. When does a kill drop anything?  serverrules/AbstractServerRules.cs
    - :1055  the monster must be reward-eligible (not a pet, not charmed recently)
    - :1218  if the monster is GREY to ANY living that damaged it (or a guard helped) -> no XP, NO LOOT AT ALL
    - :1128  killedNpc.CanDropLoot (false for some script-spawned boss adds) and not a keep guard / gamebot / pet
    - :1691  the loot is LootMgr.GetLoot(mob, killer); :1694 QuestLootGate165 then throws away quest items
             (PackageID "ClassicQuest" or id "cq_...") unless a loot owner is on a quest that names the item
             (mode165/QuestLootGate165.cs:27-43).  The item was already picked, so it still used up a pick.
 B. Which generators?  gameutils/LootMgr.cs:433-492 + table LootGenerator: three global ones, none exclusive:
    LootGeneratorMoney, LootGeneratorOneTimeDrop, LootGeneratorTemplate.  Their lists are merged
    (LootMgr.cs:413-417, LootList.AddAll LootList.cs:73-87: random entries pooled, DropCount = the largest).
 C. LootGeneratorTemplate (gameutils/LootGeneratorTemplate.cs)
    - :52-161 tables are keyed by lower-case name; inside one LootTemplate the first row per item id wins;
              a row whose item does not exist is ignored.  Rows are read in table order (SqlObjectDatabase.cs:558,
              "SELECT ... FROM table" with no ORDER BY = rowid order in SQLite).
    - :262-273 the rows of MobXLootTemplate for the monster: by NPC template id (no such rows exist in this
              world: asserted), else by lower-case monster name.
    - :283-316 NO MobXLootTemplate row: the monster's own LootTemplate; Chance == 100 -> always, Count copies;
              anything else -> random entry.  DropCount stays at the LootList default 1 (LootList.cs:44).
    - :400-452 + :573-614 WITH rows: per row, the table it names (the monster's own table is read once, :426-431;
              a row naming a missing table is skipped when another row names the monster's own table, :420-425).
              Chance == 100 -> always (Count copies, or the row's DropCount when Count <= 0, :596-603);
              else random entry and DropCount = max(DropCount, row.DropCount) (:607), never below 1.
    - LootGeneratorBase.cs:72-80 an item with a realm drops only for a loot owner of that realm
              (allow_cross_realm_items is False in this world).  It is filtered BEFORE the roll, so it does not
              compete for a pick when the killer is of another realm.
    - negative chances ("timed XP item") do not exist in this world: asserted.
 D. The roll (gameutils/LootList.cs:93-155)
    - every random entry passes when  Chance >= Util.Random(1, 100)  (Util.cs:27, inclusive) = Chance %;
    - then up to DropCount of the entries that passed are taken, each pick uniform among those left, no repeats.
    So  P(item) = p * E[ min(1, DropCount / (1 + number of OTHER entries that passed)) ].
 E. Coin (gameutils/LootGeneratorMoney.cs:24-42, mode165/Coin165.cs:29-43): chance = coin165.json by monster name,
    else by body type (clamped 0..100, Util.Chance(int) = chance > Next(100)); amount for level L:
    min = 2 + ((L+1)^3 >> 3), plus Util.Random(min >> 1), times money_drop (1).
 F. One-time drops (gameutils/LootGeneratorOneTimeDrop.cs:133-216): for each player that damaged the monster,
    each LootOTD row of that monster name with MinLevel <= player level is put straight into the backpack, once
    per character.  Not a roll, not part of the pool.  A row is loaded only if a Mob row has that name (:59).
"""
import argparse, json, math, os, re, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import wgcommon as wg  # noqa: E402

REALMS = (1, 2, 3)
QUEST_PACKAGE = "ClassicQuest"                      # QuestLootGate165.cs:22
QUEST_ID = re.compile(r"cq_[A-Za-z0-9_]+")          # QuestLootGate165.cs:23
BODY_NAMES = ["None", "Animal", "Demon", "Dragon", "Elemental", "Giant", "Humanoid", "Insect", "Magical",
              "Reptile", "Plant", "Undead"]         # Coin165.cs:19-20


class Broken(Exception):
    """An assumption this check relies on is no longer true for the data; the rules must be re-read."""


# ----------------------------------------------------------------------------------------------- the model
def load_model(con):
    m = {}
    items = {}
    for r in con.execute("select Id_nb, Name, Realm, PackageID, PackSize, MaxCount, Level from ItemTemplate"):
        items[r["Id_nb"].lower()] = dict(id=r["Id_nb"], name=r["Name"], realm=r["Realm"] or 0,
                                         package=r["PackageID"] or "", pack=r["PackSize"] or 0,
                                         maxcount=r["MaxCount"] or 0, level=r["Level"] or 0)
    m["items"] = items

    # C :52-111  LootTemplate, table order, first row per item wins, missing items ignored
    templates, missing_items, dup_rows = {}, 0, 0
    for r in con.execute("select TemplateName, ItemTemplateID, Chance, Count from LootTemplate order by rowid"):
        t = templates.setdefault(r["TemplateName"].lower(), {})
        iid = r["ItemTemplateID"].lower()
        if iid not in items:
            missing_items += 1
            continue
        if iid in t:
            dup_rows += 1
            continue
        t[iid] = (int(r["Chance"]), int(r["Count"]))
        if r["Chance"] < 0:
            raise Broken("LootTemplate %s / %s has a negative chance (timed XP item): rule C must be extended"
                         % (r["TemplateName"], r["ItemTemplateID"]))
    m["templates"], m["missing_items"], m["dup_rows"] = templates, missing_items, dup_rows

    # C :116-158  MobXLootTemplate, table order
    mxlt = {}
    for r in con.execute("select MobName, LootTemplateName, DropCount from MobXLootTemplate order by rowid"):
        mxlt.setdefault(r["MobName"].lower(), []).append((r["MobName"], r["LootTemplateName"], int(r["DropCount"])))
    m["mxlt"] = mxlt
    # C :262  a row keyed by an NPC template id would take priority over the name; there are none
    tids = {str(r[0]) for r in con.execute("select TemplateId from NpcTemplate")}
    clash = sorted(k for k in mxlt if k in tids)
    if clash:
        used = [k for k in clash if con.execute("select 1 from Mob where NPCTemplateID = ? limit 1", (int(k),)).fetchone()]
        if used:
            raise Broken("MobXLootTemplate rows keyed by NPC template id exist (%s): rule C :262 must be modelled" % used[:5])

    # B  generators
    gens = [(r["LootGeneratorClass"], r["ExclusivePriority"], r["MobName"], r["MobGuild"], r["MobFaction"], r["RegionID"])
            for r in con.execute("select * from LootGenerator")]
    want = {"DOL.GS.LootGeneratorMoney", "DOL.GS.LootGeneratorOneTimeDrop", "DOL.GS.LootGeneratorTemplate"}
    if {g[0] for g in gens} != want or any(g[1] or g[2] or g[3] or g[4] or g[5] for g in gens):
        raise Broken("LootGenerator table changed: %r (rule B assumes three global, non-exclusive generators)" % gens)
    prop = {r["Key"].lower(): r["Value"] for r in con.execute("select Key, Value from ServerProperty")}
    if str(prop.get("allow_cross_realm_items", "False")).lower() != "false":
        raise Broken("allow_cross_realm_items is on: the realm filter of rule C no longer applies")
    m["money_drop"] = float(prop.get("money_drop", "1") or 1)
    if str(prop.get("classic165_mode", "False")).lower() != "true":
        raise Broken("classic165_mode is not True in this world: Coin165 and QuestLootGate165 would be off")

    # A :1694  quest items and the quests that name them (QuestLootGate165.cs:57-91)
    quests_by_item = {}
    cols = [c[1] for c in con.execute("pragma table_info(DataQuest)") if "CHAR" in c[2].upper() or "TEXT" in c[2].upper()]
    for r in con.execute("select ID, %s from DataQuest" % ", ".join("`%s`" % c for c in cols)):
        for c in cols:
            v = r[c]
            if v and "cq_" in v.lower():
                for hit in QUEST_ID.findall(v):
                    quests_by_item.setdefault(hit.lower(), set()).add(int(r["ID"]))
    cq_path = wg.RUNTIME + "/server/classic-quests.json"
    m["classic_quests_file"] = os.path.exists(cq_path)
    if m["classic_quests_file"]:
        cq = json.load(open(cq_path, encoding="utf-8"))
        for qid, entry in (cq.get("Quests") or {}).items():
            text = json.dumps(entry)
            if "cq_" in text.lower():
                for hit in QUEST_ID.findall(text):
                    quests_by_item.setdefault(hit.lower(), set()).add(int(qid))
    m["quests_by_item"] = quests_by_item
    m["quest_names"] = {int(r["ID"]): r["Name"] for r in con.execute("select ID, Name from DataQuest")}

    # E  coin
    coin_path = wg.RUNTIME + "/true165/coin165.json"
    m["coin"] = json.load(open(coin_path, encoding="utf-8")) if os.path.exists(coin_path) else None

    # F  one-time drops
    mob_names = {r[0].lower() for r in con.execute("select distinct Name from Mob where Name is not null")}
    m["mob_names"] = mob_names
    otd = {}
    for r in con.execute("select MobName, ItemTemplateID, MinLevel from LootOTD order by rowid"):
        if r["MobName"].lower() not in mob_names or r["ItemTemplateID"].lower() not in items:
            continue                                   # LootGeneratorOneTimeDrop.cs:59-73
        otd.setdefault(r["MobName"].lower(), []).append((items[r["ItemTemplateID"].lower()]["id"], int(r["MinLevel"])))
    m["otd"] = otd
    return m


def is_quest_item(item):                                # QuestLootGate165.cs:27-29
    return item["package"] == QUEST_PACKAGE or item["id"].lower().startswith("cq_")


def can_receive(item, realm):                           # LootGeneratorBase.cs:72-80, cross-realm off
    return item["realm"] == 0 or item["realm"] == realm


def kill_plan(m, key, realm):
    """What LootGeneratorTemplate hands to LootList for one kill of monster `key` by a loot owner of `realm`.
    Returns (fixed: {item id lower: copies}, random: [(item id lower, chance)], drop_count)."""
    templates, items = m["templates"], m["items"]
    fixed, rnd, drop_count = {}, [], 1                  # LootList() default DropCount = 1  (LootList.cs:44)
    rows = m["mxlt"].get(key)
    if rows is None:                                    # :283-316
        for iid, (chance, count) in templates.get(key, {}).items():
            if not can_receive(items[iid], realm):
                continue
            if chance == 100:
                if count > 0:
                    fixed[iid] = fixed.get(iid, 0) + count
            else:
                rnd.append((iid, chance))
        return fixed, rnd, drop_count
    has_own = any(t.lower() == n.lower() for n, t, _ in rows)          # :409-410
    own_read = False
    for mob_name, tname, dc in rows:                                     # :412-452
        template_name = tname if tname else mob_name
        if template_name.lower() not in templates:
            if has_own:
                continue
            template_name = mob_name
        if template_name.lower() == mob_name.lower():
            if own_read:
                continue
            own_read = True
        for iid, (chance, count) in templates.get(template_name.lower(), {}).items():   # :573-614
            if not can_receive(items[iid], realm):
                continue
            if chance == 100:
                n = count if count > 0 else dc
                if n > 0:
                    fixed[iid] = fixed.get(iid, 0) + n
            else:
                rnd.append((iid, chance))
                drop_count = max(drop_count, dc)
    return fixed, rnd, drop_count


def pass_probability(chance):
    """P(Chance >= Util.Random(1, 100))  (LootList.cs:129)."""
    return min(100, max(0, chance)) / 100.0


def exact(rnd, drop_count):
    """Rule D in closed form: [P(entry i is in the loot)] for each random entry."""
    p = [pass_probability(c) for _, c in rnd]
    n = len(p)
    if n <= drop_count:
        return p
    # distribution of the number of entries that pass, by a forward DP; the "others" distribution for entry i is
    # recomputed without i (O(n^2) each, n <= ~110) to avoid dividing by (1 - p) near 1.
    out = []
    for i in range(n):
        dist = [1.0]
        for j in range(n):
            if j == i:
                continue
            q = p[j]
            if q <= 0.0:
                continue
            nxt = [0.0] * (len(dist) + 1)
            for k, v in enumerate(dist):
                nxt[k] += v * (1.0 - q)
                nxt[k + 1] += v * q
            dist = nxt
        share = sum(v * min(1.0, drop_count / (k + 1.0)) for k, v in enumerate(dist))
        out.append(p[i] * share)
    return out


def simulate(rnd, drop_count, kills, rng):
    """Plays LootList.GetLoot() `kills` times with numpy; returns how often each random entry dropped."""
    import numpy as np
    n = len(rnd)
    if n == 0:
        return []
    chance = np.array([c for _, c in rnd], dtype=np.int32)
    passed = rng.integers(1, 101, size=(kills, n), dtype=np.int32) <= chance      # Chance >= Random(1, 100)
    if n <= drop_count:
        return passed.sum(axis=0).tolist()
    # "pick a uniform random survivor, remove it, DropCount times" == the DropCount smallest of iid uniform keys
    keys = rng.random((kills, n))
    keys[~passed] = 2.0
    kth = np.partition(keys, drop_count - 1, axis=1)[:, drop_count - 1]
    chosen = passed & (keys <= kth[:, None])
    return chosen.sum(axis=0).tolist()


def coin_chance(m, key, body_type):
    """Coin165.ChanceFor in percent; None when the body type decides and the caller did not give one."""
    coin = m["coin"]
    if coin is None:
        return 100
    if key in {k.lower(): 0 for k in coin["monsters"]}:
        return min(100, max(0, int({k.lower(): v for k, v in coin["monsters"].items()}[key])))
    if body_type is None:
        return None
    name = BODY_NAMES[body_type] if 0 <= body_type < len(BODY_NAMES) else "None"
    v = coin["body_type_default"].get(name)
    return 100 if v is None else min(100, max(0, int(v)))


def coin_amount(level, money_drop=1.0):
    """LootGeneratorMoney.cs:27-32 -> (min, max) copper for a monster of this level."""
    lvl = max(1, level + 1)
    lo = 2 + ((lvl * lvl * lvl) >> 3)
    return int(lo * money_drop), int((lo + (lo >> 1)) * money_drop)


def expected(m):
    """key -> {"dc": {realm: DropCount}, "items": {item id: {"realm", "quest", "fixed", "c": {realm: chance}}}}
    for every monster name that has a loot table. chance is per kill, for a loot owner of that realm."""
    out = {}
    keys = sorted(set(m["mxlt"]) | set(m["templates"]))
    for key in keys:
        entry, dcs, seen_plans = {}, {}, {}
        for realm in REALMS:
            fixed, rnd, dc = kill_plan(m, key, realm)
            sig = (tuple(sorted(fixed.items())), tuple(rnd), dc)
            if sig not in seen_plans:
                seen_plans[sig] = exact(rnd, dc)
            probs = seen_plans[sig]
            dcs[realm] = dc
            for iid, copies in fixed.items():
                e = entry.setdefault(iid, {"c": {}, "fixed": copies})
                e["c"][realm] = 1.0
            for (iid, _), pr in zip(rnd, probs):
                e = entry.setdefault(iid, {"c": {}, "fixed": 0})
                e["c"][realm] = pr
        if not entry:
            continue
        for iid, e in entry.items():
            it = m["items"][iid]
            e["realm"] = it["realm"]
            e["quest"] = is_quest_item(it)
            e["id"] = it["id"]
        out[key] = {"dc": dcs, "items": entry}
    return out


# ----------------------------------------------------------------------------------------------- self test
def enumerate_exact(rnd, drop_count):
    """Rule D by brute force: every pass / fail outcome of the pool, weighted. Only for small pools."""
    p = [pass_probability(c) for _, c in rnd]
    n = len(p)
    out = [0.0] * n
    for mask in range(1 << n):
        w, k = 1.0, 0
        for j in range(n):
            if mask >> j & 1:
                w *= p[j]
                k += 1
            else:
                w *= 1.0 - p[j]
        if w == 0.0 or k == 0:
            continue
        share = min(1.0, drop_count / k)       # each of the k winners is among the DropCount picked with this chance
        for j in range(n):
            if mask >> j & 1:
                out[j] += w * share
    return out


def enumeration_check(m, max_pool=12):
    """The closed formula against the brute-force enumeration for every kill plan with a pool of at most max_pool."""
    worst, plans, entries = 0.0, 0, 0
    for key in sorted(set(m["mxlt"]) | set(m["templates"])):
        seen = set()
        for realm in REALMS:
            fixed, rnd, dc = kill_plan(m, key, realm)
            sig = (tuple(rnd), dc)
            if not rnd or len(rnd) > max_pool or sig in seen:
                continue
            seen.add(sig)
            plans += 1
            a, b = exact(rnd, dc), enumerate_exact(rnd, dc)
            for x, y in zip(a, b):
                entries += 1
                worst = max(worst, abs(x - y))
    return plans, entries, worst


def monte_carlo(m, kills, seed, limit_seconds=600):
    """Every monster with a random pool, each distinct realm plan, `kills` kills. Returns (n entries, worst z, list)."""
    import numpy as np
    rng = np.random.default_rng(seed)
    t0, n_entries, worst, bad, plans = time.time(), 0, (0.0, None), [], 0
    for key in sorted(set(m["mxlt"]) | set(m["templates"])):
        seen = set()
        for realm in REALMS:
            fixed, rnd, dc = kill_plan(m, key, realm)
            sig = (tuple(rnd), dc)
            if not rnd or sig in seen:
                continue
            seen.add(sig)
            plans += 1
            probs = exact(rnd, dc)
            hits = simulate(rnd, dc, kills, rng)
            for (iid, ch), pr, h in zip(rnd, probs, hits):
                n_entries += 1
                f = h / kills
                sd = math.sqrt(max(pr * (1 - pr), 1e-12) / kills)
                z = abs(f - pr) / sd if pr not in (0.0, 1.0) else (0.0 if f == pr else 99.0)
                if z > worst[0]:
                    worst = (z, (key, realm, iid, ch, dc, len(rnd), pr, f))
                if z > 5.0:
                    bad.append((key, realm, iid, pr, f, z))
        if time.time() - t0 > limit_seconds:
            raise Broken("Monte Carlo ran over %d s" % limit_seconds)
    return n_entries, plans, worst, bad, time.time() - t0


# ----------------------------------------------------------------------------------------------- drops.js
def read_provide(path):
    """A data file is  WG.provide("name", <json>);  -> the JSON value."""
    s = open(path, encoding="utf-8").read().strip()
    a = s.index(",", s.index("WG.provide(")) + 1
    b = s.rindex(")")
    return json.loads(s[a:b])


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--db", default=wg.DEFAULT_DB)
    ap.add_argument("--out", default=os.path.join(wg.ROOT, "data"))
    ap.add_argument("--kills", type=int, default=20000)
    ap.add_argument("--seed", type=int, default=165)
    ap.add_argument("--dump", default="")
    ap.add_argument("--no-mc", action="store_true")
    ap.add_argument("--tol", type=float, default=0.0005, help="largest accepted difference in chance (0..1)")
    ap.add_argument("--summary", default="", help="write a JSON summary here (used by build_all.py)")
    a = ap.parse_args()

    con = wg.open_db(a.db)
    try:
        m = load_model(con)
    except Broken as e:
        print("BROKEN ASSUMPTION: %s" % e)
        return 2
    exp = expected(m)
    n_items = sum(len(v["items"]) for v in exp.values())
    spawned = sum(1 for k in exp if k in m["mob_names"])
    print("my model: %d monster names with loot (%d of them have a Mob row), %d monster-item pairs"
          % (len(exp), spawned, n_items))
    print("          %d LootTemplate rows ignored (item missing), %d duplicate item rows ignored"
          % (m["missing_items"], m["dup_rows"]))
    realm_dep = sum(1 for v in exp.values() for e in v["items"].values()
                    if len(e["c"]) == 3 and max(e["c"].values()) - min(e["c"].values()) > 1e-9)
    print("          %d pairs whose chance depends on the killer's realm (realm items leave the pool for others)" % realm_dep)
    summary = {"names": len(exp), "names_spawned": spawned, "pairs": n_items, "realm_dependent_pairs": realm_dep}

    if a.dump:
        os.makedirs(os.path.dirname(os.path.abspath(a.dump)), exist_ok=True)
        json.dump({k: {"dc": v["dc"], "items": {e["id"]: {"c": e["c"], "fixed": e["fixed"], "realm": e["realm"],
                                                          "quest": e["quest"]} for e in v["items"].values()}}
                   for k, v in exp.items()}, open(a.dump, "w", encoding="utf-8"), indent=0)
        print("dumped my table to %s" % a.dump)

    ok = True
    plans, entries, worst_enum = enumeration_check(m)
    good = worst_enum < 1e-12
    ok = ok and good
    print("%s: the closed formula equals a brute-force enumeration of every roll outcome for the %d kill plans with a pool "
          "of 12 or fewer (%d entries); largest difference %.1e" % ("PASS" if good else "FAIL", plans, entries, worst_enum))
    summary["enumeration"] = {"plans": plans, "entries": entries, "largest_difference": worst_enum}
    if not a.no_mc:
        n, plans, worst, bad, secs = monte_carlo(m, a.kills, a.seed)
        print("Monte Carlo (seed %d, %d kills each): %d plans, %d entries in %.0f s; worst z = %.2f %s"
              % (a.seed, a.kills, plans, n, secs, worst[0], "" if worst[1] is None else
                 "(%s, realm %d, %s: chance %d, DropCount %d, pool %d, formula %.5f, simulated %.5f)" % worst[1]))
        summary["mc"] = {"kills": a.kills, "seed": a.seed, "plans": plans, "entries": n, "worst_z": round(worst[0], 2)}
        if bad:
            ok = False
            print("FAIL: formula and simulation disagree (z > 5) for %d entries:" % len(bad))
            for b in bad[:20]:
                print("   %s realm %d %s formula %.5f simulated %.5f z %.1f" % b)
        else:
            print("PASS: the closed formula matches the simulated server procedure")

    path = os.path.join(a.out, "drops.js")
    if not os.path.exists(path):
        print("FAIL: %s does not exist yet: nothing to compare" % path)
        ok = False
    else:
        import verify_drops_compare as cmp_mod
        m["db_path"], m["out_dir"] = a.db, a.out
        res = cmp_mod.compare(m, exp, read_provide(path), a.tol)
        summary["compare"] = res["summary"]
        for line in res["lines"]:
            print(line)
        ok = ok and res["ok"]
    if a.summary:
        json.dump(summary, open(a.summary, "w", encoding="utf-8"), indent=1)
    print("verify_drops: %s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Broken as e:
        print("BROKEN ASSUMPTION: %s" % e)
        sys.exit(2)
