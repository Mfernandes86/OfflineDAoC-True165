# data/vendors.js and data/vendoritems.js (V-D)

Built by `python -I source/build_vendors.py` (about 10 seconds). Rules and their `file:line` references are in the
builder's header. Last change: 2026-11-12.

## vendors.js

```
WG.provide("vendors", {
  meta, kinds, currencies, realms, capitals,
  vendors: {cols: [...], rows: [[...], ...]}      // read with WG.rows
})
```

### vendors (1,217 rows) — one per merchant a player can reach and buy from

| col | type | meaning |
|---|---|---|
| `id` | int | 1..n, ordered by region, name, x, y. Stable while the world does not change. `#/vendor/<id>` |
| `name` | string | the name the NPC shows (the NPC template's name when the template replaces mob values) |
| `guild` | string | the line under the name ("Armor Merchant"); may be `""` |
| `realm` | 0..3 | the NPC's realm. 0 = no realm (35 merchants) |
| `zone` | int | ZoneID (always present in zones.js) |
| `x`, `y` | int | zone-local game units, the `/loc` numbers |
| `list` | string | key into `vendoritems.lists` |
| `kind` | string | one word for what the list mostly holds, see `kinds`. Stable masters are always `"stable"` |
| `n` | int | number of goods on the list |
| `currency` | `"coin"` / `"bp"` / `"item"` | what the price is counted in: copper, bounty points, or pieces of `currencyItem` |
| `currencyItem` | string or null | item id of the currency (`DiamondSeal`, `SapphireSeal`, `EmeraldSeal`); its name is in `currencies` |
| `ownRealmOnly` | 0 / 1 | 1 = trades only with its own realm (the Darkness Falls seal merchants) |
| `rep` | object or null | reputation gate: `{faction, factionName, base, maxAggro: 50, need: -50, start, startsRefused, text}` |
| `stable` | 0 / 1 | 1 = a stable master; its routes are in routes.js (`stables[].vendor` = this id) |

Sample:

```
[1, "Adaliae Ruthic", "Merchant", 1, 6, 56550, 21310, "760aa7c9-8e64-409d-9463-c06455d3a7c9", "crafting", 30, "coin", null, 0, null, 0]
[3, "Aelda", "Armor Merchant", 1, 3, 40119, 39786, "AlbChain", "armor", 24, "coin", null, 0, null, 0]
[250, "Aklee Edelmar", "Merchant", 1, 26, 30385, 19634, "AlbBountyMerchant", "jewelry", 12, "bp", null, 0, null, 0]
[1181, "Beelo", "Sapphire Armor Merchant", 3, 249, 34107, 33224, "f6f1b508-...", "armor", 49, "item", "SapphireSeal", 1, null, 0]
[341, "Callisa", "Stable Master", 0, 57, 8189, 49477, "Camp_Clifton", "stable", 1, "coin", null, 0,
 {"faction": 16, "factionName": "Cryptos Mythicos", "base": 100, "maxAggro": 50, "need": -50, "start": -100,
  "startsRefused": true, "text": "Cryptos Mythicos reputation -50 or better (a new character starts at -100: refused)"}, 1]
```

### Other keys

- `kinds`: `[[kind, label], ...]` in filter order: weapons, armor, shields, ammo, instruments, jewelry, potions,
  poisons, dyes, crafting, siege, medallions, housing, stable. `kind` is our summary (the commonest item kind on the
  list), not a server field; the merchant's real description is `guild`.
- `currencies`: `{"DiamondSeal": "Daemon Diamond Seal", ...}`.
- `realms`: `{"0": "None", "1": "Albion", "2": "Midgard", "3": "Hibernia"}`.
- `capitals`: `{"1": {zone: 26, gate: {zone, x, y}, gates: [{zone, x, y}, ...]}, "2": {...}, "3": {...}}`. `zone` is
  the capital city zone (26 Camelot, 120 Jordheim, 209 Tir na Nog). `gates` are the OUTDOOR sides of the city's
  doors (ZonePoint rows whose target is the city), `gate` is the first of them. Camelot and Tir na Nog have 2,
  Jordheim 4 (two doors, two rows each).
- `meta`: counts, what was left out, and three sentences a page can print: `meta.price`, `meta.sell.text`,
  `meta.reputation`.

## vendoritems.js

```
WG.provide("vendoritems", {
  lists: { "<list id>": [[item id, price, page, slot], [item id, price, page, slot, pack], ...], ... },
  names: { "<item id>": "item name", ... }        // every sold item (5,812), so a row is never a bare id
})
```

- `item id` = `ItemTemplate.Id_nb` in the ItemTemplate table's own spelling (the same `id` as items.js).
- `price` = what ONE purchase costs, as the server charges it: `ItemTemplate.Price`, in copper for `coin`
  merchants, in bounty points for `bp`, in pieces of the currency item for `item`.
  `GameMerchant.cs:104-112`: `totalValue = number * template.Price`. Nothing in `GameServer/mode165/` changes a
  merchant price; there is no realm tax. Price 0 is real (stable tickets, teleport medallions, a few quest
  goods): print "free".
- `pack` (5th element, only when it is not 1) = pieces one purchase gives (`PackSize`): arrows, bolts, throwing
  weapons and poisons come 20 at a time. `price` is for the whole pack.
- `page` 0-4, `slot` 0-29: the position in the merchant window. Rows are ordered by page, slot.
- 592 lists, 22,872 rows. Only lists used by a listed merchant are there.

Sample:

```
"AlbChain": [["iron_chain_hauberk", 3420, 0, 0], ["iron_chain_legs", 2060, 0, 1], ["iron_chain_sleeves", 1360, 0, 2], ...]
"03ff8cff-...": [["mithril_balanced_throwing_knives", 12960, 0, 0, 20], ["mithril_weighted_throwing_knives2", 18720, 0, 1, 20], ...]
"AlbBountyMerchant": [["Smooth_Earthen_Stone", 100, 0, 0], ...]          // 100 bounty points
"e7e980e8-...": [["HS_CastleSauvage_AdribardsRetreat", 0, 0, 0], ...]     // a stable master: tickets, free
"names": {"bronze_studded_vest": "bronze studded vest", ...}
```

## Rules a page may state

- **Selling to a merchant** (`GameMerchant.cs:312-328`, `item_sell_ratio` = 50): any merchant pays
  `value x count / pack size x 50 / 100`, rounded down, for anything that can be dropped; nothing otherwise.
  (items.js has the per-item `sell` already.)
- **Reputation** (`GameNPC.cs:2633-2641`, `Faction.cs:184-194`): a merchant with `rep` refuses to open its window
  while your standing with `rep.factionName` is Hostile or worse, i.e. reputation below -50. `startsRefused` true
  means a new character must earn it first (Cryptos Mythicos, Krrzck, The Remnants, Outland Residents: all start at
  -100). Reputation is earned by kills only in True 1.65 (no emissary, no hunt); the three Shrouded Isles
  factions, the monsters to kill and the 250-kill average are in routes.js `factions` (see routes.md). 25
  merchants have a gate.
- **Seal merchants** (27, all in Darkness Falls) trade with their own realm only.

## What is missing, and why

- Left out and counted in `meta.leftOut`: 38 merchants in switched-off regions (tutorial 27, Cathal Valley 165,
  the New Frontiers battlegrounds), 19 in switched-off expansions, 25 whose list holds nothing a player can buy,
  5 with no list at all, 2 second spawn rows of a merchant already listed on the same spot (Ley Manton, Tria
  Ellowis). 601 of the 1,201 lists in `MerchantItem` (28,842 rows) belong to no placed merchant.
- 4 list rows point at an item template that does not exist; the server skips them, so do we.
- 6 merchants use an NPC template id that exists twice with different values (Berandil, Eachann, Kerall Anacha,
  Kyndia, Macilen Polden, Tiaga). The server picks one of the two at random at start-up; we take the first row.
- Not merchants although they carry an item list: hasteners (`GameHastener`), `Adventurer165` guards, 5 plain
  `GameNPC` rows. Keep guard merchants (`GuardMerchant`) have no Mob row. Housing lot markers and consignment
  merchants are not covered.
- Merchants that scripts create at run time (none in True 1.65 as far as the source shows) are not covered.
