"""True 1.65 0.8 wallfix ALB (lead brief <lead workspace>/wallaudit\\WALLAUDIT-BRIEF.md, 2026-11-12; owner: "the quest npc
spackit spawns behind an invisible wall where the player can't walk. do a 10 agent audit of any spawns like this behind
invisible zone walls and move the npcs and the quest markers accordingly"). Report: <lead workspace>/wallaudit\\reports\\ALB.md.

Albion regions 1, 2 and 51: spawns that stand behind the client's one-sided zone walls (content/zones/zoneNNN/bound.json,
the real 1.64 bound.csv), where a player can see them but never walk to them (lane DETECT's flagged_v2.json, regions
1/2/51). Each one moves to the playable side, on the ground and on the server navmesh: Z = the navmesh floor
(<lead workspace>/wallaudit\\tools.py ground_info / ground_z, navmesh-165 first as the server reads it). Every new spot is
tools.is_reachable, >= 250 units inside every wall, and has a tools.nav_poly at that Z. Camps move as one group by one
offset (relative spacing kept; the smallest offset that puts every member on such a spot). Nothing else changes
(levels, templates, loot, flags, respawns, headings).

Period positions used where they exist:
  Cynwik the Wizard (quest NPC): stood at the post-New-Frontiers Snowdonia Fortress spot in Black Mtns. North (live
    Allakhazam 2012 "loc=33673,6074 Black Mtns. North, upstairs in Snowdonia Fortress"). Period: Allakhazam mob 3545
    (2002-10-16) "Known Habitats: Snowdonia (Snowdonia Fortress)"; Warcry Wizard's Appeal spoiler "in the upper level";
    the quest walkthrough "also in the fortress" beside Captain Leri. Now on the pre-NF fortress upper floor beside Leri.
  granite giant earthmagi: the Prima Atlas Dartmoor map draws each earthmagi with a reinforcer + stonemender group, the
    middle one by Nodin / Trahern / Penn, in the valley south of the ridge it stood on; moved into that valley.
  Everything else has no usable period position (no CapnBry sighting of that camp, or frontier sightings from the New
    Frontiers map), so it goes to the nearest playable ground at its camp's edge.
Grimspound is REMOVED, not moved (owner 2026-11-12: "remove it"): archived with remove.where rules, keyed by id + name:
its 10 Mob rows (Kel, Kyl, Dannen, Kan, Kyr, Lamont, Kuz, Kek, Kon, Los Ortiz), its 4 crafting stations (WorldObject)
and Kel's merchant list (19 MerchantItem rows, ItemListID 60848b48-..., used by no other NPC). Not 1.65: no period
source (<= 2003) names Grimspound, the real 1.64 client has no town there and walls the plateau off, its crafting
stations stood 2,800 under the 1.64 terrain (a later map), and Los Ortiz is a "Dragonslayer Journeyman" (a later system).
Left as unused leftovers (listed in the report): the 10 NpcTemplates only these rows used, NPCEquipment sets
56f53687-... (Los Ortiz) and bf173123-... (Lamont), and the unused NpcTemplate 13075 "Grimspound guard".
Left (see the report): thrawn ogre lookout 99995043 (Aldland, on a watchtower top 100 behind the wall) and Keep Sentinel
true165-keepguard-castle-sauvage-keepsentinel-06 (Castle Sauvage tower top, 45 behind the wall, exactly where CapnBry
saw it: 21672, 59839, Z 3560); both stand on structures nobody walks onto, have no quest role, and are seen from inside.

    python -I build_wallfix_alb165.py      rerunnable: its own rules entries are replaced; add-file rows are checked
                                           against their old or new position and left alone once moved
Out (both copies, runtime\\true165 and tools\\claude-version\\true165): true165-rules.json (through
<lead workspace>/gamefix\\locks.py) and the moved rows in add-restoredspawns165.json, add-periodspawns165.json and
add-primaspawns165.json (each keeps its format and line endings). Rerun it after anything rebuilds those files.
"""
import json, os, sqlite3, sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
FOLDERS = (os.path.join(CV, "runtime", "true165"), HERE)
CLEAN = os.environ.get("OFFLINE_DAOC_CLEAN_DB") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "runtime", "data", "opendaoc-165.sqlite3.db")   # the clean world (database/unpack_db.py)
LANE = "ALB"
TAG = "True 1.65 0.8 wallfix %s: " % LANE

