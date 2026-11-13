"""OFFLINEDAOC Database - quests (lane Q).

    python -I source/build_quests.py [--db <world.sqlite3.db>] [--out <data dir>]

Writes data/quests.js: every quest a player can take in the True 1.65 world, with its steps, targets, kill-step
hand-overs and rewards.  Shape and field list: source/contract/quests.md.

Reads (all read-only): the world database (DataQuest, Mob, NpcTemplate, WorldObject, ItemTemplate, ServerProperty,
Zones, Regions), RUNTIME/server/classic-quests.json and classic-quest-guides.json, and three files of other lanes that
must be built first: data/zones.js (which zones exist in the tool), data/mobs.js (monster kind keys),
data/drops.js (quest-gated loot rows and where turn-in items drop; lane D-D's numbers are used as they are).

Server rules ported here (paths under GameServer/ of wgcommon.SERVER_SRC):
  gameobjects/GameObject.cs:737-806         a DataQuest row is attached to every object whose name equals StartName
                                            (case-insensitive) in region StartRegionID (0 = any region).
  quests/QuestsMgr/QuestRealmImpostors.cs   a hostile realm-0 NPC that shares its name with a realm NPC of its region
                                            gives no data quests.
  quests/QuestsMgr/DataQuest.cs:166-200     eStartType (0 Standard, 1 Collection) and eStepType (0 Kill ... 11 CollectFinish).
  DataQuest.cs:1007-1059                    who may take it: MinLevel..MaxLevel, AllowedClasses, the ClassType hook
                                            (ClassicQuestStep: Races, OnlyDuring, ExcludedBy, RequiresAnyOf), not already
                                            on it, done fewer than MaxCount times, QuestDependency met.
  quests/QuestsMgr/QuestDependencyRule.cs   which finished quest meets a dependency (the part just before, per class).
  DataQuest.cs:1583-1789 AdvanceQuestStep   a step gives its own StepItemTemplates item when it completes (not a delivery
                                            step); on a Kill / Search step "item;N" rolls Util.Chance(N) first and a
                                            failed roll means the kill does not count; the next step's item is handed
                                            over at the same moment when the next step is a delivery.
  DataQuest.cs:2893-2928 OnEnemyKilled      a kill counts when the dead monster's name equals TargetName (any case) and
                                            its region equals the target region (0 = any).
  DataQuest.cs:2485-2511 CollectCountNeeded how many of its item a hand-in step asks for.
  DataQuest.cs:2246-2375                    Collection quests: one item handed to the giver = RewardXP (True 1.65: the
                                            fixed number; 0 = experience needed for the level / 50), MaxCount times.
  DataQuest.cs:3067-3336 FinishQuest        final rewards (the player's class version, ClassicQuests.RewardFor), the
                                            last step's RewardXP / RewardMoney.
  quests/QuestsMgr/ClassicQuests.cs:99-184  the per-step extras of classic-quests.json (Custom steps, Drops, Spawn,
                                            Marker, NeedsItem, Grant, Wearing, OwnSpawnOnly), per-quest Races,
                                            XpShares, OnlyDuring, ExcludedBy, RequiresAnyOf, CollectRewards, SideTurnIns.
  ClassicQuests.cs:742-779 DropForSteps     a monster listed in a step's Drops gives the item with Util.Chance(Chance)
                                            to a player on that step; on a kill step whose own item it is, another
                                            monster counts like the target.
  ClassicQuests.cs:857-864 ScaleXP          step experience = max(fixed RewardXP, XpShares[step] x the experience of
                                            the player's current level); fixed at level 50.
  ClassicQuests.Features07.cs:37-48, 247-274  side turn-ins and their drops.
  gameobjects/GamePlayer.cs:4196-4249       XPForLevel (experience needed for each level).
  mode165/Classic165Mode.cs:48-75, events/GameEventMgr.cs:115-126
                                            freeshard scripts do not start in True 1.65: bounty masters, faction
                                            emissaries (reputation hunts), daily quests, Sluaghbinder.
  mode165/Epics165.cs                       the level 50 epic finales and their prerequisite part.
The scripted quests (11 epic finales, 7 others) cannot be read from a table: their facts were read from the C# by hand
and are kept at the end of this file (SCRIPTED_JSON), each with file:line; the builder checks them against the source
and the world on every run.
"""
import json, os, re, sys, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wgcommon as wg

STEP_KIND = {0: "kill", 1: "kill", 2: "deliver", 3: "deliver", 4: "talk", 5: "talk", 6: "whisper", 7: "whisper",
             8: "search", 9: "search", 10: "collect", 11: "collect"}
PEACE = 0x10
XP_TABLE_FILE = "GameServer/gameobjects/GamePlayer.cs"


def load_js(out_dir, name):
    """Read another lane's data/<name>.js (WG.provide("<name>", <json>);)."""
    path = os.path.join(out_dir, name + ".js")
    if not os.path.exists(path):
        path = os.path.join(wg.ROOT, "data", name + ".js")
    assert os.path.exists(path), "data/%s.js is missing: build it before quests (build_all order)" % name
    text = open(path, encoding="utf-8").read()
    head = 'WG.provide(%s,' % json.dumps(name)
    assert text.startswith(head), "data/%s.js does not start with %s" % (name, head)
    return json.loads(text[len(head):text.rstrip().rfind(")")])


def split_ranges(s):
    out = set()
    for part in (s or "").replace(",", ";").split(";"):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            a, b = part.split("-", 1)
            out.update(range(int(a), int(b) + 1))
        else:
            out.add(int(part))
    return out


def parse_classes(s):
    """QuestDependencyRule.ParseClasses: ids > 0 separated by | ; ,"""
    out = []
    for part in re.split(r"[|;,]", s or ""):
        part = part.strip()
        if part.isdigit() and int(part) > 0:
            out.append(int(part))
    return out


def xp_for_level(src_root):
    """GamePlayer.XPForLevel: the list of 51 numbers (index = level - 1 -> experience needed to BE that level)."""
    text = open(os.path.join(src_root, XP_TABLE_FILE), encoding="utf-8-sig", errors="replace").read()
    i = text.index("private static readonly long[] XPForLevel")
    block = text[i:text.index("};", i)]
    nums = [int(n) for n in re.findall(r"^\s*(\d+),\s*// xp to level", block, re.M)]
    assert len(nums) == 51 and nums[0] == 0 and nums[49] == 169999999950, "XPForLevel table not understood"
    return nums


def is_part_of(row_name, line):
    """Epics165.IsPartOf: the line's name, or the line's name with ' (level N)'."""
    a, b = (row_name or "").lower(), (line or "").lower()
    if not a or not b:
        return False
    if a == b:
        return True
    m = re.fullmatch(re.escape(b) + r" \(level (\d+)\)", a)
    return bool(m)


