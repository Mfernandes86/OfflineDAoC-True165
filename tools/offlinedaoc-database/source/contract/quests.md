# data/quests.js and app/lib/quests.js (lane Q)

Built by `python -I source/build_quests.py` (about 2 s, no arguments; `--db`, `--out` as every builder). It reads the
world database, `runtime/server/classic-quests.json`, `classic-quest-guides.json`, the server source (the experience
table and the scripted quest files it checks), and three data files of other lanes that must exist first:
`data/zones.js`, `data/mobs.js`, `data/itemmeta.js`, `data/drops.js`. So it runs **last** in `build_all.py`.
`WG.load("quests")`; 2.8 MB. The rules behind every field, with `file:line`, are in the builder's header.

## 1. For the other page lanes: one line each

`app/lib/quests.js` is loaded before the components and every view. Everything is under `WG.quest`.

```js
WG.quest.itemSection(el, itemId)          // item page: quests that reward it, need it, or hand it over on a kill
WG.quest.mobSection(el, kindKey)          // monster page: quests that send you to kill it, and what it hands over
WG.quest.zoneSection(el, zoneId)          // zone page: quests given in the zone, then quests with a step there
WG.quest.npcSection(el, name, zoneId)     // merchant / NPC page: quests the NPC gives or takes part in (zoneId optional)
```

Each loads `data/quests.js` itself (and tolerates missing files of other lanes), appends one titled card
(`WG.ui.section("Quests", {count})`) to `el`, appends **nothing** when there is nothing to say, and returns a
`Promise` of the number of rows. `opts` (third / fourth argument, all optional): `title` ("Quests"), `max` (rows before
a "Show all" button, default 12), `empty` (text to show instead of nothing), `me: false` (do not dim the quests the
header character cannot take), `plain: true` (no card, just the list). Quests that exist once per class are merged
into one row (the version the header character can take). `kindKey` is the monster key of mobs.js / drops.js.

Search (P-U): after `WG.quest.ready()`,
`WG.quest.search(text, limit) -> {list: [{id, name, sub, href, q}], total}` (by quest name, then by giver name; one hit
per quest, class versions merged; `sub` = "Quest, level 13 to 50, Hibernia, from Eruven").

Lookups (after `WG.quest.ready().then(...)`; all synchronous):