WHY = {
    "cynwik": "quest NPC (The Craft of Retribution 21318-21321, Wizard Lost, Wizard's Appeal) stood at the post-New-"
              "Frontiers Snowdonia Fortress spot, Black Mtns. North 33679, 6113 (live Allakhazam 2012: 'loc=33673,6074 "
              "Black Mtns. North, upstairs in Snowdonia Fortress'), 1,770 under the mountain and 235 behind its invisible "
              "wall. Period: Allakhazam mob 3545 (2002-10-16) Known Habitats Snowdonia (Snowdonia Fortress); Warcry "
              "Wizard's Appeal spoiler 'in the upper level'; the quest walkthrough 'also in the fortress' with Captain "
              "Leri. Now on the upper floor of the period Snowdonia Fortress beside Captain Leri: Snowdonia 28988, 56796, "
              "Z 9088 (navmesh floor, 1,479 inside the walls)",
    "earthmagi": "granite giant earthmagi (Dartmoor, the only one; Allakhazam mob 3178, Prima Atlas [65]) stood on a "
                 "mountain top at 29170, 27531, 2,553 behind the invisible walls; the Prima Atlas Dartmoor map draws "
                 "each earthmagi with a reinforcer + stonemender group, the middle one by Nodin / Trahern / Penn in the "
                 "valley south of that ridge: moved to 28700, 35000, Z 580 (navmesh), 1,947 inside the walls",
    "nearest": "stood behind the invisible wall at its camp's edge, out of a player's reach; no period position for "
               "this camp; moved to the nearest playable ground (wallaudit tools), on the navmesh floor at least 250 "
               "inside the wall",
    "group": "stood behind the invisible wall with its camp mates, out of a player's reach; no period position for this "
             "camp; the camp moved together by one offset (relative spacing kept) to the nearest playable ground "
             "(wallaudit tools), each on the navmesh floor at least 250 inside the wall",
    "buried": "stood buried ~1,300 under the mountain behind the invisible wall; moved to the nearest playable ground "
              "(wallaudit tools), on the navmesh floor at least 250 inside the wall, among its siblings",
}

# Mob rows of the base world (rules UPDATE): Mob_ID, name, region, old (x, y, z), new (x, y, z), why key, offset note
BASE_MOVES = [
    ("829c7123-4cbf-4656-b4d0-b317d8c6aa16", "Cynwik the Wizard", 1, (517007, 374753, 9040), (528700, 359900, 9088), "cynwik"),
    ("5f0c777c-cf63-4526-bc93-1b2e9ce3d570", "granite giant stonelord", 1, (387865, 720960, 1674), (387077, 721228, 418), "buried"),
    ("b7b9b6f1-3fad-48c6-8016-4bb5361d2d4d", "granite giant pounder", 1, (392887, 731617, 1437), (393390, 731037, 529), "buried"),
    ("ac75abce-c138-4cd6-a658-ab62f5030647", "worker ant", 1, (541126, 473408, 4237), (540795, 473464, 3914), "nearest"),
    ("c4216f1d-b1e4-4363-8f42-06bcbb33a21d", "weak skeleton", 1, (542783, 473678, 5403), (542529, 474687, 4046), "nearest"),
    ("99992854", "mountain algae", 51, (398900, 480396, 3576), (398940, 480778, 3369), "nearest"),
    ("99994353", "mountain algae", 51, (403551, 479808, 3831), (403531, 480256, 3511), "nearest"),
    ("ef50314b-a9cc-415d-a76c-482c41b33e20", "marsh slime", 51, (265415, 432181, 4953), (265560, 432440, 4757), "nearest"),
    # camps (one offset each) and singles
    ("635cd22e-2dd5-4ada-a999-3adbe2a9c09c", "Stonecrush excavator", 1, (362992, 725211, 2537), (363030, 724612, 490), "group"),
    ("a6db3def-6c11-479c-af61-d4dcb4e8b3b0", "Stonecrush excavator", 1, (363011, 725103, 2149), (363049, 724504, 335), "group"),
    ("37b2682a-62a8-46b1-a7ed-26a619a54d4f", "brown bear", 1, (400448, 635048, 7307), (402009, 636936, 7082), "group"),
    ("4d47c34b-d773-4ad0-a7a1-2d4f31d4c2bd", "brown bear", 1, (400247, 634739, 7386), (401808, 636627, 7130), "group"),
    ("d07c2af1-7047-4c28-8371-2cfa6e669814", "bone snapper", 1, (290761, 680382, 2549), (292011, 680382, 2685), "group"),
    ("d647667e-b706-4774-aba7-e2d91cb10a97", "bone snapper", 1, (290874, 678917, 2549), (292124, 678917, 2685), "group"),
    ("88ee1edd-b49a-477c-9bb8-30bd0c631144", "undead filidh", 1, (556345, 543567, 3581), (558595, 543567, 2584), "group"),
    ("cc726560-ebae-408e-b4b0-82a29a256d98", "undead filidh", 1, (555577, 542549, 3616), (557827, 542549, 2953), "group"),
    ("395e927f-535c-4fb6-9ab7-9b0816db005c", "brownie nomad", 1, (557419, 545552, 2923), (557769, 545552, 2816), "nearest"),
    ("7dc37023-cc83-4aee-80f3-22d2700ff20c", "necromancer", 1, (494299, 417924, 9485), (494312, 417624, 9412), "nearest"),
    ("bae16a7c-08a1-4dcf-bb87-18affc485330", "wandering albions", 1, (554760, 524133, 2512), (555110, 524133, 2517), "nearest"),
    ("c1f0b856-980b-46f8-8050-a50bcd4363ed", "carrion drake", 1, (552182, 551894, 3472), (552887, 552369, 3126), "nearest"),
    ("d4f5e5c9-bf7c-4f91-a9d4-2bbe42d0f93d", "goblin lookout", 1, (484863, 440251, 6435), (485613, 440251, 5743), "nearest"),
    ("f549e8bd-2620-4217-8d9f-b6efe5929f8d", "hill avenger", 1, (485720, 415073, 9150), (485930, 414458, 8991), "nearest"),
]


