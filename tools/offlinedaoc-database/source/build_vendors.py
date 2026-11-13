"""OFFLINEDAOC Database - merchants (lane V-D).

    python -I source/build_vendors.py [--db <world>] [--out <data dir>]

Writes data/vendors.js and data/vendoritems.js. Reads the world READ-ONLY (wgcommon.open_db) and the server's C#
source (text only, never built or run). Every rule below is ported from the server; the references are
file:line under  development-source/server/GameServer/  as read on 2026-11-12.

WHICH MOB ROWS ARE MERCHANTS (the class the server really creates for a Mob row)
  world/Region.cs:745-748    a row with a Guild whose name has an [NPCGuildScript] class gets THAT class first
                             (use_npcguildscripts = True in ServerProperty); e.g. Guild "Stable Master" ->
                             GameStableMaster (gameobjects/CustomNPC/GameStableMaster.cs:15), "Smith" -> Blacksmith.
  world/Region.cs:766-768    else the NPC template's ClassType, when the template has ReplaceMobValues and a
                             ClassType other than DOL.GS.GameNPC
  world/Region.cs:770-772    else Mob.ClassType
  A merchant is a row whose class derives from GameMerchant (gameobjects/GameMerchant.cs:16). GameHastener
  (CustomNPC/Hastener.cs:8), Adventurer165 (mode165/Adventurer165.cs:23) and plain GameNPC rows that carry an
  ItemsListTemplateID are NOT merchants: nothing reads their list.

WHICH LIST A MERCHANT USES
  gameobjects/GameMerchant.cs:349-357   LoadTemplate: the template's ItemsListTemplateID, when not empty (whether or
                                        not the template replaces mob values)
  gameobjects/GameMerchant.cs:366-373   LoadFromDatabase: then Mob.ItemsListTemplateID, when not empty, wins
  scripts/teleporters/OFMerchant.cs:27-55, 76-118   OFMerchant / OFMerchantHome pick a list by realm and region
  gameutils/FrontierSupplyMerchantPolicy.cs:18-35   lists SiegeMerchant* / OFMerchant_* rename the guild

NAME, GUILD, FLAGS, FACTION
  gameobjects/GameNPC.cs:1341-1345, 1416-1435   a template with ReplaceMobValues overwrites name, guild, flags
                                                and faction; the realm always comes from the Mob row (1088)

WHAT IS FOR SALE AND FOR HOW MUCH
  gameutils/MerchantTradeItems.cs:32,37,271-287   5 pages (0-4) of 30 slots (0-29); anything else is never shown
  packets/Server/PacketLib168.cs:1894-1909        the window sends pages 0-4, slots 0-29 only
  gameutils/MerchantTradeItems.cs:150-170         a row whose ItemTemplate is missing is skipped
  gameobjects/GameMerchant.cs:104-112             one purchase costs ItemTemplate.Price and gives PackSize pieces
                                                  (1 when PackSize is 0). No realm tax, no True 1.65 multiplier:
                                                  GameServer/mode165/ never changes a merchant price.
  gameobjects/GameMerchant.cs:513-556             GameBountyMerchant: the same number, paid in bounty points
  gameobjects/GameMerchant.cs:679-739, 838-851    seal merchants: the same number, counted in seals (DiamondSeal /
                                                  SapphireSeal / EmeraldSeal items)
  gameobjects/GameMerchant.cs:805-836             Darkness Falls seal merchants trade with their own realm only
  gameobjects/GameMerchant.cs:312-328             selling TO a merchant: Price * count / PackSize * item_sell_ratio
                                                  / 100 (item_sell_ratio = 50), nothing for items that cannot be dropped

WHO MAY TRADE (reputation)
  gameobjects/GameNPC.cs:2633-2641                an NPC of another realm (realm 0 for every player) that has a
                                                  faction, and no PEACE flag (serverrules/NormalServerRules.cs:115),
                                                  refuses a player whose standing is Hostile or worse
  gameutils/Faction.cs:184-194                    Hostile = aggro level above 50 (starts at Faction.BaseAggroLevel)

LEFT OUT, AND COUNTED: merchants in regions a player cannot enter (ServerProperty disabled_regions and
disabled_expansions, world/Region.cs:234-245), merchants with nothing a player can buy, lists no placed merchant uses.
"""
import collections, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wgcommon as wg

