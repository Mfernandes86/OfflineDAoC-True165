# FIX-GUIDE-07: how to write a 0.7 quest fix

This guide is for the agents fixing the 0.7 quest audit (part01 to part10), the patrol fixer, and the integrator.
The owner approved fixing every audit finding and the two GitHub issues for 0.7.

**The short version.** You write JSON *fragments* into your own folder only:
`tools\claude-version\true165\questfix07\<fixer>\`. Run `build_questfix07_165.py --check` until it reports
no errors, then run `verify_questfix07.py --quick`. Only the integrator runs the generator for real, and that run
writes the content files the launcher applies. Nobody writes a game database or a content file by hand.

```
cd tools\claude-version\true165\questfix07   (from the install folder)
python -I build_questfix07_165.py --check                      # validate all fragments (~5 s), writes nothing
python -I build_questfix07_165.py --out <scratch dir> --reseed # write into a copy, then look at the diffs
python -I verify_questfix07.py --quick                         # scratch world from the live world + only our output (~10 s)
python -I verify_questfix07.py                                 # scratch world from the shipped v0.6 world + all content (~40 s)
python -I build_questfix07_165.py                              # INTEGRATOR ONLY: write the real content files
```

Fixer folders: `part01` to `part10`, `patrols`, `integrator`. The loader ignores folders whose names start with `_` or
`.` (worked examples are in `_examples\`). Any other folder name is an error.

---

## 1. How data reaches the True 1.65 world

The game server reads `runtime\data\opendaoc-165.sqlite3.db`. Players get the shipped world (v0.6). The launcher then
brings that world up to date from the content folder `runtime\true165\` with `TrueMode.Refresh`
(`development-source\tools\OfflineDaoc.Launcher\TrueMode.cs`; `Derive` does the same on a fresh copy). It does this
whenever the content changed, with the server stopped. The steps always run in this order:

| # | Step | From | What it does |
|---|------|------|--------------|
| 1 | settings | `true165-rules.json` `settings` | ServerProperty values |
| 2 | **removals** | `true165-rules.json` `remove` (`mob_classes`, `mob_packages`, `mob_names`, `mob_guilds`, `loot_generators`, `where`) | Rows are moved into `offline_true165_removed` (archived, never just deleted) |
| 3 | **updates** | `true165-rules.json` `updates` (`{sql, reason}`, about 12,300 of them) | Raw SQL, run in file order. **One failing statement aborts the whole refresh.** |
| 4 | period drops | `drops165.json` (`monsters: {name: {drop_count, items: [[Id_nb, chance]]}}`) | All earlier "period drops" LootTemplate rows are deleted and re-added. An item already in the monster's loot template is skipped. The MobXLootTemplate cap is only ever *raised* to `drop_count`. |
| 5 | **add-files** | every `add-*.json` (`{table, key, reason, rows}`), in ordinal file-name order | Row not there: inserted and tracked in `offline_true165_added`. Row there and added by this mode: **rewritten with the file's values**. Row there but not ours: left alone. Rows the file added before but no longer lists: deleted. |

`runtime\server\classic-quests.json` (quest markers, OnlyDuring/ExcludedBy, event spawns, Custom steps, Chat, quest
monsters) and `classic-quest-guides.json` (the Quest Guide text) are not world data. The server reads them next to
CoreServer.exe and reloads them every 5 seconds. `QuestTalkTruce` ("talk truce") is computed from the DataQuest rows, so
it follows any DataQuest fix automatically.

### THE RULE: updates run before add-files

An update to a row that an add-file creates does nothing on a fresh world, because the row does not exist yet. On a
refreshed world it is overwritten one step later. **A fix to a row an add-file creates must change that add-file's row**
(fragment kind `add_edit`). `--check` finds every update or remove whose rows are tracked as added by an add-file, and
reports it as an error that names the file. drops165.json rows work the same way: use a `drop` entry, not an update on
LootTemplate.

### Which generator writes which file

Most content files are generated. When a generator runs again it rewrites its file, so a hand edit there is lost.
questfix07 deals with this in three ways:

* **Updates and removes.** Its entries in `true165-rules.json` carry the reason prefix `True 1.65 0.7 questfix07`.
  Every other generator only strips its own tag (`True 1.65 Prima quest`, `True 1.65 batch 36`, ...), so this block
  survives them.
* **New rows.** These live in questfix07's own `add-questfix07-<table>.json` files. No other generator writes those
  files.
* **Edits inside files questfix07 does not own** (`add_edit` → `add-*.json`, `drop` → `drops165.json`, `quest` →
  `classic-quests.json`). Each edit is recorded in a ledger (`questfix07\_state\ledger.json`). On the next run the
  generator takes it back out (where it is still there unchanged) and applies the fragments again. **After any
  generator below rewrites one of these files, run `build_questfix07_165.py` again.** `--check` prints a NOTE for
  every add-file you edit that has a known generator.

| File | Written by |
|------|-----------|
| `add-primaquests165.json`, `add-primaquestitems165.json` | build_primaquests165.py (also rewrites its entries in classic-quests.json; build_dfspawntrim165.py touches quests) |
| `add-primaitems165.json`, `add-primaspawns165.json`, `add-primatemplates165.json` | prima_batch_2026-11-07/build_prima_batch165.py |
| `add-primaencounters165.json`, `-enctemplates`, `-paths`, `-pathpoints` | build_encounters165.py |
| `add-restoredspawns165.json` | build_twins165.py, build_dfspawntrim165.py, build_encounters165.py, fix_offmesh_spawns165.py |
| `add-periodspawns165.json` | fix_offmesh_spawns165.py |
| `add-inns165.json`, `add-paths165.json`, `add-pathpoints165.json` | build_inns165.py (+ fix_huginfell_route165.py) |
| `add-routefloor165.json` | build_routefloor165.py, refine_routefloor165.py, fix_routebridges165.py, fix_innisbridge165.py |
| `add-droptwins165.json` / `dropfix165.json` | build_dropfix165.py |
| `drops165.json` | build_drops165.py, then edited in place by build_dropfix165.py, build_prima165.py, the Prima batch (out\ copy), build_moonstone165.py, fix_batch36_165.py |
| `add-bgspawns165.json`, `add-bgkeeps165.json`, `add-thidranki165.json` | build_bgspawns165.py, build_bgkeeps165.py |
| `add-keepguards165.json`, `add-gates165.json`, `add-navmesh164-doors.json` | build_keepguards165.py, build_gates165.py |
| `add-recipes165-*.json` | build_recipes165.py |
| vendor and merchant files (`add-merchant*`, `add-vendoritems165`, `add-goodsitems165`, `add-deaditems165`, `add-missingmerchant*`) | vendor_work scripts, build_merchantfix2_165.py, build_goodsitems165.py |
| `classic-quests.json` | tools\claude-version\fix_*.py quest tools, build_primaquests165.py, build_twins165.py, build_rindakeys165.py, build_stacks165.py, build_questmodels165.py |
| no generator (hand-made) | add-coinonly*, add-dropitems165, add-epics165, add-npctemplates165, add-siegeammo165-merchantitems, add-spectralprovisioner165, add-trophies165, add-uthgarditems165 |

**Sweepers.** build_lootprices165.py, build_trashlevel165.py, build_stacks165.py, build_itemmodels165.py and
build_merchantrealm165.py rewrite rows in *every* add-file, ours included. The next questfix07 run writes the
fragment values back over theirs, and prints a NOTE for every own row whose file value differed. If a sweep's value is
right (for example a Price), put it into your fragment row.

`derive_true165_database.py` / `content165.py` are the old Python tools behind the launcher's `Derive`. They have
no "rewrite our rows" and no "remove unlisted rows" step. The launcher is the real path, and `verify_questfix07.py`
carries a Python copy of `TrueMode.Refresh` (`qf07common.launcher_refresh`) for that reason.

---

## 2. Choosing the kind of entry

| You need to... | Kind |
|---|---|
| change a row that is in the shipped/base world (a DataQuest 1-21530, a base Mob, an ItemTemplate no add-file creates) | `update` |
| change a row an `add-*.json` creates (the DataQuests 21531-21540, Prima spawns and items, restored spawns, ...) | `add_edit` (`set`) |
| take away a base-world row (a freeshard quest, a stray spawn, a wrong base loot row) | `remove` (archived, so it can be restored) |
| take away a row an add-file creates | `add_edit` with `"delete": true` |
| add a new row (an NPC or spawn, a period item, a quest, a path and its points, a merchant row, a loot row) | `row` |
| make a monster drop an item (or stop a drops165 drop, or raise its per-kill cap) | `drop` |
| quest markers, OnlyDuring / ExcludedBy, Races, XpShares, event Spawn, Custom steps, NeedsItem/Issuer, Grant, QuestMonsterIds, Fellows, ClassRewards, Chat, Quest Guide lines | `quest` |

To find out who owns a row, run `--check`: it tells you. You can also read the live world read-only, with
`SELECT Reason FROM offline_true165_added WHERE SourceTable='DataQuest' AND RowKey='21531'`. A reason maps to an
add-file's `reason`. No row there means a base row (`update`). A reason starting with `period drops:` means
drops165.json (`drop`).

---

## 3. The fragment format

A fragment is any `*.json` file in your folder. Files load in name order, and so do the entries inside a file. You
can keep one file per finding group (`a1-killtask-drops.json`, `a2-startnames.json`, ...).

```json
{
  "fixer": "part01",
  "about": "optional free text",
  "entries": [ { ... }, { ... } ]
}
```

Every entry has these fields:

| field | required | meaning |
|---|---|---|
| `kind` | yes | `update`, `remove`, `row`, `add_edit`, `drop`, `quest` |
| `finding` | yes | the audit finding id this fixes. Use `P<part>-<id>` (e.g. `P01-A2`, `P07-C3`), `PAT-<n>` for patrols, `GH-<issue>` for GitHub issues, `INT-<n>` for the integrator. Characters allowed: letters, digits, `. _ # : / -`. Several entries may share one finding id. |
| `why` | yes | one sentence with the evidence. It becomes the rules `reason`: `True 1.65 0.7 questfix07 <finding> [<fixer>]: <why>` |
| `source` | for new ItemTemplate rows, NPC speech (Chat, Greet) and Quest Guide text | the period source (Allakhazam page/zam id, Uthgard, Prima page, CapnBry sighting) |
| `note` | no | free text, ignored |

