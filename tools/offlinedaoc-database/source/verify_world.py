"""The verifier's own reading of the world (lane D-V): which regions are open, what each spawn is called and what
levels it can have at run time, which list a merchant sells, which tickets a stable master honours.

Imported by verify_data.py and verify_prices.py. Written from the server's C#, not from the other lanes' builders:

 - Region.cs:234-246, :255-263   a region is closed when its id is in server property disabled_regions, or when
                                 (Regions.Expansion + 1) is in disabled_expansions. Nothing in a closed region
                                 can be reached, so its spawns and merchants are not "in the world".
 - GameNPC.cs:1076-1180          a spawn starts from its Mob row, then LoadTemplate(NpcTemplateMgr.GetTemplate(id))
 - GameNPC.cs:1316-1351, :1416-1451  with a template whose ReplaceMobValues is set: Name, Guild, Faction, Race,
                                 BodyType, Flags from the template, aggro level / range from the template (:1453-1461)
 - GameNPC.cs:1360-1390, Util.cs:86-114  level = one uniform pick from the template's Level string
                                 (";" separates entries, "a-b" = every level a..b), only with ReplaceMobValues;
                                 an entry that is not a number 0..255 leaves the level as it was
 - GameNPC.cs:3255-3270          the pick is made again at every respawn
 - GameMerchant.cs:346-373       a merchant sells its Mob row's ItemsListTemplateID; only when that is empty, the
                                 template's list (set first by LoadTemplate, then replaced by LoadFromDatabase)
 - GameMerchant.cs:95-112        one purchase costs ItemTemplate.Price and gives PackSize pieces (or 1)
 - GameStableMaster.cs:128-136   a ticket works when it is in the master's list, Item_Type 40, PathPoints with the
                                 ticket's Id_nb exist (a Path row is optional, MovementMgr.cs:102-112) and the
                                 first point is within 500 on X and on Y
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import wgcommon as wg  # noqa: E402


def split_csv(s, range_check=True):
    """Util.SplitCSV (Util.cs:86-114)."""
    if s is None:
        return []
    out = []
    for part in [p for p in str(s).split(";") if p != ""]:
        if range_check and "-" in part:
            bits = part.split("-")
            try:
                a, b = int(bits[0]), int(bits[1])
            except (ValueError, IndexError):
                continue                      # C#: nothing is added when either end does not parse
            if a > b:
                a, b = b, a
            out.extend(str(i) for i in range(a, b + 1))
        else:
            out.append(part)
    return out


def closed_regions(con):
    prop = {r["Key"].lower(): (r["Value"] or "") for r in con.execute("select Key, Value from ServerProperty")}
    by_id = {x.strip() for x in split_csv(prop.get("disabled_regions", ""))}
    by_exp = {x.strip() for x in split_csv(prop.get("disabled_expansions", ""))}
    closed = set()
    for r in con.execute("select RegionID, Expansion from Regions"):
        if str(r["RegionID"]) in by_id or str((r["Expansion"] or 0) + 1) in by_exp:
            closed.add(r["RegionID"])
    return closed


def template_levels(level_string, mob_level):
    """The levels a spawn with this template Level string can have (GameNPC.cs:1372-1390)."""
    entries = split_csv(level_string) if level_string not in (None, "") else []
    if not entries:
        return {mob_level}
    out = set()
    for e in entries:
        try:
            v = int(e.strip()) if e.strip().lstrip("+").isdigit() else None
        except ValueError:
            v = None
        if v is None or not (0 <= v <= 255):
            out.add(mob_level)                # byte.TryParse failed: the level stays what it was
        else:
            out.add(v)
    return out


_CLASSES = None


def classes():
    """(parents, guild_scripts, no_loot) read once from every .cs under GameServer:
    parents        class name -> base class name
    guild_scripts  [(guild name, realm or None, class name)] from [NPCGuildScript("Guild", eRealm.X)] attributes
    no_loot        class names that override CanDropLoot to false"""
    global _CLASSES
    if _CLASSES is not None:
        return _CLASSES
    import re
    realm_no = {"None": None, "Albion": 1, "Midgard": 2, "Hibernia": 3}
    parents, guild_scripts, no_loot = {}, [], set()
    decl = re.compile(r"\bclass\s+(\w+)\s*(?:<[^>]*>)?\s*(?::\s*([\w.]+))?")
    attr = re.compile(r"\[\s*NPCGuildScript\w*\(\s*\"([^\"]*)\"\s*(?:,\s*eRealm\.(\w+))?\s*\)\s*\]")
    off = re.compile(r"override\s+bool\s+CanDropLoot\s*=>\s*false")
    for dp, _, fn in os.walk(wg.SERVER_SRC + "/GameServer"):
        for f in fn:
            if not f.endswith(".cs"):
                continue
            pending, cur = [], None
            for line in open(os.path.join(dp, f), encoding="utf-8", errors="replace"):
                code = line.split("//")[0]
                for a in attr.finditer(code):
                    pending.append((a.group(1), realm_no.get(a.group(2) or "None")))
                m = decl.search(code)
                if m and "class" in code.split('"')[0]:
                    cur = m.group(1)
                    if m.group(2):
                        parents.setdefault(cur, m.group(2).split(".")[-1])
                    for g, r in pending:
                        guild_scripts.append((g, r, cur))
                    pending = []
                if cur and off.search(code):
                    no_loot.add(cur)
    _CLASSES = (parents, guild_scripts, no_loot)
    return _CLASSES


_SCRIPT_LEVELS = None


def script_levels():
    """class name -> (min, max) for monster classes whose own AddToWorld() sets Level before calling the base.
    AddToWorld runs after the Mob row and the template are loaded and again at every respawn
    (GameNPC.cs:3270-3284: LoadTemplate, then AddToWorld), so this level is the one the monster has in the world.
    Read from the C#: 'Level = 79;' or 'Level = (byte)Util.Random(65, 70);' inside 'override bool AddToWorld'."""
    global _SCRIPT_LEVELS
    if _SCRIPT_LEVELS is not None:
        return _SCRIPT_LEVELS
    import re
    decl = re.compile(r"\bclass\s+(\w+)")
    start = re.compile(r"override\s+bool\s+AddToWorld\s*\(")
    assign = re.compile(r"(?<![\w.])Level\s*=\s*(?:\(\s*byte\s*\)\s*)?(?:Util\.Random\(\s*(\d+)\s*,\s*(\d+)\s*\)|(\d+))\s*;")
    out = {}
    for dp, _, fn in os.walk(wg.SERVER_SRC + "/GameServer"):
        for f in fn:
            if not f.endswith(".cs"):
                continue
            cur, inside, depth = None, False, 0
            for line in open(os.path.join(dp, f), encoding="utf-8", errors="replace"):
                code = line.split("//")[0]
                m = decl.search(code)
                if m and "class" in code.split('"')[0]:
                    cur, inside = m.group(1), False
                if cur and start.search(code):
                    inside, depth = True, 0
                if inside:
                    a = assign.search(code)
                    if a:
                        lo, hi = (int(a.group(1)), int(a.group(2))) if a.group(1) else (int(a.group(3)), int(a.group(3)))
                        lo, hi = min(lo, hi), max(lo, hi)
                        old = out.get(cur)
                        out[cur] = (lo, hi) if old is None else (min(old[0], lo), max(old[1], hi))
                    depth += code.count("{") - code.count("}")
                    if "base.AddToWorld" in code or (depth <= 0 and "}" in code and "{" not in code and not start.search(code)):
                        inside = False
    _SCRIPT_LEVELS = out
    return out


