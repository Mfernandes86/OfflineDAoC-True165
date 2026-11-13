"""True 1.65 0.8 siege165 (lead brief, lane SIEGE165, 2026-11-12). Report: <lead workspace>/wallaudit\\reports\\SIEGE165.md.

The owner: "remove them from the battlegrounds and make the gamebots get their siege equipment and ammo elsewhere".

A. The 12 battleground "Siege Master" merchants (SiegeMerchantAlb/Mid/Hib; Caledonia 250: 3, Murdaigean 251: 3,
   Thidranki 252: 6, Abermenai 253: none) are archived by one remove.where rule, by Mob_ID and list.
   NOTE for the owner (period check, see the report): the 1.53D notes (Freddyshouse 97993, 2002-09-12) added a "Siege
   Master" NPC to every realm's battleground portal keep, so a battleground siege NPC was in the game by 1.65. It sold
   tokens and walked out to build the engine; these 12 rows are plain merchants selling finished deeds. The removal is
   the owner's order; REMOVE_BG_SIEGEMASTERS = False and a rerun drops the rule again.

B. The old base rule "ItemsListTemplateID LIKE 'SiegeMerchant%' AND Region IN (1,100,200)" ("frontier siege equipment
   was crafted; only the battleground Siegemasters sold it") is replaced: with A it would leave a player no merchant for
   siege equipment anywhere, and the 1.64 Siegecraft parts (ram cladding, beak, harness, ...) are not sold by any
   spawned NPC in this world, so a ram could not be crafted either. Its six archived rows come back from
   offline_true165_removed through add-siege165.json: the border keep siege merchants of the 1.51G notes (FH 90931,
   2002-06-20: Castle Sauvage, Snowdonia Fortress, Vindsaul Faste, Svasud Faste, Druim Ligen, Druim Cain), each next to
   its keep's medallion merchant / teleporter. Their list keeps the catapult stones and ballista bolts of
   add-siegeammo165-merchantitems.json; the medallion merchants keep theirs (1.54f, FH 101036).

    python -I build_siege165.py      rerunnable: its own rules entries are replaced, the add-file is rewritten
Out (both copies, runtime\\true165 and tools\\claude-version\\true165): true165-rules.json (through
<lead workspace>/gamefix\\locks.py edit_rules), add-siege165.json.
Reads the clean v0.7 world read-only (mode=ro) to check the rows and take the archived copies.

UPDATE 2026-11-12 (lane BGCONTENT, <lead workspace>/bgfix\\reports\\BGCONTENT.md; owner: "put them back. make the bots use them"):
  * REMOVE_BG_SIEGEMASTERS = False: the 12 battleground Siege Masters stay (1.53D, FH 97993, 2002-09-12: a Siege Master
    "at your realm's portal keep within the Battlegrounds"; still selling rams, catapults, trebuchets and ammo in 2003:
    FH 116313 2003-03-02, FH 129325 2003-06-03, FH 141297 2003-08-12). Abermenai's three come from build_bgfix165.py.
  * KEEP_BORDER_SIEGE_MERCHANTS = False: the six border keep siege merchants go back to the archive. The 1.51G note
    that added them is under "MORDRED WORLD NOTES" (FH 90931): the PvP server only. On a normal server players still
    complained of no siege merchant near the battlegrounds (FH 98180, 2002-09-19), went to the merchant keeps for ammo
    (FH 105630, 2002-11-28), and 1.54f made the portal ticket vendors sell ammo (FH 101036). So the old frontier rule
    (exact v0.7 text) is put back in its place and add-siege165.json is deleted from both copies.
"""
import json, os, sqlite3, sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

LANE = "SIEGE165"
TAG = "True 1.65 0.8 siege165: "
REMOVE_BG_SIEGEMASTERS = False  # owner 2026-11-12 (later): "put them back. make the bots use them."
KEEP_BORDER_SIEGE_MERCHANTS = False  # 1.51G Mordred-only (see the update note above)

