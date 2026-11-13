# Notes from I-D (items data) — living file

Last update: 2026-11-12, after the third build (bit 128 follows mobs.js). Shapes: `source/contract/items.md`. Report: `source/reports/I-D.md`.

## What I decided

- **Catalogue = templates with a real source** (22,427 of 40,717). Source bits in `src`: 1 drop, 2 one-time drop,
  4 vendor, 8 quest reward, 16 craft, 32 starter, 64 quest step item, 128 loot of a script-summoned boss.
- **Which monsters and merchants are "in the world"**: the same rules as D-D and V-D, read from the same code
  (loaded regions `world/Region.cs:234-264`; runtime monster name from the NPC template when it has ReplaceMobValues
  `GameNPC.cs:1341-1345`; the class the server creates `Region.cs:745-772`; merchant list `GameMerchant.cs:348-373`).
  `build_items.py` compares its bits 1, 2 and 4 with `data/drops.js` and `data/vendoritems.js` when they are there
  and prints every difference; it never takes values from them, so the builder order does not matter.
  Right now: the same 7,254 drop items, the same 76 one-time drops, the same 5,812 sold items. Zero differences.
- **Bit 128** (loot of a script-made boss): which monsters the scripts really create is M-D's finding, so
  `build_items.py` reads `data/mobs.js`, table `scripted` (154 monsters; 19 of them have a loot table: Apocalypse and
  the four horsemen, Beliathan, Host, the three corrupt primals, Elder Icelord Suttung, the Prima encounter adds...).
  Their 103 items carry bit 128, the same set D-D marks with 2 in `drops.byItem` (compared at build time, no
  difference). My first rule (the monster's name is a string in the server's scripts) gave 17 monsters and was wrong
  on six: it missed the primals and Suttung and kept Blight and Noble Werewolf Alina, which nothing in this world
  starts. It remains only as the fallback when mobs.js is not there.
  15 loot tables belong to monsters nothing creates (Galand, Brannoc, Jailor Vifil, Blight...): their 13 exclusive
  items are left out.
- **Quest reward vs quest step item**: rewards (bit 8) are DataQuest Final/Optional reward templates, the JSON's
  Choices, Trades results, SideTurnIns rewards, CollectRewards items, every class version in ClassRewards, and what the
  scripted quests give in FinishQuest (or name `...Epic...`): the 11 level 50 epics, the Shrouded Isles scripts, and
  the two Albion scripts Wolf Pelt Cloak and Immediate Resolution (their item ids are constants, which my first
  parser missed; D-V found the missing cloak). Step items (bit 64) are everything else a quest names.
- **Disabled classes**: 33, 34, 39, 58-62 from ServerProperty `disabled_classes`; 63 (Sluaghbinder) is left out too:
  creation is refused in code (`CharacterCreateRequestHandler.cs:429-436`).
- **Class usability** is computed, not typed: for each class and level 1 to 50 the abilities its career
  specializations grant (`ClassXSpecialization` + `SpecXAbility`, the way `Specialization.cs:366-381` and
  `GamePlayer.cs:3497-3504` combine them), then the item rule of `AbstractServerRules.cs:649-926`. Below level 5 an
  advanced class uses its base class's rows.
- **Property names** are the server's (`SkillBase.RegisterPropertyNames` + `languages/EN/SkillHandler.txt`), parsed at
  build time. A few translation keys are missing in the English file; those properties are named after their
  eProperty member (none of them is on a catalogue item).
- **Icons**: by model, the client's own rule. Two sheets (32 px PNG, 64 px JPEG).

## Server data oddities found (for the owner; nothing here was changed)

1. **130 proc / charge references on 128 obtainable items point at spells that do not exist** (65 spell ids: 32210 on
   9 items such as Archaic Granite Long Sword, 32190 on 8 such as Blackened Granite Shield, 32031 on 5 such as Fate
   Sealer, 32012 on 5 such as Kvasir's Sword of Blight...). In the game those procs never fire. Flag `fl & 256`; list: run
   `python -I source/build_items.py` (count) or filter `fl & 256` in items.js.
2. `SpecXAbility` row 407: `ChampionCareer, SpecLevel 2, Shield, AbilityLevel 5` looks like level and value swapped
   (row 247 is the normal `5 -> Shield 2`). It has no effect on a Champion of level 5 or more at login; the tool
   shows Champions with medium shields from 5.
3. Shield `DPS_AF` values are uneven (level 2 shields with 0, 12 or 165).
4. 33 recipe ingredients have no source at all (`items.otherNames`: crown pattern, cleansed diamond seal, 30 housing
   shop pieces): those recipes cannot be completed from the world as it is.
5. 15 loot tables belong to monsters that are not in the world (see bit 128 above).

## What I need from whom

- Nothing blocking.
- I-U: when inverting `drops.byItem`, skip item ids that items.js does not have (13 ids of absent monsters).
- D-V: please run `items` after `mobs` in `build_all.py` (it reads `mobs.scripted` for bit 128).
- D-V: `build_items.py` reads `data/drops.js` and `data/vendoritems.js` only to compare (prints "DIFFERENCE" lines,
  never fails on them, never copies); `data/mobs.js` it does use (see above). An equality check of those sets belongs in `verify_data.py` if you want it to
  fail the build. My own checks: `python -I source/scratch/I-D/verify_items.py` (PASS / FAIL lines, exit code).

## Open problems

- none
