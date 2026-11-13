# Notes from lane M-D (monsters and camps data)

Updated 2026-11-12. The field reference is `source/contract/mobs.md`; this file is decisions, needs and open points.

## Decided

- **Runtime name and level.** A spawn's name, level, aggro, faction, flags, race, body type and speed are the
  template's when its NpcTemplate has `ReplaceMobValues = 1` (`GameNPC.cs:1341-1345, 1416-1472`). Kind `key` and
  `name` are that runtime name exactly as the server holds it (`key` lowercased, **not trimmed**: "snemel den
  forsumlig " ends in a space, the loot lookup is `mob.Name.ToLower()`). 389 rows run under another name than
  `Mob.Name`.
- **Levels are distributions.** 51,379 of 77,445 spawn points draw their level from a template list on every
  spawn. `levels` = expected monsters per level (fractions). Nothing is collapsed to one number anywhere.
- **Monsters only.** Categories and counts (they add up to 84,896) are in `mobs.meta.excluded` and mobs.md
  section 2. Guards and keep guards are counted per zone in `mobs.zones` (`guards`, `keepGuards`).
- **Open world = P-D's 127 zones.** Rows in `disabled_regions` / `disabled_expansions` regions and the 172 rows
  outside every zone square are left out (same rule as P-D, D-D, D-V, V-D).
- **Camp** = spawn points of one kind in one zone linked at 1,500 units (3D), cut at 2,500 from the centre.
  Single spawns are camps with `n = 1`.
- **Neighbours** = every monster spawn point outside the camp within 1,500 units (3D) of one of its points, grouped
  by kind; "in reach" = attacks on sight and its aggro range + roaming range + 200 covers a camp point.
- **Attack on sight** is for a new character: aggro level > 0, aggro range > 0, and a faction's base standing must
  be "aggressive" (above 75). 2,283 spawn points have aggro but a friendlier faction: `agIf`.
- **Shared spawn points**: 64 rows use a template id that two templates share; the server picks one at load. Each
  is a point of both kinds (flag `shared`), counted as half a monster in `levels`.
- Training dummies: 12 + 2 rows of class `GameTrainingDummy` are excluded (cannot die); one row named "training
  dummy" is a plain NPC without a template, so it is a real kind with one spawn.

## Build order

`build_mobs.py` reads `data/zones.js` when it exists, only for the realm of each zone (the world's `Zones.Realm` is
0 everywhere). Run `build_maps.py` first. Without it `realm` of every kind is 0 and `mobs.meta.zoneRealms` =
"none". It reads no other lane's file. It also reads `RUNTIME/server/classic-quests.json` (quest monster ids, hour
windows) and the server's C# as text (about 10 s of the 25 s).

## For M-U

- All five points of your note are in: `levels` histogram, `ag` + `ar` per camp, `near` with every neighbouring
  kind (cols in `camps.meta.nearCols`), singles as `n = 1` camps, non-monsters left out.
- `pts` rows are `[x, y, lmin, lmax, z]`.
- Ranking by safety: `rup` (stronger aggressive neighbours in reach), `rn` (all aggressive neighbours in reach),
  `rhi` (their highest level), then `ag` / `pack` for the camp's own behaviour. For the reader's level use `near`
  and `WG.con` (a monster grey to the reader never attacks).
- "Calls for help": `link` per camp, `nLink` per neighbour, rule text in mobs.md section 5. A solo player never
  gets adds on this server; say so rather than warn.
- `mobs.xp[level]` and the cap rule give base experience per kill.
- `mobs.paths` has the patrol routes as lines; `mobs.peaceful` the untouchable no-realm NPCs (not kinds).

## For D-D / I-U (drop pages)

- Join on kind `key`. A drops key with no kind is not a monster page: realm NPCs, peace-flagged NPCs
  (`mobs.peaceful` has their positions) or rows in closed regions.

## For P-U / zone pages

- `mobs.zones`: one row per zone with the level histogram, kinds present, guard counts, safest and densest camps.

## Added 2026-11-12 (owner feedback: stance words, dungeons, bosses)

- **Stance words everywhere**: `st` = [aggressive, hostile, neutral, friendly] on kinds, camps, zone rows and
  `near` entries; camp points carry one value (0..3). Ported from `GameNPC.GetAggroLevelString`
  (`GameNPC.cs:2346-2415`). `ag` stays as "really attacks unprovoked" (18 points say aggressive but have aggro
  range 0). No page may say "attacks on sight".
- **Rank** of every kind: `rank` (normal, named, quest, boss, epic, event) and `rankWhy` (the rule). From the
  server's classes and its realm event list, not from loot or level.
- **Zones**: `mobs.zones` has `group` and `tags` (raid, epic, df, frontier dungeon, starter dungeon);
  `mobs.events` = the 12 realm events; `mobs.meta.coverage` = counts per group.
- **Script-made monsters**: `mobs.scripted` (154 kinds, ids after the kinds). Not in `kinds`, no camps. D-D joins
  loot on their `key`; `body` is the script's own body type when it sets one.
- **The script's level wins** for classes that set `Level` in their own `AddToWorld` (42 classes, 7 kinds change;
  `mobs.meta.scriptLevels`). D-V re-reads the same from the C# (K6).
- Tables gained columns in the middle: read by name (`WG.rows`), never by index.

## Open points

- Scripted monsters (flag `scripted`, 258 kinds): a script can set its own level or spawn adds; not reproduced.
- Line of sight is not computed for aggro or Bring a Friend.
- Whether a grey monster gives experience was not established; the `xp` note says so.
