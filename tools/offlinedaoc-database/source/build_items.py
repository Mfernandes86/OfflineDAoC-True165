"""build_items.py (lane I-D): data/items.js, data/itemmeta.js and the item icon sheet icons/items.png.

Run:  python -I source/build_items.py [--db <world>] [--out <data dir>] [--no-icons]

Reads the True 1.65 world READ-ONLY through wgcommon.open_db(), the server C# source (enum values, names, rules:
parsed at build time, never copied by hand where a parse is possible) and the converted client icon sheets.
Everything written is documented in source/contract/items.md; the server rules ported here are cited as file:line
(paths relative to wgcommon.SERVER_SRC/GameServer unless stated).

Sources of an item (bit flags in column "src"):
  1  drop    a LootTemplate row the template loot generator can reach for a monster that stands in a loaded region
             (gameutils/LootGeneratorTemplate.cs:262-286: MobXLootTemplate rows by NPC template id, else by monster
             name, else the LootTemplate named after the monster; :410-425 a MobX row naming a missing table falls
             back to the monster's own table)
  2  otd     a LootOTD row (one-time drop per character) for a monster that stands in a loaded region
             (gameutils/LootGeneratorOneTimeDrop.cs:33-100); runtime/server/classic-otd.json only lists which of
             those LootOTD rows the quest builder owns, it adds none
  4  vendor  a MerchantItem row on a list used by a merchant NPC that stands in a loaded region
             (gameobjects/GameMerchant.cs:348-373: the Mob row's ItemsListTemplateID, else its NPC template's)
  8  quest   a quest reward: DataQuest Final/Optional reward templates, classic-quests.json Choices / Trades /
             SideTurnIns.Reward / CollectRewards.Items / ClassRewards variants, and the items the scripted quests
             (scripts/quests, quests/Albion: the level 50 epics and three Shrouded Isles quests) name
  16 craft   the product of a CraftedItem recipe
  32 start   given at character creation (StarterEquipment)
  64 qitem   a quest step item (handed out, collected or dropped only while on the quest:
             mode165/QuestLootGate165.cs:27-43); not a reward
  128 script on the loot table of a monster that is not a database spawn but that a server script creates in code
             (boss encounters). Which monsters those are comes from data/mobs.js, table `scripted` (lane M-D), when
             that file is there: run this builder after build_mobs.py
When data/drops.js (lane D-D) and data/vendoritems.js (lane V-D) are there, bits 1, 2 and 4 are compared with them
and every difference is printed (compare only: the order of the builders does not matter).
An item with no bit is left out and counted (see the "left" block of items.js and the builder's printout).
"""
import json, os, re, sys, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wgcommon

GS = wgcommon.SERVER_SRC + "/GameServer"
SRC_DROP, SRC_OTD, SRC_VENDOR, SRC_QUEST, SRC_CRAFT, SRC_START, SRC_QITEM, SRC_SCRIPT = 1, 2, 4, 8, 16, 32, 64, 128

# item flag bits (column "fl")
FL_NOTRADE, FL_NODROP, FL_NOPICKUP, FL_INDESTRUCTIBLE, FL_NOWEAR, FL_NOSELL, FL_QUESTGATED = 1, 2, 4, 8, 16, 32, 64
FL_NOCLASS, FL_DEADSPELL = 128, 256


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def split_csv(text):
    """Util.SplitCSV(text, true): ';' or ',' separated, ranges 'a-b' expanded. Returns a list of strings."""
    out = []
    for part in re.split(r"[;,]", text or ""):
        part = part.strip()
        if not part:
            continue
        m = re.fullmatch(r"(\d+)-(\d+)", part)
        if m:
            out.extend(str(i) for i in range(int(m.group(1)), int(m.group(2)) + 1))
        else:
            out.append(part)
    return out