Unknown keys are errors. Column names are **case-sensitive**: the launcher silently drops a column whose case does
not match the table, so `--check` rejects it and names the right spelling. JSON `true`/`false` become 1/0.

### 3.1 `update`: change base-world rows

Use the structured form whenever you can. It is quoted for you, and `--check` can tell exactly which cells it touches.

```json
{"finding": "P01-A2", "kind": "update", "table": "DataQuest",
 "set": {"StartName": "Chelseigh Stillman"}, "where": {"ID": 169},
 "why": "StartName misspelled (Stilman), so QuestNames.Same never attaches the quest; the Camelot spawn (region 10, 36657/29331) is Chelseigh Stillman"}
```

`where` is ANDed column = value (`null` means IS NULL). For anything else, use `where_sql`:

```json
{"finding": "P05-B4", "kind": "update", "table": "Mob", "set": {"Level": 34},
 "where_sql": "Name = 'Gorgoth the Seer' AND Region = 100",
 "why": "Prima Midgard p.112 gives level 34 (approved Prima level change)"}
```

Raw SQL is allowed when an update needs an expression. It must be one `UPDATE <table> SET ... WHERE ...` (no `;`, no
table-wide update):

```json
{"finding": "P08-D1", "kind": "update",
 "sql": "UPDATE DataQuest SET StepText = replace(StepText, 'Cnieda', 'Cnieda the Smith') WHERE ID = 160",
 "why": "..."}
```

