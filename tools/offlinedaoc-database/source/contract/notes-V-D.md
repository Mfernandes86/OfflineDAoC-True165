# Notes from V-D (merchants and stable routes data) — living file

Last update: 2026-11-12.

## What is published

- `data/vendors.js` (1,217 merchants), `data/vendoritems.js` (592 lists, 22,872 rows, 5,812 distinct items),
  `data/routes.js` (95 stables, 189 routes, 0 broken, 3 factions, 169 gates, 8 teleporters).
- Shapes: `source/contract/vendors.md`, `source/contract/routes.md`. Rules with `file:line`: the headers of
  `source/build_vendors.py` and `source/build_routes.py`.
- Build order: `build_vendors.py` then `build_routes.py`. `build_routes.py` imports `build_vendors.py` (code, not
  the data file) so the merchant ids agree. Both read `data/zones.js` only if it is there, to assert that every
  zone they use is one of P-D's; neither needs it.

## Decisions

- A merchant is what the server creates, not what `Mob.ClassType` says: guild script first, then the NPC
  template's class, then the row's class (`world/Region.cs:745-772`). 10 rows are merchants only through their
  template (9 `GameNPC` rows such as Marlin Thuler and Rhian, and Dalla); Gorawen stays a Spellcrafters Master
  because of her guild, and Boudron Fletcher (a `GameMerchant` row) is a Blacksmith through his template.
- The price is `ItemTemplate.Price` for one purchase of `PackSize` pieces. Nothing in `mode165/` touches it.
- **Stable rides cost nothing** in this world: every ticket template has Price 0 (the housing tickets were added
  with Price 0 too). Not a code rule: if the owner ever prices the tickets the file follows.
- Reputation is the stable master's rule (`GameNPC.cs:2633`): no realm + faction + no PEACE flag. It also applies
  to 19 ordinary merchants (vendors.js `rep`). Six stable masters ask; the list equals what the emissary code
  says in `FactionEmissaryRuntime.cs:182-187`.
- **No emissaries in True 1.65** (lane Q found it, I checked it: `Classic165Mode.cs:56-60`,
  `GameEventMgr.cs:115-126`, `classic165_mode = True`). My first files described the emissary hunt as the way to
  earn reputation: that was wrong for this game and is corrected. Reputation is earned by kills only (20% chance of
  +1 a kill of an enemy faction's monster, about 250 kills from -100 to -50). `emissary` is null everywhere;
  `factions[id].emissaryOff`, `killMobs`, `killsNeeded` are new. The builder checks the mode on every run.
- `to` of a route = the nearest stable master to the last path point (server has no such notion); `gap` says how
  far. The builder asserts that no other town's stable master is less than twice as far.
- Left out and counted: disabled regions and expansions, merchants with nothing buyable, duplicate spawn rows.

## For other lanes

- **V-U**: everything you asked for is there under the names in the two notes. Extra: `stables[].town`, `land`,
  `area`; `routes[].gap`, `end`, `botSeconds`; `factions` (emissary, hunt list, what kills help or hurt);
  `gates`; `teleporters`; `vendors.capitals[realm].gates` (all doors, `gate` = the first). `broken` is empty in
  this world. A path stretch can start or end slightly outside its zone (the point across the border is repeated).
- **I-D**: resolved. items.js holds all 5,812 sold items with the vendor bit, none extra (checked after I-D's fix);
  `names` in vendoritems.js stays as a fallback.
- **D-V**: `verify_prices.py` passes on these files (run 2026-11-12). Field names are final.
- **P-D**: all zones used are in zones.js (asserted when the file is present).

## Server observations worth the owner's eye (not changed, not in the data)

- A faction stable master that refuses a player tells them to "speak with" its emissary
  (`FactionEmissaryRuntime.cs:157-159`), an NPC that True 1.65 never spawns. The only way to open the 9 gated
  flying routes is about 250 kills of the faction's enemies.
- Withdrawn: I had noted that the player travel helper (`MobNavigationCommands.cs:705-720`) buys tickets without
  the reputation check. The code does, but no player can reach it: its command class has no command registration
  and `/mobs` is GM-only in True 1.65. Not an issue in play.
- The bots' planner likewise treats no-realm stable masters as usable and has no reputation check
  (`AutonomousStableRoutePlanner.cs:765-767`).
- `HS_Ludlow_CastleSauvage` stops 9,686 units south of the Castle Sauvage stable master (its last point is
  584252, 488597; the other three routes into Castle Sauvage end beside him).
- Six stable masters sell nothing: Ingrit, Valda, Vlasta (Midgard housing), Erie, Kohar (Hibernia housing), Aethic
  (Tir na mBeo, no list).
- Six merchants use an NPC template id that exists twice with different rows (Berandil, Eachann, Kerall Anacha,
  Kyndia, Macilen Polden, Tiaga): which one loads is random at server start.
- Ley Manton and Tria Ellowis each have two spawn rows on the same spot (two identical merchants stand there).

## Open problems

- None blocking. Not covered: old-frontier and battleground teleporters, medallion travel, housing lot markers,
  consignment merchants, the Darkness Falls door rule.
