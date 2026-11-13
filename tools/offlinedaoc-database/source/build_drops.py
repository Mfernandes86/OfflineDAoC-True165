"""OFFLINEDAOC Database - drop rates (lane D-D).

    python -I source/build_drops.py [--db <world.sqlite3.db>] [--out <data dir>] [--no-selftest]

Writes data/drops.js: for every monster kind (key = lowercased runtime name) the real chance that ONE kill yields
each item, computed the way this server computes loot. The algorithm is a port of the C# that runs; the plain-words
version with every file:line is source/droprules.md, the data shape is source/contract/drops.md.

Reads one other lane's file: data/mobs.js, table `scripted` (lane M-D: monsters that only a script, an encounter or a
quest creates). Run build_mobs.py first; without the file those monsters stay under `unspawned`.

Server code ported here (paths under wgcommon.SERVER_SRC/GameServer):
  gameutils/LootMgr.cs:55-123        the LootGenerator table rows are the only generators (3 rows: money, otd, template)
  gameutils/LootMgr.cs:412-435       GetLoot: every generator's list is merged into ONE LootList, then rolled once
  gameutils/LootList.cs:44-48,73-87  DropCount starts at 1; a merge keeps the larger DropCount
  gameutils/LootList.cs:93-155       GetLoot: fixed items always; each random entry rolls Chance >= Random(1,100);
                                     then up to DropCount winners are taken uniformly, without replacement
  gameutils/LootGeneratorTemplate.cs:238-546   which rows become fixed / random, DropCount, realm filter
  gameutils/LootGeneratorTemplate.cs:573-614   Chance == 100 -> fixed (LootTemplate.Count pieces), else random
  gameutils/LootGeneratorBase.cs:73-81         CanReceiveRealmDrop (item realm 0, or the loot owner's realm)
  gameutils/LootGeneratorMoney.cs:37-61 + mode165/Coin165.cs:29-43   coin by rule
  gameutils/LootGeneratorOneTimeDrop.cs:133-216   one-time drops
  serverrules/AbstractServerRules.cs:1128, 1219-1221, 1683-1700   no loot: grey con, keep guards, CanDropLoot,
                                     and the True 1.65 quest gate (mode165/QuestLootGate165.cs:27-43)
  gameobjects/GameNPC.cs:1180, 1341-1345, 1416-1426   the runtime name and body type come from the NpcTemplate
                                     when it has ReplaceMobValues
  quests/QuestsMgr/ClassicQuests.cs:742-779, ClassicQuests.Features07.cs:247-274   quest step drops (not loot)
  world/Region.cs:234-264            regions the player cannot enter (disabled_regions, disabled_expansions): their
                                     spawns do not count as spawned
"""
import collections, json, math, os, random, re, sys, time
from fractions import Fraction

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wgcommon as wg

T0 = time.time()

# flags of a drop row
F_ALWAYS = 1     # a fixed drop: the row has Chance 100, it drops on every kill
F_QUEST = 2      # True 1.65 quest gate: falls only while a loot owner is on a quest that names the item
F_NEVER = 4      # a quest item no quest names: the gate always removes it (it still uses up a pick)
F_REALM = 8      # realm-locked item: only a killer of the item's realm can get it

MONEY_NAMES = {"bag of coins", "small chest", "large chest", "some copper coins"}   # GameMoney.cs:8
BODY_NAMES = ["None", "Animal", "Demon", "Dragon", "Elemental", "Giant", "Humanoid", "Insect", "Magical", "Reptile",
              "Plant", "Undead"]                                                       # Coin165.cs:19-20
QUEST_PACKAGE = "ClassicQuest"                                                         # QuestLootGate165.cs:22
QUEST_ITEM_RE = re.compile(r"cq_[A-Za-z0-9_]+")                                        # QuestLootGate165.cs:23


# ------------------------------------------------------------------------------------------------------- the maths

def poisson_binomial(ps, one=1.0):
    """dist[k] = chance that exactly k of the independent rolls win."""
    dist = [one]
    for p in ps:
        q = one - p
        nxt = [dist[0] * q]
        for k in range(1, len(dist)):
            nxt.append(dist[k] * q + dist[k - 1] * p)
        nxt.append(dist[-1] * p)
        dist = nxt
    return dist


def chance_at_least_one(group, others, drop_count, one=1.0):
    """LootList.GetLoot (LootList.cs:126-151) in closed form.

    group / others: win chances (0..1) of the random entries that are this item / every other entry of the pooled
    list. Every entry rolls once; then min(DropCount, winners) winners are taken uniformly without replacement.
    Returns (chance that at least one entry of the group is taken, expected number of group entries taken)."""
    dg = poisson_binomial(group, one)
    do = poisson_binomial(others, one)
    zero = one - one
    p_none = zero
    expect = zero
    for a, pa in enumerate(dg):
        if pa == 0:
            continue
        for b, pb in enumerate(do):
            if pb == 0:
                continue
            w = pa * pb
            if a == 0:
                p_none += w
                continue
            n = a + b
            if n <= drop_count:
                expect += w * a
                continue
            # none of the a group winners among the drop_count taken from n: C(b, D) / C(n, D)
            if b >= drop_count:
                if isinstance(one, float):
                    p_none += w * (math.comb(b, drop_count) / math.comb(n, drop_count))
                else:
                    p_none += w * Fraction(math.comb(b, drop_count), math.comb(n, drop_count))
            if isinstance(one, float):
                expect += w * (a * drop_count / n)
            else:
                expect += w * Fraction(a * drop_count, n)
    return one - p_none, expect


