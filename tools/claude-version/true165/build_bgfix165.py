"""True 1.65 0.8 bgfix (lane BGCONTENT, lead brief <lead workspace>/bgfix\\BGFIX-BRIEF.md, report
<lead workspace>/bgfix\\reports\\BGCONTENT.md). The owner (2026-11-12): "all reported issues need an implementation including
guards, etc. check the caledonia guards and lord stats against period"; Siege Masters: "put them back. make the bots
use them." Period-sourced guard and lord level corrections are approved for this pass; nothing unsourced.

A. Abermenai's three Siege Masters (add-bgfix165.json). The 1.53D notes (Freddyshouse 97993, 2002-09-12) put a Siege
   Master "at your realm's portal keep within the Battlegrounds" (ALL battlegrounds); Abermenai opened in 2003 with the
   same portal keeps (Master of Runes / Master Eldritch there: FH 127534, 2003-06-02). Copies of Murdaigean's three rows
   (same lists SiegeMerchantAlb/Mid/Hib, realm, model, spot: the portal keeps stand at the same coordinates in all four
   battlegrounds). The other 12 battleground Siege Masters are base-world rows; build_siege165.py no longer removes them.

B. Portal keep guards in all four battlegrounds (add-bgfix165.json + add-bgfix165-npctemplates.json). Period: the Prima
   Atlas (2002) battleground maps put these defenders at the portal keeps - Albion: Man at Arms, Bowman, Master Wizard;
   Midgard: Viking Huscarl, Viking Archer, Troll Archer, Master of Runes; Hibernia: Guardian, Ranger, Master Ranger,
   Master Eldritch - and its battleground lists give their levels: the realm guards [..,90], Troll Archer [90], the four
   Masters [95]. Forum: "xxx is killed by a master eldritch, xxx is killed by a bowmen, xxx is killed by a Man at Arms"
   (FH 101723, Thidranki, 2002-10-17); "killed by master of runes and troll archers" (FH 100723, 2002-10-11); "a Maser of
   Runes or Master Eldrich in Abermenai" (FH 127534). The spots are the base world's 31 portal keep guard rows (archived
   2026-10-30 as "Atlas placeholder spawn", name "new mob", identical in Caledonia and Thidranki; 2 fighters at the gate
   and 8-9 on the walls of each portal keep); Murdaigean and Abermenai never had any and get the same 31 spots. The two
   gate spots take the realm's fighter; the wall spots take the realm's caster and archer types in turn around the keep
   (the per-type count has no period source). The rows are DOL.GS.GameGuard with fixed period names, like this world's
   frontier portal keep guards (keep-class guards would take the server's own names, Knight Commander / Nordic Jarl /
   Champion, and level 255). Their NpcTemplates are copies of the frontier portal keep guard templates (appearance,
   gear, spells) with the Prima level instead of 255 and aggro range 2,000 (the server's portal keep guard range,
   GameKeepGuard.SetAggression).

C. Central keep guards (true165-rules.json updates, tag "True 1.65 0.8 bgfix: "):
   * Caer Caledon: Keep.BaseLevel 46 -> 33. Period: the Caledonia keep's guards in the Prima Atlas lists are "Stoddard
     Archer [35]", "Baron Protector [35]", "Stoddard Warder [40]" (the keep was "Stoddard keep": FH 94033, 2002-08-01);
     a level 34 infiltrator saw its archers and inner melee guards orange (35-37 to a 34), the three outside the front
     gate red (38-40) and the lord purple (41+) (FH 93539, 2002-07-31). The server makes guards BaseLevel+2 and the lord
     BaseLevel + (BaseLevel/10+1)*2 + 1 at keep level 1: 33 gives guards 35 and lord 42 (was 48 / 57). Side effect: the
     keep doors' health follows BaseLevel (x200): 9,200 -> 6,600.
   * Caer Caledon's five wall casters become static archers (FH 93539: "roughly 7 archers on the first floor/walls" -
     we had 2 archers + 5 casters there; the Prima lists name no keep casters in any battleground), and the two archers
     "on the roof" come back on the lord's floor (Z 4890) as new rows.
   * Thidranki (build_bgkeeps165.py, its own add-files) and Murdaigean keep their BaseLevel: it already gives the Prima
     lords exactly (Thidranki 33, Murdaigean 40, and Murdaigean's commanders 33). Their ordinary guards come out at
     28 / 33 against the Prima 24 / 30, because the server derives every guard from BaseLevel. The Mob rows' Level column
     is set to the Prima values here (and the Caledonia values above) so that the BG row-level code change requested from
     the lead (keep guards in a battleground keep take their row level) makes them exact. Abermenai has no period
     source: its rows get today's computed values (guards 23, lord 28), so nothing changes there either way.

python -I build_bgfix165.py         rerunnable: rewrites both add-files and replaces its own rules entries
Out (both copies, runtime\\true165 and tools\\claude-version\\true165): add-bgfix165.json (Mob),
add-bgfix165-npctemplates.json (NpcTemplate), true165-rules.json (through <lead workspace>/gamefix\\locks.py edit_rules).
Reads the clean v0.7 world read-only (mode=ro) and checks every new spot on the server navmesh (navmesh-165).
"""
import json, math, os, sqlite3, sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