def script_level_of(cls):
    """The script level range of a class or of its nearest base class that sets one, or None."""
    parents, levels = classes()[0], script_levels()
    c, seen = (cls or "").split(".")[-1], set()
    while c and c not in seen:
        if c in levels:
            return levels[c]
        seen.add(c)
        c = parents.get(c)
    return None


def is_a(cls, base):
    parents = classes()[0]
    c, seen = (cls or "").split(".")[-1], set()
    while c and c not in seen:
        if c == base:
            return True
        seen.add(c)
        c = parents.get(c)
    return False


def effective_class(mob_cls, mob_guild, mob_realm, template):
    """Region.cs:745-768: the NPC guild script for the Mob row's guild and realm if one exists (ScriptMgr.cs:844-890),
    else the template's ClassType when it is set, not the default and the template replaces mob values, else the
    Mob row's ClassType."""
    _, guild_scripts, _ = classes()
    if mob_guild:
        for realm in (mob_realm, None):
            for g, r, c in guild_scripts:
                if g == mob_guild and r == realm:
                    return c
    if template is not None and template["ClassType"] and template["ClassType"] != "DOL.GS.GameNPC" and template["ReplaceMobValues"]:
        return template["ClassType"]
    return mob_cls or "DOL.GS.GameNPC"


def spawns(con, include_closed=False):
    """One dict per Mob row as the server runs it: key, name, levels (set), realm, region, x, y, class, ...
    A template id shared by several NpcTemplate rows is resolved to all of them (the server picks one at random,
    NpcTemplateMgr.GetTemplate), so levels and names are unions; that case is counted in the result's 'multi'."""
    closed = set() if include_closed else closed_regions(con)
    templates = {}
    for t in con.execute("select TemplateId, Name, Level, ReplaceMobValues, GuildName, FactionID, BodyType, Race, "
                         "AggroLevel, AggroRange, Flags, ItemsListTemplateID, ClassType from NpcTemplate"):
        templates.setdefault(t["TemplateId"], []).append(t)
    out = []
    for m in con.execute("select Mob_ID, Name, Level, Realm, Region, X, Y, Z, Guild, ClassType, NPCTemplateID, FactionID, "
                         "BodyType, Race, AggroLevel, AggroRange, Flags, ItemsListTemplateID, PathID, RespawnInterval, "
                         "RoamingRange, Brain from Mob"):
        if m["Region"] in closed:
            continue
        ts = templates.get(m["NPCTemplateID"]) or []
        # one variant per NpcTemplate row with this id (the server picks one at random when several share an id)
        variants = []
        for t in (ts or [None]):
            use_t = bool(t is not None and t["ReplaceMobValues"])
            variants.append(dict(
                name=(t["Name"] if use_t else m["Name"]) or "",
                guild=(t["GuildName"] if use_t else m["Guild"]) or "",
                levels=template_levels(t["Level"], m["Level"]) if use_t else {m["Level"]},
                faction=(t["FactionID"] if use_t else m["FactionID"]) or 0,
                body=(t["BodyType"] if use_t else m["BodyType"]) or 0,
                aggro_level=(t["AggroLevel"] if use_t else m["AggroLevel"]) or 0,
                aggro_range=(t["AggroRange"] if use_t else m["AggroRange"]) or 0,
                flags=(t["Flags"] if use_t else m["Flags"]) or 0,
                replaced=use_t))
        v0 = variants[0]
        names = {v["name"] for v in variants}
        eff = effective_class(m["ClassType"], m["Guild"], m["Realm"] or 0, ts[0] if ts else None)
        scripted = script_level_of(eff)
        if scripted:                                   # the class's own AddToWorld sets the level last
            for v in variants:
                v["table_levels"] = v["levels"]
                v["levels"] = set(range(scripted[0], scripted[1] + 1))
        levels = set()
        for v in variants:
            levels |= v["levels"]
        out.append(dict(
            script_level=scripted,
            mob_id=m["Mob_ID"], name=v0["name"], names=names, key=v0["name"].lower(), variants=variants,
            levels=levels, realm=m["Realm"] or 0, region=m["Region"], x=m["X"], y=m["Y"], z=m["Z"],
            guild=v0["guild"], cls=m["ClassType"] or "", template=m["NPCTemplateID"],
            eff_cls=eff,
            replaced=any(v["replaced"] for v in variants), multi=len(variants) > 1,
            faction=v0["faction"], body=v0["body"], aggro_level=v0["aggro_level"], aggro_range=v0["aggro_range"],
            flags=v0["flags"], mob_list=m["ItemsListTemplateID"] or "",
            template_list=(ts[0]["ItemsListTemplateID"] if ts else "") or "",
            path=m["PathID"] or "", respawn=m["RespawnInterval"], roam=m["RoamingRange"], brain=m["Brain"] or ""))
    return out