def simulate_getloot(fixed, entries, drop_count, kills, rng):
    """A literal port of LootList.GetLoot (LootList.cs:93-155) for the self test. entries: [(item, chance int)].
    Returns {item: kills that yielded it at least once}."""
    seen = collections.Counter()
    for _ in range(kills):
        loot = list(fixed)
        if drop_count > 0:
            cand = [[item, 1] for item, chance in entries if chance >= rng.randint(1, 100)]     # :127-131
            i = 0
            while i < drop_count and cand:                                                      # :143
                idx = rng.randint(0, len(cand) - 1)                                             # :145
                loot.append(cand[idx][0])
                cand[idx][1] -= 1
                if cand[idx][1] <= 0:                                                           # :147-148 SwapRemoveAt
                    cand[idx] = cand[-1]
                    cand.pop()
                i += 1
        for item in set(loot):
            seen[item] += 1
    return seen


def brute_force(entries, drop_count):
    """Exact enumeration over every win / lose outcome (self test, small lists). entries: [(item, Fraction p)]."""
    n = len(entries)
    out = collections.defaultdict(Fraction)
    for mask in range(1 << n):
        w = Fraction(1)
        winners = []
        for i, (item, p) in enumerate(entries):
            if mask >> i & 1:
                w *= p
                winners.append(item)
            else:
                w *= 1 - p
        if w == 0:
            continue
        take = min(drop_count, len(winners))
        if take == len(winners):
            for item in set(winners):
                out[item] += w
            continue
        # uniform subsets of size take
        import itertools
        combos = list(itertools.combinations(range(len(winners)), take))
        share = w / len(combos)
        for combo in combos:
            for item in {winners[i] for i in combo}:
                out[item] += share
    return out


# ------------------------------------------------------------------------------------------------------- the data

def load(con):
    d = {}
    d["items"] = {}
    for r in con.execute("select Id_nb, Name, Realm, PackageID, PackSize, MaxCount, Level from ItemTemplate"):
        d["items"][r["Id_nb"].lower()] = dict(r)       # the server's item lookups are case-insensitive
        # (LootGeneratorTemplate.cs:67-68 ToLower; DataTableHandler.cs:188-196 pre-cache key ToLower)
    # LootTemplate: TemplateName lower -> {item id lower: row}; a second row for the same item is ignored
    # (LootGeneratorTemplate.cs:84-108). Rows whose item does not exist are not loaded (:97-103).
    d["loot"] = collections.OrderedDict()
    d["lt_rows"] = d["lt_missing_item"] = d["lt_dup"] = 0
    for r in con.execute("select TemplateName, ItemTemplateID, Chance, Count from LootTemplate order by rowid"):
        d["lt_rows"] += 1
        tpl = d["loot"].setdefault(r["TemplateName"].lower(), collections.OrderedDict())
        item = r["ItemTemplateID"].lower()
        if item not in d["items"]:
            d["lt_missing_item"] += 1
            continue
        if item in tpl:
            d["lt_dup"] += 1
            continue
        tpl[item] = (int(r["Chance"]), int(r["Count"]))
    # MobXLootTemplate: MobName lower -> rows in table order (LootGeneratorTemplate.cs:141-153)
    d["mobx"] = collections.OrderedDict()
    d["mx_rows"] = 0
    for r in con.execute("select MobName, LootTemplateName, DropCount from MobXLootTemplate order by rowid"):
        d["mx_rows"] += 1
        d["mobx"].setdefault(r["MobName"].lower(), []).append((r["MobName"], r["LootTemplateName"], int(r["DropCount"])))
    d["otd"] = [dict(r) for r in con.execute("select MobName, ItemTemplateID, MinLevel from LootOTD order by rowid")]
    d["generators"] = [dict(r) for r in con.execute(
        "select LootGeneratorClass, MobName, MobGuild, MobFaction, RegionID, ExclusivePriority from LootGenerator")]
    d["props"] = {r["Key"].lower(): r["Value"] for r in con.execute("select Key, Value from ServerProperty")}
    # spawns and their runtime identity
    templates = collections.defaultdict(list)
    for r in con.execute("select TemplateId, Name, BodyType, ReplaceMobValues, ClassType from NpcTemplate"):
        templates[r["TemplateId"]].append(dict(r))
    d["templates"] = templates
    # Regions the player can enter (world/Region.cs:234-245, :255-264): not in disabled_regions ("a-b" is a range,
    # Util.SplitCSV(..., true)) and not of a disabled expansion (Regions.Expansion + 1 against disabled_expansions).
    closed = set()
    for part in (d["props"].get("disabled_regions") or "").replace(",", ";").split(";"):
        part = part.strip()
        if "-" in part:
            lo, hi = part.split("-", 1)
            closed.update(range(int(lo), int(hi) + 1))
        elif part:
            closed.add(int(part))
    off = {x.strip() for x in (d["props"].get("disabled_expansions") or "").replace(",", ";").split(";") if x.strip()}
    d["open_regions"] = {r["RegionID"] for r in con.execute("select RegionID, Expansion from Regions")
                         if r["RegionID"] not in closed and str(r["Expansion"] + 1) not in off}
    d["spawns"] = []
    d["closed_spawns"] = []
    for r in con.execute("select Name, NPCTemplateID, ClassType, BodyType, Realm, Region, Guild from Mob"):
        (d["spawns"] if r["Region"] in d["open_regions"] else d["closed_spawns"]).append(dict(r))
    d["dataquest"] = []
    cols = [c[1] for c in con.execute("pragma table_info(DataQuest)")]
    for r in con.execute("select * from DataQuest"):
        row = dict(r)
        d["dataquest"].append(row)
    d["dataquest_cols"] = cols
    return d