LANE = "BGCONTENT"
TAG = "True 1.65 0.8 bgfix: "
HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
FOLDERS = [os.path.join(CV, "runtime", "true165"), HERE]
CLEAN = os.environ.get("OFFLINE_DAOC_CLEAN_DB") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "runtime", "data", "opendaoc-165.sqlite3.db")   # the clean world (database/unpack_db.py)
NAV = os.path.join(CV, "runtime", "server", "navmesh-165", "zone%d.nav")
MOBFILE = "add-bgfix165.json"
TMPLFILE = "add-bgfix165-npctemplates.json"
PACKAGE = "true165-bgfix"
STAMP = "2026-11-12 21:00:00"
BG_REGIONS = (250, 251, 252, 253)
BG_NAMES = {250: "caledonia", 251: "murdaigean", 252: "thidranki", 253: "abermenai"}
ARRIVALS = [(36428, 51260, 3952), (53150, 26093, 4263), (19714, 19085, 4094)]   # BattlegroundBrackets.BotArrival
KEEP_CENTRE = (33089, 38271)

# --- A. Abermenai Siege Masters: copies of Murdaigean's rows -------------------------------------------------------
MURD_SIEGEMASTERS = {"alb": "3c34287b-aa10-438a-9bcb-482b088c5dee", "mid": "70a84a61-e472-4585-9213-86a46b412f57",
                     "hib": "8ae1b50b-d05a-4b46-83ce-521c0a5684b0"}

# --- B. Portal keep guards --------------------------------------------------------------------------------------
PORTAL_KEEPS = {1: (37301, 52362), 2: (54053, 24680), 3: (18362, 18257)}   # Keep rows, same in all four regions
REALM_TAG = {1: "alb", 2: "mid", 3: "hib"}
# name: (source NpcTemplate, gear to use, Prima level, new TemplateId)
TYPES = {
    "Man at Arms":     (60157485, "PortalKeepFighterAlb", 90, 916530001),
    "Bowman":          (60157484, "PortalKeepArcherAlb", 90, 916530002),
    "Master Wizard":   (60163837, "MasterWizard", 95, 916530003),
    "Viking Huscarl":  (60157473, "SvasudGuard", 90, 916530004),
    "Viking Archer":   (60167654, None, 90, 916530005),
    "Troll Archer":    (60167287, None, 90, 916530006),
    "Master of Runes": (60163807, None, 95, 916530007),
    "Guardian":        (110, "HibGuardian", 90, 916530008),
    "Ranger":          (60163820, "MasterRanger", 90, 916530009),
    "Master Ranger":   (60163820, "MasterRanger", 95, 916530010),
    "Master Eldritch": (60157481, None, 95, 916530011),
}
FIGHTER = {1: "Man at Arms", 2: "Viking Huscarl", 3: "Guardian"}
WALL = {1: ["Master Wizard", "Bowman"], 2: ["Master of Runes", "Troll Archer", "Viking Archer"],
        3: ["Master Eldritch", "Master Ranger", "Ranger"]}
PK_AGGRO_RANGE = 2000
PK_RESPAWN = 300

# --- C. Central keep guards ------------------------------------------------------------------------------------
CALEDON_KEEP = 31
CALEDON_BASELEVEL = (46, 33)
CALEDON_CASTERS = ["caledon-guard-23", "caledon-guard-25", "caledon-guard-37", "caledon-guard-46", "caledon-guard-48"]
CALEDON_OUTSIDE = ["caledon-guard-6", "caledon-guard-7", "caledon-guard-12"]     # in front of gate 250000301
CALEDON_ROOF = [(33385, 36736, 4890), (33609, 37248, 4891)]                     # lord's floor, >= 550 from the lord
CALEDON_ARCHER_SOURCE = "caledon-guard-21"
# row levels: region -> class -> level (Prima Atlas; Caledonia from Prima "Stoddard" rows + FH 93539; Abermenai = today)
ROW_LEVELS = {
    250: {"GuardFighter": 35, "GuardStaticArcher": 35, "GuardArcher": 35, "GuardStaticCaster": 35, "GuardLord": 42},
    251: {"GuardFighter": 30, "GuardArcher": 30, "GuardStaticArcher": 30, "GuardCommander": 33, "GuardLord": 40},
    252: {"GuardFighter": 24, "GuardArcher": 24, "GuardStaticArcher": 24, "GuardStaticCaster": 24, "GuardLord": 33},
    253: {"GuardFighter": 23, "GuardArcher": 23, "GuardStaticArcher": 23, "GuardCommander": 23, "GuardLord": 28},
}
CALEDON_OUTSIDE_LEVEL = 40       # Prima "Stoddard Warder [40]"; FH 93539: the three outside the front gate red to a 34

