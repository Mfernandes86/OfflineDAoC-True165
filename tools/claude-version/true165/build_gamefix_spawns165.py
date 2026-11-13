"""True 1.65 0.8 gamefix SPAWNS (lead brief <lead workspace>/gamefix\\GAMEFIX-BRIEF.md, 2026-11-12; found by the OFFLINEDAOC
Database tool, reports M-D.md and V-D.md). Report: <lead workspace>/gamefix\\reports\\SPAWNS.md.

A. Monsters with period loot that spawned peaceful (PEACE flag 16), so nobody could attack them as placed. Period
   source for each: Illia's Camelot Bestiary on period Allakhazam (Wayback captures 2003, archive
   development-source/research/period-data/archive/wayback-allakhazam/period_mobs.jsonl.gz) lists Known Loot, and the
   Prima Atlas / Shrouded Isles guides list each as a monster with a level (and drops):
     Shade of Ambrosius Aurelianus  zam mob 2606 (2003-10-11): Undead 54, Known Loot (Slender Ebony staves), Related
                                    Quest Hands Of Fate; Prima Atlas [54] with a drop table. questfix07 P04-A1 had made
                                    him GHOST|PEACE (the quest's talking spectre); the owner's rule for a quest
                                    character with period drops (2026-11-09, "one neutral monster", Freeing Osier
                                    2026-11-12) is one NPC that talks for the quest, never attacks first (AggroLevel
                                    0, kept) and can be killed for its drops: the PEACE bit goes, GHOST stays.
     Frit                           zam mob 3413 (2003-04-18): Monster 37, Coruscating Mines, Known Loot (Worn Jewel
                                    armour); Prima Atlas "Frit [37] see silvermine knocker".
     Telsar                         zam mob 5582 (2003-10-11): Monster 45, Caldey, Known Loot (Telsar's rings and
                                    wraps); a period comment calls him the "named corpse mold" that drops leather.
     fallen sea king / queen        zam mob 2843 (2003-09-18): Fallen Sea King, Monster 50-55, Vanern Swamp, Known
                                    Loot Spiked Coral Crown ("Killed queen 3 times"); Prima Atlas [60] both, coral
                                    crowns. Rows from add-restoredspawns165.json; the queen's look comes from base
                                    template 22016 (ReplaceMobValues: its Flags win).
     Borwyr the Cursed (Camelot Hills, true165-prima-new)
                                    zam mob 2465 (2003-10-04): Undead 13, east of Prydwen Keep, loc 49678, 52870
                                    (this row: 50495, 52867), Known Loot Silvered Drakescale Gauntlets; Prima Atlas
                                    Camelot Hills [13]. He borrowed base template 12009 (GHOST|PEACE, ReplaceMobValues),
                                    which also dresses the Amulet of the Planes quest's placeholder at its event point
                                    in Avalon Marsh and the quest's own event spawn; so he gets his own copy of it
                                    (add-gamefix-spawns165.json, template 917120090, GHOST only) and the quest keeps
                                    12009 as it is.
   Left as they are: Hurika (DOL.GS.Hurika.AddToWorld sets Flags = 0 after loading its PEACE template: attackable),
   the Amulet of the Planes Borwyr placeholder in Avalon Marsh (the quest's kill step brings its own fightable Borwyr,
   ClassicQuests event spawn; the placeholder is in QuestMonsterIds).

B. Ley Manton (Weapon Merchant) and Tria Ellowis (Armor Merchant) stood twice on one spot: each has the period
   OpenDAoC row (PackageID Public_DB, GameMerchant, guild, merchant list) and a second base row saved later
   (2021-09-26, PackageID NULL, GameNPC on the same ReplaceMobValues template, which also makes it a GameMerchant).
   The second row of each is archived (rules remove.where), so exactly one of each remains.

C. Spawns whose class does not exist (the server falls back to a plain GameNPC and logs an error):
     drakoran mage f79320fe   Mob.ClassType '0' -> DOL.GS.GameNPC (its 35 siblings and its template are GameNPC)
     Scholar Nenet            template 60165654 ClassType DOL.GS.Scholar (the Trials of Atlantis artifact scholar,
                              commented out in this server) -> DOL.GS.GameNPC; the period Lethantis Association NPC
                              in Campacorentin Forest stays.
     soultorn norse stormur herra (Darkness Falls, true165-prima-new) DOL.GS.GamePC -> DOL.GS.GameNPC on the row and
                              its own template 916615184 (copied from base template 60266094, also fixed; every
                              other soultorn is a GameNPC).

    python -I build_gamefix_spawns165.py      rerunnable: its own rules entries are replaced, add-file rows already
                                              fixed are left as they are
Rerun it after anything rebuilds add-restoredspawns165.json, add-primaspawns165.json or add-primatemplates165.json.
Out (both copies, runtime\\true165 and tools\\claude-version\\true165): true165-rules.json (through
<lead workspace>/gamefix\\locks.py), add-gamefix-spawns165.json (new), and the rows above in add-restoredspawns165.json,
add-primaspawns165.json, add-primatemplates165.json (each file keeps its own format and line endings).
"""
import json, os, sqlite3, sys, uuid

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
FOLDERS = (os.path.join(CV, "runtime", "true165"), HERE)
CLEAN = os.environ.get("OFFLINE_DAOC_CLEAN_DB") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "runtime", "data", "opendaoc-165.sqlite3.db")   # the clean world (database/unpack_db.py)
LANE = "SPAWNS"
TAG = locks.TAG % LANE
NOW = "2026-11-12 12:00:00"
NS = uuid.UUID("5d0c6b1e-2f43-4a8e-9b7d-6a1e0c3f9d21")
PEACE = 16

