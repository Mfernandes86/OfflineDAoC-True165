"""True 1.65 0.8 wallfix PATHS (2026-11-12, owner: "the quest npc spackit spawns behind an invisible wall where the player
can't walk. do a 10 agent audit of any spawns like this behind invisible zone walls and move the npcs and the quest markers
accordingly"): patrol / walker path points that lie behind the client's invisible zone walls.

Audit (every Path / PathPoints row of the derived world, walked by a Mob row's PathID, an adventurer's true165_adv_<id>
path, a guard's "<id> Rounds" path or a classic-quest event spawn's PathId; segments sampled every 16 units against
<lead workspace>/wallaudit\\tools.py is_reachable + wall_side): one walker leaves the player's reach.

  The Unseelie Envoy convoy (true165_unseelie_envoy_convoy, add-primapathpoints165.json, made by
  prima_batch_2026-11-07/build_encounters165.py as a straight line from the land bridge near Siopa (Bog of Cullen) to
  Lough Gur south-east, one point every ~1,500 units). Steps 24-30 of that line cut 76-1,055 units behind the south
  wall of Lough Gur / Bog of Cullen (zone 204/205 bound.json), on the outer strip between the wall and the zone edge:
  a player following the convoy from the north is stopped at the wall. No period source records the path points
  (Freddyshouse 2002-05-05 / 2003-02-04 only say the convoy walks from the Siabra city into Lough Gur and back), so
  each point moves straight north (same X, so the spacing along the route stays the same) to the first spot on the
  playable side at least 200 units inside the wall, with Z on the server navmesh floor (navmesh-165 / navmesh; the
  client terrain agrees within 12 units). All new legs 22-32 stay on the playable side.

The rows belong to add-primapathpoints165.json (no rules entry: updates run before add-files and cannot change them):
they are changed in that file, both copies, guarded by key and old X/Y, as build_gamefix_spawns165.py does. The launcher
updates rows an add-file added before, so a refresh moves the points in existing worlds.
RE-RUN THIS after anything rebuilds add-primapathpoints165.json (build_encounters165.py writes the old line again).

    python -I build_wallfix_paths165.py            write (rerunnable: rows already moved are left as they are)
    python -I build_wallfix_paths165.py --check    report only
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
RT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
FOLDERS = [os.path.join(RT, "runtime", "true165"), HERE]
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

LANE = "PATHS"
TAG = "True 1.65 0.8 wallfix PATHS: "
FILE = "add-primapathpoints165.json"
PATH_ID = "true165_unseelie_envoy_convoy"
# PathPoints_ID: (step, (old X, Y, Z), (new X, Y, Z))
MOVES = {
    "f790acd2-e70a-5423-8dbb-0a05509d5b0d": (24, (353695, 744207, 7191), (353695, 743455, 6772)),
    "7e4d4e79-e684-54d0-8787-5d0f87a2f0d8": (25, (352120, 744038, 7834), (352120, 742710, 6850)),
    "4f0e1a23-5548-5bf5-b8ad-7a221f302464": (26, (350546, 743869, 7867), (350546, 742749, 6925)),
    "958f98d5-1a13-589e-8ab6-31c2c6f9073f": (27, (348971, 743701, 7673), (348971, 742789, 7149)),
    "9e3c92fa-8fec-53dc-8fa3-3f27a4c051c0": (28, (347396, 743532, 7328), (347396, 742828, 6935)),
    "7025bd8f-14ef-5bf4-abc6-00320b5d8b05": (29, (345821, 743364, 7026), (345821, 742868, 6839)),
    "3d88a1df-a99c-5ca4-a8fc-2c8a87339d47": (30, (344247, 743195, 7154), (344247, 742907, 7054)),
}


def dump(data, newline, final):
    out = json.dumps(data, indent=1, ensure_ascii=False).encode("utf-8").replace(b"\n", newline)
    return out + newline if final else out


def fix(check):
    raws = [open(os.path.join(f, FILE), "rb").read() for f in FOLDERS]
    assert raws[0] == raws[1], "the two copies of %s differ: stop and tell the lead" % FILE
    raw = raws[0]
    nl = b"\r\n" if b"\r\n" in raw else b"\n"
    final = raw.endswith(nl)
    data = json.loads(raw.decode("utf-8"))
    assert dump(data, nl, final) == raw, "%s does not round-trip byte for byte; not touching it" % FILE
    key = data["key"]
    seen, changed = set(), 0
    for row in data["rows"]:
        m = MOVES.get(row.get(key))
        if not m:
            continue
        step, old, new = m
        seen.add(row[key])
        assert row["PathID"] == PATH_ID and row["Step"] == step, row
        cur = (row["X"], row["Y"], row["Z"])
        assert cur in (old, new), "%s step %d is at %s, expected %s or %s: the route was rebuilt differently" % (
            PATH_ID, step, cur, old, new)
        if cur != new:
            row["X"], row["Y"], row["Z"] = new
            changed += 1
        print("  step %2d  %s -> %s%s" % (step, old, new, "" if cur != new else "  (already moved)"))
    assert seen == set(MOVES), "rows not found: %s" % (set(MOVES) - seen)
    if changed and not check:
        out = dump(data, nl, final)
        for f in FOLDERS:
            open(os.path.join(f, FILE), "wb").write(out)
    return changed


def drop_own_rules(rules):
    """This lane writes no rules entries; drop any left by an earlier run so a re-run is clean."""
    rules["updates"] = [u for u in rules.get("updates", []) if not u.get("reason", "").startswith(TAG)]
    where = rules.get("remove", {}).get("where")
    if isinstance(where, list):
        rules["remove"]["where"] = [w for w in where if not str(w.get("reason", "")).startswith(TAG)]


def main():
    check = "--check" in sys.argv
    with locks.lock("rules"):          # add-files other lanes may also edit: one writer at a time
        n = fix(check)
    if not check:
        raw = open(FOLDERS[1] + "/true165-rules.json", "rb").read().decode("utf-8")
        if TAG in raw:
            locks.edit_rules(LANE, drop_own_rules)
    print("%s: %d point(s) %s" % (FILE, n, "would change" if check else "changed"))


if __name__ == "__main__":
    main()