Remember that updates run before add-files. `--check` rejects updates on rows created by an add-file or by drops165.

### 3.2 `remove`: archive base-world rows

```json
{"finding": "P01-A1-259", "kind": "remove", "table": "DataQuest", "where": "ID = 259",
 "why": "Digested Skull XP item: no period kill-task page; freeshard like IDs 200 and 400-408 (rules where-rule 'freeshard XP items')"}
```

The rows go to `offline_true165_removed`, with the row's JSON and your reason, so they can be restored. Keep the
condition narrow: more than 200 rows gives a warning.

### 3.3 `row`: add a new row

New rows go into questfix07's own `add-questfix07-<table>.json` files. Give full rows: every column you leave out
takes the table default (`--check` warns when more than half are missing). The best way is to copy a similar existing
row and change it. `LastTimeRowUpdated` is filled in for you. Allowed tables: Mob, NpcTemplate, LootTemplate,
MobXLootTemplate, Path, PathPoints, MerchantItem, LootOTD, NpcEquipment, WorldObject, Spell, LineXSpell, Door,
ItemTemplate, DataQuest. For any other table, ask the integrator.

```json
{"finding": "P03-C2", "kind": "row", "table": "Mob",
 "row": {"Mob_ID": "00070301-0000-4000-8000-000000000001", "ClassType": "DOL.GS.GameNPC", "Name": "Tiomoid",
         "Region": 200, "X": 395597, "Y": 750473, "Z": 4947, "Heading": 2048, "Speed": 200, "Model": 446, "Size": 52,
         "Level": 35, "Realm": 0, "NPCTemplateID": 0, "Flags": 0, "AggroLevel": 0, "AggroRange": 0,
         "RespawnInterval": 300, "PathID": "", "PackageID": "true165-questfix07", "...": "every other Mob column"},
 "why": "quest giver missing from Lough Gur; CapnBry sighting zone 120 local 23149,26031"}

{"finding": "P01-A3", "kind": "row", "table": "ItemTemplate",
 "source": "Allakhazam item 4711 (archived 2004-03-02): AF 12, ABS 10, level 15",
 "row": {"Id_nb": "Cnieda_Gloves", "Name": "Cnieda's Gloves", "Level": 15, "Item_Type": 22, "Object_Type": 34, "...": "..."},
 "why": "reward item of quest 160 did not exist; built from the period item page"}

{"finding": "P02-B7", "kind": "row", "table": "DataQuest",
 "row": {"ID": 27100, "Name": "...", "StartType": 0, "StartName": "...", "StartRegionID": 101, "...": "..."},
 "why": "..."}
```