# add-file rows: file -> Mob_ID -> (name, old (x, y, z), new (x, y, z))
ADDFILE_MOVES = {
    "add-restoredspawns165.json": {
        "f93354cb-dfe9-4f8c-b2cb-4c1906be9030": ("granite giant earthmagi", (381426, 723851, 4262), (380956, 731320, 580)),
        "1327f71b-8bed-449d-ad93-688a32111576": ("Stonecrush rockgrinder", (354323, 720038, 1002), (354615, 719764, 668)),
        "b24b1f21-1a8e-445c-b0d6-369a49e401db": ("Stonecrush rockgrinder", (354319, 720007, 974), (354611, 719733, 671)),
        "7ec9ea4a-9d4c-46c9-a630-6b0ae2ab4092": ("stonecrush rockgrinder", (353854, 721169, 4075), (354914, 720417, 630)),
        "83d5f507-da49-44fe-ba77-15395e93a459": ("stonecrush rockgrinder", (353947, 720974, 4002), (355007, 720222, 491)),
        "51d45901-09ae-4f22-89ef-4b85bc8e1b3a": ("piper fairy", (642549, 338064, 6005), (642224, 337501, 5519)),
        "65bf0ce8-2719-4714-be72-f986789defef": ("piper fairy", (649483, 339076, 5831), (649158, 338513, 5654)),
        "b110649c-1b9b-4669-b32a-e7886ec0ccdc": ("piper fairy", (643580, 338033, 6040), (643255, 337470, 5560)),
        "c77cb1dd-c94f-4a38-800b-244d3b2ab189": ("piper fairy", (643345, 337950, 5964), (643020, 337387, 5469)),
        "77d1b3cc-e83e-4077-834c-dbe4a947c0fd": ("albion waylayer", (609426, 339730, 6965), (609772, 338633, 6775)),
        "b3564ab0-f65f-4ad8-90b1-2a316f2ecb2f": ("albion waylayer", (609670, 339394, 6886), (610016, 338297, 6808)),
        "d1a94984-0b64-474f-80fb-2c052cad8e29": ("albion waylayer", (610647, 339845, 6319), (610993, 338748, 6348)),
        "de9e1bb5-dfdb-4cbf-a1c7-9b7b7835813b": ("albion waylayer", (611573, 340866, 5786), (611919, 339769, 5524)),
        "e745481b-a71e-4945-87f7-2b490fdb81ec": ("albion waylayer", (611045, 340815, 6157), (611391, 339718, 5705)),
        "7f0521fa-fcf1-46a0-b8fb-59eb83f98064": ("goblin scout", (537180, 495070, 3511), (537472, 494796, 3296)),
    },
    "add-periodspawns165.json": {
        "fc990256-34e2-5154-9331-c956794a0708": ("giant frog", (495820, 642622, 3057), (495845, 642123, 2882)),
    },
    "add-primaspawns165.json": {
        "e2ac16aa-e0c0-5a6d-be5c-2ba744d85185": ("greenhorn poacher", (392656, 705520, 4063), (390536, 706937, 2089)),
        "3b1c9e6c-5bf7-5024-8047-f96171b76d32": ("greenhorn poacher", (392906, 705020, 4133), (390786, 706437, 2393)),
        "a69dbaec-e808-5fca-b3ac-56a77419314d": ("greenhorn poacher", (393156, 705620, 4104), (391036, 707037, 2364)),
    },
}


