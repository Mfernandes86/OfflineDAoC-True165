"""OFFLINEDAOC Database - monsters and camps (lane M-D).

    python -I source/build_mobs.py [--db <world.sqlite3.db>] [--out <data dir>]

Reads the True 1.65 world (read-only, through wgcommon.open_db) and the server's C# source (read-only, as text) and
writes  data/mobs.js  and  data/camps.js.  The exact fields are in  source/contract/mobs.md.

Everything a page shows must be what the running server does, so each rule below names the C# it was read from
(paths relative to wgcommon.SERVER_SRC + "/GameServer"). Constants that the server could change are re-read from
the source at build time and asserted, so a changed server makes this builder fail loudly instead of lying.

How a Mob row becomes a live monster (what this builder reproduces)
-------------------------------------------------------------------
* Class      world/Region.cs:735-772     NPC guild script (trainers, crafters, ...) by Mob.Guild + realm, else the
                                         template's ClassType when the template has ReplaceMobValues, else
                                         Mob.ClassType, else DOL.GS.GameNPC.
* Row values gameobjects/GameNPC.cs:1055-1181  name, level, realm, flags, faction, aggro, race, body type, speed,
                                         respawn interval, path, roaming range come from the Mob row ...
* Template   gameobjects/GameNPC.cs:1309-1472  ... then, when the row's NpcTemplate has ReplaceMobValues = 1, the
                                         template overwrites name, guild, faction, race, body type, speed, flags,
                                         aggro level and aggro range, and the LEVEL is drawn at random from the
                                         template's Level list ("10-18", "9;10;11"; gameutils/Util.cs:86-116), again
                                         on every respawn (GameNPC.cs:3270). Several templates may share one id:
                                         the server takes one at random (gameutils/NpcTemplateMgr.cs:185-201).
* Aggro -1   gameobjects/GameNPC.cs:1140-1168  "auto" values of rows without such a template.
* Attack on sight  ai/brain/StandardMob/StandardMobBrain.cs:77-91, 675-708 and gameutils/Faction.cs:176-195
                                         aggro level > 0 and aggro range > 0, and for a monster with a faction the
                                         player's standing must be "aggressive" (faction aggro above 75; a new
                                         character starts at the faction's BaseAggroLevel). Never against a player
                                         to whom the monster is grey. The range is capped at 3,600.
* Bring a friend   ai/brain/StandardMob/StandardMobBrain.cs:787-924, BafFriends.cs, bots/CompanionBringAFriend.cs
* Respawn    gameobjects/GameNPC.cs:3165-3199
* Experience gameobjects/GameLiving.cs:531-637 (table), serverrules/AbstractServerRules.cs:597-604, 1380-1418
* Camps      the bots' own camp catalog cuts live spawns of one name per zone in 4,200-unit cells and merges
             same-name camps within 2,500 units (bots/autonomous/AutonomousWorldBotController.cs:24, 4238-4400,
             AutonomousCampDedupe.cs). A grid cuts a camp in two wherever a cell edge runs through it, so this
             builder links spawns of one kind that stand within one pull of each other instead (CAMP_LINK below)
             and then splits anything wider than the bots' merge radius (2,500 from its centre).
"""
import collections, json, math, os, re, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import wgcommon  # noqa: E402

SRC = wgcommon.SERVER_SRC + "/GameServer"
DEFAULT_CLASS = "DOL.GS.GameNPC"
REALM_NAME = {0: "None", 1: "Albion", 2: "Midgard", 3: "Hibernia"}

# ---------------------------------------------------------------------------------------------------------------
# Numbers of this tool (not the server's). Each is explained where it is used and repeated in mobs.md.
CAMP_LINK = 1500         # two spawns of one kind this close (3D) belong to one camp (single link). In this world
#                          half of all spawns have their nearest same-kind neighbour more than 500 units away and a
#                          quarter more than 965, so a tight link (the bots' 700-unit CampSkipRadius) leaves 73% of
#                          the "camps" as single spawns. 1,500 is one standard spell or bow pull: what you can reach
#                          from one spot is one camp.
CAMP_MAX_SPAN = 2500     # a linked group wider than this from its centre is split (bots' AutonomousCampDedupe.MergeRadius)
NEAR_LIST = 1500         # neighbours are listed when they stand within this (3D) of any spawn of the camp: the same
#                          pull distance; nearer than this a monster can be beside you while you fight the camp
PULL_MARGIN = 200        # bots' AutonomousHuntingGround.PullPathMargin: extra reach around an aggro range
SOCIAL_MIN = 300         # bots' AutonomousCampKnowledge.MinimumSocialRange
THREAT_LEVELS = 10       # bots' AutonomousCampThreatPolicy.LevelsAboveCamp