HERE = os.path.dirname(os.path.abspath(__file__))
RT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
FOLDERS = [os.path.join(RT, "runtime", "true165"), HERE]
CLEAN = os.environ.get("OFFLINE_DAOC_CLEAN_DB") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "runtime", "data", "opendaoc-165.sqlite3.db")   # the clean world (database/unpack_db.py)
ADDFILE = "add-siege165.json"

LISTS = ("SiegeMerchantAlb", "SiegeMerchantMid", "SiegeMerchantHib")
# region -> Mob_IDs of its battleground Siege Masters (base world rows)
BG_SIEGEMASTERS = {
    250: ["fa6d4704-eea5-41d5-be63-2ef4e6cb28d9", "b2c57bfe-388a-4e3f-9da9-a105d17ff95b", "aedd57dd-4a0b-4b3e-8ff4-5df13058fc7b"],
    251: ["3c34287b-aa10-438a-9bcb-482b088c5dee", "70a84a61-e472-4585-9213-86a46b412f57", "8ae1b50b-d05a-4b46-83ce-521c0a5684b0"],
    252: ["14640bd7-282f-49bd-a7e0-5b2d9fba8e42", "ec44d135-81c3-4301-9e2c-588c5d562955", "0580067f-cf96-4e40-8c20-077cdc313121",
          "aabd4b8c-dee7-4915-8b4e-25b5322b2df2", "486663df-2231-4e35-91fb-b8b839d25a70", "dfa362e2-7a06-4059-b500-6d374e91e6c9"],
}
# the six border keep siege merchants (1.51G), archived by the old rule
BORDER_SIEGE_MERCHANTS = {
    "f6decd62-c052-4bcf-abf8-c440b3097c88": "Cameron, Castle Sauvage",
    "5981df41-b524-474c-857e-499c491e8709": "Rhuz, Snowdonia Fortress",
    "8c56a4ab-dd79-4ac4-a285-b03d7f7b6388": "Zruch, Svasud Faste",
    "df945e91-b344-4432-87e6-773b12ef4802": "Jaso, Vindsaul Faste",
    "854192f1-962d-4d50-859e-d686e7217ffe": "Zrim, Druim Ligen",
    "7614433a-fe18-4984-8ecb-a3442c80fe59": "Sulvan, Druim Cain",
}
OLD_FRONTIER_WHERE = "ItemsListTemplateID LIKE 'SiegeMerchant%' AND Region IN (1,100,200)"
OLD_FRONTIER_REASON = "1.65: frontier siege equipment was crafted (Siegecraft); only the battleground Siegemasters sold it"

sql_list = lambda ids: ",".join(f"'{i}'" for i in ids)


def read_world():
    con = sqlite3.connect("file:" + CLEAN.replace(" ", "%20") + "?mode=ro", uri=True)
    bg = {r[0]: r for r in con.execute(
        "SELECT Mob_ID, Name, Region, ItemsListTemplateID FROM Mob WHERE ItemsListTemplateID IN (%s)" % sql_list(LISTS))}
    want = {i for ids in BG_SIEGEMASTERS.values() for i in ids}
    assert set(bg) == want, ("siege merchant rows in the clean world differ from the 12 battleground ones", set(bg) ^ want)
    for region, ids in BG_SIEGEMASTERS.items():
        for i in ids:
            assert bg[i][2] == region, (i, bg[i])
    archived = {}
    for (row_json,) in con.execute("SELECT RowJson FROM offline_true165_removed WHERE SourceTable='Mob' AND Reason=? ORDER BY Id",
                                   (OLD_FRONTIER_REASON,)):
        row = json.loads(row_json)
        archived[row["Mob_ID"]] = row          # the latest copy of each row wins
    assert set(archived) == set(BORDER_SIEGE_MERCHANTS), ("archived border siege merchants", set(archived) ^ set(BORDER_SIEGE_MERCHANTS))
    for row in archived.values():
        assert row["Region"] in (1, 100, 200) and row["ItemsListTemplateID"] in LISTS and row["ClassType"] == "DOL.GS.GameMerchant", row
    con.close()
    return [archived[i] for i in sorted(archived, key=lambda k: (archived[k]["Region"], k))]


