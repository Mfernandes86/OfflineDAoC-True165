"""Compares data/drops.js (shape: source/contract/drops.md, lane D-D) with the verifier's own model
(source/verify_drops.py). Imported by verify_drops.py; not run on its own.

Compared, for every monster name with a loot table and every item on it:
  the chance for a killer of each realm (column `byRealm`, or `chance` when the three are the same), the headline
  `chance` (item's realm if realm-locked, else the kind's `realm`), the pieces `n`, the flags (always / quest /
  never / realm-locked), the quest ids, the kind's DropCount `dc`, pool size and loot realm;
then the one-time drops, the coin chance, and the inverted `byItem` index.
"""
import verify_world as vw
from verify_drops import REALMS, is_quest_item, kill_plan

F_ALWAYS, F_QUEST, F_NEVER, F_REALM = 1, 2, 4, 8


def stack(item):
    """AbstractServerRules.cs:1750-1751: a drop of an item with PackSize > 1 (and MaxCount >= PackSize) is a stack."""
    return item["pack"] if item["pack"] > 1 and item["maxcount"] >= item["pack"] else 1


def compare(m, exp, data, tol):
    lines, ok = [], True

    def add(good, text, details=()):
        nonlocal ok
        ok = ok and bool(good)
        lines.append("%s: %s" % ("PASS" if good else "FAIL", text))
        for d in list(details)[:25]:
            lines.append("      " + str(d))

    tol = max(tol if tol < 0.0004 else 2e-6, 2e-6)       # the file keeps six decimals
    mobs, unspawned = data.get("mobs", {}), data.get("unspawned", {})
    theirs = {}
    for src, flag in ((mobs, 0), (unspawned, 1)):
        for k, kind in src.items():
            if "d" in kind:
                theirs[k] = kind
    both_places = sorted(set(mobs) & set(unspawned))
    add(not both_places, "no kind is in both `mobs` and `unspawned` (%d are)" % len(both_places), both_places)

    only_mine = sorted(set(exp) - set(theirs))
    only_theirs = sorted(set(theirs) - set(exp))
    add(not only_mine and not only_theirs,
        "monster names with a loot table: mine %d, drops.js %d, only mine %d, only drops.js %d"
        % (len(exp), len(theirs), len(only_mine), len(only_theirs)), only_mine[:10] + only_theirs[:10])

    pairs = diffs = 0
    worst = (0.0, None)
    bad_ch, bad_n, bad_flags, bad_q, bad_kind, missing, extra = [], [], [], [], [], [], []
    for key in sorted(set(exp) & set(theirs)):
        kind, mine = theirs[key], exp[key]
        rows = {}
        for r in kind["d"]:
            if str(r[0]).lower() in rows:
                extra.append("%s: %s twice" % (key, r[0]))
            rows[str(r[0]).lower()] = r
        for iid in mine["items"]:
            if iid not in rows:
                missing.append("%s: %s is not in drops.js" % (key, mine["items"][iid]["id"]))
        for iid in rows:
            if iid not in mine["items"]:
                extra.append("%s: %s is in drops.js but not on the monster's table" % (key, rows[iid][0]))
        kr = kind.get("realm", 0)
        # the kind's loot realm, DropCount and pool as the file defines them
        locked = [m["items"][i]["realm"] for i in mine["items"] if m["items"][i]["realm"]]
        if locked:
            best = max(set(locked), key=lambda x: (locked.count(x), -x))
            top = [x for x in set(locked) if locked.count(x) == locked.count(best)]
            if kr not in top:
                bad_kind.append("%s: realm %s, but its realm-locked items are mostly realm %s" % (key, kr, top))
        elif kr != 0:
            bad_kind.append("%s: realm %s, but no item on the table is realm-locked" % (key, kr))
        ref = kr if kr in REALMS else 1
        fixed, rnd, dc = kill_plan(m, key, ref)
        if kind.get("dc") != dc:
            bad_kind.append("%s: dc %s, mine %s" % (key, kind.get("dc"), dc))
        if kind.get("pool") != len(rnd):
            bad_kind.append("%s: pool %s, mine %s (killer realm %s)" % (key, kind.get("pool"), len(rnd), ref))
        for iid, e in mine["items"].items():
            r = rows.get(iid)
            if r is None:
                continue
            pairs += 1
            it = m["items"][iid]
            if r[0] != it["id"]:
                bad_flags.append("%s: item id spelled %r, the item table says %r" % (key, r[0], it["id"]))
            by_realm = r[4] if len(r) > 4 and isinstance(r[4], list) else None
            my_c = [e["c"].get(realm, 0.0) for realm in REALMS]
            their_c = by_realm if by_realm else [r[1]] * 3
            if len(their_c) != 3:
                bad_ch.append("%s / %s: byRealm has %d entries" % (key, r[0], len(their_c)))
                continue
            d = max(abs(a - b) for a, b in zip(my_c, their_c))
            head_realm = it["realm"] if it["realm"] else ref
            head = e["c"].get(head_realm, 0.0)
            d = max(d, abs(head - r[1]))
            if d > worst[0]:
                worst = (d, "%s / %s: mine %s head %.6f, drops.js %s head %s" % (key, r[0], ["%.6f" % x for x in my_c], head, their_c, r[1]))
            if d > tol:
                diffs += 1
                bad_ch.append("%s / %s: mine [alb, mid, hib] %s head %.6f; drops.js %s head %s"
                              % (key, r[0], ["%.6f" % x for x in my_c], head, their_c, r[1]))
            want_n = stack(it) * (e["fixed"] if e["fixed"] > 1 else 1)
            if r[2] != want_n:
                bad_n.append("%s / %s: n %s, mine %s (PackSize %s, MaxCount %s, fixed copies %s)"
                             % (key, r[0], r[2], want_n, it["pack"], it["maxcount"], e["fixed"]))
            quests = sorted(m["quests_by_item"].get(iid, ())) if e["quest"] else []
            want = (F_ALWAYS if e["fixed"] else 0) | (F_REALM if it["realm"] else 0)
            if e["quest"]:
                want |= F_QUEST if quests else F_NEVER
            if r[3] != want:
                bad_flags.append("%s / %s: flags %s, mine %s" % (key, r[0], r[3], want))
            their_q = sorted(r[5]) if len(r) > 5 and isinstance(r[5], list) else []
            if their_q != quests:
                bad_q.append("%s / %s: quests %s, mine %s" % (key, r[0], their_q, quests))
    add(not missing and not extra, "items per monster: %d pairs compared, %d missing from drops.js, %d extra"
        % (pairs, len(missing), len(extra)), missing[:12] + extra[:12])
    add(not bad_ch, "chance per kill for a killer of each realm and the headline chance: %d pairs, %d differ by more "
        "than %.6f; largest difference %.7f%s" % (pairs, diffs, tol, worst[0], "" if not worst[1] or worst[0] == 0 else " (" + worst[1] + ")"), bad_ch)
    add(not bad_kind, "DropCount, pool size and loot realm of %d kinds: %d differ" % (len(set(exp) & set(theirs)), len(bad_kind)), bad_kind)
    add(not bad_n, "pieces per drop (stack size): %d differ" % len(bad_n), bad_n)
    add(not bad_flags, "flags (always / quest / never / realm-locked) and item id spelling: %d differ" % len(bad_flags), bad_flags)
    add(not bad_q, "quest ids of quest-gated items: %d differ" % len(bad_q), bad_q)

    # spawned or not: `mobs` must be exactly the runtime names that have a spawn in an open region
    import wgcommon as wg
    con = wg.open_db(m.get("db_path", wg.DEFAULT_DB))
    sps = vw.spawns(con)
    names = set()
    for s in sps:
        for nme in s["names"]:
            names.add(nme.lower())
    # Script-spawned kinds (boss summons, encounter and quest event spawns) have no Mob row. drops.js marks them
    # `s: 1` and takes them from the kinds lane: they must be kinds of mobs.js and must NOT be a spawn name.
    import os
    script_kinds, kind_body, kind_cls = set(), {}, {}
    mobs_js = os.path.join(m.get("out_dir") or os.path.join(wg.ROOT, "data"), "mobs.js")
    if os.path.exists(mobs_js):
        from verify_drops import read_provide
        mobs_data = read_provide(mobs_js)
        for table in ("scripted", "kinds"):            # `scripted` = the script-made kinds M-D lists apart (no Mob row, no camps)
            kt = mobs_data.get(table)
            if not kt:
                continue
            ci = {c: i for i, c in enumerate(kt["cols"])}
            for r in kt["rows"]:
                key = r[ci["key"]]
                if key not in names:
                    script_kinds.add(key)
                if "body" in ci and key not in kind_body:
                    kind_body[key] = r[ci["body"]]         # an int (0 = "None") or null = not known
                if "cls" in ci and key not in kind_cls:
                    kind_cls[key] = r[ci["cls"]] or []
    flagged = {k for k, v in mobs.items() if v.get("s")}
    bad = sorted("%s: marked s but a spawn in an open region has that name" % k for k in flagged & names)
    bad += sorted("%s: marked s but not a kind in mobs.js" % k for k in flagged - script_kinds - names)
    bad += sorted("%s: a kind of mobs.js with no spawn row, but drops.js does not mark it s under `mobs`" % k for k in script_kinds - flagged)
    add(not bad, "script-spawned kinds (`s`): %d in drops.js, %d kinds of mobs.js have no spawn row; %d wrong"
        % (len(flagged), len(script_kinds), len(bad)), bad)
    world = names | flagged
    wrong_side = sorted(k for k in theirs if (k in mobs) != (k in world))
    add(not wrong_side, "a kind is under `mobs` exactly when a spawn in an open region has that name at run time, or it is a "
        "script-spawned kind: %d wrong (my world: %d spawn names, drops.js mobs: %d)" % (len(wrong_side), len(names), len(mobs)),
        ["%s: in %s, my world %s it" % (k, "mobs" if k in mobs else "unspawned", "has" if k in world else "does not have") for k in wrong_side])
    not_mine = sorted(set(mobs) - world)
    not_theirs = sorted(names - set(mobs))
    add(not not_mine and not not_theirs, "kinds: every spawned name is a key of `mobs` and the other way round: "
        "%d only in drops.js, %d only in my world" % (len(not_mine), len(not_theirs)), not_mine[:8] + not_theirs[:8])

    # one-time drops
    bad = []
    all_kinds = dict(unspawned)
    all_kinds.update(mobs)
    their_otd = {k: sorted((str(i).lower(), lv) for i, lv in v["otd"]) for k, v in all_kinds.items() if v.get("otd")}
    my_otd = {k: sorted((i.lower(), lv) for i, lv in v) for k, v in m["otd"].items()}
    for k in sorted(set(their_otd) | set(my_otd)):
        if their_otd.get(k) != my_otd.get(k):
            bad.append("%s: drops.js %s, mine %s" % (k, their_otd.get(k), my_otd.get(k)))
    add(not bad, "one-time drops: %d monsters, %d items; %d differ"
        % (len(my_otd), sum(len(v) for v in my_otd.values()), len(bad)), bad)

    # coin
    bad, n = [], 0
    bodies = {}
    for s in sps:
        for v in s["variants"]:
            bodies.setdefault(v["name"].lower(), set()).add(v["body"])
    coin = m["coin"]
    measured = {k.lower(): min(100, max(0, int(v))) for k, v in coin["monsters"].items()} if coin else {}
    from verify_drops import BODY_NAMES
    for k, kind in mobs.items():
        if kind.get("s") and k not in measured and kind_body.get(k) is None:
            if "coin" in kind and coin is not None:      # no measured rate and no known body type: no coin field
                bad.append("%s: script-spawned, no measured rate and no body type in mobs.js, but coin %s" % (k, kind["coin"]))
            continue
        if "coin" not in kind:
            bad.append("%s: no coin field" % k)
            continue
        n += 1
        if coin is None:
            want, cm = 100, False
        elif k in measured:
            want, cm = measured[k], True
        else:
            vals = set()
            for b in (bodies.get(k, {0}) if not kind.get("s") else {kind_body[k]}):
                nm = BODY_NAMES[b] if 0 <= b < len(BODY_NAMES) else "None"
                v = coin["body_type_default"].get(nm)
                vals.add(100 if v is None else min(100, max(0, int(v))))
            want, cm = (min(vals) if len(vals) == 1 else [min(vals), max(vals)]), False
        if kind["coin"] != want or bool(kind.get("cm")) != cm:
            bad.append("%s: coin %s cm %s, mine %s measured %s (body types %s)" % (k, kind["coin"], kind.get("cm"), want, cm, sorted(bodies.get(k, []))))
    add(not bad, "coin chance of %d spawned kinds (coin165.json by name, else by body type): %d differ" % (n, len(bad)), bad)
    amount = (data.get("meta", {}).get("coin", {}) or {})
    add(abs(float(amount.get("moneyDrop", -1)) - m["money_drop"]) < 1e-9, "coin amount multiplier money_drop = %s" % m["money_drop"])

    # quest hand-overs (not loot: ClassicQuests.cs:742-779 DropForSteps, and the side turn-ins)
    import json as _json, os as _os, re as _re
    cq_path = wg.RUNTIME + "/server/classic-quests.json"
    if _os.path.exists(cq_path):
        cq = _json.load(open(cq_path, encoding="utf-8"))
        mine_q = {}
        for qid, q in (cq.get("Quests") or {}).items():
            for i, st in enumerate(q.get("Steps") or []):
                for d in (((st or {}).get("Custom") or {}).get("Drops") or []):
                    e = mine_q.setdefault((d["Mob"].lower(), d["Item"].lower(), int(qid)), [1.0, set()])
                    e[0] = min(e[0], min(100, max(0, d.get("Chance", 0))) / 100.0)      # Util.Chance(int)
                    e[1].add(i)
            for sd in q.get("SideTurnIns") or []:
                src = sd.get("DropFrom")
                for nme in ([src] if isinstance(src, str) else (src or [])):
                    e = mine_q.setdefault((nme.lower(), sd["Item"].lower(), int(qid)), [1.0, set()])
                    e[0] = min(e[0], min(100, max(0, sd.get("DropChance", 0))) / 100.0)
        their_q = {}
        for k, v in all_kinds.items():
            for r in v.get("q") or []:
                their_q[(k, str(r[0]).lower(), r[2])] = [r[1], set(r[3])]
        bad = ["%s: drops.js %s, classic-quests.json %s" % (k, their_q.get(k), mine_q.get(k))
               for k in sorted(set(mine_q) | set(their_q))
               if k not in mine_q or k not in their_q or abs(mine_q[k][0] - their_q[k][0]) > 1e-9
               or (mine_q[k][1] and mine_q[k][1] != their_q[k][1])]
        add(not bad, "quest hand-overs on a kill (`q`): %d monster / item / quest entries, %d differ" % (len(mine_q), len(bad)), bad)

    # spawn rows that never drop (`none`): keep guards (AbstractServerRules.cs:1683) and classes that turn loot off
    parents, no_loot = {}, set()
    for dp, _, fn in _os.walk(wg.SERVER_SRC + "/GameServer"):
        for f in fn:
            if f.endswith(".cs"):
                src = open(_os.path.join(dp, f), encoding="utf-8", errors="replace").read()
                for mm in _re.finditer(r"\bclass\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([\w.]+)", src):
                    parents.setdefault(mm.group(1), mm.group(2).split(".")[-1])
                if "CanDropLoot" in src:
                    cur = None
                    for line in src.splitlines():
                        mm = _re.search(r"\bclass\s+(\w+)", line)
                        if mm:
                            cur = mm.group(1)
                        if _re.search(r"override\s+bool\s+CanDropLoot\s*=>\s*false", line) and cur:
                            no_loot.add(cur)

    def never_drops(cls):
        c, seen = (cls or "").split(".")[-1], set()
        while c and c not in seen:
            if c == "GameKeepGuard" or c in no_loot:
                return True
            seen.add(c)
            c = parents.get(c)
        return False
    mine_none = {}
    for s in sps:
        for v in s["variants"]:
            e = mine_none.setdefault(v["name"].lower(), [0, 0])
            e[1] += 1
            if never_drops(s["eff_cls"]):
                e[0] += 1
    bad = []
    for k in sorted(mobs):
        got = mobs[k].get("none")
        if mobs[k].get("s"):                           # no spawn row: counted over the classes the kinds lane lists for it
            cl = kind_cls.get(k, [])
            want = [sum(1 for c in cl if never_drops(c)), len(cl)]
            if not want[0]:
                want = [0, 0]
        else:
            want = mine_none.get(k, [0, 0])
        if (got[0] if got else 0) != want[0] or (got and got[1] != want[1]):
            bad.append("%s: drops.js none %s, mine %s of %s rows" % (k, got, want[0], want[1]))
    add(not bad, "spawn rows that never drop anything (keep guards, %d classes with loot turned off): %d kinds have such rows, %d differ"
        % (len(no_loot), sum(1 for v in mine_none.values() if v[0]), len(bad)), bad)

    # `raid`: scripted epic encounters (RealmRaidLootOwner.cs:27-32: the class implements IGameEpicNpc and is not
    # GameEpicNPC or GameEpicDungeonNPC itself). It changes who receives the loot when a bot expedition helps, not a chance.
    bases = {}
    for dp, _, fn in _os.walk(wg.SERVER_SRC + "/GameServer"):
        for f in fn:
            if f.endswith(".cs"):
                src = open(_os.path.join(dp, f), encoding="utf-8", errors="replace").read()
                for mm in _re.finditer(r"class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([^{\n]+)", src):
                    bases.setdefault(mm.group(1), [b.strip().split(".")[-1].split("<")[0] for b in mm.group(2).split(",")])

    def epic(cls):
        c0 = (cls or "").split(".")[-1]
        if c0 in ("GameEpicNPC", "GameEpicDungeonNPC"):
            return False
        todo, seen = [c0], set()
        while todo:
            c = todo.pop()
            if c in seen:
                continue
            seen.add(c)
            if c == "IGameEpicNpc":
                return True
            todo.extend(bases.get(c, []))
        return False
    mine_raid = {}
    for s in sps:
        for v in s["variants"]:
            e = mine_raid.setdefault(v["name"].lower(), [0, 0])
            e[1] += 1
            if epic(s["eff_cls"]):
                e[0] += 1
    bad, n_raid = [], 0
    for k in sorted(mobs):
        if mobs[k].get("s"):
            continue                                   # no spawn row: the class comes from the kinds lane, not checked here
        got = mobs[k].get("raid")
        want = mine_raid.get(k, [0, 0])
        got_n = 0 if not got else (want[1] if got == 1 else got[0])
        n_raid += bool(want[0])
        if got_n != want[0]:
            bad.append("%s: drops.js raid %s, mine %s of %s rows" % (k, got, want[0], want[1]))
    add(not bad, "scripted epic encounters (`raid`): %d spawned kinds have such rows, %d differ" % (n_raid, len(bad)), bad)

    # byItem is the same table turned round
    bad, n = [], 0
    inv = {}
    for key, kind in theirs.items():
        for r in kind["d"]:
            inv.setdefault(r[0], {})[key] = (r[1], r[3], 1 if key in unspawned and key not in mobs else 2 if kind.get("s") else 0)
    by_item = data.get("byItem", {})
    for iid in sorted(set(inv) | set(by_item)):
        a = inv.get(iid, {})
        b = {}
        for r in by_item.get(iid, []):
            b[r[0]] = (r[1], r[2], r[3] if len(r) > 3 else 0)
            n += 1
        if a != b:
            bad.append("%s: byItem %s, from the kinds %s" % (iid, sorted(b.items())[:3], sorted(a.items())[:3]))
    add(not bad, "byItem index: %d entries for %d items, same as the per-kind rows turned round; %d items differ"
        % (n, len(by_item), len(bad)), bad)

    summary = {"kinds": len(set(exp) & set(theirs)), "pairs": pairs, "pairs_differ": diffs,
               "largest_difference": worst[0], "tolerance": tol, "otd": sum(len(v) for v in my_otd.values()),
               "coin_kinds": n and len(mobs)}
    return {"ok": ok, "lines": lines, "summary": summary}