**Items.** Use the period Id_nb if there is one. Otherwise use `qf07_<fixer>_<snake_name>`. A reward item that does
not exist may be created **only from period item data** (stats from Allakhazam, Uthgard or Prima), with `source`. If
there is no period data, skip the item and say why in your report.

**Paths.** A Path row (`Path_ID` from your range, `PathID` = a new name such as `qf07_patrols_<route>`) plus
PathPoints rows (`PathID` = the same name, `Step` 1..n). A Mob uses it with `"PathID": "<name>"`.

**Restoring a period row with its own key.** For example, an archived CapnBry spawn that has a known Mob_ID. Add
`"keep_key": "<why>"` and the range check becomes a warning. If a remove rule matches the row, it is archived and
re-added on every refresh. verify_questfix07 reports that. Change the row so the rule no longer matches it, or ask the
integrator to narrow the rule. questfix07 never edits other generators' rules entries: the rule's own generator has to
change, or the next run of that generator puts the old rule back.

### 3.4 `add_edit`: change or delete a row an add-file creates

```json
{"finding": "P10-A4", "kind": "add_edit", "file": "add-primaquests165.json", "id": 21531,
 "set": {"MaxLevel": 12}, "why": "..."}

{"finding": "P06-E2", "kind": "add_edit", "file": "add-restoredspawns165.json",
 "id": "5b251f01-ed26-5cf8-b703-236ca33adad6", "delete": true, "why": "duplicate spawn standing in the quest giver"}
```