def rules_entries():
    out = []
    for mid, name, reg, (ox, oy, oz), (nx, ny, nz), why in BASE_MOVES:
        out.append({"sql": f"UPDATE Mob SET X = {nx}, Y = {ny}, Z = {nz} WHERE Mob_ID = '{mid}' AND Region = {reg} "
                           f"AND X = {ox} AND Y = {oy}",
                    "reason": TAG + f"{name} ({mid}) at {ox}, {oy}, Z {oz} {WHY[why]}; now {nx}, {ny}, Z {nz}; "
                                    f"nothing else changed (build_wallfix_alb165.py)"})
    return out


# Grimspound, archived (owner 2026-11-12 "remove it")
GRIMSPOUND_MOBS = {
    "12016664-0f0b-4b4c-a677-3a3accda73b0": "Kel", "53f56ece-6011-4ead-809f-ad1c1f89b429": "Kyl",
    "5c350cd8-1a56-4ca7-8b66-82aa5fe77dca": "Dannen", "60f9231c-877d-4b2d-942b-b2fdd08e7f7b": "Kan",
    "c0bb66be-9224-4f88-8175-91a3895a8942": "Los Ortiz", "ca9f1aba-c89f-4f09-8d04-e20671a7d280": "Kyr",
    "e21a9430-e563-4112-bc30-1dd114798380": "Lamont", "e82ccd2a-4c1c-40cb-9139-194b1eb2be04": "Kuz",
    "ec1eb7f1-eb0c-4f55-ac4b-86bf6b7e5015": "Kek", "fde7acf2-6fb6-4dd0-bd54-ae5bfd436a4e": "Kon",
}
GRIMSPOUND_OBJECTS = {
    "60e7cc52-9062-4056-a382-0525c03b043d": "Stretcher", "82d0c64f-b2f6-432c-84cc-313fa1d7009e": "Forge",
    "cf6544ed-54ac-40bf-a600-447b38c7608d": "Loom", "e7c28ca6-54a1-4bb3-a3b9-77393eec7c70": "Alchemy Table",
}
KEL_LIST = "60848b48-e669-45de-bd47-8d23b98f778a"
KEL_LIST_ROWS = 19
NOT_165 = ("not 1.65 content (owner 2026-11-12: 'remove it'): no period source (2003 or earlier) names Grimspound; the "
           "real 1.64 client has no town on that Dartmoor plateau and its invisible walls seal it off; the camp's "
           "crafting stations stood 2,800 under the 1.64 terrain (a later map); Los Ortiz is a 'Dragonslayer "
           "Journeyman', a later system")


def where_entries():
    inlist = lambda d: ", ".join(f"'{k}'" for k in sorted(d))
    names = lambda d: ", ".join("'" + v.replace("'", "''") + "'" for v in sorted(set(d.values())))
    return [
        {"table": "Mob", "key": "Mob_ID",
         "where": f"Mob_ID IN ({inlist(GRIMSPOUND_MOBS)}) AND Name IN ({names(GRIMSPOUND_MOBS)}) AND Region = 1",
         "reason": TAG + "Grimspound (Dartmoor) NPCs Kel, Kuz (merchants), Kan (smith), Kyr (healer), Kek (enchanter), "
                         "Kon (recharger), Kyl, Dannen, Lamont, Los Ortiz archived: " + NOT_165 +
                         " (build_wallfix_alb165.py)"},
        {"table": "WorldObject", "key": "WorldObject_ID",
         "where": f"WorldObject_ID IN ({inlist(GRIMSPOUND_OBJECTS)}) AND Name IN ({names(GRIMSPOUND_OBJECTS)}) "
                  f"AND Region = 1",
         "reason": TAG + "Grimspound's four crafting stations (Stretcher, Forge, Loom, Alchemy Table) archived with "
                         "its NPCs: " + NOT_165 + " (build_wallfix_alb165.py)"},
        {"table": "MerchantItem", "key": "MerchantItem_ID",
         "where": f"ItemListID = '{KEL_LIST}'",
         "reason": TAG + f"Kel's merchant list ({KEL_LIST_ROWS} crafting-supply rows), used by no NPC but Grimspound's "
                         "Kel, archived with him: " + NOT_165 + " (build_wallfix_alb165.py)"},
    ]


