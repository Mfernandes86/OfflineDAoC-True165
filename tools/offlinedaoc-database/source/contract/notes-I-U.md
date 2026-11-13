# Notes from I-U (item pages) — living file

Last update: 2026-11-12 (pages run on the real data files).

## What is built

| File | What |
|---|---|
| `app/lib/itemtext.js` | `WG.item.*`: names, formatting, the game-style card, "can my class use it", and the in-memory item database (`WG.item.db()`) that joins items, drops, monsters, merchants and zones |
| `app/lib/upgrade.js` | `WG.upgrade.*`: the ranking (the server's own gear value, ported) |
| `app/views/items.js` | `#/items`: the item and upgrade finder |
| `app/views/item.js` | `#/item/<id>`: one item, who drops it (camps on the map), who sells it |

## `WG.item` for the other page lanes

Call `WG.item.ready()` (loads `itemmeta`) before using names. Every function takes a row of `data/items.js` as
`WG.rows` gives it; `WG.item.norm(item)` (idempotent, called for you by the functions below) adds `item.type`
(= `obj`, the Object_Type) and a lowercased name `item._lc`. Signatures only grow.

| Call | Gives |
|---|---|
| `WG.item.ready()` | Promise; names are available after it |
| `WG.item.typeName(objType)`, `slotName(slot)`, `dmgName(d)`, `realmName(r)`, `className(id)` | names (itemmeta, with the server enums as fallback). `slotName` reads 34 as 33 and 36 as 35 (Bracer, Ring) |
| `WG.item.typeSlot(item)` | one line: "Chain, feet", "Slash, one-handed", "Medium shield", "Ring", "Arrow" |
| `WG.item.statText(item)` | "AF 62, abs 34%" / "16.5 dps, 3.7 spd" / "" |
| `WG.item.bonusName(p)`, `bonusLine(p, v)`, `bonusShort(p, v)` | "Strength", "Strength: 12 pts" / "Slash resist: 4%" / "Parry: 3 lvls", "Str 12" / "Slash 4%" / "Parry +3" |
| `WG.item.bonusText(item or [[p, v], ...], {short, sep})` | all bonuses as one string (short form by default) |
| `WG.item.bonusCat(p)` | `stat`, `hits`, `power`, `resist`, `skill`, `focus`, `af`, `other` (eProperty id ranges) |
| `WG.item.effects(item)` | `[{kind: "Proc" or "Charge", name, text, chance, charges, max}]` from `procs` / `charges` and `items.spells` |
| `WG.item.kind(objType)`, `kindOf(item)`, `isGear(item)` | `weapon / armor / shield / instrument / jewelry / other`; `kindOf` uses I-D's `cat` (a potion is `other`, a cloak is `jewelry`); `isGear` = worn or wielded (slot 10 to 37 and not `other`) |
| `WG.item.usableBy(item, classId, level)` | realm, the item's class list, armor / weapon ability with the level it arrives, shield size (`items.md` section 3). `level` optional |
| `WG.item.classCanUseType(classId, objType, level, shieldSize)`, `classTypes(classId, level)`, `classes(realm)`, `cls(id)` | class tables from itemmeta |
| `WG.item.icon(item, size)` | icon element or `null` (32 px sheet; the 64 px sheet above 32 px) |
| `WG.item.link(item)`, `href(item or id)` | link element, `#/item/<id>` |
| `WG.item.tooltip(item, {link, full})` | the game-style card (`div.it-card`; put it in a `wg-card` on a page; `WG.ui.itemLink` already shows it on hover) |
| `WG.item.killsFor(chance, want)` | kills for `want` (0.5, 0.9) chance of at least one drop: arithmetic |
| `WG.item.priceText(row)`, `priceEl(row)` | "4s 50c" / "20 Daemon Emerald Seals" / "350 bounty points" / "free" for a row of `db.vendors()` |
| `WG.item.db()` | Promise of the joined database, see below |

`WG.item.db()` loads `items` (needed) and `itemmeta`, `drops`, `mobs`, `vendors`, `vendoritems`, `zones` (each
optional: a missing file is listed in `db.missing`, the pages print a note and leave that part empty).

- `db.items`, `db.get(id)` (exact, then without case).
- `db.drops(id, realm)` -> `[{key, kind, zones, near, chance, base, n, flags, always, quest, never, locked, unspawned,
  quests}]`. `chance` is for a killer of `realm` (uses the row's `byRealm`; realm 0 = the natural killer of
  `drops.md`). `kind` is the row of `mobs.js` (with `levelMin` / `levelMax` / `zoneIds` added as aliases of
  `lmin` / `lmax` / `zones[..][0]`), `null` for a name in `drops.unspawned`. Order: can fall, stands in the world,
  stands in a zone of `realm` (or of no realm), then highest chance.
  Each entry has `where`, from the 4th element of the `byItem` row (drops.md): `monster` (a world spawn kind of
  mobs.js), `script` (mark 2: only a script creates it; `kind` is then its row of `mobs.scripted`, with
  `kind.scripted = true`, a page at `#/mob/<id>`, `kind.where` spots and `kind.how`, no camps: 19 names, 111 rows),
  `npc` (stands in the world but mobs.js does not list it as a monster to hunt, `kind` null: 15 names, 84 rows),
  `absent` (mark 1: a loot table nothing in this world uses, `kind` null: 31 rows on catalogue items; never offered
  as "best way", shown dim on the item page). `WG.item.whereText(where, kind)` gives the sentence.
  `db.kind(key)` and `db.kindById(id)` answer for script-made kinds too.
- `db.otd(id)` one-time drops, `db.questDrops(id)` quest hand-overs (`q` of drops.js).
- `db.vendors(id, realm)` -> `[{v, price, pack, currency, own, usable}]`: merchants of `realm` first, coin before
  seals and bounty points, cheapest first; `usable` is false only for an `ownRealmOnly` merchant of another realm.
- `db.best(item, realm)` -> `{drop, drops, vendor, vendors, chance}`: what the finder prints as "best way to get it".
- `db.kind(key)`, `db.kindById(id)`, `db.vendor(id)`, `db.zone(id)`, `db.zoneName(id)`.

## The ranking (`WG.upgrade`) — the server's own gear value, ported

The server has a gear value: the one gamebots use to decide what to wear. The finder ranks by it and says so in
one sentence above the list (`WG.upgrade.explain`). `WG.upgrade.value(item, {level, cls})` is the number,
`WG.upgrade.detail(item, ctx)` the same with its parts (shown on the item page).

- `AutonomousBotEconomy.EquipmentValue` (`GameServer/bots/autonomous/AutonomousBotEconomy.cs:603-620`):
  `10 x max(1, item level) + max(1, quality) + 2 x DPS_AF + 3 x every bonus point (the Bonus % field included)
  + 2 x absorb (armor only)`; a melee weapon with no working damage fields is worth 0
  (`bots/BotWeaponStats.cs:13-29`). `bot_weighted_gear_ranking` is off by default (`:627`), so the flat 3 runs.
- `BotGearValue165.Adjustment` (`bots/autonomous/BotGearValue165.cs:120-167`), applied when a level is given:
  item level counts only up to my level (10 per level over is taken off); DPS only up to `12 + 3 x level`
  (tenths), AF up to `level` (cloth) or `2 x level` (2 per point over is taken off); every typed bonus counts
  `class weight x points up to its cap` instead of `3 x points`.
  Weights (`:34-73`): primary stat and mana stat 4, secondary stat and Constitution 3, tertiary 2, other stats 1;
  Acuity 4 for a caster else 0; hits 1; power 3 for a caster else 0; AF bonus 2; resists 3; a skill line the class
  trains 6, another skill 0; all magic skills 6 for a caster else 0; all melee / dual wield / archery skills 4;
  anything else 3. Caps (`:92-107`): stats and acuity `floor(1.5 x level)`, hits `4 x level`, resists
  `level / 2 + 1`, skills `level / 5 + 1`, power `level / 2 + 1`.
- `WastedFocus` (`AutonomousBotEconomy.cs:836-849`): focus points for a spell line the class does not have are
  taken off (3 per point). Class lines come from `itemmeta.classes[].focus`.
- Without a class (or for the 15 base classes, which have no stat roles) the weights are the server's fall-back.

Differences from the server, all stated on the pages:
1. The bot subtracts what its other worn pieces already give before it applies a cap; this tool does not know the
   rest of the owner's gear, so each item is capped on its own.
2. "The class has the skill line" = the class can train it (the bot checks the levels it trained).
3. `WeaponSpecBonus` (the bot's trained weapon spec, `AutonomousBotEconomy.cs:789`) is not included.
4. Shields: the server adds `2 x DPS_AF` for a shield as for a weapon. A shield's DPS_AF is the damage of its
   shield styles and the stored numbers are uneven (a level 2 bronze shield carries 16.5; I-D's warning), so mixed
   lists opened with low shields. **This tool leaves a shield's DPS out of the value**; shields rank by level,
   quality and bonuses. The item page shows the line with 0 points and says why; the sentence above the list says
   "not for shields".

**Stat focus** (the chips "Rank by these bonuses"): not a server rule and the page says so. Rank = sum of the
picked bonuses on the item, each point weighted as the server's Realm Exchange weighs it (`ItemValue165.cs:36-58`:
skill 5, resist and power 2, stat / focus / AF 1, hits 1/4); ties by gear value. With a class picked, "Skills"
counts only the lines that class trains.

Hand-checked: Legions Scaled Breastplate for a level 30 Armsman = 300 + 90 + 124 + 68 + 45 - 4 (AF 62 over the
cap 60) + 12 Str x 4 + 12 Con x 3 + 36 hits x 1 = 743 (the page shows the same lines).

## Decisions on the pages

- **Realm of a drop chance**: the finder uses the realm filter; the item page uses the item's realm when it has
  one, else the header character's realm. Monsters standing in a zone of that realm (or of no realm) come first.
- **Slots**: "Right hand" lists slot 10 and slot 11 weapons (a slot 11 weapon can be held in the right hand);
  "Left hand" lists shields, plus slot 11 weapons only for a class with `leftHand` (`items.md` rule 7).
- **Level requirement**: the server uses `LevelRequirement` only for procs, charges and potions
  (`GameLiving.cs:866, 902`, `GamePlayer.cs:7315`), not for wearing. The card says "Works from character level N"
  under a proc or charge; the finder does not hide such items.
- **Item level range** starts at my level - 5 to my level + 2 and applies to gear; for things that are not gear it
  applies only when the owner changed it (loot is level 0).
- Quest step items (`src == 64`) are left out of the finder unless a name is typed.
- **"Grey to you"** on the item page: `WG.con.color(my level, monster's highest level) == grey`; a grey kill gives
  no loot (`serverrules/AbstractServerRules.cs:1218-1219`).
- Kills for 50% / 90%: `ceil(ln(1 - want) / ln(1 - chance))`, labelled as arithmetic.

- **Quests card** on the item page: drawn by the quests lane (`WG.quest.itemSection(el, item.id)`, quests.md
  section 1): quests that reward the item, kill steps that hand it over with their chance, quests that need it.
  It sits under the map, or at the top of the right column when the item has no drop and no merchant. The short
  quest lines in "How to get it" (from `items.quests`) stay as the summary.

## Address parameters of `#/items`

`m=up` (upgrade mode), `cls`, `lvl`, `realm` (left out = the header character), `slot` (Item_Type; 35 = both
rings, 33 = both bracers), `type` (Object_Type number, or `armor`, `weapon`, `shield`, `instrument`, `jewelry`,
`other`), `il=25-32` (item level; `il=-` any), `src=d,v,q,c`, `q` (name), `focus=str,con,...`
(`str con dex qui int pie emp cha hits pow res skill af`), `cur=<item id>` (what I wear now, upgrade mode),
`sort` (`value`, `level`, `source`, `name`, `stats`, `quality`, `type`), `dir`.
Other pages can link straight in, e.g. `WG.href("items", {m: "up", slot: 25, cur: item.id})`.

## Styles

`itemtext.js` and `item.js` add two small `<style>` blocks (`#it-css`, `#it-css2`; classes `it-*`, about 15
rules: the card, the icon, the selected table row). Everything else uses `app.css`. P-U may move them into
`app.css` (then delete the two blocks); nothing else depends on where they live.

## Open problems

- none