def read(rel):
    with open(SRC + "/" + rel, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def need(pattern, text, what):
    m = re.search(pattern, text, re.S)
    assert m, "server source changed: cannot find " + what
    return m


# ---------------------------------------------------------------------------------------------------------------
# Server source: class tree, guild scripts, constants
def load_source():
    S = {}
    parents = collections.defaultdict(set)
    guild_scripts = {}
    no_xp = set()
    script_levels = {}
    cls_re = re.compile(r'^[ \t]*(?:\[[^\]\n]*\][ \t]*)*(?:public|internal|private|protected)?[ \t]*'
                        r'(?:static[ \t]+|abstract[ \t]+|sealed[ \t]+|partial[ \t]+)*class[ \t]+(\w+)(?:<[^>\n]*>)?'
                        r'[ \t]*(?::[ \t]*([^\{\n]+))?', re.M)
    gs_re = re.compile(r'\[NPCGuildScript\("([^"]+)"(?:\s*,\s*eRealm\.(\w+))?\)\]')
    files = 0
    for dp, _dn, fn in os.walk(SRC):
        for f in fn:
            if not f.endswith(".cs"):
                continue
            files += 1
            with open(os.path.join(dp, f), encoding="utf-8-sig", errors="replace") as fh:
                s = fh.read()
            decls = [(m.start(), m.group(1)) for m in cls_re.finditer(s)]
            for m in cls_re.finditer(s):
                for b in (m.group(2) or "").split(","):
                    b = b.strip().split("<")[0].strip().split(".")[-1].split(" ")[0]
                    if b:
                        parents[m.group(1)].add(b)
            if "NPCGuildScript(" in s:
                for m in gs_re.finditer(s):
                    c = re.search(r'class\s+(\w+)', s[m.end():])
                    if c:
                        guild_scripts[(m.group(1), m.group(2) or "None")] = c.group(1)
            if "/scripts/" in dp.replace("\\", "/") + "/" and "AddToWorld" in s:
                # "Level = N;" or "Level = (byte)Util.Random(a, b);" inside a class's own AddToWorld()
                for i, (pos, cname) in enumerate(decls):
                    end = decls[i + 1][0] if i + 1 < len(decls) else len(s)
                    m = re.search(r'override\s+bool\s+AddToWorld\s*\(\s*\)\s*\{', s[pos:end])
                    if not m:
                        continue
                    a0 = pos + m.end()
                    depth, j = 1, a0
                    while j < end and depth:
                        depth += (s[j] == "{") - (s[j] == "}")
                        j += 1
                    body = s[a0:j]
                    lit = re.findall(r'(?<![\w.])Level\s*=\s*(?:\(byte\)\s*)?(\d+)\s*;', body)
                    rnd = re.findall(r'(?<![\w.])Level\s*=\s*\(byte\)\s*Util\.Random\(\s*(\d+)\s*,\s*(\d+)\s*\)\s*;', body)
                    if len(lit) + len(rnd) == 1:
                        script_levels[cname] = (int(lit[0]), int(lit[0])) if lit else (int(rnd[0][0]), int(rnd[0][1]))
            if "ExperienceValue" in s:
                # classes that are worth no experience: "public override long ExperienceValue => 0;"
                for m in re.finditer(r'override\s+long\s+ExperienceValue\s*=>\s*0\s*;', s):
                    owner = [n for p, n in decls if p < m.start()]
                    if owner:
                        no_xp.add(owner[-1])
    assert files > 2000 and len(parents) > 3000, "server source not found at " + SRC
    assert "GameNPC" in parents["GameMerchant"] and "GameKeepGuard" in parents["GuardFighter"]
    S["parents"] = parents
    S["guild_scripts"] = guild_scripts
    S["no_xp_classes"] = no_xp
    S["script_levels"] = script_levels
    assert len(guild_scripts) > 60, "NPC guild scripts not found"

    # experience table  gameobjects/GameLiving.cs:531-637
    gl = read("gameobjects/GameLiving.cs")
    body = need(r'XPForLiving\s*=\s*\{(.*?)\};', gl, "GameLiving.XPForLiving").group(1)
    xp = [int(x) for x in re.findall(r'^\s*(\d+)\s*,?\s*(?://.*)?$', body, re.M)]
    assert len(xp) == 101 and xp[0] == 5 and xp[10] == 5120 and xp[50] == 41943040, "XPForLiving changed"
    S["xp"] = xp

    npc = read("gameobjects/GameNPC.cs")
    # auto aggro values  GameNPC.cs:1140-1168
    need(r'AggroRange == Constants\.USE_AUTOVALUES.*?AggroRange = 400;.*?IsDungeon\)\s*aggroBrain\.AggroRange = 300;'
         r'.*?else\s*aggroBrain\.AggroRange = 500;', npc, "auto aggro range")
    need(r'AggroLevel == Constants\.USE_AUTOVALUES.*?AggroLevel = 0;.*?Level > 5\)\s*aggroBrain\.AggroLevel = 30;'
         r'.*?Realm != eRealm\.None\)\s*aggroBrain\.AggroLevel = 60;', npc, "auto aggro level")
    need(r'template\.ReplaceMobValues &&\s*_templateLevels != null', npc, "template level rule")
    need(r'LoadTemplate\(NPCTemplate\);.*?AddToWorld\(\);\s*return 0;', npc, "respawn reload then AddToWorld")
    need(r'if \(Faction != null && aggroBrain != null && aggroBrain\.AggroLevel > 0 && aggroBrain\.AggroRange > 0\)', npc,
         "examine message: faction branch")
    need(r'else if \(aggroBrain != null && aggroBrain\.AggroLevel > 0\)', npc, "examine message: no-faction branch")
    need(r'if \(!template\.ReplaceMobValues && !LoadedFromScript\)\s*return;', npc, "template replace rule")
    need(r'Util\.Random\(ServerProperties\.Properties\.NPC_MIN_RESPAWN_INTERVAL, ServerProperties\.Properties\.'
         r'NPC_MIN_RESPAWN_INTERVAL \+ 5\).*?Name != Name\.ToLower\(\).*?minutes \+= 5;.*?Level <= 65 && Realm == 0',
         npc, "respawn rule")
    brain = read("ai/brain/StandardMob/StandardMobBrain.cs")
    S["max_aggro"] = int(need(r'MAX_AGGRO_DISTANCE = (\d+);', brain, "MAX_AGGRO_DISTANCE").group(1))
    need(r'if \(AggroLevel > 0 && AggroRange > 0 &&', brain, "proximity aggro rule")
    need(r'GetStandingToFaction\(realTargetPlayer\) is Faction\.Standing\.AGGRESIVE', brain, "faction aggro rule")
    fac = read("gameutils/Faction.cs")
    S["aggressive_above"] = int(need(r'if \(aggro > (\d+)\)\s*return Standing\.AGGRESIVE;', fac, "standing").group(1))
    S["hostile_above"] = int(need(r'else if \(aggro > (\d+)\)\s*return Standing\.HOSTILE;', fac, "standing").group(1))
    S["neutral_above"] = int(need(r'else if \(aggro > (\d+)\)\s*return Standing\.NEUTRAL;', fac, "standing").group(1))
    need(r'if \(Util\.Chance\(20\)\)', fac, "standing change chance")
    baf = read("ai/brain/StandardMob/BafFriends.cs")
    need(r'CloseRadius\(int aggroRange\) => Math\.Max\(0, aggroRange\) \* 2 / 5;', baf, "BaF close radius")
    need(r'Math\.Min\(DungeonReinforcementRadius, 250 \+ 150 \* \(Math\.Max\(1, attackers\) - 1\)\)', baf, "BaF dungeon")
    S["baf_dungeon_max"] = int(need(r'DungeonReinforcementRadius = ([\d_]+);', baf, "BaF dungeon cap").group(1).replace("_", ""))
    cbaf = read("bots/CompanionBringAFriend.cs")
    need(r'initialChance \+ \(System\.Math\.Max\(1, countedAttackers\) - 1\) \* additionalChance', cbaf, "BaF chance")
    # dungeon regions  world/Zone.cs:43-118
    zone = read("world/Zone.cs")
    dz = need(r'public bool IsDungeon\s*\{\s*get\s*\{\s*switch \(ZoneRegion\.ID\)\s*\{(.*?)return true;', zone, "Zone.IsDungeon")
    S["dungeon_regions"] = {int(x) for x in re.findall(r'^\s*case (\d+):', dz.group(1), re.M)}
    assert 249 in S["dungeon_regions"] and 20 in S["dungeon_regions"] and len(S["dungeon_regions"]) > 50
    # the bots' camp numbers, for the record
    ctl = read("bots/autonomous/AutonomousWorldBotController.cs")
    S["bot_cell"] = int(need(r'private const int CampCellSize = (\d+);', ctl, "CampCellSize").group(1))
    S["bot_merge"] = int(need(r'MergeRadius = ([\d_]+);', read("bots/autonomous/AutonomousCampDedupe.cs"), "MergeRadius").group(1).replace("_", ""))
    S["bot_skip"] = int(float(need(r'CampSkipRadius = (\d+)f;', read("bots/autonomous/AutonomousRouteThreatPolicy.cs"), "CampSkipRadius").group(1)))
    hg = read("bots/autonomous/AutonomousHuntingGround.cs")
    assert int(need(r'PullPathMargin = (\d+);', hg, "PullPathMargin").group(1)) == PULL_MARGIN
    assert int(need(r'MinimumSocialRange = (\d+);', hg, "MinimumSocialRange").group(1)) == SOCIAL_MIN
    assert S["bot_skip"] == 700 and S["bot_merge"] == CAMP_MAX_SPAN
    dd = read("bots/autonomous/AutonomousCampDedupe.cs")
    assert int(need(r'LevelsAboveCamp = (\d+);', dd, "LevelsAboveCamp").group(1)) == THREAT_LEVELS
    town = read("bots/autonomous/AutonomousNeutralTownPolicy.cs")
    S["town_radius"] = int(need(r'TownRadius = (\d+);', town, "TownRadius").group(1))
    S["town_factions"] = {int(x) for x in re.findall(r'^\s*(\d+),\s*//', need(r'TownFactionIds = new HashSet<int>\s*\{(.*?)\};', town, "TownFactionIds").group(1), re.M)}
    assert S["town_factions"] == {16, 89, 172}, S["town_factions"]
    return S


def ancestors(parents, short, _cache={}):
    if short in _cache:
        return _cache[short]
    seen, stack = set(), [short]
    while stack:
        for b in parents.get(stack.pop(), ()):
            if b not in seen:
                seen.add(b)
                stack.append(b)
    _cache[short] = seen
    return seen


def split_levels(text):
    """Util.SplitCSV(text, true) then byte.TryParse of each entry (gameutils/Util.cs:86-116, GameNPC.cs:1377).
    Returns the list WITH repeats: the server picks one entry at random, so a repeat doubles that level's chance."""
    out = []
    for part in (text or "").split(";"):
        if part == "":
            continue
        if "-" in part:
            a = part.split("-")
            try:
                lo, hi = int(a[0]), int(a[1])
            except ValueError:
                continue                       # the server drops a range it cannot parse
            if lo > hi:
                lo, hi = hi, lo
            out.extend(range(lo, hi + 1))
        else:
            out.append(part)
    return out


def level_dist(entries, row_level):
    """Chance of each level for one spawn, as the server draws it (GameNPC.cs:1372-1389): one entry of the list at
    random; an entry that is not a byte (0..255) leaves the level as it was (the row's level on first load)."""
    if not entries:
        return {row_level: 1.0}
    d = collections.Counter()
    for e in entries:
        try:
            v = int(e)
            ok = 0 <= v <= 255
        except (TypeError, ValueError):
            ok = False
        d[v if ok else row_level] += 1
    n = float(len(entries))
    return {k: v / n for k, v in d.items()}


# ---------------------------------------------------------------------------------------------------------------
# World: rows -> live spawns
SERVICE_BASES = ("GameMerchant", "GameTrainer", "GameHealer", "GameTeleporter", "CraftNPC", "GameTaxi",
                 "GameHastener", "GameVaultKeeper")
# flags  gameobjects/GameNPC.cs:595-633
F_GHOST, F_STEALTH, F_NONAME, F_CANTTARGET, F_PEACE, F_FLYING, F_TORCH, F_STATUE, F_SWIMMING = 1, 2, 4, 8, 16, 32, 64, 128, 256


def csv_ids(text):
    """Util.SplitCSV(text, true) as integers (server property lists such as "27;165;233-242;244")."""
    out = set()
    for e in split_levels(text):
        try:
            out.add(int(e))
        except (TypeError, ValueError):
            pass
    return out


def is_a(short, anc, base):
    return short == base or base in anc


def load_world(con, S):
    W = {}
    props = {r["Key"].lower(): r["Value"] for r in con.execute("select Key, Value from ServerProperty")}
    W["props"] = props
    assert props.get("classic165_mode", "").lower() == "true", "this world is not a True 1.65 world"
    assert props.get("use_npcguildscripts", "").lower() == "true"
    assert props.get("gamenpc_default_classtype") == DEFAULT_CLASS
    disabled_regions = csv_ids(props.get("disabled_regions", ""))
    # world/Region.cs:255-264: the Regions table counts expansions from 0, the property from 1
    disabled_exp = {e - 1 for e in csv_ids(props.get("disabled_expansions", ""))}
    regions = {r["RegionID"]: dict(r) for r in con.execute("select RegionID, Name, Description, Expansion, IsFrontier from Regions")}
    zs = wgcommon.zones(con)
    zbr = wgcommon.zones_by_region(zs)
    for z in zs.values():
        z["n_in_region"] = len(zbr[z["region"]])
        # world/Zone.cs:43 (by region id) - used for the Bring-a-Friend radius; world/Region.cs:374 (one zone at
        # offset 8192, 8192) - used for the auto aggro range
        z["dungeon"] = z["region"] in S["dungeon_regions"]
        z["region_dungeon"] = z["n_in_region"] == 1 and z["ox"] == 8192 and z["oy"] == 8192
    W["zones"], W["zbr"], W["regions"] = zs, zbr, regions
    W["disabled_regions"], W["disabled_exp"] = disabled_regions, disabled_exp

    factions = {r["ID"]: dict(id=r["ID"], name=r["Name"] or "", base=r["BaseAggroLevel"], friends=set(), enemies=set())
                for r in con.execute("select ID, Name, BaseAggroLevel from Faction")}
    for f in factions.values():
        f["friends"].add(f["id"])                                   # gameutils/Faction.cs:25-28
    bad_links = 0
    for r in con.execute("select FactionID, LinkedFactionID, IsFriend from LinkedFaction"):
        a, b = factions.get(r["LinkedFactionID"]), factions.get(r["FactionID"])   # gameutils/FactionMgr.cs:29-45
        if not a or not b:
            bad_links += 1
            continue
        (a["friends"] if r["IsFriend"] else a["enemies"]).add(b["id"])
    W["factions"], W["bad_links"] = factions, bad_links

    tpl = collections.defaultdict(list)
    for r in con.execute("select TemplateId, Name, ClassType, GuildName, FactionID, Level, MaxSpeed, Flags, AggroLevel, "
                         "AggroRange, Race, BodyType, MaxDistance, TetherRange, ReplaceMobValues, Spells, Styles, "
                         "Abilities from NpcTemplate order by NpcTemplate_ID"):
        tpl[r["TemplateId"]].append(dict(r))
    W["templates"] = tpl
    W["races"] = {r["ID"]: dict(r) for r in con.execute("select * from Race")}

    qpath = wgcommon.RUNTIME + "/server/classic-quests.json"
    quest_ids, hour_windows = set(), {}
    if os.path.exists(qpath):
        with open(qpath, encoding="utf-8-sig") as f:
            q = json.load(f)
        quest_ids = {x.lower() for x in q.get("QuestMonsterIds", [])}
        hour_windows = {k.lower(): (v["Start"], v["End"]) for k, v in (q.get("HourWindows") or {}).items()}
    # scripts/mobs/TimeDependentSpawnNpc.cs:66-72
    tds = read("scripts/mobs/TimeDependentSpawnNpc.cs")
    for m in re.finditer(r'\["([^"]+)"\]\s*=\s*\((\d+),\s*(\d+)\)', tds):
        hour_windows.setdefault(m.group(1).lower(), (int(m.group(2)), int(m.group(3))))
    W["quest_ids"], W["hour_windows"] = quest_ids, hour_windows

    parents, gscripts = S["parents"], S["guild_scripts"]
    rows = con.execute("select Mob_ID, ClassType, Name, Guild, X, Y, Z, Speed, Region, Level, Realm, NPCTemplateID, Race, "
                       "Flags, AggroLevel, AggroRange, RespawnInterval, FactionID, BodyType, Brain, PathID, RoamingRange, "
                       "Model, PackageID from Mob order by Mob_ID").fetchall()
    W["mob_rows"] = len(rows)
    spawns = []          # one per (row, template variant)
    cat_rows = collections.Counter()
    unknown_class = collections.Counter()
    for r in rows:
        tid = r["NPCTemplateID"]
        variants = tpl.get(tid) if tid not in (0, -1) else None       # NpcTemplateMgr.cs:187-188
        if variants:
            # variants that behave the same collapse into one
            seen, uniq = {}, []
            for t in variants:
                k = (t["ReplaceMobValues"],) + ((t["Name"], t["ClassType"], t["Level"], t["FactionID"], t["Flags"],
                                                  t["AggroLevel"], t["AggroRange"], t["Race"], t["BodyType"],
                                                  t["MaxSpeed"]) if t["ReplaceMobValues"] else ())
                if k in seen:
                    seen[k]["w"] += 1
                else:
                    seen[k] = dict(t=t, w=1)
                    uniq.append(seen[k])
            tot = float(len(variants))
            vs = [(u["t"], u["w"] / tot) for u in uniq]
        else:
            vs = [(None, 1.0)]
        z = wgcommon.zone_of(zbr, r["Region"], r["X"], r["Y"])
        for t, w in vs:
            rep = bool(t and t["ReplaceMobValues"])
            # ---- class  (world/Region.cs:735-772)
            cls = None
            guild = r["Guild"] or ""
            if guild:
                cls = gscripts.get((guild, REALM_NAME.get(r["Realm"], "?"))) or gscripts.get((guild, "None"))
            via_guild = cls is not None
            if cls is None:
                if rep and t["ClassType"] and t["ClassType"] != DEFAULT_CLASS:
                    cls = t["ClassType"]
                elif r["ClassType"] and r["ClassType"] != DEFAULT_CLASS:
                    cls = r["ClassType"]
                else:
                    cls = DEFAULT_CLASS
            short = cls.split(".")[-1]
            anc = ancestors(parents, short)
            if short != "GameNPC" and "GameNPC" not in anc:
                unknown_class[cls] += 1            # the server falls back to GameNPC (Region.cs:802-806)
                short, anc = "GameNPC", ancestors(parents, "GameNPC")
            # ---- values
            name = (t["Name"] if rep else r["Name"]) or ""
            flags = (t["Flags"] if rep else r["Flags"]) or 0
            faction = (t["FactionID"] if rep else r["FactionID"]) or 0
            if faction not in factions:
                faction = 0                        # FactionMgr.GetFactionByID -> null
            race = t["Race"] if rep else r["Race"]
            body = t["BodyType"] if rep else r["BodyType"]
            speed = t["MaxSpeed"] if rep else r["Speed"]
            entries = split_levels(t["Level"]) if rep else []
            dist = level_dist(entries, r["Level"])
            # A script class that sets Level in its own AddToWorld overrides both: AddToWorld runs after the row is
            # loaded and again after every respawn's template reload (GameNPC.cs:3270-3284).
            script_level = S["script_levels"].get(short)
            if script_level:
                dist = level_dist(list(range(script_level[0], script_level[1] + 1)), r["Level"])
            lmin, lmax = min(dist), max(dist)
            realm = r["Realm"]
            if rep:
                al, ar = t["AggroLevel"], t["AggroRange"]      # GameNPC.cs:1453-1472
            else:
                al, ar = r["AggroLevel"], r["AggroRange"]      # GameNPC.cs:1135-1169
                if ar == -1:
                    ar = 500 if realm != 0 else (300 if (z and z["region_dungeon"]) else 400)
                if al == -1:
                    al = 60 if realm != 0 else (30 if r["Level"] > 5 else 0)
            ar = min(ar, S["max_aggro"])                       # StandardMobBrain.cs:251-255
            f = factions.get(faction)
            on_sight = al > 0 and ar > 0 and (f is None or f["base"] > S["aggressive_above"])
            # the word the game shows when you click it ("... is aggressive towards you"), GameNPC.cs:2346-2415:
            # with a faction AND aggro level > 0 AND aggro range > 0 the standing with the faction decides
            # (Faction.cs:176-186); otherwise "aggressive" when the aggro level is above 0, else "neutral".
            # (Same-realm NPCs read "friendly"; monsters here have no realm.)  0 aggressive 1 hostile 2 neutral 3 friendly
            if f is not None and al > 0 and ar > 0:
                stance = (0 if f["base"] > S["aggressive_above"] else 1 if f["base"] > S["hostile_above"]
                          else 2 if f["base"] > S["neutral_above"] else 3)
            else:
                stance = 0 if al > 0 else 2
            # ---- category
            if via_guild or any(is_a(short, anc, b) for b in SERVICE_BASES):
                cat = "service"
            elif is_a(short, anc, "GameKeepGuard"):
                cat = "keepguard"
            elif is_a(short, anc, "GameGuard"):
                cat = "guard"
            elif realm != 0:
                cat = "realm"
            elif is_a(short, anc, "GameTrainingDummy"):
                cat = "dummy"
            elif flags & F_PEACE:
                cat = "peace"
            elif flags & F_CANTTARGET:
                cat = "untargetable"
            elif name.strip() == "":
                cat = "noname"
            else:
                cat = "monster"
            place = "ok"
            reg = regions.get(r["Region"])
            if reg is None:
                place = "noregion"
            elif r["Region"] in disabled_regions:
                place = "disabledregion"
            elif reg["Expansion"] in disabled_exp:
                place = "disabledexpansion"
            elif z is None:
                place = "nozone"
            cat_rows[(cat, place)] += w
            spawns.append(dict(
                id=r["Mob_ID"], w=w, nvar=len(vs), cat=cat, place=place, name=name,
                # name and key keep the name exactly as the server holds it (no trim): loot is looked up by
                # mob.Name.ToLower() (LootGeneratorTemplate.cs:270, 287), and one name ends in a space
                key=name.lower(),
                cls=short, epic="IGameEpicNpc" in anc, boss=is_a(short, anc, "GameEpicBoss"),
                timed=("day" if is_a(short, anc, "DaySpawn") else "night" if is_a(short, anc, "NightSpawn")
                       else "hours" if is_a(short, anc, "HourWindowSpawn") else ""),
                region=r["Region"], zone=z["id"] if z else None, rx=r["X"], ry=r["Y"], z=r["Z"],
                x=(r["X"] - z["ox"]) if z else None, y=(r["Y"] - z["oy"]) if z else None,
                dist=dist, lmin=lmin, lmax=lmax, realm=realm, flags=flags, faction=faction, al=al, ar=ar,
                on_sight=on_sight, stance=stance, brain=(r["Brain"] or "").split(".")[-1],
                script_level=script_level, race=race or 0, body=body or 0, speed=speed or 0,
                respawn=0 if r["RespawnInterval"] == -1 else r["RespawnInterval"],   # GameNPC.cs:1106-1109
                path=(r["PathID"] or "").strip(), roam=max(0, r["RoamingRange"] or 0), tpl=tid if t else 0, rep=rep,
                quest=r["Mob_ID"].lower() in quest_ids, guild=(t["GuildName"] if rep else r["Guild"]) or "",
                caster=bool(t and (t["Spells"] or "").strip()), row_level=r["Level"], row_name=r["Name"],
                package=r["PackageID"] or "", stablemaster=is_a(short, anc, "GameStableMaster")))
    assert abs(sum(cat_rows.values()) - len(rows)) < 1e-6
    W["spawns"], W["cat_rows"], W["unknown_class"] = spawns, cat_rows, unknown_class
    return W


# ---------------------------------------------------------------------------------------------------------------
# Camps
def single_link(pts, d):
    """Groups of points where each is within d (3D) of another of its group. pts = [(x, y, z), ...]."""
    n = len(pts)
    parent = list(range(n))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    cell = collections.defaultdict(list)
    for i, p in enumerate(pts):
        cell[(p[0] // d, p[1] // d)].append(i)
    d2 = d * d
    for i, p in enumerate(pts):
        cx, cy = p[0] // d, p[1] // d
        for ax in (cx - 1, cx, cx + 1):
            for ay in (cy - 1, cy, cy + 1):
                for j in cell.get((ax, ay), ()):
                    if j > i:
                        q = pts[j]
                        if (p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 + (p[2] - q[2]) ** 2 <= d2:
                            a, b = find(i), find(j)
                            if a != b:
                                parent[a] = b
    out = collections.defaultdict(list)
    for i in range(n):
        out[find(i)].append(i)
    return list(out.values())


def centre_radius(pts, idx):
    cx = sum(pts[i][0] for i in idx) / len(idx)
    cy = sum(pts[i][1] for i in idx) / len(idx)
    r = max(math.hypot(pts[i][0] - cx, pts[i][1] - cy) for i in idx)
    return cx, cy, r


def split_wide(pts, idx, max_r):
    """Cut a group wider than max_r from its centre in two along its long axis (2-means, deterministic), repeat."""
    cx, cy, r = centre_radius(pts, idx)
    if r <= max_r or len(idx) < 2:
        return [idx]
    # the two points farthest apart (approximately): farthest from the centre, then farthest from that one
    a = max(idx, key=lambda i: ((pts[i][0] - cx) ** 2 + (pts[i][1] - cy) ** 2, i))
    b = max(idx, key=lambda i: ((pts[i][0] - pts[a][0]) ** 2 + (pts[i][1] - pts[a][1]) ** 2, i))
    ca, cb = (pts[a][0], pts[a][1]), (pts[b][0], pts[b][1])
    for _ in range(12):
        ga = [i for i in idx if (pts[i][0] - ca[0]) ** 2 + (pts[i][1] - ca[1]) ** 2 <= (pts[i][0] - cb[0]) ** 2 + (pts[i][1] - cb[1]) ** 2]
        gb = [i for i in idx if i not in set(ga)]
        if not ga or not gb:
            break
        na = (sum(pts[i][0] for i in ga) / len(ga), sum(pts[i][1] for i in ga) / len(ga))
        nb = (sum(pts[i][0] for i in gb) / len(gb), sum(pts[i][1] for i in gb) / len(gb))
        if na == ca and nb == cb:
            break
        ca, cb = na, nb
    if not ga or not gb:
        return [idx]
    return split_wide(pts, ga, max_r) + split_wide(pts, gb, max_r)


def hist(dists_weights):
    """Sum of level chances: {level: expected number of spawns at that level}."""
    h = collections.Counter()
    for dist, w in dists_weights:
        for lv, p in dist.items():
            h[lv] += p * w
    return h


def hist_list(h):
    return [[lv, round(c, 2)] for lv, c in sorted(h.items()) if round(c, 2) > 0]


def mode(values):
    c = collections.Counter(values)
    best = max(c.values())
    return sorted(k for k, v in c.items() if v == best)[0]


def respawn_range(s, min_minutes):
    """Seconds [lo, hi] until this spawn is back (GameNPC.cs:3165-3199). A row value wins; 0 = the server's rule."""
    if s["respawn"] != 0:
        return (s["respawn"], s["respawn"]) if s["respawn"] > 0 else (-1, -1)     # negative: never comes back
    lo, hi = min_minutes, min_minutes + 5
    if s["name"] != s["name"].lower():
        lo, hi = lo + 5, hi + 5
    if s["lmax"] > 65:        # (the realm-NPC branch never applies: monsters here are realm 0)
        a_lo, a_hi = max(0, s["lmin"] - 65) + min_minutes, (s["lmax"] - 65) + min_minutes
        if s["lmin"] > 65:
            lo, hi = lo + a_lo, hi + a_hi
        else:
            hi = hi + a_hi
    return lo * 60, hi * 60


STANCE = ("aggressive", "hostile", "neutral", "friendly")


def stances(spawns):
    """[aggressive, hostile, neutral, friendly] spawn points, in the game's own words."""
    c = [0, 0, 0, 0]
    for s in spawns:
        c[s["stance"]] += 1
    return c


OCT = ("N", "NE", "E", "SE", "S", "SW", "W", "NW")


def octant(dx, dy):
    """Compass direction of (dx, dy) in game units: x grows east, y grows SOUTH (north is up on the zone maps)."""
    ang = math.degrees(math.atan2(dx, -dy)) % 360.0
    return int((ang + 22.5) // 45) % 8


# kind flag bits (mobs.js "flagBits")
KF = dict(named=1, unique=2, epic=4, boss=8, quest=16, flying=32, swimming=64, ghost=128, stealth=256, caster=512,
          night=1024, day=2048, hours=4096, patrol=8192, scripted=16384, town=32768, shared=65536, noxp=131072,
          wide=262144)
PLAIN_CLASSES = {"GameNPC", "GameEpicNPC", "GameEpicBoss", "GameEpicDungeonNPC", "NightSpawn", "DaySpawn", "HourWindowSpawn"}
BODY = {0: "", 1: "Animal", 2: "Demon", 3: "Dragon", 4: "Elemental", 5: "Giant", 6: "Humanoid", 7: "Insect",
        8: "Magical", 9: "Reptile", 10: "Plant", 11: "Undead"}          # gameutils/NpcTemplateMgr.cs:16-30


def build(con, S, W, log=print):
    import numpy as np
    zs, factions = W["zones"], W["factions"]
    props = W["props"]
    min_resp = int(props.get("npc_min_respawn_interval", "5"))
    baf_init, baf_add = int(props.get("baf_initial_chance", "0")), int(props.get("baf_additional_chance", "50"))
    assert props.get("baf_same_name_without_faction", "True").lower() == "true"
    assert props.get("allow_roam", "True").lower() == "true"

    # ---- town residents (bots/autonomous/AutonomousNeutralTownPolicy.cs): members of the three Shrouded Isles
    # neutral factions within 2,500 of one of that faction's no-realm stable masters
    anchors = [(s["region"], s["faction"], s["rx"], s["ry"]) for s in W["spawns"]
               if s["stablemaster"] and s["realm"] == 0 and s["faction"] in S["town_factions"]]
    tr2 = S["town_radius"] ** 2
    for s in W["spawns"]:
        s["town"] = s["faction"] in S["town_factions"] and any(
            a[0] == s["region"] and a[1] == s["faction"] and (a[2] - s["rx"]) ** 2 + (a[3] - s["ry"]) ** 2 <= tr2 for a in anchors)

    M = [s for s in W["spawns"] if s["cat"] == "monster" and s["place"] == "ok"]
    for i, s in enumerate(M):
        s["i"] = i
    log("monster spawn points %d (rows %d)" % (len(M), round(sum(s["w"] for s in M))))

    # ---- kinds
    by_key = collections.defaultdict(list)
    for s in M:
        by_key[s["key"]].append(s)
    keys = sorted(by_key)
    kind_id = {k: i + 1 for i, k in enumerate(keys)}

    # ---- patrol paths (Mob.PathID -> Path, PathPoints)
    path_pts = collections.defaultdict(list)
    used_paths = {s["path"].lower() for s in M if s["path"]}
    path_region = {r["PathID"].lower(): r["RegionID"] for r in con.execute("select PathID, RegionID from Path")}
    for r in con.execute("select PathID, Step, X, Y, Z, WaitTime from PathPoints order by PathID, Step"):
        if r["PathID"].lower() in used_paths:
            path_pts[r["PathID"].lower()].append((r["X"], r["Y"], r["Z"]))
    paths_missing = 0
    for s in M:
        s["samples"] = None
        if s["path"]:
            pts = path_pts.get(s["path"].lower())
            if not pts or len(pts) < 2:
                paths_missing += 1
                s["path_ok"] = False
                continue
            s["path_ok"] = True
            s["samples"] = pts
            s["path_points"] = pts
    log("patrolling monster spawns %d, without usable path points %d" % (sum(1 for s in M if s["path"]), paths_missing))

    # ---- camps: per kind and zone, link at CAMP_LINK, cut what is wider than CAMP_MAX_SPAN
    camps = []
    groups = collections.defaultdict(list)
    for s in M:
        groups[(s["zone"], s["key"])].append(s)
    n_split = 0
    for (zone, key) in sorted(groups):
        g = sorted(groups[(zone, key)], key=lambda s: (s["rx"], s["ry"], s["z"], s["id"]))
        pts = [(s["rx"], s["ry"], s["z"]) for s in g]
        for idx in single_link(pts, CAMP_LINK):
            parts = split_wide(pts, sorted(idx), CAMP_MAX_SPAN)
            n_split += len(parts) > 1
            for part in parts:
                camps.append([g[i] for i in part])
    z0 = zs
    out_camps = []
    for members in camps:
        zone = z0[members[0]["zone"]]
        cx = sum(s["rx"] for s in members) / len(members)
        cy = sum(s["ry"] for s in members) / len(members)
        zsort = sorted(s["z"] for s in members)
        cz = zsort[len(zsort) // 2]                     # median height: a camp sits on one floor of a dungeon
        r = max(math.hypot(s["rx"] - cx, s["ry"] - cy) for s in members)
        out_camps.append(dict(members=members, zone=zone["id"], key=members[0]["key"], cx=cx, cy=cy, cz=cz, r=r))
    out_camps.sort(key=lambda c: (c["zone"], c["key"], round(c["cx"]), round(c["cy"])))
    for i, c in enumerate(out_camps):
        c["id"] = i + 1
        for s in c["members"]:
            s["camp"] = c["id"]
    camps = out_camps
    log("camps %d (single spawns %d, cut from wide groups %d)" % (len(camps), sum(1 for c in camps if len(c["members"]) == 1), n_split))

    # ---- neighbours: every pair of monster spawns of one region within NEAR_LIST (3D), plus the pairs where a
    # monster's own reach (aggro range + roaming range + PULL_MARGIN) is longer than that, plus patrol paths
    N = len(M)
    P = np.array([(s["rx"], s["ry"], s["z"]) for s in M], dtype=np.float64)
    region = np.array([s["region"] for s in M])
    reach = np.array([(s["ar"] + (0 if s["path"] else s["roam"]) + PULL_MARGIN) if s["on_sight"] else 0 for s in M], dtype=np.float64)
    pair_i, pair_j, pair_d = [], [], []
    for reg in sorted(set(region.tolist())):
        ids = np.nonzero(region == reg)[0]
        ids = ids[np.argsort(P[ids, 0], kind="stable")]
        X = P[ids]
        rch = reach[ids]
        rmax = max(NEAR_LIST, float(rch.max()) if len(ids) else 0)
        xs = X[:, 0]
        lo = np.searchsorted(xs, xs - rmax, side="left")
        hi = np.searchsorted(xs, xs + rmax, side="right")
        for a in range(len(ids)):
            l, h = lo[a], hi[a]
            d = np.sqrt(((X[l:h] - X[a]) ** 2).sum(axis=1))
            ok = (d <= np.maximum(NEAR_LIST, rch[l:h]))
            ok[a - l] = False
            js = np.nonzero(ok)[0]
            if len(js):
                pair_i.append(np.full(len(js), ids[a]))
                pair_j.append(ids[l + js])
                pair_d.append(d[js])
    pair_i = np.concatenate(pair_i)
    pair_j = np.concatenate(pair_j)
    pair_d = np.concatenate(pair_d)
    log("neighbour pairs within %d: %d" % (NEAR_LIST, len(pair_i)))
    # patrols: the closest point of the walker's path to each spawn of its region
    patrol_pairs = {}
    for s in M:
        if not s.get("samples"):
            continue
        ids = np.nonzero(region == s["region"])[0]
        smp = np.array(s["samples"], dtype=np.float64)
        Q = P[ids]
        best = np.full(len(ids), 1e12)
        for k in range(len(smp) - 1):                      # exact distance from each spawn to each leg of the route
            a0, ab = smp[k], smp[k + 1] - smp[k]
            den = float((ab * ab).sum())
            t = np.clip(((Q - a0) @ ab) / den, 0.0, 1.0) if den > 0 else np.zeros(len(ids))
            d = np.sqrt(((Q - (a0 + t[:, None] * ab)) ** 2).sum(axis=1))
            best = np.minimum(best, d)
        lim = max(NEAR_LIST, reach[s["i"]])
        for a in np.nonzero(best <= lim)[0]:
            if ids[a] != s["i"]:
                patrol_pairs[(int(ids[a]), s["i"])] = float(best[a])

    def baf_friend(p, x):
        """Would x come when p is pulled?  BafFriends.cs:19-25, 195-198; GameNPC.cs:4061-4066."""
        if p["faction"]:
            return bool(x["faction"]) and p["faction"] in factions[x["faction"]]["friends"]
        return not x["faction"] and x["key"] == p["key"]

    def baf_radius(p, attackers):
        """BafFriends.cs:36-48: outdoors 2/5 of the pulled monster's aggro range; in a dungeon at least
        min(1000, 250 + 150 per extra attacker)."""
        close = max(0, p["ar"]) * 2 // 5
        if zs[p["zone"]]["dungeon"]:
            return max(close, min(S["baf_dungeon_max"], 250 + 150 * (max(1, attackers) - 1)))
        return close

    # per camp: neighbour spawn -> (closest distance to a spawn of the camp, patrol?, links)
    camp_nb = collections.defaultdict(dict)
    link_count = collections.Counter()        # spawn index -> friends within its full-group BaF radius
    full_group = 8
    for a, b, d in zip(pair_i.tolist(), pair_j.tolist(), pair_d.tolist()):
        sa, sb = M[a], M[b]
        if sa["id"] == sb["id"]:
            continue            # the same spawn point under its other template: one or the other, never both
        if d <= baf_radius(sa, full_group) and baf_friend(sa, sb):
            link_count[a] += 1
            linked = True
        else:
            linked = False
        if sb["camp"] == sa["camp"]:
            continue
        cur = camp_nb[sa["camp"]].get(b)
        if cur is None:
            camp_nb[sa["camp"]][b] = [d, False, linked]
        else:
            if d < cur[0]:
                cur[0] = d
            cur[2] = cur[2] or linked
    for (a, b), d in patrol_pairs.items():
        sa, sb = M[a], M[b]
        if sb["camp"] == sa["camp"]:
            continue
        cur = camp_nb[sa["camp"]].get(b)
        if cur is None:
            camp_nb[sa["camp"]][b] = [d, True, False]
        elif d < cur[0]:
            cur[0], cur[1] = d, True

    # pack factor exactly as the bots measure it (AutonomousCampKnowledge.Measure): the average number of other
    # members of the camp within each member's reach (aggro range, at least 300)
    def pack_factor(members):
        if len(members) < 2:
            return 0.0
        n = 0
        for a in members:
            r2 = max(SOCIAL_MIN, a["ar"]) ** 2
            for b in members:
                if a is not b and (a["rx"] - b["rx"]) ** 2 + (a["ry"] - b["ry"]) ** 2 + (a["z"] - b["z"]) ** 2 <= r2:
                    n += 1
        return n / len(members)

    camp_rows, pts_total = [], 0
    camp_by_id = {}
    for c in camps:
        mem = c["members"]
        zone = zs[c["zone"]]
        h = hist((s["dist"], s["w"]) for s in mem)
        lmin, lmax = min(s["lmin"] for s in mem), max(s["lmax"] for s in mem)
        n_ag = sum(1 for s in mem if s["on_sight"])
        ar_max = max([s["ar"] for s in mem if s["on_sight"]] or [0])
        resp = [respawn_range(s, min_resp) for s in mem]
        nb = camp_nb.get(c["id"], {})
        bykind = collections.defaultdict(list)
        for j, (d, patrol, linked) in nb.items():
            bykind[M[j]["key"]].append((M[j], d, patrol, linked))
        near = []
        rn = rhi = rup = 0
        for key, lst in bykind.items():
            n_agg = sum(1 for s, d, p, l in lst if s["on_sight"])
            in_reach = [(s, d) for s, d, p, l in lst if s["on_sight"] and d <= reach[s["i"]]]
            n_link = sum(1 for s, d, p, l in lst if l)
            dmin = min(d for s, d, p, l in lst)
            closest = min(lst, key=lambda e: (e[1], e[0]["id"]))[0]
            gx = sum(s["rx"] for s, d, p, l in lst) / len(lst) - c["cx"]
            gy = sum(s["ry"] for s, d, p, l in lst) / len(lst) - c["cy"]
            if math.hypot(gx, gy) <= max(c["r"], 300):
                direction = 8                                      # standing among the camp
            else:
                direction = octant(closest["rx"] - c["cx"], closest["ry"] - c["cy"])
            near.append([kind_id[key], len(lst), min(s["lmin"] for s, d, p, l in lst), max(s["lmax"] for s, d, p, l in lst),
                         n_agg, int(round(dmin)), direction, len(in_reach), n_link,
                         max([s["ar"] for s, d, p, l in lst if s["on_sight"]] or [0]),
                         1 if any(p for s, d, p, l in lst) else 0, stances(s for s, d, p, l in lst)])
            rn += len(in_reach)
            for s, d in in_reach:
                rhi = max(rhi, s["lmax"])
                rup += s["lmax"] > lmax
        near.sort(key=lambda e: (-e[7], -e[3], e[5], e[0]))
        links = [link_count.get(s["i"], 0) for s in mem]
        pts = [[s["x"], s["y"], s["lmin"], s["lmax"], s["z"], s["stance"]] for s in sorted(mem, key=lambda s: (s["x"], s["y"], s["id"]))]
        pts_total += len(pts)
        cf = 0
        if any(s["nvar"] > 1 for s in mem):
            cf |= 1
        if any(s["town"] for s in mem):
            cf |= 2
        if any(s["quest"] for s in mem):
            cf |= 4
        if any(s["path"] for s in mem):
            cf |= 8
        row = dict(id=c["id"], kind=kind_id[c["key"]], zone=c["zone"], x=int(round(c["cx"] - zone["ox"])),
                   y=int(round(c["cy"] - zone["oy"])), z=int(round(c["cz"])), r=int(math.ceil(c["r"])), n=len(mem),
                   lmin=lmin, lmax=lmax, levels=hist_list(h), ag=n_ag, ar=ar_max, pack=round(pack_factor(mem), 1),
                   link=max(links), resp=[min(r[0] for r in resp), max(r[1] for r in resp)], f=cf,
                   rn=rn, rhi=rhi, rup=rup, st=stances(mem), pts=pts, near=near)
        camp_rows.append(row)
        camp_by_id[c["id"]] = row
    assert pts_total == len(M)

    # ---- kinds
    kind_rows = []
    for key in keys:
        g = by_key[key]
        kid = kind_id[key]
        names = collections.Counter()
        for s in g:
            names[s["name"]] += s["w"]
        name = sorted(names.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]
        named_w = sum(s["w"] for s in g if s["name"] != s["name"].lower())
        tot_w = sum(s["w"] for s in g)
        h = hist((s["dist"], s["w"]) for s in g)
        lmin, lmax = min(s["lmin"] for s in g), max(s["lmax"] for s in g)
        zc = collections.Counter(s["zone"] for s in g)
        fl = 0
        if named_w * 2 > tot_w:
            fl |= KF["named"]
            if len(g) == 1:
                fl |= KF["unique"]
        if any(s["epic"] for s in g):
            fl |= KF["epic"]
        if any(s["boss"] for s in g):
            fl |= KF["boss"]
        if any(s["quest"] for s in g):
            fl |= KF["quest"]
        if any(s["flags"] & F_FLYING for s in g):
            fl |= KF["flying"]
        if any(s["flags"] & F_SWIMMING for s in g):
            fl |= KF["swimming"]
        if any(s["flags"] & F_GHOST for s in g):
            fl |= KF["ghost"]
        if any(s["flags"] & F_STEALTH for s in g):
            fl |= KF["stealth"]
        if any(s["caster"] for s in g):
            fl |= KF["caster"]
        for t in ("night", "day", "hours"):
            if any(s["timed"] == t for s in g):
                fl |= KF[t]
        if any(s["path"] for s in g):
            fl |= KF["patrol"]
        if any(s["cls"] not in PLAIN_CLASSES for s in g):
            fl |= KF["scripted"]
        if any(s["town"] for s in g):
            fl |= KF["town"]
        if any(s["nvar"] > 1 for s in g):
            fl |= KF["shared"]
        if any(s["cls"] in S["no_xp_classes"] for s in g):
            fl |= KF["noxp"]
        if any(s["lmin"] != s["lmax"] for s in g):
            fl |= KF["wide"]
        fac = mode([s["faction"] for s in g])
        n_ag = sum(1 for s in g if s["on_sight"])
        n_cond = sum(1 for s in g if not s["on_sight"] and s["al"] > 0 and s["ar"] > 0)
        resp = [respawn_range(s, min_resp) for s in g]
        zreal = {zs[z]["realm"] for z in zc}
        hw = W["hour_windows"].get(key)
        kind_rows.append(dict(
            id=kid, key=key, name=name, lmin=lmin, lmax=lmax, levels=hist_list(h), spawns=len(g),
            camps=len({s["camp"] for s in g}), zones=[[z, n] for z, n in sorted(zc.items(), key=lambda kv: (-kv[1], kv[0]))],
            realm=zreal.pop() if len(zreal) == 1 else 0, ag=n_ag, agIf=n_cond, st=stances(g),
            ar=max([s["ar"] for s in g if s["al"] > 0] or [0]), al=mode([s["al"] for s in g]), faction=fac,
            body=mode([s["body"] for s in g]), race=mode([s["race"] for s in g]),
            speed=[min(s["speed"] for s in g), max(s["speed"] for s in g)],
            roam=max(s["roam"] for s in g if not s["path"]) if any(not s["path"] for s in g) else 0,
            resp=[min(r[0] for r in resp), max(r[1] for r in resp)], f=fl,
            cls=sorted({s["cls"] for s in g}), hours=list(hw) if hw and fl & KF["hours"] else None))
    return dict(M=M, camps=camp_rows, kinds=kind_rows, kind_id=kind_id, by_key=by_key, min_resp=min_resp,
                baf=(baf_init, baf_add), paths_missing=paths_missing)



# ---------------------------------------------------------------------------------------------------------------
# Realm events, zone tags, boss ranks, and the monsters that scripts create (they are not rows of the Mob table)
CLS_RE = re.compile(r'^[ \t]*(?:\[[^\]\n]*\][ \t]*)*(?:public|internal|private|protected)?[ \t]*'
                    r'(?:static[ \t]+|abstract[ \t]+|sealed[ \t]+|partial[ \t]+)*class[ \t]+(\w+)', re.M)
NOT_MONSTER_BASES = ("GameSummonedPet", "GamePet", "GameKeepGuard", "GameGuard", "GameMerchant", "GameTrainer", "GameBot",
                     "GameSiegeWeapon", "GameMovingObject", "GameHealer", "GameTeleporter", "CraftNPC", "GameTaxi",
                     "GameHastener", "GameVaultKeeper", "GameTrainingDummy")
OBJECT_WORDS = ("initializator", "controller", "effect", "announce", "antipass", "explosion", "pillar of", "radiation")


def load_events(S):
    """The realm events the server runs as raids (bots/autonomous/AutonomousRealmRaid.cs:12-33, 227-238) and the
    named encounters of the two neutral ones (RealmRaidNeutralEvents.cs:81-90)."""
    txt = read("bots/autonomous/AutonomousRealmRaid.cs")
    ev = []
    for m in re.finditer(r'new\("([\w-]+)",\s*"([^"]+)",\s*eRealm\.(\w+),\s*(\d+),\s*"(\w+)",\s*(\d+)\)', txt):
        ev.append(dict(id=m.group(1), name=m.group(2), realm={"Albion": 1, "Midgard": 2, "Hibernia": 3}[m.group(3)],
                       region=int(m.group(4)), boss_class=m.group(5), suggested=int(m.group(6))))
    assert len(ev) == 12, "realm event list changed"
    finals = {int(a): re.findall(r'"(\w+)"', b) for a, b in
              re.findall(r'(\d+) => \[([^\]]+)\]', need(r'FinalTypes => Region switch\s*\{(.*?)\};', txt, "FinalTypes").group(1))}
    assert finals == {60: ["Apocalypse"], 160: ["KingTuscar", "QueenKula"], 191: ["Olcasgean"]}, finals
    need(r'IsDungeon => Region is 60 or 160 or 191 \|\| IsNeutral', txt, "epic dungeon regions")
    neu = read("bots/autonomous/RealmRaidNeutralEvents.cs")
    obj = {}
    blk = need(r'Objectives\(ushort region\) => region switch\s*\{(.*?)_ => null', neu, "neutral objectives").group(1)
    for m in re.finditer(r'(SummonersHall|DarknessFalls)\s*=>\s*\[(.*?)\]', blk, re.S):
        obj[{"SummonersHall": 248, "DarknessFalls": 249}[m.group(1)]] = re.findall(r'"([^"]+)"', m.group(2))
    assert len(obj.get(248, [])) == 4 and len(obj.get(249, [])) == 9, obj
    approaches = [int(x) for x in re.findall(r'\d+', need(r'SummonersHallApproaches = \[([^\]]+)\]', neu, "approaches").group(1))]
    pol = read("bots/autonomous/AutonomousDungeonPolicy.cs")
    fd = [int(x) for x in re.findall(r'\d+', need(r'IsSharedFrontierDungeon\(ushort region\) => region is ([^;]+);', pol, "frontier dungeons").group(1))]
    st = [int(x) for x in re.findall(r'\d+', need(r'IsStarterDungeonRegion\(ushort region\) => region is ([^;]+);', pol, "starter dungeons").group(1))]
    need(r'"/raid 40", "/raid 80"', read("commands/playercommands/CompanionRaidCommand.cs"), "/raid sizes")
    for e in ev:
        e["finals"] = finals.get(e["region"], [e["boss_class"]])
        e["objectives"] = obj.get(e["region"], [])
        e["kind"] = "dragon" if e["id"].startswith("dragon") else "epic dungeon" if e["region"] in finals else "neutral dungeon"
    return dict(events=ev, approaches=approaches, frontier_dungeons=fd, starter_dungeons=st)


def load_scripted(S, W, con):
    """Monsters that only a script, an encounter or a quest step creates. Returns (records, stats).
    Three sources, all read as text or data:
      A  `x = new SomeNpcClass(); ... x.AddToWorld()` in scripts/ and quests/ (boss adds, summoned bosses, quest
         finale bosses), with the name and level the code gives (literal, Util.Random range, or an NpcTemplate id)
      B  PrimaEncounter165.SpawnAdd(owner, template, "name", level, ...) in scripts/true165 (the True 1.65 encounters)
      C  classic-quests.json steps with a "Spawn" (quest event monsters: exact name, level, place)"""
    parents = S["parents"]
    tpl = W["templates"]
    row_by_cls, row_by_brain = collections.defaultdict(list), collections.defaultdict(list)
    for sp in W["spawns"]:
        if sp["place"] != "ok":
            continue
        row_by_cls[sp["cls"]].append(sp)
        if sp["brain"]:
            row_by_brain[sp["brain"]].append(sp)
    files = {}
    for sub in ("scripts", "quests"):
        for dp, _dn, fn in os.walk(SRC + "/" + sub):
            for f in fn:
                if f.endswith(".cs"):
                    with open(os.path.join(dp, f), encoding="utf-8-sig", errors="replace") as fh:
                        files[os.path.join(dp, f).replace("\\", "/").split("/GameServer/")[1]] = fh.read()
    cinfo, brain_bodies = {}, collections.defaultdict(set)
    spans = {}
    for rel, txt in files.items():
        ms = list(CLS_RE.finditer(txt))
        spans[rel] = [(m.group(1), m.start(), ms[i + 1].start() if i + 1 < len(ms) else len(txt)) for i, m in enumerate(ms)]
        for cname, a, b in spans[rel]:
            body = txt[a:b]
            cinfo.setdefault(cname, dict(file=rel, body=body))
            for m in re.finditer(r'new\s+(\w+Brain\w*)\s*\(', body):
                brain_bodies[m.group(1)].add(cname)

    def lvl(text):
        m = re.search(r'\(byte\)\s*Util\.Random\(\s*(\d+)\s*,\s*(\d+)\s*\)', text)
        if m:
            return int(m.group(1)), int(m.group(2))
        m = re.search(r'^\s*(?:\(byte\)\s*)?(\d+)\s*$', text)
        return (int(m.group(1)), int(m.group(1))) if m else None

    def class_defaults(cname):
        c = cinfo.get(cname)
        if not c:
            return {}
        b = c["body"]
        d = {}
        names = re.findall(r'(?<![\w.])Name\s*=\s*"([^"]+)"\s*;', b)
        if names:
            d["names"] = sorted(set(names))
        lv = [lvl(x) for x in re.findall(r'(?<![\w.])Level\s*=\s*([^;]+);', b)]
        lv = [x for x in lv if x]
        if lv:
            d["lv"] = (min(x[0] for x in lv), max(x[1] for x in lv))
        t = [int(x) for x in re.findall(r'NpcTemplateMgr\.GetTemplate\((\d+)\)', b)]
        if t:
            d["tpl"] = t[0]
        if re.search(r'(?<![\w.])Flags\s*(?:\|=|=|\^=)\s*[^;]*(PEACE|CANTTARGET)', b):
            d["obj"] = True
        if re.search(r'(?<![\w.])Realm\s*=\s*eRealm\.(Albion|Midgard|Hibernia)', b):
            d["realm"] = True
        sfx = re.search(r'(?<![\w.])Name\s*\+=\s*"([^"]+)"\s*;', b)        # Name += "'s Minion" after the template
        if sfx:
            d["suffix"] = sfx.group(1)
        bt = body_literal(b, r'(?<![\w.])')
        if bt is not None:
            d["body"] = bt
        return d

    body_ids = {v.lower(): k for k, v in BODY.items() if v}

    def body_literal(text, prefix):
        """The body type a script sets itself: BodyType = 11, or = (ushort)NpcTemplateMgr.eBodyType.Demon."""
        found = set()
        for m in re.finditer(prefix + r'BodyType\s*=\s*([^;}]+)', text):
            v = m.group(1).strip().rstrip(")").strip()
            e = re.search(r'eBodyType\.(\w+)', v)
            if e and e.group(1).lower() in body_ids:
                found.add(body_ids[e.group(1).lower()])
            elif re.fullmatch(r'(?:\(\w+\)\s*)?\d+', v):
                found.add(int(re.search(r'\d+', v.split(")")[-1]).group(0)))
            else:
                found.add(None)
        return found.pop() if len(found) == 1 else None

    recs, stats = [], collections.Counter()
    npc_classes = lambda c: c in ("GameNPC", "GameEpicNPC", "GameEpicBoss", "GameEpicDungeonNPC") or "GameNPC" in ancestors(parents, c)
    # ---- A
    for rel, txt in files.items():
        for owner, a, b in spans[rel]:
            body = txt[a:b]
            for m in re.finditer(r'(?:(\w+)\s*=\s*)?new\s+(\w+)\s*\(\s*\)\s*(?:;|\{)', body):
                var, cname = m.group(1), m.group(2)
                if not npc_classes(cname) or cname.endswith("Brain"):
                    continue
                anc = ancestors(parents, cname)
                if any(is_a(cname, anc, x) for x in NOT_MONSTER_BASES):
                    continue
                if cname in row_by_cls and cname not in ("GameNPC", "GameEpicNPC", "GameEpicBoss", "GameEpicDungeonNPC"):
                    stats["script copies of a class that is also a world row"] += 1
                    continue
                blk = body[m.end(): m.end() + 3000]
                if var:
                    cut = re.search(r'(?<![\w.])' + re.escape(var) + r'\.AddToWorld\s*\(', blk)
                    blk = blk[:cut.start()] if cut else blk[:1500]
                    pv = r'(?<![\w.])' + re.escape(var) + r'\.'
                else:
                    blk, pv = blk[:600], r'(?<![\w.])'
                d = class_defaults(cname)
                name = re.search(pv + r'Name\s*=\s*"([^"]+)"', blk)
                names = [name.group(1)] if name else d.get("names", [])
                lv = None
                ml = re.search(pv + r'Level\s*=\s*([^;,}]+)', blk)
                if ml:
                    lv = lvl(ml.group(1))
                lv = lv or d.get("lv")
                mt = re.search(r'GetTemplate\((\d+)\)', blk)
                tid = int(mt.group(1)) if mt else d.get("tpl", 0)
                t = (tpl.get(tid) or [None])[0]
                if t and not names:
                    names = [t["Name"]]
                if d.get("suffix") and not name:
                    names = [n + d["suffix"] for n in names]
                bt = body_literal(blk, pv) if var else None
                if bt is None:
                    bt = d.get("body")
                if t and lv is None:
                    e = level_dist(split_levels(t["Level"]), 0)
                    lv = (min(e), max(e))
                reg = re.search(pv + r'CurrentRegionID\s*=\s*(\d+)', blk) or re.search(pv + r'CurrentRegion\s*=\s*WorldMgr\.GetRegion\((\d+)\)', blk)
                x = re.search(pv + r'X\s*=\s*(\d+)\s*;', blk)
                y = re.search(pv + r'Y\s*=\s*(\d+)\s*;', blk)
                realm = bool(re.search(pv + r'Realm\s*=\s*eRealm\.(Albion|Midgard|Hibernia)', blk)) or d.get("realm", False)
                obj = d.get("obj", False) or bool(re.search(pv + r'Flags\s*(?:\|=|=)\s*[^;]*(PEACE|CANTTARGET)', blk))
                if not names:
                    stats["script spawns with no name in the code"] += 1
                    continue
                for nm in names:
                    recs.append(dict(name=nm, lv=lv, cls=cname, owner=owner, file=rel, tpl=tid, realm=realm, obj=obj, body=bt,
                                     region=int(reg.group(1)) if reg else None, x=int(x.group(1)) if x else None,
                                     y=int(y.group(1)) if y else None, src="script"))
    # ---- B
    for rel, txt in files.items():
        if "SpawnAdd(" not in txt:
            continue
        for owner, a, b in spans[rel]:
            body = txt[a:b]
            # ("name", ints...) rows of the class's static tables and of its own Spawn("name", ...) helper calls
            tuples = [(n, [int(v) for v in re.findall(r'-?\d+', rest)]) for n, rest in
                      re.findall(r'\(\s*"([^"]{2,40})"\s*,\s*([\d\s,\-]+)\)', body) if not re.search(r'[.!?:]', n)]
            level_shape = lambda nums: len(nums) == 3 and nums[0] > 9999 and nums[1] <= nums[2] <= 100
            for m in re.finditer(r'SpawnAdd\(\s*[\w.]+\s*,\s*([^,]+),\s*("([^"]+)"|[^,]+),\s*((?:\(byte\)\s*Util\.Random\([^)]*\))|[^,]+),', body):
                if "GameNPC SpawnAdd" in body[max(0, m.start() - 30): m.start() + 10]:
                    continue
                lv = lvl(m.group(4).strip())
                nxt = body.find("SpawnAdd(", m.end())
                bt = body_literal(body[m.end(): min(m.end() + 900, nxt if nxt > 0 else len(body))], r'npc\.')   # the call's tweak
                targ = m.group(1).strip()
                if targ.isdigit():
                    tid = int(targ)
                else:
                    c = (re.search(r'\b' + re.escape(targ.split(".")[-1]) + r'\s*=\s*(\d+)', txt)
                         if re.fullmatch(r'[\w.]+', targ) else None)
                    ix = re.fullmatch(r'\w+\[(\d+)\]\.Template', targ)
                    shaped = [nums for _n, nums in tuples if level_shape(nums)]
                    tid = int(c.group(1)) if c else shaped[int(ix.group(1))][0] if ix and int(ix.group(1)) < len(shaped) else None
                if m.group(3):
                    recs.append(dict(name=m.group(3), lv=lv, cls="GameNPC", owner=owner, file=rel, tpl=tid, realm=False,
                                     obj=False, region=None, x=None, y=None, src="encounter", body=bt))
                    continue
                for nm, nums in tuples:               # the name comes from a table of the same class
                    if lv is not None and not level_shape(nums):
                        l2 = lv
                    elif lv is None and "Min" in m.group(4) and level_shape(nums):
                        l2 = (nums[1], nums[2])
                    else:
                        continue
                    at = nums[:2] if len(nums) == 3 and min(nums[:2]) > 100000 else [None, None]
                    recs.append(dict(name=nm, lv=l2, cls="GameNPC", owner=owner, file=rel,
                                     tpl=nums[0] if nums and 9999 < nums[0] and at[0] is None else tid,
                                     realm=False, obj=False, region=None, x=at[0], y=at[1], src="encounter", body=bt))
    # ---- resolve where: the world rows whose class or brain owns the spawn (following script-to-script chains),
    # a controller the script itself creates at fixed coordinates, or the quest script that spawns it
    by_cls = collections.defaultdict(list)
    for r in recs:
        by_cls[r["cls"]].append(r)
    quest_bases = ("BaseQuest", "AbstractQuest", "RewardQuest", "DataQuest")

    def trace(owner, depth=0, seen=None):
        seen = seen if seen is not None else set()
        res = dict(rows=[], abs=[], quests=[])
        if owner in seen or depth > 8:
            return res
        seen.add(owner)
        if any(q in ancestors(parents, owner) for q in quest_bases):
            res["quests"].append(owner)
            return res
        if owner in row_by_cls and owner != "GameNPC":
            res["rows"] += row_by_cls[owner]
        if owner in row_by_brain:
            res["rows"] += row_by_brain[owner]
        if res["rows"]:
            return res
        nxt = list(brain_bodies.get(owner, ()))
        if owner.endswith("Brain") and owner[:-5] in cinfo:
            nxt.append(owner[:-5])
        for r in by_cls.get(owner, ()):
            if r["owner"] == owner:
                if r["region"] is not None and r["x"] is not None:
                    res["abs"].append((r["region"], r["x"], r["y"], owner))
            else:
                nxt.append(r["owner"])
        for n in sorted(set(nxt)):
            t = trace(n, depth + 1, seen)
            for k in res:
                res[k] += t[k]
        return res

    for r in recs:
        t = trace(r["owner"])
        r["roots"] = t["rows"]
        r["abs"] = t["abs"] + ([(r["region"], r["x"], r["y"], r["owner"])] if r["region"] is not None and r["x"] is not None else [])
        r["quest"] = t["quests"][0] if t["quests"] else None
    # ---- C
    qnames = {str(q["ID"]): q["Name"] for q in con.execute("select ID, Name from DataQuest")}
    qpath = wgcommon.RUNTIME + "/server/classic-quests.json"
    if os.path.exists(qpath):
        with open(qpath, encoding="utf-8-sig") as f:
            cq = json.load(f)
        for qid, q in sorted(cq.get("Quests", {}).items()):
            for step in q.get("Steps") or []:
                sp = (step or {}).get("Spawn")
                if not sp or not sp.get("Name"):
                    continue
                lv = sp.get("Level") or 0
                recs.append(dict(name=sp["Name"], lv=(lv, lv) if lv else None, cls="GameNPC", owner="classic quest", file="classic-quests.json",
                                 tpl=sp.get("TemplateId") or 0, realm=False, obj=False, region=sp.get("Region"), x=sp.get("X"), y=sp.get("Y"),
                                 src="quest", roots=[], abs=[(sp.get("Region"), sp.get("X"), sp.get("Y"), "quest")] if sp.get("Region") is not None and sp.get("X") is not None else [], quest=qnames.get(qid, "quest " + qid), count=sp.get("Count") or 1,
                                 despawn=sp.get("DespawnSeconds") or 0))
    return recs, stats


def rank_of(kind_spawns, name, classes, S, EV, quest):
    """One label per kind and the rule that gave it (first rule that fits)."""
    parents = S["parents"]
    ev_classes = {c: e for e in EV["events"] for c in [e["boss_class"]] + e["finals"]}
    ev_names = {n.lower(): e for e in EV["events"] for n in e["objectives"]}
    for c in classes:
        if c in ev_classes:
            return "event", "the final boss of the realm event \"%s\" (AutonomousRealmRaid.cs:227-238)" % ev_classes[c]["name"]
    if name.lower() in ev_names:
        return "event", "a named objective of the realm event \"%s\" (RealmRaidNeutralEvents.cs:81-90)" % ev_names[name.lower()]["name"]
    named = name != name.lower()
    for c in classes:
        anc = ancestors(parents, c)
        if is_a(c, anc, "GameEpicBoss") or "IRaidBossTraits" in anc:
            return "epic", "server class %s is a GameEpicBoss (the server's raid-boss class)" % c
    for c in classes:
        if "IGameEpicNpc" in ancestors(parents, c) and named:
            return "boss", "named, and server class %s is an epic-encounter class (GameEpicNPC)" % c
    for c in classes:
        if c not in PLAIN_CLASSES and named:
            return "boss", "named, with its own encounter script (class %s)" % c
    if quest:
        return "quest", "a spawn is on the classic quest monster list (classic-quests.json QuestMonsterIds)" if quest is True else quest
    if named:
        return "named", "its name has a capital letter (the server's own test, GameNPC.cs:3174)"
    return "normal", ""



def scripted_kinds(recs, S, W, EV, world_keys, first_id, kind_id):
    """Group the script, encounter and quest spawns by name. Left out and counted: realm NPCs, effect objects and
    controllers, names that are already world kinds, and classes nothing in this world creates."""
    parents, zs, zbr, tpl = S["parents"], W["zones"], W["zbr"], W["templates"]
    left = collections.Counter()
    groups = collections.OrderedDict()
    for r in recs:
        nm = r["name"]
        epic_cls = is_a(r["cls"], ancestors(parents, r["cls"]), "GameEpicBoss")
        if r["realm"]:
            left["belongs to a realm"] += 1
        elif nm.endswith(" ") or nm.lower().endswith(" of") or len(nm) < 2:
            left["name is built at run time"] += 1
        elif any(w in nm.lower() for w in OBJECT_WORDS) or (r["obj"] and not epic_cls):
            left["effect object or controller"] += 1
        elif nm.lower() in world_keys:
            left["name is already a world kind (an extra copy the script adds)"] += 1
        elif not r["roots"] and not r["abs"] and not r["quest"]:
            left["its class exists but nothing in this world creates it"] += 1
        else:
            groups.setdefault(nm.lower(), []).append(r)
    out = []
    for i, (key, g) in enumerate(sorted(groups.items())):
        names = collections.Counter(r["name"] for r in g)
        name = sorted(names.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]
        lv = [r["lv"] for r in g if r["lv"]]
        classes = sorted({r["cls"] for r in g})
        where, roots = [], collections.OrderedDict()
        for r in g:
            for sp in r["roots"]:
                roots.setdefault(sp["name"], sp)
                if sp["zone"] is not None and [sp["zone"], sp["x"], sp["y"]] not in where:
                    where.append([sp["zone"], sp["x"], sp["y"]])
            for reg, x, y, _owner in r["abs"]:
                z = wgcommon.zone_of(zbr, reg, x, y)
                if z and [z["id"], x - z["ox"], y - z["oy"]] not in where:
                    where.append([z["id"], x - z["ox"], y - z["oy"]])
        src = sorted({r["src"] for r in g})
        quest = sorted({r["quest"] for r in g if r["quest"]})
        r0 = g[0]
        if "quest" in src:
            n = max(r.get("count", 1) for r in g)
            dsp = max(r.get("despawn", 0) for r in g)
            how = "Appears for the quest \"%s\" at this spot%s%s." % (
                quest[0], " (%d of them)" % n if n > 1 else "", ", and leaves after %d minutes if not killed" % (dsp // 60) if dsp >= 60 else "")
        elif quest:
            how = "Spawned by the quest script %s (a scripted quest finale); it is not in the world until the quest calls it." % quest[0]
        elif roots:
            how = "Not a standing spawn: created by the script of %s (%s)." % (
                " / ".join("\"%s\"" % n for n in list(roots)[:3]), r0["file"].split("/")[-1])
        elif any(r["owner"] == r["cls"] and r["region"] is not None for r in g):
            how = "Not a row of the world table: its own script (%s) puts it in the world at this spot when the server starts." % r0["file"].split("/")[-1]
        else:
            how = "Not a standing spawn: created by a controller that the script %s itself puts in the world." % r0["file"].split("/")[-1]
        tid = next((r["tpl"] for r in g if r["tpl"]), 0)
        unresolved = any(r["tpl"] is None for r in g)
        t = (tpl.get(tid) or [None])[0]
        # body type as the kill sees it: the script's own literal, else the template's, else 0 when there is no
        # template and nothing sets one; None when it could not be established
        lits = {r.get("body") for r in g if r.get("body") is not None}
        body_type = lits.pop() if len(lits) == 1 else None if lits else (t["BodyType"] or 0) if t else 0 if not tid and not unresolved else None
        rank, why = rank_of(None, name, classes, S, EV, quest[0] if "quest" in src else None)
        if "quest" in src:
            rank, why = "quest", "spawned by a step of the classic quest \"%s\" (classic-quests.json)" % quest[0]
        elif quest:
            rank, why = "quest", "spawned by the quest script %s (quest finale)" % quest[0]
        elif rank == "boss" and why.startswith("named, with its own"):
            rank, why = "named", "its name has a capital letter; it is an add or a summon of the encounter, not a boss of its own"
        out.append(dict(id=first_id + i, key=key, name=name, lmin=min(x[0] for x in lv) if lv else None,
                        lmax=max(x[1] for x in lv) if lv else None, cls=classes, rank=rank, rankWhy=why,
                        body=body_type, where=where[:12], by=[kind_id[k.lower()] for k in roots if k.lower() in kind_id],
                        byNames=list(roots)[:6], how=how, src=src, quest=quest[0] if quest else None,
                        file=sorted({r["file"].split("/")[-1] for r in g})))
    return out, left


CAT_TEXT = collections.OrderedDict([
    ("monster", "Monsters (no realm, can be attacked, not a service NPC)"),
    ("service", "Merchants, trainers, healers, stable masters, hasteners, vault keepers, crafting masters and other "
                "NPCs run by an NPC guild script"),
    ("guard", "Realm guards (GameGuard)"),
    ("keepguard", "Frontier and battleground keep guards, lords, commanders, keep hasteners (GameKeepGuard)"),
    ("realm", "Other NPCs that belong to a realm (townsfolk, quest people): realm 1, 2 or 3"),
    ("peace", "No-realm NPCs with the PEACE flag: nobody can attack them (ambient animals, controllers, statues)"),
    ("untargetable", "No-realm NPCs with the CANTTARGET flag (falling ice, decoration)"),
    ("dummy", "Training dummies"),
    ("noname", "Rows with an empty name"),
])
PLACE_TEXT = collections.OrderedDict([
    ("ok", "in a zone of the playable world"),
    ("disabledregion", "in a region closed by the server property disabled_regions"),
    ("disabledexpansion", "in a region of an expansion closed by disabled_expansions (Trials of Atlantis and later)"),
    ("nozone", "position outside every zone of its region (dropped: it has no zone coordinates)"),
    ("noregion", "region missing from the Regions table"),
])
CAMP_COLS = ["id", "kind", "zone", "x", "y", "z", "r", "n", "lmin", "lmax", "levels", "ag", "ar", "pack", "link",
             "resp", "f", "rn", "rhi", "rup", "st", "pts", "near"]
KIND_COLS = ["id", "key", "name", "lmin", "lmax", "levels", "spawns", "camps", "zones", "realm", "ag", "agIf", "st", "ar",
             "al", "faction", "body", "race", "speed", "roam", "resp", "f", "cls", "hours", "rank", "rankWhy"]
SCRIPTED_COLS = ["id", "key", "name", "lmin", "lmax", "cls", "rank", "rankWhy", "body", "where", "by", "byNames", "how",
                 "src", "quest", "file"]
RANKS = ("normal", "named", "quest", "boss", "epic", "event")
NEAR_COLS = ["kind", "n", "lmin", "lmax", "nAgg", "dist", "dir", "nReach", "nLink", "ar", "patrol", "st"]


def standing_name(S, aggro):
    return ("aggressive" if aggro > S["aggressive_above"] else "hostile" if aggro > S["hostile_above"]
            else "neutral" if aggro > S["neutral_above"] else "friendly")


def main():
    A = wgcommon.args(__doc__)
    t0 = time.time()
    S = load_source()
    con = wgcommon.open_db(A.db)
    W = load_world(con, S)
    zs = W["zones"]
    # Zones.Realm is 0 for every zone of this world, so the realm of a zone is P-D's (data/zones.js, read when it
    # is there; build_maps.py first). Without it kinds get realm 0 and meta.zoneRealms says so.
    zone_realm_source = "none"
    zpath = os.path.join(A.out, "zones.js")
    if os.path.exists(zpath):
        with open(zpath, encoding="utf-8") as f:
            txt = f.read()
        zj = json.loads(txt[txt.index(",") + 1: txt.rindex(")")])
        for z in zj["zones"]:
            if z["id"] in zs:
                zs[z["id"]]["realm"] = z["realm"]
                zs[z["id"]]["pkind"], zs[z["id"]]["pexp"] = z["kind"], z["expansion"]
        zone_realm_source = "data/zones.js"
    R = build(con, S, W)
    M, camps, kinds = R["M"], R["camps"], R["kinds"]

    # ---- counts that must add up
    cat = W["cat_rows"]
    total = sum(cat.values())
    assert abs(total - W["mob_rows"]) < 1e-6
    excluded = []
    for c in CAT_TEXT:
        for p in PLACE_TEXT:
            v = cat.get((c, p), 0)
            if v:
                assert abs(v - round(v)) < 1e-6, (c, p, v)
                excluded.append([c, p, int(round(v))])
    assert sum(e[2] for e in excluded) == W["mob_rows"], "categories do not add up to the Mob table"
    hunted_rows = cat[("monster", "ok")]
    assert abs(sum(s["w"] for s in M) - hunted_rows) < 1e-6
    assert sum(c["n"] for c in camps) == len(M) == sum(k["spawns"] for k in kinds)
    assert sum(k["camps"] for k in kinds) == len(camps)
    assert len({k["key"] for k in kinds}) == len(kinds) and all(k["key"] == k["key"].lower() for k in kinds)
    assert len({k["key"].strip() for k in kinds}) == len(kinds), "two kinds differ only by spaces" 
    kind_by_id = {k["id"]: k for k in kinds}
    for c in camps:
        k = kind_by_id[c["kind"]]
        assert k["lmin"] <= c["lmin"] <= c["lmax"] <= k["lmax"]
        assert c["f"] & 1 or abs(sum(x[1] for x in c["levels"]) - c["n"]) <= 0.005 * len(c["levels"]) + 0.01, c["id"]
        assert 0 <= c["ag"] <= c["n"] and c["rup"] <= c["rn"] and sum(c["st"]) == c["n"] and c["ag"] <= c["st"][0]
        z = zs[c["zone"]]
        assert all(0 <= p[0] < z["w"] and 0 <= p[1] < z["h"] and p[2] <= p[3] for p in c["pts"])
        assert c["r"] <= CAMP_MAX_SPAN + 1, (c["id"], c["r"])
        for e in c["near"]:
            assert e[0] in kind_by_id and e[7] <= e[4] <= e[1] and e[5] >= 0

    # ---- realm events, boss ranks, zone tags
    EV = load_events(S)
    by_key = R["by_key"]
    for k in kinds:
        g = by_key[k["key"]]
        k["rank"], k["rankWhy"] = rank_of(g, k["name"], k["cls"], S, EV, True if any(sp["quest"] for sp in g) else None)
    recs, sstats = load_scripted(S, W, con)
    scripted, sleft = scripted_kinds(recs, S, W, EV, set(by_key), len(kinds) + 1, R["kind_id"])
    cls_zone = collections.defaultdict(set)
    for sp in W["spawns"]:
        if sp["place"] == "ok" and sp["zone"] is not None:
            cls_zone[sp["cls"]].add(sp["zone"])
    region_zone = {z["region"]: z["id"] for z in zs.values() if z["n_in_region"] == 1}
    zone_tags = collections.defaultdict(list)
    erows = []
    name_of_cls = {}
    for sp in M:
        name_of_cls.setdefault(sp["cls"], sp["name"])
    for sk in scripted:
        for c in sk["cls"]:
            name_of_cls.setdefault(c, sk["name"])
    for e in EV["events"]:
        zones_e = sorted(cls_zone.get(e["boss_class"], ())) or ([region_zone[e["region"]]] if e["region"] in region_zone else [])
        assert zones_e, "realm event without a zone: " + e["id"]
        bosses = [name_of_cls.get(c, c) for c in e["finals"]]
        erows.append([e["id"], e["name"], e["kind"], e["realm"], zones_e[0], e["suggested"], bosses, e["objectives"]])
        for zid in zones_e[:1]:
            if "raid" not in zone_tags[zid]:
                zone_tags[zid].append("raid")
            if e["kind"] == "epic dungeon" and "epic" not in zone_tags[zid]:
                zone_tags[zid].append("epic")
    for z in zs.values():
        if z["region"] == 249:
            zone_tags[z["id"]].append("df")
        if z["region"] in EV["frontier_dungeons"]:
            zone_tags[z["id"]].append("frontier dungeon")
        if z["region"] in EV["starter_dungeons"]:
            zone_tags[z["id"]].append("starter dungeon")

    def zone_group(z):
        k = z.get("pkind", "unknown")
        if z["region"] == 249:
            return "Darkness Falls"
        if k in ("outdoor", "dungeon") and z.get("pexp") == "si":
            return "Shrouded Isles " + k
        return k

    # ---- zone summaries
    zrows = []
    by_zone_spawn = collections.defaultdict(list)
    for s in M:
        by_zone_spawn[s["zone"]].append(s)
    by_zone_camp = collections.defaultdict(list)
    for c in camps:
        by_zone_camp[c["zone"]].append(c)
    others = collections.defaultdict(collections.Counter)
    for s in W["spawns"]:
        if s["cat"] != "monster" and s["zone"] is not None and s["place"] == "ok":
            others[s["zone"]][s["cat"]] += s["w"]
    for zid in sorted(set(by_zone_spawn) | set(others)):
        g = by_zone_spawn.get(zid, [])
        cs = by_zone_camp.get(zid, [])
        h = hist((s["dist"], s["w"]) for s in g)
        kc = collections.defaultdict(list)
        for s in g:
            kc[s["key"]].append(s)
        klist = sorted(([R["kind_id"][k], len(v), min(s["lmin"] for s in v), max(s["lmax"] for s in v),
                         sum(1 for s in v if s["on_sight"])] for k, v in kc.items()), key=lambda e: (e[2], e[3], e[0]))
        big = [c for c in cs if c["n"] >= 3]
        safest = sorted(big, key=lambda c: (c["rup"], c["rn"], c["ag"] > 0, c["pack"] if c["ag"] else 0, -c["n"], c["id"]))[:8]
        densest = sorted(big, key=lambda c: (-c["n"], c["r"], c["id"]))[:8]
        o = others.get(zid, {})
        zrows.append([zid, len(g), len(kc), len(cs), min([s["lmin"] for s in g] or [0]), max([s["lmax"] for s in g] or [0]),
                      hist_list(h), sum(1 for s in g if s["on_sight"]), stances(g), int(round(o.get("guard", 0))),
                      int(round(o.get("keepguard", 0))), int(round(o.get("service", 0) + o.get("realm", 0))), klist,
                      [c["id"] for c in safest], [c["id"] for c in densest], zone_group(zs[zid]), zone_tags.get(zid, [])])
    assert sum(z[1] for z in zrows) == len(M)
    # coverage: every zone that holds an open-world monster row has camps, and the groups add up
    assert all(z[3] > 0 for z in zrows if z[1] > 0), "a zone with monsters has no camp"
    cov = collections.OrderedDict()
    for z in zrows:
        c = cov.setdefault(z[-2], [0, 0, set(), 0])
        c[0] += 1
        c[1] += z[1]
        c[2] |= {e[0] for e in z[12]}
        c[3] += z[3]
    coverage = [[g, c[0], c[1], len(c[2]), c[3]] for g, c in sorted(cov.items())]
    assert sum(c[2] for c in coverage) == len(M) and sum(c[4] for c in coverage) == len(camps)

    # ---- factions used by monsters, and what a kill does to standing
    used = {k["faction"] for k in kinds if k["faction"]} | {s["faction"] for s in M if s["faction"]}
    more = set()
    for f in used:
        more |= W["factions"][f]["friends"] | W["factions"][f]["enemies"]
    frows = []
    for fid in sorted(used | more):
        f = W["factions"][fid]
        frows.append([fid, f["name"], f["base"], standing_name(S, f["base"]), sorted(f["friends"] - {fid}), sorted(f["enemies"]),
                      1 if fid in S["town_factions"] else 0])
    # ---- patrol lines for the map (zone-local coordinates of the spawn's zone)
    prow = []
    for s in M:
        if s.get("path_points"):
            z = zs[s["zone"]]
            prow.append([R["kind_id"][s["key"]], s["zone"], s["camp"], [[p[0] - z["ox"], p[1] - z["oy"]] for p in s["path_points"]]])
    prow.sort(key=lambda e: (e[1], e[0], e[2]))
    # ---- no-realm NPCs the server will not let anyone attack as they spawn (PEACE / CANTTARGET). Some are quest
    # or event monsters with loot that a script makes attackable later, so pages can still show where they stand.
    peaceful = []
    for s in W["spawns"]:
        if s["cat"] in ("peace", "untargetable") and s["place"] == "ok":
            peaceful.append([s["name"], s["key"], s["zone"], s["x"], s["y"], s["z"], s["lmin"], s["lmax"], s["cls"],
                             s["cat"], 1 if s["cls"] not in PLAIN_CLASSES else 0])
    peaceful.sort(key=lambda e: (e[1], e[2], e[3], e[4]))
    races = {str(r["ID"]): [r["Name"], r["ResistCrush"], r["ResistSlash"], r["ResistThrust"], r["ResistBody"], r["ResistCold"],
                            r["ResistEnergy"], r["ResistHeat"], r["ResistMatter"], r["ResistSpirit"]]
             for r in W["races"].values() if any(k["race"] == r["ID"] for k in kinds)}

    rules = dict(
        campLink=CAMP_LINK, campMaxRadius=CAMP_MAX_SPAN, nearDistance=NEAR_LIST, pullMargin=PULL_MARGIN,
        maxAggroRange=S["max_aggro"], aggressiveStandingAbove=S["aggressive_above"],
        standingChangeChance=0.2, bafInitialChance=R["baf"][0], bafAdditionalChance=R["baf"][1],
        bafOutdoorRadius="2/5 of the pulled monster's aggro range", bafDungeonRadius=[250, 150, S["baf_dungeon_max"]],
        xpCapPercent=int(W["props"].get("xp_cap_percent", "125")), respawnMinMinutes=R["min_resp"],
        stealthSight=150, threatLevels=THREAT_LEVELS, botCampCell=S["bot_cell"], botMergeRadius=S["bot_merge"],
        packDense=2.0, townRadius=S["town_radius"])
    counts = dict(mobRows=W["mob_rows"], monsterRows=int(round(hunted_rows)), spawnPoints=len(M), kinds=len(kinds),
                  camps=len(camps), singleCamps=sum(1 for c in camps if c["n"] == 1),
                  levelRangeSpawns=sum(1 for s in M if s["lmin"] != s["lmax"]),
                  onSight=sum(1 for s in M if s["on_sight"]), sharedPoints=sum(1 for s in M if s["nvar"] > 1),
                  patrols=len(prow), campsWithReachNeighbours=sum(1 for c in camps if c["rn"]),
                  campsWithStrongerReachNeighbours=sum(1 for c in camps if c["rup"]), zones=len(zrows))
    meta = dict(built=wgcommon.stamp(), db=os.path.basename(A.db), counts=counts, rules=rules, flagBits=KF,
                campFlagBits=dict(shared=1, town=2, quest=4, patrol=8), bodyTypes=BODY, stances=list(STANCE), zoneRealms=zone_realm_source, dirs=list(OCT) + ["among"],
                nearCols=NEAR_COLS, excluded=excluded, excludedText=dict(cat=CAT_TEXT, place=PLACE_TEXT),
                ranks=list(RANKS), coverage=dict(cols=["group", "zones", "spawnPoints", "kinds", "camps"], rows=coverage),
                scriptLevels={c: list(v) for c, v in sorted(S["script_levels"].items()) if any(sp["cls"] == c for sp in M)},
                scriptedLeftOut=dict(sorted(list(sleft.items()) + list(sstats.items()))))
    mobs = dict(meta=meta, kinds=wgcommon.table(KIND_COLS, [[k[c] for c in KIND_COLS] for k in kinds]),
                factions=wgcommon.table(["id", "name", "base", "standing", "friends", "enemies", "town"], frows),
                races=races, raceCols=["name", "crush", "slash", "thrust", "body", "cold", "energy", "heat", "matter", "spirit"],
                xp=S["xp"],
                zones=wgcommon.table(["zone", "spawns", "kinds", "camps", "lmin", "lmax", "levels", "ag", "st", "guards", "keepGuards",
                                      "otherNpcs", "kindList", "safest", "densest", "group", "tags"], zrows),
                events=wgcommon.table(["id", "name", "kind", "realm", "zone", "suggested", "bosses", "objectives"], erows),
                scripted=wgcommon.table(SCRIPTED_COLS, [[k[c] for c in SCRIPTED_COLS] for k in scripted]),
                paths=wgcommon.table(["kind", "zone", "camp", "pts"], prow),
                peaceful=wgcommon.table(["name", "key", "zone", "x", "y", "z", "lmin", "lmax", "cls", "why", "scripted"], peaceful))
    campsjs = dict(meta=dict(built=meta["built"], nearDistance=NEAR_LIST, nearCols=NEAR_COLS, ptsCols=["x", "y", "lmin", "lmax", "z", "st"], stances=list(STANCE),
                             campFlagBits=meta["campFlagBits"], dirs=meta["dirs"], count=len(camps)),
                   camps=wgcommon.table(CAMP_COLS, [[c[k] for k in CAMP_COLS] for c in camps]))
    s1 = wgcommon.write_data("mobs", mobs, A.out, "%d kinds, %d zones" % (len(kinds), len(zrows)))
    s2 = wgcommon.write_data("camps", campsjs, A.out, "%d camps, %d spawn points" % (len(camps), len(M)))
    assert s1 < 15e6 and s2 < 15e6, "data file over 15 MB: split it"

    # ---- summary
    print("Mob rows %d = " % W["mob_rows"] + " + ".join("%d %s" % (sum(e[2] for e in excluded if e[0] == c), c) for c in CAT_TEXT
                                                          if any(e[0] == c for e in excluded)))
    for e in excluded:
        print("   %-13s %-18s %6d" % tuple(e))
    print("unknown class types (run as GameNPC):", dict(W["unknown_class"]))
    print("hunting data: %d rows -> %d spawn points (%d points can come up as one of two kinds), %d kinds, %d camps (%d single)"
          % (counts["monsterRows"], len(M), counts["sharedPoints"], len(kinds), len(camps), counts["singleCamps"]))
    print("level comes from a template range for %d spawn points; attack on sight %d (%.0f%%); patrol paths %d"
          % (counts["levelRangeSpawns"], counts["onSight"], 100.0 * counts["onSight"] / len(M), len(prow)))
    print("camps with an aggressive neighbour in reach %d, with a stronger one %d; near entries %d"
          % (counts["campsWithReachNeighbours"], counts["campsWithStrongerReachNeighbours"], sum(len(c["near"]) for c in camps)))
    print("stance words:", dict(zip(STANCE, stances(M))))
    print("ranks:", dict(collections.Counter(k["rank"] for k in kinds)), "| scripted kinds:", len(scripted),
          dict(collections.Counter(k["rank"] for k in scripted)))
    print("script-made spawns left out:", meta["scriptedLeftOut"])
    for c in coverage:
        print("   %-24s zones %3d  spawn points %6d  kinds %5d  camps %6d" % tuple(c))
    print("done in %.1fs" % (time.time() - t0))
    return S, W, R


if __name__ == "__main__":
    main()
