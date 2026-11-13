# Notes from lane D-V (verifier, build_all, About page)

Updated 2026-11-12. Living file.

## What every data lane must keep true (so `build_all.py` and the checks work)

- Builder: `python -I source/build_<name>.py [--db <world>] [--out <data dir>]`, runnable alone with no arguments,
  exit code 0 only when the data file was written and its own asserts passed. `build_all.py` runs them one after
  another in this order: `maps, mobs, drops, vendors, routes, items, quests`, then any other `build_*.py`
  alphabetically. Why: mobs, vendors and routes read `zones.js`; drops reads `mobs.js` (script-made kinds); items
  reads `mobs.js` and compares itself with `drops.js` and `vendoritems.js`; quests reads zones, mobs, itemmeta and
  drops. `build_routes.py` imports `build_vendors.py` as code.
- Data file: exactly `WG.provide("<name>", <json>);` (use `wgcommon.write_data`). Under 15 MB.
- Document the real shape in `source/contract/<file>.md`. `verify_data.py` reads field names from those notes;
  when a field is renamed after I wrote the check, the check fails and I will ask.
- No sample rows left in `data/`.

## What I check, and how (all scripts print PASS / FAIL lines and exit non-zero on FAIL)

| Script | What | Against |
|---|---|---|
| `source/verify_drops.py` | every monster, every item of `data/drops.js` | my own reading of the loot code + a seeded Monte Carlo of the server's procedure |
| `source/verify_con.py` | `app/lib/concolor.js` as the browser runs it, my level -1..128 x monster level -1..128, levels(), bands(), span(), the presets; M-U's `con_vectors.json` | the table rebuilt from `ConLevels.cs` at check time (no copy of the table in the script) |
| `source/verify_prices.py` | merchant prices, stable routes (ticket, price, start point) | `GameMerchant.cs`, `GameStableMaster.cs` and the world tables |
| `source/verify_data.py` | cross-file integrity: ids, positions in zones, sizes, placeholders, counts against the database, monster names and level ranges against the spawn + template rule | the world tables and `GameNPC.cs` |

Test pages in `source/tests/*.html` load the tool's real scripts and are run in headless Edge by
`source/verify_jsrun.py` (there is no Node on this PC).

## Results so far (2026-11-12, against the data in `data/` at 17:15)

| Check | Result |
|---|---|
| Drops (`verify_drops.py`) | **PASS.** 2,525 monster names, 17,677 monster-item chances: 0 differ (largest difference 0.000001, rounding). DropCount, pool, loot realm, stack size, flags, quest ids, `byItem`, 76 one-time drops, coin chance of 7,177 kinds, 122 quest hand-overs, never-drop rows: all equal. My closed formula against a seeded simulation of `LootList.GetLoot`: 3,759 kill plans, 22,539 entries, 20,000 kills each, worst z 3.9. |
| Con (`verify_con.py`) | **PASS.** 2,226 of M-U's vectors, 16,900 cells of the real `con.js`, every band and preset. |
| Merchants and routes (`verify_prices.py`) | **PASS.** 1,217 merchants, 22,872 list rows (item, page, slot, price), 189 ticket routes = the 189 the server's rule yields. |
| Integrity (`verify_data.py`) | PASS except A1 (pages not written yet). 127 zones, 468 pictures, 22,405 items x 14 plain columns, 4,129 kinds (level ranges equal my reading of `Mob.Level` + template lists), 23,824 camps, 77,445 camp points on real spawns. |

What was reconciled, and who was right (the code decided each time):
- **Closed regions** (D-D): spawns in regions shut by `disabled_regions` / `disabled_expansions` are not in the world
  (`Region.cs:234-264`, entry refused in `RegionChangeRequestHandler.cs:76`). 124 kinds moved out of `mobs`,
  23 coin values changed. D-D fixed.
- **A Path row is optional** (my error): `MovementMgr.cs:102-112` rides the PathPoints even with no `Path` row.
  V-D had the route Snowdonia Fortress -> Castle Sauvage right; my first rule missed it.
- **Trailing space in a name** (M-D): "Snemel den Forsumlig " is looked up by its exact lower-cased name; key now untrimmed.
- **Effective class** (my error, M-D right): the class of a spawn is its NPC guild script, else the template's
  ClassType when the template replaces mob values, else the Mob row's (`Region.cs:745-768`). 12 "training dummy"
  rows are `GameTrainingDummy` (cannot die) and rightly left out; one plain row of that name is a real monster.
