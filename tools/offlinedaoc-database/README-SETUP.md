# OFFLINEDAOC Database: setting it up on another PC (for people and for AI assistants)

A wowhead-style look-up tool built from the True 1.65 game's own data: item upgrades and where they drop (real drop
chances), monsters to hunt by level and con colour (with neighbour warnings and wide level ranges), aggressive /
neutral, dungeons, frontiers, epic dungeons and realm event bosses, camps on the zone maps, merchants and their stock,
quest kill steps, and the stable routes (Shrouded Isles flights and reputation included).

It is static files: `index.html` plus `app/` (plain JavaScript) read `data/*.js`, `maps/` and `icons/`. Nothing is
installed, nothing goes online, nothing in it changes the game. The Python builders in `source/` read the game
read-only (they refuse the Account and character tables) and write `data/`, `maps/` and `icons/`.

In the OfflineDAoC-True165 repository the tool lives at `tools/offlinedaoc-database/` WITHOUT its built `data/`,
`maps/` and `icons/` (they are made from the game files on your PC). Build them once, then open `index.html`.

## What you need
- Python 3.10+ with Pillow and numpy (`pip install pillow numpy`).
- A checkout of the OfflineDAoC-True165 repository (the builders port rules from the server C# source in
  `development-source/server/GameServer`).
- A True 1.65 game install (the release download): its `runtime/data/opendaoc-165.sqlite3.db` (the world),
  `runtime/server` (quests, language files, navmeshes), `runtime/true165` (content JSON) and `new-client/godot/content`
  (zone maps, item icons). Use a world that has been started once with START SERVER, so the True 1.65 content is
  applied, or the clean world from the release.

## Point it at your files
Each path comes from (first match) an environment variable, `source/paths.local.json`, or the default:

| Setting | Environment variable | paths.local.json key | Default |
|---|---|---|---|
| game install (holds `runtime/` and `new-client/`) | `OFFLINEDAOC_INSTALL` | `install` | the repository root |
| world database | `OFFLINEDAOC_DB` | `db` | `<install>/runtime/data/opendaoc-165.sqlite3.db` |
| server C# source | `OFFLINEDAOC_SERVER_SRC` | `server_src` | `<repo>/development-source/server` |
| runtime folder | `OFFLINEDAOC_RUNTIME` | `runtime` | `<install>/runtime` |
| new client's godot folder | `OFFLINEDAOC_CLIENT` | `client` | `<install>/new-client/godot` |

Easiest: create `source/paths.local.json` (it stays on your PC):

```json
{
 "install": "D:/Games/OfflineDAoC-True165",
 "server_src": "D:/src/OfflineDAoC-True165/development-source/server"
}
```

## Build and open
```
python -I source/build_all.py            # about 3 minutes; builds data/, maps/, icons/ and runs every check
python -I source/build_all.py --db <another world.sqlite3.db>   # a different world, e.g. a test copy
```
It ends with "built and checked". Then double-click `index.html` (or `Open OFFLINEDAOC Database.bat`). The About page
says which world it was built from and how every number is worked out.

Single domains: `python -I source/build_<maps|mobs|drops|vendors|routes|items|quests>.py [--db ...] [--out ...]`.
Checks: `source/verify_*.py`. The build contract (data shapes, the `WG.provide` page API) is `source/CONTRACT.md` and
`source/contract/`.

## For AI assistants working on it
- Read `source/CONTRACT.md` first, then `wgcommon.py` (paths, read-only DB guard, shared helpers).
- Never write to the game's database or files; builders only read. Rebuild with `build_all.py` after game content
  changes (the True 1.65 content generators live in `tools/claude-version/true165/`).
- Drop chances, con colours, prices and aggro follow the server code; when the server rules change, port the change
  into the matching builder and run its `verify_*.py`.