SHADE_MOB, SHADE_TPL = "8ae4dc04-ddf9-454c-acfe-7c4b363f1094", 12799
FRIT_MOB, FRIT_TPL = "ef6e1aac-5068-44a6-9827-e82ae35d8340", 60161106
TELSAR_MOB = "765344ed-bc9c-4a6c-90fb-3ba505eb21ca"
SEA_KING, SEA_QUEEN, SEA_QUEEN_TPL = "5a464122-a57f-45bd-bc4d-6db6b5a50d25", "4bc1bd42-525e-4f70-8a00-ece4a5f0869a", 22016
BORWYR_PRIMA, BORWYR_TPL, BORWYR_OWN_TPL = "e617e448-b8b3-5dc0-8f9b-1930f53c6ee5", 12009, 917120090
DUPLICATES = {"231313cf-eb6a-408f-9811-f697c705ffb7": "Ley Manton", "cc83cc14-e5ff-4eb1-b35c-8ad140793909": "Tria Ellowis"}
DRAKORAN_MAGE = "f79320fe-7f0f-4597-819c-0696d37f6b02"
NENET_TPL = 60165654
STORMUR_MOB, STORMUR_TPL, STORMUR_BASE_TPL = "fa9d09b7-c3ad-5d83-89c1-c9756fa320a6", 916615184, 60266094
STORMUR_TPL_KEY = "749cf0c6-8b2b-5481-8169-c9f40223a0e5"   # its NpcTemplate_ID (the add-file's key)


