# Keep gates in the server navmesh

`keepgate.py` seals keep gatehouses in a server navmesh (`runtime\server\navmesh-165\zoneNNN.nav`) by changing
polygon flags only, the way `navmesh_shadow_islands/shadowfix.py` does. A disabled polygon gets the flags value
`Disabled` (0x10) and nothing else, so the server's default filter (include `All ^ Disabled`) never walks or snaps
onto it, and the door code (Door mask) never registers it. Every other byte of the file stays as it was.

```
python keepgate.py <zone id> <in.nav> <out.nav> <world.sqlite3.db>   # find, patch, verify (prints every step)
python keepgate.py <zone id> <in.nav> - <world.sqlite3.db>           # find + verify only
python seal_all.py <navmesh folder> <out folder> <world.sqlite3.db> [zone ...]   # every keep zone of a folder
```

**Run `seal_all.py` after ANY navmesh rebuild or splice** (BuildNav output, `navmesh_splice`, a keep rebuild like
nav164's), before regenerating roads/seams. The Recast build itself makes these leaks, so a rebuilt map brings them
back unless this step runs.

Needs Python 3.10 with numpy and scipy, the Godot client's converted zone models
(`OFFLINE DAOC MMOENGINE GODOT\godot\content\zones\zoneNNN`, keep collision `collision_*` nodes, placed as
`game/zone_loader.gd` places them) and a world database (read only: Zones, Door, Keep, Mob, ZonePoint, Teleport,
BindPoint rows).

Two kinds of polygon are disabled per keep. Each is accepted only when it closes a leak, leaves every open-door walk
of the keep's points as it was, and cuts off nothing but a small dead-end patch (at most 40,000 sq units, whose only
way out ran through the disabled polygons; it is disabled too, so a floor snap cannot strand a bot on it).

1. **Inside the keep's solid.** These are ground polygons with at least half of their 16-unit sample points inside the
   keep's client collision, 25 and 50 units above the floor. The test is four-direction ray parity on horizontal
   slices (`keepsolid.Solid`). The bfrontkeep1 gate passage floor is a top face only, about 64 units over the terrain.
   Recast (agent height 57.6) walks the terrain under it and inside the closed gatehouse blocks, past both shut gates.
2. **Unregistered door slivers.** These are Door-flag polygons in a doorway that the server's keep-door box does not
   reach (`LocalPathfindingMgr.RegisterDoor`: 64, or 120 in a battleground). With the door shut, a bot walks round the
   registered polygons on them. The open doorway still passes through the registered ones.

A leak is a shut-door case (with the registered door polygons) that reaches a keep point the ideal seal of the same
case holds. Keep points are the lord, the guards and the keep row. The ideal seal takes out every inside-solid
polygon, every sliver and the doorway-wide door polygons. Points the ideal seal still reaches stand outside, or behind
another open gate of a relic keep.

- **Finding** uses Detour's own tile links plus the looser older rule (edge mid heights within 2 recast units), so a
  borderline link cannot hide a leak.
- **Verification** (`VERIFY` / `ROW` lines) uses exactly the links Detour makes at load: `navkit._overlap_slabs`,
  a copy of `overlapSlabs` (heights cross, or within 2 x walkableClimb at either end of the overlap).
- `navmesh_splice/navcomp.py` joins tiles only within climb + 16 at the edge middle. That is stricter than Detour, so
  it can report a leaking gate as sealed. Its zone 14 keep check of 2026-11-12 did exactly that at Caer Berkstead.

Applied on 2026-11-12 to the shipped navmesh-165 files (the outputs were installed into `runtime\server\navmesh-165\`):

| Zone | Input MD5 | Keeps | Disabled | Output MD5 |
| --- | --- | --- | --- | --- |
| 250 Caledonia | 92b65f45b5a16841ff9c47a250ccb9c4 | Caer Caledon (inside 18), Hib portal keep (slivers 2) | 20 | a42b3f9e45f6d68f7897f94464d38c51 |
| 251 Murdaigean | 9cd66b94f14eee2c9ad9fafcfcbfb903 | Hib portal keep (slivers 2) | 2 | 5a190dca7b1f13883716a5a453b60c65 |
| 253 Abermenai | fa1a9bb448c3c08f5cfffb7abef23206 | Dun Abermenai (slivers 2), Hib portal keep (slivers 2) | 4 | d6722492d9d5714c0fdffc29158c5d3b |
| 14 Pennine Mountains | ce1e2a71a32df320eac37f169b1bb784 | Caer Berkstead (24), Caer Boldiam (17) | 41 | 33e9a76f47a63445e4645f9a86cade9a |
| 15 Forest Sauvage | b911a8027b21f9fc4fe6d3c5b666e51e | Caer Benowyc (19) | 19 | 9c47a6f372a8d1e8beeb5bc792ece550 |
| 113 | dff1b49f5b4de4249f276d9226178664 | Glenlock Faste (sliver 1) | 1 | 1a27c5ed8586dc23b49bde4db94280ca |
| 115 | 9d07d1890a9539afcea651dcf477c4e7 | Albion portal keep (18) | 18 | 2c3a79b9a7496549f25db78ab47e860b |
| 212 | dc3ac9e253b21ac6144a5664abd1cc08 | Dun Bolg (sliver 1), Dun nGed (sliver 1) | 2 | e2a81b1147e6e7308a371fcceea7bcb4 |
| 214 | 3d774da3a71d820fde64e6bc7046ac88 | Albion portal keep (20) | 20 | 094efb22feb1a7fac25b11b0bff2f42f |
| 12 | dce7286a35c55691fb888d8f9fbb22d8 | Castle Myrddin (slivers 4: gates 01/02) | 4 | 7adf14a7ab15b18c9cae50964acd7bbe |
| 112 | 26a22e8207193a010218479b8890dd4e | Grallarhorn Faste (sliver 1: gate 02) | 1 | 851c97984cdee2aced2cc5c8efdfedf9 |

Every other keep zone (0, 11, 102, 111, 202, 206, 207, 210, 211, 252) needs nothing. Keeps with more than two doors
are also tested with each door open and all the others shut (as the server keeps them), which found the relic keep
slivers; realm-gate rows (Door.Realm 1-3) are left to UT_GateNavmeshInstalled165.

Check with the real Detour library (server stopped): `UT_BattlegroundKeepGateNavmesh165` (Explicit, regions 1, 100, 200
and 250-253; `OFFLINE_DAOC_NAV_ROOT` = a folder with `lib\Detour.dll` and `navmesh-165\`, `OFFLINE_DAOC_DB_PATH` = a
world). The flags do not change `navcomp` area ids, so `seams.json` stays valid. Eight `roads.json` legs in zones 14,
115 and 212 run past disabled polygons; regenerating roads for those zones (`navmesh_splice/README.md`) re-proves them.

`keepsolid.py` reads the new client's zone folders: set `OFFLINE_DAOC_GODOT_ZONES` to your `godot/content/zones` if it is
not at the default `source/new-client/godot/content/zones` beside the dev tree.
