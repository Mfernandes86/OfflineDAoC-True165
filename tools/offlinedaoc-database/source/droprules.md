# How True 1.65 decides what a monster drops

Lane D-D, 2026-11-12. Read from the server source (`development-source/server`, paths below are under `GameServer/`
unless they start with `CoreDatabase/`) and the clean v0.7 True 1.65 world. `source/build_drops.py` is the port;
`data/drops.js` holds the results. Nothing here is taken from stock Dawn of Light behaviour or from memory of the
live game: every rule has its `file:line`.

## For the pages (short version)

- The chance shown is the real chance that one kill drops the item, not the raw number in the loot table.
- A monster rolls every item in its table once. If more items win than its drop limit allows, the limit number of
  winners is taken at random, each winner equally likely. Items marked "always" drop on every kill on top of that.
- Nothing drops, coin included, when the monster is grey to anyone who damaged it.
- Realm-locked items fall only for a killer of that realm (in a group: the group leader's realm).
- Quest items fall only while you are on the quest.
- One-time drops go straight into the backpack on the first kill at or above the listed level, once per character.
- Coin is a separate roll: a chance per monster (measured, or by body type), an amount by monster level.
- No random-generated (ROG) items exist in True 1.65. Monster level, your level and con colour do not change any
  chance (grey is the one exception).

## 1. Which loot generators run

`LootMgr.Init` loads the rows of the `LootGenerator` table and creates one generator object per row
(`gameutils/LootMgr.cs:55-103`). A row with no monster name, guild, faction or region is global: it runs for every
monster (`LootMgr.cs:383-386`). The True 1.65 world has exactly three rows, all global, none exclusive:

| Row | Class | Does |
|---|---|---|
| `money` | `DOL.GS.LootGeneratorMoney` | coin (section 6) |
| `otd` | `DOL.GS.LootGeneratorOneTimeDrop` | one-time drops (section 7) |
| `template` | `DOL.GS.LootGeneratorTemplate` | items from `LootTemplate` / `MobXLootTemplate` (sections 2-5) |

Nothing in the code registers another generator for the open world (the only `RegisterLootGenerator` call outside
`LootMgr` is for adventure-wing instances, `serverrules/AdventureWingJumpPoint.cs:145`). So these generators exist in
the source but never run: `ROGMobGenerator` (random-object generation, `Managers/RandomObjectGeneration/AtlasMobLoot.cs:14`),
`LootGeneratorRandom`, `LootGeneratorMobTemplate` (its tables `MobDropTemplate` and `DropTemplateXItemTemplate` are
empty), `LootGeneratorDreadedSeals`, `LootGeneratorDragonscales`, `LootGeneratorAtlanteanGlass`,
`LootGeneratorAurulite`, `LootGeneratorChest`, `LootGeneratorUniqueItem`. **Random-object generation is off.**
The builder asserts the three rows and stops if the table ever changes.

On a kill, `LootMgr.GetLoot` asks each generator for a `LootList` and merges them into one list
(`LootMgr.cs:412-435`): fixed items are concatenated, random entries are concatenated, and the list keeps the
largest `DropCount` (`gameutils/LootList.cs:73-87`). A new `LootList` starts with `DropCount = 1`
(`LootList.cs:44-51`). The merged list is then rolled once (section 4).

## 2. When a kill drops nothing at all

All of this is decided before any generator runs (`serverrules/AbstractServerRules.cs`, `OnNpcKilled`):

- **Grey**: if the monster cons grey to any player or gamebot that damaged it, the kill gives no XP, no items, no
  coin and no one-time drop (`AbstractServerRules.cs:1219-1221`, `gameobjects/GameLiving.cs:647-650`). A guard
  among the damage dealers has the same effect.
- A pet, or a monster that was charmed a short while ago, gives nothing (`gameobjects/GameNPC.cs:2938-2952`,
  `AbstractServerRules.cs:1054-1060`).
- No player or gamebot close enough with damage on it: nothing (`AbstractServerRules.cs:1077-1078`, `:1223-1225`).
- **Keep guards** and gamebots never drop (`NoItemLoot`, `AbstractServerRules.cs:1683-1685`, checked at `:1128`).
- A monster class can turn loot off with `CanDropLoot => false` (`GameNPC.cs:3323` is the default `true`; checked at
  `AbstractServerRules.cs:1128`). 51 script classes do, all of them adds summoned by bosses; three spawn rows in the
  world use such a class. `drops.js` marks these kinds with `none`.
- The class a spawn runs as is not always the `Mob` row's `ClassType` (`world/Region.cs:745-773`): an NPC guild
  script for the row's guild and realm comes first, then the NpcTemplate's `ClassType` when it is set, is not
  `DOL.GS.GameNPC` and the template has ReplaceMobValues, then the row's own. One "Hastener" row is a keep guard
  class in the table but runs as an ordinary hastener through its template, so it is not counted as a guard.

## 3. Which table rows a monster uses

The template generator keeps two maps, both keyed by **lowercased** name (`gameutils/LootGeneratorTemplate.cs:52-161`):

- `LootTemplate` rows by `TemplateName`: one entry per item id (lowercased). A second row for the same item in the
  same table is ignored (`:106-107`); a row whose item does not exist is skipped (`:97-103`). The world has no such
  rows (17,677 rows, no duplicate, none missing).
- `MobXLootTemplate` rows by `MobName`, in table order (`:141-153`).

The monster is found by its **runtime name**, lowercased (`:270-273`, `:287`). The runtime name is the `Mob` row's
name unless the spawn has an `NpcTemplate` with `ReplaceMobValues`, in which case it is the template's name
(`gameobjects/GameNPC.cs:1077`, `:1180`, `:1341-1345`, `:1416-1419`). Item ids are matched without regard to case
(`CoreDatabase/DataTableHandler.cs:188-196`). A table keyed by the NpcTemplate id would take priority
(`LootGeneratorTemplate.cs:262-266`); the world has none (asserted).

Which monsters count as spawned: a `Mob` row in a region the player can enter. Regions listed in the
`disabled_regions` server property ("27;165;233-242;244") and regions of a disabled expansion (`disabled_expansions`
"3;4;5;6" against `Regions.Expansion + 1`) are closed (`world/Region.cs:234-264`). 1,693 spawn rows are in closed
regions; 124 monster names exist only there and are left out of the tool. Of the 2,525 loot tables, 2,491 belong to
a monster with a world spawn row; 19 belong to monsters that only a script, an encounter or a quest creates
(section 8a; in `mobs` with `s: 1`); 15 match nothing in this world (seven are misspellings of a live monster's name,
for example "inquisitor yonzeal" for Inquisitor Yonzael, who therefore drops coin only) and are kept apart under
`unspawned`.

The loot owner is the killer if it is a player or a gamebot, or the owner of the killing pet
(`gameutils/LootGeneratorBase.cs:62-71`); if that player is in a group, **the group leader**
(`LootGeneratorTemplate.cs:250-255`). No loot owner (a monster killed by another monster): no table items.

An item can be received when it has no realm or its realm is the loot owner's realm
(`LootGeneratorBase.cs:73-81`; `allow_cross_realm_items` is False in this world). Items the owner cannot receive
are dropped from the list **before** rolling, so they do not compete with the rest.

Then one of two cases:

**A. The monster has no `MobXLootTemplate` row** (`LootGeneratorTemplate.cs:283-316`). Every receivable row of the
table named like the monster is used: `Chance == 100` becomes a fixed drop of `Count` pieces (`:307-310`); any other
chance becomes a random entry (`:313`). `DropCount` stays 1. Four kinds are in this case (Lich Lord Sanguis,
Field Marshal Nebir, Jailor Vifil, Crusaders Antithesis).

**B. The monster has `MobXLootTemplate` rows** (`:400-519`). For each row in table order: the table is the row's
`LootTemplateName`; if that table does not exist the monster's own table is used instead, unless another row already
names the own table (`:418-425`); the own table is read once however many rows name it (`:426-431`). For each
receivable row of that table (`:588-611`): `Chance == 100` becomes a fixed drop of `Count` pieces (`:596-603`);
any other chance becomes a random entry and raises the list's `DropCount` to the `MobXLootTemplate` row's
`DropCount` if that is larger (`:606-607`, `:448-451`). In this world every `MobXLootTemplate` row names the
monster's own table, so case B means: the own table, with `DropCount = max(1, the row's DropCount)`. A `DropCount`
of 0 therefore acts as 1.

Details that matter:

- `LootTemplate.Count` is used **only** on 100% rows. Random entries are always added with a count of 1
  (`AddRandom(lootTemplate.Chance, drop, 1)`, `:313`, `:450`), so the 327 random rows whose `Count` is 2 to 10 still
  drop one piece. All 117 fixed rows have `Count` 1.
- A negative chance would be a timed "XP item" (`:303-306`, `:318-380`). The world has none (asserted).
- 1,279 `MobXLootTemplate` names have no `LootTemplate` rows at all: those monsters drop coin only.

## 4. The roll

`LootList.GetLoot` (`gameutils/LootList.cs:93-155`):

1. Every fixed item is added (`:96`).
2. Every random entry rolls once: it wins when `Chance >= Util.Random(1, 100)` (`:127-131`). `Util.Random(1, 100)`
   is a whole number from 1 to 100, both included, each equally likely (`gameutils/Util.cs:27-30`), so an entry with
   chance c wins with probability c / 100.
3. From the winners, up to `DropCount` are taken: each time one winner is picked uniformly at random and removed
   (`:143-149`). With `DropCount` or fewer winners all of them drop.

So if an item's entry has win chance p, and K is the number of **other** entries that win, the item drops with
probability

    chance = p * sum over k of  P(K = k) * min(1, DropCount / (k + 1))

P(K = k) is the Poisson-binomial distribution of the other entries' win chances. The builder computes it exactly
(a dynamic programme over the entries, `build_drops.py: poisson_binomial, chance_at_least_one`), no sampling.
When the table has no more random entries than `DropCount`, the chance is simply the table number.
The same code handles an item that appears in several pooled entries (hypergeometric term); the world has no such
case today.

How much this matters: for 4,690 of the 17,522 monster / item pairs of spawned monsters (553 monsters) the real
chance is lower than the table number, because the table has more random entries than `DropCount` (873 monsters).
The extreme is Issorden: a table row of 3% is a real 0.29%, and its 99% rows are 9.9% each.

`chance` in `drops.js` is "at least one of this item on this kill". A dropped piece of an item with `PackSize > 1`
becomes a stack of `PackSize` (`AbstractServerRules.cs:1753-1755`): column `n`.

## 5. After the roll: the True 1.65 quest gate

`DropLoot` passes every rolled item through `QuestLootGate165.MayDrop` (`AbstractServerRules.cs:1691-1695`,
`mode165/QuestLootGate165.cs:27-43`). A quest item (package `ClassicQuest` or an id starting with `cq_`) is thrown
away unless a player among the loot owners (the top-damage player, group or battlegroup) has an active data quest
that names the item anywhere in its `DataQuest` row or in its `classic-quests.json` entry (`:57-91`). Gamebots and
companions alone never get one.

The gate runs after the roll, so a quest item still uses one of the `DropCount` picks whether or not it then
falls. 28 table rows are quest items; all of them are named by at least one quest. `drops.js` gives them flag 2
and the quest ids; their `chance` is the chance while on the quest.

Then each item is created on the ground at the corpse (`AbstractServerRules.cs:1733-1787`). In True 1.65 it is
picked up automatically only for a player who turned autoloot on (`:1772-1775`). That changes who holds the item,
not whether it drops.

## 6. Coin

`LootGeneratorMoney.GenerateLoot` (`gameutils/LootGeneratorMoney.cs:37-61`):

- First `Coin165.DropsCoin(mob)` (`mode165/Coin165.cs:29-43`): the monster's own rate from
  `runtime/true165/coin165.json` (`monsters`, by lowercased runtime name: 1,647 measured monsters), otherwise its
  body type's default (`body_type_default`: Animal 37, Insect 41, Reptile 36, Plant 17, Humanoid 81, Undead 77,
  Demon 94, Giant 78, Magical 77, Elemental 75, Dragon 68, none 89), clamped to 0..100. `Util.Chance(c)` is true
  with probability c / 100 (`Util.cs:48-51`).
- Amount in copper for a monster of level L: `min = 2 + floor((L + 1)^3 / 8)`, plus a whole number from 0 to
  `floor(min / 2)`, each equally likely, times the `money_drop` server property (1 in this world).
  Level 10: 168 to 252 copper. Level 50: 16,583 to 24,874 copper (1 gold 65 silver to 2 gold 48 silver).
- The bag of coins is a fixed drop in the merged list; it does not take a `DropCount` pick.

The body type is the runtime one (the template's when it replaces the spawn's values). 41 kinds have spawns of
different body types and so two coin rates: `coin` is then `[min, max]`.

## 7. One-time drops

`LootGeneratorOneTimeDrop.GenerateLoot` (`gameutils/LootGeneratorOneTimeDrop.cs:133-216`), table `LootOTD`
(76 rows: `MobName`, `ItemTemplateID`, `MinLevel`). For every player who damaged the monster (or whose pet did), for
every row of that monster with `MinLevel <= player level`: if the character has not had this item before
(`CharacterXOneTimeDrop`), it is put directly into the backpack and recorded. No chance is rolled, no realm check,
no `DropCount`. A full backpack means it is offered again on the next kill. Gamebots never get one.

## 8. Quest hand-overs on a kill (not loot)

The classic-quest script listens to monster deaths separately from loot (`quests/QuestsMgr/ClassicQuests.cs:715-779`,
`ClassicQuests.Features07.cs:247-274`). A player on a quest step that lists the monster gets the step's item in the
backpack with the step's own chance (`Util.Chance`), independent of everything above. `drops.js` lists these under
`q` (122 monster / item / quest entries) so the monster page can say "also gives X while on quest Y".

## 8a. Bosses that a script, an encounter or a quest spawns

Some monsters have no `Mob` row: a boss script summons them (the four Horsemen and Apocalypse, the dragons'
messengers and adds, Olcasgean's primals), a True 1.65 Prima encounter script spawns them (Talvor's Spirit, Rulod,
Kargil, Hamorik, the treant seedlings: `scripts/true165/PrimaEncounters165*.cs`, `SpawnAdd` at
`PrimaEncounters165.cs:20-42`), or a quest step spawns them for one player (`classic-quests.json` event spawns,
`quests/QuestsMgr/ClassicQuests.cs:1325-1390`). Lane M-D finds them by reading the scripts and lists them in
`mobs.js` (`scripted`, 154 names); the 143 that are not also world spawn names are in `drops.js` with `s: 1`. 19 of
them have a loot table (111 monster / item chances); 31 are summoned adds of a class that never drops.

Their loot follows exactly the same rules as any other monster: they are created as `GameNPC` or a boss class,
named by the script, and their death runs the same `OnNpcKilled` (`gameobjects/GameNPC.cs:2972-3013`). So the loot
table is found by the name the script gives, coin by that name in `coin165.json` or by the body type the script or
its template sets, and a summoned add of a class with `CanDropLoot => false` (section 2) drops nothing.

Checked for loot handed out by the scripts themselves, outside the loot tables (`source/scratch/D-D/
scan_death_scripts.py` reads every monster script):

- Every `Die` / `ProcessDeath` override in the monster scripts calls the base method, so none skips the loot path.
- No boss, encounter or event script creates an item, a chest or coin on a kill. What the epic boss scripts do on
  death is: a realm news line, the killers' kill counters (`KillsEpicBoss`, `KillsDragon`, `KillsLegion`) and guild
  merit points when that property is on (for example `scripts/namedmobs/TuscarianGlacier/KingTuscarAndQueenKula.cs:20-48`).
  None of that is loot.
- `scripts/mobs/custom` has three freeshard classes that give bounty points or a token on a kill (`BPMobHigh`,
  `BPPortMob`, `SplitMob`); no spawn in the world uses them.
- The classic-quest script's hand-overs are section 8.

### Realm expeditions: who gets the loot of a raid kill

This is the one True 1.65 rule that changes what a player receives from a boss, and it is about sharing, not about
the drop chance. When gamebots of a started realm expedition (a "/raid" on a dragon or an epic dungeon) damaged the
monster, all of its loot belongs to the expedition instead of the top damage dealer
(`serverrules/AbstractServerRules.cs:1096-1106`, `:1147-1153`, `bots/autonomous/AutonomousRealmRaid.cs:1348-1370`):

- On a **high-end encounter**, players who fought it or stand by the boss with the expedition's realm get a quarter:
  of the items that drop, the first, fifth, ninth ... (counted across the expedition's encounters) go straight into
  a player's bag, picked at random among the eligible players; the rest go to the gamebots that took part. Of the
  coin, a quarter is split among those players (`bots/autonomous/RealmRaidLootOwner.cs:21`, `:43-67`, `:70-94`).
  A high-end encounter is a monster of a scripted epic class (an epic boss class or a subclass of the epic NPC
  classes, not the plain epic trash classes: `RealmRaidLootOwner.cs:27-32`), or the expedition's own target boss
  and final dungeon bosses (`AutonomousRealmRaid.cs:1339-1346`).
- On any other kill the expedition makes (trash on the way), the players get none of the drops: everything goes to
  the gamebots.
- Quest-gated items never fall on an expedition kill (the gate looks for a player or a group among the loot owners,
  and the expedition is neither: `mode165/QuestLootGate165.cs:48-53`).

What drops is rolled as always; `chance` in `drops.js` is that roll. Kinds of a scripted epic class carry `raid`
so a page can add: "with a realm expedition you get one in four of the items that drop". Without an expedition the
ordinary rules apply to them.

## 9. What does not affect a drop chance

Read and ruled out: monster level, killer level, con colour other than grey, group size, camp bonus, named or epic
status, respawn time, time of day, the `xp_rate` and other rate properties (only `money_drop` touches loot, and only
the coin amount). `scroll_drop_rate`, the `lootgenerator_*` and `atlas_rog` properties belong to generators that do
not run.

## 10. Assumptions

1. Table order: where two `MobXLootTemplate` rows name the same monster, the first row in the table (rowid order,
   which is what `SELECT *` returns in SQLite) sets `DropCount`. Three monsters have two rows; two of them have no
   loot table and the third has equal `DropCount`s, so no number depends on this today.
2. The world is the clean v0.7 True 1.65 database (`wgcommon.DEFAULT_DB`), `coin165.json` and
   `classic-quests.json` are the files of the same release (the development copies are byte-identical).
3. For the quest gate the builder searches the whole JSON entry of a quest for item ids. The server searches the
   entry after reading it into its own classes, which could drop fields it does not know. Affects only which quest
   ids are listed for the 28 gated rows, not a chance.
4. "Realm of the kind" (`realm`) is the realm with the most realm-locked rows in the table. It only chooses which
   killer the main `chance` column is for; `byRealm` has the exact number for each realm.
5. A kill by a mixed group is rolled for the group leader's realm.

## 11. Five worked examples (numbers from the world, checked by hand)

**1. Spindly rock crab** (60 spawns). `MobXLootTemplate`: DropCount 7. Table: 7 rows, none 100%.
Seven random entries and a limit of seven: every winner drops, so each chance is its table number.
Rock crab leg 50%, `p165_uth_839` 50%, Firesteel 3%, Ring of the Stalwart Soul 3%, three items at 1%. The five
equipment items are Midgard-locked: 0 for an Albion or Hibernia killer. Coin 67% (measured).

**2. Aged basilisk** (23 spawns). DropCount 1. Table: `p165_uth_487` 100%, `p165_uth_2298` 8%, `p165_uth_473` 3%.
The 100% row is fixed: every kill. The two others share one pick:
8% item: 0.08 x (0.97 x 1 + 0.03 x 1/2) = 0.0788. 3% item: 0.03 x (0.92 x 1 + 0.08 x 1/2) = 0.0288.
Coin 0% (measured: it never carried coin).

**3. Lich Lord Sanguis** (1 spawn, no `MobXLootTemplate` row, so DropCount 1). Table: six Albion items at 99% each.
Nearly always all six win and one is taken: each item (1 - 0.01^6) / 6 = 16.6667%. An Albion kill gives exactly one
of the six; a Midgard or Hibernia killer gets none.

**4. Legion** (1 spawn). DropCount 4. Table: 68 rows for three realms and none. An Albion killer rolls 33 entries
(sum of table chances 5.90), a Midgard killer 18 (8.39). Same table number, different real chance: a 25% Albion
item is 16.2252% for Albion; a 25% Midgard item is 11.1704% for Midgard; the 99% Midgard items are 48.4486% each,
the 99% Hibernia items 56.5466% each. `byRealm` carries all three.

**5. Charred Skeletal Commander** (4 spawns). DropCount 9, nine random rows: each chance is its table number
(75%, 73%, 70%, 65%, Brimstone Breastplate 15%, ...). It never drops coin (measured 0%). One-time drop: Brimstone
Breastplate, level 24 and up: the first kill by a character of level 24 or higher puts one in the backpack, and
the 15% ground drop can still happen on any kill.

**6. Bandit leader** (19 spawns). DropCount 6, six rows: bent lockpick 70%, Jade 50%, bandit's ear 48%, obsidian
20%, Bright Melodic Lute 10% (Albion), Roman Leather Jerkin 1% (Albion). The bandit's ear is a quest item: it falls
(48%) only while a loot owner is on quest 20516; otherwise that roll is thrown away.

## 12. How the numbers were checked

- Closed form against exact enumeration of every win / lose outcome on 300 random small lists (1,068 items), in
  exact fractions: equal.
- Floats against exact fractions on 4,000 entries of real tables: within 1e-12.
- A literal Python port of `LootList.GetLoot` run for 200,000 seeded kills on each of the five largest tables
  (Golestandt 107 entries, Barrow Wight 69, Cuuldurach 68, Mummy Hag 66, Legion 33): every item within 5 standard
  errors, worst absolute difference 0.26 percentage points.
- `source/scratch/D-D/handcheck.py` recomputes every row of chosen monsters by a second formula in exact fractions
  and prints the table rows beside the file's numbers; the monsters checked are named in `source/reports/D-D.md`.
- Lane D-V wrote a second implementation from the server code without reading this one and compares every number.
