# data/mobs.js and data/camps.js (lane M-D)

Built by `python -I source/build_mobs.py` (about 25 s, no arguments). `WG.load("mobs")`, `WG.load("camps")`.
Sizes: mobs.js 1.0 MB, camps.js 7.2 MB. Tables are in the compact form (`WG.rows`).
Every rule below was read from the server's C#; the builder re-reads the constants and fails if the server changes.
File and line references are relative to `GameServer/` of `wgcommon.SERVER_SRC`.

## 1. The three things everyone must know

1. **The monster's name and level are the ones the running server gives it, not always `Mob.Name` / `Mob.Level`.**
   When a spawn's `NpcTemplate` has `ReplaceMobValues = 1` (79,229 of the 84,896 rows) the server overwrites name,
   guild, faction, race, body type, speed, flags, aggro level and aggro range with the template's
   (`gameobjects/GameNPC.cs:1341-1345, 1416-1472`). For 389 rows the name changes (47 "lammia huntress" rows run
   on the "lammia sorceress" template, 40 "miserable zombie" rows come up as "thawing corpse" ...).
   **Kind key = that runtime name, lowercased and not trimmed** (the server looks loot up by
   `mob.Name.ToLower()`; one name, "Snemel den Forsumlig ", ends in a space and so do its `name` and `key`: trim
   only when you draw it). D-D joins on `key`.
2. **Levels are ranges.** With such a template the level is drawn at random from the template's `Level` list
   ("10-18", "9;10;11"; `gameutils/Util.cs:86-116`, `GameNPC.cs:1372-1389`) every time the monster spawns or
   respawns (`GameNPC.cs:3270`). 51,379 of the 77,445 monster spawn points have more than one possible level. So a
   spawn point has `lmin`, `lmax`; a camp and a kind have `lmin`, `lmax` and `levels`, the **expected number of
   monsters at each level** (a spawn point with levels 10 to 18 adds 1/9 to each; the numbers add up to the spawn
   count and are rarely whole).
3. **Only monsters are in these files.** Guards, merchants, trainers, realm NPCs and untouchable NPCs are left out
   and counted (`mobs.meta.excluded`, section 2). Single spawns are camps with `n = 1`.

