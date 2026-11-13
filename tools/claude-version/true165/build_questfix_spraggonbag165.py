"""Spicket, Spackit and Spykit (DataQuest 21397), step 3 "Search under the bridge north of Ardee" (owner 2026-11-12:
"the quest marker doesnt point there and /search doesnt work near 49187 29547").

The world's search area (DataQuest.SourceName "SEARCH;3;;200;340873;468276;1500;5") and the step 3 map marker
(classic-quests.json) sat about 4,000 units north of Ardee in open country: Connacht /loc 54153, 50484. The period
walkthrough puts the hidden Spraggon Bag under "the bridge on the road to Druim Ligen", the bridge north of Ardee, and
says you swim under it and search. Connacht has one bridge in the client (fixture "Elf Bridge", h-bridge1) at /loc
48256, 29696, deck height 4850: world 334976, 447488 (Connacht offset 286720, 417792). Data-quest search areas do not
check height (QuestSearchArea: "Z is not checked"), so the 1,500 radius covers the deck and the water under it.

Writes: one true165-rules.json update (both copies, through the shared rules lock) and the step 3 marker of quest 21397
in runtime/server/classic-quests.json (the server reloads that file when it changes). The search area itself changes
at the next START SERVER. Re-run after anything rewrites classic-quests.json markers (questfix07).
Run: python -I build_questfix_spraggonbag165.py
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

TAG = "True 1.65 0.8 questfix SPRAGGON: "
OLD_SEARCH = "SEARCH;3;;200;340873;468276;1500;5"
NEW_SEARCH = "SEARCH;3;;200;334976;447488;1500;5"
OLD_MARK, NEW_MARK = (340873, 468276, 0), (334976, 447488, 4850)
QUESTS = os.path.normpath(os.path.join(HERE, "..", "..", "..", "runtime", "server", "classic-quests.json"))


def rules(r):
    r["updates"] = [u for u in r["updates"] if not u.get("reason", "").startswith(TAG)]
    r["updates"].append({
        "sql": f"UPDATE DataQuest SET SourceName = '{NEW_SEARCH}' WHERE ID = 21397 AND SourceName = '{OLD_SEARCH}'",
        "reason": TAG + "Spicket, Spackit and Spykit step 3 searches under the Elf Bridge north of Ardee (Connacht /loc "
                        "48256, 29696), the bridge on the road to Druim Ligen the period walkthrough names; the old area "
                        "lay in open country 4,000 units north of Ardee",
    })


print("rules bytes", locks.edit_rules("SPRAGGON", rules))

raw = open(QUESTS, "rb").read()
text = raw.decode("utf-8-sig")
start = text.index('"21397": {')
end = text.index('"XpShares"', start)
block = text[start:end]
old = re.compile(r'"Region": 200,(\s*)"X": %d,(\s*)"Y": %d,(\s*)"Z": %d' % OLD_MARK)
new = r'"Region": 200,\g<1>"X": %d,\g<2>"Y": %d,\g<3>"Z": %d' % NEW_MARK
if old.search(block):
    block2, n = old.subn(new, block)
    assert n == 1, n
    out = text[:start] + block2 + text[end:]
    json.loads(out)
    data = out.encode("utf-8")
    if raw.startswith(b"\xef\xbb\xbf"):
        data = b"\xef\xbb\xbf" + data
    open(QUESTS, "wb").write(data)
    print("marker moved")
elif re.search(r'"X": %d,\s*"Y": %d' % NEW_MARK[:2], block):
    print("marker already at the bridge")
else:
    raise SystemExit("step 3 marker of 21397 not found as expected - look before changing")
