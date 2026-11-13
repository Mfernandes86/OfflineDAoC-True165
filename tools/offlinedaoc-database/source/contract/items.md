# data/items.js and data/itemmeta.js (lane I-D) — exact shapes

Builder: `python -I source/build_items.py` (about 6 s, no arguments). Writes `data/items.js` (4.7 MB),
`data/itemmeta.js` (36 KB), `icons/items.png` (0.8 MB) and `icons/items64.jpg` (1.0 MB).
Everything below is what the files hold right now. Last change: 2026-11-12, fourth build (bit 128 follows
`mobs.js`: 19 script-made monsters, 103 items; the two Albion scripted quests Wolf Pelt Cloak and Immediate
Resolution are read too; item count 22,427; two builds of the same world now give the same file)
(added since the first note: `src` bit 128, `fl` bits 128 and 256, `fl` 64 narrowed, `items.otherNames`,
`items.scriptMonsters`, `items.comparedWith`, `itemmeta.objSkill`, the 64 px icon sheet; no column was renamed or moved).

## 1. Which items are in items.js

22,427 of the 40,717 item templates in the True 1.65 world: every template with at least one real source.
18,290 templates have no source and are left out (inherited database rows nothing drops, sells, crafts or hands
out). Counts are in `items.left` and `items.counts`.

`src` is a bit set (also in `items.srcBits`):