GS = wg.SERVER_SRC + "/GameServer"
DEFAULT_NPC_CLASS = "DOL.GS.GameNPC"
PEACE = 0x10
MAX_PAGES, MAX_SLOTS = 5, 30                      # MerchantTradeItems.cs:32,37
REALM_NAME = {0: "None", 1: "Albion", 2: "Midgard", 3: "Hibernia"}

# kind words, in the order the filter should show them: (kind, label)
KINDS = [("weapons", "Weapons"), ("armor", "Armor"), ("shields", "Shields"), ("ammo", "Arrows and bolts"),
         ("instruments", "Instruments"), ("jewelry", "Jewelry and cloaks"), ("potions", "Potions and charged items"),
         ("poisons", "Poisons"), ("dyes", "Dyes"), ("crafting", "Crafting supplies"), ("siege", "Siege equipment"),
         ("medallions", "Teleport medallions"), ("housing", "Housing"), ("stable", "Stable tickets")]


# ---------------------------------------------------------------------------------------------- server source

_CLASS_RE = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*"
                       r"class\s+(\w+)\s*(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*(?::\s*([\w\.]+))?")
_NS_RE = re.compile(r"^\s*namespace\s+([\w\.]+)")
_GUILD_RE = re.compile(r"\[\s*NPCGuildScript\(\s*\"([^\"]*)\"\s*(?:,\s*eRealm\.(\w+)\s*)?\)\s*\]")
_REALM_ENUM = {"None": 0, "Albion": 1, "Midgard": 2, "Hibernia": 3}


def scan_server_classes():
    """Read every .cs under GameServer once: {class full name: base simple name}, the guild-script table
    {(guild, realm 0..3): class full name} and the set of class full names that derive from GameMerchant."""
    bases, guilds = {}, {}
    files = 0
    for root, dirs, names in os.walk(GS):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin")]
        for name in names:
            if not name.endswith(".cs"):
                continue
            files += 1
            with open(os.path.join(root, name), encoding="utf-8", errors="replace") as f:
                text = f.read()
            if "class " not in text:
                continue
            ns, pending = "", []
            for line in text.split("\n"):
                s = line.split("//")[0] if "NPCGuildScript" not in line else line
                m = _NS_RE.match(s)
                if m:
                    ns = m.group(1)
                    continue
                for g in _GUILD_RE.finditer(s):
                    if not s.lstrip().startswith("//"):
                        pending.append((g.group(1), _REALM_ENUM.get(g.group(2) or "None", 0)))
                m = _CLASS_RE.match(s)
                if m and " class " in " " + s:
                    full = (ns + "." if ns else "") + m.group(1)
                    base = (m.group(2) or "").split(".")[-1]
                    bases.setdefault(full, base)
                    for guild, realm in pending:
                        for r in ((0, 1, 2, 3) if realm == 0 else (realm,)):   # ScriptMgr.cs:818 None matches all
                            guilds[(guild, r)] = full
                    pending = []
    assert files > 1500, "server source not found under " + GS
    simple_base = {}
    for full, base in bases.items():
        simple_base.setdefault(full.split(".")[-1], base)
    merchants = set()
    for full in bases:
        cur, seen = full.split(".")[-1], set()
        while cur and cur not in seen:
            if cur == "GameMerchant":
                merchants.add(full)
                break
            seen.add(cur)
            cur = simple_base.get(cur, "")
    assert "DOL.GS.GameMerchant" in merchants and "DOL.GS.GameStableMaster" in merchants, "merchant classes not parsed"
    assert guilds.get(("Stable Master", 1)) == "DOL.GS.GameStableMaster", "guild script table not parsed"
    assert "DOL.GS.GameHastener" in bases and "DOL.GS.GameHastener" not in merchants
    return bases, guilds, merchants


def derives(bases, full, ancestor):
    simple_base = {}
    for f, b in bases.items():
        simple_base.setdefault(f.split(".")[-1], b)
    cur, seen = full.split(".")[-1], set()
    while cur and cur not in seen:
        if cur == ancestor:
            return True
        seen.add(cur)
        cur = simple_base.get(cur, "")
    return False