def rules_entries():
    upd = lambda sql, why: {"sql": sql, "reason": TAG + why}
    updates = [
        upd(f"UPDATE NpcTemplate SET Flags = Flags & ~{PEACE} WHERE TemplateId = {SHADE_TPL} "
            f"AND Name = 'Shade of Ambrosius Aurelianus'",
            "Shade of Ambrosius Aurelianus can be fought (period Allakhazam mob 2606, 2003-10-11: Undead 54, Known Loot "
            "Slender Ebony staves, quest Hands Of Fate; Prima Atlas [54] with drops). One neutral monster (owner "
            "2026-11-09): he still takes the Hands Of Fate items and never attacks first (AggroLevel 0 from questfix07 "
            "P04-A1 kept); only the PEACE bit goes, GHOST stays. Runs after the questfix07 P04-A1 entries"),
        upd(f"UPDATE Mob SET Flags = Flags & ~{PEACE} WHERE Mob_ID = '{SHADE_MOB}'",
            "Shade of Ambrosius Aurelianus: the same on his Mob row (the template decides; kept consistent)"),
        upd(f"UPDATE NpcTemplate SET Flags = Flags & ~{PEACE} WHERE TemplateId = {FRIT_TPL} AND Name = 'Frit'",
            "Frit (Coruscating Mine) can be fought (period Allakhazam mob 3413, 2003-04-18: Monster 37, Known Loot Worn "
            "Jewel armour; Prima Atlas 'Frit [37] see silvermine knocker'); his ReplaceMobValues template carried PEACE"),
        upd(f"UPDATE Mob SET Flags = Flags & ~{PEACE} WHERE Mob_ID = '{FRIT_MOB}'",
            "Frit: the same on his Mob row (the template decides; kept consistent)"),
        upd(f"UPDATE Mob SET Flags = Flags & ~{PEACE} WHERE Mob_ID = '{TELSAR_MOB}' AND Name = 'Telsar'",
            "Telsar (Caldey) can be fought (period Allakhazam mob 5582, 2003-10-11: Monster 45, Known Loot Telsar's "
            "Bejeweled Pinky Ring, Gem Wrap, Jewelled Wrap; SI Prima guide Telsar 45)"),
        upd(f"UPDATE NpcTemplate SET Flags = Flags & ~{PEACE} WHERE TemplateId = {SEA_QUEEN_TPL} AND Name = 'fallen sea queen'",
            "fallen sea queen (Vanern Swamp) can be fought (period Allakhazam mob 2843, 2003-09-18: 'Killed queen 3 "
            "times'; Prima Atlas fallen sea queen [60], coral crowns); her ReplaceMobValues template carried PEACE. Her "
            "row and the fallen sea king's are fixed in add-restoredspawns165.json (build_gamefix_spawns165.py)"),
        upd(f"UPDATE Mob SET ClassType = 'DOL.GS.GameNPC' WHERE Mob_ID = '{DRAKORAN_MAGE}' AND ClassType = '0'",
            "drakoran mage (Avalon) named class '0', which does not exist (the server fell back to GameNPC and logged "
            "an error); its template and its 35 siblings are DOL.GS.GameNPC"),
        upd(f"UPDATE NpcTemplate SET ClassType = 'DOL.GS.GameNPC' WHERE TemplateId = {NENET_TPL} AND ClassType = 'DOL.GS.Scholar'",
            "Scholar Nenet (Lethantis Association, Campacorentin Forest): his ReplaceMobValues template named "
            "DOL.GS.Scholar, the Trials of Atlantis artifact scholar (commented out in this server); he is a plain "
            "period NPC, as his Mob row says"),
        upd(f"UPDATE NpcTemplate SET ClassType = 'DOL.GS.GameNPC' WHERE TemplateId = {STORMUR_BASE_TPL} AND ClassType = 'DOL.GS.GamePC'",
            "soultorn norse stormur herra: base template 60266094 named DOL.GS.GamePC, which does not exist; every "
            "other soultorn is a GameNPC (the Prima batch copied it into template 916615184, fixed in "
            "add-primatemplates165.json)"),
    ]
    where = [{
        "table": "Mob", "key": "Mob_ID",
        "where": "Mob_ID IN (%s) AND PackageID IS NULL AND Name IN ('Ley Manton', 'Tria Ellowis')"
                 % ", ".join(f"'{k}'" for k in sorted(DUPLICATES)),
        "reason": TAG + "Ley Manton (Weapon Merchant, Black Mountains North) and Tria Ellowis (Armor Merchant, Black "
                        "Mountains South) each stood twice on one spot: the period Public_DB merchant row stays; the "
                        "second base row (saved 2021-09-26, PackageID NULL, a GameNPC on the same merchant template) goes"}]
    return updates, where


def apply_rules(rules):
    mine = lambda e: str(e.get("reason", "")).startswith(TAG)
    updates, where = rules_entries()
    rules["updates"] = [u for u in rules["updates"] if not mine(u)] + updates
    rules["remove"]["where"] = [w for w in rules["remove"]["where"] if not mine(w)] + where