4. **Stance is said in the game's own words** (owner, 2026-11-12): `aggressive`, `hostile`, `neutral`, `friendly`,
   the word of the message a player gets when he clicks the monster ("You examine the wolf. It is aggressive towards
   you."). Every kind, camp, map point, neighbour entry and zone carries it as `st` (section 5a). Pages use these
   words and never "attacks on sight".
5. **Bosses, raid zones and script-made monsters** are in: kind `rank` / `rankWhy`, zone `group` / `tags`,
   `mobs.events`, `mobs.scripted` (sections 5b to 5d).

## 2. Which Mob rows are monsters (84,896 rows)

Order of the tests; the first that fits wins. Class = the class the server really creates
(`world/Region.cs:735-772`: NPC guild script by `Mob.Guild` + realm, else the template's ClassType when the template
replaces values, else `Mob.ClassType`, else `DOL.GS.GameNPC`), looked up in the C# class tree.

| Category (`meta.excluded[..][0]`) | Test | Rows |
|---|---|---|
| `service` | class is or extends GameMerchant, GameTrainer, GameHealer, GameTeleporter, CraftNPC, GameTaxi, GameHastener, GameVaultKeeper, or comes from an NPC guild script (82 scripts: trainers, crafting masters, registrars, smith, enchanter, recharger, stable master ...) | 2,227 |
| `keepguard` | class extends `GameKeepGuard` (frontier and battleground keep guards, lords, commanders) | 896 |
| `guard` | class extends `GameGuard` (realm guards) | 1,225 |
| `realm` | any other row with `Realm` 1, 2 or 3 (townsfolk, quest people) | 1,562 |
| `dummy` | training dummies (class `GameTrainingDummy`: cannot die). One more row is called "training dummy" but is a plain NPC with no template: the server makes it an ordinary killable monster, so it is a kind with one spawn. | 14 |
| `peace` | no realm, flag PEACE 0x10: cannot attack or be attacked (`serverrules/AbstractServerRules.cs:386-392`) | 111 |
| `untargetable` | no realm, flag CANTTARGET 0x08 | 11 |
| `monster` | everything else: no realm, attackable | 78,850 |

This is the bots' own test for "a monster worth experience" (`bots/autonomous/AutonomousWorldBotController.cs:
6261-6267`) minus its last clause (neutral town residents, kept here and flagged `town`).
Gamebots are not in the `Mob` table.

Of the 78,850 monster rows, 77,381 are in the hunting data. Left out by place (`meta.excluded[..][1]`):

| Place | Meaning | Monster rows |
|---|---|---|
| `ok` | in a zone of the playable world (the same 127 zones as `zones.js`) | 77,381 |
| `disabledregion` | region closed by `disabled_regions` = 27;165;233-242;244 (`world/Region.cs:234-242`) | 1,116 |
| `disabledexpansion` | region of an expansion closed by `disabled_expansions` = 3;4;5;6 | 181 |
| `nozone` | position outside every zone square of its region (regions 21, 61, 161): the server itself finds no zone (`Region.cs:1182-1192`) and cannot put them in the world; P-D checked that none is on real floor | 172 |

The 77,381 rows give **77,445 spawn points**: 64 rows use a template id that two different templates share
(`gameutils/NpcTemplateMgr.cs:185-201`: the server takes one at random when it loads the row), so each of those is a
point of two kinds (mostly lammia huntress / lammia sorceress in Gwyddneau and Aldland). Such kinds and camps carry
the flag `shared`; their `levels` count the point as half a monster.

## 3. mobs.js

```
WG.provide("mobs", {
  meta:     {built, db, counts, rules, flagBits, campFlagBits, bodyTypes, dirs, nearCols, excluded, excludedText, zoneRealms},
  kinds:    {cols, rows},      // 4,129 kinds
  factions: {cols, rows},      // the factions monsters belong to, and their friends and enemies
  races:    {"2005": ["Humanoid", crush, slash, thrust, body, cold, energy, heat, matter, spirit], ...},   // Race table: resists in %
  raceCols: [...],
  xp:       [5, 10, 20, ...],  // experience value of a monster by level 0..100
  zones:    {cols, rows},      // one summary row per zone (127)
  paths:    {cols, rows},      // the 56 patrol routes of monsters
  peaceful: {cols, rows}       // 91 no-realm NPCs nobody can attack as they spawn (not kinds; see below)
})
```

### kinds (`WG.rows(mobs.kinds)`)

| Field | Meaning |
|---|---|
| `id` | integer for links (`#/mob/<id>`). Assigned in key order: it changes when a kind is added. Store `key`, not `id`. |
| `key` | the lowercased runtime name, untrimmed: the identity (drops join on it) |
| `name` | the name as the game shows it (the most common spelling) |
| `lmin`, `lmax` | lowest and highest level any spawn of the kind can have |
| `levels` | `[[level, expected monsters], ...]` over all its spawn points |
| `spawns` | spawn points |
| `camps` | camps (section 4) |
| `zones` | `[[zone id, spawn points], ...]`, most spawns first |
| `realm` | 1, 2, 3 when every zone it lives in belongs to that realm (zones.js realm), else 0 |
| `st` | `[aggressive, hostile, neutral, friendly]`: spawn points by the word the game shows a new character (section 5a). Show this. |
| `ag` | spawn points that really come at a new character unprovoked (section 5a explains the 18 that say "aggressive" and do not). `ag / spawns` is the share. |
| `agIf` | spawn points that would attack on sight only if your standing with their faction were "aggressive" (they have aggro, but their faction starts friendlier) |
| `ar` | largest aggro range, in game units, among its spawns that have aggro (0 = none) |
| `al` | the commonest aggro level value (the server only tests `> 0`; kept because the owner knows the number) |
| `faction` | Faction id (0 = none), commonest among its spawns; look it up in `factions` |
| `body` | body type id, `meta.bodyTypes` (0 unknown, 1 Animal ... 11 Undead) |
| `race` | Race id (0 unknown), look up in `races` for the name and resists |
| `speed` | `[min, max]` maximum speed (a player runs 191) |
| `roam` | largest roaming range: how far it wanders from its spawn point (0 = stands still) |
| `resp` | `[min, max]` seconds until a killed one is back; `-1` = never |
| `f` | flags, bits in `meta.flagBits` (below) |
| `cls` | the server classes of its spawns (`GameNPC` for ordinary monsters) |
| `hours` | `[start, end]` game hours when it is up (only with flag `hours`), else null |
| `rank` | `normal`, `named`, `quest`, `boss`, `epic`, `event` (`meta.ranks`, section 5c) |
| `rankWhy` | the rule that gave the rank, as a sentence (empty for `normal`) |

Flag bits (`meta.flagBits`): `named` 1 (its name has a capital letter: the server's own test for a named monster,
`GameNPC.cs:3174`), `unique` 2 (named with one spawn point), `epic` 4, `boss` 8 (GameEpicNPC / GameEpicBoss
classes), `quest` 16 (a spawn is a classic quest monster, `classic-quests.json` QuestMonsterIds), `flying` 32,
`swimming` 64, `ghost` 128 (translucent look only), `stealth` 256 (hidden until you are within its aggro range, at
least 150: `mode165/StealthedMonsters165.cs`), `caster` 512 (its template has spells), `night` 1024 / `day` 2048 /
`hours` 4096 (only up at night / by day / in `hours`: `scripts/mobs/TimeDependentSpawnNpc.cs`), `patrol` 8192 (a
spawn walks a path, see `paths`), `scripted` 16384 (a script class: the script can set level, adds or behaviour that
the table rows do not show), `town` 32768 (lives in a Shrouded Isles neutral town: section 5), `shared` 65536
(section 2), `noxp` 131072 (class worth no experience; none today), `wide` 262144 (a spawn has more than one
possible level).

### factions

`id, name, base, standing, friends, enemies, town`. `base` = the aggro a new character has with it, `standing` =
what that means: above 75 aggressive, above 50 hostile, above 25 neutral, else friendly
(`gameutils/Faction.cs:176-186`). Killing a member (`serverrules/AbstractServerRules.cs:1091`, `Faction.cs:82-89,
113-134`): with a 20 % chance each, +1 aggro with the faction itself and with every id in `friends` (they like you
less), -1 with every id in `enemies`. Limits -100..100. `town` = 1 for the three neutral-town factions.

### xp

`xp[level]` = the experience a monster of that level is worth (`gameobjects/GameLiving.cs:531-637`). A solo kill
gives `min(xp[monster level], xp[my level] * 125 / 100)` before the camp bonus, other bonuses and the launcher's
XP rate (`serverrules/AbstractServerRules.cs:1380-1418, 1575-1587`; `XP_Cap_Percent` = 125). In a group the value is
shared. Not ported (they depend on the fight): damage share, camp bonus, group and guild bonus. Whether a grey
monster gives anything was not established: do not promise experience for greys.

### zones (zone summaries for zone pages)

`zone, spawns, kinds, camps, lmin, lmax, levels, ag, st, guards, keepGuards, otherNpcs, kindList, safest, densest, group, tags`

- `levels`: expected monsters per level in the zone (`[[level, count], ...]`): the level histogram.
- `ag`: spawn points that attack unprovoked; `st`: the four stance counts. `guards` / `keepGuards` / `otherNpcs`:
  the rows left out, by zone.
- `group`: `outdoor`, `Shrouded Isles outdoor`, `frontier`, `battleground`, `dungeon`, `Shrouded Isles dungeon`,
  `Darkness Falls`, `city`, `housing` (zones.js kind and expansion; Darkness Falls on its own).
- `tags`: any of `raid`, `epic`, `df`, `frontier dungeon`, `starter dungeon` (section 5b).
- `kindList`: `[[kind id, spawn points, lmin, lmax, attack on sight], ...]`, lowest level first.
- `safest`: up to 8 camp ids with at least 3 spawns, ordered by: fewest stronger aggressive neighbours in reach
  (`rup`), fewest aggressive neighbours in reach (`rn`), not aggressive itself before aggressive, loosest pack,
  most spawns. `densest`: up to 8 camp ids with the most spawns (smaller radius first on a tie).

### paths

`kind, zone, camp, pts`: the route points of each patrolling monster, zone-local `[[x, y], ...]` in the zone of
its spawn point (a route may leave the zone: such points are outside 0..65535). Draw as a line.

### peaceful

`name, key, zone, x, y, z, lmin, lmax, cls, why, scripted`: the no-realm rows with the PEACE (`why` = "peace") or
CANTTARGET ("untargetable") flag in the playable world. They are **not kinds and have no camps**: the server lets
nobody attack them as they spawn. Most are scenery (ambient rats, statues, encounter controllers), but a few are
quest or event monsters that a script or quest makes attackable later and that have loot (Hurika, fallen sea king,
fallen sea queen, Shade of Ambrosius Aurelianus, Telsar, Borwyr the Cursed, Frit). A page that shows a drop from
one of them can use this table for "where it stands" and must say that it cannot be attacked until its event.

## 4. camps.js

```
WG.provide("camps", {meta: {built, nearDistance: 1500, nearCols, ptsCols, campFlagBits, dirs, count}, camps: {cols, rows}})
```

**What a camp is.** The spawn points of one kind in one zone that stand within **1,500 units** (3D) of another of
the same camp (chains allowed), cut in two when the group would reach more than **2,500** from its centre. 1,500 is
one standard spell or bow pull: what you can pull from one spot. 2,500 is the bots' camp merge radius
(`bots/autonomous/AutonomousCampDedupe.cs:19`). The bots cut camps on a 4,200-unit grid
(`AutonomousWorldBotController.cs:24`), which splits a camp wherever a grid line crosses it; linking gives the same
size of camp without that. Half of all spawn points have their nearest same-kind neighbour more than 500 units
away, so a tighter link would turn most camps into single spawns. 23,824 camps; 12,538 are single spawns
(`n = 1`, `r = 0`): show them as small dots or hide them behind a switch.

| Field | Meaning |
|---|---|
| `id` | integer, in zone / kind order. Changes when spawns change. |
| `kind` | kind id |
| `zone` | ZoneID |
| `x`, `y` | centre (mean of its spawn points), zone-local game units (the `/loc` numbers) |
| `z` | height: the median Z of its spawn points (for dungeon floors: `zones.js` `floors[].z`) |
| `r` | radius: distance from the centre to its farthest spawn point (0 for a single spawn) |
| `n` | spawn points |
| `lmin`, `lmax`, `levels` | as for kinds, for this camp |
| `st` | `[aggressive, hostile, neutral, friendly]` counts of its `n` spawn points: a mixed camp reads "7 aggressive, 5 neutral" |
| `ag` | how many of the `n` come at a new character unprovoked |
| `ar` | their largest aggro range (0 when `ag` = 0) |
| `pack` | how tightly they stand, as the bots measure it (`AutonomousCampKnowledge.cs:108-164`): the average number of other members within each member's reach (aggro range, at least 300). 2 or more is a dense pack. |
| `link` | Bring a Friend: the most friends any one member has within the full-group reach (section 5). 0 = a pull never brings one. Only matters in a group. |
| `resp` | `[min, max]` seconds to respawn |
| `f` | `meta.campFlagBits`: 1 `shared` points, 2 `town` residents, 4 holds a `quest` monster, 8 a member `patrol`s |
| `rn` | aggressive neighbours **in reach** (below) |
| `rhi` | the highest level among them (0 when none) |
| `rup` | how many of them can be higher than the camp's own `lmax` |
| `pts` | `[[x, y, lmin, lmax, z, st], ...]` every spawn point, zone-local, with its own level range, height and stance (0 aggressive, 1 hostile, 2 neutral, 3 friendly: `meta.stances`; `meta.ptsCols`) |
| `near` | neighbours, `[[kind, n, lmin, lmax, nAgg, dist, dir, nReach, nLink, ar, patrol, st], ...]` (`meta.nearCols`) |

### Neighbours and danger (`near`)

A neighbour is a monster spawn point that is **not in this camp** and stands within **1,500 units** (3D) of at
least one spawn point of the camp, whatever its level or kind (another camp of the same kind included). One entry
per neighbouring kind:

| Col | Meaning |
|---|---|
| `kind` | kind id |
| `n` | how many of its spawn points are that close |
| `lmin`, `lmax` | their level range |
| `nAgg` | how many attack on sight |
| `dist` | distance from the closest of them to the nearest spawn point of the camp (edge distance, not from the centre) |
| `dir` | where the closest one is, seen from the camp's centre: 0 N, 1 NE, 2 E, 3 SE, 4 S, 5 SW, 6 W, 7 NW (`meta.dirs`; north is up, y grows south); 8 = "among": they stand inside the camp |
| `nReach` | how many are **in reach**: they attack on sight and a spawn point of the camp lies within their aggro range + their roaming range + 200. These notice you when you fight at that spawn point. |
| `nLink` | how many would come when a member of the camp is pulled by a group (Bring a Friend friends inside the full-group radius) |
| `ar` | largest aggro range among the aggressive ones |
| `patrol` | 1 when one of them walks a path that passes this close (its distance is then to the path, not to its spawn point) |
| `st` | `[aggressive, hostile, neutral, friendly]` counts of those `n` |

Why these numbers: the aggro range is the distance at which the server makes a monster attack
(`ai/brain/StandardMob/StandardMobBrain.cs:96-119`, capped at 3,600); a monster wanders up to its roaming range from
its spawn point (`ECS-Components/NpcMovementComponent.cs:449-460`; patrollers do not roam; for a patroller `dist`
is the exact distance to its route and `dir` points at its spawn point); 200 is the margin the
bots add around an aggro range when they plan a pull (`bots/autonomous/AutonomousHuntingGround.cs`,
PullPathMargin). A monster whose own reach is longer than 1,500 is listed even when it stands farther away.
Sorted: most in reach first, then highest level, then nearest. The bots' own rule for "this camp is deadly" is an
aggressive monster 10 or more levels above the camp's highest spawn (`AutonomousCampDedupe.cs:83-95`,
`meta.rules.threatLevels`).

