# Notes of lane Q (quests: data and pages)

## Decided

- **One quest object per DataQuest row.** The server keeps one row per class for most class quests (670 names in
  1,522 rows). The rows that are the same quest for another class share `grp`; the finder shows one row per quest
  unless "Show every class version" is on, and the embedded sections merge them too.
- **Step kinds are the server's real behaviour, not the StepType number.** The classic quest generator writes
  StepType 8 / 9 (Search) as a placeholder for steps DataQuest has no type for; `classic-quests.json` `Custom.Kind`
  is what happens (`ClassicQuests.cs:99-123`). A Search step without a Custom kind always has a search area in
  `SourceName` (asserted).
- **Kill hand-overs** are computed from `AdvanceQuestStep` (`DataQuest.cs:1583-1789`): the step's own item (with the
  `;N` chance: a failed roll means the kill does not count) and the next step's item when that step is a delivery.
  That second case is 551 kill steps whose hand-over no column names directly ("kill Bristle" has no item; the next step
  is "give Bristle's head", so the kill hands the head over).
- **Loot is not recomputed.** Quest-gated loot rows and the monsters that drop a turn-in item come from
  `data/drops.js` as D-D computed them (`how: "loot"`). The classic script's own drops are computed here and compared
  with `drops.js` `q` on every run (122 = 122).
- **Positions**: the giver is the Mob row of that name in the start region (runtime name: the NpcTemplate's when it
  replaces mob values); a step's `at` is the server's own map marker for that step (`classic-quests.json` Marker);
  an NPC target is the spawn of that name nearest to the marker.
- **Who may take it** follows the server: level range, AllowedClasses minus `disabled_classes`, and the base class
  rule (a base class stops at level 5, `GamePlayer.cs:4334`; an advanced class starts at 5). Dependencies are ported
  from `QuestDependencyRule.cs` per class; a quest whose prerequisite no allowed class can finish is left out.
- **Scripted quests** cannot be read from a table. Two helpers read the 11 epic scripts and the other quest scripts
  and wrote `source/scratch/Q/epics.json` and `scripted_other.json`; `source/scratch/Q/embed_scripted.py` puts them
  (with three hand corrections, commented there) into `build_quests.py` as `SCRIPTED_JSON`. The builder then needs
  nothing from the scratch folder. It checks every item id, title and NPC of that block against the source and world.
- **Reputation and bounty quests are not listed**: they do not exist in True 1.65 (see quests.md section 3). V-D was
  told and corrected `routes.js` (the emissary hunt was shown there as the way to earn reputation).
- The pages inject their own few `q-*` CSS rules (like V-U's pages do); nothing was needed in `app.css`.

## Told to others

- I-U: `WG.quest.itemSection` (done, on the item page). M-U: `WG.quest.mobSection` (done, on the monster page).
  P-U: `WG.quest.search`, `WG.quest.zoneSection`, `WG.quest.forZone` (done: search, home card, zone page).
  D-V: builder order (last) and the shape (in `verify_data.py` as Q1 to Q6, passing).

## Open

- (closed) D-V reported `wolf_pelt_cloak` missing from `items.js`; I-D added it: every quest reward item is in
  `items.js` now (`verify_data.py` Q5).
- **Lead**: P-U asks for `--run-all-compositor-stages-before-draw` in `source/shot.py` (map screenshots can come out
  with an empty canvas when the PC is busy). Not my file.
- The About page (D-V) could take the three sentences of `quests.meta.rules` (take, kill, xp) if it wants a quest
  paragraph.