def main():
    a = wg.args(__doc__)
    con = wg.open_db(a.db)
    out_dir = a.out
    left = collections.Counter()          # quests left out, by reason
    warn = collections.Counter()          # things counted, not fatal
    examples = collections.defaultdict(list)

    def note(counter, key, example=None):
        counter[key] += 1
        if example is not None and len(examples[key]) < 12:
            examples[key].append(example)

    # ---------------------------------------------------------------- world: zones, regions, spawns, items
    zones_js = load_js(out_dir, "zones")
    kept_zone_ids = {z["id"] for z in zones_js["zones"]}
    zone_realm = {z["id"]: z["realm"] for z in zones_js["zones"]}
    all_zones = wg.zones(con)
    zs_by_region = wg.zones_by_region({k: v for k, v in all_zones.items() if k in kept_zone_ids})
    prop = {r["Key"].lower(): r["Value"] for r in con.execute("select Key, Value from ServerProperty")}
    assert prop.get("classic165_mode", "").lower() == "true", "this world is not a True 1.65 world"
    assert prop.get("load_quests", "").lower() == "true", "load_quests is off: no quest could be taken"
    closed = split_ranges(prop.get("disabled_regions", ""))
    dis_exp = split_ranges(prop.get("disabled_expansions", ""))
    for r in con.execute("select RegionID, Expansion from Regions"):
        if (r["Expansion"] or 0) + 1 in dis_exp:
            closed.add(r["RegionID"])
    disabled_classes = split_ranges(prop.get("disabled_classes", ""))

    def place(region, x, y):
        """Region position -> (zone id, local x, local y) or None (CONTRACT section 6)."""
        if region in closed:
            return None
        z = wg.zone_of(zs_by_region, region, x, y)
        return (z["id"], int(x - z["ox"]), int(y - z["oy"])) if z else None

    tmpl = {}
    for t in con.execute("select TemplateId, Name, Flags, ClassType, ReplaceMobValues from NpcTemplate order by rowid"):
        tmpl.setdefault(t["TemplateId"], t)        # an id used twice: the first row (see mobs.md section 2)
    spawns_by_name = collections.defaultdict(list)   # lower runtime name -> spawns (open regions only)
    for m in con.execute("select Name, Region, X, Y, Z, Realm, Flags, NPCTemplateID, ClassType, Guild, Level from Mob"):
        if m["Region"] in closed:
            continue
        t = tmpl.get(m["NPCTemplateID"])
        use_t = bool(t is not None and t["ReplaceMobValues"])      # GameNPC.cs:1341-1345, 1416-1472
        name = (t["Name"] if use_t and t["Name"] else m["Name"]) or ""
        flags = (t["Flags"] if use_t else m["Flags"]) or 0
        p = place(m["Region"], m["X"], m["Y"])
        spawns_by_name[name.lower()].append(dict(
            name=name, region=m["Region"], X=m["X"], Y=m["Y"], Z=m["Z"], realm=m["Realm"] or 0, peace=bool(flags & PEACE),
            cls=(t["ClassType"] if use_t and t["ClassType"] and t["ClassType"] != "DOL.GS.GameNPC" else m["ClassType"]) or "",
            p=p, obj=False))
    for o in con.execute("select Name, Region, X, Y, Z, Realm, ClassType from WorldObject"):
        if o["Region"] in closed or not o["Name"]:
            continue
        spawns_by_name[o["Name"].lower()].append(dict(
            name=o["Name"], region=o["Region"], X=o["X"], Y=o["Y"], Z=o["Z"], realm=o["Realm"] or 0, peace=True,
            cls=o["ClassType"] or "", p=place(o["Region"], o["X"], o["Y"]), obj=True))
    realm_names = {(s["region"], k) for k, lst in spawns_by_name.items() for s in lst if s["realm"] in (1, 2, 3)}

    def impostor(s, key):
        """QuestRealmImpostors.CouldBe + Sweep."""
        return (not s["obj"]) and s["realm"] == 0 and not s["peace"] and (s["region"], key) in realm_names

    def find_npcs(name, region, quest_giver=False):
        key = (name or "").lower()
        out = [s for s in spawns_by_name.get(key, ()) if (region == 0 or s["region"] == region)]
        if quest_giver:
            out = [s for s in out if not impostor(s, key)]
        return out

    def nearest(cands, region, x, y):
        if not cands:
            return None
        placed = [s for s in cands if s["p"]] or cands
        if x is None:
            placed.sort(key=lambda s: (s["realm"] == 0, s["region"], s["X"], s["Y"]))
            return placed[0]
        return min(placed, key=lambda s: (s["region"] != region, (s["X"] - x) ** 2 + (s["Y"] - y) ** 2))

    mobs_js = load_js(out_dir, "mobs")
    kc = mobs_js["kinds"]["cols"]
    kind_id = {r[kc.index("key")]: r[kc.index("id")] for r in mobs_js["kinds"]["rows"]}
    kind_zones = {r[kc.index("key")]: {z[0] for z in r[kc.index("zones")]} for r in mobs_js["kinds"]["rows"]}
    drops_js = load_js(out_dir, "drops")
    meta_js = load_js(out_dir, "itemmeta")
    class_realm = {c["id"]: c["realm"] for c in meta_js["classes"]}
    class_base = {c["id"]: bool(c["isBase"]) for c in meta_js["classes"]}
    playable = {c for c in class_realm if c not in disabled_classes}

    items = {}
    for r in con.execute("select Id_nb, Name, Object_Type, Item_Type, Level, Realm, Price from ItemTemplate"):
        items[r["Id_nb"].lower()] = r
    used_items = {}

    def item_id(raw, where, qid):
        """Table spelling of an item id (case-insensitive key, DataTableHandler.cs:188-196), or None when missing."""
        raw = (raw or "").strip()
        if not raw:
            return None
        r = items.get(raw.lower())
        if r is None:
            note(warn, "item id not in ItemTemplate (%s)" % where, "%s: %s" % (qid, raw))
            return None
        used_items[r["Id_nb"]] = r["Name"]
        return r["Id_nb"]

    def is_gear(iid):
        r = items.get((iid or "").lower())
        return bool(r) and (r["Object_Type"] or 0) != 0

    xp_table = xp_for_level(wg.SERVER_SRC)

    script_cache = {}
    script_texts = []

    def script_named(name):
        """(file, class name, only at night) of the monster script that sets Body.Name / Name to this name, or None."""
        if name in script_cache:
            return script_cache[name]
        if not script_texts:
            root = os.path.join(wg.SERVER_SRC, "GameServer", "scripts")
            for d, _, files in os.walk(root):
                if os.sep + "quests" in d:
                    continue
                for fn in files:
                    if fn.endswith(".cs"):
                        script_texts.append((os.path.relpath(os.path.join(d, fn), os.path.join(wg.SERVER_SRC, "GameServer")).replace(os.sep, "/"),
                                             open(os.path.join(d, fn), encoding="utf-8-sig", errors="replace").read()))
        out = None
        for f, text in script_texts:
            if re.search(r'Name\s*=\s*"%s"' % re.escape(name), text):
                m = re.search(r"class\s+(\w+)\s*:\s*Game", text)
                out = (f, m.group(1) if m else "", "IsNightTime" in text)
                break
        script_cache[name] = out
        return out

    # ---------------------------------------------------------------- classic-quests.json
    cq = json.load(open(wg.RUNTIME + "/server/classic-quests.json", encoding="utf-8-sig"))
    cq_quests = {int(k): v for k, v in cq["Quests"].items()}
    class_rewards = {}
    for base, by_class in (cq.get("ClassRewards") or {}).items():
        b = item_id(base, "ClassRewards", "-")
        if not b:
            continue
        m = {}
        for cid, iid in by_class.items():
            i2 = item_id(iid, "ClassRewards", "-")
            if i2 and int(cid) in playable:
                m[cid] = i2
        class_rewards[b] = m
    hour_windows = {k.lower(): [v["Start"], v["End"]] for k, v in (cq.get("HourWindows") or {}).items()}
    guides = {k.lower() for k in json.load(open(wg.RUNTIME + "/server/classic-quest-guides.json",
                                                 encoding="utf-8-sig"))["Guides"]}
    REALM_NAME = {1: "albion", 2: "midgard", 3: "hibernia"}

    # ---------------------------------------------------------------- DataQuest rows
    rows = [dict(r) for r in con.execute("select * from DataQuest order by ID")]
    n_rows = len(rows)
    by_id = {r["ID"]: r for r in rows}
    rows_by_name = collections.defaultdict(list)
    for r in rows:
        r["cls"] = parse_classes(r["AllowedClasses"])
        rows_by_name[(r["Name"] or "").lower()].append(r)
    assert all(r["StartType"] in (0, 1) for r in rows), "a StartType other than Standard / Collection appeared"
    assert not any(r["OptionalRewardItemTemplates"] or r["RewardCLXP"] or r["RewardRP"] or r["RewardBP"] for r in rows), \
        "optional rewards / CLXP / RP / BP are now used by a row: port them"

    quests = {}
    for r in rows:
        qid = r["ID"]
        info = cq_quests.get(qid) or {}
        # ---- who gives it
        cands = find_npcs(r["StartName"], r["StartRegionID"], quest_giver=True)
        if not cands:
            why = "giver is not in the world"
            if find_npcs(r["StartName"], r["StartRegionID"]):
                why = "giver is a hostile monster named like a realm NPC (QuestRealmImpostors)"
            elif spawns_by_name.get((r["StartName"] or "").lower()):
                why = "giver stands in another region than StartRegionID"
            note(left, why, "%d %s (%s, region %d)" % (qid, r["Name"], r["StartName"], r["StartRegionID"]))
            continue
        g = nearest(cands, None, None, None)
        if not g["p"]:
            note(left, "giver stands outside every zone of the tool", "%d %s (%s)" % (qid, r["Name"], r["StartName"]))
            continue
        # ---- classes
        cls = [c for c in r["cls"] if c in playable]
        if r["cls"] and not cls:
            note(left, "only for classes that True 1.65 does not have", "%d %s %s" % (qid, r["Name"], r["cls"]))
            continue
        if cls:
            # a base class plays levels 1 to 4 and advances at 5; an advanced class starts at 5
            ok = [c for c in cls if (r["MinLevel"] <= 5 if class_base.get(c) else r["MaxLevel"] >= 5)]
            if not ok:
                note(left, "level range the allowed classes can never have", "%d %s" % (qid, r["Name"]))
                continue
            cls = ok
        realm = g["realm"] if g["realm"] in (1, 2, 3) else 0
        if not realm and cls:
            rs = {class_realm[c] for c in cls}
            realm = rs.pop() if len(rs) == 1 else 0
        if not realm:
            realm = zone_realm.get(g["p"][0], 0)
        q = dict(id=qid, name=r["Name"], realm=realm, lmin=r["MinLevel"], lmax=r["MaxLevel"], cls=sorted(cls),
                 src="data", rep=r["MaxCount"] or 0,
                 giver=dict(name=g["name"], zone=g["p"][0], x=g["p"][1], y=g["p"][2], n=len([s for s in cands if s["p"]])))
        if g["obj"]:
            q["giver"]["obj"] = 1
        if info.get("Races"):
            q["races"] = info["Races"]
        if info.get("OnlyDuring"):
            q["during"] = info["OnlyDuring"]
        if info.get("ExcludedBy"):
            q["excl"] = info["ExcludedBy"]
        if info.get("RequiresAnyOf"):
            q["reqAny"] = info["RequiresAnyOf"]
        gk = ["#%d" % qid, "%s|%s|%d" % (REALM_NAME.get(realm, ""), r["Name"].lower(), r["MinLevel"]),
              "%s|%s" % (REALM_NAME.get(realm, ""), r["Name"].lower())]
        if any(k in guides for k in gk):
            q["guide"] = 1
        if r["Description"]:
            q["offer"] = r["Description"].strip()
        hand, rw = [], []

        if r["StartType"] == 1:
            # ------------------------------------------------ Collection: hand the item to the giver, MaxCount times
            q["start"] = "turnin"
            iid = item_id(r["CollectItemTemplate"], "collection item", qid)
            if not iid:
                note(left, "turn-in item does not exist", "%d %s" % (qid, r["Name"]))
                continue
            fixed = int(r["RewardXP"] or 0)
            q["turnin"] = dict(item=iid, xp=fixed)
            cr = info.get("CollectRewards")
            if cr:
                w = [("xp", max(0, cr.get("XpWeight", 0))),
                     ("coin", max(0, cr.get("MoneyWeight", 0)) if cr.get("MoneyMax", 0) > 0 else 0),
                     ("item", max(0, cr.get("ItemWeight", 0)) if cr.get("Items") else 0),
                     ("hostile", max(0, cr.get("HostileWeight", 0)) if (cr.get("HostileName") or "").strip() else 0)]
                tot = sum(x[1] for x in w)
                q["turnin"]["roll"] = dict(
                    weights={k: v for k, v in w if v}, total=tot,
                    coin=[max(1, cr.get("MoneyMin", 0)), max(cr.get("MoneyMin", 0), cr.get("MoneyMax", 0))],
                    items=[i for i in (item_id(x, "CollectRewards", qid) for x in cr.get("Items") or []) if i],
                    hostile=[cr.get("HostileName"), cr.get("HostileLevel", 0)] if cr.get("HostileName") else None)
            src = drops_js["byItem"].get(iid) or []
            src = [[s[0], s[1]] for s in src if not (len(s) > 3 and s[3])]
            q["turnin"]["from"] = src[:12]
            q["turnin"]["fromN"] = len(src)
            if not src:
                note(warn, "turn-in item drops from no monster in drops.js", "%d %s (%s)" % (qid, r["Name"], iid))
                q.setdefault("warn", []).append("No monster in this world drops the item this turn-in asks for.")
            for s in src[:12]:
                hand.append([0, s[0], iid, s[1], "loot"])
            q["steps"] = []
            q["xp"] = fixed
            q["coin"] = 0
        else:
            # ------------------------------------------------ Standard: steps
            q["start"] = "talk"
            if r["AcceptText"]:
                q["accept"] = r["AcceptText"]
            types = [int(x) for x in (r["StepType"] or "").split("|")]
            n = len(types)
            assert n > 0 and types[-1] % 2 == 1, "quest %d does not end in a finish step" % qid
            assert all(t in STEP_KIND for t in types), "quest %d has an unknown step type" % qid

            def col(name, conv=str, default=""):
                raw = r[name]
                if raw is None or raw == "":
                    return [default] * n
                parts = raw.split("|")
                if len(parts) != n:
                    note(warn, "column %s has another number of entries than StepType" % name, qid)
                parts = (parts + [""] * n)[:n]
                return [conv(p) if p != "" else default for p in parts]
            texts = col("StepText")
            step_items = [s.strip() for s in col("StepItemTemplates")]
            advance = col("AdvanceText")
            collect = [s.strip() for s in col("CollectItemTemplate")]
            targets = col("TargetName")
            xps = col("RewardXP", int, 0)
            coins = col("RewardMoney", int, 0)
            shares = info.get("XpShares") or []
            jsteps = info.get("Steps") or []
            areas = {}
            for part in (r["SourceName"] or "").split("|"):
                if part.upper().startswith("SEARCH"):
                    p = part.split(";")
                    assert p[0].upper() == "SEARCH", "quest %d uses SEARCHSTART: port it" % qid
                    areas[int(p[1])] = (int(p[3]), int(p[4]), int(p[5]), int(p[6]), int(p[7]))
            steps = []
            bad = None
            for i, t in enumerate(types, 1):
                js = (jsteps[i] if i < len(jsteps) else None) or {}
                cu = js.get("Custom") or {}
                kind = STEP_KIND[t]
                s = dict(n=i, t=kind, text=texts[i - 1].strip())
                if t % 2 == 1:
                    assert i == n, "quest %d: finish step type before the last step" % qid
                # marker (the red map marker the server shows for this step)
                mk = js.get("Marker")
                mreg = mx = my = None
                if mk:
                    mreg, mx, my = mk["Region"], mk["X"], mk["Y"]
                    p = place(mreg, mx, my)
                    if p:
                        s["at"] = list(p)
                    else:
                        note(warn, "step marker outside every zone of the tool", "%d step %d" % (qid, i))
                # target
                tname, treg = "", 0
                if targets[i - 1]:
                    parts = targets[i - 1].split(";")
                    tname, treg = parts[0], int(parts[1]) if len(parts) > 1 and parts[1] else 0
                spawn = js.get("Spawn")
                if kind == "search":
                    if cu.get("Kind"):
                        kind = s["t"] = cu["Kind"]
                    else:
                        assert i in areas, "quest %d step %d: a search step with no search area and no custom kind" % (qid, i)
                        ar = areas[i]
                        p = place(ar[0], ar[1], ar[2])
                        if p:
                            s["area"] = [p[0], p[1], p[2], ar[3], ar[4]]
                            s.setdefault("at", list(p))
                        else:
                            note(warn, "search area outside every zone of the tool", "%d step %d" % (qid, i))
                elif cu.get("Kind"):
                    # a custom kind on a non-search step: the DataQuest type still works and the custom trigger too
                    s["also"] = cu["Kind"]
                if kind == "kill":
                    key = tname.lower()
                    tg = dict(name=tname, key=key)
                    if key in kind_id:
                        tg["k"] = "mob"
                        if treg and not any(sp["region"] == treg for sp in spawns_by_name.get(key, ())) and not \
                                (spawn and (spawn.get("Name") or "").lower() == key):
                            note(warn, "kill target has no spawn in the step's region", "%d step %d: %s" % (qid, i, tname))
                            tg["far"] = 1
                    elif spawn and (spawn.get("Name") or "").lower() == key:
                        tg["k"] = "event"
                    elif spawns_by_name.get(key):
                        tg["k"] = "npc"          # stands in the world but is not a monster kind (realm NPC, peaceful)
                        sp = nearest(find_npcs(tname, treg) or find_npcs(tname, 0), mreg, mx, my)
                        if sp and sp["p"]:
                            tg["zone"], tg["x"], tg["y"] = sp["p"]
                    elif script_named(tname):
                        # a server script gives a standing NPC this name (Alina.cs: "Noble Werewolf Alina" at night)
                        f, cls_name, at_night = script_named(tname)
                        tg["k"] = "script"
                        tg["file"] = f
                        host = [sp for lst in spawns_by_name.values() for sp in lst if sp["cls"].split(".")[-1] == cls_name and sp["p"]]
                        if host:
                            tg["zone"], tg["x"], tg["y"] = host[0]["p"]
                            tg["host"] = host[0]["name"]
                        if at_night:
                            s["night"] = 1
                        note(warn, "kill target is named by a server script", "%d step %d: %s (%s)" % (qid, i, tname, f))
                    else:
                        tg["k"] = "none"
                        note(warn, "kill target is no spawn and no event monster", "%d step %d: %s" % (qid, i, tname))
                        if not bad:
                            bad = "Step %d asks for the death of %s, which does not stand in this world and is not " \
                                  "summoned by the quest." % (i, tname)
                    if tname.lower() in hour_windows:
                        s["hours"] = hour_windows[tname.lower()]
                    s["target"] = tg
                elif tname:
                    tg = dict(name=tname)
                    sp = nearest(find_npcs(tname, treg), mreg, mx, my)
                    if sp is None and spawn and (spawn.get("Name") or "").lower() == tname.lower():
                        tg["k"] = "event"
                    elif sp is None:
                        tg["k"] = "none"
                        note(warn, "step NPC is not in the world", "%d step %d: %s;%d" % (qid, i, tname, treg))
                    else:
                        tg["k"] = "obj" if sp["obj"] else ("mob" if tname.lower() in kind_id else "npc")
                        if tg["k"] == "mob":
                            tg["key"] = tname.lower()
                        if sp["p"]:
                            tg["zone"], tg["x"], tg["y"] = sp["p"]
                    if tname.lower() in hour_windows:
                        s["hours"] = hour_windows[tname.lower()]
                    s["target"] = tg
                # items
                own, chance = None, None
                if step_items[i - 1]:
                    parts = step_items[i - 1].split(";")
                    own = item_id(parts[0], "step item", qid)
                    if len(parts) > 1 and t in (0, 1, 8, 9):
                        chance = int(parts[1]) if parts[1].strip().isdigit() else 0
                        assert 0 < chance <= 100, "quest %d step %d: drop chance %r" % (qid, i, parts[1])
                if own:
                    s["item"] = own
                if chance is not None:
                    s["ch"] = chance
                if collect[i - 1]:
                    ci = item_id(collect[i - 1], "collect item", qid)
                    if ci:
                        s["col"] = ci
                        # DataQuest.CollectCountNeeded
                        cnt = 0
                        if t in (2, 3) and step_items[i - 1].split(";")[0].strip().lower() == ci.lower():
                            cnt += 1
                        for j in range(i - 2, -1, -1):
                            if collect[j].split(";")[0].strip().lower() == ci.lower():
                                break
                            if types[j] not in (2, 3) and step_items[j].split(";")[0].strip().lower() == ci.lower():
                                cnt += 1
                        s["need"] = max(1, cnt)
                if kind == "whisper" and advance[i - 1]:
                    s["word"] = advance[i - 1]
                # what completing the step puts in the backpack (AdvanceQuestStep); a finish step gives no step item
                gets = []
                p_ok = (chance / 100.0) if chance is not None else 1.0
                if t % 2 == 0:
                    if own and t != 2:
                        gets.append([own, p_ok])
                    if types[i] in (2, 3) and step_items[i].strip():
                        nx = item_id(step_items[i].split(";")[0], "step item", qid)
                        if nx and not (own and nx == own and t != 2):
                            gets.append([nx, p_ok])
                if gets:
                    s["gets"] = gets
                if kind == "kill":
                    for g2 in gets:
                        hand.append([i, tname.lower(), g2[0], g2[1], "kill"])
                        if is_gear(g2[0]) and g2 is gets[0] and own:
                            rw.append([g2[0], "step", i])
                # classic-quests.json extras
                for d in cu.get("Drops") or []:
                    di = item_id(d.get("Item"), "Drops", qid)
                    dm = (d.get("Mob") or "").lower()
                    if not di or not dm:
                        continue
                    dch = d.get("Chance", 100)
                    if t in (0, 1) and own and di == own and dm != tname.lower():
                        # ClassicQuests.cs:759-771: counts like the target; the step's own chance when it has one
                        hand.append([i, dm, di, (chance if chance is not None else dch) / 100.0, "kill"])
                        s.setdefault("alt", []).append(dm)
                    else:
                        hand.append([i, dm, di, dch / 100.0, "drop"])
                        s.setdefault("drops", []).append([dm, di, dch / 100.0])
                    if dm not in kind_id and dm not in drops_js["mobs"]:
                        note(warn, "Drops monster is no spawn", "%d step %d: %s" % (qid, i, dm))
                if spawn:
                    sp = dict(name=spawn.get("Name"), level=spawn.get("Level") or 0, n=max(1, spawn.get("Count") or 1))
                    p = place(spawn.get("Region") or 0, spawn.get("X") or 0, spawn.get("Y") or 0)
                    if p:
                        sp["zone"], sp["x"], sp["y"] = p
                    sp["r"] = spawn.get("TriggerRadius", 1500)
                    if spawn.get("Peaceful"):
                        sp["peace"] = 1
                    if spawn.get("PathId"):
                        sp["walks"] = 1
                    if spawn.get("Escort"):
                        sp["escort"] = [[e.get("Name"), max(1, e.get("Count") or 1), e.get("Level") or 0]
                                        for e in spawn["Escort"] if e and e.get("Name")]
                    s["spawn"] = sp
                    if js.get("OwnSpawnOnly"):
                        s["own"] = 1
                if cu.get("Kind") in ("use_item", "has_item"):
                    ui = item_id(cu.get("Item"), "custom item", qid)
                    if ui:
                        s["use"] = ui
                        if cu.get("Give"):
                            s["given"] = 1          # the quest puts the item in the backpack (CheckCustomStep)
                        if cu.get("Consume"):
                            s["consume"] = 1
                        if cu.get("Kind") == "has_item":
                            src = [[x[0], x[1]] for x in (drops_js["byItem"].get(ui) or []) if not (len(x) > 3 and x[3])]
                            if src:
                                s["from"] = src[:8]
                if cu.get("Kind") in ("use_item", "travel", "say") and cu.get("Region"):
                    p = place(cu["Region"], cu.get("X", 0), cu.get("Y", 0))
                    if p:
                        s["place"] = [p[0], p[1], p[2], cu.get("Radius", 600)]
                    else:
                        note(warn, "custom step place outside every zone of the tool", "%d step %d" % (qid, i))
                if cu.get("NightOnly"):
                    s["night"] = 1
                if cu.get("Words"):
                    s["word"] = cu["Words"]
                if cu.get("Target") and "target" not in s:
                    sp2 = nearest(find_npcs(cu["Target"], 0), mreg, mx, my)
                    tg = dict(name=cu["Target"], k="npc" if sp2 else "none")
                    if sp2 and sp2["p"]:
                        tg["zone"], tg["x"], tg["y"] = sp2["p"]
                    s["target"] = tg
                if cu.get("Kind") == "die_to" and cu.get("Target"):
                    s["target"]["key"] = cu["Target"].lower()
                if cu.get("Kind") == "quest":
                    names = [x.strip() for x in (cu.get("Quest") or "").split("|") if x.strip()]
                    s["quests"] = [names, cu.get("Count") or len(names)]
                if cu.get("Choices"):
                    ch = []
                    for word, ci in cu["Choices"].items():
                        ci2 = item_id(ci, "Choices", qid)
                        if ci2:
                            ch.append([word, ci2])
                            rw.append([ci2, "choice", i])
                    s["choices"] = ch
                if cu.get("Trades"):
                    tr = []
                    for tin, tout in cu["Trades"].items():
                        a1 = item_id(tin, "Trades", qid)
                        b1 = item_id(tout, "Trades", qid) if tout else None
                        if a1:
                            tr.append([a1, b1])
                            src = [[x[0], x[1]] for x in (drops_js["byItem"].get(a1) or []) if not (len(x) > 3 and x[3])]
                            if src:
                                s.setdefault("from", []).extend(src[:8])
                        if b1 and is_gear(b1):
                            rw.append([b1, "trade", i])
                    s["trades"] = tr
                if js.get("NeedsItem"):
                    ni = item_id(js["NeedsItem"], "NeedsItem", qid)
                    if ni:
                        s["needs"] = [ni, js.get("Issuer") or ""]
                if js.get("Grant"):
                    gr = [x for x in (item_id(x, "Grant", qid) for x in js["Grant"]) if x]
                    if gr:
                        s["grant"] = [gr, js.get("GrantFrom") or ""]
                        for x in gr:
                            if is_gear(x):
                                rw.append([x, "grant", i])
                if js.get("Wearing"):
                    s["wear"] = [x for x in (item_id(x, "Wearing", qid) for x in js["Wearing"]) if x]
                if kind == "collect" and s.get("col") and not s.get("drops"):
                    src = [[x[0], x[1]] for x in (drops_js["byItem"].get(s["col"]) or []) if not (len(x) > 3 and x[3])]
                    if src:
                        s["from"] = src[:8]
                if xps[i - 1]:
                    s["xp"] = xps[i - 1]
                if i - 1 < len(shares) and shares[i - 1] > 0:
                    s["share"] = shares[i - 1]
                if coins[i - 1]:
                    s["coin"] = coins[i - 1]
                steps.append(s)
            if bad:
                q.setdefault("warn", []).append(bad)
            q["steps"] = steps
            q["xp"] = sum(xps)
            q["share"] = round(sum(x for x in shares[:n] if x > 0), 4)
            q["coin"] = sum(coins)
            for side in info.get("SideTurnIns") or []:
                si = item_id(side.get("Item"), "SideTurnIns", qid)
                sr = item_id(side.get("Reward"), "SideTurnIns", qid) if side.get("Reward") else None
                if not si:
                    continue
                e = dict(item=si, to=side.get("Target"), reward=sr, fromStep=max(1, side.get("FromStep", 1)),
                         toStep=side.get("ToStep", 0))
                if side.get("DropFrom"):
                    e["mob"] = side["DropFrom"].lower()
                    e["ch"] = side.get("DropChance", 100) / 100.0
                    hand.append([e["fromStep"], e["mob"], si, e["ch"], "side"])
                q.setdefault("side", []).append(e)
                if sr:
                    rw.append([sr, "side", 0])
            finals = [x for x in (item_id(raw, "final reward", qid) for raw in (r["FinalRewardItemTemplates"] or "").split("|")) if x]
            rw[0:0] = [[fi, "final", 0] for fi in finals]        # all of them are given (FinishQuest), in the row's order
        # quest-gated loot rows of D-D's drops.js for this quest
        q["hand"] = hand
        seen = set()
        q["rw"] = [x for x in rw if not (tuple(x[:2]) in seen or seen.add(tuple(x[:2])))]
        quests[qid] = q

    # loot rows flagged "quest" (falls only while on the quest): add to that quest's hand-overs
    n_loot = 0
    for key, k in drops_js["mobs"].items():
        for d in k.get("d") or []:
            if len(d) > 5 and d[3] & 2 and d[5]:
                for qid in d[5]:
                    if qid in quests:
                        iid = item_id(d[0], "drops.js quest loot", qid)
                        if iid:
                            quests[qid]["hand"].append([0, key, iid, d[1], "loot"])
                            n_loot += 1

    # cross-check the classic script's drops with D-D's `q` (drops.md): same monster, item, quest, chance
    mine = collections.defaultdict(set)
    for qid, q in quests.items():
        for h in q["hand"]:
            if h[4] in ("drop", "side") or (h[4] == "kill" and h[1] != (q["steps"][h[0] - 1].get("target") or {}).get("key")):
                mine[(h[1], h[2].lower(), qid)].add(round(h[3], 4))
    theirs = collections.defaultdict(set)
    for key, k in drops_js["mobs"].items():
        for e in k.get("q") or []:
            theirs[(key, e[0].lower(), e[2])].add(round(e[1], 4))
    only_theirs = [k for k in theirs if k not in mine and k[2] in quests]
    diff = [k for k in theirs if k in mine and not (theirs[k] & mine[k])]
    print("classic-script drops: %d here, %d in drops.js q; in drops.js only %d, chance differs %d" %
          (len(mine), len(theirs), len(only_theirs), len(diff)))
    for k in (only_theirs + diff)[:10]:
        print("   ", k, theirs.get(k), mine.get(k))
    warn["drops.js q entries not matched here"] += len(only_theirs)
    warn["drops.js q entries with another chance"] += len(diff)

    # ---------------------------------------------------------------- scripted quests
    scripted = json.loads(SCRIPTED_JSON)
    epic_lines = [e["line"] for e in scripted["epics"]]
    s_left = []

    def raw_place(region, pos):
        if region is None or not pos or pos[0] is None:
            return None
        return place(region, pos[0], pos[1])

    def s_target(name, region, pos, kill, sid=""):
        if not name:
            return None
        tg = dict(name=name)
        names = [x.strip() for x in name.split(",")]
        keys = [x.lower() for x in names if x.lower() in kind_id]
        p = raw_place(region, pos)
        if kill:
            tg["k"] = "mob" if len(keys) == len(names) else "event"
            if len(names) == 1:
                tg["key"] = names[0].lower()
            else:
                tg["keys"] = [x.lower() for x in names]
            if tg["k"] == "event" and not p and not any(spawns_by_name.get(x.lower()) for x in names):
                tg["k"] = "none"
                note(warn, "scripted quest: kill target is not in the world and not placed by the script", "%s: %s" % (sid, name))
        else:
            sp = nearest(find_npcs(name, region or 0), region, pos[0] if pos and pos[0] is not None else None,
                         pos[1] if pos and pos[0] is not None else None)
            tg["k"] = "npc"
            if sp and sp["p"] and not p:
                p = sp["p"]
            elif not sp and p:
                tg["made"] = 1          # the script creates this NPC at start-up; it is not a Mob row
            elif not sp:
                tg["k"] = "none"
                note(warn, "scripted quest: step NPC is only looked up by name and is not in the world", "%s: %s" % (sid, name))
        if p:
            tg["zone"], tg["x"], tg["y"] = p
        return tg

    def add_scripted(e, kind):
        sid = "s-" + e["key"].lower()
        src_file = os.path.join(wg.SERVER_SRC, "GameServer", e["file"])
        assert os.path.exists(src_file), "scripted quest source missing: " + e["file"]
        text = open(src_file, encoding="utf-8-sig", errors="replace").read()
        cls = [c for c in e.get("classes") or [] if c in playable]
        q = dict(id=sid, name=e["title"], realm=e["realm"], lmin=e["minLevel"], lmax=e["maxLevel"], cls=sorted(cls),
                 src="script", file=e["file"], rep=1, start="script", how=e.get("start") or "", kind=kind)
        givers = []
        for g in e.get("givers") or []:
            if g.get("name"):
                givers.append(g)
        if not givers and isinstance(e.get("requires"), dict) and e["requires"].get("kind") == "FinishedPart":
            # Epics165.TrainersOf(line, level): the trainers who give that part (exact name, exact level)
            lvl = e["requires"]["level"]
            names = sorted({r["StartName"] for r in rows if r["MinLevel"] == lvl and (r["Name"] or "").lower() == e["line"].lower()
                            and r["ID"] in quests})
            givers = [dict(name=nm, region=by_reg) for nm in names
                      for by_reg in sorted({r["StartRegionID"] for r in rows if r["StartName"] == nm and r["MinLevel"] == lvl
                                            and (r["Name"] or "").lower() == e["line"].lower()})]
            if not givers:
                s_left.append((e["title"], "no trainer gives the level %d part named exactly '%s' (Epics165.TrainersOf)" % (lvl, e["line"])))
                return
            q["giverNote"] = "Any trainer who gives the level %d part of %s." % (lvl, e["line"])
        glist = []
        for g in givers:
            p = raw_place(g.get("region"), [g.get("x"), g.get("y")]) if g.get("x") is not None else None
            if not p:
                sp = nearest(find_npcs(g["name"], g.get("region") or 0), None, None, None)
                p = sp["p"] if sp else None
                if not sp and "created" not in (g.get("how") or ""):
                    s_left.append((e["title"], "its giver %s is only looked up by the script and is not in the world" % g["name"]))
                    return
            if p:
                glist.append(dict(name=g["name"], zone=p[0], x=p[1], y=p[2]))
        if glist:
            q["giver"] = dict(glist[0], n=len(glist))
            if len(glist) > 1:
                q["givers"] = glist
        elif givers:
            s_left.append((e["title"], "giver stands outside every zone of the tool"))
            return
        steps, hand, rw = [], [], []
        for st in e.get("steps") or []:
            kind_s = {"return": "talk", "interact": "talk"}.get(st["type"], st["type"])
            s = dict(n=st["n"], t=kind_s, text=st.get("text") or "")
            tg = s_target(st.get("target"), st.get("region"), st.get("pos"), kind_s == "kill", sid)
            if st.get("targetIsOwnTrainer"):
                tg = dict(name="your own class trainer", k="trainer")
            if st.get("targetByRealm"):
                br = {}
                for rk, tv in st["targetByRealm"].items():
                    p = raw_place(tv.get("region"), tv.get("pos"))
                    assert p, "%s: %s stands outside every zone" % (sid, tv.get("name"))
                    assert find_npcs(tv["name"], tv["region"]), "%s: %s is not a Mob row" % (sid, tv["name"])
                    br[rk] = dict(name=tv["name"], zone=p[0], x=p[1], y=p[2], town=tv.get("town") or "")
                s["byRealm"] = br
            if tg:
                s["target"] = tg
                if "zone" in tg:
                    s["at"] = [tg["zone"], tg["x"], tg["y"]]
            if st.get("count") and st["count"] > 1:
                s["count"] = st["count"]
            if st.get("mobLevel"):
                s["level"] = st["mobLevel"]
            if st.get("spawnedByScript") and kind_s == "kill":
                s["scripted"] = 1
            # "<class>EpicArms" and the like stand for the class armour, which is listed under the rewards
            raw_g = [x for x in st.get("gives") or [] if "<" not in x]
            raw_t = [x for x in st.get("takes") or [] if "<" not in x]
            gets = [x for x in (item_id(x, "scripted step item", sid) for x in raw_g) if x]
            takes = [x for x in (item_id(x, "scripted step item", sid) for x in raw_t) if x]
            for x in raw_g + raw_t:
                assert x.lower() in text.lower() or x.lower() in items, "%s: item %s is not in %s" % (sid, x, e["file"])
            if gets:
                ch = (st.get("dropChance") if st.get("dropChance") is not None else 100) / 100.0
                s["gets"] = [[x, ch if kind_s == "kill" else 1.0] for x in gets]
                if kind_s == "kill" and tg:
                    for key in tg.get("keys") or [tg.get("key")]:
                        for x in gets:
                            if key:
                                hand.append([st["n"], key, x, ch, "kill"])
            if takes:
                s["takes"] = takes
            if st.get("note"):
                s["note"] = st["note"]
            if st.get("cite"):
                s["cite"] = st["cite"]
            steps.append(s)
        q["steps"] = steps
        if e.get("startGives"):
            q["startGives"] = [x for x in (item_id(x, "scripted start item", sid) for x in e["startGives"]) if x]
        rwd = e.get("rewards") or {}
        q["xp"] = rwd.get("xp") or 0
        q["coin"] = rwd.get("money") or 0
        if rwd.get("xpNote"):
            q["xpNote"] = rwd["xpNote"]
        if rwd.get("note"):
            q["rwNote"] = rwd["note"]
        for x in rwd.get("items") or []:
            x2 = item_id(x, "scripted reward", sid)
            if x2:
                rw.append([x2, "final", 0])
        by_class = {}
        for cid, lst in (rwd.get("byClass") or {}).items():
            if int(cid) not in playable:
                continue
            got = [x for x in (item_id(x, "scripted class reward", sid) for x in lst) if x]
            if got:
                by_class[cid] = got
        if by_class:
            q["rwClass"] = by_class
        if e.get("requires"):
            q["req"] = e["requires"] if isinstance(e["requires"], dict) else dict(text=e["requires"])
        if e.get("line"):
            q["line"] = e["line"]
        q["notes"] = (e.get("notes") or [])[:12]
        if e.get("uncertain"):
            q["uncertain"] = e["uncertain"]
        q["hand"] = hand
        q["rw"] = rw
        assert e["title"].split(" (level")[0].lower() in text.lower() or kind != "epic", "%s: title not found in the script" % sid
        quests[sid] = q

    for e in scripted["epics"]:
        add_scripted(e, "epic")
    for e in scripted["others"]:
        add_scripted(e, e.get("kind") or "town")

    # ---------------------------------------------------------------- dependencies and chains
    scripted_names = collections.defaultdict(list)
    for sid, q in quests.items():
        if q["src"] == "script":
            scripted_names[q["name"].lower()].append(sid)
    realm_classes = collections.defaultdict(list)
    for c in sorted(playable):
        realm_classes[class_realm[c]].append(c)

    def classes_of(q):
        return q["cls"] or realm_classes.get(q["realm"]) or sorted(playable)

    def dep_for_class(dep, own_name, own_min, c):
        """QuestDependencyRule.IsMet as 'which quests meet it for class c': (ids, required MinLevel or -1)."""
        dep = dep.strip()
        if dep.startswith("#"):
            ids = [int(x.lstrip("#")) for x in dep[1:].split(",") if x.strip().lstrip("#").isdigit()]
            return [i for i in ids if i in quests], -1
        own = dep.lower() == own_name.lower()
        required = -1
        mine_rows = [x for x in rows_by_name.get(dep.lower(), ()) if not x["cls"] or c in x["cls"]]
        for x in mine_rows:
            if (x["MinLevel"] >= own_min) if own else (x["MinLevel"] > own_min):
                continue
            required = max(required, x["MinLevel"])
        if required < 0:
            # any finished quest of that name: scripted ones, or data quest rows the class can have finished
            ids = list(scripted_names.get(dep.lower(), ())) + [x["ID"] for x in mine_rows if x["ID"] in quests and x["MinLevel"] <= 50]
            return ids, -1
        ids = [x["ID"] for x in mine_rows if x["MinLevel"] >= required and x["ID"] in quests]
        if not own:
            ids += list(scripted_names.get(dep.lower(), ()))
        # show the part that is needed first (the lowest that meets it)
        ids.sort(key=lambda i: (quests[i]["lmin"], str(i)))
        return ids, required

    changed = True
    rounds = 0
    while changed:
        changed = False
        rounds += 1
        for qid in [k for k, q in quests.items() if q["src"] == "data"]:
            q, r = quests[qid], by_id[qid]
            deps = [d for d in (r["QuestDependency"] or "").split("|") if d.strip()]
            need, ok_classes = [], []
            for c in classes_of(q):
                met = True
                for d in deps:
                    ids, required = dep_for_class(d, r["Name"], r["MinLevel"], c)
                    if not ids:
                        met = False
                if met and q.get("reqAny") and not any(i in quests for i in q["reqAny"]):
                    met = False
                if met:
                    ok_classes.append(c)
            if (deps or q.get("reqAny")) and not ok_classes:
                note(left, "its prerequisite quest cannot be finished by any class that may take it",
                     "%d %s needs %s" % (qid, r["Name"], r["QuestDependency"] or q.get("reqAny")))
                del quests[qid]
                changed = True
                continue
            if q["cls"] and len(ok_classes) < len(q["cls"]):
                note(warn, "class removed from a quest: it can never finish the prerequisite",
                     "%d %s %s" % (qid, r["Name"], sorted(set(q["cls"]) - set(ok_classes))))
                q["cls"] = sorted(ok_classes)
                changed = True
    for qid, q in quests.items():
        if q["src"] != "data":
            continue
        r = by_id[qid]
        deps = [d for d in (r["QuestDependency"] or "").split("|") if d.strip()]
        need = []
        for d in deps:
            ids = []
            for c in classes_of(q):
                got, required = dep_for_class(d, r["Name"], r["MinLevel"], c)
                if got:
                    # the part needed: the lowest-level quest that meets it for this class
                    lo = quests[got[0]]["lmin"]
                    ids += [i for i in got if quests[i]["lmin"] == lo]
            ids = sorted(set(ids), key=str)
            need.append(dict(name=d.lstrip("#"), ids=ids))
        if need:
            q["need"] = need
        if q.get("reqAny"):
            q["reqAny"] = [i for i in q["reqAny"] if i in quests]

    # epic finales: the last part before 50 (Epics165.FinishedLastPart / FinishedPart)
    for sid, q in quests.items():
        if q["src"] != "script" or not q.get("line") or not isinstance(q.get("req"), dict) or not q["req"].get("kind"):
            continue
        ids = []
        for c in classes_of(q):
            parts = [x for x in rows if is_part_of(x["Name"], q["line"]) and x["MinLevel"] < 50 and (not x["cls"] or c in x["cls"])]
            if q["req"]["kind"] == "FinishedPart":
                ids += [x["ID"] for x in parts if x["MinLevel"] == q["req"]["level"] and x["ID"] in quests]
            elif parts:
                top = max(x["MinLevel"] for x in parts)
                ids += [x["ID"] for x in parts if x["MinLevel"] == top and x["ID"] in quests]
        ids = sorted(set(ids))
        q["need"] = [dict(name=q["line"], ids=ids)]
        if not ids:
            note(warn, "epic finale with no earlier part in the world", sid)

    nxt = collections.defaultdict(set)
    for qid, q in quests.items():
        for nd in q.get("need") or []:
            for i in nd["ids"]:
                nxt[i].add(qid)
        for i in q.get("reqAny") or []:
            nxt[i].add(qid)
    for qid, q in quests.items():
        if nxt.get(qid):
            q["next"] = sorted(nxt[qid], key=str)

    # kind, parts of a name, class-version groups
    by_name_realm = collections.defaultdict(list)
    for qid, q in quests.items():
        by_name_realm[(q["name"].lower(), q["realm"])].append(q)
    groups = collections.defaultdict(list)
    for qid, q in quests.items():
        if q["src"] == "data":
            if any(is_part_of(q["name"], ln) for ln in epic_lines):
                q["kind"] = "epic"
                q["line"] = next(ln for ln in epic_lines if is_part_of(q["name"], ln))
            elif q["start"] == "turnin" or q["name"].endswith("-- Kill Task"):
                q["kind"] = "killtask"
            elif q["cls"]:
                q["kind"] = "class"
            elif q.get("need") or q.get("next") or q.get("reqAny") or q.get("during"):
                q["kind"] = "chain"
            else:
                q["kind"] = "town"
            levels = sorted({x["lmin"] for x in by_name_realm[(q["name"].lower(), q["realm"])] if x["src"] == "data"})
            if len(levels) > 1:
                q["part"] = [levels.index(q["lmin"]) + 1, len(levels)]
            # the same quest once per class (each class has its own row, often with its own trainer as the giver)
            groups[(q["name"].lower(), q["realm"], q["lmin"], q["lmax"], len(q["steps"]))].append(qid)
    for key, ids in groups.items():
        for i in ids:
            quests[i]["grp"] = ids[0]
            if len(ids) > 1:
                quests[i]["grpN"] = len(ids)

    # ---------------------------------------------------------------- checks and output
    data_n = sum(1 for q in quests.values() if q["src"] == "data")
    assert data_n + sum(left.values()) == n_rows, "counts do not add up: %d in + %d out != %d rows" % (data_n, sum(left.values()), n_rows)
    kill_steps = sum(1 for q in quests.values() for s in q["steps"] if s["t"] == "kill" and q["src"] == "data")
    hand_n = collections.Counter(h[4] for q in quests.values() for h in q["hand"])
    for q in quests.values():
        assert q["realm"] in (0, 1, 2, 3) and 0 <= q["lmin"] <= q["lmax"] <= 50, "level or realm of %s" % q["id"]
        if q.get("giver"):
            assert q["giver"]["zone"] in kept_zone_ids and 0 <= q["giver"]["x"] < 65536 and 0 <= q["giver"]["y"] < 65536
        for h in q["hand"]:
            assert 0 < h[3] <= 1.0, "hand-over chance out of range in %s" % q["id"]
        for s in q["steps"]:
            if "at" in s:
                assert s["at"][0] in kept_zone_ids
        for c in q["cls"]:
            assert c in playable
    names = {k: v for k, v in sorted(used_items.items())}
    kinds = collections.Counter(q["kind"] for q in quests.values())
    meta = dict(
        built=wg.stamp(), db=os.path.basename(a.db),
        counts=dict(rows=n_rows, dataQuests=data_n, scripted=len(quests) - data_n, total=len(quests),
                    leftOut=dict(left), killSteps=kill_steps, handOvers=dict(hand_n), kinds=dict(kinds),
                    steps=sum(len(q["steps"]) for q in quests.values()),
                    names=len({q["name"].lower() for q in quests.values()}),
                    scriptedLeftOut=[list(x) for x in s_left]),
        warn=dict(warn),
        notLive=scripted["notLive"], systems=scripted["systems"],
        kindNames=[["class", "Class and guild"], ["epic", "Epic"], ["chain", "Quest chain"], ["town", "Town"],
                   ["killtask", "Kill task"]],
        stepNames=dict(kill="Kill", deliver="Deliver", talk="Talk", whisper="Say", search="Search", collect="Bring",
                       use_item="Use item", choose="Choose reward", trade="Hand in", quest="Finish quests",
                       travel="Go to", has_item="Get item", say="Speak", group="Join a group", die_to="Be slain",
                       other="Other"),
        rules=dict(
            take="Level between the quest's lowest and highest level, a class the quest allows, not on it already, "
                 "done fewer times than its limit, and the quests it needs finished (DataQuest.cs:1007-1059).",
            kill="A kill counts when the monster's name matches, whoever of your group lands it. A step with a chance "
                 "rolls it on every kill: a failed roll means that kill does not count (DataQuest.cs:1624-1649).",
            xp="Step experience is the larger of the fixed number and the step's share of your current level "
               "(ClassicQuests.ScaleXP); at level 50 only the fixed number.",
            turnin="Hand the item to the NPC: each one pays the fixed experience, up to the limit, while your level is in "
                   "the range (DataQuest.cs:2246-2375).",
            off="Bounty masters, faction emissaries (reputation hunts) and the battleground daily quests do not exist in "
                "True 1.65 (Classic165Mode.cs:48-75)."),
        xpForLevel=xp_table)
    out = dict(meta=meta, quests=sorted(quests.values(), key=lambda q: (q["lmin"], q["name"].lower(), str(q["id"]))),
               classRewards=class_rewards, names=names)
    size = wg.write_data("quests", out, out_dir, "%d quests" % len(quests))
    assert size < 15e6, "quests.js is over 15 MB: split it"

    print("DataQuest rows %d = %d taken + %d left out" % (n_rows, data_n, sum(left.values())))
    for k, v in left.most_common():
        print("  left out %4d  %s" % (v, k))
        for ex in examples[k][:4]:
            print("             e.g. %s" % (ex,))
    print("scripted quests: %d in, left out: %s" % (len(quests) - data_n, s_left))
    print("kinds:", dict(kinds))
    print("kill steps (data quests): %d; hand-overs by source: %s; quest-gated loot rows joined: %d" % (kill_steps, dict(hand_n), n_loot))
    print("steps: %d, item names: %d, class reward sets: %d, dependency rounds: %d" % (meta["counts"]["steps"], len(names), len(class_rewards), rounds))
    for k, v in sorted(warn.items()):
        if v:
            print("  note %5d  %s" % (v, k))
            for ex in examples[k][:5]:
                print("             e.g. %s" % (ex,))
    return 0


