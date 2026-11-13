"""True 1.65 0.8 wallfix MIDM (lead brief <lead workspace>/wallaudit\\WALLAUDIT-BRIEF.md, 2026-11-12; owner: "the quest npc
spackit spawns behind an invisible wall where the player can't walk. do a 10 agent audit of any spawns like this behind
invisible zone walls and move the npcs and the quest markers accordingly"). Report: <lead workspace>/wallaudit\\reports\\MIDM.md.

Midgard region 100. Fifteen Mob rows stood where a player can never walk: behind the real 1.64 client's bound.csv walls
(client content/zones/zoneNNN/bound.json; one-sided, the playable side is the line's right).

  Malmohus (zone 116), 13 x 'swarm of flies' (template 60166696, level 52, Flags 32 FLYING, no loot table, no quest,
    no script names them; the Gjalpinulva encounter does not use them; all 12-41k units from the dragon). They stood inside the
    mountain rings the walls fence off. They were also 350-1820 units UNDER that mountain's ground, so no one ever saw
    them. No period position exists: Illia's bestiary on period Allakhazam lists 'Swarm of Flies 50, Animal' in
    Malmohus with no location, CapnBry's radar never logged one, and the Prima Atlas Malmohus map has no label for them.
    So each camp moved as a group (relative spacing kept) by the shortest shift that puts every member on the playable
    valley side, at least 250 units inside the walls, on the server navmesh, out of the lake, and at least 180 units from
    any other spawn. Z = ground + 75: the hover of the 21 reachable swarms in the zone (their median over the ground is
    about 75, range 45-197).
  Odin's Gate (zone 115):
    bone-eater eviscerater acda71ae: in the sealed strip between the east ring wall (line 1) and the two-way frontier wall
      at x 63776 (lines 2/4). The radar sighting nearest to it (local 63226, 36089) is in the same strip, so it moved to
      the nearest playable spot inside its own camp (the other 10 eviscerater rows): 348 west, 229 south.
    white wolf adbbd9ac (add-restoredspawns165.json): in the notch outside the south-west ring wall (line 0). There are no
      white wolf radar sightings within 4,000 units, so it moved 476 units west to the nearest playable spot. Its
      add-file row is changed here, as build_gamefix_spawns165.py does it, so the launcher's add-file update moves
      the row in existing worlds too.

Nothing else changes (level, template, loot, heading, roaming, respawn). Base rows: rules 'updates' guarded by the old X/Y,
tagged 'True 1.65 0.8 wallfix MIDM: ' (a re-run replaces its own entries). Positions come from the lane DETECT helpers
(<lead workspace>/wallaudit\\tools.py: walls, reach components, navmesh-165 then navmesh floors) and are written into this file
as data, so a re-run gives the same result.

    python -I build_wallfix_midm165.py
Out (both copies, runtime\\true165 and tools\\claude-version\\true165): true165-rules.json (through
<lead workspace>/gamefix\\locks.py) and the white wolf row in add-restoredspawns165.json (format and line endings kept).
Rerun it after anything rebuilds add-restoredspawns165.json.
"""
import json, os, sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
FOLDERS = (os.path.join(CV, "runtime", "true165"), HERE)
LANE = "MIDM"
TAG = "True 1.65 0.8 wallfix %s: " % LANE
REGION = 100
ZONES = {116: ("Malmohus", 688128, 966656), 115: ("Odin's Gate", 589824, 573440)}

# camp, Mob_ID, name, zone, old X, Y, Z, new X, Y, Z, ground Z under the new spot, units inside the wall
MOVES = [
    ("A", "6b517a00-e91c-49aa-8a3e-6aeae0b57856", "swarm of flies", 116, 712159, 982138, 5381, 711809, 980315, 4635, 4560, 592),
    ("A", "7d8ac9e0-84bf-4b53-8401-6e6f27524d82", "swarm of flies", 116, 712428, 982493, 5306, 712078, 980670, 5105, 5030, 252),
    ("B", "5b7ca0ae-853b-4ece-ad9f-cbc83196fade", "swarm of flies", 116, 702823, 986407, 4431, 704241, 984431, 3032, 2957, 803),
    ("B", "6214760a-6d25-4c00-929b-527b48c50290", "swarm of flies", 116, 702981, 986554, 4452, 704399, 984578, 3061, 2986, 787),
    ("B", "9a1a22f5-6f41-49ec-8e6f-86a635cc35d3", "swarm of flies", 116, 702618, 986950, 4431, 704036, 984974, 3459, 3384, 251),
    ("C", "cfa572b3-fc99-46b2-9453-11dcfab0e76d", "swarm of flies", 116, 706246, 991504, 4857, 704622, 991015, 4643, 4568, 260),
    ("C", "fdaa0018-7608-4887-a587-83c6c1e3cac3", "swarm of flies", 116, 705633, 991637, 4857, 704009, 991148, 3721, 3646, 870),
    ("C", "315e3f39-ba40-4ed5-92ee-64f83f2205e7", "swarm of flies", 116, 706092, 991728, 4857, 704468, 991239, 4331, 4256, 429),
    ("C", "d1d73083-0f4d-401a-ae6d-19636b1d8647", "swarm of flies", 116, 705848, 992215, 4857, 704224, 991726, 4158, 4083, 254),
    ("D", "ec76152a-f3e9-445c-9feb-f1b5a572b659", "swarm of flies", 116, 717933, 997230, 4901, 719115, 997294, 4360, 4285, 251),
    ("E", "dac35ff4-8100-4905-a007-3f2268d86055", "swarm of flies", 116, 718076, 1006035, 4516, 720041, 1003974, 3962, 3887, 250),
    ("F1", "d0b951a8-0852-49c6-b80d-798ad307f8c9", "swarm of flies", 116, 702643, 1011813, 6368, 705869, 1008892, 4544, 4469, 253),
    ("F2", "5f4eca8d-ac40-4216-ad75-13a289b7db77", "swarm of flies", 116, 704656, 1012855, 6374, 706397, 1009254, 4291, 4216, 252),
    ("EVI", "acda71ae-9903-45f8-9a4a-deb8c65b9102", "bone-eater eviscerater", 115, 652698, 609317, 8363, 652350, 609546, 8095, 8095, 252),
]
WOLF = ("WOLF", "adbbd9ac-2436-4697-bc00-eaf3582ea7e2", "white wolf", 115, 617417, 612314, 6201, 616941, 612378, 5752, 5752, 257)