# ------------------------------------------------------------------------------------------------ server source
def parse_enum(path, name):
    """A C# enum as {member: int}. Members that reference another member (A = B) are resolved."""
    text = re.sub(r"/\*.*?\*/", "", read(path), flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    text = re.sub(r"(?m)^\s*#\w+[^\n]*$", "", text)   # #region / #endregion lines
    m = re.search(r"enum\s+" + name + r"\b[^{]*\{(.*?)\}", text, re.S)
    assert m, "enum %s not found in %s" % (name, path)
    out, last = {}, -1
    for part in m.group(1).split(","):
        part = part.strip()
        if not part:
            continue
        if "=" in part:
            key, val = [s.strip() for s in part.split("=", 1)]
            val = val.split(".")[-1]
            if re.fullmatch(r"-?\d+", val):
                last = int(val)
            elif re.fullmatch(r"0x[0-9a-fA-F]+", val):
                last = int(val, 16)
            else:
                assert val in out, "enum %s: cannot resolve %s = %s" % (name, key, val)
                last = out[val]
        else:
            key, last = part, last + 1
        out[key] = last
    return out


def parse_property_names(eprop):
    """eProperty id -> display name, exactly as SkillBase.RegisterPropertyNames (gameutils/SkillBase.cs:1410+) fills
    m_propertyNames: a literal, or a translation key looked up in the server's English language file."""
    lang = {}
    for line in read(wgcommon.RUNTIME + "/server/languages/EN/SkillHandler.txt").splitlines():
        if line.startswith("#") or ":" not in line:
            continue
        k, v = line.split(":", 1)
        lang[k.strip()] = re.sub(r"\s+", " ", v).strip()
    text = read(GS + "/gameutils/SkillBase.cs")
    body = text[text.index("private static void RegisterPropertyNames()"):]
    body = body[:body.index("#endregion")]
    out = {}
    for m in re.finditer(r'm_propertyNames\.Add\(eProperty\.(\w+),\s*(?:LanguageMgr\.GetTranslation\([^,]+,\s*"([^"]+)"\)|"([^"]*)")\)', body, re.S):
        prop, key, literal = m.group(1), m.group(2), m.group(3)
        name = lang.get(key) if key else literal
        if name is None:
            # the English language file has no such line (the server would show the key itself): leave the
            # property unnamed here; main() names it after its eProperty member and reports it
            continue
        name = re.sub(r"^- ", "", name)
        name = re.sub(r":? \{0\}.*$", "", name)   # "- To-hit bonus: {0}% (PvE Only)" -> "To-hit bonus"
        out[eprop[prop]] = name
    assert len(out) > 150, "property names: parsed only %d" % len(out)
    return out


def parse_specs():
    """Specs constants (gameutils/SkillConstants.cs:467+), spec key -> skill property and -> focus property
    (gameutils/SkillBase.cs:1009-1166)."""
    text = read(GS + "/gameutils/SkillConstants.cs")
    body = text[text.index("public abstract class Specs"):]
    consts = dict(re.findall(r'public const string (\w+)\s*=\s*"([^"]*)"', body))
    sb = read(GS + "/gameutils/SkillBase.cs")
    to_skill = {consts[a]: b for a, b in re.findall(r"m_specToSkill\.Add\(Specs\.(\w+),\s*eProperty\.(\w+)\)", sb)}
    to_focus = {consts[a]: b for a, b in re.findall(r"m_specToFocus\.Add\(Specs\.(\w+),\s*eProperty\.(\w+)\)", sb)}
    assert len(to_skill) > 90 and len(to_focus) > 20
    return to_skill, to_focus


def parse_obj_skill(eobj, eprop, old_archery):
    """Object_Type -> the skill property its weapon specialization raises: SkillBase.ObjectTypeToSpec
    (gameutils/SkillBase.cs:934-1004; the bows depend on ServerProperty allow_old_archery) then SpecToSkill."""
    text = read(GS + "/gameutils/SkillConstants.cs")
    consts = dict(re.findall(r'public const string (\w+)\s*=\s*"([^"]*)"', text[text.index("public abstract class Specs"):]))
    to_skill, _ = parse_specs()
    sb = read(GS + "/gameutils/SkillBase.cs")
    body = sb[sb.index("private static void InitializeObjectTypeToSpec()"):sb.index("private static void InitializeSpecToSkill()")]
    out = {}
    for obj, spec in re.findall(r"m_objectTypeToSpec\.Add\(eObjectType\.(\w+),\s*Specs\.(\w+)\)", body):
        if obj in ("Longbow", "CompositeBow", "RecurvedBow") and (spec == "Archery") == old_archery:
            continue
        key = consts[spec]
        if key in to_skill:
            assert eobj[obj] not in out, obj
            out[eobj[obj]] = eprop[to_skill[key]]
    assert out[eobj["SlashingWeapon"]] == eprop["Skill_Slashing"] and out[eobj["Shield"]] == eprop["Skill_Shields"]
    assert eobj["Longbow"] in out and eobj["RecurvedBow"] in out
    return out


def parse_classes(eclass, eprop):
    """Every player class script (playerclasses/*/Class*.cs): id, name, base class name, stats (inherited from the
    parent script when the class does not set them), left-hand weapons."""
    scripts = {}
    for folder in ("albion", "midgard", "hibernia", "base"):
        d = GS + "/playerclasses/" + folder
        for fn in sorted(os.listdir(d)):
            if not fn.endswith(".cs"):
                continue
            text = read(d + "/" + fn)
            m = re.search(r'\[CharacterClass(?:Attribute)?\(\(int\)\s*eCharacterClass\.(\w+),\s*"([^"]+)",\s*"([^"]+)"', text)
            c = re.search(r"public class (\w+)\s*:\s*(\w+)", text)
            assert m and c, "class script not understood: " + fn
            stats = {k: v for k, v in re.findall(r"m_(primaryStat|secondaryStat|tertiaryStat|manaStat)\s*=\s*eStat\.(\w+)", text)}
            lh = re.search(r"override bool CanUseLefthandedWeapon\s*(?:=>\s*(true|false)|\{\s*get\s*\{\s*return\s*(true|false))", text)
            scripts[c.group(1)] = dict(file="playerclasses/%s/%s" % (folder, fn), folder=folder, parent=c.group(2),
                                       enum=m.group(1), id=eclass[m.group(1)], name=m.group(2), base=m.group(3),
                                       stats=stats, left=(lh.group(1) or lh.group(2)) == "true" if lh else None)
    estat = {"UNDEFINED": 0, "STR": eprop["Strength"], "DEX": eprop["Dexterity"], "CON": eprop["Constitution"],
             "QUI": eprop["Quickness"], "INT": eprop["Intelligence"], "PIE": eprop["Piety"], "EMP": eprop["Empathy"],
             "CHR": eprop["Charisma"], "ACU": eprop["Acuity"]}   # Enums/eStat.cs:25-39

    def inherited(script, key):
        while script:
            if key in script["stats"]:
                return estat[script["stats"][key]]
            script = scripts.get(script["parent"])
        return 0

    def left(script):
        while script:
            if script["left"] is not None:
                return script["left"]
            script = scripts.get(script["parent"])
        return False   # gameobjects/CharacterClasses/CharacterClassBase.cs:290 default

    out = {}
    for s in scripts.values():
        # the base class is the parent class script (ClassArmsman : ClassFighter); the attribute's third value is only
        # a display name ("Rogue" names both the Albion and the Midgard rogue)
        parent = scripts.get(s["parent"])
        assert parent is not None or s["parent"] == "CharacterClassBase", s["parent"]
        out[s["id"]] = dict(id=s["id"], name=s["name"], baseId=parent["id"] if parent else s["id"], file=s["file"], folder=s["folder"],
                            primary=inherited(s, "primaryStat"), secondary=inherited(s, "secondaryStat"),
                            tertiary=inherited(s, "tertiaryStat"), mana=inherited(s, "manaStat"), leftHand=left(s))
    return out


def parse_class_realms(eclass):
    """Realm of each class: the realm -> classes dictionary in GlobalConstants.cs (around :938-1008)."""
    text = read(GS + "/GlobalConstants.cs")
    text = re.sub(r"//[^\n]*", "", text)
    out = {}
    for realm, rid in (("Albion", 1), ("Midgard", 2), ("Hibernia", 3)):
        m = re.search(r"\{eRealm\." + realm + r",\s*new List<eCharacterClass>\(\)\s*\{(.*?)\}\}", text, re.S)
        assert m, "class list for " + realm
        for name in re.findall(r"eCharacterClass\.(\w+)", m.group(1)):
            out.setdefault(eclass[name], set()).add(rid)
    return out


# The ability an item type needs, transcribed from serverrules/AbstractServerRules.cs:649-926
# (CheckAbilityToUseItem) for allow_cross_realm_items = False. Any ONE of the listed abilities is enough.
WEAPON_ABILITY = {
    8: ["Weaponry: Staves"], 5: ["Weaponry: Shortbows"], 27: ["Weaponry: Fist Wraps"], 28: ["Weaponry: Mauler Staff"],
    2: ["Weaponry: Crushing"], 3: ["Weaponry: Slashing"], 4: ["Weaponry: Thrusting"], 6: ["Weaponry: Two Handed"],
    7: ["Weaponry: Polearms"], 9: ["Weaponry: Longbows", "Weaponry: Archery"], 10: ["Weaponry: Crossbow"],
    24: ["Weaponry: Flexible"], 11: ["Weaponry: Swords"], 12: ["Weaponry: Hammers"], 13: ["Weaponry: Axes"],
    17: ["Weaponry: Axes"], 14: ["Weaponry: Spears"], 15: ["Weaponry: Composite Bows", "Weaponry: Archery"],
    16: ["Weaponry: Thrown"], 25: ["Weaponry: Hand to Hand"], 18: ["Weaponry: Recurved Bows", "Weaponry: Archery"],
    19: ["Weaponry: Blades"], 20: ["Weaponry: Blunt"], 21: ["Weaponry: Piercing"], 22: ["Weaponry: Large Weapons"],
    23: ["Weaponry: Celtic Spears"], 26: ["Weaponry: Scythe"],
    44: ["Weaponry: Crossbow"],                                                              # bolts
    43: ["Weaponry: Composite Bows", "Weaponry: Longbows", "Weaponry: Recurved Bows", "Weaponry: Shortbows"],  # arrows
    45: ["Weaponry: Instruments"],
}
ARMOR_LEVEL = {31: 0, 32: 1, 33: 2, 37: 3, 34: 3, 38: 4, 35: 4, 36: 5}   # GlobalConstants.cs:47-57 (ArmorLevel)
ARMOR_ABILITY = {1: "AlbArmor", 2: "MidArmor", 3: "HibArmor"}             # by ITEM realm (AbstractServerRules.cs:696-706)


def check_rule_source():
    """Fail if the server rule this file transcribes no longer reads the way it did."""
    text = read(GS + "/serverrules/AbstractServerRules.cs")
    for needle in ("case eObjectType.Staff: abilityCheck = Abilities.Weapon_Staves",
                   "case eObjectType.Shield: return living.GetAbilityLevel(Abilities.Shield) >= Math.Max(1, item.Type_Damage)",
                   "case eObjectType.Poison: return living.GetModifiedSpecLevel(Specs.Envenom) > 0",
                   "case eObjectType.Instrument: return living.HasAbility(Abilities.Weapon_Instruments)",
                   "case eObjectType.LeftAxe:\n                case eObjectType.Axe:",
                   "otherCheck = new string[] { Abilities.Weapon_Longbows, Abilities.Weapon_Archery }",
                   "case eObjectType.Magical: return true;"):
        assert needle.replace("\n", "\r\n") in text or needle in text, "AbstractServerRules.cs changed: " + needle
    consts = dict(re.findall(r'public const string (\w+)\s*=\s*"([^"]*)"', read(GS + "/gameutils/SkillConstants.cs")))
    for key in ("Weapon_Staves", "Weapon_Crossbow", "Weapon_CelticSpear", "Weapon_Instruments", "Shield", "AlbArmor"):
        assert key in consts
    used = {a for v in WEAPON_ABILITY.values() for a in v} | set(ARMOR_ABILITY.values()) | {"Shield"}
    assert used <= set(consts.values()), "ability keys not in SkillConstants.cs: %s" % (used - set(consts.values()))
    gp = read(GS + "/gameobjects/GamePlayer.cs")
    assert "double dpsCap = 1.2 + 0.3 * Level;" in gp                                   # GamePlayer.cs:5725
    assert "itemArmorFactorCap = type is eObjectType.Cloth ? modifiedCharacterLevel : playerArmorFactorCap;" in gp  # :5628
    gl = read(GS + "/gameobjects/GameLiving.cs")
    assert "double procChance = (weapon.ProcChance > 0 ? weapon.ProcChance : 10) * (weapon.SPD_ABS / 35.0) * 0.01;" in gl  # :785
    assert "int chance = armor.ProcChance > 0 ? armor.ProcChance : 10;" in gl             # :907
    ex = read(GS + "/mode165/Exchange165.cs")
    assert ">= 100 => 2.475, 99 => 1.6, 98 => 1.35, 97 => 1.15, 96 => 1.0, 95 => 0.85, 94 => 0.75, _ => 0.6," in ex  # :70
    iv = read(GS + "/bots/autonomous/ItemValue165.cs")
    assert "double baseGold = 200 * curve * Exchange165.SlotFactor(item);" in iv          # :109
    assert "double utility = 1 + 0.5 * Math.Min(Utility(item), uCap) / uCap;" in iv       # :112


# ------------------------------------------------------------------------------------------------ classes
def build_classes(con, eprop, eclass, props):
    spec_to_skill, spec_to_focus = parse_specs()
    scripts = parse_classes(eclass, eprop)
    realms = parse_class_realms(eclass)
    disabled_raw = con.execute("select Value from ServerProperty where Key='disabled_classes'").fetchone()[0]
    disabled = {int(x) for x in split_csv(disabled_raw)}
    # Sluaghbinder (63): an experimental class that cannot be created any more
    # (packets/Client/168/CharacterCreateRequestHandler.cs:429-436 "the Sluaghbinder is discontinued").
    discontinued = {eclass["Sluaghbinder"]}
    cc = read(GS + "/packets/Client/168/CharacterCreateRequestHandler.cs")
    assert "the Sluaghbinder is discontinued" in cc

    spec_impl = {r["KeyName"].lower(): (r["Implementation"] or "") for r in con.execute("select KeyName, Implementation from Specialization")}
    spec_name = {r["KeyName"].lower(): r["Name"] for r in con.execute("select KeyName, Name from Specialization")}
    class_specs = {}
    for r in con.execute("select ClassID, SpecKeyName, LevelAcquired from ClassXSpecialization"):
        class_specs.setdefault(r["ClassID"], []).append((r["SpecKeyName"], r["LevelAcquired"]))
    spec_abil = {}
    for r in con.execute("select Spec, SpecLevel, AbilityKey, AbilityLevel, ClassId from SpecXAbility"):
        spec_abil.setdefault(r["Spec"].lower(), []).append((r["SpecLevel"], r["AbilityKey"], r["AbilityLevel"], r["ClassId"]))

    def is_career(key):
        return "Career" in spec_impl.get(key.lower(), "") or "RealmAbilities" in spec_impl.get(key.lower(), "")

    def abilities(cid, level):
        """Ability key -> level for a character of this class at this level, computed fresh as at login:
        every career specialization of the class (ClassXSpecialization rows of the class and of class 0) counts as
        trained to the character's level (gameutils/Specialization.cs:554-557); per specialization and ability the
        row with the highest SpecLevel not above that level is the one granted (Specialization.cs:366-381); across
        specializations the highest ability level is kept (gameobjects/GamePlayer.cs:3497-3504)."""
        out = {}
        for key, acquired in class_specs.get(cid, []) + class_specs.get(0, []):
            if acquired > level or not is_career(key):
                continue
            best = {}
            for spec_level, akey, alevel, only in spec_abil.get(key.lower(), []):
                if only not in (0, cid) or spec_level > level:
                    continue
                if akey not in best or spec_level > best[akey][0] or (spec_level == best[akey][0] and alevel > best[akey][1]):
                    best[akey] = (spec_level, alevel)
            for akey, (_, alevel) in best.items():
                if akey not in out or alevel > out[akey]:
                    out[akey] = alevel
        return out

    classes, notes = [], []
    for cid in sorted(scripts):
        s = scripts[cid]
        if cid in disabled or cid in discontinued:
            continue
        base_id = s["baseId"]
        is_base = base_id == cid
        rl = realms.get(cid, set())
        if s["folder"] != "base":
            folder_realm = {"albion": 1, "midgard": 2, "hibernia": 3}[s["folder"]]
            assert rl == {folder_realm}, "realm of %s: list %s, folder %s" % (s["name"], rl, s["folder"])
        elif s["name"] == "Acolyte":
            rl = {1}   # GlobalConstants also lists Acolyte under Hibernia for the discontinued Sluaghbinder novice path
        assert len(rl) == 1, "realm of %s: %s" % (s["name"], rl)
        realm = next(iter(rl))

        # Usability by level. A character plays levels 1-4 as its base class (ServerProperty start_as_base_class =
        # True; advancing at the trainer is possible from level 5), so below 5 the base class's table is used.
        def abil_at(level):
            return abilities(base_id if (not is_base and level < 5) else cid, level)
        per_level = {lv: abil_at(lv) for lv in range(1, 51)}

        def first_level(test):
            got = [lv for lv in range(1, 51) if test(per_level[lv])]
            if not got:
                return 0
            if got != list(range(got[0], 51)):
                notes.append("%s: an ability is lost again at a later level (levels %s)" % (s["name"], got))
            return got[0]

        armor = []
        for obj in (32, 33, 34, 37, 35, 38, 36):
            # armour of the class's own realm; realm-less armour uses the best of the three abilities (:701-705)
            lv = first_level(lambda a, o=obj: a.get(ARMOR_ABILITY[realm], -1) >= ARMOR_LEVEL[o])
            if lv:
                armor.append([obj, lv])
        weapons = []
        for obj in sorted(WEAPON_ABILITY):
            lv = first_level(lambda a, o=obj: any(k in a for k in WEAPON_ABILITY[o]))
            if lv:
                weapons.append([obj, lv])
        shield = []
        for size in (1, 2, 3):
            lv = first_level(lambda a, z=size: a.get("Shield", 0) >= z)
            if lv:
                shield.append([size, lv])

        # trainable lines: the class's own ClassXSpecialization rows that are not careers
        lines, skills, focus, unmapped = [], [], [], []
        base_lines = {k.lower() for k, _ in class_specs.get(base_id, [])}
        envenom = 0
        for key, acquired in sorted(class_specs.get(cid, []), key=lambda t: (t[1], t[0])):
            if is_career(key):
                continue
            lv = max(1, acquired)
            if not is_base and key.lower() not in base_lines:
                lv = max(lv, 5)   # a line only the advanced class has arrives when the character advances (level 5)
            canon = next((k for k in spec_to_skill if k.lower() == key.lower()), None)
            if canon is None:
                unmapped.append(key)
            else:
                skills.append(eprop[spec_to_skill[canon]])
            fcanon = next((k for k in spec_to_focus if k.lower() == key.lower()), None)
            if fcanon:
                focus.append(eprop[spec_to_focus[fcanon]])
            lines.append([spec_name.get(key.lower(), key), lv])
            if key.lower() == "envenom":
                envenom = lv
        if unmapped:
            notes.append("%s: spec lines with no skill property: %s" % (s["name"], ", ".join(unmapped)))
        if envenom:
            weapons.append([46, envenom])   # poison: Envenom spec above 0 (AbstractServerRules.cs:896); 1 point at the level the line arrives
        classes.append(dict(id=cid, name=s["name"], realm=realm, base=base_id, isBase=is_base,
                            armor=armor, weapons=sorted(weapons), shield=shield, shieldMax=max([z for z, _ in shield] or [0]),
                            leftHand=s["leftHand"], primary=s["primary"], secondary=s["secondary"],
                            tertiary=s["tertiary"], mana=s["mana"], skills=sorted(set(skills)), focus=sorted(set(focus)),
                            lines=lines, src=s["file"]))
    return classes, sorted(disabled), sorted(discontinued), notes


# ------------------------------------------------------------------------------------------------ world: sources
def loaded_regions(con):
    """Regions the server loads (world/Region.cs:234-264): not in disabled_regions, expansion not disabled."""
    prop = {r["Key"]: r["Value"] for r in con.execute("select Key, Value from ServerProperty where Key in ('disabled_regions','disabled_expansions')")}
    dis_regions = set(split_csv(prop.get("disabled_regions", "")))
    dis_exp = set(split_csv(prop.get("disabled_expansions", "")))
    out = set()
    for r in con.execute("select RegionID, Expansion from Regions"):
        if str(r["RegionID"]) in dis_regions or str(r["Expansion"] + 1) in dis_exp:
            continue
        out.add(r["RegionID"])
    return out


SERVER_SCAN = None


def scan_server():
    """One pass over the server's C# (text only): the classes that derive from GameMerchant (full names), the NPC
    guild script table {(guild, realm): class full name} ([NPCGuildScript("Smith", eRealm.Albion)] on a class;
    a realm-less attribute serves every realm), and the string literals of the scripts that create monsters in
    code (scripts/, ai/): used to tell a script-summoned monster from a loot table nothing in the world uses."""
    global SERVER_SCAN
    if SERVER_SCAN is not None:
        return SERVER_SCAN
    parents, full_name, guilds, literals = {}, {}, {}, set()
    realm_id = {"None": 0, "Albion": 1, "Midgard": 2, "Hibernia": 3}
    files = 0
    for dp, dn, fns in os.walk(GS):
        dn[:] = [d for d in dn if d not in ("obj", "bin")]
        rel = os.path.relpath(dp, GS).replace("\\", "/")
        for fn in fns:
            if not fn.endswith(".cs"):
                continue
            files += 1
            text = read(os.path.join(dp, fn))
            code = re.sub(r"(?m)^\s*//.*$", "", text)
            ns = re.search(r"namespace\s+([\w.]+)", code)
            ns = ns.group(1) + "." if ns else ""
            for m in re.finditer(r"class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([\w.]+)", code):
                parents.setdefault(m.group(1), m.group(2).split(".")[-1])
            for m in re.finditer(r"class\s+(\w+)", code):
                full_name.setdefault(m.group(1), ns + m.group(1))
            if "NPCGuildScript(" in code:
                for m in re.finditer(r'((?:\[\s*NPCGuildScript\(\s*"[^"]*"\s*(?:,\s*eRealm\.\w+\s*)?\)\s*\]\s*)+)(?:\[[^\]]*\]\s*)*(?:public\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)', code):
                    for g in re.finditer(r'NPCGuildScript\(\s*"([^"]*)"\s*(?:,\s*eRealm\.(\w+)\s*)?\)', m.group(1)):
                        rid = realm_id.get(g.group(2) or "None", 0)
                        for r in ((0, 1, 2, 3) if rid == 0 else (rid,)):   # ScriptMgr: a realm-less script serves all
                            guilds[(g.group(1).lower(), r)] = ns + m.group(2)
            if rel.startswith("scripts") or rel.startswith("ai"):
                for m in re.finditer(r'"([^"\n]{3,60})"', text):
                    literals.add(m.group(1).lower())
    assert files > 1500, "server source not found under " + GS
    merchants = set()
    for name in full_name:
        p, seen = name, set()
        while p and p not in seen and p != "GameMerchant":
            seen.add(p)
            p = parents.get(p)
        if p == "GameMerchant":
            merchants.add(full_name[name].lower())
    assert "dol.gs.gamemerchant" in merchants and "dol.gs.gamestablemaster" in merchants
    assert "dol.gs.gamehastener" not in merchants
    assert guilds.get(("stable master", 1), "").endswith("GameStableMaster"), "guild script table not parsed"
    SERVER_SCAN = (merchants, guilds, literals)
    return SERVER_SCAN


def collect_sources(con, item_ids, out_dir):
    """item id (lower) -> source bits, plus the detail maps the pages show. item_ids: lower id -> real Id_nb."""
    src, stats = {}, {}

    def add(item, bit):
        k = (item or "").strip().lower()
        if k in item_ids:
            src[k] = src.get(k, 0) | bit
            return True
        return False

    regions = loaded_regions(con)
    stats["regionsLoaded"] = len(regions)
    merchant_classes, guild_scripts, script_literals = scan_server()
    use_guild_scripts = str(con.execute("select Value from ServerProperty where Key='use_npcguildscripts'").fetchone()[0]).lower() == "true"

    # ---- monsters that stand in the world, by lower-case RUNTIME name; the NPC template ids they use.
    # The name is the Mob row's, replaced by the NPC template's when the template has ReplaceMobValues
    # (gameobjects/GameNPC.cs:1077, 1180, 1341-1345); several templates can share an id, each one counts.
    # The class the server creates (world/Region.cs:745-772): the NPC guild script of the row's Guild, else the
    # template's ClassType when it replaces mob values and is not plain GameNPC, else the Mob row's ClassType.
    templates = {}
    for r in con.execute("select TemplateId, Name, ClassType, ItemsListTemplateID, ReplaceMobValues from NpcTemplate"):
        templates.setdefault(r["TemplateId"], []).append(r)
    live_names, live_templates, spawn_rows, skipped, renamed = set(), set(), 0, 0, 0
    merchant_lists, merchant_rows = set(), 0
    for r in con.execute("select Name, Region, Realm, Guild, NPCTemplateID, ClassType, ItemsListTemplateID from Mob"):
        if r["Region"] not in regions:
            skipped += 1
            continue
        spawn_rows += 1
        tpls = templates.get(r["NPCTemplateID"], []) if r["NPCTemplateID"] not in (None, -1) else []
        replacing = [t for t in tpls if t["ReplaceMobValues"]]
        row_names = {(t["Name"] or "").lower() for t in replacing}
        if not replacing or len(replacing) < len(tpls):
            row_names.add((r["Name"] or "").lower())
        if row_names != {(r["Name"] or "").lower()}:
            renamed += 1
        live_names |= row_names
        if tpls:
            live_templates.add(str(r["NPCTemplateID"]))
        cls = None
        if use_guild_scripts and r["Guild"] and 0 <= r["Realm"] <= 3:
            cls = guild_scripts.get((r["Guild"].lower(), r["Realm"]))
        if cls is None:
            t = tpls[0] if tpls else None
            if t is not None and t["ClassType"] and t["ClassType"] != "DOL.GS.GameNPC" and t["ReplaceMobValues"]:
                cls = t["ClassType"]
            else:
                cls = r["ClassType"] or "DOL.GS.GameNPC"
        if cls.lower() in merchant_classes:
            merchant_rows += 1
            lst = r["ItemsListTemplateID"] or next((t["ItemsListTemplateID"] for t in tpls if t["ItemsListTemplateID"]), "")
            if lst:
                merchant_lists.add(lst.lower())
    stats["spawnRows"], stats["spawnRowsInUnloadedRegions"], stats["monsterNames"] = spawn_rows, skipped, len(live_names)
    stats["spawnRowsRenamedByTemplate"], stats["merchantRows"] = renamed, merchant_rows

    # monsters a classic quest step spawns (classic-quests.json "Spawn": Name) also die and roll loot by name
    quests_json = json.loads(read(wgcommon.RUNTIME + "/server/classic-quests.json"))
    quest_spawn_names = set()

    def walk_spawns(o):
        if isinstance(o, dict):
            sp = o.get("Spawn")
            if isinstance(sp, dict) and sp.get("Name"):
                quest_spawn_names.add(sp["Name"].lower())
            for v in o.values():
                walk_spawns(v)
        elif isinstance(o, list):
            for v in o:
                walk_spawns(v)
    walk_spawns(quests_json.get("Quests", {}))
    stats["questSpawnNames"] = len(quest_spawn_names - live_names)
    names = live_names | quest_spawn_names

    # ---- template loot
    loot = {}
    for r in con.execute("select TemplateName, ItemTemplateID from LootTemplate"):
        loot.setdefault(r["TemplateName"].lower(), []).append(r["ItemTemplateID"])
    mobx = {}
    for r in con.execute("select MobName, LootTemplateName from MobXLootTemplate"):
        mobx.setdefault(r["MobName"].lower(), []).append((r["LootTemplateName"] or r["MobName"]).lower())

    def tables_of(key, by_name):
        got = set()
        if key in mobx:
            own_row = key in mobx[key]
            for t in mobx[key]:
                if t not in loot:
                    if own_row:
                        continue
                    t = key
                if t in loot:
                    got.add(t)
        elif by_name and key in loot:
            got.add(key)
        return got
    reached, missing_items = set(), set()
    for key in names:
        reached |= tables_of(key, True)
    for key in live_templates:
        reached |= tables_of(key, False)
    for t in reached:
        for item in loot.get(t, ()):
            if not add(item, SRC_DROP):
                missing_items.add(item)
    # Loot tables no placed monster uses. A monster a server script, an encounter file or a quest creates in code
    # still rolls its table when killed: its items get the "script" bit; the rest belong to monsters that are not
    # in the world. WHICH monsters the scripts really create is lane M-D's finding (data/mobs.js, table `scripted`:
    # it follows each script to the world row or script that starts it; source/contract/mobs.md section 5d), and
    # that list is used when the file is there. Without it this builder falls back to a weaker test of its own (the
    # monster's name is a string in GameServer/scripts or GameServer/ai) and says so.
    mobs_js = load_data(out_dir, "mobs")
    script_names, script_from = None, "own scan of the server scripts (data/mobs.js was not there)"
    if mobs_js is not None and isinstance(mobs_js.get("scripted"), dict) and "key" in mobs_js["scripted"].get("cols", []):
        ki = mobs_js["scripted"]["cols"].index("key")
        script_names = {row[ki].lower() for row in mobs_js["scripted"]["rows"]}
        script_from = "data/mobs.js, table scripted (%d script-made monsters)" % len(script_names)
    script_tables, dead_tables = [], []
    for key in sorted(set(mobx) | set(loot)):
        if key in names or key in live_templates:
            continue
        tabs = tables_of(key, True) - reached
        if not tabs:
            continue
        if (key in script_names) if script_names is not None else (key in script_literals):
            script_tables.append(key)
            for t in tabs:
                for item in loot[t]:
                    add(item, SRC_SCRIPT)
        else:
            dead_tables.append(key)
    stats["scriptFrom"] = script_from
    stats["lootTemplates"], stats["lootTemplatesReached"] = len(loot), len(reached)
    stats["lootRowsToMissingItems"] = len(missing_items)
    stats["scriptMonsterTables"], stats["absentMonsterTables"] = script_tables, dead_tables

    # ---- one-time drops
    otd_rows = otd_live = 0
    for r in con.execute("select MobName, ItemTemplateID from LootOTD"):
        otd_rows += 1
        if r["MobName"].lower() in names:
            otd_live += add(r["ItemTemplateID"], SRC_OTD)
    stats["otdRows"], stats["otdRowsReached"] = otd_rows, otd_live

    # ---- merchants
    all_lists, sold_rows = set(), 0
    for r in con.execute("select ItemListID, ItemTemplateID from MerchantItem"):
        all_lists.add(r["ItemListID"].lower())
        if r["ItemListID"].lower() in merchant_lists:
            sold_rows += add(r["ItemTemplateID"], SRC_VENDOR)
    stats["merchantLists"], stats["merchantListsInWorld"] = len(all_lists), len(merchant_lists & all_lists)

    # ---- crafting
    craft = {}
    recipes = {}
    for r in con.execute("select CraftedItemID, Id_nb, CraftingLevel, CraftingSkillType from CraftedItem"):
        if add(r["Id_nb"], SRC_CRAFT):
            recipes[r["CraftedItemID"]] = r
    mats = {}
    for r in con.execute("select CraftedItemId_nb, IngredientId_nb, Count from CraftedXItem"):
        mats.setdefault(r["CraftedItemId_nb"], []).append((r["IngredientId_nb"], r["Count"]))
    stats["recipes"] = len(recipes)
    for rid, r in recipes.items():
        k = r["Id_nb"].lower()
        entry = [r["CraftingSkillType"], r["CraftingLevel"], [[item_ids.get(i.lower(), i), n] for i, n in sorted(mats.get(rid, mats.get(r["Id_nb"], [])))]]
        if k not in craft or entry[1] < craft[k][1]:
            craft[k] = entry

    # ---- starter equipment
    for r in con.execute("select TemplateID from StarterEquipment"):
        add(r["TemplateID"], SRC_START)

    # ---- quests
    quest_of = {}   # item -> set of (name, minLevel, giver, role)

    def qadd(item, bit, name, level, giver, role):
        if add(item, bit):
            quest_of.setdefault(item.strip().lower(), set()).add((name, level, giver or "", role))

    def tokens(text):
        return [t.strip() for t in re.split(r"[|;]+", text or "") if t.strip() and not t.strip().isdigit()]

    dq = {}
    for r in con.execute("select ID, Name, StartName, MinLevel, OptionalRewardItemTemplates, FinalRewardItemTemplates, StepItemTemplates, CollectItemTemplate, SourceName from DataQuest"):
        dq[r["ID"]] = r
        for t in tokens(r["FinalRewardItemTemplates"]):
            qadd(t, SRC_QUEST, r["Name"], r["MinLevel"], r["StartName"], "reward")
        for t in tokens(r["OptionalRewardItemTemplates"]):
            qadd(t, SRC_QUEST, r["Name"], r["MinLevel"], r["StartName"], "choice")
        for col in ("StepItemTemplates", "CollectItemTemplate"):
            for t in tokens(r[col]):
                qadd(t, SRC_QITEM, r["Name"], r["MinLevel"], r["StartName"], "step")
    stats["dataQuests"] = len(dq)

    def walk_quest(o, q):
        name, level, giver = (q["Name"], q["MinLevel"], q["StartName"]) if q is not None else ("", 0, "")
        if isinstance(o, dict):
            for k, v in o.items():
                if k == "Choices" and isinstance(v, dict):
                    for item in v.values():
                        qadd(item, SRC_QUEST, name, level, giver, "choice")
                elif k == "Trades" and isinstance(v, dict):
                    for give, get in v.items():
                        qadd(give, SRC_QITEM, name, level, giver, "step")
                        if get:
                            qadd(get, SRC_QUEST, name, level, giver, "trade")
                elif k == "Reward" and isinstance(v, str):
                    qadd(v, SRC_QUEST, name, level, giver, "reward")
                elif k == "CollectRewards" and isinstance(v, dict):
                    for item in v.get("Items", []):
                        qadd(item, SRC_QUEST, name, level, giver, "random reward")
                elif k == "Grant" and isinstance(v, list):
                    for item in v:
                        qadd(item, SRC_QITEM, name, level, giver, "step")
                elif k in ("Item", "NeedsItem") and isinstance(v, str):
                    qadd(v, SRC_QITEM, name, level, giver, "step")
                walk_quest(v, q)
        elif isinstance(o, list):
            for v in o:
                walk_quest(v, q)
    for qid, entry in quests_json.get("Quests", {}).items():
        walk_quest(entry, dq.get(int(qid)) if qid.isdigit() else None)
    # class versions of a reward (quests/QuestsMgr/ClassicQuests.cs:395-402): each variant is a reward of the same quests
    for base_item, by_class in quests_json.get("ClassRewards", {}).items():
        for variant in sorted(set(by_class.values())):
            for name, level, giver, role in sorted(quest_of.get(base_item.lower(), {("", 0, "", "reward")})):
                qadd(variant, SRC_QUEST, name, level, giver, role if role != "step" else "reward")
    # scripted quests: the level 50 epics and the Shrouded Isles scripts name their items in code
    # (X = GameServer.Database.FindObjectByKey<DbItemTemplate>("id")). What FinishQuest hands out with GiveItem is a
    # reward (the epic armour: variables named ...Epic...); every other item the script names is a step item.
    # The id is a string, or a constant of the same file (const string wolfPeltCloak = "wolf_pelt_cloak").
    rx = re.compile(r'(\w+)\s*=\s*(?:\(DbItemTemplate\)\s*)?GameServer\.Database\.FindObjectByKey<DbItemTemplate>\(\s*(?:"([^"]+)"|(\w+))\s*\)')
    scripted = scripted_rewards = 0
    for root in (GS + "/scripts/quests", GS + "/quests"):
        for dp, dn, fns in os.walk(root):
            for fn in sorted(fns):
                if not fn.endswith(".cs") or "Sluaghbinder" in fn or "QuestsMgr" in dp or os.sep + "Tasks" in dp:
                    continue
                text = read(os.path.join(dp, fn))
                consts = dict(re.findall(r'const\s+string\s+(\w+)\s*=\s*"([^"]*)"', text))
                found = {}
                for var, literal, ident in rx.findall(text):
                    item = literal or consts.get(ident)
                    if item:
                        found.setdefault(var, item)
                if not found:
                    continue
                title = re.search(r'questTitle\s*=\s*"([^"]+)"', text)
                lvl = re.search(r"(?:minimumLevel|MIN_LEVEL)\s*=\s*(\d+)", text)
                # the body of FinishQuest, by matching braces
                fq = text.find("void FinishQuest()")
                body = ""
                if fq >= 0:
                    start = text.find("{", fq)
                    depth, pos = 0, start
                    while start >= 0 and pos < len(text):
                        if text[pos] == "{":
                            depth += 1
                        elif text[pos] == "}":
                            depth -= 1
                            if depth == 0:
                                break
                        pos += 1
                    body = text[start:pos]
                # GiveItem(player, item) or GiveItem(npc, player, item): the item is the last argument
                given = {args.split(",")[-1].strip() for args in re.findall(r"GiveItem\(([^()]*)\)", body)}
                for var, item in found.items():
                    scripted += 1
                    is_reward = var in given or "Epic" in var
                    scripted_rewards += is_reward
                    qadd(item, SRC_QUEST if is_reward else SRC_QITEM, title.group(1) if title else fn[:-3],
                         int(lvl.group(1)) if lvl else 0, "", "scripted quest" if is_reward else "step")
    stats["scriptedQuestItemRefs"], stats["scriptedQuestRewards"] = scripted, scripted_rewards
    assert scripted_rewards > 200, "scripted quest rewards: only %d found" % scripted_rewards

    # a quest step item that is also a real reward / drop keeps both bits; QITEM alone means "only during the quest"
    return src, dict(craft=craft, quest_of=quest_of, merchant_lists=merchant_lists, all_merchant_items=None), stats


# ------------------------------------------------------------------------------------------------ items
WEAPON_TYPES = set(range(1, 29))
ARMOR_TYPES = set(range(31, 39))
RANGED_TYPES = {5, 9, 10, 15, 18}
JEWEL_SLOTS = {24, 29, 32, 33, 34, 35, 36, 37}


def category(obj, slot):
    if obj in WEAPON_TYPES:
        return "weapon"
    if obj in ARMOR_TYPES:
        return "armor"
    if obj == 42:
        return "shield"
    if obj == 45:
        return "instrument"
    if obj == 41:
        if slot in JEWEL_SLOTS:
            return "jewelry"
        if slot == 26:
            return "cloak"
        return "magical"
    if obj in (43, 44):
        return "ammo"
    if obj == 46:
        return "poison"
    if obj == 47:
        return "tincture"
    if obj == 48:
        return "gem"
    if 49 <= obj <= 71:
        return "housing"
    if 80 <= obj <= 84:
        return "siege"
    return "other"


def shows_description(text):
    """mode165/ItemTooltip165.cs:16-27 (ShowsDescription)."""
    if not text or not text.strip():
        return False
    t = text.strip()
    low = t.lower()
    return not (low == "non public" or low == "(null)" or t.startswith("Atlas ROG") or
                (low.startswith("classic level 50") and "class template" in low) or t.startswith("Loot gem (period"))


QUALITY_FACTOR = lambda q: 2.475 if q >= 100 else {99: 1.6, 98: 1.35, 97: 1.15, 96: 1.0, 95: 0.85, 94: 0.75}.get(q, 0.6)   # mode165/Exchange165.cs:68-71
SLOT_FACTOR = {25: 1.0, 27: 0.85, 21: 0.7, 28: 0.7, 22: 0.6, 23: 0.6, 12: 1.1, 10: 0.9, 11: 0.9, 13: 0.9, 26: 0.5}      # Exchange165.cs:74-84, others 0.6


def csharp_round(x):
    """Math.Round(double): to nearest, ties to even (the C# default), as Python's round() does."""
    return int(round(x))


def value_weights(eprop):
    """ItemValue165.BuildWeights (bots/autonomous/ItemValue165.cs:36-58): weight of one bonus point by property."""
    w = [1.0] * 257
    w[eprop["MaxHealth"]] = 0.25
    w[eprop["MaxMana"]] = 2.0
    w[eprop["PowerPool"]] = 2.0
    for p in range(eprop["Resist_First"], eprop["Resist_Last"] + 1):
        w[p] = 2.0
    for p in range(eprop["Skill_First"], eprop["Skill_Last"] + 1):
        w[p] = 5.0
    for n in ("AllMagicSkills", "AllMeleeWeaponSkills", "AllSkills", "AllDualWieldingSkills", "AllArcherySkills"):
        w[eprop[n]] = 5.0
    return w


def load_data(out_dir, name):
    path = os.path.join(out_dir, name + ".js")
    if not os.path.exists(path):
        return None
    text = read(path)
    return json.loads(text[text.index(",") + 1: text.rindex(")")])


def reconcile(src, item_ids, out_dir):
    """Compare only: the drop lane (drops.js) and the merchant lane (vendoritems.js) decide on their own which
    monsters and merchants are in the world. When their files are there, the item sets are compared with this
    builder's bits 1, 2 and 4 and every difference is printed; nothing is taken over (the files may be older than
    this run, and build_all.py runs this builder first). Returns the counts."""
    out = {}
    drops = load_data(out_dir, "drops")
    if drops is not None and "byItem" in drops:
        theirs = {k.lower() for k, v in drops["byItem"].items() if any(len(e) < 4 or not e[3] for e in v)}
        mine = {k for k, b in src.items() if b & SRC_DROP}
        otd_theirs = {e[0].lower() for kind in drops.get("mobs", {}).values() for e in kind.get("otd", [])}
        otd_mine = {k for k, b in src.items() if b & SRC_OTD}
        # byItem entries: 4th element absent = a world spawn kind, 1 = a name nothing spawns, 2 = a script-made kind
        scr_theirs = {k.lower() for k, v in drops["byItem"].items() if any(len(e) > 3 and e[3] == 2 for e in v)}
        scr_mine = {k for k, b in src.items() if b & SRC_SCRIPT}
        out["drops"] = dict(theirs=len(theirs), mine=len(mine), onlyTheirs=sorted(theirs - mine), onlyMine=sorted(mine - theirs),
                            otdOnlyTheirs=sorted(otd_theirs - otd_mine), otdOnlyMine=sorted(otd_mine - otd_theirs),
                            scriptTheirs=len(scr_theirs), scriptMine=len(scr_mine),
                            scriptOnlyTheirs=sorted(scr_theirs - scr_mine), scriptOnlyMine=sorted(scr_mine - scr_theirs))
    vend = load_data(out_dir, "vendoritems")
    if vend is not None and "lists" in vend:
        theirs = {e[0].lower() for rows in vend["lists"].values() for e in rows if isinstance(e, list) and e and isinstance(e[0], str)}
        mine = {k for k, b in src.items() if b & SRC_VENDOR}
        out["vendoritems"] = dict(theirs=len(theirs), mine=len(mine), onlyTheirs=sorted(theirs - mine), onlyMine=sorted(mine - theirs))
    return out


def build_items(con, eprop, disabled_classes, sell_ratio, out_dir):
    rows = con.execute("select * from ItemTemplate").fetchall()
    item_ids = {}
    for r in rows:
        k = r["Id_nb"].lower()
        assert k not in item_ids, "two item templates differ only by case: " + r["Id_nb"]
        item_ids[k] = r["Id_nb"]
    src, detail, stats = collect_sources(con, item_ids, out_dir)

    # every MerchantItem template id, in the world or not: ItemValue165.MerchantStock (ItemValue165.cs:223-252)
    merchant_stock = {r[0].lower() for r in con.execute("select distinct ItemTemplateID from MerchantItem")}
    weights = value_weights(eprop)

    stats["reconcile"] = reconcile(src, item_ids, out_dir)

    spells = {r["SpellID"]: r for r in con.execute("select * from Spell")}
    line_level = {}
    for r in con.execute("select LineName, SpellID, Level from LineXSpell where LineName in ('Item Effects','Potions','Mundane Poisons')"):
        line_level[(r["LineName"], r["SpellID"])] = r["Level"]
    model_icons = json.loads(read(wgcommon.CLIENT + "/content/items/model_icons.json"))

    cols = ["id", "name", "level", "realm", "cat", "obj", "slot", "hand", "dmg", "dpsaf", "spdabs", "size", "quality",
            "bonus", "bonuses", "procs", "charges", "classes", "levelReq", "price", "sell", "util", "fair", "model",
            "icon", "src", "fl", "stack", "pack", "weight"]
    out, used_spells, descs = [], set(), {}
    left = {"noSource": 0, "noSourceByCat": {}, "onlyDisabledClasses": 0, "missingSpells": 0}
    missing_spell_refs = []
    used_props = {}
    for r in rows:
        k = r["Id_nb"].lower()
        obj, slot = r["Object_Type"], r["Item_Type"]
        cat = category(obj, slot)
        bits = src.get(k, 0)
        if not bits:
            left["noSource"] += 1
            left["noSourceByCat"][cat] = left["noSourceByCat"].get(cat, 0) + 1
            continue
        allowed = [int(x) for x in split_csv(r["AllowedClasses"]) if x.isdigit() and int(x) > 0]
        # "Usable by" leaves the disabled classes out, unless every listed class is disabled: then the list stays as
        # it is, so the item never looks usable by everyone (mode165/ItemTooltip165.cs:53-63). Such an item is kept
        # (a merchant or a monster really has it) and flagged: no True 1.65 class can use it.
        classes = [c for c in allowed if c not in disabled_classes]
        no_class = bool(allowed) and not classes
        if no_class:
            classes = allowed
            left["onlyDisabledClasses"] += 1
        dead_spell = [False]

        bonuses = []
        for i in list(range(1, 11)) + ["Extra"]:
            t = r["ExtraBonusType"] if i == "Extra" else r["Bonus%dType" % i]
            v = r["ExtraBonus"] if i == "Extra" else r["Bonus%d" % i]
            if t and v:
                bonuses.append([t, v])
                used_props[t] = used_props.get(t, 0) + 1

        def spell_ref(sid, what):
            if not sid:
                return None
            if sid not in spells:
                left["missingSpells"] += 1
                missing_spell_refs.append((r["Id_nb"], what, sid))
                dead_spell[0] = True
                return None
            used_spells.add(sid)
            return sid
        procs, charges = [], []
        for col in ("ProcSpellID", "ProcSpellID1"):
            sid = spell_ref(r[col], "proc")
            if sid:
                procs.append(sid)
        for col, c, m in (("SpellID", "Charges", "MaxCharges"), ("SpellID1", "Charges1", "MaxCharges1")):
            sid = spell_ref(r[col], "charge")
            if sid:
                charges.append([sid, r[c], r[m]])
        poison = spell_ref(r["PoisonSpellID"], "poison")
        if poison:
            charges.append([poison, r["PoisonCharges"], r["PoisonMaxCharges"]])
        proc_field = None
        if procs:
            # chance in percent per hit. Weapons: (ProcChance or 10) x speed / 3.5 (gameobjects/GameLiving.cs:785);
            # armour, when struck: ProcChance or 10 (GameLiving.cs:907)
            base = r["ProcChance"] if r["ProcChance"] > 0 else 10
            chance = round(base * (r["SPD_ABS"] / 35.0), 1) if cat == "weapon" else float(base)
            proc_field = [chance] + procs

        is_shield = obj == 42
        size = max(1, r["Type_Damage"]) if is_shield else 0   # AbstractServerRules.cs:893: size 0 counts as small
        price = r["Price"]
        # what a merchant pays (gameobjects/GameMerchant.cs:320; AutonomousBotEconomy.cs:1947-1958): Price x
        # item_sell_ratio / 100 for one piece of a full pack, nothing when the item cannot be dropped
        pack = max(1, r["PackSize"])
        sell = 0
        if r["IsDropable"]:
            sell = price // pack * sell_ratio // 100
            if price == 1 and sell == 0:
                sell = price // pack

        # ---- the server's item value (bots/autonomous/ItemValue165.cs), one piece at 100% condition
        util = sum(weights[t if 0 <= t < 256 else 0] * abs(v) for t, v in bonuses) if bonuses else 0
        shelf = price // pack if (price > 0 and k in merchant_stock) else 0                       # :80-85
        is_gear = r["MaxCount"] <= 1 and (obj in WEAPON_TYPES or obj in ARMOR_TYPES or obj in (41, 42, 45))   # :88-92
        if not is_gear:
            fair = shelf * 9 // 10 if shelf > 0 else max(2 * sell, sell + 1)                      # :103-104
        else:
            lvl = min(50, max(1, r["Level"]))                                                     # :107
            curve = (lvl / 50.0) ** 3
            base_gold = 200 * curve * SLOT_FACTOR.get(slot, 0.6)
            ucap = max(4, 0.8 * lvl)
            utility = 1 + 0.5 * min(util, ucap) / ucap
            has_proc = bool(r["SpellID"] or r["SpellID1"] or r["ProcSpellID"] or r["ProcSpellID1"])   # :197-198
            p = base_gold * QUALITY_FACTOR(r["Quality"]) * utility * (1.25 if has_proc else 1) * 1.0 * 10000
            cap = max(1, 800 * curve) * 10000
            fair = csharp_round(min(max(p, 1), cap))
            fair = max(fair, 2 * sell)
            if shelf > 0:
                fair = min(fair, shelf * 9 // 10)
        fair = max(1, fair)

        fl = 0
        if not r["IsTradable"] or (r["Flags"] & 0x4):
            fl |= FL_NOTRADE
        if not r["IsDropable"] or (r["Flags"] & 0x8):
            fl |= FL_NODROP
        if not r["IsPickable"]:
            fl |= FL_NOPICKUP
        if r["IsIndestructible"] or (r["Flags"] & 0x2):
            fl |= FL_INDESTRUCTIBLE
        if r["IsNotLosingDur"]:
            fl |= FL_NOWEAR
        if sell == 0 or (r["Flags"] & 0x1):
            fl |= FL_NOSELL
        # a quest item on a loot table falls only for a character on a quest that names it
        # (mode165/QuestLootGate165.cs:27-43: PackageID "ClassicQuest" or an id starting with cq_)
        if bits & (SRC_DROP | SRC_SCRIPT) and ((r["PackageID"] or "") == "ClassicQuest" or k.startswith("cq_")):
            fl |= FL_QUESTGATED
        if no_class:
            fl |= FL_NOCLASS
        if dead_spell[0]:
            fl |= FL_DEADSPELL

        icon = int(model_icons.get(str(r["Model"]), 0))
        if shows_description(r["Description"]):
            descs[r["Id_nb"]] = r["Description"].strip()
        out.append([r["Id_nb"], r["Name"], r["Level"], r["Realm"], cat, obj, slot, r["Hand"],
                    0 if is_shield else r["Type_Damage"], r["DPS_AF"], r["SPD_ABS"], size, r["Quality"], r["Bonus"],
                    bonuses, proc_field, charges or None, classes, r["LevelRequirement"], price, sell,
                    round(util, 2) if util else 0, fair, r["Model"], icon, bits, fl, max(1, r["MaxCount"]), pack, r["Weight"]])
    out.sort(key=lambda row: (row[1].lower(), row[0].lower()))

    spell_table = {}
    for sid in sorted(used_spells):
        s = spells[sid]
        text = re.sub(r"\s+", " ", (s["Description"] or "").strip())
        level = next((line_level[(ln, sid)] for ln in ("Item Effects", "Potions", "Mundane Poisons") if (ln, sid) in line_level), 0)
        spell_table[str(sid)] = [s["Name"], s["Type"] or "", text[:200], round(s["Damage"], 1), s["DamageType"],
                                 round(s["Value"], 1), s["Duration"], s["Frequency"], s["Radius"], s["Range"], s["Target"], level]
    kept = {row[0].lower() for row in out}
    craft = {item_ids[k]: v for k, v in detail["craft"].items() if k in kept}
    quests = {}
    for k, entries in detail["quest_of"].items():
        if k in kept:
            named = [e for e in entries if e[0]] or list(entries)   # an entry without a quest name only when nothing better
            quests[item_ids[k]] = [list(e) for e in sorted(named, key=lambda e: (e[1], e[0], e[3], e[2]))][:12]   # full key: the same file on every run
    stats["missingSpellRefs"] = missing_spell_refs[:40]
    # ingredients that are not catalogue items themselves (nothing drops, sells or crafts them): their names, so a
    # page can still print the recipe
    names_by_id = {r["Id_nb"].lower(): r["Name"] for r in rows}
    other_names = {}
    for v in craft.values():
        for ing, n in v[2]:
            if ing.lower() not in kept:
                other_names[ing] = names_by_id.get(ing.lower(), ing)
    left["ingredientsWithNoSource"] = len(other_names)
    return dict(cols=cols, rows=out, spells=spell_table, craft=craft, quests=quests, descs=descs, otherNames=other_names), left, stats, used_props, item_ids


# ------------------------------------------------------------------------------------------------ icons
ICON_FIRST, ICON_SHEETS = 900, ["WPN_900", "WPN_1000", "WPN_1100", "ITM_1200", "ITM_1300", "ITM_1400", "ITM_1500"]


def build_icons(used_icons):
    """icons/items.png: the client's seven item icon sheets (content/ui/icons/original, 10 x 10 cells of 32 px each,
    icon numbers 900-1599) stacked top to bottom. Icon n is the 32 px cell at x = (n % 10) * 32,
    y = ((n - 900) // 10) * 32: the same cell rule the game client uses (godot game/ui/item_db.gd:73-88)."""
    from PIL import Image
    src_dir = wgcommon.CLIENT + "/content/ui/icons/original"
    sheets = json.loads(read(wgcommon.CLIENT + "/content/items/items.json"))["sheets"]
    sheet = Image.new("RGB", (320, 320 * len(ICON_SHEETS)))
    for i, name in enumerate(ICON_SHEETS):
        assert sheets[str(ICON_FIRST + 100 * i)] == name, "client sheet table changed"
        im = Image.open(src_dir + "/" + name + ".png").convert("RGB")
        assert im.size == (320, 320), name
        sheet.paste(im, (0, 320 * i))
    out_dir = os.path.join(wgcommon.ROOT, "icons")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "items.png")
    sheet.save(path, optimize=True)
    outside = sorted(n for n in used_icons if n and not (ICON_FIRST <= n < ICON_FIRST + 100 * len(ICON_SHEETS)))
    assert not outside, "icon numbers outside the item sheets: %s" % outside[:10]
    # cells that are plain black hold no picture: report how many used icons point at one
    px = sheet.load()
    blank = 0
    for n in used_icons:
        if not n:
            continue
        x0, y0 = (n % 10) * 32, ((n - ICON_FIRST) // 10) * 32
        if all(sum(px[x0 + dx, y0 + dy]) < 24 for dx in range(4, 28, 4) for dy in range(4, 28, 4)):
            blank += 1
    # the same seven sheets at 64 px a cell, from the client's own enlarged sheets (content/ui/icons/x4plus,
    # 1280 px, halved): for places that draw the icon large. Same cell rule with 64 in place of 32.
    big = Image.new("RGB", (640, 640 * len(ICON_SHEETS)))
    for i, name in enumerate(ICON_SHEETS):
        im = Image.open(wgcommon.CLIENT + "/content/ui/icons/x4plus/" + name + ".png").convert("RGB")
        assert im.size == (1280, 1280), name
        big.paste(im.resize((640, 640), Image.LANCZOS), (0, 640 * i))
    path64 = os.path.join(out_dir, "items64.jpg")
    big.save(path64, quality=88)
    return os.path.getsize(path), blank, os.path.getsize(path64)


# ------------------------------------------------------------------------------------------------ main
OBJ_NAMES = {   # Enums/eObjectType.cs member -> the name the pages show
    "GenericItem": "Item", "GenericWeapon": "Generic weapon", "CrushingWeapon": "Crush", "SlashingWeapon": "Slash",
    "ThrustWeapon": "Thrust", "Fired": "Short bow", "TwoHandedWeapon": "Two handed", "PolearmWeapon": "Polearm",
    "Staff": "Staff", "Longbow": "Longbow", "Crossbow": "Crossbow", "Flexible": "Flexible", "Sword": "Sword",
    "Hammer": "Hammer", "Axe": "Axe", "Spear": "Spear", "CompositeBow": "Composite bow", "Thrown": "Thrown",
    "LeftAxe": "Left axe", "HandToHand": "Hand to hand", "RecurvedBow": "Recurved bow", "Blades": "Blades",
    "Blunt": "Blunt", "Piercing": "Piercing", "LargeWeapons": "Large weapon", "CelticSpear": "Celtic spear",
    "Scythe": "Scythe", "FistWraps": "Fist wraps", "MaulerStaff": "Mauler staff", "GenericArmor": "Generic armor",
    "Cloth": "Cloth", "Leather": "Leather", "Studded": "Studded", "Chain": "Chain", "Plate": "Plate",
    "Reinforced": "Reinforced", "Scale": "Scale", "Magical": "Magical", "Shield": "Shield", "Arrow": "Arrow",
    "Bolt": "Bolt", "Instrument": "Instrument", "Poison": "Poison", "AlchemyTincture": "Alchemy tincture",
    "SpellcraftGem": "Spellcraft gem", "GardenObject": "Garden object", "HouseWallObject": "House wall object",
    "HouseFloorObject": "House floor object", "HouseCarpetFirst": "House carpet", "HouseNPC": "House NPC",
    "HouseVault": "House vault", "HouseInteriorObject": "House crafting station", "HouseTentColor": "House tent colour",
    "HouseExteriorBanner": "House banner (outside)", "HouseExteriorShield": "House shield (outside)",
    "HouseRoofMaterial": "House roof material", "HouseWallMaterial": "House wall material",
    "HouseDoorMaterial": "House door material", "HousePorchMaterial": "House porch material",
    "HouseWoodMaterial": "House wood material", "HouseShutterMaterial": "House shutter material",
    "HouseInteriorBanner": "House banner (inside)", "HouseInteriorShield": "House shield (inside)",
    "HouseBindstone": "House bindstone", "HouseCarpetSecond": "House carpet (second)",
    "HouseCarpetThird": "House carpet (third)", "HouseCarpetFourth": "House carpet (fourth)",
    "SiegeBalista": "Siege ballista", "SiegeCatapult": "Siege catapult", "SiegeCauldron": "Siege cauldron",
    "SiegeRam": "Siege ram", "SiegeTrebuchet": "Siege trebuchet",
}
SLOT_NAMES = {   # gameutils/IGameInventory.cs:9-70 (eInventorySlot) values an item's Item_Type takes
    "HorseArmor": "Horse armor", "HorseBarding": "Horse barding", "Horse": "Horse", "RightHandWeapon": "Right hand",
    "LeftHandWeapon": "Left hand", "TwoHandWeapon": "Two handed", "DistanceWeapon": "Ranged", "HeadArmor": "Head",
    "HandsArmor": "Hands", "FeetArmor": "Feet", "Jewelry": "Jewel", "TorsoArmor": "Chest", "Cloak": "Cloak",
    "LegsArmor": "Legs", "ArmsArmor": "Arms", "Neck": "Neck", "Waist": "Belt", "LeftBracer": "Bracer",
    "RightBracer": "Bracer (right)", "LeftRing": "Ring", "RightRing": "Ring (right)", "Mythical": "Mythical",
}
DMG_NAMES = {"Natural": "Natural", "Crush": "Crush", "Slash": "Slash", "Thrust": "Thrust", "Body": "Body",
             "Cold": "Cold", "Energy": "Energy", "Heat": "Heat", "Matter": "Matter", "Spirit": "Spirit"}
CRAFT_SKILLS = None


def prop_kind(pid, e):
    if e["Stat_First"] <= pid <= e["Stat_Last"] or pid == e["Acuity"]:
        return "stat"
    if pid == e["MaxHealth"]:
        return "hits"
    if pid in (e["MaxMana"], e["PowerPool"]):
        return "power"
    if e["Resist_First"] <= pid <= e["Resist_Last"] or pid == e["Resist_Natural"]:
        return "resist"
    if e["Skill_First"] <= pid <= e["Skill_Last"] or pid in (e["AllMagicSkills"], e["AllMeleeWeaponSkills"], e["AllSkills"],
                                                           e["AllDualWieldingSkills"], e["AllArcherySkills"]):
        return "skill"
    if e["Focus_Darkness"] <= pid <= e["Focus_Arboreal"] or pid == e["AllFocusLevels"] or \
            e["Focus_EtherealShriek"] <= pid <= e["Focus_Witchcraft"]:
        return "focus"
    if e["StatCapBonus_First"] <= pid <= e["StatCapBonus_Last"] or e["ResCapBonus_First"] <= pid <= e["ResCapBonus_Last"]:
        return "cap"
    return "other"


def main():
    a = wgcommon.args(__doc__)
    no_icons = "--no-icons" in sys.argv
    t0 = time.time()
    check_rule_source()
    con = wgcommon.open_db(a.db)
    eprop = parse_enum(GS + "/Enums/eProperty.cs", "eProperty")
    eclass = parse_enum(GS + "/Enums/eCharacterClass.cs", "eCharacterClass")
    eobj = parse_enum(GS + "/Enums/eObjectType.cs", "eObjectType")
    eslot = parse_enum(GS + "/gameutils/IGameInventory.cs", "eInventorySlot")
    edmg = parse_enum(GS + "/Enums/eDamageType.cs", "eDamageType")
    ecraft = parse_enum(GS + "/craft/CraftingMgr.cs", "eCraftingSkill")
    props = parse_property_names(eprop)
    assert eprop["Strength"] == 1 and eprop["Resist_Thrust"] == 19 and eprop["Skill_Archery"] == 115 and eprop["Acuity"] == 156

    classes, disabled, discontinued, class_notes = build_classes(con, eprop, eclass, props)
    disabled_all = set(disabled) | set(discontinued)
    sell_ratio = int(con.execute("select Value from ServerProperty where Key='item_sell_ratio'").fetchone()[0])
    cross = con.execute("select Value from ServerProperty where Key='allow_cross_realm_items'").fetchone()[0]
    assert str(cross).lower() == "false", "allow_cross_realm_items is on: the class usability table assumes it is off"
    start_base = con.execute("select Value from ServerProperty where Key='start_as_base_class'").fetchone()[0]
    assert str(start_base).lower() == "true"

    old_archery = str(con.execute("select Value from ServerProperty where Key='allow_old_archery'").fetchone()[0]).lower() == "true"
    obj_skill = parse_obj_skill(eobj, eprop, old_archery)
    items, left, stats, used_props, item_ids = build_items(con, eprop, disabled_all, sell_ratio, a.out)
    rows, cols = items["rows"], items["cols"]
    ci = {c: i for i, c in enumerate(cols)}

    # ---------------- checks
    assert len(rows) > 5000, "suspiciously few items: %d" % len(rows)
    assert len({r[0].lower() for r in rows}) == len(rows)
    for r in rows:
        assert r[ci["src"]] > 0
        assert r[ci["cat"]] != "shield" or 1 <= r[ci["size"]] <= 3 or r[ci["size"]] == 12, "shield size %s" % r[:2]
    by_cat, by_src = {}, {}
    for r in rows:
        by_cat[r[ci["cat"]]] = by_cat.get(r[ci["cat"]], 0) + 1
        for bit, name in ((1, "drop"), (2, "otd"), (4, "vendor"), (8, "quest"), (16, "craft"), (32, "start"), (64, "qitem"), (128, "script")):
            if r[ci["src"]] & bit:
                by_src[name] = by_src.get(name, 0) + 1
    unnamed_props = sorted(p for p in used_props if p not in props)

    # ---------------- icons
    used_icons = {r[ci["icon"]] for r in rows}
    icon_info = None
    if not no_icons:
        size, blank, size64 = build_icons(used_icons)
        with_icon = sum(1 for r in rows if r[ci["icon"]])
        icon_info = dict(sheet="icons/items.png", cell=32, cols=10, first=ICON_FIRST, count=100 * len(ICON_SHEETS),
                         itemsWithIcon=with_icon, itemsWithout=len(rows) - with_icon, usedIconsOnBlankCell=blank, bytes=size,
                         sheet64="icons/items64.jpg", cell64=64, bytes64=size64)

    items["built"] = wgcommon.stamp()
    items["srcBits"] = {"drop": 1, "otd": 2, "vendor": 4, "quest": 8, "craft": 16, "start": 32, "qitem": 64, "script": 128}
    items["flBits"] = {"noTrade": 1, "noDrop": 2, "noPickup": 4, "indestructible": 8, "noWear": 16, "noSell": 32,
                       "questGated": 64, "noClass165": 128, "deadSpell": 256}
    items["spellCols"] = ["name", "type", "text", "damage", "dmgType", "value", "duration", "frequency", "radius", "range", "target", "level"]
    items["counts"] = dict(items=len(rows), byCat=by_cat, bySource=by_src, templatesInWorld=len(item_ids))
    left["absentMonsterLootTables"] = len(stats["absentMonsterTables"])
    items["left"] = left
    items["scriptMonsters"] = stats["scriptMonsterTables"]
    items["scriptMonstersFrom"] = stats["scriptFrom"]
    items["comparedWith"] = {k: dict(theirs=v["theirs"], mine=v["mine"], differences=sum(len(x) for x in v.values() if isinstance(x, list)))
                             for k, v in stats["reconcile"].items()}
    items["icons"] = icon_info
    size_items = wgcommon.write_data("items", items, a.out, "%d items" % len(rows))
    assert size_items < 15e6

    # ---------------- itemmeta
    obj_names = {}
    for member, val in eobj.items():
        if member.startswith("_"):
            continue
        assert member in OBJ_NAMES, "new eObjectType member: " + member
        obj_names[str(val)] = OBJ_NAMES[member]
    slot_names = {str(eslot[m]): n for m, n in SLOT_NAMES.items()}
    slot_names["40"] = "Not equipped"   # eInventorySlot.FirstBackpack: the Item_Type of things that are carried, not worn
    dmg_names = {str(edmg[m]): n for m, n in DMG_NAMES.items()}
    used_objs = sorted({r[ci["obj"]] for r in rows})
    used_slots = sorted({r[ci["slot"]] for r in rows})
    assert all(str(o) in obj_names for o in used_objs), [o for o in used_objs if str(o) not in obj_names]
    craft_names = {str(v): re.sub(r"(?<!^)([A-Z])", r" \1", k).strip() for k, v in ecraft.items() if not k.startswith("_")}
    prop_table = {}
    for pid in sorted(set(props) | set(used_props)):
        name = props.get(pid)
        if name is None:
            member = next((m for m, v in eprop.items() if v == pid and not m.endswith(("_First", "_Last"))), "Property %d" % pid)
            name = re.sub(r"_", " ", member)
        prop_table[str(pid)] = [name, prop_kind(pid, eprop), used_props.get(pid, 0)]
    meta = dict(
        built=wgcommon.stamp(),
        props=prop_table, propCols=["name", "kind", "itemsUsingIt"],
        objTypes=obj_names, slots=slot_names, dmgTypes=dmg_names,
        hands={"0": "One handed", "1": "Two handed", "2": "Left hand"},
        shieldSizes={"1": "Small", "2": "Medium", "3": "Large"},
        instruments={"1": "Drum", "2": "Lute", "3": "Flute", "4": "Harp"},   # Enums/eInstrumentType.cs: an instrument's DPS_AF
        realms={"0": "Any", "1": "Albion", "2": "Midgard", "3": "Hibernia"},
        cats=["weapon", "armor", "shield", "instrument", "jewelry", "cloak", "magical", "ammo", "poison", "tincture",
              "gem", "housing", "siege", "other"],
        craftSkills=craft_names,
        armorTypes=[32, 33, 34, 37, 35, 38, 36], armorLevel={str(k): v for k, v in ARMOR_LEVEL.items()},
        rangedTypes=sorted(RANGED_TYPES),
        weaponAbility={str(k): v for k, v in WEAPON_ABILITY.items()},
        objSkill={str(k): v for k, v in sorted(obj_skill.items())},
        classes=classes, disabledClasses=disabled, discontinuedClasses=discontinued,
        classNames={str(v): k for k, v in eclass.items() if v},
        rules=dict(
            dpsCap="weapon DPS counts up to 1.2 + 0.3 x level (GamePlayer.cs:5723-5731); in DPS_AF units 12 + 3 x level",
            afCap="armour AF counts up to 2 x level, cloth up to 1 x level (GamePlayer.cs:5614-5630, 5635-5650)",
            procLevel="a proc or charge works from the item's LevelRequirement, or min(50, item level) when that is 0 (GameLiving.cs:866, 902; GamePlayer.cs:7315)",
            slots="Item_Type 10 goes in the right hand (or the two-handed slot), 11 in the right or the left hand, 12 two-handed, 13 ranged; a left-hand weapon that is not a shield needs a class with leftHand (GamePlayerInventory.cs:782-828); instruments go in the two-handed or the ranged slot",
            procChance="weapon procs: (ProcChance or 10) x speed / 3.5 percent per hit (GameLiving.cs:785); armour procs when struck: ProcChance or 10 percent (GameLiving.cs:907)",
            bonusCaps="item bonus caps by level: stats 1.5 x level, hits 4 x level, resists level / 2 + 1, skills level / 5 + 1, power level / 2 + 1 (BotGearValue165.cs:92-107)",
            sell="a merchant pays Price x %d / 100 (ServerProperty item_sell_ratio; GameMerchant.cs:320)" % sell_ratio,
            fair="items.fair = the Realm Exchange fair price of one piece at full condition (ItemValue165.cs:96-125); items.util = ItemValue165.Utility (:64-74)",
            realm="an item with a realm can only be used by that realm (allow_cross_realm_items = False; AbstractServerRules.cs:666-670)",
            baseClass="levels 1 to 4 are played as the base class (start_as_base_class = True); class tables use the base class's abilities below level 5",
        ),
        caps=dict(dpsaf=[0] + [12 + 3 * lv for lv in range(1, 51)], af=[0] + [2 * lv for lv in range(1, 51)],
                  afCloth=[0] + list(range(1, 51))),
        valueWeights=dict(default=1, hits=0.25, power=2, resist=2, skill=5, src="ItemValue165.cs:36-58"),
        sellRatio=sell_ratio,
        notes=class_notes,
    )
    size_meta = wgcommon.write_data("itemmeta", meta, a.out, "%d classes, %d property names" % (len(classes), len(prop_table)))

    # ---------------- summary
    print("items kept: %d of %d templates" % (len(rows), len(item_ids)))
    print("  by category: " + ", ".join("%s %d" % kv for kv in sorted(by_cat.items(), key=lambda kv: -kv[1])))
    print("  by source:   " + ", ".join("%s %d" % kv for kv in sorted(by_src.items(), key=lambda kv: -kv[1])))
    print("left out: %d with no source (%s); kept and flagged: %d usable only by classes not in True 1.65" % (
        left["noSource"], ", ".join("%s %d" % kv for kv in sorted(left["noSourceByCat"].items(), key=lambda kv: -kv[1])), left["onlyDisabledClasses"]))
    print("spell references to spells that do not exist (left off the item, item flagged): %d" % left["missingSpells"])
    for k in ("regionsLoaded", "spawnRows", "spawnRowsInUnloadedRegions", "spawnRowsRenamedByTemplate", "monsterNames",
              "questSpawnNames", "lootTemplates", "lootTemplatesReached", "lootRowsToMissingItems", "otdRows",
              "otdRowsReached", "merchantRows", "merchantLists", "merchantListsInWorld", "recipes", "dataQuests",
              "scriptedQuestItemRefs", "scriptedQuestRewards"):
        print("  %-28s %s" % (k, stats[k]))
    print("  script-made monsters taken from: " + stats["scriptFrom"])
    print("  loot tables of script-summoned monsters (src bit 128): %d: %s" % (len(stats["scriptMonsterTables"]), ", ".join(stats["scriptMonsterTables"])))
    print("  loot tables of monsters not in the world (left out): %d: %s" % (len(stats["absentMonsterTables"]), ", ".join(stats["absentMonsterTables"])))
    for lane, r in stats["reconcile"].items():
        diff = {k: v for k, v in r.items() if isinstance(v, list) and v}
        print("  compared with %s.js: their items %d, this builder %d: %s" % (lane, r["theirs"], r["mine"],
              "the same set" if not diff else "DIFFERENCE " + "; ".join("%s %d %s" % (k, len(v), v[:6]) for k, v in diff.items())))
    if not stats["reconcile"]:
        print("  drops.js / vendoritems.js not there yet: nothing to compare the drop and vendor bits with")
    print("classes: %d (disabled %s, discontinued %s)" % (len(classes), disabled, discontinued))
    print("bonus types on items without a server name: %s" % unnamed_props)
    for n in class_notes:
        print("  note: " + n)
    if icon_info:
        print("icons: %(itemsWithIcon)d items with an icon, %(itemsWithout)d without, %(usedIconsOnBlankCell)d used icons on a blank cell, sheet %(bytes)d bytes" % icon_info)
    print("spells: %d, craft: %d, quests: %d, descriptions: %d" % (len(items["spells"]), len(items["craft"]), len(items["quests"]), len(items["descs"])))
    print("done in %.1f s" % (time.time() - t0))


if __name__ == "__main__":
    main()
