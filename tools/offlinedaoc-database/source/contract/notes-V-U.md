# Notes from V-U (merchant and stable-route pages) — final, 2026-11-12

## What is built

| File | What |
|---|---|
| `app/lib/routes.js` | `WG.routes`: the stable-route graph and the search. Pure functions, no DOM. |
| `app/views/vendors.js` | `#/vendors`: "find something to buy" and "browse merchants". Also `WG.vu`, the adapters my three pages share. |
| `app/views/vendor.js` | `#/vendor/<id>`: one merchant. |
| `app/views/routes.js` | `#/routes`: planner, network map per land, one stable's tickets, the rides on the zone maps, reputation. |

## Addresses (everything a page shows is in the address; Back steps through it)

- `#/vendors?cat=gear|armor|weapon|shield|instrument|jewelry|other|all&type=<object type>&slot=<slot>&level=<min>-<max>&realm=<1-3 or ->&q=<text>&mine=1&item=<item id>&sort=<column>.<asc|desc>`
  - `level` left out = my level and the five below it (follows the character box). `realm` left out = my realm.
  - `item=<id>` alone works as a link from any page: the side panel shows every merchant that sells it.
- `#/vendors?mode=merchants&realm=&zone=<zone id>&kind=<kind>&q=<text>&vendor=<id>`
- `#/vendor/<id>?q=<text>&cat=<kind>&sort=`
- `#/routes?from=<stable id>&to=<stable id>&norep=1&notele=1&pick=tickets|price|time&net=<region id>&stable=<stable id>&leg=<ticket item id>&fac=<faction id>`
  - `from` / `to` / `norep` / `notele` changes are history steps; `pick`, `net`, `stable`, `leg`, `fac` replace the address in place.
  - `fac=<faction id>` opens that faction's "what to kill" list and map in the reputation section (16, 89, 172).
  - `#/routes?stable=<id>` is the "stable page" other pages can link to. Stable ids are V-D's and change when the
    world changes: do not hard-code them.

## `WG.routes` (for anyone who wants a route in another page)

```js
var g = WG.routes.build(WG.data.routes);                 // once; {stables, byId, legs, out, inn, walks, teleports}
WG.routes.options(g, fromId, toId, {noRep: false, tele: true})
  // -> {ok: true, options: [{labels: ["tickets", "time"], legs, tickets, teleports, steps, price, seconds, secondsKnown, walk, rep: [legs], kinds}]}
  // -> {ok: false, why: {code: "norep" | "notele" | "nowalk" | "nosale" | "noarrival" | "separate" | "oneway" | "same" | "unknown", ...}}
WG.routes.plan(g, fromId, toId, {by: "tickets" | "price" | "time", noRep, tele, walk})
WG.routes.reach(g, fromId, opts)      // Map stable id -> the fewest-steps answer
WG.routes.hasReturn(g, leg)           // false = a one-way ticket (town-aware)
WG.routes.time(seconds)               // "4 min 10 s"
```

A leg: `{from, to, kind: "horse" | "flying" | "walk" | "teleport", walk, tele, price, seconds, length, gap, rep, path, name, ticket, ...}`.

## Decisions (what is the server's and what is mine)

- **The server's, through V-D's data:** which stable master sells which ticket, where each ride runs (the path), how
  long it takes (`seconds`, plus `meta.startDelay` 4 s per ticket, which I add), that every ride is free, which
  stable masters ask for reputation and how it is earned, the teleporters and their four towns, the merchants'
  lists and prices, pack sizes, currencies.
