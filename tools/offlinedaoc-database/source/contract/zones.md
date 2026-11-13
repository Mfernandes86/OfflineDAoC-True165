# data/zones.js and maps/ (lane P-D)

Built by `python -I source/build_maps.py`: about 10 s when the pictures exist (a picture is only re-made when its
source is newer), about 2.5 minutes from an empty `maps/` folder or with `--force`. `--no-images` writes only
`data/zones.js`. Load with `WG.load("zones")`.

## 1. The coordinate rule (proved, section 6)

Every picture in this file comes with `px = [W, H]` (its size in pixels) and `rect = [x0, y0, w, h]` (the square of
the zone it shows, in zone-local game units: the `/loc` numbers).

```js
// game position (zone-local x, y) -> pixel on a picture `p` (a layer, a floor, or the zone itself: zone.px / zone.rect)
function project(p, x, y) {
  return [(x - p.rect[0]) * p.px[0] / p.rect[2], (y - p.rect[1]) * p.px[1] / p.rect[3]];
}
// pixel -> game position
function unproject(p, px, py) {
  return [p.rect[0] + px * p.rect[2] / p.px[0], p.rect[1] + py * p.rect[3] / p.px[1]];
}
```

- x grows to the right (east), **y grows down** (south). No flip, no rotation, no margin, the same scale both ways.
- Outdoor zones: `rect` is the whole zone, `[0, 0, 65536, 65536]`. Every zone of True 1.65 is 65536 x 65536 units.
- Capitals and dungeons: the picture shows only the part of the zone that is built, so `rect` is a smaller square
  somewhere inside (or slightly past the edge of) the zone. **Always use the picture's own `rect`**; never assume
  the whole zone. A pin outside the picture's rect is still a real place: draw it on plain background.
- Region position to zone-local: `x = X - zone.ox`, `y = Y - zone.oy` (`wgcommon.zone_of` gives the zone).
- Overview pictures (section 4) use the same rule with **region** coordinates.
- Height (Z) plays no part in the rule. Dungeon floors use it only to say which floor a spot is on (section 3).

## 2. Which zones are in

The world table has 481 zones. **127 are kept, 354 left out**, by the server's own switches:

| Rule | Zones left out |
|---|---|
| The zone's region belongs to an expansion the server has switched off: `ServerProperty disabled_expansions = "3;4;5;6"` (Trials of Atlantis and later), applied the way `GameServer/world/Region.cs:255-264` applies it (Regions.Expansion + 1) | 311 |
| The region is switched off: `ServerProperty disabled_regions = "27;165;233-242;244"` (`Region.cs:234-242`): the tutorial, the New Frontiers battlegrounds, Passage of Conflict | 15 |
| The region is loaded but unused: no spawn stands in it and no zone point or teleport leads to it (regions 163 New Frontiers, 192-194, 231, 232, 245, 247, 325, 326, 328, 355, 356, 359) | 28 |

Kept: 46 regions. Kinds: 51 outdoor, 12 frontier, 4 battleground, 3 city, 30 dungeon, 27 housing.

