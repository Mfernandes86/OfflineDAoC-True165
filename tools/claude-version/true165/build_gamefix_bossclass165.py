"""True 1.65 0.8 gamefix BOSSCLASS (lead, owner 2026-11-12): Qan'ris Duros and Scout Argyle stay plain NPCs.

Their NPC templates name the classes 'QanrisDuros' and 'ScoutArgyle' with no namespace, so the server never finds
them and runs both as a plain GameNPC (it falls back and logs a warning) - that is how they have always fought.
The owner asked to check period data and bot-friendliness before wiring the scripts up:
- Qan'ris Duros: period named level-68 drakoran in the Dales of Devwy (Prima Shrouded Isles guide, monster table;
  Camelot Herald forum posts 2003-08 and 2003-11 "named drack ... just left of the bridge"). No period source gives
  him special mechanics. The script (scripts/namedmobs/.../QanrisDuros.cs) is an Atlas freeshard rework: 30,000 HP,
  AF 350, 20/30 % resists, crowd-control immunity, an uninterruptible 400-damage Thunder Stomp in a 1,000 radius every
  few seconds, a full heal whenever nobody is in aggro range, and region-wide bring-a-friend aimed at region 3500 (not
  his region). It would change his difficulty (project rule: never change difficulty numbers; freeshard content is
  removed) and it is bot-unfriendly (the full-heal reset breaks pulls and corpse runs, the stomp wipes casters,
  crowd-control plans fail). NOT wired.
- Scout Argyle: period named level-12 in Gotar, no loot (Prima Atlas guide "Scout Argyle [12] (no loot)", CapnBry
  radar sightings at level 12), the kill step of the Hunter / Shadowblade guild quest "A War of Old". The script
  (scripts/mobs/MidgardMobs/ScoutArgyle.cs) only adds bring-a-friend for monsters packaged 'ScoutArgyleBaf' - none
  exist in the world - and saves itself to the database on every spawn. Nothing period, nothing gained. NOT wired.
So both templates (and rows) name DOL.GS.GameNPC explicitly: exactly today's behaviour, without the fallback warning.
Both are base rows of the world (not added by a True 1.65 file), so rules updates reach them.
"""
import os
import sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks

LANE = "BOSSCLASS"
TAG = locks.TAG % LANE
UPDATES = [
    ("UPDATE NpcTemplate SET ClassType = 'DOL.GS.GameNPC' WHERE TemplateId = 60165072 AND ClassType = 'QanrisDuros'",
     "Qan'ris Duros template names a class that does not exist (Atlas script not wired: bot-unfriendly, not period); plain GameNPC as he always ran"),
    ("UPDATE Mob SET ClassType = 'DOL.GS.GameNPC' WHERE Mob_ID = '5848c78e-3394-492b-aa5d-39e7853bd8fc' AND ClassType = 'DOL.GS.QanrisDuros'",
     "Qan'ris Duros row: plain GameNPC, as the template"),
    ("UPDATE NpcTemplate SET ClassType = 'DOL.GS.GameNPC' WHERE TemplateId = 60165671 AND ClassType = 'ScoutArgyle'",
     "Scout Argyle template names a class that does not exist (its script adds nothing period); plain GameNPC as he always ran"),
    ("UPDATE Mob SET ClassType = 'DOL.GS.GameNPC' WHERE Mob_ID = 'd7187d48-ac6f-448d-adce-1fe644e100ef' AND ClassType = 'DOL.GS.ScoutArgyle'",
     "Scout Argyle row: plain GameNPC, as the template"),
]


def apply(rules):
    rules["updates"] = [u for u in rules["updates"] if not u.get("reason", "").startswith(TAG)]
    rules["updates"].extend({"sql": sql, "reason": TAG + why} for sql, why in UPDATES)


if __name__ == "__main__":
    size = locks.edit_rules(LANE, apply)
    print("rules written (both copies):", size, "bytes;", len(UPDATES), "updates tagged", TAG.strip())