# server navmesh floor under the OLD spot (the mountain's ground the swarm was buried under)
OLD_GROUND = {"6b517a00": 5740, "7d8ac9e0": 5851, "5b7ca0ae": 6022, "6214760a": 6090, "9a1a22f5": 6127, "cfa572b3": 5914,
              "fdaa0018": 5839, "315e3f39": 5986, "d1d73083": 6090, "ec76152a": 5681, "dac35ff4": 6343, "d0b951a8": 7626,
              "5f4eca8d": 6730}

WHY = {
    "fly": ("one of the Malmohus fly swarms (no loot, no quest; period Allakhazam/Illia lists 'Swarm of Flies 50' in "
            "Malmohus with no location, no CapnBry sighting, not on the Prima Atlas map). It stood inside a walled-off "
            "mountain ring %d units below that mountain's ground, so it was never seen. Camp %s moved as a group by the "
            "shortest shift to the playable valley side (>= 250 inside, on the navmesh, >= 180 from other spawns); "
            "FLYING (Flags 32), so it hovers 75 over the ground like the zone's 21 reachable swarms"),
    "EVI": ("one of the 11 bone-eater eviscerater camp rows at the Odin's Gate east wall. It stood in the sealed strip "
            "between the east ring wall and the two-way frontier wall at local x 63776; the nearest CapnBry sighting "
            "(63226, 36089) is in the same strip. Moved to the nearest playable spot inside its own camp"),
    "WOLF": ("a white wolf restored by add-restoredspawns165.json. It stood in the notch outside the south-west ring "
             "wall; no white wolf radar sighting within 4,000 units. Moved to the nearest playable spot west of that wall"),
}


def loc(m):
    zone, ox, oy = m[3], ZONES[m[3]][1], ZONES[m[3]][2]
    return ZONES[zone][0], m[4] - ox, m[5] - oy, m[7] - ox, m[8] - oy


def reason(m):
    zname, olx, oly, nlx, nly = loc(m)
    kind = "fly" if m[2] == "swarm of flies" else m[0]
    why = WHY[kind] % (OLD_GROUND[m[1][:8]] - m[6], m[0]) if kind == "fly" else WHY[kind]
    return (TAG + "%s (%s) stood behind the invisible zone wall at %s %d, %d, Z %d, out of a player's reach; %s; now "
            "%d, %d, Z %d (ground %d, %d inside the wall); nothing else changed (build_wallfix_midm165.py)"
            % (m[2], m[1], zname, olx, oly, m[6], why, nlx, nly, m[9], m[10], m[11]))


def updates():
    return [{"sql": "UPDATE Mob SET X = %d, Y = %d, Z = %d WHERE Mob_ID = '%s' AND Region = %d AND X = %d AND Y = %d"
                    % (m[7], m[8], m[9], m[1], REGION, m[4], m[5]), "reason": reason(m)} for m in MOVES]


def dump(data, newline, final):
    out = json.dumps(data, indent=1, ensure_ascii=False).encode("utf-8").replace(b"\n", newline)
    return out + newline if final else out


def fix_wolf():
    name = "add-restoredspawns165.json"
    raws = [open(os.path.join(f, name), "rb").read() for f in FOLDERS]
    assert raws[0] == raws[1], "the two copies of %s differ: stop and tell the lead" % name
    raw = raws[0]
    nl = b"\r\n" if b"\r\n" in raw else b"\n"
    final = raw.endswith(nl)
    data = json.loads(raw.decode("utf-8"))
    assert dump(data, nl, final) == raw, "%s does not round-trip byte for byte; not touching it" % name
    rows = [r for r in data["rows"] if r.get("Mob_ID") == WOLF[1]]
    assert len(rows) == 1 and rows[0]["Name"] == WOLF[2] and rows[0]["Region"] == REGION, rows
    r = rows[0]
    old, new = (WOLF[4], WOLF[5], WOLF[6]), (WOLF[7], WOLF[8], WOLF[9])
    cur = (r["X"], r["Y"], r["Z"])
    assert cur in (old, new), "white wolf row is at %r, expected %r or %r" % (cur, old, new)
    if cur == new:
        return 0
    r["X"], r["Y"], r["Z"] = new
    out = dump(data, nl, final)
    for f in FOLDERS:
        open(os.path.join(f, name), "wb").write(out)
    return 1


def main():
    with locks.lock("rules"):          # add-files other lanes may also edit: one writer at a time
        print("add-restoredspawns165.json: white wolf row %s" % ("moved" if fix_wolf() else "already moved"))
    ups = updates()
    mine = lambda e: str(e.get("reason", "")).startswith(TAG)

    def apply(rules):
        rules["updates"] = [u for u in rules["updates"] if not mine(u)] + ups
    size = locks.edit_rules(LANE, apply)
    print("true165-rules.json: %d updates tagged %r (%d bytes)" % (len(ups), TAG, size))
    print(reason(WOLF))


if __name__ == "__main__":
    main()