`id` is the value of the file's `key` column for that row. The number or text must match (it is compared as text).
The key column itself cannot be changed: delete the row and add a new one instead.

### 3.5 `drop`: drops165.json

```json
{"finding": "P01-A1-114", "kind": "drop", "mob": "farmer", "add": [["Sack_of_Grain", 6]],
 "why": "kill task 114 item drops from nothing; period kill task names Farmer (Lyonesse)", "source": "Allakhazam zam 426"}

{"finding": "P09-B2", "kind": "drop", "mob": "snow giant", "add": [["Ice_Water", 4]], "drop_count": 2, "why": "..."}

{"finding": "P04-C1", "kind": "drop", "mob": "abominable snowman", "remove": ["Water_Opal"], "why": "..."}
```

* `mob` is the monster name. drops165.json keys are lower case and the match ignores case. A new monster gets an
  entry with `drop_count` 1 unless you give one.
* `add` is a list of `[Id_nb, chance 1-100]`. Kill-task items: the audit's period rate, or about 6% (the median of
  killtask_drops_restored.json) when the period source has no number.
* An item that is already listed is an error unless you add `"replace": true`. Even then, the launcher skips an item
  the monster's loot template already has from the base world, so the chance cannot change that way. verify_questfix07
  says when that happens; use an `update` on that LootTemplate row instead.
* `remove` only removes drops165 items. A base-world LootTemplate row needs `remove` (kind) on LootTemplate.
* `drop_count` sets the cap in the file. In the world the cap is only ever raised.

### 3.6 `quest`: classic-quests.json / classic-quest-guides.json

`file` defaults to `classic-quests.json`. `path` is a list of keys. Quest ids are text (`"20213"`), step numbers are
list indexes (1-based, index 0 is null).

```json
{"finding": "P02-C1", "kind": "quest", "op": "set", "path": ["Quests", "21539", "OnlyDuring"],
 "value": ["The Growling Ghost"], "why": "..."}

{"finding": "P07-A1", "kind": "quest", "op": "set", "path": ["Quests", "20146", "Steps", 2, "Marker"],
 "value": {"Region": 51, "X": 552197, "Y": 512689, "Z": 3181}, "why": "marker pointed at the old camp; CapnBry sighting ..."}

{"finding": "P07-A2", "kind": "quest", "op": "merge", "path": ["Quests", "20390", "Steps", 6],
 "value": {"NeedsItem": "cq_humberton_tithe", "Issuer": "Belef"}, "why": "..."}

{"finding": "P03-D5", "kind": "quest", "op": "add_unique", "path": ["QuestMonsterIds"],
 "value": "00070301-0000-4000-8000-000000000001", "why": "gamebots must leave the new quest monster alone"}

{"finding": "P03-D6", "kind": "quest", "op": "remove_value", "path": ["Quests", "21540", "ExcludedBy"],
 "value": "Growler's Necklace", "why": "..."}

{"finding": "P05-F1", "kind": "quest", "op": "delete", "path": ["Quests", "20213", "Steps", 2, "Spawn"], "why": "..."}

{"finding": "P08-G1", "kind": "quest", "op": "set", "create": true, "path": ["Chat", "Master Vismer", "accomplish"],
 "value": "<the recorded period line>", "source": "Allakhazam quest page 'Lunaris' archived 2004-11-23", "why": "..."}
```

* `set` replaces the value at the path. Its parent must exist; `"create": true` makes missing objects and pads lists.
* `merge` sets each key of the object you give. It is the same as several `set`s, and other keys stay.
* `delete` removes a key. List items are never deleted, because that would renumber the steps: set them to `null`.
* `add_unique` adds a value to a list if it is not there (and creates the list). `remove_value` takes a value out.
* Property names are checked against the server's classes (QuestInfo, StepInfo, Point, EventSpawn, CustomStep,
  StepDrop, Guide). Unknown or wrongly cased names are errors, because the server would ignore them. Custom `Kind` is
  one of use_item, travel, has_item, interact, quest, group, die_to, trade, choose, say.