DEFAULT_NPC_CLASS = "DOL.GS.GameNPC"


def runtime_kinds(d, spawns, guild_class=None):
    """key (lowercased runtime name) -> {name, n (spawn rows), body: Counter, classes: Counter}.

    GameNPC.LoadFromDatabase sets Name from the Mob row (GameNPC.cs:1077) and then LoadTemplate (:1180) overwrites
    the name and body type with the NpcTemplate's when the template has ReplaceMobValues (:1341-1345, :1416-1426).
    NpcTemplateMgr.GetTemplate picks one at random when several templates share the id, so each is counted.

    The class the spawn runs as (world/Region.cs:745-773): the NPC guild script for the row's Guild and Realm if
    there is one, else the template's ClassType when it is set, is not DOL.GS.GameNPC and the template has
    ReplaceMobValues, else the Mob row's ClassType."""
    kinds = {}
    renamed = 0
    for s in spawns:
        variants = []
        tpls = [t for t in d["templates"].get(s["NPCTemplateID"], ()) if t["ReplaceMobValues"]]
        all_tpls = d["templates"].get(s["NPCTemplateID"], ())
        scripted = guild_class(s["Guild"], s["Realm"]) if guild_class else None
        own_class = scripted or s["ClassType"] or ""
        if tpls:
            for t in tpls:
                tc = t["ClassType"]
                cls = scripted or (tc if tc and tc.lower() != DEFAULT_NPC_CLASS.lower() else s["ClassType"]) or ""
                variants.append((t["Name"] or "", t["BodyType"], cls))
            if len(tpls) < len(all_tpls):
                variants.append((s["Name"] or "", s["BodyType"], own_class))
        else:
            variants.append((s["Name"] or "", s["BodyType"], own_class))
        if any(v[0].lower() != (s["Name"] or "").lower() for v in variants):
            renamed += 1
        for name, body, cls in variants:
            k = kinds.setdefault(name.lower(), {"name": name, "n": 0, "body": collections.Counter(),
                                                "classes": collections.Counter()})
            k["n"] += 1
            k["body"][body] += 1
            k["classes"][cls] += 1
    return kinds, renamed


