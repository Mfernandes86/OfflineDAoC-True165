# data/routes.js (V-D)

Built by `python -I source/build_routes.py` (about 10 seconds; it imports `build_vendors.py` so the merchant ids
match). Rules and their `file:line` references are in the builder's header. Last change: 2026-11-12.

```
WG.provide("routes", { meta, notes, stables: [...], routes: [...], broken: [...], factions: {...},
                       gates: [...], teleporters: [...] })
```

All positions are zone id + zone-local `x`, `y` (the `/loc` numbers). All zone ids are in zones.js.

## The short answers

- **Rides are free.** Every ticket item has Price 0 and the stable master charges the item price
  (`GameStableMaster.cs:63`). `price` is there and is 0 on all 189 routes; `meta.free` is true.
- **189 connections, 0 broken** in the v0.7 world. `broken` is `[]` but the builder fills it when a ticket has no
  path, a path that starts 500 or more from its stable master (X or Y), a single point, or a type other than Once.
- **12 flying routes** (4 dragonfly in Albion's isles, 4 gryphon in Midgard's, 4 wyvern in Hibernia's); 9 of them
  need reputation. **No boats**: no boat stable master stands in the world.
- **28 housing routes** (ticket ids `HH_*`) between the housing zones' stable masters, added by True 1.65.

## stables (95) — every stable master a player can reach

| field | meaning |
|---|---|
| `id` | 1..n (region, name order) |
| `name` | the stable master's name |
| `place` | the name of the stop: the destination name on tickets that arrive there ("Castle Sauvage"); if nothing arrives, the server area it stands in, else the stop name in its ticket ids, else the zone name. Two stable masters of one town share a `place` (Caer Diogel, Hagall, Necht) |
| `zone`, `x`, `y` | where it stands |
| `realm` | the NPC's realm; 0 = no realm (the 8 faction and neutral stable masters, and Addard Yarley) |
| `land` | whose land it stands in: 1 Albion, 2 Midgard, 3 Hibernia (from the region) |
| `master` | always true in this world. A ticket that landed more than 12,000 units from every stable master would get an entry with `master: false`, `name: null` |
| `town` | stable masters within 3,000 units of each other share a `town` (= the smallest id among them). Four towns have two: Cleavon Smyth + Vilmalin (Caer Diogel), Beneir Halldorson + Korlis (Hagall), Bulchaid + Zrrazk (Necht), Aethic + Truichon (Tir na mBeo). You walk between them |
| `area` | the server's named area it stands in, or null |
| `vendor` | its id in vendors.js, or null when it sells nothing |
| `tickets` | number of tickets it sells. 6 sell none: Ingrit, Valda, Vlasta, Aethic, Erie, Kohar |
| `guild` | "Stable Master" ("Stablemaster" for Dalniver) |
| `reputation` | null, or the same object as on its routes (below) |
| `faction` | faction id when it has one (a realm stable master can have one without asking for reputation) |

```
{"id": 1, "name": "Addard Yarley", "place": "Western Cornwall", "zone": 6, "x": 17884, "y": 49263, "realm": 0,
 "land": 1, "master": true, "town": 1, "area": null, "vendor": 2, "tickets": 2, "guild": "Stable Master",
 "reputation": null, "faction": 14}
{"id": 54, "name": "Ingrit", "place": "Erikstaad", "zone": 114, "x": 27879, "y": 42314, "realm": 2, "land": 2,
 "master": true, "town": 54, "area": null, "vendor": null, "tickets": 0, ...}
```

## routes (189) — one per ticket that works

| field | meaning |
|---|---|
| `id` | 1..n |
| `from`, `to` | stable ids. `to` is the stable master nearest to where the mount stops (same region) |
| `fromName`, `toName` | `from`'s `place`; the destination as the ticket names it ("Wearyall Village") |
| `ticket` | item id (= the PathID the server loads) |
| `name` | the ticket's name ("Ticket to Castle Sauvage", "Wyvern Ticket to Krrzck") |
| `price` | copper; 0 everywhere |
| `kind` | `"horse"` or `"flying"` (the mount's NPC template has the FLYING flag) |
| `mount` | `"horse"`, `"dragonfly"`, `"gryphon"`, `"wyvern"` |
| `realm` | the selling stable master's realm (0 = sells to any realm, subject to `reputation`) |
| `seconds` | the ride: 3D path length / 1000 + stops at points that have a wait (one decimal). The mount leaves 4 seconds after the purchase (`meta.startDelay`), not included |
| `botSeconds` | what the bots' planner computes for the same path (2D length, each leg at the point's own speed capped at 1000). The cross-check only; pages show `seconds` |
| `length` | 3D path length, game units |
| `points` | number of path points in the database (the drawn `path` is thinned) |
| `gap` | units between where you get off and the `to` stable master. Usually under 2,500; 8 routes are longer, the longest 9,686 (Ludlow to Castle Sauvage stops well south of the stable) |
| `end` | `{zone, x, y}` where the mount stops |
| `reputation` | null or `{faction, factionName, base, maxAggro: 50, need: -50, start, startsRefused, text, how, emissary: {name, zone, x, y} or null}` |
| `path` | `[{zone, pts: [[x, y], ...]}, ...]` in ride order. One entry per stretch inside one zone (up to 6 a route). The point across a zone border is repeated on both sides so the line does not break: a stretch can start or end a little outside 0..65535. Thinned to stay within 20 units of the real path (10,392 points in all) |

```
{"id": 28, "from": 17, "to": 15, "fromName": "Ludlow", "toName": "Castle Sauvage", "ticket": "HS_Ludlow_CastleSauvage",
 "name": "Ticket to Castle Sauvage", "price": 0, "kind": "horse", "mount": "horse", "realm": 1, "seconds": 72.5,
 "botSeconds": 72.3, "length": 72545, "points": 125, "gap": 9686, "end": {"zone": 0, "x": 35388, "y": 5269},
 "reputation": null,
 "path": [{"zone": 2, "pts": [[45998, 43721], [46824, 44070], ...]}, {"zone": 0, "pts": [[-180, 7199], [1248, 7338], ...]}]}
{"id": 153, "from": 73, "to": 69, "fromName": "Necht", "toName": "Krrzck", "ticket": "Wyvern_Necht_Krrzck",
 "name": "Wyvern Ticket to Krrzck", "price": 0, "kind": "flying", "mount": "wyvern", "realm": 0, "seconds": 93.8, ...
 "reputation": {"faction": 89, "factionName": "Krrzck", "base": 100, "maxAggro": 50, "need": -50, "start": -100,
   "startsRefused": true, "text": "Krrzck reputation -50 or better (a new character starts at -100: refused)",
   "how": "Kills only: each kill of a monster of Botonids, Olcasgean has a 20% chance of +1 reputation (about 250 kills from a new character). Killing the faction's own members costs reputation the same way. The stable master's refusal names an emissary, Kzzirrak; that NPC and its hunt are switched off in True 1.65.",
   "emissary": null}}
```

One-way is real: 25 connections have no ticket back (town to town). Derive it from the missing reverse edge; use
`town`, not `id`, when a town has two stable masters.

## Reputation, exactly

The rule is the stable master's, not the route's (`GameNPC.cs:2633-2641`): an NPC that is **not of your realm**,
**has a faction** and **does not have the PEACE flag** will not talk to you while that faction's standing is
Hostile or worse. Standing comes from the aggro level, -100..100, shown in game as reputation = minus aggro:
Friendly up to 25, Neutral 26-50, Hostile 51-75, Aggressive above 75 (`Faction.cs:184-194`). So you need aggro 50
or lower = **reputation -50 or better** (`FactionEmissaryRuntime.RequiredAggroForStables = 50`). A new character
starts at the faction's `BaseAggroLevel` (100 for all three: reputation -100).

Who asks (6 stable masters, 9 routes):

| Faction (id) | Realm it serves | Stable masters that ask | Routes | Open stable master into it |
|---|---|---|---|---|
| Cryptos Mythicos (16) | Albion | Nimea (Clifton), Callisa (Lammia Camp) | Clifton to Lammia Camp, Clifton to Caer Diogel, Lammia Camp to Clifton | Vilmalin (Caer Diogel to Clifton): no faction, serves anyone |
| The Remnants (172) | Midgard | Minerva (Iarn Dwarf Camp) | Iarn Dwarf Camp to Modernagrav, to Hagall | Korlis (Hagall to Iarn Dwarf Camp): Midgard realm, serves Midgard; Hermione (Modernagrav to Iarn Dwarf Camp): peaceful, no faction |
| Krrzck (89) | Hibernia | Zrrazk (Necht), Dalniver (Krrzck), Calvine (Mantid Town) | Necht to Krrzck, Krrzck to Necht, Krrzck to Mantid Town, Mantid Town to Krrzck | none: the whole wyvern loop needs it |

This is the same list the emissary code speaks (`FactionEmissaryRuntime.cs:182-187`).

### How it is earned in True 1.65: by kills only (changed 2026-11-12, found by lane Q)

The reputation emissaries (Kzzirrak, Ysslith, Hrodvar Deepvow) and their repeatable hunt **do not exist in True
1.65**. `FactionEmissaryRuntime` is on the list of freeshard scripts (`mode165/Classic165Mode.cs:56-60`) whose
start-up hook is skipped when `classic165_mode` is on (`events/GameEventMgr.cs:115-126`), and that hook is the only
place the emissaries are created; the world has `classic165_mode = True`. The builder reads all three facts on
every run (`meta.mode165: true`, `meta.emissaries: false`). So:

- `reputation.emissary` is `null` on every route and stable; `factions[id].emissary` is `null`, `hunts` is `[]`,
  `huntsNeeded` is `null`.
- `factions[id].emissaryOff = {name, host, town, why}` names the NPC that the stable master's refusal message
  still tells the player to speak to (`GameStableMaster.cs:34`, `FactionEmissaryRuntime.cs:132-161`: that text is
  static and still runs). A page should say that this NPC is not in the game.
- The only way up is **kills** (`Faction.cs:82-89, 113-135`, `AbstractServerRules.cs:1091`): each kill of a
  monster whose faction is in `killsHelp` has a 20% chance of +1 reputation; each kill of a member of `killsHurt`
  a 20% chance of -1. Every player who damaged the monster rolls. From -100 to -50 is 50 points:
  `killPoints: 50`, `killChance: 0.2`, `killsNeeded: 250` (the average; it is a dice roll).
- `factions[id].killMobs` = what to kill: every no-realm monster kind of a `killsHelp` faction, per zone,
  `{name, zone, x, y, levelMin, levelMax, n, faction}` (`x`, `y` = the spawn nearest the middle of that kind's
  spawns in the zone; `n` = spawn rows; level range includes template ranges). 241 entries for Cryptos Mythicos,
  100 for The Remnants, 62 for Krrzck, dungeons included. Whether a grey monster still counts was not established.

`factions["16" | "89" | "172"]`:

```
{id, name, base: 100, start: -100, need: -50, startsRefused: true,
 stables: [ids that ask], openStables: [ids of that faction that do not ask],
 killsHelp: [faction names], killsHurt: [faction names],
 killChance: 0.2, killPoints: 50, killsNeeded: 250,
 killMobs: [{name, zone, x, y, levelMin, levelMax, n, faction}, ...],
 emissary: null, emissaryOff: {name, host, town, why}, hunts: [], huntsNeeded: null}
```

In a world with the mode off the same builder fills `emissary` (`{name, title, realm, zone, x, y, host,
hostStable, town, mounts}`), `hunts` (`{name, zone, x, y, level, levelMin, levelMax, n, common}`: the species the
emissary can name, `FactionReputationTargets.cs`), `huntsNeeded` and `huntEnemies`, and `how` describes the hunt
(10 kills, +10, +5 after a reroll). That is not this game.

Merchants of these factions follow the same rule (vendors.js `rep`).

## Other fixed travel

- `gates` (169): the server's zone points, `{id, realm, from: {zone, x, y}, to: {zone, x, y}, rule?}`. Walking onto
  `from` puts you at `to` (city doors, dungeon entrances, the crossings between mainland and isles). `realm` 0 =
  any realm. `rule: "DFEnterJumpPoint"` (12) = the Darkness Falls doors, which have their own entry rule (not
  ported). `id` is the table's Id and is not unique. No disabled or zone-less side in this world. The jump is
  triggered by the game client (its own jump box sends the zone point id); `from` is the spot the server records
  for it. The three mainland-to-isles entrances are the True 1.65 ones: 153 Avalon Marsh to Isle of Glass, 165
  Gotar to Aegir's Landing, 167 Shannon Estuary to Domnann (`world/ShroudedIslesPortals.cs:48-55`).
- `teleporters` (8): the Shrouded Isles teleporter NPCs, `{name, guild, realm, zone, x, y, to: [{name, zone, x, y}]}`.
  They send their own realm to four towns of the isle for free (`AlbionSITeleporter.cs` and its two siblings; the
  server hides the town you are standing in).
- **Not covered**: the old-frontier teleporters (`OFTeleporter`, 9 NPCs, `meta.otherTeleporterNpcs`), battleground
  and housing teleporters, medallion travel (the medallion merchants are in vendors.js, kind `medallions`), and
  boats (none exist).

## meta

`{built, stables, landings, routes, broken, flying, free, speed: 1000, startDelay: 4, boardDistance: 500,
arriveMax: 12000, simplify: 20, leftOut, gatesLeftOut, pathPointsInNoZone, checks, content, otherTeleporterNpcs,
quest: {kills: 10, gain: 10, gainRerolled: 5, levelCap: 45, levelWindow: 3, minSpawns: 5, killChance: 20}}`.
`mode165` / `emissaries` say whether True 1.65 Mode is on and whether the reputation emissaries exist (true /
false here). `quest` holds the emissary hunt's constants from the code; they do not apply while `emissaries` is
false. `content` is the cross-check against the True 1.65 content files: all 28 housing routes and all 99 routes that
`add-routefloor165.json` re-laid are in the world and ride.

## Ours, not the server's

- `to` and `gap`: the server has no "destination stable"; a ride just ends at the last path point. We name the
  nearest stable master of the region as the destination (limit 12,000 units). It is never a close call: the
  stable master of any other town is at least twice as far from the end of the ride (Ludlow to Castle Sauvage:
  9,686 against 19,714) and at least 4.5 times as far on every other route.
- `town` (3,000 units) and the thinning of `path` (20 units).
- `place` when no ticket arrives at a stable.
