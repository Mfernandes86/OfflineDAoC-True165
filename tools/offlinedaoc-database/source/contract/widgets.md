# Shared page parts: exact signatures (P-U)

Files: `app/components/widgets.js` (`WG.ui`), `app/components/map.js` (`WG.Map`), `app/components/me.js`
(the header box). All three load before every view. **Signatures only grow; nothing below will be removed or
change meaning.** If you need something that is not here, tell P-U (agent id in `AGENTS.md`) or write it in your
notes file; do not work around it with a private copy.

Everything that returns an element returns a detached DOM node unless it takes `el` (then it fills `el`, clearing
it first, and returns an api object). "Node or text" means a DOM node, a string, a number, an array of those, or
null. Colours: any CSS colour, or a con name (`"red"`, `"purple"`, ...), or a realm number is accepted where a
`color` is asked for (`WG.ui.color()` resolves it).

---------------------------------------------------------------------------------------------------------------

## 1. Map viewer — `WG.Map`

### `WG.Map.create(el, opts) -> map`

```js
var map = WG.Map.create(el, {
  zone:   12,            // zone id (number) or a zone object from data/zones.js. Required (may be set later).
  pins:   [ ... ],       // see below; default []
  lines:  [ ... ],       // see below; default []
  height: 560,           // px, default 560. The width is the width of el.
  onPin:  function (pin, index, event) {},   // click on a pin. Without it a click follows pin.href.
  onHover: function (pin, index) {},         // optional; pin = null when the cursor leaves a pin
  legend: true,          // true (default): made from the pins. false: none. Or [{kind, color, label}] to state it.
  focus:  3,             // optional: pin index (or pin id) to focus when the data is first shown
  title:  "text",        // optional small caption in the top left corner (default: the zone name)
  showAll: false         // true = draw the labels of all pins when they fit (default: only hover and focus)
});
```

The viewer loads `data/zones.js` itself; you do not have to. It may be created before the data has arrived.

Pin:

```js
{ x: 23456, y: 34567,    // zone-local game units, the /loc numbers (CONTRACT section 6). Required.
  label: "small skeleton",    // first line of the hover label
  sub:   "level 3 to 5 - 14 spawns",   // second line, optional (text; "\n" starts a new line)
  kind:  "camp",         // "camp" | "spawn" | "vendor" | "stable" | "point" | "danger".  Default "point".
  color: "red",          // optional. Default by kind: camp and spawn = the red --pin, vendor = gold,
                         //   stable = teal, danger = orange, point = white.
  n:      14,            // camp: the number written in the pin (omit or 1 = no number)
  radius: 900,           // optional, game units: a translucent circle of the real size around the pin
  href:  "#/mob/123",    // optional: where a click goes when there is no onPin
  id:    "c881",         // optional: any value; map.focus(id) finds the pin by it
  legend: "Red con"      // optional: legend text for this pin's kind + colour (first one seen wins)
}
```

- `camp`: round red pin, constant size on screen (a little larger with more spawns), the count inside.
- `spawn`: small dot (single spawn, or the individual spawn points of a camp).
- `vendor`: gold diamond. `stable`: teal square. `danger`: orange triangle with "!". `point`: white ring.
- Pins are drawn in the order: spawn, camp circles, camp, vendor, stable, danger, point; the focused pin last.

Line (routes, patrol paths):

```js
{ pts: [[x, y], [x, y], ...],   // zone-local game units
  color: "#5ad1c8",             // default teal
  label: "Camelot Hills to Cotswold",   // optional, shown on hover and at the middle of the line when showAll
  dashed: false,                // true = dashed (flying routes)
  width: 2,                     // screen px, default 2
  arrow: false }                // true = arrow head at the last point
```

The returned `map`:

| member | what |
|---|---|
| `map.setData({zone, pins, lines})` | replace what is shown. Leave a key out to keep it. A new `zone` refits the view. |
| `map.focus(i, opts)` | centre on pin `i` (index, or the pin's `id`), zoom in enough to separate it, show its label and a pulse. `opts.zoom === false` keeps the zoom. `map.focus(null)` clears. Scrolls the page so the map is visible unless `opts.scroll === false`. |
| `map.fit()` | show the whole zone. `map.fitPins()` frames just the pins and lines. |
| `map.highlight(i)` | show pin `i`'s label and ring without moving the view (use from a list's row hover). `null` clears. |
| `map.setLayer(name, visible)` | pins and lines may carry `layer: "name"`; this hides or shows a layer. |
| `map.zone()` | the zone object in use (or null while loading). |
| `map.el` | the root element. `map.resize()` after you change the size of the container yourself. |
| `map.destroy()` | remove listeners. Call it from your view's `leave`. |