def scan_classes():
    """Class facts from the server source: which NPC classes never drop loot.

    AbstractServerRules.cs:1128   if (killedNpc.CanDropLoot && !NoItemLoot(killedNpc)) ... DropLoot
    AbstractServerRules.cs:1683   NoItemLoot: GameKeepGuard (and subclasses), GameBot, player / bot pets
    GameNPC.cs:3323               CanDropLoot => true unless a class overrides it"""
    base = {}
    no_loot = set()
    class_body = {}             # class -> body type literals its own code assigns (`BodyType = N;`)
    spawn_body = {}             # lowercased spawn name -> body type literals a SpawnAdd call's tweak assigns
    body_re = re.compile(r"(?<![\w.])BodyType\s*=\s*(\d+)\s*;")
    guild_scripts = {}          # guild name -> {realm name or None: class}   [NPCGuildScript("guild", eRealm.X)]
    guild_re = re.compile(r'\[NPCGuildScript\(\s*"([^"]+)"\s*(?:,\s*eRealm\.(\w+)\s*)?\)\]')
    class_re = re.compile(r"\bclass\s+(\w+)(?:\s*<[^>]*>)?\s*(?::\s*([\w.]+))?")
    root = os.path.join(wg.SERVER_SRC, "GameServer")
    files = 0
    for dirpath, _dirs, names in os.walk(root):
        for name in names:
            if not name.endswith(".cs"):
                continue
            files += 1
            try:
                with open(os.path.join(dirpath, name), encoding="utf-8", errors="replace") as f:
                    text = f.read()
            except OSError:
                continue
            if "class" not in text:
                continue
            current = None
            pending = []
            for line in text.split("\n"):
                if "NPCGuildScript" in line and not line.lstrip().startswith("//"):
                    pending += guild_re.findall(line.split("//")[0])
                m = class_re.search(line.split("//")[0])
                if m and not line.lstrip().startswith("//") and not line.lstrip().startswith("*") and \
                        not line.lstrip().startswith("///"):
                    current = m.group(1)
                    if m.group(2) and current not in base:
                        base[current] = m.group(2).split(".")[-1]
                    for guild, realm in pending:
                        guild_scripts.setdefault(guild, {})[realm if realm and realm != "None" else None] = current
                    pending = []
                if "CanDropLoot" in line and "override" in line and "false" in line and current:
                    no_loot.add(current)
                if "BodyType" in line and current:
                    # a class that sets its own body type (after its template is loaded): `BodyType = 2;`
                    for m2 in body_re.finditer(line.split("//")[0]):
                        class_body.setdefault(current, set()).add(int(m2.group(1)))
            if "SpawnAdd(" in text:
                # PrimaEncounter165.SpawnAdd(near, template, "name", level, ..., npc => { npc.BodyType = N; ... })
                for m3 in re.finditer(r"SpawnAdd\(", text):
                    i, depth = m3.end(), 1
                    while i < len(text) and depth:
                        depth += text[i] == "("
                        depth -= text[i] == ")"
                        i += 1
                    call = text[m3.end():i]
                    names_in_call = re.findall(r'"([^"]+)"', call.split("=>")[0])
                    bodies = {int(x) for x in re.findall(r"\.BodyType\s*=\s*(\d+)", call)}
                    if len(names_in_call) == 1 and len(bodies) == 1:
                        spawn_body.setdefault(names_in_call[0].lower(), set()).update(bodies)
    assert files > 1500, "server source not found under " + root
    assert "GameKeepGuard" in base, "GameKeepGuard class not found in the server source"

    def reason(class_type):
        cls = (class_type or "").split(".")[-1]
        seen = set()
        while cls and cls not in seen:
            seen.add(cls)
            if cls == "GameKeepGuard":
                return "keep guard"
            if cls in no_loot:
                return "script"
            cls = base.get(cls)
        return None

    realm_names = {0: "None", 1: "Albion", 2: "Midgard", 3: "Hibernia"}

    def guild_class(guild, realm):
        """ScriptMgr.FindNPCGuildScriptClass (gameutils/ScriptMgr.cs:797-891): a script for this guild name whose
        attribute has no realm or this realm. Region.cs:745 only asks when the row has a guild and a realm 0..3."""
        if not guild or realm not in realm_names:
            return None
        found = guild_scripts.get(guild)
        if not found:
            return None
        return found.get(realm_names[realm]) or found.get(None)

    def epic_encounter(class_type):
        """RealmRaidLootOwner.IsScriptedEpicEncounter (bots/autonomous/RealmRaidLootOwner.cs:31-32): a class that is
        an epic NPC (GameEpicBoss, GameEpicNPC or a subclass) but not the plain GameEpicNPC / GameEpicDungeonNPC."""
        leaf = (class_type or "").split(".")[-1]
        if leaf in ("GameEpicNPC", "GameEpicDungeonNPC"):
            return False
        cls, seen = leaf, set()
        while cls and cls not in seen:
            seen.add(cls)
            if cls in ("GameEpicBoss", "GameEpicNPC"):
                return True
            cls = base.get(cls)
        return False
    assert base.get("GameEpicDungeonNPC") == "GameEpicNPC" and "GameEpicBoss" in base, "epic NPC classes not found"
    reason.epic_encounter = epic_encounter

    def script_body(key, classes):
        """The body type a script-spawned monster has at the kill, when the code states exactly one: the literal its
        own class assigns (it runs in AddToWorld, after the template is loaded), or the one the SpawnAdd tweak sets.
        None when the code does not state one."""
        found = set(spawn_body.get(key, ()))
        named = [c for c in classes if c and c.split(".")[-1] != "GameNPC"]
        for c in named:
            lits = class_body.get(c.split(".")[-1])
            if not lits:
                return None if not found else (next(iter(found)) if len(found) == 1 else None)
            found |= lits
        return next(iter(found)) if len(found) == 1 else None
    reason.script_body = script_body
    return reason, sorted(no_loot), files, guild_class, len(guild_scripts)


def quest_gate(d):
    """QuestLootGate165.QuestsByItem (QuestLootGate165.cs:57-91): quest item id (lower) -> ids of the data quests
    that name it in any text column of their DataQuest row or anywhere in their classic-quests.json entry."""
    by_item = collections.defaultdict(set)
    names = {}
    for row in d["dataquest"]:
        qid = row.get("ID")
        names[qid] = row.get("Name")
        for col, val in row.items():
            if isinstance(val, str) and "cq_" in val.lower():
                for m in QUEST_ITEM_RE.findall(val):
                    by_item[m.lower()].add(qid)
    path = os.path.join(wg.RUNTIME, "server", "classic-quests.json")
    quests = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            quests = json.load(f).get("Quests", {})
        for qid, entry in quests.items():
            text = json.dumps(entry, ensure_ascii=False)
            if "cq_" in text.lower():
                for m in QUEST_ITEM_RE.findall(text):
                    by_item[m.lower()].add(int(qid))
    return by_item, names, quests


def is_quest_item(item):                                    # QuestLootGate165.cs:27-29
    return item["PackageID"] == QUEST_PACKAGE or item["Id_nb"].lower().startswith("cq_")


def can_receive(item, realm):                               # LootGeneratorBase.cs:73-81, allow_cross_realm_items off
    return item["Realm"] == 0 or item["Realm"] == realm


def build_pool(d, key, realm):
    """LootGeneratorTemplate.GenerateLoot for a monster whose runtime name lowercases to key, killed by a loot owner
    of the given realm. Returns (fixed [(item, pieces)], random [(item, chance int)], DropCount)."""
    fixed, rand = [], []
    drop_count = 1                                           # LootList.cs:44 (new LootList() -> DropCount 1)
    rows = d["mobx"].get(key)
    if rows is None:
        # no MobXLootTemplate row: the monster's own table, DropCount stays 1 (LootGeneratorTemplate.cs:283-316)
        for item, (chance, count) in d["loot"].get(key, {}).items():
            it = d["items"][item]
            if not can_receive(it, realm):
                continue
            if chance == 100:
                fixed.append((item, count))
            else:
                rand.append((item, chance))                  # AddRandom(Chance, drop, 1): Count is not used
        return fixed, rand, drop_count
    # MobXLootTemplate rows exist (LootGeneratorTemplate.cs:400-519)
    has_own = any((tpl or "").lower() == mob.lower() for mob, tpl, _dc in rows)       # :409-410
    own_read = False
    for mob, tpl, dc in rows:
        name = tpl if tpl else mob                           # :418-419
        if name.lower() not in d["loot"]:                    # :420-425
            if has_own:
                continue
            name = mob
        if name.lower() == mob.lower():                      # :426-431
            if own_read:
                continue
            own_read = True
        for item, (chance, count) in d["loot"].get(name.lower(), {}).items():          # :588-611
            it = d["items"][item]
            if not can_receive(it, realm):
                continue
            if chance == 100:
                fixed.append((item, count if count > 0 else dc))
            else:
                rand.append((item, chance))
                drop_count = max(drop_count, dc)             # :607
    return fixed, rand, drop_count