def money_keys():
    """{class simple name: currency item id} from 'public override string MoneyKey { get { return "X"; } }'."""
    text = open(GS + "/gameobjects/GameMerchant.cs", encoding="utf-8", errors="replace").read()
    out = {}
    for m in re.finditer(r"class\s+(\w+)\s*:\s*\w+\s*\{\s*public override string MoneyKey \{ get \{ return \"(\w+)\"; \} \}", text):
        out[m.group(1)] = m.group(2)
    assert out.get("GameDiamondSealsMerchant") == "DiamondSeal", "MoneyKey classes not parsed"
    return out


# ---------------------------------------------------------------------------------------------- world helpers

def split_csv_ranges(text):
    """Util.SplitCSV(text, true): ';' or ',' separated ids, 'a-b' ranges expanded."""
    out = set()
    for part in re.split(r"[;,]", text or ""):
        part = part.strip()
        if not part:
            continue
        m = re.fullmatch(r"(\d+)-(\d+)", part)
        if m:
            out.update(range(int(m.group(1)), int(m.group(2)) + 1))
        elif part.isdigit():
            out.add(int(part))
    return out


def server_property(con, key, default):
    r = con.execute("select Value from ServerProperty where Key = ?", (key,)).fetchone()
    return r[0] if r and r[0] is not None else default


def disabled_regions(con):
    """Regions a player cannot enter: world/Region.cs:234-245 (+ IsExpansionDisabled 254-263)."""
    ids = split_csv_ranges(server_property(con, "disabled_regions", ""))
    exp = split_csv_ranges(server_property(con, "disabled_expansions", ""))
    out = {}
    for r in con.execute("select RegionID, Expansion, Description from Regions"):
        if r["RegionID"] in ids:
            out[r["RegionID"]] = "region disabled (disabled_regions)"
        elif (r["Expansion"] + 1) in exp:
            out[r["RegionID"]] = "expansion disabled (disabled_expansions)"
    return out


def load_templates(con):
    """TemplateId -> row. NpcTemplateMgr.GetTemplate picks a random one when an id is duplicated; we take the
    first by NpcTemplate_ID and report merchants whose duplicates disagree on a field we use."""
    out, dup = {}, collections.defaultdict(list)
    for r in con.execute("select * from NpcTemplate order by TemplateId, NpcTemplate_ID"):
        dup[r["TemplateId"]].append(r)
        out.setdefault(r["TemplateId"], r)
    return out, dup


def item_kind(it):
    """One kind word for one item (used only to name what a merchant mostly sells)."""
    ot, slot = it["Object_Type"], it["Item_Type"]
    name = (it["Name"] or "").lower()
    if 2 <= ot <= 28:
        return "weapons"
    if 31 <= ot <= 38:
        return "armor"
    if ot == 42:
        return "shields"
    if ot in (43, 44):
        return "ammo"
    if ot == 45:
        return "instruments"
    if ot == 46:
        return "poisons"
    if ot in (47, 48):
        return "crafting"
    if 49 <= ot <= 71:
        return "housing"
    if ot == 41:
        return "jewelry" if 24 <= slot <= 37 else "potions"
    if (it["Id_nb"] or "").lower().startswith("deploy_") or "siege" in name:
        return "siege"
    if re.search(r"\b(dye|enamel|wash|remover|bleach)\b", name):
        return "dyes"
    if "medallion" in name or "necklace" in name:
        return "medallions"
    return "crafting"


def of_merchant_identity(cls_simple, realm, region):
    """scripts/teleporters/OFMerchant.cs:27-55 and 76-118: name and list by realm (and region for the Home kind)."""
    name = {1: "Sall Fadri", 2: "Gwulla", 3: "Araisa"}.get(realm)
    if name is None:
        return None, None
    tag = {1: "Alb", 2: "Mid", 3: "Hib"}[realm]
    if cls_simple == "OFMerchant":
        return name, "OFMerchant_" + tag
    other = {1: {200: "Hib", 100: "Mid"}, 2: {200: "Hib", 1: "Alb"}, 3: {100: "Mid", 1: "Alb"}}[realm]
    return name, "OFMerchant_%s_Home%s" % (tag, other.get(region, ""))