# --- D. Follow-up (owner 2026-11-12: "just guess and place them where it makes sense and their levels for the bridge
#        guards too. get it all implemented properly"). Every spot below is a GUESS where no source gives one.
# D1. Caer Caledon's outside guards away from the gate (FH 93539, 2002-07-31, to a level 34: "1 red and 1 orange between
#     the Midgard and Albion bridge. 1 purple ... at the Hibernian bridge"; the scout also saw "two of those huscarls
#     came from outside the keep": fighters). The bridges (1.64 client N-Bridge2 fixtures) stand 4,800-6,000 from the keep,
#     outside its 3,000 KeepArea, so each guard stands on the keep's side of the field, about 2,650-2,700 out on the line
#     towards its bridge, where it still joins keep.Guards (keeps\KeepArea.cs KEEP_RADIUS) and changes realm with it.
#     (id suffix, X, Y, Z, row level): red = 38-40 -> 40 (Stoddard Warder [40]); orange 35-37 -> 35; purple 41+ -> 41.
CALEDON_BRIDGE_GUARDS = [("bridge-guard-1", 34824, 40339, 3331, 40), ("bridge-guard-2", 33557, 40929, 3246, 35),
                         ("bridge-guard-3", 31215, 36397, 3285, 41)]
CALEDON_FIGHTER_SOURCE = "caledon-guard-7"
# D2. Thidranki commanders: the Prima Atlas Thidranki list names Knight Commander / Viking Jarl / Champion [27]; count and
#     spots are a GUESS: 3 (as Murdaigean's keep), in the courtyard: just inside keep door 252000302, and north and south.
THID_COMMANDERS = [("commander-1", 33930, 38272, 3722), ("commander-2", 33280, 38772, 3722), ("commander-3", 33280, 37772, 3722)]
THID_COMMANDER_LEVEL = 27
THID_COMMANDER_SOURCE = "131ca907-64dc-4356-b0ef-3fed683a919e"   # a Murdaigean GuardCommander row
# D3. The named portal keep NPCs of the Prima Atlas (maps p.246-248, lists p.249; the period Allakhazam bestiary has Pmara
#     "Humanoid, All Realms, 35", Reson "Humanoid, 41", Faulkener "Monster, 8", all Thidranki). Their role is unknown: a
#     GUESS - peaceful realm NPCs (flag PEACE) standing in their own portal keep near the portal, at the Atlas level.
#     Caledonia's three appear only on its map (no level): the Thidranki/Murdaigean tiers are used (Hib 35, Mid 41, Alb 8).
#     Abermenai has no named portal keep NPC in any source: none is placed.
PK_NAMED = {250: {1: "Groush", 2: "Eriksson", 3: "Clannach"}, 251: {1: "Sourg", 2: "Neprac", 3: "Teegra"},
            252: {1: "Faulkener", 2: "Reson", 3: "Pmara"}}
PK_NAMED_LEVEL = {1: 8, 2: 41, 3: 35}
PK_NAMED_SPOT = {1: (37963, 53121, 4154), 2: (54639, 24302, 4323), 3: (18248, 17829, 4324)}   # near each portal
# D4. Merchant keeps (period: Caer Benowyc, Bledmeer Faste, Dun Crauchon - "In these keeps there are merchants who sell
#     siege parts, there are also woodmerchants there", FH 89097 2002-05-29; the higher level tradeskill material merchants,
#     FH 91351 1.51 notes / FH 91463 "u can buy lvl 9-10 materials in the merchant keep"; ammo "only ... at the merchant
#     keep", FH 86254, FH 100226; merchants lost with the keep, FH 104945). Spots and names: the CapnBry radar's "Merchant"
#     NPCs in each keep (names change with the owning realm; which name belongs to which realm and role is a GUESS).
#     Class DOL.GS.Keeps.GuardMerchant: a keep guard that takes the keep's realm and sells only to that realm. Name
#     "Alb|Mid|Hib" and ItemsListTemplateID "<base>" (-> "<base>Alb/Mid/Hib") need the code change requested in the report;
#     without it the server shows its own names and every one sells the realm's RvRCraftingList (the material merchant).
MK_KEEPS = {"benowyc": ("Caer Benowyc", 1), "bledmeer": ("Bledmeer Faste", 100), "crauchon": ("Dun Crauchon", 200)}
MK_MERCHANTS = [   # keep, role, X, Y, Z, "Alb|Mid|Hib" (empty = the server's own name), list base (None = RvRCraftingList)
    ("benowyc", "siege", 653796, 346557, 6284, "|Luthor|", "true165MkSiege"),
    ("benowyc", "wood", 653581, 345555, 6245, "|Olafsson|", "true165MkWood"),
    ("benowyc", "materials", 653744, 345959, 6282, "|Vrudon|", None),
    ("bledmeer", "siege", 647733, 583620, 7432, "Collen|Frenrik|Freagh", "true165MkSiege"),
    ("bledmeer", "wood", 647741, 583267, 7434, "Daggon|Yvigir|Vauclua", "true165MkWood"),
    ("bledmeer", "materials", 648087, 583176, 7434, "Logan|Hridnon|Tinoawan", None),
    ("crauchon", "siege", 425504, 318090, 4730, "|Brera|Naradith", "true165MkSiege"),
    ("crauchon", "wood", 425940, 317876, 4730, "|Svenrir|Ellyoron", "true165MkWood"),
    ("crauchon", "materials", 425649, 317871, 4730, "|Gundor|Haelrach", None),
]
MK_WOOD = ["rowan_wooden_boards", "elm_wooden_boards", "oak_wooden_boards", "ironwood_wooden_boards",
           "heartwood_wooden_boards", "runewood_wooden_boards", "stonewood_wooden_boards"]