- **Template variants** (my error): when several NpcTemplate rows share an id the name and body type go together
  per row; I had mixed them. Fixed in `verify_world.py`.

## Facts I read in the server that other lanes may need

- **Grey gives nothing** (`AbstractServerRules.cs:1218`): if the monster is grey to anyone who damaged it, the kill
  gives no XP and no loot at all, coin included. Worth one line on the hunt page and the drop tables.
- **Runtime name and level of a spawn** (`GameNPC.cs:1076-1180`, `:1309-1451`, `Util.cs:86-114`): a `Mob` row with
  an `NpcTemplate` whose `ReplaceMobValues` is 1 takes the template's Name, Guild, Faction, Race, BodyType, Flags,
  aggro level and range; its level is one uniform pick from the template's `Level` string (`;` separates entries,
  `a-b` expands to every level from a to b, repeats weigh more), re-picked on every respawn. Without such a
  template the `Mob` row's own values are used. 79,253 of 84,896 spawns have such a template; 439 of them have a
  template name different from the `Mob` name. Loot is looked up by the runtime name.
- **Merchant price** (`GameMerchant.cs:95-112`): one purchase = `ItemTemplate.Price` copper for `PackSize` pieces
  (or 1). Bounty, seal and other currency merchants charge the same number in their own currency.
- **Stable route** (`GameStableMaster.cs:128-136`): a ticket works at a stable master when it is in that master's
  list, its `Item_Type` is 40, PathPoints with the ticket's id exist (a `Path` row is optional) and the first point
  is within 500 units of the master on both X and Y.
- **Closed regions**: see above. Anything standing in one is unreachable.
- **Effective class of a spawn**: see above (`verify_world.effective_class`).

## Added 2026-11-12 after the owner's feedback (lead's message)

`verify_data.py` now also checks:
- **T1** no page or data text says "attacks on sight": every script under `app/` (comments are skipped, strings
  are not), `index.html` and every string in `data/*.js`. The sweep flags the phrase in the rendered pages too.
- **C8** every zone that has monsters in the world has camps; **K5** every huntable spawn's name is a kind
  (huntable = no realm, in a zone of an open region, not peaceful, can be targeted, and its effective class is not
  a training dummy, guard, keep guard, merchant, trainer or NPC guild script class).
- **D5** every kind in `mobs.js` whose name has a `LootTemplate` table has that loot joined under the same key in
  `drops.js` `mobs` (row count equal); it prints how many kinds carry the `scripted` flag and how many of those
  have loot.
The stance words as the server prints them (`GameNPC.cs:2346-2417`): with a faction (and aggro level and range
above 0) the word is the player's standing with the faction (aggressive above 75, hostile 51-75, neutral 26-50,
friendly up to 25, `Faction.cs:184-194`); without a faction, aggressive when the aggro level is above 0, else
neutral; same realm = friendly.

## Added later the same day

- Script-set levels: a monster class whose own `AddToWorld` sets `Level` overrules the table (the call comes after
  `LoadTemplate`, `GameNPC.cs:3270-3284`). `verify_world.script_levels()` reads them from the C#; K6 compares with
  `mobs.meta.scriptLevels` (42 classes, all equal).
- Script-made kinds (`mobs.scripted`, no Mob row): S1 checks the table; in `drops.js` they must be under `mobs`
  with `s: 1`; coin = coin165.json by name, else the table's `body`; `none` from the table's `cls`; `byItem` fourth
  value 2. Items only they drop carry source bit 128 in `items.js` (I5).
- `raid` in `drops.js`: checked from the rule itself (class implements `IGameEpicNpc`, not `GameEpicNPC` /
  `GameEpicDungeonNPC`, `RealmRaidLootOwner.cs:27-32`): 187 kinds, equal.
- Quests (`quests.js`): Q1 ids and the DataQuest count, Q2 places inside zones, Q3 monsters named are kinds, Q4 items
  exist and names equal the table, Q5 reward items are in `items.js`, Q6 hand-over rows against `drops.js` `q`.

## Open problems

(none yet)
