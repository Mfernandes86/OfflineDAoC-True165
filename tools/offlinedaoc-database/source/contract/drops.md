# data/drops.js (lane D-D)

Built by `python -I source/build_drops.py` (about 60 s with its self test, 4 s with `--no-selftest`; no arguments
needed; reads only the world database, the server source, `runtime/true165/coin165.json` and
`runtime/server/classic-quests.json`). It reads no other lane's data file. Lane D-V's `source/verify_drops.py`
recomputes every number independently: PASS on this file.
The rules behind every number: `source/droprules.md`.

```js
WG.provide("drops", {
  meta:      { built, db, cols, flags, counts, coin, noLootClasses, selftest },
  mobs:      { "<kind key>": KIND, ... },      // every monster kind spawned in a region the player can enter
                                               // (7,177; 2,491 of them have a loot table, the rest only coin)
                                               // plus the 143 kinds only a script makes (`s: 1`; 19 have a loot table)
  unspawned: { "<lowercased name>": KIND },   // 15 loot tables that nothing in this world uses (misspelled or orphaned)
  byItem:    { "<item id>": [[kind key, chance, flags, mark], ...] },   // best chance first; mark: absent = a world
                                               // spawn kind, 1 = an `unspawned` name, 2 = a script-made kind (`s`)
  quests:    { "<DataQuest ID>": "quest name" }    // every quest id used below
});
```

`kind key` = the lowercased **runtime** monster name, the same key as `mobs.js` (`key`): the NpcTemplate's name
when the spawn's template has ReplaceMobValues, else the Mob row's name. Spawns in regions the player cannot enter
(`disabled_regions`, `disabled_expansions`: the tutorial, New Frontiers battlegrounds, Trials of Atlantis and later)
do not count: 124 kinds exist only there and are left out (none of them has a loot table).
`item id` = `ItemTemplate.Id_nb` in the item table's own spelling (the same `id` as `items.js`).

## KIND

| Field | Type | Meaning |
|---|---|---|
| `d` | rows, see below | Items this kind can drop as loot. Missing when it has no loot table. Sorted best chance first. |
| `dc` | int | DropCount: the most random items one kill can give (fixed 100% items come on top). Only with `d`. |
| `pool` | int | How many random (non-100%) entries roll on a kill, for a killer of realm `realm`. Only with `d`. |
| `realm` | 0-3 | The realm whose characters get the whole table (the realm of its realm-locked items; 0 = no realm-locked items, same for everyone). Only with `d`. |
| `coin` | int 0-100, or `[min, max]` | Chance in percent that a kill drops a bag of coins. A pair when spawns of this kind have different body types (41 kinds). Missing only on a script-made kind (`s`) whose body type M-D could not establish (`body` null in `mobs.scripted`; none today) and that has no measured rate: say nothing rather than guess. |
| `cm` | 1 | Present when `coin` is this monster's own measured rate; absent = its body type's default. |
| `otd` | `[[item id, minLevel], ...]` | One-time drops: see below. |
| `q` | `[[item id, chance 0..1, quest id, [steps]], ...]` | Quest hand-overs on a kill: see below. |
| `s` | 1 | The kind has no `Mob` row: a boss script, a True 1.65 encounter script or a quest step creates it. These are the rows of `mobs.scripted` in `mobs.js` (lane M-D; `where`, `how`, `by` there say where it appears) whose key is not also a world spawn name: 143 kinds. Its `d`, `coin`, `otd`, `none`, `raid` follow the same rules as any other kind; `none` and `raid` are then `[classes, of classes]` from its `cls`. |
| `raid` | 1 or `[n, of]` | The kind (or `n` of its `of` spawn rows) is a scripted epic encounter: when a realm expedition of gamebots takes part in the kill, the loot is shared and a player gets one item in four of what drops plus a quarter of the coin (`droprules.md`, section 8a). `chance` is unchanged; it is what drops. |
| `none` | `[n, of, reason]` | `n` of this kind's `of` spawn rows never drop anything, coin included (`reason`: `"keep guard"` or `"script"`, a class that turns loot off). When `n == of` the kind never drops. 34 kinds. |
| `closed` | 1 | Only in `unspawned`: the name is spawned, but only in a closed region (none today). |

### A row of `d`: `[item, chance, n, flags, byRealm, quests]`