ADDFILE_FIXES = {   # file -> key -> {column: (value before, value after)}
    "add-restoredspawns165.json": {
        SEA_KING: {"Name": ("fallen sea king",) * 2, "Flags": (17, 17 & ~PEACE)},
        SEA_QUEEN: {"Name": ("fallen sea queen",) * 2, "Flags": (17, 17 & ~PEACE)},
    },
    "add-primaspawns165.json": {
        BORWYR_PRIMA: {"Name": ("Borwyr the Cursed",) * 2, "NPCTemplateID": (BORWYR_TPL, BORWYR_OWN_TPL),
                       "Flags": (17, 17 & ~PEACE)},
        STORMUR_MOB: {"Name": ("soultorn norse stormur herra",) * 2, "ClassType": ("DOL.GS.GamePC", "DOL.GS.GameNPC")},
    },
    "add-primatemplates165.json": {
        STORMUR_TPL_KEY: {"TemplateId": (STORMUR_TPL,) * 2, "Name": ("soultorn norse stormur herra",) * 2,
                          "ClassType": ("DOL.GS.GamePC", "DOL.GS.GameNPC")},
    },
}


def dump(data, newline, final):
    out = json.dumps(data, indent=1, ensure_ascii=False).encode("utf-8").replace(b"\n", newline)
    return out + newline if final else out


def fix_addfile(name, fixes):
    raws = [open(os.path.join(f, name), "rb").read() for f in FOLDERS]
    assert raws[0] == raws[1], f"the two copies of {name} differ: stop and tell the lead"
    raw = raws[0]
    nl = b"\r\n" if b"\r\n" in raw else b"\n"
    data = json.loads(raw.decode("utf-8"))
    final = raw.endswith(nl)
    assert dump(data, nl, final) == raw, f"{name} does not round-trip byte for byte; not touching it"
    key = data["key"]
    done = set()
    changed = 0
    for row in data["rows"]:
        want = fixes.get(str(row[key]))
        if not want:
            continue
        done.add(str(row[key]))
        for col, (before, after) in want.items():
            assert row.get(col) in (before, after), f"{name} {row[key]} {col} = {row.get(col)!r}, expected {before!r}"
            if row[col] != after:
                row[col] = after
                changed += 1
    assert done == set(fixes), f"{name}: rows not found: {set(fixes) - done}"
    if changed:
        out = dump(data, nl, final)
        for f in FOLDERS:
            open(os.path.join(f, name), "wb").write(out)
    return changed


def own_borwyr_template():
    c = sqlite3.connect("file:" + CLEAN + "?mode=ro", uri=True)
    c.row_factory = sqlite3.Row
    t = dict(c.execute("SELECT * FROM NpcTemplate WHERE TemplateId = ?", (BORWYR_TPL,)).fetchone())
    assert t["Name"] == "Borwyr the Cursed" and t["Flags"] == 17 and t["ReplaceMobValues"] == 1, t
    assert c.execute("SELECT 1 FROM NpcTemplate WHERE TemplateId = ?", (BORWYR_OWN_TPL,)).fetchone() is None
    t.update({"TemplateId": BORWYR_OWN_TPL, "Flags": 17 & ~PEACE, "PackageID": "true165-gamefix-template",
              "LastTimeRowUpdated": NOW, "NpcTemplate_ID": str(uuid.uuid5(NS, f"gamefix-spawns|{BORWYR_TPL}|borwyr"))})
    return t


def write_own_addfile():
    data = {"table": "NpcTemplate", "key": "NpcTemplate_ID",
            "reason": TAG + "own NPC templates for spawns that borrowed a peaceful one (build_gamefix_spawns165.py): "
                            "Borwyr the Cursed, the period Camelot Hills night undead (Allakhazam mob 2465), on a copy "
                            "of base template 12009 without PEACE; the Amulet of the Planes quest keeps 12009",
            "rows": [own_borwyr_template()]}
    out = json.dumps(data, indent=1, ensure_ascii=False).encode("utf-8") + b"\n"
    for f in FOLDERS:
        open(os.path.join(f, "add-gamefix-spawns165.json"), "wb").write(out)


def main():
    with locks.lock("rules"):           # the add-files other lanes may also edit: one writer at a time
        write_own_addfile()
        for name, fixes in ADDFILE_FIXES.items():
            print(f"{name}: {fix_addfile(name, fixes)} values changed")
    size = locks.edit_rules(LANE, apply_rules)
    updates, where = rules_entries()
    print(f"true165-rules.json: {len(updates)} updates + {len(where)} remove.where rule(s) tagged {TAG!r} ({size} bytes)")


if __name__ == "__main__":
    main()