MK_AMMO = {1: ["catapult_stone", "ballista_bolt"], 2: ["catapult_stone2", "ballista_bolt2"], 3: ["catapult_stone3", "ballista_bolt3"]}
MERCHFILE = "add-bgfix165-merchantitems.json"
# D5. The battleground Siege Masters' stock: the period Thidranki Siegemasters sold "Field Ballista Token, Field Catapult
#     Token, Field Trebuchet Token, Small Ram Token" (Allakhazam bestiary, Siegemaster Cadman / Camey / Erinden / Ordrand /
#     Salendar / Sigfreid). The Atlas "heavy siege ram" (deploy_heavy_siege_ram, 100g) is not in it: removed from the three
#     SiegeMerchant lists (only the battleground Siege Masters use them). Prices are NOT changed: one period price only
#     (FH 146112, 2003-09-09: "Cost of one ram : 5g  Cost of ammunition : 1g"), no second source.

sql_list = lambda ids: ",".join("'%s'" % i for i in ids)


def open_clean():
    return sqlite3.connect("file:" + CLEAN.replace(" ", "%20") + "?mode=ro", uri=True)


def rows_of(con, sql, args=()):
    cur = con.execute(sql, args)
    names = [d[0] for d in cur.description]
    return [dict(zip(names, r)) for r in cur]


def heading_from(x0, y0, x1, y1):
    """DOL Point2D.GetHeading from (x0,y0) towards (x1,y1)."""
    h = math.atan2(-(x1 - x0), (y1 - y0)) * (2048 / math.pi)
    return int(h + 4096) % 4096


class Nav:
    def __init__(self):
        sys.path.insert(0, os.path.join(CV, "development-source", "server", "tools", "navmesh_splice"))
        from navcomp import NavAreas
        self.areas = {z: NavAreas(NAV % z) for z in BG_REGIONS}
        self.main = {z: {a.root[a.locate(*p, tolerance=64)] for p in ARRIVALS} for z, a in self.areas.items()}

    def check(self, region, label, x, y, z):
        a = self.areas[region]
        i = a.locate(x, y, z, tolerance=64)
        if i is None or a.root[i] not in self.main[region]:
            raise SystemExit(f"{label} at {region}:{x},{y},{z} is not on the main navmesh-165 area")


def build_templates(con):
    out = []
    for name, (src, gear, level, tid) in TYPES.items():
        t = rows_of(con, "SELECT * FROM NpcTemplate WHERE TemplateId=?", (src,))
        assert len(t) == 1, (name, src, len(t))
        t = t[0]
        t.update(TemplateId=tid, Name=name, Level=str(level), ClassType="DOL.GS.GameGuard", AggroRange=PK_AGGRO_RANGE,
                 PackageID=PACKAGE, LastTimeRowUpdated=STAMP, NpcTemplate_ID="true165-bgfix-tmpl-%d" % tid,
                 TranslationId=None, Suffix=None, GuildName=None)
        if gear:
            t["EquipmentTemplateID"] = gear
        assert t["EquipmentTemplateID"], name
        assert con.execute("SELECT count(*) FROM NPCEquipment WHERE TemplateID=?",
                           (t["EquipmentTemplateID"].split(";")[0],)).fetchone()[0] > 0, (name, t["EquipmentTemplateID"])
        out.append(t)
    return out


def pk_spots(con):
    spots = []
    for (rj,) in con.execute("SELECT RowJson FROM offline_true165_removed WHERE SourceTable='Mob' AND "
                             "Reason='Atlas placeholder spawn' ORDER BY Id"):
        r = json.loads(rj)
        if r["Region"] != 250:
            continue
        realm, d = min(((rl, math.hypot(r["X"] - c[0], r["Y"] - c[1])) for rl, c in PORTAL_KEEPS.items()),
                       key=lambda t: t[1])
        if d > 3000:
            continue        # the central keep's own rows
        spots.append(dict(realm=realm, cls=r["ClassType"].split(".")[-1], X=r["X"], Y=r["Y"], Z=r["Z"],
                          Heading=r["Heading"], key=r["Mob_ID"]))
    assert len(spots) == 31, len(spots)
    return spots


def assign_pk_types(spots):
    """Gate fighters keep their spot; the wall spots take the realm's wall types in turn, by angle round the keep."""
    out = []
    for realm, (cx, cy) in PORTAL_KEEPS.items():
        mine = [s for s in spots if s["realm"] == realm]
        fighters = sorted((s for s in mine if s["cls"] == "GuardFighter"), key=lambda s: (s["X"], s["Y"]))
        walls = sorted((s for s in mine if s["cls"] != "GuardFighter"),
                       key=lambda s: math.atan2(s["Y"] - cy, s["X"] - cx))
        assert len(fighters) == 2 and len(walls) in (8, 9), (realm, len(fighters), len(walls))
        for s in fighters:
            out.append((s, FIGHTER[realm]))
        for n, s in enumerate(walls):
            out.append((s, WALL[realm][n % len(WALL[realm])]))
    return out


