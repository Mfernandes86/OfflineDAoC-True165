# Agent directory (lead, 2026-11-12)

Use SendMessage with the id as `to` for a question that blocks you. Keep messages short and specific; put anything
lasting in your notes file. The lead is `main`.

| Lane | Does | Agent id | Partner |
|---|---|---|---|
| I-D | items data | a4cb673037698925c | I-U |
| I-U | item finder, item page | a1e4e89dbb4a59fda | I-D |
| D-D | drop rates | a42f1335524a4bcff | D-V |
| D-V | verifier, build_all, about | abcdd1277f0f59a01 | D-D |
| M-D | monsters and camps data | ae1cd629b5015be16 | M-U |
| M-U | hunting finder, monster page, con library | ad1bef8385f282bf2 | M-D |
| P-D | zones, map images, coordinate rule | a0dd9573d70a0b998 | P-U |
| P-U | map viewer, widgets, css, home, search, zones | ab2415a20fa0abece | P-D |
| V-D | merchants and stable routes data | a41588875b4c1f9f8 | V-U |
| V-U | merchant pages, route planner | a935c66366424c36a | V-D |
| Q | quests: data AND pages (added later, owner's request) | ad007bad62959fb7e | works with I-U, M-U, P-U, D-V |

Who needs whom most, beyond the pairs:
- D-D and M-D: the monster key (lowercased name) and which names are really spawned.
- I-U, M-U, V-U need P-U's `source/contract/widgets.md` (map viewer and widget signatures) and P-D's coordinate rule.
- I-U and M-U both show drops: read `source/contract/drops.md`.
- V-U needs P-D's realm overview maps for the route network.
- D-V checks everyone at the end; answer its punch list.
- Q (quests) owns `source/build_quests.py`, `data/quests.js`, `app/lib/quests.js`, `app/views/quests.js`, `app/views/quest.js`.
  It offers `WG.quest.forItem / forMob / forZone / section` (see `source/contract/quests.md` when it lands): I-U, M-U and
  P-U add a "Quests" section / search entries through it; D-V adds it to build_all, verify_data and the sweep.