def apply_rules(rules):
    mine = lambda e: str(e.get("reason", "")).startswith(TAG)
    rules["updates"] = [u for u in rules["updates"] if not mine(u)] + rules_entries()
    rules["remove"]["where"] = [w for w in rules["remove"]["where"] if not mine(w)] + where_entries()


def check_clean():
    c = sqlite3.connect("file:" + CLEAN + "?mode=ro", uri=True)
    for mid, name, reg, (ox, oy, oz), _new, _why in BASE_MOVES:
        r = c.execute("SELECT Name, Region, X, Y, Z FROM Mob WHERE Mob_ID = ?", (mid,)).fetchone()
        assert r == (name, reg, ox, oy, oz), (mid, r)
    for mid, name in GRIMSPOUND_MOBS.items():
        assert c.execute("SELECT Name FROM Mob WHERE Mob_ID = ?", (mid,)).fetchone() == (name,), mid
    for wid, name in GRIMSPOUND_OBJECTS.items():
        assert c.execute("SELECT Name FROM WorldObject WHERE WorldObject_ID = ?", (wid,)).fetchone() == (name,), wid
    assert c.execute("SELECT COUNT(*) FROM MerchantItem WHERE ItemListID = ?", (KEL_LIST,)).fetchone()[0] == KEL_LIST_ROWS
    users = c.execute("SELECT COUNT(*) FROM Mob WHERE ItemsListTemplateID = ? AND Mob_ID <> ?",
                      (KEL_LIST, "12016664-0f0b-4b4c-a677-3a3accda73b0")).fetchone()[0]
    assert users == 0, "Kel's merchant list has another user"


def dump(data, newline, final):
    out = json.dumps(data, indent=1, ensure_ascii=False).encode("utf-8").replace(b"\n", newline)
    return out + newline if final else out


def fix_addfile(name, moves):
    raws = [open(os.path.join(f, name), "rb").read() for f in FOLDERS]
    assert raws[0] == raws[1], f"the two copies of {name} differ: stop and tell the lead"
    raw = raws[0]
    nl = b"\r\n" if b"\r\n" in raw else b"\n"
    data = json.loads(raw.decode("utf-8"))
    final = raw.endswith(nl)
    assert dump(data, nl, final) == raw, f"{name} does not round-trip byte for byte; not touching it"
    key = data["key"]
    done, changed = set(), 0
    for row in data["rows"]:
        mv = moves.get(str(row[key]))
        if not mv:
            continue
        nm, old, new = mv
        assert row["Name"] == nm, (name, row[key], row["Name"])
        cur = (row["X"], row["Y"], row["Z"])
        assert cur in (old, new), f"{name} {row[key]} at {cur}, expected {old} or {new}"
        if cur != new:
            row["X"], row["Y"], row["Z"] = new
            changed += 1
        done.add(str(row[key]))
    assert done == set(moves), f"{name}: rows not found: {set(moves) - done}"
    if changed:
        out = dump(data, nl, final)
        for f in FOLDERS:
            open(os.path.join(f, name), "wb").write(out)
    return changed


def main():
    check_clean()
    with locks.lock("rules"):           # add-files other lanes may also edit: one writer at a time
        for name, moves in ADDFILE_MOVES.items():
            print(f"{name}: {fix_addfile(name, moves)} row(s) moved")
    size = locks.edit_rules(LANE, apply_rules)
    print(f"true165-rules.json: {len(rules_entries())} updates + {len(where_entries())} remove.where tagged {TAG!r} ({size} bytes)")


if __name__ == "__main__":
    main()