SCRIPTED_JSON = r'''{"epics":[{"key":"Academy50","file":"scripts/quests/Albion/epic/Academy50.cs","title":"Symbol of the Broken (level 50)","line":"Symbol of the Broken","realm":1,"minLevel":50,"maxLevel":50,"classes":[4,7,8],"requires":{"kind":"FinishedPart","line":"Symbol of the Broken","level":48,"text":"The player must have finished the level 48 data quest part named \"Symbol of the Broken\" (or \"Symbol of the Broken (level N)\" with MinLevel 48) (Academy50.cs:1394, Epics165.cs:23-32)."},"givers":[{"name":"Veteran Guardsman Esmond","region":1,"x":null,"y":null,"z":null,"how":"looked up by name in realm Albion, region 1 (Epics165.FindNpc -> WorldMgr.GetNPCsByName); NOT created by the script: when he is missing the quest is not loaded at all in 1.65 mode (warning logged)","cite":"Academy50.cs:1181-1186, 1191"}],"start":"Interact (right-click) with Veteran Guardsman Esmond: with no speech he opens the quest offer window (\"Symbol of the Broken (level 50): wield the ritual dagger and be rid of Morgana once and for all.\"). Accepting starts step 1 and Esmond hands over the jeweled ritual dagger (cq_jeweled_ritual_dagger) when the player has none in the backpack. No whisper word. (Academy50.cs:1207-1226)","steps":[{"n":1,"text":"Take the jeweled ritual dagger to the fallen tower in Lyonesse (loc 20k, 39k): follow the Telamon road past the Danaoian farmers to the fallen tower where Morgana stands. Do not attack Morgana. Slay her demons Bechard and Silcharde and the small demons that fly around the tower, then stand by the tower.\nBechard killed: (<n> | 1)\nSilcharde killed: (<n> | 1)\nSummoned demons killed: (<n> | 20)","type":"kill","target":"Bechard, Silcharde, demonic minion","targetIsOwnTrainer":false,"count":22,"region":1,"pos":[306093,670120,3116],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":[],"note":"Needs 1 Bechard + 1 Silcharde + 20 demonic minions (class DemonicMinion, level 30-33), counted on the quest owner's EnemyKilled events. Bechard and Silcharde are spawned by Morgana's brain when a PrivLevel 1 player on step 1 comes within 1000 units of Morgana; each boss spawns 14-16 minions when it dies. When all three counts are met the dagger is removed, \"A sense of calm settles about you!\" is shown and the step becomes 2. The three counters are plain fields on the quest object (not saved custom properties). pos is Morgana's spot; Bechard spawns at 306044,670253,3028 and Silcharde at 306132,669983,3040.","cite":"Academy50.cs:1230-1235, 1509-1533, 1242-1248; MorganaBechardSilcharde.cs:79-93, 179-214, 308-321, 517-524"},{"n":2,"text":"A sense of calm settles about you! Return to Master Ferowl at the inn in Cotswold.","type":"talk","target":"Master Ferowl","targetIsOwnTrainer":false,"count":null,"region":1,"pos":[559461,510653,2712],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":[],"note":"Interacting with Master Ferowl gives 1,937,768,448 experience (Epics165.FinaleXp) and moves to step 3. The dagger was already removed by the script when the tower was cleared (not handed to an NPC). pos is where the script creates Ferowl when no Master Ferowl is found within 3000 units of 559690,510258 in region 1.","cite":"Academy50.cs:1236, 1254-1258, 120-150"},{"n":3,"text":"Master Ferowl asks you to go to your trainer in Camelot for your reward.","type":"return","target":null,"targetIsOwnTrainer":true,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with any trainer of the player's own class (Epics165.IsOwnTrainer: a GameTrainer of the player's realm that can train the player; the code does not check that the trainer is in Camelot). Needs 6 free backpack slots, otherwise \"You do not have enough free space in your inventory!\" and nothing happens. The trainer gives the six armour pieces and the quest finishes.","cite":"Academy50.cs:1237, 1259-1260; Epics165.cs:106-121"}],"rewards":{"xp":1937768448,"xpNote":"Given by Master Ferowl at the step 2 -> 3 talk (Academy50.cs:1256), not at FinishQuest; in 1.65 mode FinishQuest gives only the armour (Academy50.cs:1571-1577).","money":null,"byClass":{"4":["MinstrelEpicBoots","MinstrelEpicHelm","MinstrelEpicGloves","MinstrelEpicArms","MinstrelEpicVest","MinstrelEpicLegs"],"7":["WizardEpicBoots","WizardEpicHelm","WizardEpicGloves","WizardEpicVest","WizardEpicArms","WizardEpicLegs"],"8":["SorcerorEpicBoots","SorcerorEpicHelm","SorcerorEpicGloves","SorcerorEpicVest","SorcerorEpicArms","SorcerorEpicLegs"]},"note":"GiveEpicArmour, Academy50.cs:1590-1618. No money (the AddMoney line is commented out, Academy50.cs:1587)."},"monsters":[{"name":"Morgana","level":90,"region":1,"x":306093,"y":670120,"z":3116,"createdByScript":true,"cite":"Academy50.cs:153-195 (looked up by name in realm None near 306093,670120; created when missing with Level 90 at line 174; not to be attacked; her AddToWorld then loads NPC template 700000001, MorganaBechardSilcharde.cs:35-36, which may replace the level)"},{"name":"Bechard","level":null,"region":1,"x":306044,"y":670253,"z":3028,"createdByScript":true,"cite":"MorganaBechardSilcharde.cs:179-195 (spawned by MorganaBrain; NPC template 700000009, line 288; the quest file's own Bechard creation is commented out, Academy50.cs:199-230)"},{"name":"Silcharde","level":null,"region":1,"x":306132,"y":669983,"z":3040,"createdByScript":true,"cite":"MorganaBechardSilcharde.cs:197-213 (spawned by MorganaBrain; NPC template 700000008, line 439)"},{"name":"demonic minion","level":30,"region":1,"x":306041,"y":670103,"z":3310,"createdByScript":true,"cite":"MorganaBechardSilcharde.cs:308-321, 460-473, 517-524 (level random 30-33; 14-16 spawn within 150 units of this point at each boss's death)"}],"repeatable":false,"notes":["In 1.65 mode ScriptLoaded returns after Load165(): Master Ferowl gets no Interact/Whisper handlers and is not a quest giver; only Esmond's Interact is hooked (Academy50.cs:1064-1068, 1179-1194).","The sealed pouch (sealed_pouch) of the non-1.65 flow is never given in 1.65 mode; the template is still created at load (Academy50.cs:269-291, 1529-1533).","The jeweled ritual dagger template cq_jeweled_ritual_dagger is only looked up, not created; when the template is missing the quest still starts, without a dagger (Academy50.cs:1187, 1224-1225, 1244-1245).","Morgana's brain starts the encounter only for a player with Account.PrivLevel == 1 who is on step 1 within 1000 units, so a GM account does not make the demons appear (MorganaBechardSilcharde.cs:83-93).","After the encounter the whole encounter resets after SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes (MorganaBechardSilcharde.cs:160-162).","Step 1 kills are handled by the shared Notify code, steps 2-3 by Notify165 on GameObjectEvent.InteractWith (Academy50.cs:1503-1507, 1250-1261).","The step 1 text says \"then stand by the tower\", but the code advances as soon as the three kill counts are met; no area check (Academy50.cs:1529-1533).","No 'abort' path in 1.65 mode (the abort whisper is in TalkToFerowl, which is not registered)."],"uncertain":["Veteran Guardsman Esmond's coordinates: the script only looks him up in region 1 (Cornwall Station per the comment, Academy50.cs:84-89); no position in the code.","Levels of Morgana, Bechard and Silcharde come from NPC templates 700000001 / 700000009 / 700000008 in the database, not from the scripts.","Whether the kill counters survive a relog: they are instance fields with no save code in this file (Academy50.cs:53-55)."],"startGives":["cq_jeweled_ritual_dagger"]},{"key":"Church50","file":"scripts/quests/Albion/epic/Church50.cs","title":"Passage to Eternity (level 50)","line":"Passage to Eternity","realm":1,"minLevel":50,"maxLevel":50,"classes":[6,1],"requires":{"kind":"FinishedLastPart","line":"Passage to Eternity","level":null,"text":"The player must have finished the last data quest part of \"Passage to Eternity\" below level 50 that the player's class may take (highest MinLevel below 50 among the line's rows; no such row in the world = no gate) (Church50.cs:975, Epics165.cs:59-78)."},"givers":[{"name":"Roben Fraomar","region":1,"x":408557,"y":651675,"z":5200,"how":"looked up by name in realm Albion (WorldMgr.GetNPCsByName) within 3000 units of 408557,651675 in region 1; created by the script at these coordinates if missing","cite":"Church50.cs:102-131"}],"start":"Interact (right-click) with Roben Fraomar: with no speech he opens the quest offer window (\"Passage to Eternity (level 50): slay Sister Blythe in the ancient temple of Arawn in Lyonesse.\"). Accepting starts step 1. No whisper word. (Church50.cs:814-829)","steps":[{"n":1,"text":"Gather a group and go to the ancient temple of Arawn within Lyonesse, behind the pikemen house at 35k, 41k. Slay Sister Blythe; the dark one's priests guard her.","type":"kill","target":"Sister Blythe","targetIsOwnTrainer":false,"count":1,"region":1,"pos":[322231,671546,2762],"spawnedByScript":true,"mobLevel":69,"gives":[],"takes":[],"note":"Any killed enemy whose name equals \"Sister Blythe\" counts (name match on the quest owner's EnemyKilled event). The message \"Return to your trainer in Camelot.\" follows. No Statue of Arawn is given in 1.65 mode. Sister Blythe spawns 4 Fallen Executioner adds (class FallenExecutioner) around 322192,671493,2764.","cite":"Church50.cs:833-834, 841-846, 135-164; SisterBlythe.cs:82-120"},{"n":2,"text":"Return to your trainer in Camelot. Make sure you have 6 free slots in your inventory.","type":"return","target":null,"targetIsOwnTrainer":true,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with any trainer of the player's own class (Epics165.IsOwnTrainer; the code does not check the trainer's location). Needs 6 free backpack slots. The quest finishes: armour and experience.","cite":"Church50.cs:835, 847-849; Epics165.cs:106-121"}],"rewards":{"xp":1937768448,"xpNote":"GainExperience(eXPSource.Quest, 1937768448, true) in FinishQuest, at the trainer (Church50.cs:1152).","money":null,"byClass":{"6":["ClericEpicBoots","ClericEpicArms","ClericEpicGloves","ClericEpicHelm","ClericEpicVest","ClericEpicLegs"],"1":["PaladinEpicBoots","PaladinEpicArms","PaladinEpicGloves","PaladinEpicHelm","PaladinEpicVest","PaladinEpicLegs"]},"note":"FinishQuest, Church50.cs:1125-1153. No money (AddMoney is commented out, line 1153). In 1.65 mode the statue is not removed because it was never given (line 1128)."},"monsters":[{"name":"Sister Blythe","level":69,"region":1,"x":322231,"y":671546,"z":2762,"createdByScript":true,"cite":"Church50.cs:135-164 (looked up by name in realm None near 322231,671546; created when missing, Level 69 at line 156; SisterBlythe.AddToWorld then loads NPC template 12982, SisterBlythe.cs:88-89, which may replace the level; respawn = SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes, line 92)"},{"name":"FallenExecutioner (class; name from NPC template 60160685)","level":null,"region":1,"x":322192,"y":671493,"z":2764,"createdByScript":true,"cite":"SisterBlythe.cs:107-120, 238 (4 adds spawned with Sister Blythe; they die when she dies, lines 98-105; not needed for the quest)"}],"repeatable":false,"notes":["In 1.65 mode only Roben's Interact is hooked (TalkToRoben165); the whisper chain vision/occupants/break the light of camelot and the 'abort' whisper of the other flow are not registered (Church50.cs:742-746, 788-801).","The statue_of_arawn template is still created at load but never given in 1.65 mode (Church50.cs:172-194, 841-846).","Item templates are added to the database only when BaseQuest.SAVE_INTO_DATABASE is true (default true, BaseQuest.cs:31).","The comment block at the end of the file (Church50.cs:1158-1167) is a leftover from the Midgard Seer quest (Loken, Morlin Caan) and does not describe this quest."],"uncertain":["Sister Blythe's final level and the adds' name and level come from NPC templates 12982 / 60160685 in the database.","Whether a line row for \"Passage to Eternity\" below level 50 exists for Cleric/Paladin in the world database (FinishedLastPart allows everyone when none exists)."]},{"key":"Defenders50","file":"scripts/quests/Albion/epic/Defenders50.cs","title":"Feast of the Decadent (level 50)","line":"Feast of the Decadent","realm":1,"minLevel":50,"maxLevel":50,"classes":[2,3,5,10,60],"requires":{"kind":"FinishedLastPart","line":"Feast of the Decadent","level":null,"text":"The player must have finished the last data quest part of \"Feast of the Decadent\" below level 50 that the player's class may take (highest MinLevel below 50 among the line's rows; no such row in the world = no gate) (Defenders50.cs:1480, Epics165.cs:59-78)."},"givers":[{"name":"Lidmann Halsey","region":1,"x":466464,"y":634554,"z":1954,"how":"looked up by name in realm Albion (WorldMgr.GetNPCsByName) within 3000 units of 466464,634554 in region 1; created by the script at these coordinates if missing","cite":"Defenders50.cs:104-136"}],"start":"Interact (right-click) with Lidmann Halsey: with no speech he opens the quest offer window (\"Feast of the Decadent (level 50): once again seek out Elvinia Dareal at Caer Witrin.\"). Accepting starts step 1. No whisper word. (Defenders50.cs:1301-1316)","steps":[{"n":1,"text":"Seek out Elvinia Dareal at Caer Witrin.","type":"talk","target":"Elvinia Dareal","targetIsOwnTrainer":false,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with an NPC of the player's realm whose name is exactly \"Elvinia Dareal\" (Epics165.IsNamed). The script neither looks her up nor creates her: she must already exist in the world. She says nothing from this script; the step just becomes 2.","cite":"Defenders50.cs:1320, 1340-1341; Epics165.cs:111-112"},{"n":2,"text":"Slay Cailleach Uragaig, the leader of the cailleach sisterhood in Lyonesse. Follow the road southwest past the lesser telamon and the two houses with the pikemen and pygmy goblins; the sisterhood lives in the ruins of pillars in the clearing straight west.","type":"kill","target":"Cailleach Uragaig","targetIsOwnTrainer":false,"count":1,"region":1,"pos":[316218,664484,2736],"spawnedByScript":true,"mobLevel":70,"gives":[],"takes":[],"note":"Any killed enemy named \"Cailleach Uragaig\" counts (name match on the quest owner's EnemyKilled event). Message \"Return to your trainer in Camelot.\" No sealed pouch is given in 1.65 mode.","cite":"Defenders50.cs:1321-1323, 1331-1337, 142-171"},{"n":3,"text":"Return to your trainer in Camelot.","type":"return","target":null,"targetIsOwnTrainer":true,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with any trainer of the player's own class (Epics165.IsOwnTrainer; location not checked): gives 1,937,768,448 experience and moves to step 4.","cite":"Defenders50.cs:1324, 1342-1346"},{"n":4,"text":"Seek out master smith Kalea Tait in Castle Sauvage for your reward. Make sure you have 6 free slots in your inventory.","type":"talk","target":"Kalea Tait","targetIsOwnTrainer":false,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with an NPC of the player's realm named exactly \"Kalea Tait\"; needs 6 free backpack slots. She is not looked up or created by the script. The quest finishes and the armour is given.","cite":"Defenders50.cs:1325, 1347-1348"}],"rewards":{"xp":1937768448,"xpNote":"Epics165.FinaleXp, given by the player's own trainer at the step 3 -> 4 talk (Defenders50.cs:1344); FinishQuest in 1.65 mode gives only the armour (lines 1633-1639).","money":null,"byClass":{"2":["ArmsmanEpicBoots","ArmsmanEpicArms","ArmsmanEpicGloves","ArmsmanEpicHelm","ArmsmanEpicLegs","ArmsmanEpicVest"],"3":["ScoutEpicArms","ScoutEpicBoots","ScoutEpicGloves","ScoutEpicHelm","ScoutEpicLegs","ScoutEpicVest"],"5":["TheurgistEpicArms","TheurgistEpicBoots","TheurgistEpicGloves","TheurgistEpicHelm","TheurgistEpicLegs","TheurgistEpicVest"],"10":["FriarEpicArms","FriarEpicBoots","FriarEpicGloves","FriarEpicHelm","FriarEpicLegs","FriarEpicVest"],"60":["MaulerAlbEpicArms","MaulerAlbEpicBoots","MaulerAlbEpicGloves","MaulerAlbEpicHelm","MaulerAlbEpicLegs","MaulerAlbEpicVest"]},"note":"GiveEpicArmour, Defenders50.cs:1651-1697. No money (AddMoney commented out, line 1648). The MaulerAlb templates are only looked up, never created by the script (lines 1219-1224)."},"monsters":[{"name":"Cailleach Uragaig","level":70,"region":1,"x":316218,"y":664484,"z":2736,"createdByScript":true,"cite":"Defenders50.cs:142-171 (looked up by name in realm None near 316218,664484; created when missing, Level 70 at line 163; CailleachUragaig.AddToWorld loads NPC template 12941, CailleachUragaig.cs:88, which may replace the level; respawn = SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes, line 91)"}],"repeatable":false,"notes":["Elvinia Dareal (step 1) and Kalea Tait (step 4) are expected to exist in the world as Albion-realm NPCs; the script never looks them up or creates them, it only compares the name of the NPC the player interacts with (Defenders50.cs:1340, 1347).","In 1.65 mode only Lidmann's Interact is hooked (TalkToLidmann165); the 'services', 'slay' and 'abort' whispers of the other flow are not registered, so there is no abort (Defenders50.cs:1230-1234, 1275-1288).","The \"This quest is not available to Maulers yet\" check lives in the non-1.65 TalkToLidmann only (lines 1364-1370); in 1.65 mode CheckQuestQualification still lists MaulerAlb (line 1465) and GiveEpicArmour would hand out the looked-up Mauler templates (null when they do not exist).","sealed_pouch is created at load but never given in 1.65 mode (shared Id_nb with Academy50 and Shadows50).","The comment block at the end (Defenders50.cs:1702-1711) is a leftover from the Midgard Seer quest."],"uncertain":["Positions and regions of Elvinia Dareal (Caer Witrin) and Kalea Tait (Castle Sauvage): not in the script.","Cailleach Uragaig's final level (NPC template 12941).","Whether a line row below level 50 exists for each of the five classes (FinishedLastPart allows the class when none exists)."]},{"key":"Shadows50","file":"scripts/quests/Albion/epic/Shadows50.cs","title":"Lord of Deceit (level 50)","line":"Lord of Deceit","realm":1,"minLevel":50,"maxLevel":50,"classes":[19,11,13,12,9,33],"requires":{"kind":"FinishedPart","line":"Lord of Deceit","level":48,"text":"The player must have finished the level 48 data quest part named \"Lord of Deceit\" (or \"Lord of Deceit (level N)\" with MinLevel 48) (Shadows50.cs:1978, Epics165.cs:23-32)."},"givers":[{"name":"Captain Rhodri","region":1,"x":null,"y":null,"z":null,"how":"looked up by name in realm Albion, region 1 (Epics165.FindNpc -> WorldMgr.GetNPCsByName); NOT created by the script: when he is missing the quest is not loaded in 1.65 mode","cite":"Shadows50.cs:1792-1796, 1801"}],"start":"Interact (right-click) with Captain Rhodri: with no speech he opens the quest offer window (\"Lord of Deceit (level 50): stop the Ellyll lord who stands behind the plot against Albion.\"). Accepting starts step 1. No whisper word. (Shadows50.cs:1817-1832)","steps":[{"n":1,"text":"Stop the Ellyll lord. Lord Elidyn stands in the ruin on the hill above the Ellyll camp near Caer Berkstead in the Pennine Mountains (loc 2k, 50k). Slay him; be prepared to fight a lot of adds when he comes running.","type":"kill","target":"Lord Elidyn","targetIsOwnTrainer":false,"count":1,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Any killed enemy named exactly \"Lord Elidyn\" counts (name match on the quest owner's EnemyKilled event). The script does not look him up or create him; the comment says his spawn is in the 1.65 database only (add-epics165.json).","cite":"Shadows50.cs:1836-1837, 1845-1846, 77-84"},{"n":2,"text":"Lord Elidyn is dead. Return to Captain Rhodri in Snowdonia Fortress.","type":"return","target":"Captain Rhodri","targetIsOwnTrainer":false,"count":null,"region":1,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":["<class>EpicArms","<class>EpicBoots","<class>EpicGloves","<class>EpicHelm","<class>EpicLegs","<class>EpicVest"],"takes":[],"note":"Interact with Captain Rhodri with 6 free backpack slots: he gives the six epic armour pieces of the player's class (see rewards.byClass) and the step becomes 3. Without the free slots: \"You do not have enough free space in your inventory!\" and nothing happens.","cite":"Shadows50.cs:1838, 1847-1852"},{"n":3,"text":"Captain Rhodri says your trainer has a second reward for you. Return to your trainer in Camelot.","type":"return","target":null,"targetIsOwnTrainer":true,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with any trainer of the player's own class (Epics165.IsOwnTrainer; location not checked): the quest finishes and gives 1,937,768,448 experience.","cite":"Shadows50.cs:1839, 1853-1855, 2131-2136"}],"rewards":{"xp":1937768448,"xpNote":"Epics165.FinaleXp in FinishQuest at the trainer (Shadows50.cs:2135).","money":null,"byClass":{"19":["ReaverEpicArms","ReaverEpicBoots","ReaverEpicGloves","ReaverEpicHelm","ReaverEpicLegs","ReaverEpicVest"],"11":["MercenaryEpicArms","MercenaryEpicBoots","MercenaryEpicGloves","MercenaryEpicHelm","MercenaryEpicLegs","MercenaryEpicVest"],"13":["CabalistEpicArms","CabalistEpicBoots","CabalistEpicGloves","CabalistEpicHelm","CabalistEpicLegs","CabalistEpicVest"],"9":["InfiltratorEpicArms","InfiltratorEpicBoots","InfiltratorEpicGloves","InfiltratorEpicHelm","InfiltratorEpicLegs","InfiltratorEpicVest"],"12":["NecromancerEpicArms","NecromancerEpicBoots","NecromancerEpicGloves","NecromancerEpicHelm","NecromancerEpicLegs","NecromancerEpicVest"],"33":["HereticEpicArms","HereticEpicBoots","HereticEpicGloves","HereticEpicHelm","HereticEpicLegs","HereticEpicVest"]},"note":"The armour is given by Captain Rhodri at step 2 (GiveEpicArmour, Shadows50.cs:1850, 2149-2213), not at FinishQuest. No money."},"monsters":[{"name":"Lord Elidyn","level":null,"region":null,"x":null,"y":null,"z":null,"createdByScript":false,"cite":"Shadows50.cs:84, 1845 (matched by name only; comment at lines 77-81: spawn in the 1.65 database only, add-epics165.json, Pennine Mountains loc 2k 50k)"},{"name":"Cailleach Uragaig","level":70,"region":1,"x":316218,"y":664484,"z":2736,"createdByScript":true,"cite":"Shadows50.cs:149-178 (still looked up / created at load, but NOT used by the 1.65 flow)"}],"repeatable":false,"notes":["The class constant questTitle is still \"Feast of the Decadent\" (Shadows50.cs:32); in 1.65 mode the quest's Name is Epics165.FinaleTitle(\"Lord of Deceit\") (line 2053) and the prerequisite line is Title165 = \"Lord of Deceit\" (lines 83, 1978).","ScriptLoaded still looks up or creates Lidmann Halsey (region 1, 466464,634554,1954) and Cailleach Uragaig before the 1.65 branch (Shadows50.cs:115-178); neither takes part in the 1.65 flow.","In 1.65 mode only Captain Rhodri's Interact is hooked; there are no whisper words and no abort (Shadows50.cs:1745-1749, 1790-1804).","Heretic (class 33) is in the qualification list and has armour templates (Shadows50.cs:1964, 1460-1741, 2203-2211).","sealed_pouch is created at load but never given in 1.65 mode."],"uncertain":["Captain Rhodri's coordinates (Snowdonia Fortress per the text): only region 1 is in the code.","Lord Elidyn's region, position and level: not in the script (database spawn)."]},{"key":"Essence50","file":"scripts/quests/Hibernia/epic/Essence50.cs","title":"The Moonstone Twin (level 50)","line":"The Moonstone Twin","realm":3,"minLevel":50,"maxLevel":50,"classes":[45,48,49,41],"requires":{"kind":"FinishedLastPart","line":"The Moonstone Twin","level":null,"text":"The player must have finished the last data quest part of \"The Moonstone Twin\" below level 50 that the player's class may take (the comment says the level 47 part; the code takes the highest MinLevel below 50 among the line's rows, and allows everyone when no row exists) (Essence50.cs:1481-1484, Epics165.cs:59-78)."},"givers":[{"name":"Brigit","region":201,"x":33105,"y":32909,"z":8008,"how":"looked up by name in realm Hibernia (WorldMgr.GetNPCsByName) within 3000 units of 32927,32743 in region 201; created by the script at 33105,32909,8008 if missing","cite":"Essence50.cs:118-148"}],"start":"Interact (right-click) with Brigit: with no speech she opens the quest offer window (\"The Moonstone Twin (level 50): slay Caithor in the Cursed Forest and bring back the moonstone's twin.\"). Accepting starts step 1. No whisper word. (Essence50.cs:1326-1341)","steps":[{"n":1,"text":"Kill the far dorochas guarding the steed along the west bank of the river in the Cursed Forest; a giant Caithor appears. Kill him and the true Caithor appears with another set of far dorochas. Kill the true Caithor and take the moonstone's twin.","type":"kill","target":"Caithor","targetIsOwnTrainer":false,"count":1,"region":200,"pos":[470547,531497,4984],"spawnedByScript":true,"mobLevel":null,"gives":["Moonstone"],"takes":[],"note":"Only the kill of the true Caithor counts (the target must be of class Caithor, not GhostOfCaithor). Chain: 5 far dorochas of class CaithorDorocha must die before Giant Caithor (level 62-65) becomes visible and attackable; when Giant Caithor dies the true Caithor spawns at the same spot with 4 far dorochas. On the kill: \"You collect the Moonstone from Caithor\", the Moonstone is given and the step becomes 2.","cite":"Essence50.cs:1345-1347, 1588-1597; Caithor.cs:52-83, 178-231, 250-259, 318-341"},{"n":2,"text":"Return to Brigit and give her the moonstone.","type":"deliver","target":"Brigit","targetIsOwnTrainer":false,"count":null,"region":201,"pos":[33105,32909,8008],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":["Moonstone"],"note":"Hand the Moonstone to an NPC named \"Brigit\" (name match on the GiveItem event): it is removed and the step becomes 3. No free-slot check here.","cite":"Essence50.cs:1348, 1355-1360"},{"n":3,"text":"Brigit sends you to Lauralaye near the vault keeper for your reward. Make sure you have 6 free slots in your inventory.","type":"talk","target":"Lauralaye","targetIsOwnTrainer":false,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with an NPC of the player's realm named exactly \"Lauralaye\" with 6 free backpack slots: the quest finishes (armour + experience). Lauralaye is not looked up or created by the script.","cite":"Essence50.cs:1349, 1361-1363; Epics165.cs:111-121"}],"rewards":{"xp":1937768448,"xpNote":"GainExperience(eXPSource.Quest, 1937768448, true) in FinishQuest, at Lauralaye (Essence50.cs:1672).","money":null,"byClass":{"45":["ChampionEpicArms","ChampionEpicBoots","ChampionEpicGloves","ChampionEpicHelm","ChampionEpicLegs","ChampionEpicVest"],"48":["BardEpicArms","BardEpicBoots","BardEpicGloves","BardEpicHelm","BardEpicLegs","BardEpicVest"],"41":["EnchanterEpicArms","EnchanterEpicBoots","EnchanterEpicGloves","EnchanterEpicHelm","EnchanterEpicLegs","EnchanterEpicVest"],"49":["NightshadeEpicArms","NightshadeEpicBoots","NightshadeEpicGloves","NightshadeEpicHelm","NightshadeEpicLegs","NightshadeEpicVest"]},"note":"FinishQuest, Essence50.cs:1629-1673; the same FinishQuest runs in both modes (it calls RemoveItem(Brigit, player, Moonstone) again at line 1631 although the stone was already taken at step 2). No money (AddMoney commented out, line 1673)."},"monsters":[{"name":"Giant Caithor","level":62,"region":200,"x":470547,"y":531497,"z":4984,"createdByScript":true,"cite":"Essence50.cs:153-182 (class GhostOfCaithor; looked up by name in realm None near 470547,531497 in region 200, created when missing with Level random 62-64 at line 174); Caithor.cs:185-186 sets Name \"Giant Caithor\" and Level random 62-65 on every AddToWorld; hidden (model 1, untargetable, peace) until 5 CaithorDorocha are dead, Caithor.cs:250-275"},{"name":"Caithor","level":null,"region":200,"x":470547,"y":531497,"z":4984,"createdByScript":true,"cite":"Caithor.cs:222-231 (class Caithor, spawned when Giant Caithor dies; NPC template 50023, line 54; no respawn; this is the kill the quest needs, Essence50.cs:1591)"},{"name":"far dorocha (class CaithorDorocha, NPC template 60160718)","level":null,"region":200,"x":null,"y":null,"z":null,"createdByScript":false,"cite":"Caithor.cs:318-341 (world mobs of class CaithorDorocha; each death counts toward the 5 needed while the true Caithor is not up); 4 more spawn around the true Caithor, Caithor.cs:67-83"}],"repeatable":false,"notes":["Step 1 is handled by the shared Notify code (Essence50.cs:1588-1597); steps 2 and 3 by Notify165 (lines 1600-1604, 1353-1364).","Lauralaye (step 3) is expected to exist as a Hibernia-realm NPC; the script never looks her up or creates her (Essence50.cs:1362).","In 1.65 mode only Brigit's Interact is hooked; the 'services', 'fulfill' and 'abort' whispers are not registered (Essence50.cs:1257-1261, 1300-1313).","Item templates are added to the database only when BaseQuest.SAVE_INTO_DATABASE is true (default true).","The comment block at the end (Essence50.cs:1678-1687) is a leftover from the Midgard Seer quest."],"uncertain":["Lauralaye's region and position: not in the script.","The true Caithor's level and the far dorochas' name, level and positions (NPC templates 50023 / 60160718 and database spawns).","Which level the last pre-50 part has for these classes in the world database (comment says 47)."]},{"key":"Focus50","file":"scripts/quests/Hibernia/epic/Focus50.cs","title":"Unnatural Powers (level 50)","line":"Unnatural Powers","realm":3,"minLevel":50,"maxLevel":50,"classes":[44,50,62,46,40],"requires":{"kind":"FinishedLastPart","line":"Unnatural Powers","level":null,"text":"The player must have finished the last data quest part of \"Unnatural Powers\" below level 50 that the player's class may take (the comment says the level 47 part; the code takes the highest MinLevel below 50 among the line's rows, and allows everyone when no row exists) (Focus50.cs:1475-1478, Epics165.cs:59-78)."},"givers":[{"name":"Ainrebh","region":200,"x":421281,"y":516273,"z":1877,"how":"looked up by name in realm Hibernia (WorldMgr.GetNPCsByName) within 3000 units of 421281,516273 in region 200; created by the script at these coordinates if missing","cite":"Focus50.cs:119-148"}],"start":"Interact (right-click) with Ainrebh: with no speech she opens the quest offer window (\"Unnatural Powers (level 50): slay the Green Maw in the Cursed Forest and bring its key to Ainrebh in Caille.\"). Accepting starts step 1. No whisper word. (Focus50.cs:1336-1351)","steps":[{"n":1,"text":"Seek out Green Maw in Cursed Forest and kill it!","type":"kill","target":"Part of Green Maw","targetIsOwnTrainer":false,"count":2,"region":200,"pos":[488306,521440,6328],"spawnedByScript":true,"mobLevel":50,"gives":["GreenMaw_key"],"takes":[],"note":"The key is NOT given for Green Maw's own death. Green Maw's death spawns 3 \"Part of Green Maw\" (level 58-60); when those 3 are dead 4 more spawn (level 53-55); when those 4 are dead the last 2 spawn (level 50, class GreenMawAdd3). The quest counts kills of the last kind only and needs 2: then \"You collect Green Maw's Key\", the key is given and the step becomes 2. The counter is a plain field on the quest object (not saved). All three kinds share the name \"Part of Green Maw\". pos is Green Maw's spot.","cite":"Focus50.cs:1562, 1580-1595, 52; GreenMaw.cs:86-100, 167-198, 259-290, 318-361"},{"n":2,"text":"Return to Ainrebh and give her Green Maw's Key!","type":"deliver","target":"Ainrebh","targetIsOwnTrainer":false,"count":null,"region":200,"pos":[421281,516273,1877],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":["GreenMaw_key"],"note":"Hand GreenMaw's Key to an NPC named \"Ainrebh\" (name match on the GiveItem event) with 6 free backpack slots; otherwise \"You do not have enough free space in your inventory!\". She says \"You have earned this Epic Armor, wear it with honor!\" and the quest finishes.","cite":"Focus50.cs:1564, 1596-1609"}],"rewards":{"xp":1937768448,"xpNote":"GainExperience(eXPSource.Quest, 1937768448, true) in FinishQuest (Focus50.cs:1672).","money":null,"byClass":{"44":["HeroEpicArms","HeroEpicBoots","HeroEpicGloves","HeroEpicHelm","HeroEpicLegs","HeroEpicVest"],"50":["RangerEpicArms","RangerEpicBoots","RangerEpicGloves","RangerEpicHelm","RangerEpicLegs","RangerEpicVest"],"40":["EldritchEpicArms","EldritchEpicBoots","EldritchEpicGloves","EldritchEpicHelm","EldritchEpicLegs","EldritchEpicVest"],"46":["WardenEpicArms","WardenEpicBoots","WardenEpicGloves","WardenEpicHelm","WardenEpicLegs","WardenEpicVest"],"62":["MaulerHibEpicBoots","MaulerHibEpicArms","MaulerHibEpicGloves","MaulerHibEpicHelm","MaulerHibEpicVest","MaulerHibEpicLegs"]},"note":"FinishQuest, Focus50.cs:1620-1673 (same code in both modes; the key is removed at line 1622). No money (AddMoney commented out). The MaulerHib templates are only looked up, never created (lines 1256-1261)."},"monsters":[{"name":"Green Maw","level":65,"region":200,"x":488306,"y":521440,"z":6328,"createdByScript":true,"cite":"Focus50.cs:153-182 (looked up by name in realm None near 488306,521440 in region 200; created when missing, Level 65 at line 174; GreenMaw.AddToWorld loads NPC template 50022, GreenMaw.cs:73, which may replace the level; respawn = SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes, line 78)"},{"name":"Part of Green Maw","level":58,"region":200,"x":488306,"y":521440,"z":6328,"createdByScript":true,"cite":"GreenMaw.cs:93-100, 134-176 (class GreenMawAdd, 3 spawn within 50 units of Green Maw's corpse, level random 58-60)"},{"name":"Part of Green Maw","level":53,"region":200,"x":488306,"y":521440,"z":6328,"createdByScript":true,"cite":"GreenMaw.cs:184-198, 226-268 (class GreenMawAdd2, 4 spawn when the 3 first parts are dead, level random 53-55)"},{"name":"Part of Green Maw","level":50,"region":200,"x":488306,"y":521440,"z":6328,"createdByScript":true,"cite":"GreenMaw.cs:276-290, 318-361 (class GreenMawAdd3, 2 spawn when the 4 second parts are dead, level 50; these 2 are the quest kills, Focus50.cs:1584)"}],"repeatable":false,"notes":["This quest has no 1.65-specific Notify or Description: in 1.65 mode only the title (Focus50.cs:1551), the prerequisite (line 1477) and the start (quest window on Interact, lines 1267-1271, 1310-1351) differ; the steps are the script's own (comment at line 56).","In 1.65 mode only Ainrebh's Interact is hooked: the 'services', 'fulfill' and 'abort' whispers are not registered, so step 2 can only be completed by handing over the key (Focus50.cs:1596-1609).","The \"not available to Maulers yet\" check is in the non-1.65 TalkToAinrebh only (lines 1366-1372); CheckQuestQualification still lists MaulerHib (line 1460).","The part positions are relative to the corpse of the mob that spawned them (within 50 units), so the listed coordinates are Green Maw's.","The comment block at the end (Focus50.cs:1678-1687) is a leftover from the Midgard Seer quest."],"uncertain":["Green Maw's final level (NPC template 50022).","Whether the add counter survives a relog (instance field, Focus50.cs:52).","Which level the last pre-50 part has for these classes in the world database (comment says 47)."]},{"key":"Harmony50","file":"scripts/quests/Hibernia/epic/Harmony50.cs","title":"Last Heir (level 50)","line":"Last Heir","realm":3,"minLevel":50,"maxLevel":50,"classes":[43,47,56,55,42,58,39],"requires":{"kind":"FinishedPart","line":"Last Heir","level":47,"text":"The player must have finished the level 47 data quest part named \"Last Heir\" (or \"Last Heir (level N)\" with MinLevel 47) (Harmony50.cs:2325, Epics165.cs:23-32)."},"givers":[{"name":null,"region":null,"x":null,"y":null,"z":null,"how":"Epics165.TrainersOf(\"Last Heir\", 47, eRealm.Hibernia): the trainers who give the level 47 part, i.e. every GameTrainer of realm Hibernia whose name and region are the StartName / StartRegionID of a DataQuest row with MinLevel 47 and Name exactly \"Last Heir\". Each gets an Interact handler and the quest to give; the offer is only made by the player's own class trainer (Epics165.IsOwnTrainer). None found = quest not loaded in 1.65 mode (warning logged).","cite":"Harmony50.cs:2115-2133, 2149-2157; Epics165.cs:91-108"}],"start":"Interact (right-click) with your own class trainer among the Last Heir level 47 trainers (Path of Harmony trainers in Tir na Nog per the comment): the quest offer window opens (\"Last Heir (level 50): Morven is dead, but his influence is not.\"). Accepting starts step 1. No whisper word. (Harmony50.cs:2149-2165, 2363-2368)","steps":[{"n":1,"text":"Head to the Cursed Forest and kill Cailean the named tree at location 25K 25K. He calls other trees to his aid; bring a group with crowd control.","type":"kill","target":"Cailean","targetIsOwnTrainer":false,"count":1,"region":200,"pos":[479042,508134,4569],"spawnedByScript":true,"mobLevel":65,"gives":["Horn"],"takes":[],"note":"Any killed enemy named \"Cailean\" counts, but ONLY when the player has 1 free backpack slot at the moment of the kill; with a full backpack the kill is silently ignored. The Horn is given and the step becomes 2 (no message in 1.65 mode). Cailean calls walking trees (level 48-50) and rotted trees (level 40-44).","cite":"Harmony50.cs:2169-2170, 2179-2184, 163-192; Cailean.cs:199-240, 368-369, 508-509"},{"n":2,"text":"Return to the parthanan farm northeast of Innis Carthaig and give the horn to Donnchadh (loc 57k, 26k).","type":"deliver","target":"Donnchadh","targetIsOwnTrainer":false,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":["Horn"],"note":"Hand the Horn to an NPC of the player's realm named exactly \"Donnchadh\": it is removed and the step becomes 3. Donnchadh is not looked up or created by the script.","cite":"Harmony50.cs:2171, 2185-2190"},{"n":3,"text":"Head to Innis Carthaig and speak to Chieftess Niamh.","type":"talk","target":"Chieftess Niamh","targetIsOwnTrainer":false,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":["Revelin`s Ring"],"takes":[],"note":"Interact with an NPC of the player's realm named exactly \"Chieftess Niamh\": gives 1,937,768,448 experience and the step becomes 4. Optional secret reward BEFORE that: while on step 3, interact with Revelin the stable boy (region 200, 343442,706235,6336) with 1 free backpack slot to receive Revelin`s Ring once (custom property \"revelinring\"); it is no longer available after speaking to the Chieftess. Chieftess Niamh is not looked up or created by the script.","cite":"Harmony50.cs:2172, 2191-2204, 128-158, 2117"},{"n":4,"text":"Speak to Talriese in Innis Carthaig to receive your full set of epic armor.","type":"talk","target":"Talriese","targetIsOwnTrainer":false,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with an NPC of the player's realm named exactly \"Talriese\" with 6 free backpack slots: the quest finishes and the armour is given. Talriese is not looked up or created by the script.","cite":"Harmony50.cs:2173, 2205-2206"}],"rewards":{"xp":1937768448,"xpNote":"Epics165.FinaleXp, given by Chieftess Niamh at the step 3 -> 4 talk (Harmony50.cs:2202); FinishQuest in 1.65 mode gives only the armour (lines 2475-2481).","money":null,"byClass":{"43":["BlademasterEpicArms","BlademasterEpicBoots","BlademasterEpicGloves","BlademasterEpicHelm","BlademasterEpicLegs","BlademasterEpicVest"],"55":["AnimistEpicArms","AnimistEpicBoots","AnimistEpicGloves","AnimistEpicHelm","AnimistEpicLegs","AnimistEpicVest"],"42":["MentalistEpicArms","MentalistEpicBoots","MentalistEpicGloves","MentalistEpicHelm","MentalistEpicLegs","MentalistEpicVest"],"47":["DruidEpicArms","DruidEpicBoots","DruidEpicGloves","DruidEpicHelm","DruidEpicLegs","DruidEpicVest"],"56":["ValewalkerEpicArms","ValewalkerEpicBoots","ValewalkerEpicGloves","ValewalkerEpicHelm","ValewalkerEpicLegs","ValewalkerEpicVest"],"58":["VampiirEpicArms","VampiirEpicBoots","VampiirEpicGloves","VampiirEpicHelm","VampiirEpicLegs","VampiirEpicVest"],"39":["BainsheeEpicArms","BainsheeEpicBoots","BainsheeEpicGloves","BainsheeEpicHelm","BainsheeEpicLegs","BainsheeEpicVest"]},"note":"GiveEpicArmour at Talriese, Harmony50.cs:2493-2567. Optional extra for every class: Revelin`s Ring from Revelin during step 3 (lines 2193-2199; template looked up, not created). No money."},"monsters":[{"name":"Cailean","level":65,"region":200,"x":479042,"y":508134,"z":4569,"createdByScript":true,"cite":"Harmony50.cs:163-192 (looked up by name in realm None near 479042,508134 in region 200; created when missing, Level 65 at line 184; Cailean.AddToWorld loads NPC template 60158846, Cailean.cs:89, which may replace the level; respawn = SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes, line 92)"},{"name":"walking tree","level":48,"region":200,"x":479042,"y":508134,"z":4569,"createdByScript":true,"cite":"Cailean.cs:199-205, 337-371 (4-5 spawned by Cailean's brain within 500 units of him, level random 48-50; not needed for the quest)"},{"name":"rotted tree","level":40,"region":200,"x":479778,"y":508293,"z":4534,"createdByScript":true,"cite":"Cailean.cs:217-240, 477-511 (8-10 spawned by Cailean's brain around 479778,508293,4534 / 478647,508450,4639 / 479444,508548,4532, level random 40-44; not needed for the quest)"}],"repeatable":false,"notes":["The class constant questTitle is still \"The Horn Twin\" (Harmony50.cs:33); in 1.65 mode the Name is Epics165.FinaleTitle(\"Last Heir\") (line 2400) and the prerequisite line is Title165 = \"Last Heir\" (lines 93, 2325).","Epics165.TrainersOf compares the row name with string.Equals to \"Last Heir\" (Epics165.cs:96), while the prerequisite uses IsPartOf (also accepts \"Last Heir (level 47)\"): if the level 47 rows carry a \"(level 47)\" suffix no trainer is found and the quest is not loaded.","Donnchadh, Chieftess Niamh and Talriese are expected to exist as Hibernia-realm NPCs; the script never looks them up or creates them, it only compares names (Harmony50.cs:2186, 2200, 2205).","Revelin is still looked up or created at load (region 200, 343442,706235,6336) but in 1.65 mode he only hands out the optional ring; his Interact/Whisper handlers of the other flow are not registered (Harmony50.cs:128-158, 2072-2076).","In 1.65 mode only the AcceptQuest event is hooked (no decline handler, no abort whisper) (Harmony50.cs:2125, 2363-2368).","The ring template id contains a backtick: \"Revelin`s Ring\" (Harmony50.cs:2117)."],"uncertain":["Which trainers TrainersOf returns (depends on the DataQuest rows named \"Last Heir\" with MinLevel 47 in the world database) and their positions.","Regions and positions of Donnchadh, Chieftess Niamh and Talriese: not in the script.","Whether the item template \"Revelin`s Ring\" exists in the database (the ring is skipped when it does not).","Cailean's final level (NPC template 60158846)."]},{"key":"MidgardRogue50","file":"scripts/quests/Midgard/epic/MidgardRogue50.cs","title":"War Concluded (level 50)","line":"War Concluded","realm":2,"minLevel":50,"maxLevel":50,"classes":[23,25],"requires":{"kind":"FinishedLastPart","line":"War Concluded","level":null,"text":"The player must have finished the last data quest part of \"War Concluded\" below level 50 that the player's class may take (highest MinLevel below 50 among the line's rows; the Epics165 comment says this line's last part is level 45; no such row = no gate) (MidgardRogue50.cs:988-991, Epics165.cs:53-78)."},"givers":[{"name":"Masrim","region":100,"x":749099,"y":813104,"z":4437,"how":"looked up by name in realm Midgard (WorldMgr.GetNPCsByName) within 3000 units of 749099,813104 in region 100; created by the script at these coordinates if missing","cite":"MidgardRogue50.cs:102-131"}],"start":"Interact (right-click) with Masrim: she says \"Midgard needs your [services]\". Whisper \"services\": the quest offer window opens (\"Will you help Masrim [Rogue Level 50 Epic]?\"). Accepting starts step 1 with the popup \"Kill Oona in Raumarik!\"; declining shows \"Our God forgives your laziness, just look out for stray lightning bolts.\" This script has no 1.65-specific start. (MidgardRogue50.cs:830-864, 1034-1053)","steps":[{"n":1,"text":"Seek out Oona in Raumarik and kill her! When you get into Raumarik, go west to the river and follow the river south until the river divides.","type":"kill","target":"Oona","targetIsOwnTrainer":false,"count":1,"region":100,"pos":[607233,786850,4384],"spawnedByScript":true,"mobLevel":65,"gives":["oona_head"],"takes":[],"note":"Any killed enemy named \"Oona\" counts (name match on the quest owner's EnemyKilled event): \"You collect Oona's Head\", the head is given, step 2.","cite":"MidgardRogue50.cs:1070, 1090-1098, 136-166"},{"n":2,"text":"Return to Masrim and give her Oona's Head!","type":"deliver","target":"Masrim","targetIsOwnTrainer":false,"count":null,"region":100,"pos":[749099,813104,4437],"spawnedByScript":true,"mobLevel":null,"gives":["sealed_pouch"],"takes":["oona_head"],"note":"Either hand Oona's Head to Masrim (name match on the GiveItem event), or interact and whisper \"sealed pouch\" to her (the head is then removed by the script). She says \"Take this sealed pouch to Morlin Caan in Jordheim for your reward!\", gives the Sealed Pouch, step 3.","cite":"MidgardRogue50.cs:1072, 1100-1110, 840-842, 871-879"},{"n":3,"text":"Go to Morlin Caan in Jordheim and give him the Sealed Pouch for your reward!","type":"deliver","target":"Morlin Caan","targetIsOwnTrainer":false,"count":null,"region":101,"pos":[33400,33620,8023],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":["sealed_pouch"],"note":"Either hand the Sealed Pouch to Morlin Caan, or interact (\"Were you able to [fulfill] your given task?\") and whisper \"fulfill\". Needs 6 free backpack slots, otherwise \"You do not have enough free space in your inventory!\". He says \"You have earned this Epic Armor, wear it with honor!\" and the quest finishes (the pouch is removed either way).","cite":"MidgardRogue50.cs:1074, 1113-1127, 912-947, 1140"}],"rewards":{"xp":1937768448,"xpNote":"GainExperience(eXPSource.Quest, 1937768448, true) in FinishQuest (MidgardRogue50.cs:1163).","money":null,"byClass":{"23":["ShadowbladeEpicArms","ShadowbladeEpicBoots","ShadowbladeEpicGloves","ShadowbladeEpicHelm","ShadowbladeEpicLegs","ShadowbladeEpicVest"],"25":["HunterEpicArms","HunterEpicBoots","HunterEpicGloves","HunterEpicHelm","HunterEpicLegs","HunterEpicVest"]},"note":"FinishQuest, MidgardRogue50.cs:1138-1164. No money (AddMoney commented out, line 1164)."},"monsters":[{"name":"Oona","level":65,"region":100,"x":607233,"y":786850,"z":4384,"createdByScript":true,"cite":"MidgardRogue50.cs:136-166 (looked up by name in realm None near 607233,786850 in region 100; created when missing, Level 65 at line 157, GHOST flag; Oona.AddToWorld loads NPC template 60164669, Oona.cs:79, which may replace the level; respawn = SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes, line 81)"},{"name":"undead <race of the player> (class OonaUndeadAdd)","level":36,"region":100,"x":null,"y":null,"z":null,"createdByScript":true,"cite":"Oona.cs:114-139, 324-334 (adds named \"undead \" + the player's race, spawned at a player's position, level random 36-38; not needed for the quest)"}],"repeatable":false,"notes":["The script has no Classic165Mode branch except the title (MidgardRogue50.cs:1059) and the prerequisite (line 990): start, whispers, steps, Description and FinishQuest are the same in both modes.","The abort whisper works in 1.65 mode too: whisper \"abort\" to Masrim while on the quest, then confirm (MidgardRogue50.cs:880-882, 1001-1016); AbortQuest removes only the sealed pouch (line 1135).","Only Interact and WhisperReceive are registered on Masrim and Morlin Caan (lines 784-788); the ReceiveItem branches inside TalkToMasrim / TalkToMorlinCaan never fire, item hand-ins go through Notify (GiveItem event).","Morlin Caan is looked up in realm Midgard near 33400,33620 in region 101 or created there with guild name \"Smith\" (MidgardRogue50.cs:171-200).","TalkToMorlinCaan returns early unless Masrim.CanGiveQuest(...) > 0 (line 906), i.e. the player must still qualify (class, level, or already on the quest).","sealed_pouch is the same Id_nb the Albion scripts create.","The comment block at the end (lines 1169-1178) is the Seer quest's walkthrough, not this quest's."],"uncertain":["Oona's final level (NPC template 60164669).","Which level the last pre-50 part of \"War Concluded\" has for Shadowblade/Hunter in the world database."]},{"key":"Mystic50","file":"scripts/quests/Midgard/epic/Mystic50.cs","title":"Saving the Clan (level 50)","line":"Saving the Clan","realm":2,"minLevel":50,"maxLevel":50,"classes":[27,29,30,59],"requires":{"kind":"FinishedLastPart","line":"Saving the Clan","level":null,"text":"The player must have finished the last data quest part of \"Saving the Clan\" below level 50 that the player's class may take (highest MinLevel below 50 among the line's rows; a comment in the script mentions level 45/48 parts given by Danica; no such row = no gate) (Mystic50.cs:1446-1449, 121-123, Epics165.cs:59-78)."},"givers":[{"name":"Danica","region":100,"x":803559,"y":723329,"z":4719,"how":"looked up by name in realm Midgard (WorldMgr.GetNPCsByName) in region 100: first the Danica nearest to 804440,722267 (within 3000 units; the comment says the world Danica stands there), else the one near 802818,727413; created by the script at 803559,723329,4719 if none is found","cite":"Mystic50.cs:119-154"}],"start":"Interact (right-click) with Danica: \"...That [worries me].\" Whisper \"worries me\", then whisper \"face Kelic\": the quest offer window opens (\"Will you face Kelic [Mystic Level 50 Epic]?\"). Accepting starts step 1 and Danica gives the directions to Kelic. This script has no 1.65-specific start. (Mystic50.cs:1360-1378, 1492-1516)","steps":[{"n":1,"text":"Find Kelic in Raumarik. Head to the river and go north. At the end go northwest to the next river, cross and head west. Follow the snowline until you reach a group of trees.","type":"kill","target":"Kelic","targetIsOwnTrainer":false,"count":1,"region":100,"pos":[621577,745848,4593],"spawnedByScript":true,"mobLevel":65,"gives":["kelics_totem"],"takes":[],"note":"Any killed enemy named \"Kelic\" counts (name match on the quest owner's EnemyKilled event): Kelic's Totem is given, \"Kelic drops his Totem and you pick it up!\", step 2.","cite":"Mystic50.cs:1533, 1553-1561, 159-190"},{"n":2,"text":"Return to Danica and give her the totem!","type":"deliver","target":"Danica","targetIsOwnTrainer":false,"count":null,"region":100,"pos":[803559,723329,4719],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":["kelics_totem"],"note":"Either hand Kelic's Totem to Danica (name match on the GiveItem event), or interact (\"...Give me Kelic's [totem] now! Hurry!\") and whisper \"totem\"; the totem is removed and the step becomes 3.","cite":"Mystic50.cs:1535, 1563-1572, 1352-1354, 1398-1408"},{"n":3,"text":"Speak with Danica for your reward!","type":"whisper","target":"Danica","targetIsOwnTrainer":false,"count":null,"region":100,"pos":[803559,723329,4719],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with Danica (\"...you can [take them] with our thanks!\") and whisper \"take them\" with 6 free backpack slots; otherwise \"You do not have enough free space in your inventory!\". The quest finishes; this is the only way to finish (no hand-in).","cite":"Mystic50.cs:1537, 1355-1357, 1385-1397"}],"rewards":{"xp":1937768448,"xpNote":"GainExperience(eXPSource.Quest, 1937768448, true) in FinishQuest (Mystic50.cs:1633).","money":null,"byClass":{"27":["SpiritmasterEpicArms","SpiritmasterEpicBoots","SpiritmasterEpicGloves","SpiritmasterEpicHelm","SpiritmasterEpicLegs","SpiritmasterEpicVest"],"29":["RunemasterEpicArms","RunemasterEpicBoots","RunemasterEpicGloves","RunemasterEpicHelm","RunemasterEpicLegs","RunemasterEpicVest"],"30":["BonedancerEpicArms","BonedancerEpicBoots","BonedancerEpicGloves","BonedancerEpicHelm","BonedancerEpicLegs","BonedancerEpicVest"],"59":["WarlockEpicArms","WarlockEpicBoots","WarlockEpicGloves","WarlockEpicHelm","WarlockEpicLegs","WarlockEpicVest"]},"note":"FinishQuest, Mystic50.cs:1584-1634; Danica says \"May it serve you well, knowing that you have helped preserve the history of Midgard!\". No money (AddMoney commented out, line 1634)."},"monsters":[{"name":"Kelic","level":65,"region":100,"x":621577,"y":745848,"z":4593,"createdByScript":true,"cite":"Mystic50.cs:159-190 (looked up by name in realm None near 621577,745848 in region 100; created when missing, Level 65 at line 180, GHOST flag; Kelic.AddToWorld loads NPC template 50035, Kelic.cs:78, which may replace the level; respawn = SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes, line 81)"}],"repeatable":false,"notes":["The script has no Classic165Mode branch except the title (Mystic50.cs:1522) and the prerequisite (line 1448): start, whispers, steps, Description and FinishQuest are the same in both modes.","The abort whisper works in 1.65 mode: whisper \"abort\" to Danica while on the quest and confirm (Mystic50.cs:1409-1411, 1459-1474); AbortQuest removes the totem (line 1581).","Only Interact and WhisperReceive are registered on Danica (lines 1298-1299); the ReceiveItem branch in TalkToDanica never fires, the totem hand-in goes through Notify (GiveItem event).","In CheckPlayerAcceptQuest the long direction speech (lines 1511-1516) sits after the if/else, so Danica also says it when the offer is declined.","Warlock (class 59) is in the qualification list and has armour templates created by the script (lines 1440, 992-1290).","The walkthrough comment at the end (lines 1640-1649) is the Seer quest's, not this quest's."],"uncertain":["Kelic's final level (NPC template 50035).","Which Danica is picked at runtime and her exact position (world NPC near 804440,722267 per the comment).","Which level the last pre-50 part of \"Saving the Clan\" has per class in the world database."]},{"key":"Seer50","file":"scripts/quests/Midgard/epic/Seer50.cs","title":"The Desire of a God (level 50)","line":"The Desire of a God","realm":2,"minLevel":50,"maxLevel":50,"classes":[28,26],"requires":{"kind":"FinishedLastPart","line":"The Desire of a God","level":null,"text":"The player must have finished the last data quest part of \"The Desire of a God\" below level 50 that the player's class may take (highest MinLevel below 50 among the line's rows; no such row = no gate) (Seer50.cs:1004-1007, Epics165.cs:59-78)."},"givers":[{"name":"Inaksha","region":100,"x":805929,"y":702449,"z":4960,"how":"looked up by name in realm Midgard (WorldMgr.GetNPCsByName) within 3000 units of 805929,702449 in region 100; created by the script at these coordinates if missing","cite":"Seer50.cs:103-132"}],"start":"Interact (right-click) with Inaksha: \"Midgard needs your [services].\" Whisper \"services\": the quest offer window opens (\"Will you help Inaksha [Seer Level 50 Epic]?\"). Accepting starts step 1 with the popup \"Good now go kill him!\". This script has no 1.65-specific start. (Seer50.cs:849-865, 1050-1069)","steps":[{"n":1,"text":"Seek out Loken in Raumarik and kill him!","type":"kill","target":"Loken","targetIsOwnTrainer":false,"count":1,"region":100,"pos":[636784,762433,4596],"spawnedByScript":true,"mobLevel":65,"gives":["ball_of_flame"],"takes":[],"note":"Any killed enemy named \"Loken\" counts (name match on the quest owner's EnemyKilled event): \"You get a ball of flame\", the Ball of Flame is given, step 2. Loken has 2 wolves named \"Loken's companion\" (level 50).","cite":"Seer50.cs:1086, 1108-1117, 137-166; Loken.cs:99-105, 289-301"},{"n":2,"text":"Return to Inaksha and give her the Ball of Flame!","type":"deliver","target":"Inaksha","targetIsOwnTrainer":false,"count":null,"region":100,"pos":[805929,702449,4960],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":["ball_of_flame"],"note":"Either hand the Ball of Flame to Inaksha (name match on the GiveItem event; this path only sets step 3 and does not itself call RemoveItem), or interact (\"...do you [have something] for me?\") and whisper \"have something\" (the ball is removed by the script). She answers \"Great! Please visit Miri in Jordheim now and bring her the [sealed pouch].\"","cite":"Seer50.cs:1088, 1119-1127, 840-842, 872-879"},{"n":3,"text":"Talk with Inaksha about Loken's demise!","type":"whisper","target":"Inaksha","targetIsOwnTrainer":false,"count":null,"region":100,"pos":[805929,702449,4960],"spawnedByScript":true,"mobLevel":null,"gives":["sealed_pouch"],"takes":[],"note":"Whisper \"sealed pouch\" to Inaksha (the word is offered in her step 2 answer and when interacting on step 3): \"Take this sealed pouch to Miri in Jordheim for your reward!\", the Sealed Pouch is given, step 4. This is the only way to pass step 3.","cite":"Seer50.cs:1090, 843-845, 880-887"},{"n":4,"text":"Go to Miri in Jordheim and give her the Sealed Pouch for your reward!","type":"deliver","target":"Miri","targetIsOwnTrainer":false,"count":null,"region":101,"pos":[30641,32093,8305],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":["sealed_pouch"],"note":"Either hand the Sealed Pouch to Miri, or interact (\"Were you able to [fulfill] your given task?\") and whisper \"fulfill\". Needs 6 free backpack slots, otherwise \"You do not have enough free space in your inventory!\". \"You have earned this Epic Armor, wear it with honor!\" and the quest finishes (the pouch is removed in FinishQuest).","cite":"Seer50.cs:1092, 1129-1143, 924-961, 1156"}],"rewards":{"xp":1937768448,"xpNote":"GainExperience(eXPSource.Quest, 1937768448, true) in FinishQuest (Seer50.cs:1179).","money":null,"byClass":{"28":["ShamanEpicArms","ShamanEpicBoots","ShamanEpicGloves","ShamanEpicHelm","ShamanEpicLegs","ShamanEpicVest"],"26":["HealerEpicArms","HealerEpicBoots","HealerEpicGloves","HealerEpicHelm","HealerEpicLegs","HealerEpicVest"]},"note":"FinishQuest, Seer50.cs:1154-1180. No money (AddMoney commented out, line 1180)."},"monsters":[{"name":"Loken","level":65,"region":100,"x":636784,"y":762433,"z":4596,"createdByScript":true,"cite":"Seer50.cs:137-166 (looked up by name in realm None near 636784,762433 in region 100; created when missing, Level 65 at line 158; Loken.AddToWorld loads NPC template 60163372, Loken.cs:78, which may replace the level; respawn = SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes, line 82)"},{"name":"Loken's companion","level":50,"region":100,"x":636780,"y":762427,"z":4597,"createdByScript":true,"cite":"Loken.cs:99-105, 180-188, 289-309 (class LokenWolf, 2 spawn within 100 units of this point; not needed for the quest)"}],"repeatable":false,"notes":["The script has no Classic165Mode branch except the title (Seer50.cs:1075) and the prerequisite (line 1006): start, whispers, steps, Description and FinishQuest are the same in both modes.","The abort whisper works in 1.65 mode: whisper \"abort\" to Inaksha while on the quest and confirm (Seer50.cs:888-890, 1017-1032); AbortQuest removes the pouch and the ball (lines 1150-1151).","Only Interact and WhisperReceive are registered on Inaksha and Miri (lines 786-789); the ReceiveItem branches in TalkToInaksha / TalkToMiri never fire, item hand-ins go through Notify (GiveItem event).","Miri is looked up in realm Midgard near 30641,32093 in region 101 or created there (Seer50.cs:171-200). A player not on the quest who interacts with her hears \"Danica and I need your help to seek out Loken in Raumarik and to kill him!\" (line 935), but only when the player qualifies for the quest (line 918).","sealed_pouch is the same Id_nb the other epic scripts create."],"uncertain":["Whether the Ball of Flame leaves the inventory when it is handed to Inaksha on step 2: Notify does not call RemoveItem on that path (Seer50.cs:1119-1127); only the whisper path does (line 875).","Loken's final level (NPC template 60163372).","Which level the last pre-50 part of \"The Desire of a God\" has per class in the world database."]},{"key":"Viking50","file":"scripts/quests/Midgard/epic/Viking50.cs","title":"An End to the Daggers (level 50)","line":"An End to the Daggers","realm":2,"minLevel":50,"maxLevel":50,"classes":[22,31,21,24,32,61,34],"requires":{"kind":"FinishedLastPart","line":"An End to the Daggers","level":null,"text":"The player must have finished the last data quest part of \"An End to the Daggers\" below level 50 that the player's class may take (highest MinLevel below 50 among the line's rows; no such row = no gate) (Viking50.cs:2127-2130, Epics165.cs:59-78)."},"givers":[{"name":"Lynnleigh","region":100,"x":760118,"y":758453,"z":4737,"how":"looked up by name in realm Midgard (WorldMgr.GetNPCsByName) within 3000 units of 760118,758453 in region 100; created by the script at these coordinates if missing","cite":"Viking50.cs:132-161"}],"start":"Interact (right-click) with Lynnleigh: \"...That [worries me].\" Whisper \"worries me\", then whisper \"face Ydenia\": the quest offer window opens (\"Will you face Ydenia [Viking Level 50 Epic]?\"). Accepting starts step 1. This script has no 1.65-specific start. (Viking50.cs:1958-1977, 2173-2192)","steps":[{"n":1,"text":"Seek out Ydenia in Vanern Swamp and kill her!","type":"kill","target":"Ydenia of the Seithkona","targetIsOwnTrainer":false,"count":1,"region":100,"pos":[665094,894559,1791],"spawnedByScript":true,"mobLevel":65,"gives":["tome_enchantments"],"takes":[],"note":"Any killed enemy named \"Ydenia of the Seithkona\" counts (name match on the quest owner's EnemyKilled event): the Tome of Enchantments is given, \"Ydenia drops the Tome of Enchantments and you pick it up!\", step 2. Lynnleigh's hint on interact: \"You can find her on an island in the swamp.\"","cite":"Viking50.cs:2209, 2231-2240, 201-232, 1945"},{"n":2,"text":"Return to Lynnleigh and give her Tome of Enchantments!","type":"deliver","target":"Lynnleigh","targetIsOwnTrainer":false,"count":null,"region":100,"pos":[760118,758453,4737],"spawnedByScript":true,"mobLevel":null,"gives":["sealed_pouch"],"takes":["tome_enchantments"],"note":"Path A: hand the Tome of Enchantments to Lynnleigh (name match on the GiveItem event): the tome is removed, she says \"Take this sealed pouch to Elizabeth in Mularn for your reward!\", gives the Sealed Pouch, step 3. Path B: interact (\"Hello Adventurer, did you [get something] for me?\") and whisper \"get something\": the tome is removed and the step becomes 3 WITHOUT a pouch.","cite":"Viking50.cs:2211, 2242-2251, 1947-1949, 1984-1991"},{"n":3,"text":"Take the Sealed Pouch to Elizabeth in Mularn!","type":"whisper","target":"Elizabeth","targetIsOwnTrainer":false,"count":null,"region":100,"pos":[802808,727114,4696],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":["sealed_pouch"],"note":"Interact with Elizabeth (\"Hello, Lynnleigh sent you with a [sealed pouch], right?\") and whisper \"sealed pouch\": the pouch is removed and the step becomes 4. Handing the pouch over does nothing on this step (Notify has no step 3 give). Alternative (needed after path B of step 2): whisper \"sealed pouch\" to Lynnleigh on step 3; she gives a Sealed Pouch and the step becomes 4 (that pouch is never taken back by the script).","cite":"Viking50.cs:2213, 2037-2040, 2059-2067, 1950-1952, 1992-1999"},{"n":4,"text":"Speak with Elizabeth for your reward!","type":"whisper","target":"Elizabeth","targetIsOwnTrainer":false,"count":null,"region":100,"pos":[802808,727114,4696],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":[],"note":"Interact with Elizabeth (\"...you can [take them] with our thanks!\") and whisper \"take them\" with 6 free backpack slots; otherwise \"You do not have enough free space in your inventory!\". \"You have earned this Epic Armor, wear it with honor!\" and the quest finishes. This whisper is the only way to finish.","cite":"Viking50.cs:2215, 2042-2045, 2068-2080"}],"rewards":{"xp":1937768448,"xpNote":"GainExperience(eXPSource.Quest, 1937768448, true) in FinishQuest (Viking50.cs:2341).","money":null,"byClass":{"22":["WarriorEpicArms","WarriorEpicBoots","WarriorEpicGloves","WarriorEpicHelm","WarriorEpicLegs","WarriorEpicVest"],"31":["BerserkerEpicArms","BerserkerEpicBoots","BerserkerEpicGloves","BerserkerEpicHelm","BerserkerEpicLegs","BerserkerEpicVest"],"21":["ThaneEpicArms","ThaneEpicBoots","ThaneEpicGloves","ThaneEpicHelm","ThaneEpicLegs","ThaneEpicVest"],"24":["SkaldEpicArms","SkaldEpicBoots","SkaldEpicGloves","SkaldEpicHelm","SkaldEpicLegs","SkaldEpicVest"],"32":["SavageEpicArms","SavageEpicBoots","SavageEpicGloves","SavageEpicHelm","SavageEpicLegs","SavageEpicVest"],"34":["ValkyrieEpicArms","ValkyrieEpicBoots","ValkyrieEpicGloves","ValkyrieEpicHelm","ValkyrieEpicLegs","ValkyrieEpicVest"],"61":["MaulerMidEpicArms","MaulerMidEpicBoots","MaulerMidEpicGloves","MaulerMidEpicHelm","MaulerMidEpicLegs","MaulerMidEpicVest"]},"note":"FinishQuest, Viking50.cs:2263-2342. No money (AddMoney commented out, line 2342). The MaulerMid templates are only looked up, never created (lines 1867-1874). FinishQuest removes no item."},"monsters":[{"name":"Ydenia of the Seithkona","level":65,"region":100,"x":665094,"y":894559,"z":1791,"createdByScript":true,"cite":"Viking50.cs:201-232 (looked up by name in realm None near 665094,894559 in region 100; created when missing, Level 65 at line 222, GHOST flag; Ydenia.AddToWorld loads NPC template 60168100, Ydenia.cs:46, which may replace the level; respawn = SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL minutes, line 49)"}],"repeatable":false,"notes":["The script has no Classic165Mode branch except the title (Viking50.cs:2198) and the prerequisite (line 2129): start, whispers, steps, Description and FinishQuest are the same in both modes.","Elizabeth (reward NPC) is looked up by name in realm Midgard within 3000 units of 802597,727896 in region 100; when missing she is created as an Enchanter NPC (guild \"Enchanter\", level 17) at 802808,727114,4696 (Viking50.cs:165-195).","The abort whisper works in 1.65 mode: whisper \"abort\" to Lynnleigh while on the quest and confirm (Viking50.cs:2000-2002, 2140-2155); AbortQuest removes the pouch and the tome (lines 2259-2260).","Only Interact and WhisperReceive are registered on Lynnleigh and Elizabeth (lines 1883-1887); the ReceiveItem branches in both talk handlers never fire.","After path A of step 2 the player already holds a pouch; whispering \"sealed pouch\" to Lynnleigh on step 3 gives a second one (lines 1992-1999, 2249).","Lynnleigh's opening and acceptance speeches are copies of the Mystic quest's text with the name swapped (they speak of Raumarik, a totem and \"him\"), while the journal and her step 1 hint send the player to Vanern Swamp (lines 1960, 2191, 1945, 2209).","The \"not available to Maulers yet\" check runs in TalkToLynnleigh in both modes when the MaulerMid templates are missing; the line is spoken by Elizabeth, not Lynnleigh (lines 1927-1933).","Valkyrie (class 34) is in the qualification list and has armour templates created by the script (lines 2114, 1566-1865)."],"uncertain":["Ydenia's final level (NPC template 60168100).","Elizabeth's actual position when a world NPC of that name is found (the script only searches near 802597,727896).","Which level the last pre-50 part of \"An End to the Daggers\" has per class in the world database."]}],"others":[{"key":"ImmediateResolutionVB","file":"scripts/quests/Albion/ImmediateResolutionVB.cs","title":"Immediate Resolution","realm":1,"minLevel":1,"maxLevel":5,"classes":[],"requires":null,"givers":[{"name":"Master Torr","region":1,"x":508520,"y":476163,"z":2304,"how":"looked up by name (realm Albion), Mob row exists (ClassType DOL.GS.Trainer.FighterTrainer); the quest does not load if he or Steward Willie is missing","cite":"ImmediateResolutionVB.cs:144-163,190"}],"start":"Right-click Master Torr, whisper [adventurers], accept the dialog \"Do you accept this task?\"","steps":[{"n":1,"text":"Locate Steward Willie in the keep overlooking Humberton!","type":"whisper","target":"Steward Willie","count":null,"region":1,"pos":[503547,474330,2788],"spawnedByScript":false,"mobLevel":null,"gives":["wax_sealed_note"],"takes":[],"dropChance":null,"note":"Right-click Steward Willie, whisper [a message], then [attacked]: he hands over the Wax Sealed Note and the quest goes to step 2. Steward Willie is looked up by name; Mob row exists.","cite":"ImmediateResolutionVB.cs:224,236-246"},{"n":2,"text":"Locate the guards who defend a bridge just east of Humberton. Deliver the wax-sealed note to them. Do not tarry!","type":"deliver","target":"guardsman","count":1,"region":1,"pos":[519273,452388,2329],"spawnedByScript":true,"mobLevel":45,"gives":[],"takes":["wax_sealed_note"],"dropChance":null,"note":"Give the note to any of the four guards the script itself places at start-up (GameGuard from NpcTemplate 12964 'guardsman', level 45, not saved): 519273/452388/2329, 519829/452156/2368, 523091/452281/2330, 522534/452024/2354. The quest finishes on the hand-over; then ten NpcTemplate 12211 'robber' NPCs appear near that pair of guards (cluster 519203/451090/2370 or 523755/451229/2379) and attack them. Killing them is not part of the quest.","cite":"ImmediateResolutionVB.cs:62-131,411-463"}],"rewards":{"xp":50,"xpNote":"ForceGainExperience(50)","money":30,"items":[],"byClass":{},"rp":null,"bp":null,"note":"Money is Money.GetMoney(0,0,0,0, 30 + Util.Random(50)) = 30 to 80 copper. ImmediateResolutionVB.cs:393-400"},"repeatable":false,"maxCount":1,"notes":["No Classic165Mode check anywhere in the file; the type is not in FreeshardScriptNamespaces/Types (mode165/Classic165Mode.cs:51-60); load_quests = True; region 1 is open.","wax_sealed_note is created and saved by the script only when missing; the world already has the ItemTemplate row. ImmediateResolutionVB.cs:166-181","Any class of level 1-5 qualifies (CheckQuestQualification checks level only). ImmediateResolutionVB.cs:382-391","The DataQuest table also has three rows named 'Immediate Resolution' (IDs 20204 Mistress Trethia class 15, 20205 Magus Oreal class 18, 20206 Master Lorik class 14); DataQuest 20509 'Commencement' (Master Torr) has QuestDependency 'Immediate Resolution', which this scripted quest meets (quests/QuestsMgr/QuestDependencyRule.cs:19-23).","The [abort] whisper cannot abort it: at Steward Willie the abort case sits in the branch for players who are not on the quest, where CheckPlayerAbortQuest returns at once; at Master Torr it is in the on-quest branch and works. ImmediateResolutionVB.cs:249-256,295-302,308-313","MaxQuestCount is the AbstractQuest default 1 (quests/QuestsMgr/AbstractQuest.cs:119)."],"uncertain":["Not run in the game: read from source and the v0.7 world database only.","The four guards are script objects, not Mob rows; they exist only if NpcTemplate 12964 loads (it is in the NpcTemplate table)."],"kind":"town"},{"key":"WolfPeltCloak","file":"scripts/quests/Albion/WolfPeltCloak.cs","title":"Wolf Pelt Cloak","realm":1,"minLevel":1,"maxLevel":11,"classes":[],"requires":null,"givers":[{"name":"Steward Willie","region":1,"x":503547,"y":474330,"z":2788,"how":"looked up by name (realm Albion), Mob row exists; the quest does not load if Steward Willie, Seamstress Lynnet, Brother Don or one of the three item templates is missing (all are present)","cite":"WolfPeltCloak.cs:45-94,103"}],"start":"Right-click Steward Willie, whisper [problem], [like to help], [serve him well], accept the dialog \"Do you accept the Wolf Pelt Cloak quest?\" ([pack of wolves] is extra talk)","steps":[{"n":1,"text":"[Step #1] Go out into the fields to hunt a wolf pup and flay its fur.","type":"kill","target":"black wolf pup, gray wolf pup","count":1,"region":1,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":["wolf_fur"],"takes":[],"dropChance":100,"note":"Any killed monster whose name ends in 'wolf pup' (case-insensitive) counts: 'black wolf pup' (41 Mob rows) and 'gray wolf pup' (80 Mob rows) in region 1, Mob.Level 0-1. No roll: the first kill gives the fur. The fur is a copy of wolf_fur renamed '<monster name> fur' (same Id_nb). The name test also matches 'werewolf pup' (1 Mob row, region 100, level 20).","cite":"WolfPeltCloak.cs:325-326,354-366"},{"n":2,"text":"[Step #2] Bring the fur back to Steward Willie in Humberton Fort.","type":"deliver","target":"Steward Willie","count":1,"region":1,"pos":[503547,474330,2788],"spawnedByScript":false,"mobLevel":null,"gives":["wolf_head_token"],"takes":["wolf_fur"],"dropChance":null,"note":"Give (drag) the fur to Steward Willie.","cite":"WolfPeltCloak.cs:368-380"},{"n":3,"text":"[Step #3] Go to Seamstress Lynnet in Ludlow and bring her the wolf head token.","type":"deliver","target":"Seamstress Lynnet","count":1,"region":1,"pos":[529832,478544,2217],"spawnedByScript":false,"mobLevel":null,"gives":["wolf_pelt_cloak"],"takes":["wolf_head_token"],"dropChance":null,"note":"Give the token to Seamstress Lynnet (a GameMerchant, looked up by name, Mob row exists); the quest finishes.","cite":"WolfPeltCloak.cs:382-391"}],"rewards":{"xp":50,"xpNote":"GainExperience(eXPSource.Quest, 50, true)","money":50,"items":["wolf_pelt_cloak"],"byClass":{},"rp":null,"bp":null,"note":"50 copper. WolfPeltCloak.cs:402-412. Afterwards the cloak can be given to Brother Don (region 1, 505411/495024/2432, Mob ClassType DOL.GS.Scripts.BrotherDon) for 200 more experience; the cloak is taken (scripts/namedmobs/BrotherDon.cs:16-39, WolfPeltCloak.cs:336-348)."},"repeatable":false,"maxCount":1,"notes":["No Classic165Mode check in the file; not a freeshard script type (mode165/Classic165Mode.cs:51-60); region 1 is open.","wolf_pelt_cloak, wolf_fur and wolf_head_token are only looked up; all three ItemTemplate rows exist. WolfPeltCloak.cs:76-94","Quest audit 2026-11-11 narrowed the kill test from any name holding 'wolf' to names ending in 'wolf pup'. WolfPeltCloak.cs:357-360","Aborting removes wolf_fur and wolf_head_token. WolfPeltCloak.cs:395-400","No DataQuest row is named 'Wolf Pelt Cloak'."],"uncertain":["Not run in the game.","Whether the Brother Don donation fires: his class tests e == GamePlayerEvent.ReceiveItem inside GameNPC.Notify (BrotherDon.cs:18); the quest's own Don branch only runs while a WolfPeltCloak quest is active, and the cloak is only handed out when it finishes."],"kind":"town"},{"key":"LostStoneofArawn","file":"scripts/quests/Albion/shrouded isles/LostStoneOfArawn.cs","title":"Lost Stone of Arawn","realm":1,"minLevel":48,"maxLevel":50,"classes":[],"requires":null,"givers":[{"name":"Honayt'rt","region":51,"x":435217,"y":495273,"z":3134,"how":"looked up by name within 3000 units of that spot (Mob row exists); created there by the script if missing","cite":"LostStoneOfArawn.cs:94-123,276"}],"start":"Right-click Honayt'rt, whisper [help me], accept the dialog \"Will you help Honayt'rt retrieve the [Lost Stone of Arawn]?\"","steps":[{"n":1,"text":"Speak to Honayt'rt in Wearyall Village.","type":"whisper","target":"Honayt'rt","count":null,"region":51,"pos":[435217,495273,3134],"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Whisper [Stone of Arawn] to Honayt'rt.","cite":"LostStoneOfArawn.cs:541-547"},{"n":2,"text":"Speak to N'chever in Wearyall Village.","type":"whisper","target":"N'chever","count":null,"region":51,"pos":[435972,492370,3087],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Right-click N'chever, whisper [stone] (talk only), then [Lost Stone of Arawn]. N'chever has no Mob row: the script creates him at 435972/492370/3087 (level 52, model 752). It looks for him at 30763/29908, which never matches, so it creates him at every start.","cite":"LostStoneOfArawn.cs:125-154,629-641"},{"n":3,"text":"Speak to O'honat in Caer Diogel.","type":"whisper","target":"O'honat","count":null,"region":51,"pos":[404696,503469,5192],"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Right-click O'honat (Mob row exists), whisper [Gwyddneau].","cite":"LostStoneOfArawn.cs:157-189,706-714"},{"n":4,"text":"Leave Caer Diogel and head out of town to the West. As you reach the coast, turn North. The demon that we need to kill usually roams the Plains of Gwyddneau.","type":"kill","target":"Nyaegha","count":1,"region":51,"pos":[348381,479838,3320],"spawnedByScript":true,"mobLevel":65,"gives":["lost_stone_of_arawn"],"takes":[],"dropChance":100,"note":"Walking into the 1500-unit circle around the spot on step 4 makes Nyaegha (SINeckBoss, level 65, model 605, size 150) appear there and attack (\"Nyaegha ambushes you!\"). No roll: its death gives the Lost Stone of Arawn to the killer and to every group member on step 4. The world has no Nyaegha Mob row (the permanent one was removed, runtime/true165/true165-rules.json:468-469). SINeckBoss: 14125 hit points (6000 + level*125), 20% resists, a 300 damage spirit AoE proc; it says \"Cowards..\" and leaves when out of combat for 60 seconds.","cite":"LostStoneOfArawn.cs:255-262,314-379,426-471; scripts/mobs/SINeckBossNPC.cs:22-25; ai/brain/Special/SINeckBossBrain.cs:11-17"},{"n":5,"text":"Go back to Caer Diogel and give O'honat the Stone.","type":"whisper","target":"O'honat","count":null,"region":51,"pos":[404696,503469,5192],"spawnedByScript":false,"mobLevel":null,"gives":["scroll_wearyall_loststone"],"takes":["lost_stone_of_arawn"],"dropChance":null,"note":"Right-click O'honat, whisper [impossible], then [Farewell] (needs one free backpack slot): she takes the stone, gives the Victory Speech for Albion and after 3 seconds teleports the player to Wearyall Village (region 51, 435868/493994/3088).","cite":"LostStoneOfArawn.cs:717-738,754-759"},{"n":6,"text":"Read the speech to see who it is addressed to, and return it for your reward.","type":"return","target":"Honayt'rt","count":null,"region":51,"pos":[435217,495273,3134],"spawnedByScript":false,"mobLevel":null,"gives":["Ancient_Copper_Necklace"],"takes":["scroll_wearyall_loststone"],"dropChance":null,"note":"Right-click Honayt'rt, whisper [reward]. Needs one free backpack slot or the quest does not finish.","cite":"LostStoneOfArawn.cs:514-518,550-552,838-859"}],"rewards":{"xp":null,"xpNote":"(ExperienceForNextLevel - ExperienceForCurrentLevel) / 3 when the player is level 49 or 50, / 2 at level 48; GainExperience(eXPSource.Quest, ..., false)","money":1214100,"items":["Ancient_Copper_Necklace"],"byClass":{},"rp":null,"bp":null,"note":"Money.GetMoney(0, 0, 121, 41, Util.Random(50)) = 121 gold 41 silver plus 0 to 50 copper (Util.Random is inclusive). The script asks for item key 'ancient_copper_necklace'; the ItemTemplate row is 'Ancient_Copper_Necklace' (Id_nb is COLLATE NOCASE; level 51 necklace, PackageID 'drmed'). LostStoneOfArawn.cs:196,838-852"},"repeatable":false,"maxCount":1,"notes":["No Classic165Mode check in the file (the Midgard sister quest AncestralSecrets has one, the Albion and Hibernia ones do not); not a freeshard script type; region 51 has Regions.Expansion 1, so client expansion 2, which is not in disabled_expansions '3;4;5;6' (world/Region.cs:244-262).","Both quest items already have ItemTemplate rows; the script creates them only when missing. LostStoneOfArawn.cs:199-250","The ReceiveItem branches of the NPC handlers are never reached: only Interact and WhisperReceive handlers are registered. LostStoneOfArawn.cs:267-274,741-751","Aborting removes the stone and the speech. LostStoneOfArawn.cs:831-836","DataQuest rows 20403-20406 'Defender's Orders' depend on a quest named 'The Lost Stone', a different name: this scripted quest ('Lost Stone of Arawn') does not meet it."],"uncertain":["Not run in the game.","The script's own Nyaegha only gives the stone when DyingEventArgs.Killer is a GamePlayer (LostStoneOfArawn.cs:349-352): a killing blow from a pet or a hired companion gives nothing, and the step then needs a new ambush (leave and re-enter the circle).","N'chever is created (and saved, BaseQuest.SAVE_INTO_DATABASE is always true) at every server start because the look-up spot differs from the create spot, so a long-running install may hold several N'chever at the same place; only the newest one answers the quest whispers."],"kind":"town"},{"key":"TheLostSeed","file":"scripts/quests/Hibernia/shrouded isles/TheLostSeed.cs","title":"The Lost Seed","realm":3,"minLevel":48,"maxLevel":50,"classes":[],"requires":null,"givers":[{"name":"Terod","region":181,"x":382809,"y":421409,"z":5604,"how":"looked up by name within 3000 units of that spot (Mob row exists); created there by the script if missing","cite":"TheLostSeed.cs:77-106,263"}],"start":"Right-click Terod, whisper [help us], then [Lost Seed], accept the dialog \"Will you help Terod find [The Lost Seed]?\"","steps":[{"n":1,"text":"Speak with Terod in Droighaid.","type":"whisper","target":"Terod","count":null,"region":181,"pos":[382809,421409,5604],"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Whisper [treant] to Terod.","cite":"TheLostSeed.cs:543-549"},{"n":2,"text":"Speak with Kredril in Droighaid and tell him more about the Lost Seed.","type":"whisper","target":"Kredril","count":null,"region":181,"pos":[380514,421419,5520],"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Right-click Kredril (Mob row exists), whisper [the Lost Seed] (talk only), then [Jandros].","cite":"TheLostSeed.cs:108-137,634-645"},{"n":3,"text":"Go to Emolia and port yourself to Aalid Feie.","type":"whisper","target":"Emolia","count":null,"region":181,"pos":[378864,421086,5528],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Whisper [Aalid Feie] to Emolia: the step advances, and as a HiberniaSITeleporter she also sends the player there (Teleport row 'Aalid Feie', region 181, 313726/352686/3592). Emolia has no Mob row: the script creates her at 378864/421086/5528 (level 50, model 714). It looks for her at 404696/503469, which never matches, so it creates her at every start.","cite":"TheLostSeed.cs:140-171,719-724; gameobjects/CustomNPC/Teleporters/HiberniaSITeleporter.cs:56-60"},{"n":4,"text":"Speak with Jandros in Aalid Feie and ask him about the Lost Seed.","type":"whisper","target":"Jandros","count":null,"region":181,"pos":[310873,349961,3571],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Right-click Jandros, whisper [Kredril], [the Lost Seed], [died] (talk only), then [Feairna-Athar]. Jandros has no Mob row: created by the script at 310873/349961/3571 (level 54, model 734); looked for at 404696/503469, so created at every start.","cite":"TheLostSeed.cs:174-204,802-821"},{"n":5,"text":"Follow the path north towards Cothrom Gorge. Once in the forest, head West. There, you will find the Treant Feairna-Athar. Kill him and bring me the jewel.","type":"kill","target":"Feairna-Athar","count":1,"region":181,"pos":[288348,319950,2328],"spawnedByScript":true,"mobLevel":65,"gives":["glowing_red_jewel"],"takes":[],"dropChance":100,"note":"Walking into the 1500-unit circle around the spot on step 5 makes Feairna-Athar (SINeckBoss, level 65, model 767, size 100) appear there and attack (\"Feairna-Athar ambushes you!\"), unless another NPC with a SINeckBossBrain stands within 8000 units. No roll: its death gives the Glowing Red Jewel to the killer and to every group member on step 5. The world has no Feairna-Athar Mob row (the permanent 'Feairna-athar' was removed, runtime/true165/true165-rules.json:535). Same SINeckBoss numbers as Nyaegha (14125 hit points).","cite":"TheLostSeed.cs:238-245,305-339,340-363,410-466"},{"n":6,"text":"Return the Glowing Red Jewel to Jandros in Aalid Feie.","type":"whisper","target":"Jandros","count":null,"region":181,"pos":[310873,349961,3571],"spawnedByScript":true,"mobLevel":null,"gives":[],"takes":["glowing_red_jewel"],"dropChance":null,"note":"Right-click Jandros, whisper [the Jewel]: he takes the jewel.","cite":"TheLostSeed.cs:774-776,822-829"},{"n":7,"text":"Speak with Terod in Droighaid for your reward.","type":"return","target":"Terod","count":null,"region":181,"pos":[382809,421409,5604],"spawnedByScript":false,"mobLevel":null,"gives":["Paidrean Necklace"],"takes":[],"dropChance":null,"note":"Right-click Terod, whisper [reward]. Needs one free backpack slot or the quest does not finish.","cite":"TheLostSeed.cs:509-511,550-555,955-977"}],"rewards":{"xp":null,"xpNote":"(ExperienceForNextLevel - ExperienceForCurrentLevel) / 3 when the player is level 49 or 50, / 2 at level 48; GainExperience(eXPSource.Quest, ..., false)","money":1214100,"items":["Paidrean Necklace"],"byClass":{},"rp":null,"bp":null,"note":"Money.GetMoney(0, 0, 121, 41, Util.Random(50)) = 121 gold 41 silver plus 0 to 50 copper (Util.Random is inclusive). 'Paidrean Necklace' is the ItemTemplate Id_nb (level 51 necklace, PackageID 'drmed'). TheLostSeed.cs:209,955-967"},"repeatable":false,"maxCount":1,"notes":["No Classic165Mode check in the file; not a freeshard script type; region 181 has Regions.Expansion 1 (client expansion 2, not disabled).","glowing_red_jewel already has an ItemTemplate row; the script creates it only when missing. TheLostSeed.cs:211-235","The ReceiveItem branches are never reached (only Interact and WhisperReceive handlers are registered), so the jewel is handed over with the [the Jewel] whisper, not by dragging it. TheLostSeed.cs:250-260,833-847","Declining the offer still prints Terod's two 'Thanks, let's talk more about the Lost Seed!' lines. TheLostSeed.cs:903-914","Aborting removes the jewel. TheLostSeed.cs:949-953","No DataQuest row is named 'The Lost Seed'."],"uncertain":["Not run in the game.","The script's own Feairna-Athar only gives the jewel when DyingEventArgs.Killer is a GamePlayer (TheLostSeed.cs:344-347): a killing blow from a pet or a hired companion gives nothing.","Emolia and Jandros are created (and saved) at every server start because the look-up spot differs from the create spot; several copies may stand on the same place, and only the newest one answers the quest whispers."],"kind":"town"},{"key":"WildWilderness","file":"quests/Albion/SI/WildWilderness.cs","title":"Wild Wilderness","realm":3,"minLevel":1,"maxLevel":5,"classes":[55,57],"requires":null,"givers":[{"name":"Miach","region":181,"x":424066,"y":446316,"z":5952,"how":"looked up by name at exactly X 424066 / Y 446316 (Mob row exists, ClassType DOL.GS.Trainer.ForesterTrainer); created there by the script if missing","cite":"WildWilderness.cs:55-86,154"}],"start":"Right-click Miach, whisper [hunt], accept the dialog \"Will you help rid the area of young lungers?\"","steps":[{"n":1,"text":"Speak with Miach about the lungers.","type":"whisper","target":"Miach","count":null,"region":181,"pos":[424066,446316,5952],"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Whisper [tail] to Miach.","cite":"WildWilderness.cs:252-257"},{"n":2,"text":"Kill a lunger for its tail. They can be found just outside the gates of Domnann.","type":"kill","target":"lunger","count":1,"region":181,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":["lunger_tail"],"takes":[],"dropChance":100,"note":"Any killed monster named exactly 'lunger' (lower-cased compare) counts; no roll, the first kill gives the Lunger Tail. 35 'lunger' Mob rows in region 181, Mob.Level 0-1.","cite":"WildWilderness.cs:337-346"},{"n":3,"text":"Bring the tail to Resalg in Grove of Domnann and talk about the [formula].","type":"whisper","target":"Resalg","count":null,"region":181,"pos":[424834,445558,5977],"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":["lunger_tail"],"dropChance":null,"note":"Right-click Resalg (looked up at exactly 424834/445558, Mob row exists), whisper [formula]: he takes the tail and the quest finishes.","cite":"WildWilderness.cs:88-118,284-289,309-313"}],"rewards":{"xp":20,"xpNote":"ForceGainExperience(20)","money":600,"items":[],"byClass":{},"rp":null,"bp":null,"note":"Money.GetMoney(0,0,0,6,0) = 6 silver. WildWilderness.cs:405-411"},"repeatable":false,"maxCount":1,"notes":["Lives in GameServer/quests (compiled into the server itself, not the scripts folder); its [ScriptLoadedEvent] is registered all the same because ScriptMgr.GameServerScripts includes the GameServer assembly (gameutils/ScriptMgr.cs:61-67, GameServer.cs:756-761).","No Classic165Mode check; namespace DOL.GS.Quests.Hibernia is not a freeshard namespace; region 181 is open; class ids 55 and 57 are not in disabled_classes '33;34;39;58-62'.","Class test: only Animist (55) and Forester (57), level 1-5. WildWilderness.cs:388-403","The [formula] whisper has no step check: whispering it to Resalg finishes the quest on any step. WildWilderness.cs:302-313","lunger_tail has an ItemTemplate row; when missing the script builds it in memory without saving it. WildWilderness.cs:122-142","The DataQuest table has its own 'Wild Wilderness' (ID 20811, StartName Miach, level 1-5, AllowedClasses 53|51|57): a Forester sees both the data quest and this scripted one on Miach."],"uncertain":["Not run in the game.","Whether both the scripted quest and DataQuest 20811 can be taken by the same Forester (nothing in the code ties them together)."],"kind":"class"},{"key":"RoadsideInn165","file":"mode165/Courier165.cs","title":"The Traveler's Inn","realm":0,"minLevel":5,"maxLevel":50,"classes":[],"requires":"The player has no hired companion on the roster and has not had this quest before; a level 50 player who has not had The Horn of Rallying gets that one first.","givers":[],"start":"Given automatically: 8 seconds after entering the game or 3 seconds after a level-up, at level 5 or higher, a courier only that player sees (Albion a glowing sphere, Midgard a messenger wolf, Hibernia a messenger fairy) appears, speaks and leaves after 9 seconds; the quest is already in the journal.","steps":[{"n":1,"text":"{Courier} brought word of a traveler's inn on the road near {Town}, in {Zone}. Travel there and speak with {Innkeeper}, the innkeeper, about the companions who gather there.","type":"interact","target":null,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Right-click the Innkeeper165 NPC of your own realm: the quest finishes. The journal text is built per realm (see targetByRealm).","targetByRealm":{"1":{"name":"Gwenllian","region":1,"pos":[462122,649820,1816],"town":"Caer Witrin","zone":"Avalon Marsh","courier":"a glowing sphere"},"2":{"name":"Halvar","region":100,"pos":[705691,790857,4541],"town":"Huginfell","zone":"West Svealand","courier":"a messenger wolf"},"3":{"name":"Muireann","region":200,"pos":[314694,634017,4866],"town":"Connla","zone":"Shannon Estuary","courier":"a messenger fairy"}},"cite":"Courier165.cs:30-38,167-193"}],"rewards":{"xp":null,"xpNote":null,"money":null,"items":[],"byClass":{},"rp":null,"bp":null,"note":"No experience, coin or item. The innkeeper says the first companion costs nothing and the hire window opens. Courier165.cs:187-191; mode165/Innkeeper165.cs:30-41"},"repeatable":false,"maxCount":1,"notes":["True 1.65 only: OnCheck returns unless Classic165Mode.IsOn. Courier165.cs:56","Given only when HiredCompanions165.RosterOf(player).Count == 0 and the player neither has nor has finished it. Courier165.cs:68-69,79-84","All three innkeepers have Mob rows with ClassType DOL.GS.Innkeeper165.","No maximum level in the code; maxLevel 50 is the level cap."],"uncertain":["Not run in the game."],"kind":"town"},{"key":"HornOfRallying165","file":"mode165/Courier165.cs","title":"The Horn of Rallying","realm":0,"minLevel":50,"maxLevel":50,"classes":[],"requires":null,"givers":[],"start":"Given automatically: 8 seconds after entering the game or 3 seconds after the level-up, at level 50, the realm's courier appears, speaks and leaves; the quest is already in the journal.","steps":[{"n":1,"text":"A courier brought word that {HornHandler}, who keeps the Horn of Rallying at the traveler's inn near {Town} in {Zone}, would speak with you. Champions who sound the horn call the realm to its great hunts.","type":"interact","target":null,"count":null,"region":null,"pos":null,"spawnedByScript":false,"mobLevel":null,"gives":[],"takes":[],"dropChance":null,"note":"Right-click the Horn Handler (HornHandler165) at your realm's inn: the quest finishes (see targetByRealm).","targetByRealm":{"1":{"name":"Wystan","region":1,"pos":[462305,647981,1768],"town":"Caer Witrin","zone":"Avalon Marsh"},"2":{"name":"Hallveig","region":100,"pos":[708270,791384,4648],"town":"Huginfell","zone":"West Svealand"},"3":{"name":"Cormac","region":200,"pos":[313845,635199,4848],"town":"Connla","zone":"Shannon Estuary"}},"cite":"Courier165.cs:205-223; mode165/HornHandler165.cs:42-63"}],"rewards":{"xp":null,"xpNote":null,"money":null,"items":[],"byClass":{},"rp":null,"bp":null,"note":"No experience, coin or item: finishing it gives the right to sound the Horn of Rallying (HornHandler165.MaySound: level 50 and this quest finished). mode165/HornHandler165.cs:75-77"},"repeatable":false,"maxCount":1,"notes":["True 1.65 only (Courier165.cs:56). Checked before the inn quest: a level 50 player without it gets the horn quest first, the inn quest on a later login or level check. Courier165.cs:64-71","All three Horn Handlers have Mob rows with ClassType DOL.GS.HornHandler165."],"uncertain":["Not run in the game."],"kind":"town"}],"notLive":[{"key":"AncestralSecrets","file":"scripts/quests/Midgard/shrouded isles/AncestralSecrets.cs","title":"Ancestral Secrets","why":"ScriptLoaded returns at once when Classic165Mode.IsOn (AncestralSecrets.cs:77-86: 'not offered', its reward 'Beaded Resisting Stones' is a freeshard item and the period Gazsi weapons are not in the world), before any handler or AddQuestToGive (AncestralSecrets.cs:301). Ota Yrling, Jaklyr, Longbeard and Styr have Mob rows in region 151 but carry no quest."},{"key":"CaleKeepCaptureAlb","file":"scripts/quests/BattlegroundQuests/Caledonia/CaleKeepCaptureAlb.cs","title":"[Daily] Caledonia Conquerer","why":"namespace DOL.GS.DailyQuest.Albion (CaleKeepCaptureAlb.cs:14) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (CaleKeepCaptureAlb.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 250) and AddQuestToGive (CaleKeepCaptureAlb.cs:120) never runs."},{"key":"CaleKeepCaptureHib","file":"scripts/quests/BattlegroundQuests/Caledonia/CaleKeepCaptureHib.cs","title":"[Daily] Caledonia Conquerer","why":"namespace DOL.GS.DailyQuest.Hibernia (CaleKeepCaptureHib.cs:14) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (CaleKeepCaptureHib.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 250) and AddQuestToGive (CaleKeepCaptureHib.cs:122) never runs."},{"key":"CaleKeepCaptureMid","file":"scripts/quests/BattlegroundQuests/Caledonia/CaleKeepCaptureMid.cs","title":"[Daily] Frontier Conquerer","why":"namespace DOL.GS.DailyQuest.Midgard (CaleKeepCaptureMid.cs:14) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (CaleKeepCaptureMid.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 250) and AddQuestToGive (CaleKeepCaptureMid.cs:122) never runs."},{"key":"CaleKillQuestAlb","file":"scripts/quests/BattlegroundQuests/Caledonia/CaleKillQuestAlb.cs","title":"[Daily] Fen's New Friends","why":"namespace DOL.GS.DailyQuest.Albion (CaleKillQuestAlb.cs:14) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (CaleKillQuestAlb.cs:61) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 250) and AddQuestToGive (CaleKillQuestAlb.cs:121) never runs."},{"key":"CaleKillQuestHib","file":"scripts/quests/BattlegroundQuests/Caledonia/CaleKillQuestHib.cs","title":"[Daily] Fen's New Friends","why":"namespace DOL.GS.DailyQuest.Hibernia (CaleKillQuestHib.cs:13) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (CaleKillQuestHib.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 250) and AddQuestToGive (CaleKillQuestHib.cs:120) never runs."},{"key":"CaleKillQuestMid","file":"scripts/quests/BattlegroundQuests/Caledonia/CaleKillQuestMid.cs","title":"[Daily] Fen's New Friends","why":"namespace DOL.GS.DailyQuest.Hibernia (CaleKillQuestMid.cs:13) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (CaleKillQuestMid.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Rey' is never created (no Mob row of that name in region 250) and AddQuestToGive (CaleKillQuestMid.cs:120) never runs."},{"key":"ThidKeepCaptureAlb","file":"scripts/quests/BattlegroundQuests/Thidranki/ThidKeepCaptureAlb.cs","title":"[Daily] Thidranki Conquerer","why":"namespace DOL.GS.DailyQuest.Albion (ThidKeepCaptureAlb.cs:14) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (ThidKeepCaptureAlb.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 252) and AddQuestToGive (ThidKeepCaptureAlb.cs:120) never runs."},{"key":"ThidKeepCaptureHib","file":"scripts/quests/BattlegroundQuests/Thidranki/ThidKeepCaptureHib.cs","title":"[Daily] Thidranki Conquerer","why":"namespace DOL.GS.DailyQuest.Hibernia (ThidKeepCaptureHib.cs:14) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (ThidKeepCaptureHib.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 252) and AddQuestToGive (ThidKeepCaptureHib.cs:122) never runs."},{"key":"ThidKeepCaptureMid","file":"scripts/quests/BattlegroundQuests/Thidranki/ThidKeepCaptureMid.cs","title":"[Daily] Frontier Conquerer","why":"namespace DOL.GS.DailyQuest.Midgard (ThidKeepCaptureMid.cs:14) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (ThidKeepCaptureMid.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 252) and AddQuestToGive (ThidKeepCaptureMid.cs:122) never runs."},{"key":"ThidKillQuestAlb","file":"scripts/quests/BattlegroundQuests/Thidranki/ThidKillQuestAlb.cs","title":"[Daily] Fen's New Friends","why":"namespace DOL.GS.DailyQuest.Albion (ThidKillQuestAlb.cs:14) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (ThidKillQuestAlb.cs:61) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 252) and AddQuestToGive (ThidKillQuestAlb.cs:121) never runs."},{"key":"ThidKillQuestHib","file":"scripts/quests/BattlegroundQuests/Thidranki/ThidKillQuestHib.cs","title":"[Daily] Fen's New Friends","why":"namespace DOL.GS.DailyQuest.Hibernia (ThidKillQuestHib.cs:13) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (ThidKillQuestHib.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 252) and AddQuestToGive (ThidKillQuestHib.cs:120) never runs."},{"key":"ThidKillQuestMid","file":"scripts/quests/BattlegroundQuests/Thidranki/ThidKillQuestMid.cs","title":"[Daily] Fen's New Friends","why":"namespace DOL.GS.DailyQuest.Hibernia (ThidKillQuestMid.cs:13) is in Classic165Mode.FreeshardScriptNamespaces (mode165/Classic165Mode.cs:51-54,62-69), so its [ScriptLoadedEvent] handler (ThidKillQuestMid.cs:60) does nothing while classic165_mode is True (events/GameEventMgr.cs:119-126): the giver 'Pazz' is never created (no Mob row of that name in region 252) and AddQuestToGive (ThidKillQuestMid.cs:120) never runs."}],"systems":[{"name":"tasks: kill task (quests/Tasks/KillTask.cs)","playerCanGet":true,"from":"A named guard of the player's own realm (KillTask.CheckNamedGuard: a name holding 'Guard' other than plain Guard/Guardsman/Guardian forms; 'Sir ', 'Captain ' or 'Lady ' with no guild; 'Jarl ', 'Soldier ', 'Soldat ', 'Sentinel ' (not ...Runes/...Kynon), a name holding 'Viking' (not ...Archer/Dreng/Huscarl/Jarl), 'Huntress ', 'Bork ', '...Flayer...Kegnar').","how":"Target the guard within whisper distance and type /task, or whisper/say 'task' to him. Levels 1-19 only; one task at a time; as many tasks per level as the level (reset on level-up; a failed or aborted task counts); 2 hour time limit; /task with no target shows it, /task abort gives it up. The guard names one common (lower-case named, realm 0, not peaceful) monster of his zone between the player's level -3 and +1; kill one, then right-click the same guard. Reward: experience 3 x ServerRules.GetExperienceForLiving(player level) at hand-in, coin from the table [28,57,77,105,140,190,257,347,470,632,735,852,987,1147,1330,1542,1790,2077,2407,2801] copper by level (plus or minus 3 percent). There is no Classic165Mode check: the same in True 1.65. task_give_random_item is False, so no task item drops.","cite":"quests/Tasks/AbstractTask.cs:44,49,455,464-467,510-526,557-594; quests/Tasks/KillTask.cs:30,58-71,140-166,248-258,283-315,347-355,391-486; commands/playercommands/task.cs:40-67; gameobjects/GameNPC.cs:2690-2700; gameobjects/GamePlayer.cs:4826-4831"},{"name":"tasks: delivery task ('Money Task', quests/Tasks/MoneyTask.cs)","playerCanGet":true,"from":"Any townsperson of the player's realm who is not a guard and not a crafting master (AbstractTask.IsTownsperson: name starts with a capital, targetable, not a GameGuard/keep guard/pet/gamebot, no 'Guard' in the name).","how":"/task with the NPC targeted, or whisper 'task'. Same level 1-19 and per-level limits as kill tasks. The NPC hands over a generated item and names another townsperson of the same zone (one more than 3000 units away when there is one); give that NPC the item. Reward: coin table [25,34,46,63,84,114,154,208,282,379,441,511,592,688,798,925,1074,1246,1444,1830] copper by level (plus or minus 3 percent); experience min(GetExperienceNeededForLevel(level) * 0.5 / (level - 1), the kill task amount); at level 1 that formula divides by zero (result not checked). An NPC is asked in the order kill, craft, delivery.","cite":"quests/Tasks/MoneyTask.cs:20,45-54,65,111-137,159-188,201-224; quests/Tasks/AbstractTask.cs:144-151,510-517,544-548"},{"name":"tasks: craft task (quests/Tasks/CraftTask.cs)","playerCanGet":true,"from":"A crafting master (CraftNPC: AlchemistsMaster, ArmorCraftingMaster, FletchingMaster, SpellCraftingMaster, TailoringMaster, WeaponCraftingMaster ... all have Mob rows) whose craft is the player's primary crafting skill.","how":"/task with the master targeted, or whisper 'task'. Same level 1-19 and per-level limits. The master names a recipe of that craft with CraftingLevel above the player's skill and below skill + 40, and a townsperson of the zone to bring it to; give that NPC the crafted item (matched by item name). Reward: coin = the item template's Price x 1.25, experience GetExperienceNeededForLevel(level) * 0.5 / (level - 1). The coin amount is held in memory only (not saved with the task).","cite":"quests/Tasks/CraftTask.cs:20,40-52,103-129,136-150,157-194,206-217; quests/Tasks/AbstractTask.cs:144-151"},{"name":"bot kill tasks (mode165/BotKillTasks165.cs)","playerCanGet":false,"from":"Nobody: this is for solo gamebots only.","how":"Solo autonomous gamebots of level 4-19 take a kill task from a named guard on a town visit they make anyway (40 percent of visits, one per level) and are paid the player's KillTask reward on a later visit. A player gets nothing from it; the player's own kill tasks are the /task system above.","cite":"mode165/BotKillTasks165.cs:10-37,58-60; bots/autonomous/AutonomousWorldBotController.KillTasks.cs:17-37"},{"name":"town errands (mode165/TownErrands165.cs)","playerCanGet":false,"from":"Nobody: not a quest.","how":"A gamebot rule: the reasons ('repair', 'ammo', 'pull weapon', 'craft') that send a gamebot to town besides a full backpack, at most one trip per 30 minutes. Players and hired companions are not touched by it (hired bots are excluded).","cite":"mode165/TownErrands165.cs:7-13,32-54"},{"name":"couriers (mode165/Courier165.cs)","playerCanGet":true,"from":"Nobody to ask: a courier comes to the player.","how":"True 1.65 only. The two BaseQuest classes RoadsideInn165 (level 5+) and HornOfRallying165 (level 50) are put straight into the journal on login or level-up; see the two entries in 'quests'.","cite":"mode165/Courier165.cs:40-74,158-224"},{"name":"daily / weekly / monthly quests (quests/QuestsMgr/DailyQuest.cs, WeeklyQuest.cs, MonthlyQuest.cs)","playerCanGet":false,"from":"Nobody.","how":"The only classes deriving from these in the source are the twelve battleground dailies under scripts/quests/BattlegroundQuests, all in namespace DOL.GS.DailyQuest.*, whose start-up hooks do nothing in True 1.65 (see notLive). No class derives from WeeklyQuest or MonthlyQuest.","cite":"mode165/Classic165Mode.cs:51-54; events/GameEventMgr.cs:119-126"},{"name":"missions and task dungeons (quests/Missions/*.cs)","playerCanGet":false,"from":"MissionMaster keep guards and TaskMaster NPCs, neither of which exists in the world.","how":"KillMission, ScoutMission, RaizeMission and CaptureMission are only created by keeps/Gameobjects/Guards/MissionMaster.cs; TaskDungeonMission only by gameobjects/CustomNPC/TaskMaster.cs, which also refuses normal players ('Task Dungeons are currently disabled'). The world has no Mob row and no KeepPosition row with either class.","cite":"keeps/Gameobjects/Guards/MissionMaster.cs:33-129; gameobjects/CustomNPC/TaskMaster.cs:11-20"}]}'''

if __name__ == "__main__":
    sys.exit(main())
