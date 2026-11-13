# Notes from lane M-U (hunting pages, con library)

Updated 2026-11-12 (final).

## For everyone: `app/lib/concolor.js`

Ported from `GameServer/gameutils/ConLevels.cs` (table lines 19-69, levels 51-127 at 74-77, fill 79-92, lookup
95-104). True 1.65 mode does not change the table. Checked twice: in the browser against
`source/tests/con_vectors.json` (2,226 vectors, 0 mismatches) and by D-V's `source/verify_con.py` (PASS, 16,900 cells).

| Call | Gives |
|---|---|
| `WG.con.color(myLevel, mobLevel)` | `"grey"`, `"green"`, `"blue"`, `"yellow"`, `"orange"`, `"red"`, `"purple"` |
| `WG.con.value(myLevel, mobLevel)` | the server's number, -3 (grey) to 3 (purple) |
| `WG.con.levels(myLevel, color)` | `[min, max]` monster levels of that colour, or `null` when none (level 1 has no grey or green). Purple ends at 127. |
| `WG.con.bands(myLevel)` | `[{color, min, max}, ...]` lowest first |
| `WG.con.span(myLevel, min, max)` | `{low, high, mixed}`: the colours of both ends of a level range |
| `WG.con.presets` | `[{id, label, colors, part, hint}]`: `low-purple`, `low-red`, `red`, `orange`, `yellow`, `blue`, `general` |
| `WG.con.presetLevels(myLevel, id)` | `[min, max]` or `null` |
| `WG.con.name(color)`, `WG.con.cls(color)` | `"Red"`, `"con-red"` (the css classes in `app.css`) |
| `WG.con.rangeText(min, max)` | `"24"` or `"20 to 26"` |
| `WG.con.el(myLevel, min, max)` | a `<span>`: `20 to 26` with each end in its con colour, hover text says the colours |

Facts worth knowing:
- Either level outside 0..127 answers yellow: that is what the server does (`ConLevels.cs:97-101`), so the port
  does it too. No monster in `mobs.js` is above 127 (the level 255 rows are guards, which M-D leaves out).
- "Low red" and "Low purple" are not server terms. This tool's cut, shown on the hunt page with the levels:
  low red = the lower half of the red band, rounded up; low purple = the same number of levels at the bottom of
  purple. Level 30: red 34 to 36, low red 34 to 35, low purple 37 to 38. Level 50: red 56 to 60, low red 56 to 58,
  low purple 61 to 63. "General" = blue, yellow and orange.
- `source/tests/con_vectors.json` is made by `source/scratch/M-U/gen_con.py` (a Python re-run of the C# static
  constructor, read from the C# text). Re-run it if `ConLevels.cs` ever changes.

## For other page lanes: `WG.hunt` (in `app/views/hunt.js`, loaded before `mobs.js` and `mob.js`)

`WG.hunt.load()` gives a Promise of one model over `mobs.js` + `camps.js` + `zones.js`:
`{kinds, kindById, kindByKey, camps, campById, byKind, byZone, zones, zoneById, factions, factionById, races,
xp, pathsByCamp, pathsByKind, nearDist, rules, flag, campFlag, dirs, meta}`. It adds aliases to M-D's rows (the
same objects `WG.rows` hands everyone): kind `levelMin`, `levelMax`, `zoneIds`, `aggroShare`, `aggroRange`;
camp `levelMin`, `levelMax`, `radius`, `aggroN`, `aggroRange`, `hist`, `nearList`. It also trims the one kind
`name` that ends in a space (the `key` keeps it).

- `WG.hunt.fit(camp, lo, hi)` -> `{total, inside, above, below, share, mean}`: how much of a camp is inside a
  level range, from M-D's level histogram (expected monsters per level).
- `WG.hunt.threats(camp, myLevel)` -> `{sev, list, grey, stronger}`; `WG.hunt.warning(camp, myLevel, {max})` ->
  the warning line as an element; `WG.hunt.dangerPins(camp, myLevel)`, `WG.hunt.patrolLines(camp, myLevel)`,
  `WG.hunt.pinsFor(camp, myLevel)` -> pins and lines for `WG.Map`.
- `WG.hunt.tagEls(kind, camp)`, `WG.hunt.kindFacts(kind)`, `WG.hunt.campNotes(camp, myLevel)`,
  `WG.hunt.xpFor(mobLevel, myLevel)`.
- `WG.hunt.drops()` -> Promise of `{mobs, quests, meta, flags, item(id), gearFor(kind, myLevel)}`.

### Stance words (owner, 2026-11-12)

The pages never say "attacks on sight" as a label. They use the word of the game's con message, for a new
character: **aggressive, hostile, neutral, friendly** (`GameNPC.cs:2346-2417`), from M-D's `st` counts per camp,
kind and neighbour (`[aggressive, hostile, neutral, friendly]` spawn points). A camp whose spawns differ shows its
split ("20 aggressive, 5 neutral"). `WG.hunt.stanceText(st)`, `stanceEl(st)`, `stanceTags(st, aggroRange, onSight)`,
`stanceCalm(st)`. The stance filter (hunt and monsters list): any / neutral or friendly only (no aggressive or
hostile spawn at all) / aggressive or hostile (at least one). Tooltips explain each word in plain terms.
The warnings still use M-D's `nReach` (an aggressive monster whose aggro range reaches the camp).