A page can say: "level `lmin` to `lmax`, aggressive, `nReach` in reach, about `dist` units `dirs[dir]`".
In a `shared` camp (28 camps) the entry for the other kind of the pair counts the camp's own points again, each of
which is one kind or the other at any time: read those numbers as "up to".
Remember that a monster never attacks a player to whom it is grey (`StandardMobBrain.cs:686-688`): filter by the
reader's level with `WG.con`.

## 5. Server rules the pages can state (all in `mobs.meta.rules`)

- **Attack on sight**: aggro level > 0 and aggro range > 0 (`StandardMobBrain.cs:82`); a monster with a faction
  attacks a player only when the player's standing with the faction is "aggressive" (aggro above 75;
  `StandardMobBrain.cs:694-698`), so `ag` uses the faction's `base` (a new character). The aggro level is not a
  chance: any value above 0 behaves the same.
- **Bring a Friend** (`StandardMobBrain.cs:787-924`, `BafFriends.cs`, `bots/CompanionBringAFriend.cs`; properties
  `baf_initial_chance` 0, `baf_additional_chance` 50): only when the monster is attacked, once per fight.
  Chance = 50 % x (group members - 1): **a solo player never gets adds**; two members 50 % for one add; three
  members one add; five two adds. Hired companions count as members. Who comes: monsters of the same faction or of
  a faction that lists it as a friend; a monster without a faction brings faction-less monsters of the same name.
  Even friends that are not aggressive come. From how far: outdoors 2/5 of the pulled monster's aggro range (200
  for the usual 500); in a dungeon at least 250 + 150 per extra member, up to 1,000. The friend must be in line of
  sight along the floor (not computed here: `link` and `nLink` count everyone inside the radius for a group of 8).
