# Notes, lane P-D (zones, map pictures, the coordinate rule)

The contract for my files is `source/contract/zones.md`. This file is what I decided and why, and what is open.

## Decided

- **The rule**: every picture carries `px` and `rect`; `pixel = ((x - rect[0]) * px[0] / rect[2], (y - rect[1]) *
  px[1] / rect[3])`; x east, y down; zone-local game units. Outdoors `rect` is the whole zone; in capitals and
  dungeons it is a smaller square (the client's own `regions.dat` square, or the navigation mesh's box).
- **Three kinds of picture per zone** (`layers`, best first), because no single source is best everywhere:
  - `aerial`: the client's own top-down render of the real zone (2048 px, exact, sharp when zoomed);
  - `game`: the game's painted map (labels and icons the owner knows; soft, because the art is 256 px);
  - `plan`: a floor plan I draw from the server's navigation mesh (dungeons and capitals).
  Order: outdoors aerial, game. Dungeons plan, game. Capitals game, plan, aerial. Housing game.
- **Which zones exist**: the server's switches (`disabled_regions`, `disabled_expansions`) plus "the region is in
  use" (a spawn stands in it, or a zone point or teleport leads to it). 127 of 481.
- **Kinds**: added `"battleground"` (250-253) to the contract's list; `"other"` is unused. Avalon City, Trollheim
  and Fomor are `"dungeon"` (hostile single-zone regions), only the three capitals are `"city"`.
- **Realm** of a zone: from the client's realm regions, spread along zone points. Frontier dungeons carry the realm
  whose frontier they open from (Hall of the Corrupt 1, Dodens Gruva 2, Marfach Caverns 3); Darkness Falls,
  Summoner's Hall and the battlegrounds are 0.
- **Server zones 276 / 277** are Marfach Caverns / Hall of the Corrupt (the client builds them as 247 / 245).
  The client's painted map "z245" is the later Labyrinth, not this dungeon: refused by a check (none of the
  zone's spawns is at its floor heights). 277 has the floor plan only.
- **Water on the top-down pictures is painted blue** (the client renders it flat pale grey). Only inside the
  client's own water map of the zone, only pixels of that flat colour.
- **Abermenai's (253) top-down render has no ground** (a faulty render in the client content): refused by a check,
  the zone ships the game's map.
- **Overviews**: one per multi-zone region, zones tiled by their offsets, region coordinates, no painted names.
- Spots outside every zone square (283 `Mob` rows) are not in the world (`GameObject.cs:601-608`) and get no zone.

## For other lanes

- **P-U**: uses `layers`, `view`, `floors`, `overviews` already (seen working on the zone page and the route page).
  A floor picker is exact only for pins that carry `z`; pins without `z` belong on every floor.
- **M-D**: optional `z` per camp (median Z of its spawns) would let dungeon camps sit on their own floor in the 7
  dungeons with `floors`. Not required.
- **V-D / V-U**: route points are region coordinates in the database; an overview takes them directly
  (`rect` is in region units). A route that changes region continues on another overview or on a single zone.
- **D-V**: `python -I source/build_maps.py` takes about 10 s when `maps/` is filled and about 2.5 minutes from
  nothing (378 pictures). It prints its checks and asserts: every picture exists and is square-scaled, the folder
  is under 150 MB, each floor plan holds its zone's spawns. `--no-images` rewrites only `data/zones.js`.

## Open

- 15 housing zones have no picture at all (nothing to build one from). They hold 1 to 9 spawns each.
- The three capitals' top-down pictures are small cut-outs (380 to 684 px): the render is 32 units a pixel for a
  whole zone and a capital is a small part of its zone. New renders would need the game client, which this tool
  must not run.
- 23 dungeons have no per-floor pictures (the client names no floors for them). Most are a single level; in the
  stacked ones a higher floor hides what is under it on the floor plan.
- The game's painted maps come through the client's 4x upscale: some small lettering is smeared.