def frontier_label(list_id):
    """gameutils/FrontierSupplyMerchantPolicy.cs:18-27."""
    if list_id in ("SiegeMerchantAlb", "SiegeMerchantMid", "SiegeMerchantHib"):
        return "Siege Equipment"
    if list_id and list_id.startswith("OFMerchant_"):
        return "Teleport Medallions"
    return None


# ---------------------------------------------------------------------------------------------- the merchants

def collect(con, verbose=True):
    """Every merchant the server creates from the Mob table, with its effective values.
    Returns dict(merchants=[...], left=Counter, lists={list id: [rows]}, items={id lower: row}, ...).
    build_routes.py imports this so both files share the merchant ids."""
    bases, guilds, merchant_classes = scan_server_classes()
    mkeys = money_keys()
    use_guild_scripts = str(server_property(con, "use_npcguildscripts", "True")).lower() == "true"
    sell_ratio = int(server_property(con, "item_sell_ratio", "50"))
    zs = wg.zones(con)
    zbr = wg.zones_by_region(zs)
    off = disabled_regions(con)
    templates, tdups = load_templates(con)
    factions = {r["ID"]: r for r in con.execute("select ID, Name, BaseAggroLevel from Faction")}
    items = {r["Id_nb"].lower(): r for r in con.execute(
        "select Id_nb, Name, Level, Object_Type, Item_Type, Price, PackSize, Color, Realm, Description from ItemTemplate")}
    mi = collections.defaultdict(list)
    n_mi = 0
    for r in con.execute("select ItemListID, ItemTemplateID, PageNumber, SlotPosition, MerchantItem_ID from MerchantItem"
                         " order by ItemListID, PageNumber, SlotPosition, MerchantItem_ID"):
        mi[r["ItemListID"].lower()].append(r)        # the column is COLLATE NOCASE, as the server's SQL sees it
        n_mi += 1

    left = collections.Counter()
    out, classes_seen, ambiguous = [], collections.Counter(), []
    rows = con.execute("select * from Mob order by Region, Name, X, Y, Mob_ID").fetchall()
    for m in rows:
        t = templates.get(m["NPCTemplateID"]) if m["NPCTemplateID"] not in (None, -1) else None   # Region.cs:739
        guild = m["Guild"] or ""
        cls = None
        if use_guild_scripts and guild and 0 <= m["Realm"] <= 3:                                  # Region.cs:745
            cls = guilds.get((guild, m["Realm"]))
        if cls is None:
            if t is not None and t["ClassType"] and t["ClassType"] != DEFAULT_NPC_CLASS and t["ReplaceMobValues"]:
                cls = t["ClassType"]                                                              # Region.cs:766
            elif m["ClassType"] and m["ClassType"] != DEFAULT_NPC_CLASS:
                cls = m["ClassType"]                                                              # Region.cs:770
            else:
                cls = DEFAULT_NPC_CLASS
        # class names in the table are compared case-insensitively nowhere in C#; keep the exact text
        if cls not in merchant_classes:
            continue
        simple = cls.split(".")[-1]
        classes_seen[cls] += 1
        replace = bool(t is not None and t["ReplaceMobValues"])
        name = (t["Name"] if replace else m["Name"]) or ""
        guild_eff = (t["GuildName"] if replace else m["Guild"]) or ""
        flags = int((t["Flags"] if replace else m["Flags"]) or 0)
        faction = int((t["FactionID"] if replace else m["FactionID"]) or 0)
        realm = int(m["Realm"])
        list_id = None
        if t is not None and t["ItemsListTemplateID"]:
            list_id = t["ItemsListTemplateID"]                                                   # GameMerchant.cs:353
        if m["ItemsListTemplateID"]:
            list_id = m["ItemsListTemplateID"]                                                   # GameMerchant.cs:371
        if simple in ("OFMerchant", "OFMerchantHome"):
            nm, lid = of_merchant_identity(simple, realm, m["Region"])
            if nm:
                name, list_id = nm, lid
            guild_eff = "Medallion Merchant"
            flags |= PEACE
        label = frontier_label(list_id)
        if label:
            guild_eff = label                                                                    # ApplyLabel
        if t is not None and len(tdups[m["NPCTemplateID"]]) > 1:
            sig = {(d["Name"], d["GuildName"], d["Flags"], d["FactionID"], d["ItemsListTemplateID"], d["ReplaceMobValues"],
                    d["ClassType"]) for d in tdups[m["NPCTemplateID"]]}
            if len(sig) > 1:
                ambiguous.append(name)

        z = wg.zone_of(zbr, m["Region"], m["X"], m["Y"])
        currency, currency_item = "coin", None
        if derives(bases, cls, "GameBountyMerchant"):
            currency = "bp"
        elif derives(bases, cls, "GameItemCurrencyMerchant"):
            key = mkeys.get(simple)
            ci = items.get((key or "").lower())
            currency, currency_item = "item", (ci["Id_nb"] if ci else key)
        own_realm_only = derives(bases, cls, "GameDarknessFallsSealsMerchant")
        fac = factions.get(faction)
        gated = realm == 0 and fac is not None and not (flags & PEACE)                           # GameNPC.cs:2633
        out.append(dict(
            mob=m["Mob_ID"], cls=cls, simple=simple, name=name, guild=guild_eff, realm=realm, region=m["Region"],
            rx=m["X"], ry=m["Y"], rz=m["Z"], zone=z, flags=flags, faction=faction if fac is not None else 0,
            fac=fac, gated=gated, list=list_id, currency=currency, currency_item=currency_item,
            own_realm_only=own_realm_only, is_stable=derives(bases, cls, "GameStableMaster"),
            off=off.get(m["Region"]), template=m["NPCTemplateID"]))

    # the buyable rows of each list a merchant uses
    lists = {}
    stats = collections.Counter()
    for v in out:
        lid = v["list"]
        if not lid or lid.lower() in lists:
            continue
        rows_ok, seen = [], set()
        for r in mi.get(lid.lower(), ()):
            it = items.get(r["ItemTemplateID"].lower())
            if it is None:
                stats["rows skipped: item template missing"] += 1
                continue
            if not (0 <= r["PageNumber"] < MAX_PAGES and 0 <= r["SlotPosition"] < MAX_SLOTS):
                stats["rows skipped: page or slot outside the window"] += 1
                continue
            key = (r["PageNumber"], r["SlotPosition"])
            if key in seen:                       # MerchantTradeItems.cs:158-165: the first row keeps the slot
                stats["rows skipped: slot already taken"] += 1
                continue
            seen.add(key)
            rows_ok.append((it, r["PageNumber"], r["SlotPosition"]))
        lists[lid.lower()] = rows_ok
    used = set(lists)
    all_lists = set(mi)
    return dict(merchants=out, lists=lists, items=items, zones=zs, zbr=zbr, factions=factions, left=left, stats=stats,
                classes=classes_seen, ambiguous=ambiguous, sell_ratio=sell_ratio, bases=bases, off=off,
                unused_lists=sorted(all_lists - used), all_lists=len(all_lists), mi_rows=n_mi,
                unused_rows=sum(len(mi[k]) for k in all_lists - used), templates=templates)