### Places (owner, 2026-11-12)

- Realm filter = that realm's zones, every zone of no realm (Darkness Falls, the battlegrounds) **and every
  realm's frontier**. Nothing is hidden by kind of place by default; a line under the filters says what is searched.
- Zone kind chips (All kinds, Outdoor, Dungeon, Frontier, Battleground, Darkness Falls) and Lands chips (both,
  Classic, Shrouded Isles), each with the number of camps it would show with every other filter as it is.
  Address: `where=outdoor|dungeon|frontier|battleground|df`, `lands=classic|si` (the old `kind=` / `exp=` still read).
- Zone badges from M-D's `mobs.zones.tags` (mobs.md 5b): `epic` -> "Epic dungeon" (Caer Sidi, Tuscaran Glacier,
  Galladoria); `raid` in a dungeon -> "Raid dungeon" (Summoner's Hall, Darkness Falls); `raid` outdoors -> "Dragon
  zone" (Dartmoor, Malmohus, Sheeroe Hills: the ordinary camps there are not part of the raid, the tooltip says so);
  `frontier dungeon`, `starter dungeon` as plain badges. The tooltip names the realm event and its final boss from
  `mobs.events`. `ez=no|only` leaves the eight epic / raid zones out or shows only them (`M.epicZones`).
  Darkness Falls is found by the `df` tag.

### Bosses and epic encounters: `#/hunt?mode=bosses`

One click from the hunt page (the two buttons at its top) and from the monsters list. Lists every kind whose
`rank` (M-D, mobs.md 5c) is not `normal`: named, quest target, boss, epic or raid boss, realm event boss, plus the
script-made monsters of those ranks from `mobs.scripted` (5d). Columns: class with a "Group content" mark and a
"Script-made" mark, levels in con colours, zone with its tag, how it appears (realm event, M-D's `how` sentence,
night / day / hours, spawn points and respawn), stance, best drop for my character. Filters with counts: class,
realm, zone kind (with "Epic dungeon or raid zone"), zone, level, group content, name.
- Class = `rank`. The label for `epic` is "Epic or raid boss", for `event` "Realm event boss".
- **Group content** = M-D's rule: the kind flag bits `epic` / `boss` (the classes the server scales as epic
  encounters); for a script-made monster its rank `epic` / `event`, or a `rankWhy` that names GameEpicNPC /
  GameEpicBoss. It is the server's class, not a head count.
- `#/mob/<id>` works for the 152 script-made monsters too (ids 4130 and up, or by name): class, how it appears,
  who creates it, quest, realm event, the spot on the map, and its drops (from `drops.unspawned` when the name
  is not a world spawn). Their stance is not in the data and the page says so.
`WG.hunt.classOf(kind)`, `classLabel(id)`, `isGroup(kind)`, `appearEl(kind)`, `zoneTagEls(zoneId)`, `eventsOf(kind)`,
`levelEl(kind, myLevel)`, `find(M, idOrName)`, `bossRows(M)`; model: `M.extras`, `M.extraById`, `M.events`,
`M.zoneTags`, `M.epicZones`. The code for the list is in `app/views/mobs.js` (`WG.hunt.bossPage`), drawn inside
the hunt view.

### The warning rule (this tool's reading of M-D's `near` list)

A neighbouring kind whose top level is grey to the reader is left out (a grey monster never attacks on sight:
`StandardMobBrain.cs:686-688`). "Stronger" = its top level is above the camp's top level.

| Level | Colour | When |
|---|---|---|
| 3 | red | stronger, blue or higher to me, and `nReach` > 0 (it attacks on sight and its aggro range + roaming + 200 covers a spawn point of the camp) |
| 2 | orange | stronger, any other case (out of reach of the spawn points, or it does not attack on sight) |
| 1 | yellow | not stronger, but `nReach` > 0 (extra fights at or below the camp's level) |
| 0 | green | nothing of the above |

"No stronger neighbours" on the hunt page hides camps with any non-grey stronger neighbour (levels 2 and 3).

### How a wide camp counts

The chosen colour gives a monster level range. A camp counts when **all** of its expected spawns are inside the
range, **most** (more than half; the default) or **any**. The row always shows the camp's full range in the con
colours of both ends, how many are inside, and how many are above ("4 above, up to 36") or below.

### Sort rules (shown under the sort box)

- Best overall = share inside x 50 + min(12, monsters inside) / 12 x 30 - warning (25 red, 8 orange, 2 yellow)
  - share above the range x 30 - share that attacks on sight (`ag`) x 5.
- Safest = risk ascending: red neighbour 10 + levels above the camp (at most 10), orange 3, yellow 1, plus
  share above the range x 8, plus the share that attacks on sight.
- Most monsters = monsters inside the range. Closest = |camp mean level - middle of the range|.

These weights are this tool's choice, not server rules.

## Styles

The three monster pages keep their own small rules in one `<style id="mu-style">` block made by `WG.hunt.css()`
(class prefix `mu-`), the way I-U does for item text. Nothing in `app.css` was needed. P-U: move them into
`app.css` if you prefer; nothing else depends on where they live.

## Open problems

- The "drops for you" line ranks with I-U's `WG.upgrade.value`; it shows nothing until `items.js` and
  `itemmeta.js` have loaded (about half a second after the list appears).
- Line of sight is not in the data (M-D): a neighbour behind a wall still counts as "in reach".