def mob_row(**kw):
    row = {"ClassType": "DOL.GS.GameGuard", "TranslationId": "", "Name": "", "Suffix": "", "Guild": "", "ExamineArticle": "",
           "MessageArticle": "", "X": 0, "Y": 0, "Z": 0, "Speed": 0, "Heading": 0, "Region": 0, "Model": 0, "Size": 50,
           "Strength": 0, "Constitution": 0, "Dexterity": 0, "Quickness": 0, "Intelligence": 0, "Piety": 0, "Empathy": 0,
           "Charisma": 0, "Level": 1, "Realm": 0, "EquipmentTemplateID": "", "ItemsListTemplateID": None, "NPCTemplateID": -1,
           "Race": 0, "Flags": 0, "AggroLevel": 0, "AggroRange": 0, "MeleeDamageType": 2, "RespawnInterval": PK_RESPAWN,
           "FactionID": 0, "BodyType": 0, "HouseNumber": 0, "Brain": "", "PathID": "", "OwnerID": "", "RoamingRange": 0,
           "IsCloakHoodUp": 0, "Gender": 0, "PackageID": PACKAGE, "VisibleWeaponSlots": 0, "LastTimeRowUpdated": STAMP,
           "Mob_ID": ""}
    row.update(kw)
    return row


def build_mobs(con, nav, templates):
    tmpl = {t["Name"]: t for t in templates}
    rows = []
    # A. Abermenai Siege Masters
    for tag, mid in MURD_SIEGEMASTERS.items():
        src = rows_of(con, "SELECT * FROM Mob WHERE Mob_ID=?", (mid,))
        assert len(src) == 1 and src[0]["Region"] == 251 and src[0]["ItemsListTemplateID"].startswith("SiegeMerchant"), mid
        r = dict(src[0])
        r.update(Region=253, Mob_ID="true165-bgfix-abermenai-siegemaster-" + tag, PackageID=PACKAGE, LastTimeRowUpdated=STAMP)
        nav.check(253, r["Mob_ID"], r["X"], r["Y"], r["Z"])
        rows.append(r)
    # B. Portal keep guards
    typed = assign_pk_types(pk_spots(con))
    for region in BG_REGIONS:
        count = {}
        for s, name in typed:
            t = tmpl[name]
            rtag = REALM_TAG[s["realm"]]
            count[rtag] = count.get(rtag, 0) + 1
            mid = "true165-bgfix-pk-%s-%s-%02d" % (BG_NAMES[region], rtag, count[rtag])
            nav.check(region, mid, s["X"], s["Y"], s["Z"])
            model = int(str(t["Model"]).split(";")[0])
            size = int(str(t["Size"]).split(";")[0].split("-")[0])
            rows.append(mob_row(Name=name, X=s["X"], Y=s["Y"], Z=s["Z"], Heading=s["Heading"], Region=region,
                                Model=model, Size=size, Level=int(t["Level"]), Realm=s["realm"],
                                EquipmentTemplateID=t["EquipmentTemplateID"], NPCTemplateID=t["TemplateId"],
                                Race=t["Race"], BodyType=t["BodyType"], AggroLevel=t["AggroLevel"],
                                AggroRange=t["AggroRange"], MeleeDamageType=t["MeleeDamageType"],
                                VisibleWeaponSlots=t["VisibleWeaponSlots"], Flags=t["Flags"], Mob_ID=mid))
    # C. Caer Caledon's two roof archers
    src = rows_of(con, "SELECT * FROM Mob WHERE Mob_ID=?", (CALEDON_ARCHER_SOURCE,))
    assert len(src) == 1 and src[0]["ClassType"] == "DOL.GS.Keeps.GuardStaticArcher", CALEDON_ARCHER_SOURCE
    for n, (x, y, z) in enumerate(CALEDON_ROOF, 1):
        mid = "true165-bgfix-caledon-roof-archer-%d" % n
        nav.check(250, mid, x, y, z)
        assert math.hypot(x - KEEP_CENTRE[0], y - KEEP_CENTRE[1]) < 3000     # inside the KeepArea (keeps\KeepArea.cs)
        r = dict(src[0])
        r.update(X=x, Y=y, Z=z, Heading=heading_from(KEEP_CENTRE[0], KEEP_CENTRE[1], x, y), Level=ROW_LEVELS[250]["GuardStaticArcher"],
                 Name="Renegade Hunter", Realm=0, PackageID=PACKAGE, LastTimeRowUpdated=STAMP, Mob_ID=mid)
        rows.append(r)
    # D1. Caer Caledon's guards towards the bridges (GUESSED spots, levels from FH 93539's colours)
    src = rows_of(con, "SELECT * FROM Mob WHERE Mob_ID=?", (CALEDON_FIGHTER_SOURCE,))[0]
    assert src["ClassType"] == "DOL.GS.Keeps.GuardFighter"
    for tag, x, y, z, lvl in CALEDON_BRIDGE_GUARDS:
        mid = "true165-bgfix-caledon-" + tag
        nav.check(250, mid, x, y, z)
        assert math.hypot(x - KEEP_CENTRE[0], y - KEEP_CENTRE[1]) < 2950, mid      # joins Caer Caledon's KeepArea
        r = dict(src)
        r.update(X=x, Y=y, Z=z, Heading=heading_from(KEEP_CENTRE[0], KEEP_CENTRE[1], x, y), Level=lvl, Realm=0,
                 PackageID=PACKAGE, LastTimeRowUpdated=STAMP, Mob_ID=mid)
        rows.append(r)
    # D2. Thidranki Faste's commanders (GUESSED count and spots; Prima level 27)
    src = rows_of(con, "SELECT * FROM Mob WHERE Mob_ID=?", (THID_COMMANDER_SOURCE,))[0]
    assert src["ClassType"] == "DOL.GS.Keeps.GuardCommander" and src["Region"] == 251
    thid_centre = (33280, 38272)                     # the 1.64 nfrontkeep1 fixture (csv252.mpk)
    for tag, x, y, z in THID_COMMANDERS:
        mid = "true165-bgfix-thidranki-" + tag
        nav.check(252, mid, x, y, z)
        assert math.hypot(x - thid_centre[0], y - thid_centre[1]) < 700, mid       # in the courtyard
        r = dict(src)
        r.update(Region=252, X=x, Y=y, Z=z, Heading=heading_from(thid_centre[0], thid_centre[1], x, y),
                 Level=THID_COMMANDER_LEVEL, Realm=0, PackageID=PACKAGE, LastTimeRowUpdated=STAMP, Mob_ID=mid)
        rows.append(r)
    # D3. Named portal keep NPCs (GUESSED role: peaceful; spot near the portal)
    looks = {m["Realm"]: m for m in rows_of(con, "SELECT * FROM Mob WHERE Mob_ID IN (%s)" % sql_list(MURD_SIEGEMASTERS.values()))}
    for region, by_realm in PK_NAMED.items():
        for realm, name in by_realm.items():
            x, y, z = PK_NAMED_SPOT[realm]
            mid = "true165-bgfix-pk-%s-%s-%s" % (BG_NAMES[region], REALM_TAG[realm], name.lower())
            nav.check(region, mid, x, y, z)
            lk = looks[realm]
            rows.append(mob_row(ClassType="DOL.GS.GameNPC", Name=name, X=x, Y=y, Z=z, Region=region,
                                Heading=heading_from(x, y, *PORTAL_KEEPS[realm]), Model=lk["Model"], Size=lk["Size"],
                                Level=PK_NAMED_LEVEL[realm], Realm=realm, Race=lk["Race"], BodyType=lk["BodyType"],
                                Flags=16, Mob_ID=mid))                          # 16 = GameNPC.eFlags.PEACE
    # D4. Merchant keep merchants (radar spots; GuardMerchant, keep-bound)
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
    import tools                                     # server navmesh (any zone) + DETECT reachability
    keeps = {r["Name"]: r for r in rows_of(con, "SELECT KeepID, Name, Region, X, Y, Z, Realm FROM Keep")}
    for key, role, x, y, z, names, base in MK_MERCHANTS:
        kname, region = MK_KEEPS[key]
        k = keeps[kname]
        assert k["Region"] == region and math.hypot(x - k["X"], y - k["Y"]) < 2950, (key, role)
        p = tools.nav_poly(region, x, y, z)
        if p is None or abs(p[2] - z) > 16 or tools.is_reachable(region, x, y) is not True:
            raise SystemExit(f"merchant keep spot {key}/{role} {x},{y},{z} not on the navmesh or not reachable: {p}")
        assert names.count("|") == 2, names
        rows.append(mob_row(ClassType="DOL.GS.Keeps.GuardMerchant", Name=names, Guild="Merchant", X=x, Y=y, Z=z,
                            Region=region, Heading=heading_from(x, y, k["X"], k["Y"]), Model=0, Level=50, Realm=k["Realm"],
                            ItemsListTemplateID=base, Brain="DOL.AI.Brain.KeepGuardBrain", Speed=0, RespawnInterval=0,
                            Mob_ID="true165-bgfix-mk-%s-%s" % (key, role)))
    # spacing: every new row at least 120 from every other NPC (old or new) on its floor
    allmobs = rows_of(con, "SELECT Mob_ID, Region, X, Y, Z FROM Mob WHERE Region IN (1,100,200,250,251,252,253)")
    for r in rows:
        if r["ClassType"] == "DOL.GS.GameMerchant":
            continue                                 # Abermenai's Siege Masters copy their Murdaigean twins exactly
        for o in allmobs + rows:
            if o is r or o["Mob_ID"] == r["Mob_ID"] or o["Region"] != r["Region"] or abs(o["Z"] - r["Z"]) > 250:
                continue
            d = math.hypot(o["X"] - r["X"], o["Y"] - r["Y"])
            if d < 120 and not (o["Mob_ID"].startswith("caledon-guard") and r["Mob_ID"].startswith("true165-bgfix-pk")):
                raise SystemExit(f"{r['Mob_ID']} stands {d:.0f} from {o['Mob_ID']}")
    return rows