283 `Mob` rows stand outside every zone square of their region (regions 21, 61, 161 and the switched-off 233): the
server cannot place them (`GameObject.cs:601-608`: no zone at the position, `AddToWorld` fails), so they have no zone and no pin. `wgcommon.zone_of`
returns `None` for them. Checked against the navigation meshes: none of them is on real floor (for example 113
rows of Tomb of Mithra at X 89,911 to 98,695 when the dungeon's mesh ends at X 33,254).

## 3. Shape

```
WG.provide("zones", {
  built: "2026-11-12 17:40",
  rule: "...the rule above as one sentence...",
  zones: [ zone, ... ],            // sorted by id
  regions: [ {id, name, realm, expansion, zones: [zone ids]}, ... ],
  overviews: [ overview, ... ],    // section 4
  dropped: {total, later_expansion, region_switched_off, unused_region, no_region_row, switches, spawns_outside_every_zone}
})
```

A zone:

| Field | Meaning |
|---|---|
| `id` | ZoneID |
| `region` | RegionID |
| `name` | `Zones.Name` |
| `realm` | 0 none, 1 Albion, 2 Midgard, 3 Hibernia. The nine realm regions are named by the client (`maps.json` regions); every other region takes the realm of the regions whose zone points lead into it, 0 when several realms lead in (Darkness Falls, Summoner's Hall) or none (battlegrounds). A realm's frontier zones and frontier dungeons carry that realm. |
| `kind` | `"outdoor"`, `"frontier"` (the 12 zones on the client's realm war map), `"battleground"` (the `Battleground` table: 250-253), `"city"` (the three capitals, `CapitalRegion`), `"dungeon"` (any other region made of one zone: classic dungeons, the Shrouded Isles ones, and Avalon City / Trollheim / Fomor, which are hostile), `"housing"` (`Regions.HousingEnabled`). `"other"` is not used. **`"battleground"` is one more value than the contract draft listed.** |
| `expansion` | `"classic"` or `"si"` (`Regions.Expansion` 0 / 1) |
| `w`, `h` | zone size in game units (always 65536) |
| `ox`, `oy` | the zone's corner in region units (`Zones.OffsetX * 8192`): zone-local = region - this |
| `layers` | the pictures of the zone, best first: `[{id, label, src, px: [W, H], rect: [x0, y0, w, h], z?}]`. Empty for 15 housing zones. |
| `map`, `px`, `rect` | the first layer's `src`, `px`, `rect` (null when there is no picture): the contract's simple form |
| `thumb` | a 256 px thumbnail of the first layer, or null |
| `view` | `[x0, y0, w, h]`: the square to show first. The whole zone outdoors; in a city or dungeon the square around every picture, the floor mesh and every spawn, plus 3 %. Use it for a zone with no picture too. |
| `floors` | only the 7 dungeons for which the client has floor maps (19, 60, 125, 150, 161, 190, 249; 23 floors): `[{name, z: [z0, z1], layers: [...], src, px, rect}]`. A spot is on a floor when `z0 < Z <= z1` (the client's own test, `map_window.gd:185`). `layers` is like a zone's: `plan` (the whole dungeon dim, this floor lit) then `game` (the game's own floor map); `src` / `px` / `rect` repeat the `game` one. Not every spot is on a named floor (Trollheim: 80.6 % of its spawns are), so a spot outside every band belongs on the whole-dungeon picture only. A pin needs its Z for this: without Z, show the pin on every floor. |
| `spawns` | `Mob` rows standing in the zone (monsters, merchants, guards: everything) |
| `desc` | the client's own zone description (may be empty) |
| `bg` | battlegrounds only: `[min level, max level]` from the `Battleground` table |

Layer ids:

| `id` | `label` | What it is | Size |
|---|---|---|---|
| `aerial` | From above | The zone seen straight down: the client's own orthographic render of the real terrain, water, buildings and trees. 32 game units a pixel. For a city it is the same picture cut to the built part (so smaller than 2048: Camelot 380 px, Jordheim 554, Tir na Nog 684, Avalon City 1019). Water is painted blue here: the client renders it as flat pale grey (it mirrors the sky), which reads as snow; only pixels inside the client's own water map of the zone that have that flat colour are changed. 70 zones. | 2048 x 2048 JPEG, quality 84 |
| `game` | Game map | The game's own painted zone map (the one the Map window shows, with its labels), with the project's period fixes. The client's art is 256 px; this is the 4x version the game itself displays (its lettering is a little soft: that is the game's upscale, not this tool's). 108 zones. | 1024 x 1024 JPEG, quality 88 |
| `plan` | Floor plan | Drawn here from the server's navigation mesh (the floor monsters can walk on), top down. Lower floors are darker and drawn first, higher floors lighter and on top; dark lines are walls and drops. `z: [low, high]` is the height the shading spans. 33 zones: every dungeon and capital (plus 23 floor versions). | 2048 x 2048 PNG, 96 colours |

Recommended first layer (the order of `layers`): outdoors `aerial` then `game`; dungeons `plan` then `game`;
capitals `game` (it names the buildings) then `plan` then `aerial`; housing `game`.
A viewer that offers a switch between the layers needs nothing else: each layer carries its own `rect`.

Files: `maps/aNNN.jpg`, `maps/zNNN.jpg`, `maps/pNNN.png`, floors `maps/zNNN_FF.jpg` and `maps/pNNN_FF.png`,
thumbnails `maps/t/zNNN.jpg`, overviews `maps/overview/rRRR.jpg`. NNN is always the zone id of zones.js.
(`maps/build-notes.json` is the builder's own memory between runs; pages do not read it.) 378 pictures, 82 MB.

## 4. Overviews (for the route pages)

One picture for every region that has more than one zone: the zones' own pictures tiled by their real offsets.

```
{ id: 1, name: "Albion", realm: 1, expansion: "classic",
  src: "maps/overview/r001.jpg", px: [2208, 2784],
  rect: [286720, 286720, 376832, 475136],          // REGION units covered: x0, y0, w, h
  zones: [ {id: 0, px: [1536, 1152, 384, 384], pictured: true}, ... ] }   // each zone's square on the picture
```

| id | Region | Zones | Picture |
|---|---|---|---|
| 1 | Albion (with its frontier) | 14 | 2208 x 2784 |
| 100 | Midgard (with its frontier) | 14 | 1728 x 2688 |
| 200 | Hibernia (with its frontier) | 14 | 1728 x 2880 |
| 51 | Avalon (Albion's Shrouded Isles) | 7 | 1920 x 1152 |
| 151 | Aegir (Midgard's) | 7 | 1440 x 1248 |
| 181 | HyBrasil (Hibernia's) | 7 | 1152 x 1728 |
| 2, 102, 202 | the three housing regions | 9 each | 1152 x 1152; 5 of the 9 squares are plain (no picture exists) |

- Rule: `pixel = ((X - rect[0]) * px[0] / rect[2], (Y - rect[1]) * px[1] / rect[3])` with region X, Y: exactly what
  `Mob.X`, `PathPoints.X` and `ZonePoint` hold. From a zone-local position: `X = zone.ox + x`.
- One zone is 384 px (48 px per 8192-unit tile). No text is painted on the picture: draw the zone names from
  `zones[].px` (the zone's name is in `zones`).
- A route that leaves a region (Albion mainland to Avalon, a capital, a dungeon) continues on another overview or
  on a single zone: the Shrouded Isles and the mainland are different regions with unrelated coordinates, so they
  cannot share one picture. Single-zone regions (capitals, dungeons) have no overview: use the zone itself.
- The client's own painted realm maps (`content/maps`, r001 and so on) were not used: they leave out the frontier
  zones and are 256 px. The tiles here join exactly (rivers and roads run on across the zone edges), which is also
  the proof that offsets and orientation are right.

## 5. Sample (5 zones, `desc` cut short)

```json
{"id":0,"region":1,"name":"Camelot Hills","realm":1,"kind":"outdoor","expansion":"classic","w":65536,"h":65536,
 "ox":548864,"oy":483328,"map":"maps/a000.jpg","px":[2048,2048],"rect":[0,0,65536,65536],"thumb":"maps/t/z000.jpg",
 "layers":[{"id":"aerial","label":"From above","src":"maps/a000.jpg","px":[2048,2048],"rect":[0,0,65536,65536]},
           {"id":"game","label":"Game map","src":"maps/z000.jpg","px":[1024,1024],"rect":[0,0,65536,65536]}],
 "view":[0,0,65536,65536],"spawns":1965,"desc":"Nestled by the majestic City of Camelot, ..."}

{"id":19,"region":20,"name":"Stonehenge Barrows","realm":1,"kind":"dungeon","expansion":"classic","w":65536,"h":65536,
 "ox":8192,"oy":8192,"map":"maps/p019.png","px":[2048,2048],"rect":[15364,13233,13358,13358],"thumb":"maps/t/z019.jpg",
 "layers":[{"id":"plan","label":"Floor plan","src":"maps/p019.png","px":[2048,2048],"rect":[15364,13233,13358,13358],"z":[13420,16723]},
           {"id":"game","label":"Game map","src":"maps/z019.jpg","px":[1024,1024],"rect":[16622,13583,12694,12694]}],
 "view":[16241,13202,13456,13456],"spawns":332,"desc":"Located beneath the sparse soils of Salisbury plains, ...",
 "floors":[{"name":"Upper Barrows","z":[15399,17605],"src":"maps/z019_00.jpg","px":[1024,1024],"rect":[16622,13583,12694,12694],
            "layers":[{"id":"plan","label":"Floor plan","src":"maps/p019_00.png","px":[2048,2048],"rect":[15364,13233,13358,13358],"z":[15626,16766]},
                      {"id":"game","label":"Game map","src":"maps/z019_00.jpg","px":[1024,1024],"rect":[16622,13583,12694,12694]}]},
           {"name":"Lower Barrows","z":[13399,15399], ...}]}

{"id":26,"region":10,"name":"City of Camelot","realm":1,"kind":"city","expansion":"classic","w":65536,"h":65536,
 "ox":8192,"oy":8192,"map":"maps/z026.jpg","px":[1024,1024],"rect":[21900,13350,11200,11200],"thumb":"maps/t/z026.jpg",
 "layers":[{"id":"game",...,"rect":[21900,13350,11200,11200]},{"id":"plan",...,"rect":[21634,13049,12072,12072],"z":[7501,10122]},
           {"id":"aerial",...,"px":[380,380],"rect":[21568,12960,12160,12160]}],
 "view":[21556,12961,12153,12153],"spawns":223,"desc":"You are in the grand city of Camelot, ..."}

{"id":250,"region":250,"name":"Caledon","realm":0,"kind":"battleground","expansion":"classic", ...,
 "map":"maps/a250.jpg","px":[2048,2048],"rect":[0,0,65536,65536], ..., "spawns":410,"bg":[30,35]}

{"id":20,"region":2,"name":"Brisworthy","realm":1,"kind":"housing","expansion":"classic","w":65536,"h":65536,
 "ox":458752,"oy":458752,"map":null,"px":null,"rect":null,"thumb":null,"layers":[],"view":[0,0,65536,65536],"spawns":3,"desc":""}
```

## 6. Proof of the rule

By looking (overlays drawn with nothing but `data/zones.js`, the shipped pictures and the rule; tool
`source/scratch/P-D/overlay.py`, pictures kept in `source/scratch/P-D/`):

- `proof_z000_aerial.jpg`, Camelot Hills from above: the three stable masters stand at Nob's Stable, Gronyr's Farm
  and the Camelot gate, the merchants in the houses of Cotswold and Prydwen Keep, the bind stones at Castle
  Sauvage, Cotswold and Prydwen Bridge, the horse route points on the roads, monsters on the fields and none in
  the river.
- `proof_z000_game.jpg`, the same on the game's painted map: the stable masters sit on the map's horse icons.
- `proof_z249_plan.jpg`, Darkness Falls floor plan: 2,226 monsters and 27 merchants, all in rooms and corridors.
- `proof_z026_game.jpg` / `proof_z026_plan.jpg`, City of Camelot: merchants inside the buildings on both.
- `proof_z051_aerial.jpg`, Isle of Glass (Shrouded Isles): the pond monsters stand in the ponds, none in the open sea,
  merchants and stable masters in the two villages and the keep.
- `proof_z150_game.jpg`, Trollheim on the game's map with the client's own square (not the whole zone).
- `proof_z060_floor2_game.jpg` and `proof_z249_floor3_plan.jpg`: only the spawns whose Z is in the floor's band,
  drawn on that floor's picture: they fill the lit rooms and nothing else.
- one per family besides: `proof_z100_aerial.jpg` (Midgard), `proof_z200_aerial.jpg` (Hibernia),
  `proof_z151_aerial.jpg` and `proof_z181_game.jpg` (Shrouded Isles), `proof_z011_aerial.jpg` (frontier),
  `proof_z252_aerial.jpg` (battleground), `proof_z013_game.jpg` (housing: the four stable masters sit on the
  map's stable icons, under their own painted names), `proof_z120_game.jpg`, `proof_z209_aerial.jpg`,
  `proof_z050_aerial.jpg` (cities, cut-out top-down pictures), `proof_z248_plan.jpg`, `proof_z277_plan.jpg`.

By counting (the builder prints these on every run and fails if a floor plan drops under 80 %):

- Floor plans, all 33: 92.5 % to 100 % of each zone's spawns stand on drawn floor (the mesh is the floor minus a
  body's width from the walls, so a few stand in the margin).
- Game maps of capitals and dungeons: 94.8 % to 100 % of each zone's spawns are inside the square the client gives.
- Twelve zone records compared field by field with the database and the client's map squares: no difference.
- One client picture is refused by this check: client zone 245's map (used for server zone 277, Hall of the
  Corrupt) is the later Labyrinth map: none of the zone's spawns is at its floor heights. Zone 277 ships the floor
  plan only.

## 7. What is missing

- **No picture (15 zones)**: housing zones 20, 64, 260, 261, 262 (Albion), 121, 122, 266, 267, 268 (Midgard), 219,
  225, 272, 273, 274 (Hibernia). The client was never built for them, the game has no map of them and the server
  has no mesh. They hold 1 to 9 spawns each. `map: null`, `layers: []`, `view` = the whole zone.
- **Abermenai (253) has the game's map only**: the client's top-down render of it came out without its ground
  (81 % of it is one flat colour), so the builder refuses it (any whole-zone render over 65 % flat is refused; the
  next flattest real one is Campacorentin Forest at 49 %).
- **Floor plan only (4 zones)**: 246 Dodens Gruva, 248 Summoner's Hall, 276 Marfach Cavern, 277 Hall of the
  Corrupt: the game has no (matching) map of them.
- The 12 housing zones that have a picture have the game's painted map only (no top-down render exists).
- A floor plan shows the whole dungeon in one picture, so in a stacked dungeon a higher floor hides what is under
  it. The 7 dungeons with `floors` have per-floor pictures for that; the other 23 dungeons do not (the client names
  no floors for them, and most are a single level).
- The top-down pictures were rendered by the game client; this tool cannot render new ones (no game runs), so the
  three capitals' top-down pictures are small cut-outs and housing has none.