def solve_pool(fixed, rand, drop_count, cache):
    """item -> (chance of at least one, expected pieces, always?) for one pooled list."""
    sig = (tuple(fixed), tuple(rand), drop_count)
    if sig in cache:
        return cache[sig]
    out = {}
    fixed_n = collections.Counter()
    for item, pieces in fixed:
        fixed_n[item] += pieces
    ps = [min(max(c, 0), 100) / 100.0 for _i, c in rand]     # LootList.cs:129  Chance >= Util.Random(1, 100)
    by_item = collections.defaultdict(list)
    for i, (item, _c) in enumerate(rand):
        by_item[item].append(i)
    for item, idx in by_item.items():
        group = [ps[i] for i in idx]
        iset = set(idx)
        others = [p for i, p in enumerate(ps) if i not in iset]
        p, e = chance_at_least_one(group, others, drop_count)
        out[item] = (p, e, False)
    for item, n in fixed_n.items():
        if n <= 0:
            continue
        prev = out.get(item, (0.0, 0.0, False))
        out[item] = (1.0, prev[1] + n, True)
    cache[sig] = out
    return out


def selftest(d):
    """The closed form against exact enumeration, exact fractions and a literal port of the C# loop."""
    rng = random.Random(165)
    checked = 0
    # 1. random small pools: closed form (floats and fractions) == brute force
    for _ in range(300):
        n = rng.randint(1, 9)
        dc = rng.randint(1, 4)
        items = ["i%d" % rng.randint(0, n - 1) for _ in range(n)]
        chances = [rng.choice([1, 2, 5, 10, 25, 50, 75, 99, 100, 0]) for _ in range(n)]
        exact = brute_force([(it, Fraction(c, 100)) for it, c in zip(items, chances)], dc)
        for it in set(items):
            g = [Fraction(c, 100) for i2, c in zip(items, chances) if i2 == it]
            o = [Fraction(c, 100) for i2, c in zip(items, chances) if i2 != it]
            pf, _e = chance_at_least_one(g, o, dc, Fraction(1))
            pd, _e2 = chance_at_least_one([float(x) for x in g], [float(x) for x in o], dc)
            assert pf == exact.get(it, 0), ("closed form != enumeration", items, chances, dc, it)
            assert abs(pd - float(pf)) < 1e-12, ("float drift", pd, pf)
            checked += 1
    # 2. the real pools with 2..12 random entries: floats == fractions
    real = 0
    for key in d["loot"]:
        fixed, rand, dc = build_pool(d, key, 1)
        if not 2 <= len(rand) <= 12:
            continue
        ps = [Fraction(c, 100) for _i, c in rand]
        for i in range(len(rand)):
            pf, _ = chance_at_least_one([ps[i]], ps[:i] + ps[i + 1:], dc, Fraction(1))
            pd, _ = chance_at_least_one([float(ps[i])], [float(x) for x in ps[:i] + ps[i + 1:]], dc)
            assert abs(pd - float(pf)) < 1e-12
            real += 1
        if real > 4000:
            break
    # 3. a literal port of LootList.GetLoot, 200,000 seeded kills on the five largest real pools
    sim = []
    biggest = sorted(d["loot"], key=lambda k: -len(d["loot"][k]))[:5]
    cache = {}
    worst = 0.0
    for key in biggest:
        best = max((1, 2, 3), key=lambda r: len(build_pool(d, key, r)[1]))
        fixed, rand, dc = build_pool(d, key, best)
        solved = solve_pool(fixed, rand, dc, cache)
        kills = 200000
        seen = simulate_getloot([f[0] for f in fixed], rand, dc, kills, rng)
        for item, (p, _e, _a) in solved.items():
            err = abs(seen[item] / kills - p)
            sigma = math.sqrt(max(p * (1 - p), 1e-9) / kills)
            assert err < 5 * sigma + 1e-9, ("simulation disagrees", key, item, p, seen[item] / kills)
            worst = max(worst, err)
        sim.append((key, len(rand), dc))
    return {"enumerated": checked, "fraction_checked": real, "simulated": sim, "sim_worst_abs_error": worst}


