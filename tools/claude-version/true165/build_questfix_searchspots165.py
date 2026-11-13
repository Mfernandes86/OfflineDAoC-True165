"""Quest search areas moved to where the period walkthroughs put them (owner 2026-11-12, after the Spraggon bag: "check as
many as you think you can fit in with 10 agents"). Audit: <lead workspace>/searchaudit (91 search steps; 63 OK, these
WRONG; whole-zone areas that already fire anywhere were left alone). Data-quest search areas check X/Y only.

  Missing Tapestry step 7 (all 14 rows): the settlement /loc 29893,45423 was used; the step is "inside the house"
      -> the camp's only house, Camp Fomorhouse01, zone 182 /loc 28480,44416 (1,735 away; radius 400).
  Thraxia's Dilemma step 2 (all 13 rows): huts "at 50K, 40K", parchment "in the easternmost of the huts"; the 500 circle at
      50000,40000 missed both Were Huts -> the easternmost hut, zone 106 /loc 51072,39296.
  A Piece of Gormghlaith's Mind step 9: the Ruined Hut (the roofless barn) sat 36 units outside the circle -> zone 201
      /loc 10982,8656.
  Reaching Beyond step 3: the spot was on the keep on the hilltop; the walkthrough says swim at the base of the bridge ->
      the Camelot Hills bridge, zone 0 /loc 36608,50944 (11,942 away).
  The Piper's Price step 4: "Find the den around 16.4k, 24.7k" -> zone 204 /loc 16400,24700 (3,850 away; radius 600).

Writes true165-rules.json updates (both copies, the shared rules lock). Applies at the next START SERVER.
Run: python -I build_questfix_searchspots165.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

TAG = "True 1.65 0.8 questfix SEARCH: "
FIXES = [  # (quest name, old SEARCH part, new SEARCH part, why)
    ("Missing Tapestry", "SEARCH;7;;181;357573;405871;400;5", "SEARCH;7;;181;356160;404864;400;5",
     "step 7 searches inside the Fomorian camp's house (zone 182 /loc 28480,44416), not the settlement's /loc"),
    ("Thraxia's Dilemma", "SEARCH;2;;100;738128;941120;500;5", "SEARCH;2;;100;739200;940416;500;5",
     "step 2 searches the easternmost Were Hut (zone 106 /loc 51072,39296); the old circle missed both huts"),
    ("A Piece of Gormghlaith's Mind", "SEARCH;9;;200;330634;557009;500;5", "SEARCH;9;;200;330470;557520;500;5",
     "step 9 centred on the Ruined Hut (zone 201 /loc 10982,8656), 36 units outside the old circle"),
    ("Reaching Beyond", "SEARCH;3;;1;574139;530505;1500;5", "SEARCH;3;;1;585472;534272;1500;5",
     "step 3 searches the water at the base of the bridge (zone 0 /loc 36608,50944), not the hilltop keep"),
    ("The Piper's Price", "SEARCH;4;;200;304720;701136;600;5", "SEARCH;4;;200;303120;704636;600;5",
     "step 4 searches the den the walkthrough puts at 16.4k, 24.7k (zone 204), 3,850 from the old spot"),
    # Zone-wide circles (radius 32768: /search fired anywhere, the marker sat at the zone centre). Owner: "fix these".
    # Each gets the period spot with a generous radius, as the old areas were forgiving.
    ("Dark Places of the Soul", "SEARCH;14;;100;688128;638976;32768;5", "SEARCH;14;;100;683660;610208;5000;5",
     "step 14 the cold stone between the rocks where a player saw the frost spectre at 28.3k, 4k in Jamtland Mountains "
     "(zone 113); radius 5000 (the walkthrough's 'north-east of Glenlock' does not match that loc, so the circle is wide)"),
    ("Impossible Mission", "SEARCH;4;;1;319488;663552;32768;5", "SEARCH;4;;1;315720;664512;2000;5",
     "step 4 the ruined temple and its guards on the Lyonesse shore: between the two Ruined Temple fixtures (zone 10 "
     "/loc 29000, 33728), radius 2000"),
    ("Reports", "SEARCH;3;;51;524288;524288;32768;5", "SEARCH;3;;51;524464;544304;2000;5",
     "step 3 hide by the boats in the town north along the road into the Isle of Glass: the docks (zone 51 /loc "
     "32944, 52784), radius 2000"),
    ("Willing Sacrifice", "SEARCH;6;;21;40960;40960;32768;5", "SEARCH;6;;21;28415;32728;2500;5",
     "step 6 the password room beyond the forgotten promise in the Tomb of Mithra (zone 21 /loc 20223, 24536), radius "
     "2500 to take in the neighbouring rooms (the walkthrough gives turns, not a /loc)"),
    ("Immortal Gift", "SEARCH;12;;224;40960;40960;32768;5", "SEARCH;12;;224;30145;31358;1500;5",
     "step 12 the forge behind the Dreaded Ursine in Treibh Caillte (zone 224 /loc 21953, 23166), radius 1500"),
]


def q(s):
    return s.replace("'", "''")


def fn(rules):
    rules["updates"] = [u for u in rules["updates"] if not u.get("reason", "").startswith(TAG)]
    for name, old, new, why in FIXES:
        rules["updates"].append({
            "sql": f"UPDATE DataQuest SET SourceName = REPLACE(SourceName, '{old}', '{new}') "
                   f"WHERE Name = '{q(name)}' AND SourceName LIKE '%{old}%'",
            "reason": TAG + f"{name}: {why} (period walkthrough + client zone fixtures)",
        })


print("rules bytes", locks.edit_rules("SEARCH", fn))