What the owner gets without any work from the page: drag to pan, wheel to zoom at the cursor, double-click to zoom
in, buttons (+, -, fit, "pins"), keys (+ - 0 and arrows when the map has focus), hover label, a legend, the
coordinates under the cursor as `Zone Name: loc=23456,34567` with a "copy /loc" button (click on the map first to
pin the coordinates), a scale bar, and a truthful fallback for zones with no image (a grid with the zone's real
proportions; or P-D's floor plan where one exists).

Added after the first version (all optional):

- **Pictures of a zone.** P-D gives most zones several pictures (`layers`: "From above", "Game map", "Floor plan",
  plus per-floor maps in 7 dungeons). The viewer shows a switch at the top left and remembers the owner's choice.
  `opts.layer: "game"` forces one for this viewer; `opts.preferLayer: "plan"` only suggests one (used until the owner
  picks a picture himself for that kind of zone: good for a zoomed-in map in a capital, where the floor plan is sharper); `opts.layers: false` hides the switch; `map.layer("plan")` sets
  it, `map.layer()` reads it. Each picture is placed by its own `rect` (P-D's rule), so pins never move.
- **First view.** The viewer opens on `zone.view` (the built part of a dungeon or capital), not on the empty
  65536 square; "Zone" returns there.
- **Region overviews** (stable routes): `WG.Map.create(el, {overview: 1, pins, lines, ...})` shows P-D's overview
  picture of region 1 (`zones.overviews`; also `zone: "r1"` or `zone: WG.Map.overview(1)`). On an overview:
  a pin with `zone: <zone id>` has its `x, y` read as that zone's /loc numbers and is placed for you; a line takes
  `zone` for all its points or a third number per point, `pts: [[x, y, zoneId], ...]`, so it can cross zones;
  without `zone` the numbers are overview-local (region X - `overview.rect[0]`). The cursor readout and Copy /loc
  print the real zone under the cursor and its true loc. Zone squares are outlined and named.
  Helpers: `WG.Map.overviewOf(zoneId)` (null for capitals and dungeons), `WG.Map.toOverview(ov, zoneId, x, y)`,
  `WG.Map.fromOverview(ov, x, y) -> {zone, x, y}`.
- `opts.locBar: false` hides the coordinate bar; `opts.controls: false` hides the buttons.
- `map.mark(x, y)` puts the white cross (the "marked place") at a position; `map.mark(null)` removes it.
- `map.view()` -> `{x, y, scale}` (centre in game units, screen px per unit).
- A line reacts to the cursor only when it has a `label`; `sub` adds a second line. Pins and lines take
  `legend: "text"` to name their legend entry. Clicking a legend entry hides or shows those pins.
- `map.focus(i)`: when the pins carry numeric `id`s, a number is matched against the ids first, then used as index.
- Screenshots: the picture is an `<img>` under the canvas, so `source/shot.py` shows the map as the owner sees it.
- **Dungeon floors.** Seven stacked dungeons have `floors` in zones.js: the viewer shows "All floors | <floor> ..."
  next to the picture switch. A pin may carry `z` (its height, e.g. a camp's `z`): with a floor picked it is shown
  only on its floor (`z0 < z <= z1`); pins without `z` show on every floor. `map.floor(i)` / `map.floor(-1)` sets
  it, `map.floor()` reads it, `opts.onFloor(index, floor)` tells you.
- `pin.group: "text"`: pins with the same group share one legend entry and are hidden together (default: one
  entry per kind + colour). `opts.legend` entries may carry the same `group`. `map.setData({legend: [...]})`.
- `map.fitPins(test)`: with a function `(pin) -> bool` only those pins are framed.

### `WG.Map.multi(el, groups, opts) -> multi`

```js
var multi = WG.Map.multi(el, [
  {zone: 12, pins: [...], lines: [...], label: "optional tab text"},   // one per zone
  {zone: 51, pins: [...]}
], {height: 560, onPin: fn, legend: true, order: "busiest"});   // order: "busiest" (default: most pins first) or "given"
```

One tab per zone ("Camelot Hills 14"), busiest first, one viewer below. A single group shows no tab strip.
`multi.show(zoneId)`, `multi.focus(zoneId, pinIndexOrId)`, `multi.setData(groups)`, `multi.map` (the viewer),
`multi.destroy()`. `onPin(pin, index, event, group)`.

`WG.Map.groupByZone(places)` is a helper: an array of `{zone, x, y, ...pin fields}` -> the `groups` array.

### Small helpers

- `WG.Map.loc(zone, x, y) -> "Camelot Hills: loc=23456,34567"` (zone id or object; needs zones data loaded).
- `WG.Map.project(zone, x, y) -> [px, py]` image pixel of a game position, by P-D's rule (`source/contract/zones.md`).
- `WG.Map.mini(el, {zone, pins, height: 220})`: the same viewer without controls or legend, for cards and tooltips.

---------------------------------------------------------------------------------------------------------------

## 2. Table — `WG.ui.table(el, opts) -> table`

```js
var t = WG.ui.table(el, {
  columns: [
    {key: "name",  label: "Name", render: function (row, value) { return WG.ui.mobLink(row); }},
    {key: "level", label: "Level", num: true, width: 70},
    {key: "pct",   label: "Chance", num: true, render: function (r) { return WG.pct(r.chance); },
                   sort: function (r) { return r.chance; }, title: "Chance per kill"}
  ],
  rows: arrayOfObjects,
  rowHref: function (row) { return WG.href("mob/" + row.id); },   // whole row is a link
  onRow:   function (row, event) {},      // click on a row (if both are given, onRow wins)
  onHover: function (row) {},             // optional; row = null on leave (for map.highlight)
  rowClass: function (row) { return "x"; },   // optional extra class per row
  sortKey: "level", sortDir: "desc",      // initial sort; sortDir "asc" | "desc"
  pageSize: 50,                           // default 50; 0 = no paging (only for short lists)
  empty: "No monsters match.",            // text when rows is empty
  onSort: function (key, dir) {},         // optional, after the user changes the sort (put it in the address)
  stickyHead: true,                       // default true: the header row stays under the page header
  dense: false,                           // true = tighter rows
  count: "monsters"                       // optional noun for the pager: "1 to 50 of 1,234 monsters"
});
```

Column fields: `key` (required, unique), `label`, `render(row, value, rowIndex) -> Node or text` (default: the
value as text; null and undefined show nothing), `sort`: `false` = not sortable, a `function (row) -> number or
string` = sort by that, omitted = sort by `row[key]`; `num: true` = right-aligned, numeric, first click sorts high
to low; `width` (px number or CSS text); `title` (header tooltip); `cls` (class for the cells); `align`
(`"left" | "right" | "center"`). Strings sort case-insensitively, empty values always last.
`first: "desc"` (or `"asc"`) sets the direction of the first click on that header without changing the alignment
(default: high to low for `num` columns, A to Z for the rest).

Returned: `t.setRows(rows)` (keeps the sort, goes to page 1), `t.setColumns(columns)`, `t.sort(key, dir)`,
`t.page(n)` (1-based), `t.refresh()` (re-render after the rows' contents changed), `t.rows()` (the rows in the
current order), `t.state()` -> `{sortKey, sortDir, page, pageSize}`, `t.el`.

Fast with 20,000 rows: only the current page is in the DOM; sorting uses keys computed once per sort.

## 3. Filter bar — `WG.ui.filters(el, defs, onChange) -> filters`

```js
var f = WG.ui.filters(el, [
  {key: "q",     label: "Name",  type: "text",   value: "", placeholder: "part of a name"},
  {key: "realm", label: "Realm", type: "select", value: "",  options: [["", "Any"], [1, "Albion"], [2, "Midgard"], [3, "Hibernia"]]},
  {key: "con",   label: "Con",   type: "chips",  value: "red", options: [{value: "yellow", label: "Yellow", color: "yellow"}, ...]},
  {key: "type",  label: "Type",  type: "multi",  value: [],  options: ["Cloth", "Leather", "Studded"]},
  {key: "level", label: "Level", type: "range",  value: [10, 20], min: 1, max: 50},
  {key: "aggro", label: "Only peaceful", type: "toggle", value: false},
  {key: "lvl",   label: "My level", type: "number", value: 30, min: 1, max: 50}
], function (values, changedKey) { /* values = {q: "", realm: "", con: "red", type: [], level: [10, 20], aggro: false, lvl: 30} */ });
```

- `options`: an array of strings, of `[value, label]` pairs, or of `{value, label, color, title, count}` objects.
- Value by type: `text` string; `select` the option value (`""` = the "any" option; values keep their type);
  `chips` one value, or an array when the def has `multi: true` (a chip clicked again is released unless
  `required: true`); `multi` array of values (a drop-down with check boxes and a search field when long);
  `range` `[min or null, max or null]`; `toggle` boolean; `number` number or null.
- Other def fields: `title` (tooltip), `width` (px), `hidden: true`, `group: "text"` (starts a new labelled group).
- Returned: `f.values()`, `f.set(key, value)` (no onChange call), `f.setOptions(key, options)`, `f.reset()`
  (back to the values the defs were created with; calls onChange), `f.query()` -> a flat object for `WG.setQuery`
  (values equal to the def's `def` field, or empty, are left out), `f.el`.
- Address helpers: `WG.ui.filters.fromQuery(defs, query)` returns the defs with `value` filled from the route's
  query (`multi` and multi-chips = comma separated, `range` = `"10-20"`, `"10-"`, `"-20"`, `toggle` = `"1"`).
  Give each def a `def` (its default) if the default is not "empty", so `f.query()` knows what to leave out.

Typical view:

```js
render: function (el, params, query) {
  var defs = WG.ui.filters.fromQuery(DEFS, query), bar = WG.h("div"), list = WG.h("div");
  WG.append(el, [bar, list]);
  var f = WG.ui.filters(bar, defs, function (v) { WG.setQuery(f.query()); table.setRows(apply(v)); });
  ...
}
```

## 4. Tabs — `WG.ui.tabs(el, tabs, opts) -> tabs`

```js
var tabs = WG.ui.tabs(el, [
  {key: "drops", label: "Drops", count: 12, render: function (panel) { ... }},   // render runs once, on first show
  {key: "map",   label: "Where", content: someNode}
], {active: "drops", onChange: function (key) {}, pref: "mob.tab"});            // pref: remember the last tab
```

`tabs.select(key)`, `tabs.active()`, `tabs.setCount(key, n)`, `tabs.panel(key)` (the panel element), `tabs.el`.
A tab with `count: 0` is shown dimmed. `opts.rerender: true` calls `render` on every show.

## 5. Small parts (all return a detached element)

| call | gives |
|---|---|
| `WG.ui.chip(text, opts)` | a pill. `opts`: `color`, `title`, `href`, `onclick`, `active` (bool), `cls`, `count`. |
| `WG.ui.levelInput(opts)` | number box with - and + for a level. `opts`: `value`, `min` (1), `max` (50), `onChange(n)`, `label`. The element has `.get()` and `.set(n)`. |
| `WG.ui.tooltip(target, content, opts)` | hover tooltip on `target`. `content`: text, a node, or `function (target) -> Node or text` (called on first show). `opts.delay` ms (default 120). `WG.ui.tooltip.show(content, x, y)` / `.hide()` for manual use. |
| `WG.ui.itemLink(item, opts)` | `<a href="#/item/<id>">` with the name, realm tint and (when I-U's `WG.item.tooltip` exists) the item tooltip. `item` = an item object (`{id, name, ...}`) or an id (name looked up in `WG.data.items` when loaded). `opts.text` overrides the text; `opts.icon: false` hides the icon. |
| `WG.ui.mobLink(kind, opts)` | `<a href="#/mob/<id>">` name. `kind` = a row of mobs.js kinds (`{id, name, lmin, lmax}`; `levelMin` / `levelMax` are read too) or an id. `opts.level: true` adds the con-coloured level text. |
| `WG.ui.zoneLink(zone, opts)` | `<a href="#/zones/<id>">` zone name, realm tinted. `zone` = id or zone object. `opts.loc: [x, y]` adds `?x=&y=` so the zone page marks the place; `opts.query: {kind: <kind id>}` opens the zone showing only that monster, `{focus: "v<merchant id>"}` (or `"s<stable id>"`, `"c<camp id>"`) focuses that pin. |
| `WG.ui.vendorLink(v, opts)` | `<a href="#/vendor/<id>">` name. `v` = `{id, name}` or an id. |
| `WG.ui.level(min, max, myLevel)` | `<span>` "20 to 26" (or "20"), each end in its con colour for `myLevel` (default `WG.me.level`). Works (plain text) before `con.js` exists. |
| `WG.ui.conClass(mobLevel, myLevel)` | `"con-red"` etc., or `""` when `WG.con` is not there. |
| `WG.ui.color(c)` | CSS colour for a con name, realm number or CSS colour. |
| `WG.ui.realm(realm, opts)` | `<span class="realm-1">Albion</span>`; `opts.short` = "Alb". Realm 0 = "Any" dimmed. |
| `WG.ui.money(copper)` | coloured coins ("12g 34s 56c"). |
| `WG.ui.page(el, {title, sub, crumbs: [[text, href], ...], actions})` | the standard page head (h1, dim sub line, breadcrumb). Returns the head element. |
| `WG.ui.section(title, opts, ...children)` | a titled card. `opts` (optional object): `count`, `collapsed`, `actions`, `cls`. |
| `WG.ui.kv([[label, value], ...])` | a compact two-column fact list (value = Node or text; null rows are skipped). |
| `WG.ui.bar(share, opts)` | a small horizontal bar for a 0..1 share (drop chance, aggro share). `opts`: `color`, `text`, `width`. |
| `WG.ui.histogram(values, opts)` | small column chart for a level spread. `values`: `[[level, count], ...]` or `{level: count}`. `opts`: `myLevel` (con colours the columns), `height`. |
| `WG.ui.loading(el, text)` | puts a "Loading ..." line in `el` (returns `el`). `WG.ui.empty(text)` returns the empty-state block. |
| `WG.ui.note(text, kind)` | an inline note. `kind`: `"info"` (default), `"warn"`, `"danger"`, `"estimate"` (for numbers that are estimates). |
| `WG.ui.copy(text, buttonEl)` | copies to the clipboard; flashes "copied" on `buttonEl` if given. |
| `WG.ui.debounce(fn, ms)` | debounced function. |
| `WG.ui.num(n)` | "12,345". |
| `WG.ui.stance(o, opts)` | the monster's stance in the game's own words (the owner asked for them): `<span class="wg-stance aggressive">aggressive</span>`, hostile, neutral or friendly. `o` = anything of M-D's that carries `st` (a kind, a camp, a zone row, a `near` entry: the four counts; a spawn point's index 0..3), the counts array itself, a word, or the older `{ag, n or spawns, faction}`. If any member is aggressive the word is "aggressive", with the share when it is not all of them ("aggressive 3 of 7"; `opts.count: false` leaves it out). Empty when nothing is known (script-made monsters). `WG.ui.stanceWord(o)` gives the bare word, `WG.ui.stanceText(o)` the text ("3 of 7 aggressive"), `WG.ui.stanceInfo(o)` `{word, n, of}`. |
| `WG.ui.rank(kind)` / `WG.ui.rankBadge(kind, opts)` | `"event"`, `"epic"`, `"boss"`, `"quest"`, `"named"` or `""` (M-D's `rank`; the flag bits when it is missing) and a small badge for event / epic / boss (quest and named too with `opts.all`); the tooltip is `rankWhy`. `WG.ui.rankLabel(rank)` gives the text. |
| `WG.ui.searchBox(opts)` | the search box with its drop-down list (`app/views/search.js`). `opts`: `large`, `placeholder`, `value`, `autofocus`, `onQuery(text)` (then no list: the box only reports the text). `WG.search.find(type, text, limit)` -> `{list, total}` for `type` "mob", "item", "vendor", "zone" after `WG.search.start()`. |
| `WG.ui.meBox(opts)` | a large copy of the "my character" box for a page. |
| `WG.ui.classes()` / `WG.ui.className(id)` | the class list from itemmeta (`[{id, name, realm}]`), empty until that file is loaded. |
| `WG.ui.icon(item, size)` | item icon element when I-D's icons can be drawn (else an empty box of that size). |

## 6. My character — `app/components/me.js`

The header box edits `WG.me` (`{level, realm, cls}`; core.js) and calls `WG.setMe`, so `WG.onMe(fn)` listeners
fire. Class names come from `data/itemmeta.js` when it exists. `WG.ui.meBox(opts)` returns a second, larger copy
of the box for a page (`opts.title`); both stay in sync. Views that depend on the character should take the
level from the address when it is there (`query.level`) and from `WG.me.level` otherwise.

## 7. CSS classes you can use (`app/app.css`)

Layout: `wg-card`, `wg-row`, `wg-grow`, `wg-cols-2` / `wg-cols-3` (equal columns that fold at narrow widths),
`wg-split` (a wide left part and a 420px right part that folds under at 1100), `wg-stack` (vertical gap).
Text: `wg-dim`, `wg-warn`, `wg-danger`, `wg-num` (tabular numbers, right aligned), `wg-small`, `wg-nowrap`.
Colours: `realm-1..3`, `con-grey ... con-purple` (text), `bg-con-red` etc. (a filled badge).
Parts: `wg-chip`, `wg-badge`, `wg-note` (+ `warn`, `danger`, `estimate`), `wg-kv`, `wg-btn` (`primary`, `small`,
`ghost`), `wg-list` (plain link list with hover rows), `wg-head` (page head).
Need a rule that is not here? Send it to P-U; it goes in a marked block at the end of `app.css`.