def assign_ids(world):
    """Stable integer ids for the merchants that go into vendors.js: order by region, name, x, y, Mob_ID.
    A merchant is listed when a player can reach it and it has at least one thing to buy. Returns the kept list."""
    kept = []
    seen_spot = set()
    left = world["left"]
    for v in world["merchants"]:
        rows = world["lists"].get((v["list"] or "").lower(), [])
        v["rows"] = rows
        if v["off"]:
            left["left out: " + v["off"]] += 1
            continue
        if v["zone"] is None:
            left["left out: position in no zone"] += 1
            continue
        if not v["list"]:
            left["left out: no item list"] += 1
            continue
        if not rows:
            left["left out: list has nothing a player can buy"] += 1
            continue
        key = (v["region"], v["rx"], v["ry"], v["name"].lower(), v["list"].lower(), v["cls"])
        if key in seen_spot:                      # two Mob rows for one merchant on one spot (Ley Manton, Tria Ellowis)
            left["left out: second spawn row of the same merchant on the same spot"] += 1
            continue
        seen_spot.add(key)
        kept.append(v)
    kept.sort(key=lambda v: (v["region"], v["name"].lower(), v["rx"], v["ry"], v["mob"]))
    for i, v in enumerate(kept, 1):
        v["id"] = i
    return kept