- **Respawn** (`GameNPC.cs:3165-3199`): the row's seconds when it has any; otherwise 5 to 10 minutes, 10 to 15
  for a named monster, longer above level 65. `resp` already holds the result.
- **Neutral towns** (`town`): members of Cryptos Mythicos, Krrzck or The Remnants within 2,500 units of that
  faction's stable master. They can be killed, and every kill can cost standing with the faction that runs the
  town (see `factions`). The bots never hunt them.
- **Night / day**: `night` monsters are only up while it is night in their region, `day` ones by day.

## 5a. Stance: the game's words

The examine message is built by `GameNPC.GetAggroLevelString` (`gameobjects/GameNPC.cs:2346-2415`; texts in
`language/EN/GameObjects/GameNPC.txt:7-15`: "aggressive", "hostile", "neutral", "friendly", "{0} towards you").
True 1.65 mode does not change it. For a monster (no realm):

| The monster has | The game says |
|---|---|
| a faction, aggro level > 0 and aggro range > 0 | the player's standing with the faction: **aggressive** above 75, **hostile** above 50, **neutral** above 25, else **friendly** (`gameutils/Faction.cs:176-186`) |
| anything else, aggro level > 0 | **aggressive** |
| anything else, aggro level 0 | **neutral** |

`st` is that word for a new character (standing = the faction's `base`). Today: 57,891 spawn points aggressive, 0
hostile (no faction starts between 51 and 75), 18,761 neutral, 793 friendly. 10 kinds are mixed.

How the word relates to what the monster does (`ag`): it attacks unprovoked only when the word is "aggressive"
**and** its aggro range is above 0 (`StandardMobBrain.cs:82`). 18 spawn points have an aggro level but range 0:
they read "aggressive" and never come at you. "Hostile", "neutral" and "friendly" monsters never attack first.

What changes it: only the standing with the monster's faction (`mobs.factions`: `base`, `friends`, `enemies`).
Each kill of a member has a 20 % chance to raise your aggro with that faction and each of its `friends` by 1 and
to lower it by 1 with each of its `enemies` (`Faction.cs:82-134`, range -100 to 100). So a "neutral" faction
monster (base 26 to 50) turns "hostile" after enough kills of its friends and "aggressive" (and attacks on
sight) above 75; killing its enemies moves it towards "friendly". A monster without a faction never changes.
`agIf` on a kind counts the spawn points that can change this way.

## 5b. Dungeons, frontiers, epic and raid zones (coverage)

Every zone that holds a monster row of the open world has camps (the builder asserts it; D-V's check C8: 97
zones). `mobs.meta.coverage` gives spawn points, kinds and camps per zone `group`; `mobs.zones` gives them per zone.
The 30 zones with no monster are the 3 capitals and the 27 housing zones.

Zone `tags`, each from the server's own lists:

| Tag | Zones | From |
|---|---|---|
| `epic` | Caer Sidi 60, Tuscaran Glacier 160, Galladoria 191 | the realm events "epic-albion / midgard / hibernia" (`bots/autonomous/AutonomousRealmRaid.cs:14, 230-232`) |
| `raid` | the three epic dungeons, Summoner's Hall 248, Darkness Falls 249, and the dragons' zones Dartmoor 4, Malmohus 116, Sheeroe Hills 216 | a realm event ends there (`AutonomousRealmRaid.cs:227-238`) |
| `df` | Darkness Falls 249 | region 249 |
| `frontier dungeon` | Dodens Gruva 246, Summoner's Hall 248, Marfach Caverns 276, Hall of the Corrupt 277 | `AutonomousDungeonPolicy.IsSharedFrontierDungeon` (the other three are the ways into Summoner's Hall) |
| `starter dungeon` | Tomb of Mithra 21, Nisse's Lair 129, Muire Tomb 221 | `AutonomousDungeonPolicy.IsStarterDungeonRegion` |

