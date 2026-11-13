"""Housing hasteners only where the real 1.64 client has ground (owner 2026-11-12: "do it").

build_rules165.py now leaves out the 59 housing hasteners standing in the 15 housing zones the 1.64 client does not have
(it writes their ids to rules165-hasteners-dropped.json). A world that already added them keeps them, because an
add-file never deletes, so this adds one remove.where rule to true165-rules.json (both copies, through the shared
locks helper) that archives exactly those rows. Re-run after build_rules165.py. Run: python -I build_hastenerzones165.py
"""
import json, os, sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TAG = "True 1.65 0.8 hastenerfix: "
ids = json.load(open(os.path.join(HERE, "rules165-hasteners-dropped.json"), encoding="utf-8"))
assert ids and all(len(i) == 36 for i in ids), ids


def fn(rules):
    w = [r for r in rules["remove"]["where"] if not r.get("reason", "").startswith(TAG)]
    w.append({
        "table": "Mob",
        "key": "Mob_ID",
        "where": "ClassType = 'DOL.GS.GameHastener' AND Region IN (2, 102, 202) AND Mob_ID IN (%s)"
                 % ", ".join("'%s'" % i for i in ids),
        "reason": TAG + "%d housing hasteners in housing zones the 1.64 client does not have (Brisworthy, Aylesbury, "
                        "Chiltern, Sherborne, Stoneleigh; Frisia, Kaupang, Holmestrand, Nittedal, Stavgaard; Saeranthal, "
                        "Aberillan, Tullamore, Moycullen, Broughshane): no ground or walls there, never reachable (wall "
                        "audit 2026-11-12, lane BGFR). build_rules165.py no longer adds them." % len(ids),
    })
    rules["remove"]["where"] = w


print("rules bytes", locks.edit_rules("HASTENERFIX", fn), "| rows", len(ids))