| # | Name | Meaning |
|---|---|---|
| 0 | `item` | Item id. |
| 1 | `chance` | 0..1, six decimals. The chance that **one kill** puts at least one of this item on the ground, for the natural killer: a character of the item's realm when the item is realm-locked, otherwise a character of the kind's `realm` (any realm when that is 0). Already includes the DropCount competition; it is not the raw table number. |
| 2 | `n` | Pieces you get when it drops (a stack of the item's PackSize: arrows, bolts, some trade goods; else 1). |
| 3 | `flags` | Bit flags: 1 `always` (fixed drop, every kill), 2 `quest` (falls only while a loot owner is on one of the quests in column 5), 4 `never` (a quest item no quest names: never falls), 8 `realm` (realm-locked: only a killer of the item's realm gets it). |
| 4 | `byRealm` | Optional. `[Albion, Midgard, Hibernia]` chances when the chance is not the same for all three killer realms. Missing = `chance` holds for every realm (to six decimals). `0` is only a filler when column 5 follows. |
| 5 | `quests` | Optional (only with flag 2 or 4). The DataQuest ids that let the item fall; names in `quests`. |

Reading a chance for the current character: `var c = (Array.isArray(r[4]) && WG.me().realm) ? r[4][WG.me().realm - 1] : r[1];`
Expected kills for one drop: `1 / c`. Chance of at least one in k kills: `1 - Math.pow(1 - c, k)`.

Things a page must say next to these numbers (short versions in `droprules.md`, section "For the pages"):
- Nothing drops, coin included, when the monster is grey to anyone who damaged it.
- The loot realm is the group leader's when the killer is grouped.
- A row with flag 2 is a quest item: it falls only while you are on the quest.

### `otd`: one-time drops

`[item id, minLevel]`: the first time a character of level `minLevel` or higher takes part in a kill of this monster,
the item is put straight into the backpack, once per character, ever. Not a chance: it is certain on the first
eligible kill. 76 in the world. The same item is often also in `d` as an ordinary chance drop.

### `q`: quest hand-overs

`[item id, chance, quest id, steps]`: while a player is on one of the listed steps of that quest (an empty step list =
a side turn-in open during the quest), killing this monster puts the quest item in the backpack with that chance.
This is the quest script, not loot: it does not compete with `d` and does not appear in `byItem`. It covers the
classic-quest script's own drops only (122 entries on 55 kinds); the ordinary kill steps of the `DataQuest` table
(kill the target, the step's item is handed over, some with a chance) are quest progress and are not in this file.

### `coin` amount

`meta.coin.amount`: for a monster of level L, min = 2 + floor((L + 1)^3 / 8) copper, max = min + floor(min / 2),
every whole number in between equally likely (then times `meta.coin.moneyDrop`, which is 1). Use the level from
`mobs.js` / `camps.js`. `WG.money(copper)` formats it.

## Joining with mobs.js

Every `key` of `mobs.js` `kinds` and of `mobs.js` `scripted` is a key of `mobs` here (11 scripted names are also
world spawn names and are ordinary kinds here, without `s`). The reverse is not true: `mobs` here has every spawned name
(townsfolk, merchants, horses and guards included, 7,177), `mobs.js` only the 4,129 monster kinds. So a key found in
`byItem` or `mobs` can be missing from `mobs.js`: show the name as plain text with no link. 15 names with a loot
table are in that case (peaceful or realm NPCs such as horse, hund, telsar, erich, fallen sea king).

## Five rows (real, from the file)

```js
"spindly rock crab": {"d": [["p165_prima_spindly_rock_crab_leg", 0.5, 1, 0], ["p165_uth_839", 0.5, 1, 0],
    ["Firesteel", 0.03, 1, 8, [0.0, 0.03, 0.0]], ["Ring_of_the_Stalwart_Soul", 0.03, 1, 8, [0.0, 0.03, 0.0]], ...],
    "dc": 7, "pool": 7, "realm": 2, "coin": 67, "cm": 1}
"black wraith": {"d": [["p165_prima_etheric_spirit_shackles", 0.45, 1, 0],
    ["cq_hib_shadowy_gem", 0.3, 1, 10, [0.0, 0.0, 0.3], [20254]], ...], "dc": 12, "pool": 14, "realm": 3, "coin": 100, "cm": 1}
"charred skeletal commander": {"d": [["p165_uth_2536", 0.75, 1, 0], ..., ["Brimstone_Breastplate", 0.15, 1, 8, [0.0, 0.15, 0.0]]],
    "dc": 9, "pool": 9, "realm": 2, "coin": 0, "cm": 1, "otd": [["Brimstone_Breastplate", 24]]}
"lich lord sanguis": {"d": [["Sanguine_robe", 0.166667, 1, 8, [0.166667, 0.0, 0.0]], ...], "dc": 1, "pool": 6, "realm": 1, "coin": 77}
"giant dragonfly": {"coin": 41, "q": [["cq_giant_dragonfly_wing", 1.0, 20146, [2, 3]], ...]}
```

`byItem` sample: `"Firesteel": [["carrion crawler", 0.03, 8], ["haunt", 0.03, 8], ["perfidious pook", 0.03, 8], ["spindly rock crab", 0.03, 8]]`

## What is not in the file, and why

- Random-object generation (ROG) items: off in True 1.65 (no such generator row in `LootGenerator`), so there are none.
- Timed "XP item" drops (negative chances): the world has no such row; the builder asserts it.
- A chance per monster level, killer level or con colour: the server has no such rule. The only level rules are grey
  = nothing, and `minLevel` on one-time drops.
- Merchants, crafting, quest rewards: other lanes.
- `unspawned` names are not in `mobs.js`: 15 loot tables nothing in this world uses (7 are misspellings of a live
  monster's name, the rest belong to monsters nothing creates). Leave them out of pages, or show them as plain text
  ("not in the world").