def reputation_of(v):
    """None, or the data form of the gate in GameNPC.cs:2633 for this merchant."""
    if not v["gated"]:
        return None
    f = v["fac"]
    starts = f["BaseAggroLevel"] > 50
    return dict(faction=f["ID"], factionName=f["Name"], base=f["BaseAggroLevel"], maxAggro=50, need=-50,
                start=-f["BaseAggroLevel"], startsRefused=starts,
                text="%s reputation -50 or better (a new character starts at %d: %s)" % (
                    f["Name"], -f["BaseAggroLevel"], "refused" if starts else "allowed"))


def capitals(con, world):
    """Per realm: the capital city zone and the outdoor side of each of its gates (ZonePoint rows that lead in)."""
    caps = {1: 10, 2: 101, 3: 201}                 # region ids: Camelot City, Jordheim, Tir na Nog
    out = {}
    for realm, region in caps.items():
        city = [z for z in world["zones"].values() if z["region"] == region]
        assert len(city) == 1, "capital region %d should hold one zone" % region
        gates = []
        for r in con.execute("select Id, SourceX, SourceY, SourceRegion from ZonePoint where TargetRegion = ?"
                             " and SourceRegion <> ? order by Id", (region, region)):
            z = wg.zone_of(world["zbr"], r["SourceRegion"], r["SourceX"], r["SourceY"])
            if z is not None:
                gates.append(dict(zone=z["id"], x=r["SourceX"] - z["ox"], y=r["SourceY"] - z["oy"]))
        assert gates, "no gate found for capital region %d" % region
        out[str(realm)] = dict(zone=city[0]["id"], gate=gates[0], gates=gates)
    return out