def write_addfile(rows):
    data = {
        "table": "Mob",
        "key": "Mob_ID",
        "reason": TAG + "the border keep siege merchants back (1.51G notes, Freddyshouse 90931, 2002-06-20: Castle Sauvage, "
                        "Snowdonia Fortress, Vindsaul Faste, Svasud Faste, Druim Ligen, Druim Cain), the supply of siege "
                        "equipment and ammunition players carried into the battlegrounds once the battleground Siege Masters "
                        "were removed (owner 2026-11-12); rows from offline_true165_removed (build_siege165.py)",
        "rows": rows,
    }
    out = json.dumps(data, indent=1, ensure_ascii=False).encode("utf-8") + b"\n"
    for f in FOLDERS:
        path = os.path.join(f, ADDFILE)
        if KEEP_BORDER_SIEGE_MERCHANTS:
            open(path, "wb").write(out)
        elif os.path.exists(path):
            os.remove(path)     # the old frontier rule archives the rows again (rules run before add-files)


def apply_rules(rules):
    where = rules["remove"]["where"]
    mine = lambda e: str(e.get("reason", "")).startswith(TAG)
    old = lambda e: e.get("table") == "Mob" and e.get("where") == OLD_FRONTIER_WHERE
    at = next((n for n, e in enumerate(where) if mine(e) or old(e)), len(where))
    keep = [e for e in where[:at] if not (mine(e) or old(e))]
    rest = [e for e in where[at:] if not (mine(e) or old(e))]
    new = []
    if REMOVE_BG_SIEGEMASTERS:
        ids = [i for region in sorted(BG_SIEGEMASTERS) for i in BG_SIEGEMASTERS[region]]
        new.append({
            "table": "Mob",
            "key": "Mob_ID",
            "where": f"Mob_ID IN ({sql_list(ids)}) AND ItemsListTemplateID IN ({sql_list(LISTS)}) AND Region IN (250,251,252)",
            "reason": TAG + "the 12 battleground Siege Masters (Caledonia 3, Murdaigean 3, Thidranki 6) removed (owner "
                            "2026-11-12: \"remove them from the battlegrounds\"); siege equipment and ammunition are bought "
                            "in the frontier (border keep siege merchants, add-siege165.json; medallion merchants sell "
                            "stones and bolts) and carried in, as players did (2002-08-31, FH 97308: \"set out to bled to get "
                            "ram supplies\"). This rule replaces the old frontier rule \"" + OLD_FRONTIER_WHERE + "\".",
        })
    if not KEEP_BORDER_SIEGE_MERCHANTS:
        new.append({"table": "Mob", "key": "Mob_ID", "where": OLD_FRONTIER_WHERE, "reason": OLD_FRONTIER_REASON})
    rules["remove"]["where"] = keep + new + rest


if __name__ == "__main__":
    rows = read_world()
    write_addfile(rows)
    size = locks.edit_rules(LANE, apply_rules)
    if KEEP_BORDER_SIEGE_MERCHANTS:
        for f in FOLDERS[1:]:
            assert open(os.path.join(FOLDERS[0], ADDFILE), "rb").read() == open(os.path.join(f, ADDFILE), "rb").read()
        print(f"{ADDFILE}: {len(rows)} border keep siege merchants ({', '.join(BORDER_SIEGE_MERCHANTS[r['Mob_ID']] for r in rows)})")
    else:
        assert not any(os.path.exists(os.path.join(f, ADDFILE)) for f in FOLDERS)
        print(f"{ADDFILE}: removed; the six border keep siege merchants stay archived (old frontier rule)")
    print(f"true165-rules.json: battleground rule {'on' if REMOVE_BG_SIEGEMASTERS else 'off'}, "
          f"old frontier rule {'replaced' if KEEP_BORDER_SIEGE_MERCHANTS else 'restored'} ({size} bytes)")