def merchant_list(sp):
    """GameMerchant.cs:346-373: the Mob row's list, else the template's."""
    return sp["mob_list"] or sp["template_list"]


def place(zs_by_region, sp):
    """(zone id, local x, local y) of a spawn by the contract's rule, or None outside every zone."""
    z = wg.zone_of(zs_by_region, sp["region"], sp["x"], sp["y"])
    if z is None:
        return None
    return z["id"], int(sp["x"] - z["ox"]), int(sp["y"] - z["oy"])


def stable_routes(con, sps=None):
    """Every (stable master spawn, ticket) the server would honour: list of dicts
    {mob_id, name, region, x, y, ticket, price, path_start: (x, y, z), path_end: (x, y, z), points}."""
    sps = sps if sps is not None else spawns(con)
    items = {r["Id_nb"].lower(): r for r in con.execute("select Id_nb, Name, Item_Type, Price, Color, Realm from ItemTemplate where Item_Type = 40")}
    lists = {}
    for r in con.execute("select ItemListID, ItemTemplateID, PageNumber, SlotPosition from MerchantItem"):
        if r["ItemTemplateID"] and r["ItemTemplateID"].lower() in items:
            lists.setdefault((r["ItemListID"] or "").lower(), []).append(r["ItemTemplateID"].lower())
    paths = {}
    for r in con.execute("select PathID from Path"):
        paths[(r["PathID"] or "").lower()] = None
    pts = {}
    for r in con.execute("select PathID, Step, X, Y, Z from PathPoints order by PathID, Step"):
        pts.setdefault((r["PathID"] or "").lower(), []).append((r["X"], r["Y"], r["Z"]))
    out, near_miss = [], []
    for sp in sps:
        lst = merchant_list(sp)
        if not lst or lst.lower() not in lists:
            continue
        if not is_a(sp["eff_cls"], "GameStableMaster"):
            continue
        for tid in lists[lst.lower()]:
            p = pts.get(tid)
            if not p:                              # MovementMgr.cs:102-112: the points are enough, a Path row is optional
                near_miss.append((sp["name"], tid, "no path points"))
                continue
            if abs(p[0][0] - sp["x"]) < 500 and abs(p[0][1] - sp["y"]) < 500:
                out.append(dict(mob_id=sp["mob_id"], name=sp["name"], region=sp["region"], x=sp["x"], y=sp["y"],
                                ticket=items[tid]["Id_nb"], ticket_name=items[tid]["Name"], price=items[tid]["Price"],
                                path_start=p[0], path_end=p[-1], points=len(p), cls=sp["eff_cls"], realm=sp["realm"]))
            else:
                near_miss.append((sp["name"], tid, "path starts %d,%d away" % (abs(p[0][0] - sp["x"]), abs(p[0][1] - sp["y"]))))
    return out, near_miss


if __name__ == "__main__":
    con = wg.open_db()
    closed = closed_regions(con)
    sps = spawns(con)
    kinds = {}
    for s in sps:
        kinds.setdefault(s["key"], []).append(s)
    print("closed regions: %d; spawns in open regions: %d; distinct names: %d" % (len(closed), len(sps), len(kinds)))
    print("spawns with a level range: %d; with a shared template id: %d"
          % (sum(1 for s in sps if len(s["levels"]) > 1), sum(1 for s in sps if s["multi"])))
    routes, miss = stable_routes(con, sps)
    print("stable routes the server honours: %d (tickets listed but not usable there: %d)" % (len(routes), len(miss)))
    for m in miss[:12]:
        print("   ", m)