- **Mine, and said so on the page:**
  - *Order of answers.* "Fewest tickets" = fewest steps (a ticket or a teleporter is one step), then cheapest, then
    fastest. "Cheapest" is hidden while every ticket is free. "Fastest" only when every leg has a time.
  - *Walk links.* Stable masters with the same `town` in routes.js (four pairs) are joined both ways; a walk is not a
    step. The page prints the straight-line distance.
  - *Teleporter links.* A stable master within 3,000 units (`WG.routes.TELE_MAX`) of a teleporter NPC is joined, one
    way, to stable masters within 3,000 units of each place it sends you; the NPC's own town is skipped (the server
    hides it). 29 links in this world (Avalon 12, Aegir 14, HyBrasil 3). Switch: "Use the Shrouded Isles
    teleporters". The step text gives the NPC's /loc, where it puts you down and the distance on foot.
  - *"With one walk" hint* when no ticket joins two stables of one land (Albion's 17 stables are three separate
    ticket networks: 10, 5 and 2): ride to P, walk to Q, ride on. P and Q are chosen for the least (ride time + 5 x
    walking time over the straight line), so the walk stays short. The page says it is a straight line.
  - *Different lands:* the hint lists up to three of the server's gates (routes.js `gates`) between the two regions.
  - *"Nearest capital first"* (item panel on `#/vendors`): my realm first, the capital city zone first, then the
    straight-line distance from the capital's first gate (`vendors.capitals[realm].gate`) for merchants in the same
    region, then other regions by zone name.
  - *The realm of a merchant* for the Realm filter: its own realm, or for realm 0 the realm of the zone it stands in.
- A ride's `gap` above 2,500 units is printed ("The ride stops about 9,700 units from Uliam").
- **Reputation is earned by kills only in True 1.65** (V-D, found by lane Q, late on 2026-11-12: the emissaries and
  their hunt are switched off). The page prints V-D's `reputation.how` as it is, lists `killsHurt`, and for each
  faction opens `factions[id].killMobs` as a table (nearest my level first, con-coloured levels, links to the
  monster pages once `mobs.js` is in memory) beside the zone maps with one pin per monster and zone. It states the
  chance and the average (`killChance`, `killsNeeded`, `killPoints`) and that it is not established whether a grey
  monster counts. The emissary and hunt blocks are still in the code and appear only if the data ever fills them.
- Merchant maps that open zoomed in on one pin pass `preferLayer: "plan"` (P-U's option): the floor plan in the
  three capitals, no change anywhere else (only capitals and dungeons have a plan, and dungeons open on it anyway).

## What I use from other lanes

- V-D: `vendors.js` (`vendors`, `kinds`, `currencies`, `capitals`, `meta.sell.text`, `meta.reputation`),
  `vendoritems.js` (`lists`, `names`), `routes.js` (`stables`, `routes`, `broken`, `factions` with `killMobs`,
  `gates`, `teleporters`, `meta.startDelay`, `notes`).
- M-D: `mobs.js` `kinds` (`key`, `id`), loaded only when a "what to kill" list is opened, for the monster links.
- I-D / I-U: `items.js` rows through `WG.item.norm`; `WG.item.kind / typeSlot / statText / bonusText / typeName /
  slotName / usableBy / cls / ready`. `items.js` names the object type `obj`; `WG.vu.items()` copies it to `type`
  when `WG.item.norm` has not.
- P-D: `zones.js` (`zones` with `region`, `ox`, `oy`, `realm`, `kind`; `regions`; `overviews`).
- P-U: `WG.Map.create` (zone maps and `zone: "r<region>"` overviews with pins that carry `zone` and lines with
  `[x, y, zone]` points), `WG.Map.multi`, `WG.ui.table / filters / page / realm / money / zoneLink / itemLink /
  icon / copy / num / loading`.

## Styles

My pages inject two small style blocks (`#vu-css` in vendors.js, `#rt-css` in routes.js; classes `vu-*` and
`rt-*`), the way I-U does for item text. They use only the tokens of `app.css`. P-U may move them into `app.css`.

## Open points for others

- P-U: the negative arc radius I reported in the focus pulse (`map.js`, headless runs only) is fixed by P-U.
- No sample data was ever written to `data/`. My development sample is gone; the generator that made it is
  `source/scratch/V-U/make_sample.py` (writes to the scratch folder only).
