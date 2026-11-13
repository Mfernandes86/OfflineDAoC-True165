# Notes from P-U (map viewer, widgets, css, home, search, zones) — living file

Last update: 2026-11-12.

## What is there

- `source/contract/widgets.md`: the signatures. Everything in it is implemented; additions since the first version are
  marked "Added after the first version". Nothing was removed or changed in meaning.
- `app/components/widgets.js` (`WG.ui`), `app/components/map.js` (`WG.Map`), `app/components/me.js`.
- `app/app.css`: the lead's tokens kept, components added, sections numbered at the top of the file.
- Pages: `#/home`, `#/search?q=&type=`, `#/zones?realm=&kind=&q=&sort=`, `#/zones/<id>?kind=&x=&y=&focus=&routes=1&singles=0`.

## Decisions

- **The map picture is an `<img>` under a transparent canvas.** Drawing the picture into the canvas together with
  text came out black in headless Edge with `--disable-gpu` (what `source/shot.py` uses); the `<img>` way shows
  right in both and pans more smoothly. Pins, lines, grid and labels are on the canvas, so hundreds of pins are one
  cheap redraw.
- **Coordinates**: only `geom()` in map.js knows P-D's rule (`px`, `rect`, `view`, `layers`, `floors`, overviews).
  A picture is always placed by its own `rect`; the viewer opens on `zone.view`.
- **/loc form**: the client prints `Zone Name: loc=X,Y,Z dir=D` (`game/zone_viewer.gd:1690`). The viewer shows and
  copies `Zone Name: loc=X,Y` (no height: the map has none).
- **Camp colour on the zone page** = the con of the camp's commonest level (`levels` mode) to the character's
  level; the label always states the full range. One legend entry per con colour (click to hide a colour).
- **Zone "most monsters" range** on the zone list = the middle 90 % of the zone's level histogram
  (`mobs.zones.levels`); the full range is shown next to it.
- **Header**: two rows (brand, search, my character / the pages) so it never wraps unpredictably; `--header-h` is
  measured, sticky table heads sit under it.
- Search ranks: whole name, name starts with the text, a word starts with it, anywhere; then common monsters and
  obtainable items first.

## For other lanes

- I-U, V-U: your injected style blocks (`#it-css`, `.vu-*`) are fine where they are; they use the tokens. Send me
  rules only if you want them in app.css.
- `WG.ui.table` columns take `first: "desc"` (I-U asked).
- `WG.Map` overviews for the route network (V-U asked): `overview: <region id>`, pins with `zone`, line points
  `[x, y, zoneId]`.
- M-U: camps carry `z`; pass it as `pin.z` and the dungeon floor picker filters your pins.

## Open

- A floor picked in a dungeon filters only pins that carry `z`.
- Pins far outside a dungeon picture's `rect` are drawn on plain background (they are real places; P-D's note).

## Later the same day

- **Owner feedback (stance words, bosses, dungeons)**: `WG.ui.stance(o)` / `stanceWord` / `stanceText` and
  `WG.ui.rank` / `rankBadge` are the one place for these words (widgets.md section 5). They read M-D's new fields
  first (`stance` on a kind, camp or kindList entry; `rank` or `class` on a kind; `tags` on a `mobs.zones` row) and
  fall back to what mobs.js has today (`ag`, the faction's standing, the flag bits). M-D: if your field names
  differ, tell me and I change the readers, not the pages.
- Zone list: counts on the realm and kind chips; a realm filter also shows the zones no realm owns; zone tags
  become badges and a "Special" filter when the data has them. Zone page: "Bosses and named monsters" first.
- **Quests (lane Q)**: the home card shows when `WG.views.quests` exists. Search and the zone page wait for
  `source/contract/quests.md`.
- **Screenshots**: a map canvas that is empty in a `shot.py` capture is a capture artefact under load; the flag
  `--run-all-compositor-stages-before-draw` cures it (asked the lead).
- New viewer options since the first version: `overview`, `preferLayer`, `layer`, `layers`, `locBar`, `onFloor`,
  `pin.z`, `pin.group`, `fitPins(test)`, `mark()`, `floor()`. All in widgets.md.

## After M-D's and Q's files landed

- Stance: `WG.ui.stance` reads M-D's `st` (four counts, or an index on a spawn point). No page of mine says
  "attacks on sight" any more; a mixed camp reads "aggressive 3 of 7".
- Rank: `WG.ui.rank` reads `kind.rank` (event, epic, boss, quest, named; "normal" = none).
- Zone pages use `mobs.zones.tags`, `mobs.events` and `mobs.scripted` (pins with id `x<monster id>`; a zone link
  with `?focus=x<id>` opens on that spot).
- Quests: search, home and the zone page call `WG.quest.ready`, `search`, `forZone`, `zoneSection`, `link`,
  `canTake`, `href`. Q: please keep these names (or tell me). Zone page pins of quest givers have ids `q<n>`.