| call | returns |
|---|---|
| `WG.quest.ready()` | `Promise`: loads the data, builds the indexes once. `WG.quest.full()` also loads itemmeta, zones, mobs, items. |
| `WG.quest.all()`, `WG.quest.get(id)`, `WG.quest.meta()` | the quests (objects of section 2), one quest, `quests.meta` |
| `WG.quest.forItem(itemId)` | `{rewards: [{q, how, step, cls}], needs: [{q, step, role}], hands: [{q, step, mob, chance, how}], total}`. `how` of a reward: `final`, `choice`, `trade`, `grant`, `side`, `step`, `random`; `cls` = the class whose version of the reward this item is (0 = everyone's). `role`: `hand in`, `deliver`, `use`, `have`, `wear`, `turn-in`, `side turn-in`. Item ids are compared without case. |
| `WG.quest.forMob(kindKey)` | `[{q, steps: [n], kill: bool, hands: [{item, chance, how, step}]}]`, one entry per quest, lowest level first |
| `WG.quest.forNpc(name, zoneId)` | `{gives: [quest], steps: [{q, n, t}]}` |
| `WG.quest.forZone(zoneId)` | `{gives: [quest], steps: [{q, ns: [step numbers]}]}` |
| `WG.quest.section(el, list, opts)` | draws a list consistently; `list` = quests or `{q, note}` rows; returns the section element or null |
| `WG.quest.itemRows(id)`, `mobRows(key)`, `zoneRows(zone)`, `npcRows(name, zone)` | the `{q, note}` rows the four sections draw |
| `WG.quest.link(q, opts)` | `<a href="#/quest/<id>">`, realm tinted, with the "part 2/3", "epic", "kill task" tags (`opts.tags: false` = name only) |
| `WG.quest.canTake(q, me)` | level, realm and class of `me` (default `WG.me`) fit; what the character has finished is not known |
| `WG.quest.xpAt(q, level)` | experience the whole quest pays a character of that level (per item for a turn-in) |
| `WG.quest.rewardItems(q, classId)` | `[{id, how, step, own, versions}]`: the reward items, the class's own version where rewards are made per class |
| `WG.quest.chanceText(c)` | "every kill" or "50% a kill" |

Routes: `#/quests?lv=&any=&realm=&cls=&min=&zone=&kind=&rw=&use=&rep=&all=&q=&sort=` and `#/quest/<id>?step=<n>`.
`id` is the DataQuest ID (a number) or, for a scripted quest, `s-<class name in lower case>` (`s-academy50`).

## 2. data/quests.js

```
WG.provide("quests", {
  meta: {built, db, counts, warn, notLive, systems, kindNames, stepNames, rules, xpForLevel},
  quests: [ quest, ... ],                 // sorted by lowest level, then name
  classRewards: {"<reward item id>": {"<class id>": "<that class's version, item id>"}},   // 16 rewards made per class
  names: {"<item id>": "item name"}       // every item id used anywhere in the file (2,649)
})
```

Keys are the shared ones: item id = `ItemTemplate.Id_nb` in the table's spelling, monster key = the lower-cased runtime
name (mobs.js `key`), zone = ZoneID, positions = zone-local x, y (the `/loc` numbers), realm 0-3, class id =
eCharacterClass.

### quest

| field | meaning |
|---|---|
| `id` | DataQuest `ID`, or `"s-<name>"` for a scripted quest |
| `name` | quest name. Many names occur several times: one row per class (`grp`) and one per part of a chain (`part`) |
| `src` | `"data"` (a DataQuest row) or `"script"` (a C# quest: `file`) |
| `realm` | the giver's realm (else the realm of its classes, else of the giver's zone); 0 = any (the two courier quests) |
| `lmin`, `lmax` | the level range in which it can be taken (MinLevel, MaxLevel) |
| `cls` | class ids that may take it, `[]` = every class. Classes True 1.65 does not have are removed |
| `races` | optional: race names it is offered to (classic-quests.json `Races`) |
| `kind` | `class` (class-limited, not epic), `epic` (a part of one of the 11 epic lines, or its level 50 finale), `killtask` (an item turn-in, or a "-- Kill Task" quest), `chain` (no class limit; needs or opens another quest), `town` (the rest) |
| `start` | `talk` (talk to the giver and say `accept`), `turnin` (hand the item to the giver: no journal entry), `script` (`how` says it) |
| `accept`, `offer` | the accept word (AcceptText) and what the giver says (Description) |
| `giver` | `{name, zone, x, y, n}`; `n` = how many NPCs of that name stand in the start region. `givers` (one scripted quest) = all of them. Missing for the two courier quests |
| `rep` | how often it can be done (MaxCount); 1 = once |
| `steps` | the steps in order (below); `[]` for a turn-in |
| `turnin` | turn-ins only: `{item, xp (fixed experience per item; 0 = experience needed for the level / 50), from: [[kind key, chance]] (monsters whose loot has the item, best first, at most 12), fromN, roll}`; `roll` (6 quests) = `{weights: {xp, coin, item, hostile}, total, coin: [min, max] copper, items: [...], hostile: [name, level]}`: each item handed in rolls one of these |
| `xp`, `share`, `coin` | sums over the steps: fixed experience, share of a level, copper |
| `hand` | **the kill hand-overs**: `[[step (0 = any step), kind key, item id, chance 0..1 per kill, how], ...]` (below) |
| `rw` | reward items: `[[item id, how, step], ...]`; `how` = `final` (at the end), `choice` (one of a choose step), `trade`, `grant`, `side`, `step` (a kill step's own item that is gear). Look the id up in `classRewards` for per-class versions |
| `rwClass` | scripted quests: `{"<class id>": [item ids]}` (the epic armour) |
| `need` | `[{name, ids: [quest ids]}]`: the quests that must be finished first. `ids` = the part that meets it, for the classes of this quest (QuestDependencyRule: the highest part below this one) |
| `reqAny`, `during`, `excl` | ids of which one must be finished; quest names that must be in progress; quest names that close it once started or finished |
| `next` | ids of the quests that need this one |
| `part` | `[k, n]`: this is part k of the n quests of this name in the realm (by level) |
| `line` | the epic line it belongs to |
| `grp`, `grpN` | id of the first of the rows that are the same quest for other classes, and how many |
| `guide` | 1 = the game's Quest Guide button has the period walkthrough |
| `warn` | sentences about something that cannot work in this world (a kill target that is nowhere) |
| scripted only | `file`, `how`, `giverNote`, `startGives`, `req` (`{kind, line, level, text}`), `xpNote`, `rwNote`, `notes`, `uncertain` |

### step

| field | meaning |
|---|---|
| `n`, `t`, `text` | 1-based number, kind, the journal text. `t`: `kill`, `deliver` (give `item`, which the quest handed you, to the target), `collect` (bring `need` x `col` to the target), `talk`, `whisper` (say `word`), `search` (/search inside `area`), and the classic-script kinds `use_item`, `choose`, `trade`, `quest`, `travel`, `has_item`, `say`, `group`, `die_to` |
| `at` | `[zone, x, y]`: the red map marker the game shows for the step |
| `target` | `{name, k, key, zone, x, y}`; `k`: `mob` (a monster kind: `key` joins mobs.js), `npc` (stands in the world; its position), `event` (the quest's own monster, see `spawn`), `script` (a server script gives a standing NPC this name: `file`, `host` = the NPC, its position; 12 rows, Noble Werewolf Alina), `obj`, `trainer`, `none` (not in this world; none today). Scripted: `keys` when several kinds count, `made: 1` when the script places the NPC |
| `item`, `ch` | the step's own item (StepItemTemplates) and, on a kill or search step, its drop chance in percent: a failed roll means the kill does not count |
| `col`, `need` | the item handed in and how many (CollectCountNeeded) |
| `gets` | `[[item id, chance]]`: what completing the step puts in the backpack (its own item, and the next step's item when that is a delivery) |
| `drops` | `[[kind key, item id, chance]]`: monsters that hand the item over while you are on this step (classic-quests.json Drops) |
| `alt` | kind keys that count like the kill target (they carry the step's item too) |
| `from` | `[[kind key, chance]]`: ordinary loot sources of the item the step asks for (drops.js) |
| `spawn` | `{name, level, n, zone, x, y, r, peace, walks, escort: [[name, n, level]]}`: appears for the player within `r` units of the spot; `own: 1` = only this one counts |
| `use`, `consume`, `given`, `place`, `night` | custom steps: the item, whether it is used up, whether the quest hands it out, `[zone, x, y, radius]`, only at night |
| `choices`, `trades`, `quests` | `[[word, item id]]`; `[[item in, item out or null]]`; `[[quest names], how many]` |
| `needs`, `grant`, `wear` | `[item id, who re-issues it]`; `[[item ids], from whom]`; item ids that must be worn |
| `hours` | `[start, end]` game hours when the target is up (classic-quests.json HourWindows) |
| `xp`, `share`, `coin` | fixed experience, share of the current level, copper paid when the step completes. Experience = max(xp, share x experience of the level); `meta.xpForLevel[level] - meta.xpForLevel[level - 1]` is that experience |
| scripted | `count`, `level`, `scripted`, `takes`, `note`, `cite`, `byRealm` |

### hand (what the owner asked for)

`[step, kind key, item id, chance, how]`, chance per kill as the server rolls it; 1 = guaranteed.

| how | what | rule |
|---|---|---|
| `kill` (2,141) | a DataQuest kill step: the kill of the target puts the item in the backpack. 978 steps roll a chance (30 to 80 %; 15, 25, 33 in four); the rest are certain. Also the next step's item when that step is a delivery, and other monsters that count for the step | `DataQuest.cs:1616-1659`, `ClassicQuests.cs:759-771` |
| `drop` (171) | the classic quest script: a listed monster gives the item with its chance while the player is on that step | `ClassicQuests.cs:742-779` |
| `side` (12) | the item of an optional side turn-in | `ClassicQuests.Features07.cs:247-274` |
| `loot` (363) | from D-D's `drops.js`, not recomputed: loot-table rows that fall only while on this quest (74), and for a turn-in the monsters whose ordinary loot has the item (289) | `source/droprules.md` section 5 |

`drop` + `side` + the other-monster part of `kill` are the same 122 entries as `drops.js` `q`; the builder compares
them on every run (0 differences).

### Sample

```json
{"id": 20008, "name": "Cellar Infestation", "realm": 1, "lmin": 1, "lmax": 5, "cls": [14, 16, 17], "src": "data", "rep": 1,
 "giver": {"name": "Mistress Laws", "zone": 51, "x": 45339, "y": 56883, "n": 1}, "guide": 1, "offer": "You have barely ...",
 "start": "talk", "accept": "immediate",
 "steps": [
  {"n": 1, "t": "talk", "text": "Speak to Ylaine Barrett in Gothwaite Harbor about the swamp goo in the cellar.",
   "at": [51, 31270, 50622], "target": {"name": "Ylaine Barrett", "k": "npc", "zone": 51, "x": 31270, "y": 50622}},
  {"n": 2, "t": "kill", "text": "Hunt the swamp goo in the water just down the hill from Caer Gothwaite and return a skin to Ylaine.",
   "at": [51, 36943, 48304], "target": {"name": "swamp goo", "key": "swamp goo", "k": "mob"},
   "item": "cq_slimy_swamp_goo_skin", "ch": 50, "gets": [["cq_slimy_swamp_goo_skin", 0.5]]},
  {"n": 3, "t": "collect", "text": "Return the skin to Ylaine Barrett.", "at": [51, 31270, 50622],
   "target": {"name": "Ylaine Barrett", "k": "npc", "zone": 51, "x": 31270, "y": 50622},
   "col": "cq_slimy_swamp_goo_skin", "need": 1, "xp": 11, "share": 0.22, "coin": 300}],
 "xp": 11, "share": 0.22, "coin": 300, "hand": [[2, "swamp goo", "cq_slimy_swamp_goo_skin", 0.5, "kill"]], "rw": [],
 "next": [20011], "kind": "class", "grp": 20008}

{"id": 2, "name": "Drakoran Skirmisher Head XP Item", "realm": 1, "lmin": 20, "lmax": 29, "cls": [], "src": "data", "rep": 15,
 "giver": {"name": "Eafa Thatcher", "zone": 51, "x": 54536, "y": 20921, "n": 1}, "start": "turnin",
 "turnin": {"item": "Drakoran_Skirmisher_Head", "xp": 163840, "from": [["drakoran skirmisher", 0.06]], "fromN": 1},
 "steps": [], "xp": 163840, "coin": 0, "hand": [[0, "drakoran skirmisher", "Drakoran_Skirmisher_Head", 0.06, "loot"]],
 "rw": [], "kind": "killtask", "grp": 2}
```

## 3. What is in, what is not (counts of the current build)

- **1,522 DataQuest rows = 1,484 in + 38 left out.** Left out: 30 rows only for base classes at levels a base class
  cannot reach (a base class stops gaining levels at 5, `GamePlayer.cs:4334`); 7 rows only for classes True 1.65 does
  not have (`disabled_classes`); 1 row whose prerequisite no class that may take it can finish (Dark Essence for the
  Warrior: Helping Dalea has no Warrior row). Every giver stands in the world.
- **18 scripted quests in**: the 11 level 50 epic finales and Immediate Resolution, Wolf Pelt Cloak, Lost Stone of
  Arawn, The Lost Seed, Wild Wilderness, The Traveler's Inn, The Horn of Rallying. Their facts were read from the C#
  by hand (kept at the end of the builder with `file:line`; the builder checks titles, item ids and NPCs).
- **Not in True 1.65, so not in the file** (`meta.notLive`, `meta.rules.off`): the bounty quests and the faction
  reputation hunts (`Classic165Mode.cs:56-60` + `GameEventMgr.cs:115-126`: their start-up does not run, the bounty
  masters and emissaries never spawn), the 12 battleground daily quests (same rule), Ancestral Secrets
  (`AncestralSecrets.cs:81`), everything Sluaghbinder. `/task` kill, delivery and craft tasks (levels 1 to 19, from
  named guards and townspeople) exist but are generated on the spot: described in `meta.systems`, not listed.
- Tale of Two Trolls (12 class versions) asks for the death of "Noble Werewolf Alina", which is no spawn: the NPC
  Alina takes that name, level 22 and no realm at night (`scripts/mobs/Alina.cs`). The builder finds such names in the
  monster scripts (`target.k = "script"`, step `night: 1`). No kill target is left unexplained.
- The quest guide texts (the period walkthroughs the game shows) are not copied; `guide: 1` says the game has one.
- What a character has already finished is not known to the tool: `canTake` checks level, realm and class only.
