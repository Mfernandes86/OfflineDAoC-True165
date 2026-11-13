# lead_tools

Helpers the True 1.65 content generators import (written for the 2026-11-12 lanes):

- `locks.py`: `edit_rules(lane, fn)` edits `true165-rules.json` in both copies (runtime/true165 and tools/claude-version/true165) under a lock, keeping its format; `lock(name)` for builds and test worlds.
- `tools.py`: the wall audit helpers (`is_reachable`, `nearest_reachable`, `ground_z`, `period_sightings`) over the client's zone walls (godot/content/zones/zoneNNN/bound.json) and the server navmesh.
- `detect_walls_v2.py`: lists every spawn, quest point and path point a player cannot reach behind the invisible zone walls.

Paths: `OFFLINE_DAOC_ROOT` (the install / dev tree, default four folders up), `OFFLINE_DAOC_GODOT_CONTENT` (the new client's godot/content), `OFFLINE_DAOC_TEST_WORLD` (a world database copy), `OFFLINE_DAOC_CLEAN_DB` (the clean world for generators). The period sightings index is part of the local period archive, not shipped.