* **Never invent NPC dialogue.** Chat, Greet and Quest Guide entries need `source` and must be the recorded period text.
* Markers, objective colours and the Quest Guide stay on in True 1.65. Nothing is gated by Classic165Mode.

---

## 4. ID ranges for new rows

Each fixer has its own ranges, so parallel fixers never collide. `--check` rejects a key outside your range.

| fixer | DataQuest.ID | NpcTemplate.TemplateId | Spell.SpellID | GUID key (`TT` = table code) |
|---|---|---|---|---|
| part01 | 27000-27099 | 970701000-970701999 | 970100-970199 | `000701TT-0000-4000-8000-nnnnnnnnnnnn` |
| part02 | 27100-27199 | 970702000-970702999 | 970200-970299 | `000702TT-0000-4000-8000-nnnnnnnnnnnn` |
| part03 | 27200-27299 | 970703000-970703999 | 970300-970399 | `000703TT-0000-4000-8000-nnnnnnnnnnnn` |
| part04 | 27300-27399 | 970704000-970704999 | 970400-970499 | `000704TT-0000-4000-8000-nnnnnnnnnnnn` |
| part05 | 27400-27499 | 970705000-970705999 | 970500-970599 | `000705TT-0000-4000-8000-nnnnnnnnnnnn` |
| part06 | 27500-27599 | 970706000-970706999 | 970600-970699 | `000706TT-0000-4000-8000-nnnnnnnnnnnn` |
| part07 | 27600-27699 | 970707000-970707999 | 970700-970799 | `000707TT-0000-4000-8000-nnnnnnnnnnnn` |
| part08 | 27700-27799 | 970708000-970708999 | 970800-970899 | `000708TT-0000-4000-8000-nnnnnnnnnnnn` |
| part09 | 27800-27899 | 970709000-970709999 | 970900-970999 | `000709TT-0000-4000-8000-nnnnnnnnnnnn` |
| part10 | 27900-27999 | 970710000-970710999 | 971000-971099 | `000710TT-0000-4000-8000-nnnnnnnnnnnn` |
| patrols | 28000-28099 | 970711000-970711999 | 971100-971199 | `000711TT-0000-4000-8000-nnnnnnnnnnnn` |
| integrator | 28100-28199 | 970712000-970712999 | 971200-971299 | `000712TT-0000-4000-8000-nnnnnnnnnnnn` |

Table codes `TT`: Mob 01, NpcTemplate 02, LootTemplate 03, MobXLootTemplate 04, Path 05, PathPoints 06,
MerchantItem 07, LootOTD 08, NpcEquipment 09, WorldObject 10, Spell 11, LineXSpell 12, Door 13. `n` is 12 decimal
digits counting up from 1.

Example: part04's third new Mob is `00070401-0000-4000-8000-000000000003`, and its first PathPoints row is
`00070406-0000-4000-8000-000000000001`. An NpcTemplate row needs both its `NpcTemplate_ID` (code 02) and a
`TemplateId` from the integer range.

All ranges were checked free in the live and v0.6 worlds on 2026-11-11 (DataQuest max 21540, NpcTemplate has nothing
in 97xxxxxxx, no Spell in 970000-979999). ItemTemplate (Id_nb) and Path names (PathID) have no ranges: they must be
unique across the world, every add-file and every fragment, and `--check` enforces that.

---

## 5. Testing a fragment