def build_merchant_items(con):
    """The merchant keep lists: siege parts (the realm's 1.64 Siegecraft ingredients except wood) + its catapult stones and
    ballista bolts, and the wood merchant's boards. Existing ItemTemplates only."""
    items = []
    tags = {1: "Alb", 2: "Mid", 3: "Hib"}
    for realm, tag in tags.items():
        parts = [r[0] for r in con.execute(
            "SELECT DISTINCT x.IngredientId_nb FROM CraftedItem c JOIN ItemTemplate i ON i.Id_nb = c.Id_nb "
            "JOIN CraftedXItem x ON x.CraftedItemId_nb = c.Id_nb JOIN ItemTemplate ii ON lower(ii.Id_nb) = lower(x.IngredientId_nb) "
            "WHERE c.CraftingSkillType = 3 AND i.Realm = ? AND ii.Name NOT LIKE '%wooden boards' "
            "AND ii.Name NOT LIKE '%metal bars' ORDER BY ii.Name", (realm,))]
        assert len(parts) == 12, (tag, parts)
        for base, ids in (("true165MkSiege", parts + MK_AMMO[realm]), ("true165MkWood", MK_WOOD)):
            for slot, iid in enumerate(ids):
                assert con.execute("SELECT count(*) FROM ItemTemplate WHERE Id_nb=?", (iid,)).fetchone()[0] == 1, iid
                items.append({"ItemListID": base + tag, "ItemTemplateID": iid, "PageNumber": 0, "SlotPosition": slot,
                              "LastTimeRowUpdated": STAMP, "MerchantItem_ID": "true165-bgfix-%s%s-%02d" % (base, tag, slot)})
    return items