def main():
    do_selftest = "--no-selftest" not in sys.argv
    sys.argv = [x for x in sys.argv if x != "--no-selftest"]
    a = wg.args(__doc__)
    con = wg.open_db(a.db)
    d = load(con)

    # ---- the assumptions this port rests on: fail loudly if the world stops matching them
    gens = sorted(g["LootGeneratorClass"] for g in d["generators"])
    assert gens == ["DOL.GS.LootGeneratorMoney", "DOL.GS.LootGeneratorOneTimeDrop", "DOL.GS.LootGeneratorTemplate"], gens
    for g in d["generators"]:
        assert not g["MobName"] and not g["MobGuild"] and not g["MobFaction"] and not g["RegionID"] and \
            not g["ExclusivePriority"], ("a generator is no longer global / non-exclusive", g)
    assert d["props"].get("classic165_mode", "").lower() == "true", "this world is not a True 1.65 world"
    assert d["props"].get("allow_cross_realm_items", "").lower() == "false", "cross-realm items changed"
    money_drop = float(d["props"].get("money_drop", "1"))
    chances = [c for t in d["loot"].values() for c, _n in t.values()]
    assert min(chances) > 0 and max(chances) <= 100, "a LootTemplate chance outside 1..100 (timed XP items or dead rows)"
    assert not any(k.isdigit() for k in d["mobx"]), "MobXLootTemplate keyed by NpcTemplate id (LootGeneratorTemplate.cs:262)"
    assert d["lt_missing_item"] == 0, "LootTemplate rows naming a missing item: %d" % d["lt_missing_item"]
    for t in d["loot"].values():
        for item in t:
            assert d["items"][item]["Name"] not in MONEY_NAMES, "a loot row is a coin item (GameMoney.cs:8)"
    for con_name in ("MobDropTemplate", "DropTemplateXItemTemplate"):
        assert con.execute("select count(*) from " + con_name).fetchone()[0] == 0, con_name + " is in use"

    no_loot_reason, no_loot_classes, cs_files, guild_class, guild_script_count = scan_classes()
    assert guild_script_count > 50, "NPC guild scripts not found in the server source"
    if d["props"].get("use_npcguildscripts", "true").lower() != "true":      # Region.cs:745
        guild_class = None
    kinds, renamed = runtime_kinds(d, d["spawns"], guild_class)             # spawns in regions the player can enter
    closed_kinds, _r = runtime_kinds(d, d["closed_spawns"], guild_class)    # tutorial, NF battlegrounds, later expansions
    q_by_item, q_names, quests_json = quest_gate(d)
    test = selftest(d) if do_selftest else None

    # Kinds that no Mob row spawns but a script, an encounter file or a quest does (dragon summons, the Horsemen,
    # Prima encounters, quest finale bosses). Lane M-D finds them and lists them in data/mobs.js, table `scripted`
    # (source/contract/mobs.md section 5d): key = the name the script gives, lowercased; cls = server classes;
    # body = the body type of its template, 0 when not known. Their loot is looked up by name like any other
    # monster's (they are GameNPC / boss classes whose death runs the same OnNpcKilled path). A scripted name that
    # is also the runtime name of a world spawn row stays an ordinary kind.
    scripted = {}
    scripted_also_spawned = 0
    scripted_no_body = 0
    body_disagree = []
    mobs_js = os.path.join(a.out, "mobs.js")
    if not os.path.exists(mobs_js):
        mobs_js = os.path.join(wg.ROOT, "data", "mobs.js")
    if os.path.exists(mobs_js):
        with open(mobs_js, encoding="utf-8") as f:
            text = f.read()
        table = json.loads(text[text.index(",") + 1:text.rindex(")")]).get("scripted") or {}
        cols = table.get("cols", [])
        assert not table or "key" in cols, "data/mobs.js: scripted has no key column"
        for row in table.get("rows", []):
            rec = dict(zip(cols, row))
            if rec["key"] in kinds:
                scripted_also_spawned += 1
                continue
            body = rec.get("body")
            classes = rec.get("cls") or []
            if isinstance(classes, str):
                classes = [classes]
            k = {"name": rec.get("name") or rec["key"], "n": 0, "body": collections.Counter(),
                 "classes": collections.Counter(classes), "scripted": True}
            # Coin165 reads the body type at the kill. M-D's `body` is that: the script's own BodyType when it sets
            # one (it wins over the template: Beliathan.cs:187-204 LoadTemplate, then BodyType = 2), else the
            # template's, 0 when there is no template and nothing sets one (Coin165 then uses its "None" rate),
            # null when it could not be established: no coin figure is published then.
            # Cross-check: where this builder's own scan of the scripts finds exactly one literal, it must agree.
            own = no_loot_reason.script_body(rec["key"], classes)
            if isinstance(body, int) and body >= 0:
                k["body"][body] += 1
                if own is not None and own != body:
                    body_disagree.append((rec["key"], body, own))
            else:
                scripted_no_body += 1
            scripted[rec["key"]] = k
    else:
        print("note: data/mobs.js not found; script-spawned kinds stay under `unspawned`")

    # coin (Coin165.cs:29-43 + LootGeneratorMoney.cs:42-50)
    with open(os.path.join(wg.RUNTIME, "true165", "coin165.json"), encoding="utf-8") as f:
        coin_file = json.load(f)
    coin_named = {k.lower(): max(0, min(100, int(v))) for k, v in coin_file["monsters"].items()}
    coin_body = {k.lower(): max(0, min(100, int(v))) for k, v in coin_file["body_type_default"].items()}

    def coin_for(key, kind):
        if key in coin_named:
            return [coin_named[key], coin_named[key], "measured"]
        vals = []
        for body, n in kind["body"].items():
            name = BODY_NAMES[body] if body is not None and 0 <= body < len(BODY_NAMES) else "None"
            vals.append(coin_body.get(name.lower(), 100))
        if not vals:
            return [None, None, "unknown"]       # a script-spawned kind whose body type is not known: say nothing
        return [min(vals), max(vals), "body type"]

    # ---- drops per kind
    cache = {}
    loot_names = set(d["loot"]) | set(d["mobx"])
    stats = collections.Counter()
    mobs_out, unspawned_out = {}, {}
    by_item = collections.defaultdict(list)
    used_quests = set()

    def rows_for(key):
        per_realm = {}
        for realm in (1, 2, 3):
            fixed, rand, dc = build_pool(d, key, realm)
            per_realm[realm] = (solve_pool(fixed, rand, dc, cache), dc, len(rand), len(fixed))
        all_items = []
        for realm in (1, 2, 3):
            for item in per_realm[realm][0]:
                if item not in all_items:
                    all_items.append(item)
        if not all_items:
            return None
        locked = collections.Counter(d["items"][i]["Realm"] for i in all_items if d["items"][i]["Realm"])
        home = max(sorted(locked), key=lambda r: locked[r]) if locked else 0
        rows = []
        for item in all_items:
            it = d["items"][item]
            natural = it["Realm"] if it["Realm"] else (home or 1)
            ch = [per_realm[r][0].get(item, (0.0, 0.0, False))[0] for r in (1, 2, 3)]
            p, e, always = per_realm[natural][0][item]
            flags = 0
            if always:
                flags |= F_ALWAYS
            if it["Realm"]:
                flags |= F_REALM
            qids = None
            if is_quest_item(it):
                qids = sorted(q_by_item.get(item, ()))
                flags |= F_QUEST if qids else F_NEVER
                used_quests.update(qids)
            pack = it["PackSize"] if it["PackSize"] > 1 and it["MaxCount"] >= it["PackSize"] else 1
            # AbstractServerRules.cs:1753-1755: one dropped piece becomes a stack of PackSize
            pieces = (round(e / p) if p > 0 else 1) if always else 1
            row = [it["Id_nb"], round(p, 6), pack * max(1, pieces), flags]
            alt = [round(x, 6) for x in ch]
            if any(abs(x - p) > 5e-7 for x in ch):
                row.append(alt)
            elif qids is not None:
                row.append(0)
            if qids is not None:
                row.append(qids)
            rows.append(row)
        rows.sort(key=lambda r: (-r[1], r[0].lower()))
        dc = per_realm[home or 1][1]
        return {"d": rows, "dc": dc, "pool": per_realm[home or 1][2], "realm": home}

    stats["kinds spawned only in closed regions"] = len(set(closed_kinds) - set(kinds))
    stats["spawn rows in closed regions"] = len(d["closed_spawns"])
    stats["script-spawned kinds (from mobs.js)"] = len(scripted)
    stats["script-spawned names that are also world spawn names"] = scripted_also_spawned
    stats["script-spawned kinds with no body type (no coin figure)"] = scripted_no_body
    stats["script-spawned kinds whose body type differs from this builder's own script scan"] = len(body_disagree)
    for row in body_disagree:
        print("note: body type of %r: mobs.js %s, script literal found here %s" % row)
    for key in sorted(loot_names | set(kinds) | set(scripted)):
        kind = kinds.get(key) or scripted.get(key)
        entry = rows_for(key) if key in loot_names else None
        if kind is None:
            if entry:
                if key in closed_kinds:
                    entry["closed"] = 1                      # spawned only in a region the player cannot enter
                    stats["closed-region kinds with loot"] += 1
                unspawned_out[key] = entry
                stats["unspawned kinds with loot"] += 1
                stats["unspawned loot rows"] += len(entry["d"])
            else:
                stats["unspawned names with an empty table"] += 1
            continue
        out = {}
        reasons = collections.Counter()
        for cls, n in kind["classes"].items():
            reasons[no_loot_reason(cls)] += n
        is_scripted = bool(kind.get("scripted"))
        if entry:
            out.update(entry)
            stats["spawned kinds with loot"] += 1
            stats["loot rows"] += len(entry["d"])
            if is_scripted:
                stats["script-spawned kinds with loot"] += 1
        if is_scripted:
            out["s"] = 1                                     # no Mob row: a script, encounter or quest spawns it
        lo, hi, src = coin_for(key, kind)
        if lo is not None:
            out["coin"] = lo if lo == hi else [lo, hi]
        if src == "measured":
            out["cm"] = 1
        total = sum(kind["classes"].values())
        blocked = sum(n for r, n in reasons.items() if r)
        if blocked:
            # these spawns never reach DropLoot at all: no items, no coin
            out["none"] = [blocked, total, sorted(r for r in reasons if r)[0]]
            stats["kinds with spawns that never drop"] += 1
        # realm raid share (RealmRaidLootOwner.cs:27-32, AutonomousRealmRaid.cs:1339-1346): a scripted epic encounter
        epic = sum(n for cls, n in kind["classes"].items() if no_loot_reason.epic_encounter(cls))
        if epic:
            out["raid"] = 1 if epic == total else [epic, total]
            stats["kinds that are scripted epic encounters (raid share)"] += 1
        mobs_out[key] = out

    # one-time drops (LootGeneratorOneTimeDrop.cs:133-216)
    otd_unspawned = 0
    for o in d["otd"]:
        key = o["MobName"].lower()
        item = d["items"].get(o["ItemTemplateID"].lower())
        assert item is not None, ("LootOTD item missing", o)
        target = mobs_out.get(key)
        if target is None:
            otd_unspawned += 1
            target = unspawned_out.setdefault(key, {})
            if key in closed_kinds:
                target["closed"] = 1
        target.setdefault("otd", []).append([item["Id_nb"], int(o["MinLevel"])])
    stats["one-time drops"] = len(d["otd"])
    stats["one-time drops on a name with no spawn"] = otd_unspawned

    # quest step drops: not loot, a quest hands the item over on the kill (ClassicQuests.cs:742-779, Features07:247-274)
    qdrops = collections.defaultdict(dict)
    for qid, entry in quests_json.items():
        for step_no, step in enumerate(entry.get("Steps") or []):
            custom = (step or {}).get("Custom") or {}
            for drop in custom.get("Drops") or []:
                item = d["items"].get((drop.get("Item") or "").lower())
                if not item or not drop.get("Mob"):
                    stats["quest step drops skipped (item or monster missing)"] += 1
                    continue
                k = (item["Id_nb"], int(qid))
                cur = qdrops[drop["Mob"].lower()].setdefault(k, [int(drop.get("Chance", 100)), []])
                cur[0] = min(cur[0], int(drop.get("Chance", 100)))
                cur[1].append(step_no)
        for side in entry.get("SideTurnIns") or []:
            item = d["items"].get((side.get("Item") or "").lower())
            if not item or not side.get("DropFrom"):
                stats["quest step drops skipped (item or monster missing)"] += 1
                continue
            k = (item["Id_nb"], int(qid))
            qdrops[side["DropFrom"].lower()].setdefault(k, [int(side.get("DropChance", 100)), []])
    for key, drops in qdrops.items():
        target = mobs_out.get(key)
        if target is None:
            stats["quest step drops on a name with no spawn"] += len(drops)
            target = unspawned_out.setdefault(key, {})
            if key in closed_kinds:
                target["closed"] = 1
        rows = []
        for (item, qid), (chance, steps) in sorted(drops.items(), key=lambda kv: (kv[0][0].lower(), kv[0][1])):
            rows.append([item, max(0, min(100, chance)) / 100.0, qid, sorted(set(steps))])
            used_quests.add(qid)
            stats["quest step drops"] += 1
        target["q"] = rows

    # reverse index
    for section, name in ((mobs_out, 0), (unspawned_out, 1)):
        for key, entry in section.items():
            for row in entry.get("d", ()):
                # 4th element: absent = a world spawn kind, 1 = an unspawned name, 2 = a script-spawned kind (`s`)
                mark = [1] if name else ([2] if entry.get("s") else [])
                by_item[row[0]].append([key, row[1], row[3]] + mark)
    for item in by_item:
        by_item[item].sort(key=lambda r: (-r[1], r[0]))

    # ---- checks on the result
    total_pairs = sum(len(e.get("d", ())) for e in mobs_out.values()) + sum(len(e.get("d", ())) for e in unspawned_out.values())
    assert total_pairs == d["lt_rows"] - d["lt_dup"] - d["lt_missing_item"], (total_pairs, d["lt_rows"])
    for section in (mobs_out, unspawned_out):
        for key, entry in section.items():
            rows = entry.get("d", ())
            for row in rows:
                assert 0.0 <= row[1] <= 1.0, (key, row)
                assert (row[1] == 1.0) == bool(row[3] & F_ALWAYS) or row[1] < 1.0, (key, row)
            if rows:
                # the random picks can never exceed DropCount: sum of the chances of the non-fixed rows, per realm
                for realm in (0, 1, 2):
                    s = sum((r[4][realm] if len(r) > 4 and isinstance(r[4], list) else r[1]) for r in rows if not r[3] & F_ALWAYS)
                    assert s <= entry["dc"] + 1e-4, (key, s, entry["dc"])

    quest_names = {str(q): (q_names.get(q) or "") for q in sorted(used_quests)}
    coin_amount = "min = 2 + floor((level + 1)^3 / 8); max = min + floor(min / 2); every whole number between is equally likely"
    value = {
        "meta": {
            "built": wg.stamp(),
            "db": os.path.basename(a.db),
            "cols": ["item", "chance", "n", "flags", "byRealm", "quests"],
            "flags": {"always": F_ALWAYS, "quest": F_QUEST, "never": F_NEVER, "realm": F_REALM},
            "counts": dict(stats, **{
                "kinds": len(mobs_out), "LootTemplate rows": d["lt_rows"], "MobXLootTemplate rows": d["mx_rows"],
                "items that drop": len(by_item), "spawn rows renamed by their template": renamed,
            }),
            "coin": {"moneyDrop": money_drop, "amount": coin_amount, "body": coin_body, "bodyNames": BODY_NAMES},
            "noLootClasses": no_loot_classes,
            "selftest": test,
        },
        "mobs": mobs_out,
        "unspawned": unspawned_out,
        "byItem": dict(sorted(by_item.items(), key=lambda kv: kv[0].lower())),
        "quests": quest_names,
    }
    size = wg.write_data("drops", value, a.out, "%d kinds, %d (monster, item) chances" % (len(mobs_out), total_pairs))
    assert size < 15e6

    print("server source files scanned: %d, classes that never drop: %d" % (cs_files, len(no_loot_classes)))
    for k in sorted(value["meta"]["counts"]):
        print("  %-52s %s" % (k, value["meta"]["counts"][k]))
    if test:
        print("self test: %d enumerated, %d exact-fraction, simulated %s, worst simulation error %.5f" % (
            test["enumerated"], test["fraction_checked"], test["simulated"], test["sim_worst_abs_error"]))
    print("done in %.1f s" % (time.time() - T0))


if __name__ == "__main__":
    main()