def main():
    a = wg.args(__doc__.split("\n")[0])
    con = wg.open_db(a.db)
    world = collect(con)
    kept = assign_ids(world)
    items = world["items"]

    # kind of each list = the commonest item kind on it
    list_kind = {}
    for lid, rows in world["lists"].items():
        c = collections.Counter(item_kind(it) for it, _, _ in rows)
        order = [k for k, _ in KINDS]
        list_kind[lid] = max(c, key=lambda k: (c[k], -order.index(k))) if c else None

    cols = ["id", "name", "guild", "realm", "zone", "x", "y", "list", "kind", "n", "currency", "currencyItem",
            "ownRealmOnly", "rep", "stable"]
    vrows, used_lists = [], {}
    for v in kept:
        lid = v["list"].lower()
        used_lists.setdefault(lid, v["list"])
        kind = "stable" if v["is_stable"] else list_kind[lid]
        if frontier_label(v["list"]) == "Teleport Medallions":
            kind = "medallions"
        z = v["zone"]
        vrows.append([v["id"], v["name"], v["guild"], v["realm"], z["id"], v["rx"] - z["ox"], v["ry"] - z["oy"],
                      used_lists[lid], kind, len(v["rows"]), v["currency"], v["currency_item"],
                      1 if v["own_realm_only"] else 0, reputation_of(v), 1 if v["is_stable"] else 0])

    # vendoritems: list -> [[item id, price, page, slot, pack]]
    lists_out, names = {}, {}
    n_rows = 0
    for lid, shown in sorted(used_lists.items(), key=lambda kv: kv[1].lower()):
        rows = []
        for it, page, slot in sorted(world["lists"][lid], key=lambda r: (r[1], r[2])):
            pack = it["PackSize"] if it["PackSize"] and it["PackSize"] > 0 else 1       # GameMerchant.cs:106
            price = int(it["Price"] or 0)
            assert price >= 0, "negative price on " + it["Id_nb"]
            rows.append([it["Id_nb"], price, page, slot] + ([pack] if pack != 1 else []))
            names[it["Id_nb"]] = it["Name"]
        lists_out[shown] = rows
        n_rows += len(rows)

    currencies = {}
    for v in kept:
        if v["currency"] == "item" and v["currency_item"]:
            ci = items.get(v["currency_item"].lower())
            currencies[v["currency_item"]] = ci["Name"] if ci else v["currency_item"]

    left = dict(world["left"])
    never_placed = len(world["unused_lists"])
    meta = dict(
        built=wg.stamp(), merchants=len(vrows), lists=len(lists_out), rows=n_rows,
        leftOut=left, skippedRows=dict(world["stats"]),
        listsNoMerchantUses=never_placed, rowsOnThoseLists=world["unused_rows"],
        listsInWorld=world["all_lists"], rowsInWorld=world["mi_rows"],
        sell=dict(ratio=world["sell_ratio"],
                  text="Any merchant buys what you can drop for %d%% of its value: value x count / pack size x %d / 100, "
                       "rounded down. An item that cannot be dropped sells for nothing." % (world["sell_ratio"], world["sell_ratio"])),
        price="The price is the item's value in copper for one purchase. One purchase gives the pack size "
              "(arrows, bolts and poisons come 20 at a time). Bounty merchants take that number in bounty points, "
              "seal merchants in seals. There is no realm tax and no True 1.65 price change.",
        reputation="A merchant with a faction and no realm refuses you while that faction is Hostile or worse "
                   "(aggro above 50). rep.startsRefused says whether a new character is refused.")

    # checks
    ids = [r[0] for r in vrows]
    assert ids == list(range(1, len(ids) + 1)), "merchant ids must be 1..n"
    assert all(r[7] in lists_out for r in vrows), "every merchant's list must be in vendoritems"
    assert all(lists_out[r[7]] for r in vrows), "a listed merchant has an empty list"
    assert set(lists_out) == {r[7] for r in vrows}, "vendoritems holds a list no listed merchant uses"
    zs = world["zones"]
    for r in vrows:
        z = zs[r[4]]
        assert 0 <= r[5] < z["w"] and 0 <= r[6] < z["h"], "merchant outside its zone: %r" % (r[:7],)
        assert r[8] in dict(KINDS), "unknown kind %r" % (r[8],)
    assert 1000 < len(vrows) < 2000, "unexpected merchant count %d" % len(vrows)
    for lid, rows in lists_out.items():
        assert len({(x[2], x[3]) for x in rows}) == len(rows), "two items in one slot on " + lid
    zfile = os.path.join(a.out, "zones.js")
    if os.path.exists(zfile):                      # P-D's zone list, when it is there: every zone we use must be in it
        import json
        t = open(zfile, encoding="utf-8").read()
        known = {z["id"] for z in json.loads(t[t.index(",") + 1:t.rindex(")")])["zones"]}
        assert {r[4] for r in vrows} <= known, "merchant zones not in zones.js: %s" % sorted({r[4] for r in vrows} - known)

    wg.write_data("vendors", dict(meta=meta, kinds=KINDS, currencies=currencies, realms=REALM_NAME,
                                  capitals=capitals(con, world), vendors=wg.table(cols, vrows)), a.out,
                  "%d merchants" % len(vrows))
    wg.write_data("vendoritems", dict(lists=lists_out, names=names), a.out,
                  "%d lists, %d rows, %d distinct items" % (len(lists_out), n_rows, len(names)))

    print("merchant classes:", dict(world["classes"]))
    print("merchant rows in Mob: %d, listed: %d" % (len(world["merchants"]), len(vrows)))
    for k, n in sorted(left.items()):
        print("  %-55s %d" % (k, n))
    for k, n in sorted(world["stats"].items()):
        print("  %-55s %d" % (k, n))
    print("lists in MerchantItem: %d (%d rows); used by a listed merchant: %d (%d rows shown); used by no placed "
          "merchant: %d (%d rows)" % (world["all_lists"], world["mi_rows"], len(lists_out), n_rows, never_placed,
                                      world["unused_rows"]))
    print("by kind:", dict(collections.Counter(r[8] for r in vrows)))
    print("by realm:", dict(collections.Counter(r[3] for r in vrows)))
    print("by currency:", dict(collections.Counter(r[10] for r in vrows)))
    print("reputation-gated merchants:", [(r[1], r[13]["factionName"], r[13]["base"]) for r in vrows if r[13]])
    print("left out, by name:", sorted((v["name"], v["region"], v["off"] or ("no list" if not v["list"] else "empty list"))
                                       for v in world["merchants"] if "id" not in v))
    if world["ambiguous"]:
        print("merchants whose duplicated NPC template rows disagree (first row used):", sorted(set(world["ambiguous"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