def build_updates(con):
    ups = []
    add = lambda sql, why: ups.append({"sql": sql, "reason": TAG + why})
    old, new = CALEDON_BASELEVEL
    add(f"UPDATE Keep SET BaseLevel = {new} WHERE KeepID = {CALEDON_KEEP} AND Region = 250 AND BaseLevel = {old}",
        f"Caer Caledon's keep base level {old} -> {new}: its guards come out at level 35 and its lord at 42 (were 48 / 57). "
        "Period: the Prima Atlas lists the Caledonia keep's guards as Stoddard Archer [35], Baron Protector [35] and "
        "Stoddard Warder [40] (\"Stoddard keep\" = the Caledonia keep, Freddyshouse 94033, 2002-08-01); to a level 34 its "
        "archers and inner guards were orange (35-37) and the lord purple (41+) (FH 93539, 2002-07-31). The doors follow "
        "the base level (x200): 9,200 -> 6,600 health")
    casters = rows_of(con, "SELECT Mob_ID, ClassType FROM Mob WHERE Mob_ID IN (%s)" % sql_list(CALEDON_CASTERS))
    assert len(casters) == 5 and all(c["ClassType"] == "DOL.GS.Keeps.GuardStaticCaster" for c in casters), casters
    add("UPDATE Mob SET ClassType = 'DOL.GS.Keeps.GuardStaticArcher', Brain = 'DOL.AI.Brain.KeepGuardBrain', "
        "Name = 'Renegade Hunter', Realm = 0, AggroLevel = 99, AggroRange = 2100, VisibleWeaponSlots = 51, Model = 781, "
        "Gender = 2 WHERE Region = 250 AND ClassType = 'DOL.GS.Keeps.GuardStaticCaster' AND Mob_ID IN (%s)" % sql_list(CALEDON_CASTERS),
        "Caer Caledon's five wall-walk casters are archers (values as the keep's own archer caledon-guard-21): a level 34 "
        "scout counted \"roughly 7 archers on the first floor/walls\" and no casters (FH 93539, 2002-07-31), and the Prima "
        "Atlas battleground lists name no keep casters (only the portal keeps' Masters)")
    # row levels (only live with the BG row-level code change; the server otherwise derives them from Keep.BaseLevel)
    # base-world rows only: add-file rows (true165-*) carry their levels in their own files (build_bgkeeps165.py)
    keep_classes = ("ClassType LIKE 'DOL.GS.Keeps.Guard%' AND ClassType <> 'DOL.GS.Keeps.GuardMerchant' "
                    "AND Mob_ID NOT LIKE 'true165-%'")
    for region, by_class in ROW_LEVELS.items():
        guards = rows_of(con, f"SELECT Mob_ID, ClassType FROM Mob WHERE Region = ? AND {keep_classes} AND "
                              f"sqrt((X-{KEEP_CENTRE[0]})*(X-{KEEP_CENTRE[0]})+(Y-{KEEP_CENTRE[1]})*(Y-{KEEP_CENTRE[1]})) < 3000 "
                              "ORDER BY Mob_ID", (region,))
        levels = {}
        for g in guards:
            cls = g["ClassType"].split(".")[-1]
            if g["Mob_ID"] in CALEDON_CASTERS:
                cls = "GuardStaticArcher"
            lvl = CALEDON_OUTSIDE_LEVEL if g["Mob_ID"] in CALEDON_OUTSIDE else by_class[cls]
            levels.setdefault(lvl, []).append(g["Mob_ID"])
        for lvl in sorted(levels):
            ids = levels[lvl]
            add(f"UPDATE Mob SET Level = {lvl} WHERE Region = {region} AND Mob_ID IN ({sql_list(ids)})",
                f"{BG_NAMES[region].capitalize()} keep guard row level {lvl} ({len(ids)} rows): "
                + {250: "Prima Atlas Caledonia keep guards (Stoddard Archer / Baron Protector 35, Stoddard Warder 40 for the "
                        "three outside the front gate, FH 93539) and the lord at 42 (purple to a 34, = the server's value at base level 33)",
                   251: "Prima Atlas Murdaigean: guards and archers [30], commanders [33], lords [40]",
                   252: "Prima Atlas Thidranki: guards and archers [24], lords [33]",
                   253: "no period source for Abermenai: the rows carry today's computed values (guards 23, lord 28)"}[region]
                + ". The server takes keep guard levels from Keep.BaseLevel; the row level counts only with the "
                  "battleground row-level code change")
    return ups