1. **`python -I build_questfix07_165.py --check`** reads all fragments (yours and everyone else's) and checks them
   against the content files and the live dev world, read-only. It takes about 5 s. It reports:
   * shape errors, unknown keys and columns (case-sensitive), wrong types, keys outside your range, keys that already
     exist (world, any add-file or another fragment), missing `finding`/`why`/`source`;
   * SQL that does not compile (an `EXPLAIN` on the read-only world), updates and removes that hit add-file or
     drops165 rows, updates that match nothing (warning);
   * **conflicts between fixers**, which are errors that name both entries: two updates writing the same cell (table,
     rowid, column) with different values (found by running each WHERE on the world); two edits of the same add-file
     row and column, or a delete against an edit; the same drop item or different `drop_count`s on one monster;
     overlapping quest paths. Identical duplicates are warnings;
   * references to items, templates and paths that do not exist (warnings), and classic-quests property names
     (errors);
   * NOTES for edited add-files that a generator rewrites.

   Exit code 1 if there are errors. Fix them all. A conflict with another fixer goes to the integrator. Do not edit
   the other fixer's folder.
2. **`python -I build_questfix07_165.py --out <dir> --reseed`** writes the merge into a copy (`<dir>\true165`,
   `<dir>\server`) so you can diff it against `runtime\true165` and `runtime\server`. Keep `<dir>` in your
   scratchpad.
3. **`python -I verify_questfix07.py --quick`** builds a scratch world: a copy of the live dev world plus only the
   questfix07 output (our tagged rules entries, our add-files, the add-files we edit, the drops of the monsters we
   touch). It then checks every entry in the world. About 10 s.
4. **`python -I verify_questfix07.py`** (full) builds a copy of the shipped v0.6 world, then applies the *whole* content
   (all rules, drops, every add-file) with the launcher's refresh logic. That is what a player updating from v0.6
   gets. Measured at **about 40 s** in total (merge 6 s, copy 1 s, refresh 34 s).

Both verify modes write only into
a scratch folder of your own (pass it with `--world`).
Use `--world <dir>` for a private copy when several agents verify at the same time. Each run prints the path of the
scratch world. The checks cover: update cells hold the new values, rows and edits are present with their values,
drops are in LootTemplate at your chance with the cap raised, removed rows are gone, quest JSON holds the results,
none of our rows is matched by a remove rule, and every touched DataQuest has its start NPC in its region, its target
spawns (info) and existing items. `--quick-check` adds `PRAGMA quick_check`.

You can open the scratch world read-only to look further, but never the real worlds for writing.

---

## 6. Hard rules (owner)

* Period sources first: Allakhazam (classic quests archive), CapnBry sightings, Uthgard, the Prima guides, the
  wayback item pages (`development-source\research\period-data\`, `read_archive.py`; run Python on archive data with
  `python -I`).
* Never invent NPC dialogue. Only HIGH-likelihood period items and drops. A missing reward item may be created only
  from period item data; if there is none, skip it and say why.
* Never change combat or difficulty numbers. The only exception is Prima-documented monster level, aggro and speed
  (approved). Never lower boss levels. No starter armor for bots.
* Quest QoL (markers, objective colours, Quest Guide) stays on and is never gated by Classic165Mode.
* Never write the game DBs. Never hand-edit content files. Fixers write only their own folder. Only the integrator runs
  the real build.
* Never touch frozen release folders (`release-true165-v0.*`), GitHub or the owner's clone. No pushes.
* Never query Account, DOLCharacters, CharacterX* or other player tables.
* 1,000-2,000 bots per realm: nothing heavy per tick. That matters for anything that changes server behaviour; data
  rows are fine.
* Bounty and battleground quests are OFF in True 1.65: ignore those findings.
* Every long command gets a hard timeout. Run probes once.

---

## 7. Integrator procedure

1. `python -I build_questfix07_165.py --check`: no errors, and read the warnings. Settle conflicts (one fragment
   changes, the other stays). Resolve duplicates.
2. `python -I verify_questfix07.py`: full mode, 0 problems.
3. With the dev server stopped (the content only matters at the next launcher start anyway), run
   `python -I build_questfix07_165.py`. It writes **both** content copies (`runtime\true165` and
   `tools\claude-version\true165`, which must be byte-identical first), `runtime\server\classic-quests.json` /
   `classic-quest-guides.json`, and the ledger `questfix07\_state\ledger.json`. A second run writes the same bytes.
4. If a sweeper has to run afterwards (prices for new items, for example), run it, then copy its values into the
   fragments and re-run step 3, so the next questfix07 run does not undo them.
5. The launcher applies the new content on its next start (content stamp changed), backing the world up first.
6. Add CHANGELOG lines only when the owner says so (GitHub hold).

Withdrawing a fix means deleting its fragment entry and re-running. The rules block, the own add-files and the ledger
edits all follow. A table whose rows all disappear keeps an empty own add-file, so the launcher deletes the rows it
added before. Once 0.7 ships, never withdraw a new ItemTemplate row: owned copies of the item would lose their
template.

## 8. Files here

| file | what |
|---|---|
| `build_questfix07_165.py` | the merge (`--check`, `--out`, `--reseed`, `--fragments`, `--db`) |
| `verify_questfix07.py` | scratch world + checks (`--quick`, `--world`, `--base`, `--fragments`, `--quick-check`) |
| `qf07common.py` | shared code: validation, merge, ledger, launcher refresh port |
| `part01`..`part10`, `patrols`, `integrator` | fixer fragment folders |
| `_examples\` | worked examples of every kind (`--fragments _examples` checks them); never applied |
| `_state\ledger.json` | written by the real run; do not edit |

## 9. Quest engine features added 2026-11-11 (use them in `quest` entries)

All are optional keys of classic-quests.json; `--check` knows them. A custom step's DataQuest StepType is `8` (Search,
no search area) so that only the engine completes it; a custom step that ends the quest uses `9`.

- **Whisper step with a choice of words.** `Steps[n].Custom = {"Kind": "whisper", "Target": "<NPC name>", "Words": "knowledge|power"}`.
  Any of the words whispered (or clicked) to the NPC completes the step; talking to the NPC shows the journal step with the
  words as links. Use it where the period lets the player answer one of several things.
- **Side turn-ins.** `Quests.<id>.SideTurnIns = [{"Item": "<Id_nb handed in>", "Target": "<NPC name>", "Reward": "<Id_nb>",
  "FromStep": 1, "ToStep": 0, "DropFrom": "<monster name>", "DropChance": 100, "Thanks": "<recorded line or omit>"}]`.
  Handing Item to Target while the quest is on a step from FromStep to ToStep (0 = to the end) takes it and gives Reward
  (the player's ClassRewards version if there is one), once per quest; the quest's step does not change. DropFrom: that
  monster drops Item for a player on the quest (in the window) who has not handed it in and holds none. The item should be
  a quest token (Object_Type 0, Price 0).
- **Worn items.** `Steps[n].Wearing = ["<Id_nb>", ...]`: the step (any type: search, kill, travel, use_item...) does not
  advance or finish unless all are equipped; the player is told which one is missing.
- **Own spawn only.** `Steps[n].OwnSpawnOnly = true` on a kill step with a `Spawn`: only the quest's event monster counts,
  not a permanent monster of the same name; the event monster also appears when a permanent one stands near.
- **Event monsters that walk a path.** `Steps[n].Spawn.PathId = "<PathID>"` (Path + PathPoints rows added with `row`
  entries, as the patrols did): the monster appears at the Spawn's X/Y/Z (make that the path's first point) instead of
  beside the player, walks the path, and its `Escort` (`[{"Name", "TemplateId", "Level", "Model", "Count", "Peaceful"}]`)
  follows it and shares its fights. It is not sent at the player; it attacks whoever comes within its aggro range
  (its template's, or level 100 / range 500 when the template has none). TriggerRadius is measured from the Spawn point:
  make it large enough that a player approaching the route makes it appear, and give it a longer DespawnSeconds.
- **Mid-quest items.** `Steps[n].Grant` items are now given in the player's ClassRewards version, and a real item
  (anything but an Object_Type 0 / Price 0 token) is granted once and remembered across server restarts.
- **Hour windows.** Top-level `HourWindows.<NPC name> = {"Start": 0, "End": 3}` (game hours, End may be 24, Start above
  End wraps past midnight) for a Mob whose ClassType is `DOL.GS.HourWindowSpawn`.