`mobs.events` (`id, name, kind, realm, zone, suggested, bosses, objectives`): the 12 realm events the server runs
as raids: three dragons, three epic dungeons, and Summoner's Hall and Darkness Falls once per realm. `suggested`
= 200, the raid size the server musters for each (its bots). `bosses` = the final encounter by name,
`objectives` = the named encounters in order for the two neutral dungeons. A player's own raid is `/raid 40` or
`/raid 80` (companions, level 50; `commands/playercommands/CompanionRaidCommand.cs`). The server states no group
size for ordinary dungeons. When an event runs is decided at run time, not in the data.

## 5c. Rank of a kind (`rank`, `rankWhy`)

First rule that fits:

| `rank` | Rule | Kinds |
|---|---|---|
| `event` | its class is the final boss of a realm event, or its name is one of a neutral event's objectives | 19 |
| `epic` | its class is a `GameEpicBoss` (the server's raid-boss class) | 148 |
| `boss` | named (capital letter) and its class is an epic-encounter class (`GameEpicNPC`), or named with its own encounter script | 177 |
| `quest` | a spawn is on the classic quest monster list (`classic-quests.json` QuestMonsterIds) | 312 |
| `named` | its name has a capital letter (the server's own test, `GameNPC.cs:3174`) | 823 |
| `normal` | everything else | 2,650 |

`rankWhy` says which rule and which class. Group content: flag bits `epic` (4) and `boss` (8) mark the classes the
server scales as epic encounters (`set_difficulty_on_epic_encounters`); `unique` marks a single spawn point.
Loot and level-against-zone were not used as evidence: the class and the event lists are the server's own word.

**The script's level wins.** 42 monster classes set their level in their own `AddToWorld`, which runs after the row
and template are loaded and after every respawn (`GameNPC.cs:3270-3284`). Their kinds carry the script's level
(`meta.scriptLevels`: class -> [min, max]); for 7 kinds it differs from the table (Lich Lord Ilron 79 not 82,
Skeletal Sacristan 77 not 81, Warlord Dorinakka 81 not 85, Zytka 65 to 70, Shredclaw 66 to 68, Melancholic Fairy
Queen 64 to 68, Giant Caithor 62 to 65).

## 5d. Monsters that only a script creates (`mobs.scripted`)

154 kinds that are **not rows of the Mob table**: summoned bosses and adds, encounter spawns, quest event and
quest finale monsters. They are not in `kinds` and have no camps. Cols:

| Field | Meaning |
|---|---|
| `id` | continues after the kind ids (4130 and up), so `#/mob/<id>` can show them |
| `key`, `name` | as for kinds: the name the script gives (a `Name += "'s Minion"` after the template is applied: "Beliathan's Minion"), key = lowercased (drops join on it) |
| `lmin`, `lmax` | the level the code gives (a number, a `Util.Random(a, b)` range, or its template's list); null when the script works it out at run time |
| `cls` | server classes (`GameNPC` for encounter and quest spawns) |
| `rank`, `rankWhy` | as for kinds; `quest` = spawned by a quest step or a quest script |
| `body` | body type id as the kill sees it: the script's own `BodyType = N` when it sets one, else its template's; 0 when there is no template and nothing sets one; null when it could not be established |
| `where` | `[[zone, x, y], ...]`: where it appears: the spot of the world monster or controller whose script creates it, or the spot the quest or script names. Empty when the spot is worked out at run time. |
| `by`, `byNames` | kind ids and names of the world rows whose script creates it (`byNames` also lists controllers, which are not kinds) |
| `how` | one sentence for the page: what makes it appear |
| `src` | `script` (boss and quest scripts), `encounter` (the True 1.65 Prima encounters), `quest` (a `Spawn` step of `classic-quests.json`) |
| `quest` | the quest's name (or quest script class) when a quest makes it appear |
| `file` | the script or data file it was read from |

Found by reading every `x = new SomeNpc(); ... x.AddToWorld()` in `scripts/` and `quests/`, every
`PrimaEncounter165.SpawnAdd(...)` in `scripts/true165/`, and every `Spawn` step of `classic-quests.json`, then
following the owner back to a world row (class or brain), a controller the script places itself, or a quest.
Examples: Apocalypse 87 and the horsemen Fames, Bellum, Morbus, Funus 83 (Caer Sidi), Host 79, Spectral
Provisioner 77, Beliathan (Darkness Falls), Elder Icelord Suttung 81, the Corrupt Primals 83 and the Earthmenders
(Galladoria), Master of Pain 74, the heads of Myrddraxis, Hamorik 61 (Caldey), Gormror and his lieutenants
(Aegir's Landing), Talvor's Spirit, Morgana 90 with Bechard and Silcharde (the Academy epic), Sister Blythe, Kelic.

Left out and counted (`meta.scriptedLeftOut`): 97 script spawns whose name is already a world kind (extra copies:
"ancient zombie" adds, clones), 52 copies of classes that are world rows, 36 that belong to a realm, 23 with no
name in the code (the dragons' adds are named from a list at run time), 13 effect objects and controllers, 13
whose class nothing in this world creates (Blight, Cronker, Alluvian, Unnatural Storm, amalgamate parthanan: their
controller is neither a world row nor made by a script), 4 with a name built at run time.

## 6. Samples

kinds (cols: id, key, name, lmin, lmax, levels, spawns, camps, zones, realm, ag, agIf, ar, al, faction, body, race,
speed, roam, resp, f, cls, hours):

```json
[1726,"grove nymph","grove nymph",10,18,[[10,8.11],[11,8.11],[12,8.11],[13,8.11],[14,8.11],[15,8.11],[16,8.11],[17,8.11],[18,8.11]],73,11,[[1,73]],1,73,0,500,90,27,6,2005,[192,192],200,[80,80],262656,["GameNPC"],null]
[3999,"will o' wisp","will o' wisp",9,11,[[9,18.67],[10,18.67],[11,18.67]],56,14,[[8,32],[9,24]],1,0,0,0,0,0,8,2006,[200,200],200,[80,80],262272,["GameNPC"],null]
[2152,"lammia sorceress","lammia sorceress",52,56,[[52,31.17],[54,31.17],[56,31.17]],121,33,[[55,59],[56,55],[57,7]],1,121,0,500,200,16,6,2005,[191,191],200,[80,80],360960,["GameNPC"],null]
[764,"cuuldurach the glimmer king","Cuuldurach the Glimmer King",80,80,[[80,1.0]],1,1,[[216,1]],3,1,0,800,100,83,3,0,[400,400],0,[3600,3600],16399,["HibCuuldurach"],null]
[3356,"spraggon","spraggon",4,5,[[4,135.0],[5,135.0]],270,109,[[201,155],[200,47],[207,43],[202,25]],3,0,0,0,0,0,4,2003,[188,188],200,[80,600],262144,["GameNPC"],null]
```

camps (cols: id, kind, zone, x, y, z, r, n, lmin, lmax, levels, ag, ar, pack, link, resp, f, rn, rhi, rup, pts, near;
`pts` cut short here):

```json
[773,1726,1,46312,17188,2563,649,10,10,18,[[10,1.11],[11,1.11],[12,1.11],[13,1.11],[14,1.11],[15,1.11],[16,1.11],[17,1.11],[18,1.11]],10,500,2.6,2,[80,80],0,0,0,0,[[45916,17127,10,18,2563], ...],[[2809,1,12,12,1,1197,7,0,0,500,0]]]
[2898,3999,8,18066,40728,1927,1825,12,9,11,[[9,4.0],[10,4.0],[11,4.0]],0,0,0.0,0,[80,80],0,1,13,1,[[16595,41088,9,11,1930], ...],[[4040,2,12,13,2,803,2,1,0,500,0],[3999,4,9,11,0,605,0,0,0,0,0],[3659,3,2,2,0,1185,3,0,0,0,0]]]
[19084,3356,200,20808,17273,4741,1823,6,4,5,[[4,3.0],[5,3.0]],0,0,0.0,0,[80,80],0,10,8,10,[[19896,18850,4,5,4700], ...],[[2686,16,5,6,16,451,8,8,0,500,0],[2971,3,5,8,3,503,5,2,0,500,0],[1848,2,5,7,0,923,8,0,0,0,0],[3199,1,3,4,0,1085,1,0,0,0,0],[2268,5,2,3,0,615,7,0,0,0,0],[2407,3,1,2,0,161,8,0,0,0,0],[1644,4,1,1,0,277,8,0,0,0,0],[3892,3,0,1,0,890,8,0,0,0,0]]]
[22561,764,216,56390,59264,2965,0,1,80,80,[[80,1.0]],1,800,0.0,0,[3600,3600],0,0,0,0,[[56390,59264,80,80,2965]],[]]
[5557,2152,55,26407,12272,7498,0,1,52,56,[[52,0.33],[54,0.33],[56,0.33]],1,500,0.0,0,[80,80],1,0,0,0,[[26407,12272,52,56,7498]],[]]
```

Reading the third: a camp of 6 spraggons (kind 3356) in zone 200 at 20808, 17273, levels 4 to 5, none aggressive.
Sixteen level 5 to 6 monsters of kind 2686 stand among them, all aggressive, 8 of them in reach; three level 5 to 8
of kind 2971 to the south-west, 503 units from the nearest spraggon, 2 in reach. `rn` 10, `rhi` 8, `rup` 10.

factions: `[2,"Barfog Renegade",100,"aggressive",[226],[51,57],0]`
zones: `[0,1835,87,523,0,24,[[0,285.0],[1,106.0],...],593,18,0,111,[[412,115,0,0,0],...],[camp ids],[camp ids]]`
paths: `[2520,0,291,[[17205,44811],[19300,43100],[21330,41503],[23300,39000],[25194,36481]]]`

## 7. What is missing or approximate

- **Scripted monsters** (`f` scripted): the script's level and its summons are now read (sections 5c, 5d). Phases,
  abilities and anything decided during the fight are not.
- **Line of sight** is not computed: aggro and Bring a Friend need it in the game (walls, floors). In dungeons a
  neighbour on another floor counts only when it is within the 3D distance.
- **Standing** is a new character's. A player who has killed a faction's members can find `agIf` monsters hostile.
- **172 monster rows** in Tomb of Mithra, Krondon and Iarnvidiur's Lair have positions outside the zone: dropped,
  as the server cannot place them either.
- The realm of a kind needs `data/zones.js` (build maps before mobs); without it `realm` is 0 and
  `meta.zoneRealms` says "none".
- Experience per kill: only the base value and the cap are given (section 3, xp).
- Monster spells, styles and resist details are not exported (only `caster` and the race's resists).