def write_json(name, data):
    out = (json.dumps(data, indent=1, ensure_ascii=False) + "\n").replace("\n", "\r\n").encode("utf-8")
    for f in FOLDERS:
        open(os.path.join(f, name), "wb").write(out)


def main():
    con = open_clean()
    nav = Nav()
    templates = build_templates(con)
    mobs = build_mobs(con, nav, templates)
    updates = build_updates(con)
    con.close()
    keys = [r["Mob_ID"] for r in mobs]
    assert len(keys) == len(set(keys))
    write_json(TMPLFILE, {"table": "NpcTemplate", "key": "NpcTemplate_ID",
                          "reason": TAG + "templates of the battleground portal keep guards (copies of the frontier portal keep "
                                          "guard templates with the Prima Atlas battleground levels: realm guards 90, Masters 95; "
                                          "build_bgfix165.py)",
                          "rows": templates})
    write_json(MOBFILE, {"table": "Mob", "key": "Mob_ID",
                         "reason": TAG + "battleground content: Abermenai's three portal keep Siege Masters (1.53D), the period "
                                         "portal keep guards of all four battlegrounds (Prima Atlas maps and levels), Caer "
                                         "Caledon's roof archers and bridge-side guards (FH 93539), Thidranki's commanders, "
                                         "the Atlas' named portal keep NPCs, and the merchant keep merchants (FH 89097, CapnBry "
                                         "radar); build_bgfix165.py",
                         "rows": mobs})
    con = open_clean()
    merch = build_merchant_items(con)
    con.close()
    write_json(MERCHFILE, {"table": "MerchantItem", "key": "MerchantItem_ID",
                           "reason": TAG + "merchant keep lists: the realm's siegecraft parts with its catapult stones and ballista "
                                           "bolts (true165MkSiegeAlb/Mid/Hib) and the wood merchant's boards (true165MkWoodAlb/Mid/Hib); "
                                           "period: FH 89097 (2002-05-29), FH 86254, FH 100226; build_bgfix165.py",
                           "rows": merch})
    new_where = [
        {"table": "Mob", "key": "Mob_ID",
         "where": "ClassType = 'DOL.GS.Keeps.GuardMerchant' AND Mob_ID NOT LIKE 'true165-bgfix-mk-%'",
         "reason": TAG + "freeshard keep merchant (moved here from remove.mob_classes \"DOL.GS.Keeps.GuardMerchant\": same effect, "
                         "except the period merchant keep merchants of build_bgfix165.py; FH 89097 2002-05-29 \"In these keeps "
                         "there are merchants who sell siege parts, there are also woodmerchants there\")"},
        {"table": "MerchantItem", "key": "MerchantItem_ID",
         "where": "ItemListID IN ('SiegeMerchantAlb','SiegeMerchantMid','SiegeMerchantHib') AND ItemTemplateID = 'deploy_heavy_siege_ram'",
         "reason": TAG + "the battleground Siege Masters sold no heavy siege ram: the period Thidranki Siegemasters' goods were "
                         "\"Field Ballista Token, Field Catapult Token, Field Trebuchet Token, Small Ram Token\" (Allakhazam "
                         "bestiary); the Atlas heavy siege ram (100g) leaves the three SiegeMerchant lists, which only the "
                         "battleground Siege Masters use"},
    ]

    def apply(rules):
        rules["updates"] = [u for u in rules["updates"] if not str(u.get("reason", "")).startswith(TAG)] + updates
        rules["remove"]["where"] = [e for e in rules["remove"]["where"] if not str(e.get("reason", "")).startswith(TAG)] + new_where
        rules["remove"]["mob_classes"].pop("DOL.GS.Keeps.GuardMerchant", None)   # replaced by the first where rule above
    size = locks.edit_rules(LANE, apply)
    for name in (MOBFILE, TMPLFILE, MERCHFILE):
        assert open(os.path.join(FOLDERS[0], name), "rb").read() == open(os.path.join(FOLDERS[1], name), "rb").read()
    from collections import Counter
    print(f"{TMPLFILE}: {len(templates)} templates")
    print(f"{MOBFILE}: {len(mobs)} rows " + str(Counter((r['Region'], r['Name']) for r in mobs if r['ClassType'] == 'DOL.GS.GameGuard').most_common(3)) + " ...")
    print(f"true165-rules.json: {len(updates)} bgfix updates ({size} bytes)")


if __name__ == "__main__":
    main()