| bit | name | items | meaning |
|---|---|---|---|
| 1 | drop | 7,254 | on a loot table a monster standing in a loaded region rolls (rates: D-D's `drops.js`; the item set is the same as `drops.byItem` without its "unspawned" rows, checked at build time) |
| 2 | otd | 76 | a one-time drop (LootOTD) of such a monster |
| 4 | vendor | 5,812 | on the list of a merchant standing in a loaded region (who and price: V-D's `vendoritems.js`; same item set, checked at build time) |
| 8 | quest | 1,527 | a quest reward: data quests, classic-quests.json choices / trades / side turn-ins / class versions, and what the scripted level 50 epics hand out |
| 16 | craft | 8,272 | the product of a recipe |
| 32 | start | 18 | starting equipment |
| 64 | qitem | 1,169 | a quest STEP item (handed out, collected, or dropped only while on the quest); not a reward |
| 128 | script | 103 | on the loot table of a monster that is NOT a database spawn but that a server script creates in code (19 boss encounters, names in `items.scriptMonsters`: Apocalypse and the four horsemen, Beliathan, Host, the corrupt primals, Elder Icelord Suttung, ...). Which monsters those are is M-D's finding (`mobs.js`, table `scripted`), read at build time; in `drops.js` they are kinds with `s: 1` and their `byItem` entries have 2 as 4th element (the same 103 items, checked at build time). Show as "boss encounter, not a fixed spawn" |

- `src == 64` (934 items, 18 of them wearable): only a quest step item. Keep out of an upgrade finder.
- `src == 128` (90 items): only from a script-summoned boss.
- 13 more items are named only by `drops.unspawned` monsters that nothing creates (Galand, Brannoc, Jailor Vifil,
  Blight, ...: 15 loot tables of monsters that are not in the world). They are NOT in items.js: when inverting `drops.byItem`,
  skip ids that `items.js` does not have.

## 2. items.js

```
WG.provide("items", {
  cols: [...30 names...], rows: [[...], ...],      // read with WG.rows(d.items); sorted by name
  spells: {"<spell id>": [name, type, text, damage, dmgType, value, duration, frequency, radius, range, target, level]},
  spellCols: [the 12 names above],
  craft:  {"<item id>": [skill id, skill level, [[ingredient item id, count], ...]]},      // 8,272 items
  otherNames: {"<ingredient id>": name},   // 33 ingredients that are not catalogue items (nothing provides them)
  quests: {"<item id>": [[quest name, quest min level, quest giver, role], ...]},          // 2,692 items, max 12 each
  descs:  {"<item id>": "description text"},                                               // 711 items
  srcBits, flBits, counts, left, icons, scriptMonsters, comparedWith, built
})
```

| col | type | from | meaning, unit |
|---|---|---|---|
| `id` | string | Id_nb | the item key every lane uses (the database's own spelling; compare case-insensitively) |
| `name` | string | Name | |
| `level` | int | Level | item level (0 to 51; a few boss drops above) |
| `realm` | int | Realm | 0 any, 1 Albion, 2 Midgard, 3 Hibernia. A realm item can only be used by that realm |
| `cat` | string | derived | `weapon` (obj 1-28), `armor` (31-38), `shield` (42), `instrument` (45), `jewelry` (obj 41, slot 24/29/32/33/34/35/36/37), `cloak` (obj 41, slot 26), `magical` (other obj 41: potions, tokens, horses), `ammo` (43, 44), `poison` (46), `tincture` (47), `gem` (48), `housing` (49-71), `siege` (80-84), `other` (0: loot, materials, dyes) |
| `obj` | int | Object_Type | names in `itemmeta.objTypes` |
| `slot` | int | Item_Type | names in `itemmeta.slots`. 10 right hand, 11 left hand, 12 two-handed, 13 ranged, 21 head, 22 hands, 23 feet, 24 jewel, 25 chest, 26 cloak, 27 legs, 28 arms, 29 neck, 32 belt, 33/34 bracer, 35/36 ring, 40 or 0 not equipped |
| `hand` | int | Hand | 0 one-handed, 1 two-handed, 2 left hand |
| `dmg` | int | Type_Damage | weapons: damage type (`itemmeta.dmgTypes`: 1 crush, 2 slash, 3 thrust, 10-15 magic). Always 0 for shields (their Type_Damage is the size: see `size`) |
| `dpsaf` | int | DPS_AF | weapons: DPS x 10 (165 = 16.5 DPS). Armor: armor factor. Shields: DPS x 10 that the shield's own styles hit with (a shield style uses the shield as its weapon, `ECS-Components/Actions/PlayerAttackAction.cs:71`, always crush damage, `AttackComponent.cs:115`); the stored values are uneven (0, 12, 165 on level 2 shields). Instruments: the instrument kind (`itemmeta.instruments`) |
| `spdabs` | int | SPD_ABS | weapons: speed x 10 (49 = 4.9 s). Shields: the speed x 10 its styles use. Armor: absorb % |
| `size` | int | Type_Damage | shields only: 1 small, 2 medium, 3 large (a stored 0 counts as 1, as the server does). 0 for everything else |
| `quality` | int | Quality | % |
| `bonus` | int | Bonus | the item bonus % field |
| `bonuses` | [[type, value], ...] | Bonus1..10 + ExtraBonus | type = eProperty id (`itemmeta.props`); pairs with type 0 or value 0 left out; may be `[]` |
| `procs` | null or [chance, spell id, spell id?] | ProcChance, ProcSpellID, ProcSpellID1 | chance = % per hit as the server rolls it: weapons (ProcChance or 10) x speed / 3.5, armor ProcChance or 10. Spell ids are keys of `items.spells` |
| `charges` | null or [[spell id, charges, max charges], ...] | SpellID/Charges/MaxCharges, SpellID1/..., PoisonSpellID/... | potions and poisons carry their effect here |
| `classes` | [class id, ...] | AllowedClasses | classes True 1.65 does not have are removed; `[]` = no class restriction. With `fl & 128` the list holds only such classes (kept as stored) |
| `levelReq` | int | LevelRequirement | 0 = none. Procs and charges work from this level, or from min(50, item level) when 0 |
| `price` | int | Price | copper, the template value = what a merchant charges for `pack` pieces |
| `sell` | int | derived | copper a merchant pays for one: Price / PackSize x 50 / 100; 0 = cannot be sold |
| `util` | number | derived | the server's weighted bonus points (`ItemValue165.Utility`: skills 5, resists and power 2, hits 0.25, the rest 1 per point) |
| `fair` | int | derived | copper: the server's own single value for the item, the Realm Exchange fair price of one piece at full condition (`ItemValue165.Fair`). See section 5 |
| `model` | int | Model | |
| `icon` | int | derived | client icon number (900-1599), 0 = none. See section 4 |
| `src` | int | derived | source bits (section 1) |
| `fl` | int | derived | flag bits (`items.flBits`): 1 cannot be traded (3 items), 2 cannot be dropped (97), 4 cannot be picked up, 8 indestructible, 16 never wears, 32 a merchant will not buy it (2,053), 64 on a loot table but falls only while on its quest (15), 128 no True 1.65 class can use it (1), 256 has a proc / charge that points at a spell the game does not have, so it never fires (128) |
| `stack` | int | MaxCount | largest stack (1 = does not stack) |
| `pack` | int | PackSize | pieces a merchant sells at once for `price` |
| `weight` | int | Weight | tenths of a pound |

True 1.65 has no no-trade rule of its own: `mode165` never reads the trade flags, and the world has 3 untradable
templates in the catalogue. The flags are the database's.

Spell row: `text` is the Spell table's own Description (cut at 200 characters), `damage` and `value` as stored,
`dmgType` an eDamageType id, `duration` seconds, `frequency` tenths of a second between ticks, `level` the spell's
level in the Item Effects / Potions / Mundane Poisons line (0 = not in those lines; the server gates by `levelReq`).

Quest row `role`: `reward`, `choice` (one of several), `trade`, `random reward`, `scripted quest` (epic reward), `step`.

### 5-row sample (real rows)

```
["1_Adamant_Tether_Breaker","Adamant Tether Breaker",51,0,"weapon",6,12,1,2,165,49,0,100,35,[[1,22],[20,4],[13,6],[40,4]],[14.0,31129],null,[],45,15000,7500,74.0,13500,7,903,1,0,1,1,56]
["p165_item_3291","Abysmal Boots",49,3,"armor",38,23,0,0,98,27,0,89,0,[[2,12],[4,10],[1,10],[14,8]],null,null,[46,44,47,45],0,18,9,48.0,16,432,1226,4,0,1,1,32]
["duskwood_exceptional_block_shield","duskwood exceptional block shield",51,1,"shield",42,11,0,0,165,48,3,85,0,[],null,null,[],0,342630,171315,0,1080000,1059,926,16,0,1,1,41]
["cq_hib_animist_bard_blademaster_champion_druid_eldritch_enchant__animist_s_belt_animist","Animist's Belt",35,3,"jewelry",41,32,0,0,0,0,0,100,15,[[5,14],[9,4],[17,7]],null,null,[55],0,0,0,36.0,1528065,597,1347,8,32,1,1,5]
["deploy_siege_catapult","catapult",0,1,"magical",41,40,0,0,0,0,0,100,0,[],null,[[33553,1,1]],[],0,500000,0,0,30,1677,1448,4,34,1,1,500]
```
and `spells["31129"] = ["71 DD(350 AoE) Heat","DirectDamage","Damages the target for 71",71.0,13,71.0,0,0,350,1500,"Enemy",1]`,
`craft["duskwood_exceptional_block_shield"] = [1, 995, [["arcanium_metal_bars",5],["duskwood_wooden_boards",3],["tempered_leather_squares",1]]]`,
`quests["cq_hib_..._animist"] = [["Viraniel's Plague",35,"Viraniel","reward"]]`.

## 3. itemmeta.js

```
props:      {"<eProperty id>": [name, kind, how many catalogue items use it]}   // every id the server names (236)
            kind: stat | hits | power | resist | skill | focus | cap | other.
            Names are the server's own (SkillBase.RegisterPropertyNames + the English language file).
            Resist names are the bare "Body", "Slash" (kind "resist"); skill 25 is also "Crush": add the word on the page.
objTypes:   {"<Object_Type>": name}       slots: {"<Item_Type>": name}       dmgTypes: {"<Type_Damage>": name}
hands, shieldSizes, instruments, realms, cats, craftSkills ({"<CraftingSkillType>": name})
armorTypes: [32,33,34,37,35,38,36]   armorLevel: {"<obj>": 0..5}   rangedTypes: [5,9,10,15,18]
weaponAbility: {"<obj>": [ability keys, any one is enough]}
objSkill:   {"<obj>": eProperty skill id}   // the skill line a weapon / shield / poison type uses (slash sword -> 44)
classes:    [{id, name, realm, base (id of the base class; own id for a base class), isBase,
              armor:   [[Object_Type, from level], ...],
              weapons: [[Object_Type, from level], ...]      // includes 43 arrows, 44 bolts, 45 instruments, 46 poisons
              shield:  [[size, from level], ...], shieldMax,
              leftHand (bool: may hold a weapon in the left hand),
              primary, secondary, tertiary, mana (eProperty stat ids, 0 = none; base classes have none),
              skills: [eProperty skill ids of the lines the class trains], focus: [eProperty focus ids],
              lines: [[line name, from level], ...], src (the class script)}]     // 54: 15 base + 39 advanced
disabledClasses [33,34,39,58,59,60,61,62], discontinuedClasses [63], classNames {"<id>": enum name, all classes}
caps:       {dpsaf: [by level 0..50], af: [...], afCloth: [...]}
rules:      {dpsCap, afCap, procLevel, procChance, slots, bonusCaps, sell, fair, realm, baseClass}  // one sentence + file:line each
valueWeights, sellRatio, notes
```

How "can my class use this item" is decided (the server's `CheckAbilityToUseItem`,
`serverrules/AbstractServerRules.cs:649-926`, with `allow_cross_realm_items = False`):

1. `item.realm` is 0 or the class's realm.
2. `item.classes` is empty or contains the class id (and `fl & 128` is off).
3. armor (obj 31-38): the type is in `class.armor` with `from level <= my level`.
   The list is by armor ability level, so it names both realm spellings (studded 34 / reinforced 37, chain 35 /
   scale 38); rule 1 removes the other realms' pieces.
4. weapons, arrows, bolts, instruments, poisons: the type is in `class.weapons` with `from level <= my level`.
   Bows: a class with the Archery ability (Scout, Ranger) passes for every bow type; rule 1 keeps it to its realm's.
5. shield (obj 42): `item.size <=` the largest size in `class.shield` reached at my level.
6. obj 41 (jewelry, cloaks, magical) and obj 0 / 1: always.
7. slot: a `slot` 11 item that is not a shield can be held in the RIGHT hand by anyone who passes rule 4 (a Thane
   may swing a left axe in the main hand); holding it in the LEFT hand needs `class.leftHand`
   (`gameutils/GamePlayerInventory.cs:782-828`). Instruments go in the two-handed or the ranged slot.

"From level" below 5 for an advanced class is taken from its base class (a character plays levels 1 to 4 as the
base class and advances at 5), so `Polearm` for an Armsman says 5 although the ability row says 1.

## 4. Icons

- `icons/items.png`: 320 x 2240, the client's seven item icon sheets stacked; 32 px cells, 10 per row.
  For `icon = n` (900..1599): `x = (n % 10) * 32`, `y = floor((n - 900) / 10) * 32`. `icon = 0`: draw no icon.
  CSS: `background: url(icons/items.png) -Xpx -Ypx; width: 32px; height: 32px`.
- `icons/items64.jpg`: 640 x 4480, the same cells at 64 px (the client's own enlarged sheets, halved). Same rule
  with 64 in place of 32. For the item page header or anything drawn larger than 32 px.

The icon comes from the item's model exactly as the game client picks it (`content/items/model_icons.json`).
22,374 items have one; 53 do not (siege engines and a few oddities). `items.icons` repeats the numbers.

## 5. Derived numbers (each is the server's own rule)

- **DPS cap**: a weapon's DPS counts up to `1.2 + 0.3 x level` (`gameobjects/GamePlayer.cs:5723-5731`), in `dpsaf`
  units `12 + 3 x level` = `itemmeta.caps.dpsaf[level]`.
- **Armor factor cap**: `2 x level`, cloth `1 x level` (`GamePlayer.cs:5614-5630`, applied in `GetArmorAF` :5635-5650)
  = `caps.af[level]`, `caps.afCloth[level]`. Effective AF of a piece = min(AF, cap) x quality x condition.
- **Bonus caps by level**: stats 1.5 x level, hits 4 x level, resists level / 2 + 1, skills level / 5 + 1, power
  level / 2 + 1 (`bots/autonomous/BotGearValue165.cs:92-107`).
- **`fair`** (`bots/autonomous/ItemValue165.cs:96-125`), one piece, full condition:
  gear (weapons, armor, magical, shields, instruments that do not stack) =
  `200 gold x (level/50)^3 x slot factor x quality factor x (1 + 0.5 x min(util, cap)/cap) x 1.25 if it has a proc
  or charges`, `cap = max(4, 0.8 x level)`, never above `800 gold x (level/50)^3`, at least twice what a merchant
  pays, at most 90% of the merchant price when any merchant list holds it. Slot factors: chest 1, legs 0.85, head
  and arms 0.7, hands and feet 0.6, two-handed 1.1, one-handed / left / ranged 0.9, cloak 0.5, other 0.6
  (`mode165/Exchange165.cs:74-84`); quality factors 100: 2.475, 99: 1.6, 98: 1.35, 97: 1.15, 96: 1, 95: 0.85,
  94: 0.75, lower 0.6 (:68-71). Everything else: 90% of the merchant price, or twice the sale value.
  It is a price, not a power score: use it as a tie-breaker or a "worth" column, not as the upgrade ranking.
- **`util`** (`ItemValue165.cs:36-74`): weighted bonus points, weights in `itemmeta.valueWeights`.

## 6. What is missing, and why

- No drop rates, no monsters, no merchants here: `drops.js` (D-D) and `vendoritems.js` (V-D) own those; `src` only
  says that the source exists.
- 130 proc / charge references on 128 items point at spells that are not in the Spell table: the reference is left
  off the item and the item gets `fl & 256` (in the game such a proc never fires; the server logs "Proc ID not found").
- `quests` keeps the quest name, minimum level, giver and role; not the steps.
- No condition / durability columns (every template is full), no colour / emblem / extension.
- 13 items that only monsters absent from the world would drop (section 1).
- Build order: `build_items.py` reads `data/mobs.js` (table `scripted`) for bit 128. Without that file it falls back
  to its own weaker test (the monster's name is a string in the server's scripts: 17 monsters instead of 19, two of
  them different) and prints that it did; `items.scriptMonstersFrom` says which was used.
