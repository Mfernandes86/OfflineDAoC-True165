**Offline DAoC True 1.65 v0.7**: single-player Dark Age of Camelot as it was at patch 1.65 (Classic and Shrouded
Isles, November 2003), with a world full of gamebots, hired companions, and a new client built in Godot 4.

> **This is an AI-made project.** All of the code was written by AI tools; the project owner has no coding
> experience. Large parts of the game haven't been played through by a person. Expect bugs, and keep a backup of
> your save.

> **Releases are not on a regular schedule.** Expect potentially long gaps between releases. Bug reports and
> ideas are welcome in [Issues](https://github.com/zomboden/OfflineDAoC-True165/issues), and anyone who wants to
> continue development in a fork is free to do so, no need to ask me.

## What's new in 0.7

- **Quests you can finish:** an audit of every classic quest brought 2,084 checked fixes for quests that could not be
  completed, wrong places, missing or wrong rewards, and quest guide texts. Quest targets that walk a route show it
  on the map, and the level 50 epics follow the True 1.65 routes.
- **Gamebots that fight like veterans:** casters cast in a sensible order and stop bolting into melee, healers share
  heal claims, tanks peel, mezzers retry a resisted mez, groups pick roles by spec and call RUN from a lost fight.
  Class work for most classes, about 3,800 more banter lines (from chris4aker's Living Server), and bots that no
  longer stand on merchants and trainers.
- **Companions:** new AI Options rows (their own stance, crowd control, pet orders, an archer's weapon, song
  twisting and more), `/camp` hold points, and items dragged straight between a companion's bag and its paper doll.
- **Realm Exchange overhaul:** one row per item, a "usable by me" filter, new categories and sorts, and gamebots
  that price and buy with more sense.
- **Keeps and realm events:** 13 frontier keeps that had no lord can be taken again, horn raids have an end and can
  be called off, and the four Pennine keeps have their inner gates where the game draws them. These raid and siege
  fixes are tested in code but not yet seen in a live raid.
- **A smoother client:** the stutter in busy places is fixed (frames over 100 ms on Camelot's main road went from
  about 50 a minute to none), going berserk or into a shade no longer freezes the game, and a Hero's Stag form is
  drawn.
- **The 1.64 look:** every outdoor zone has the 1.64 sky and area fog (Muspelheim's red fog fades in as you walk in),
  and the original's invisible boundary walls are in.
- **Launcher:** a CRAFTING SKILL RATE option (1x to 10x), and the Player XP rate now counts in the frontiers, the
  battlegrounds and Darkness Falls.
- **Fixes from GitHub issues #42 to #53:** Spindelhalla's cobweb floors, water edges at Emain Macha, a healer
  restarting its cure, duplicate companions, quickbar slots that swapped styles, `/dfowner`, `/fly` across zones,
  and more.

The full list is in the [changelog](https://github.com/zomboden/OfflineDAoC-True165/blob/main/CHANGELOG.md).

## Already playing an earlier version? Update in place (about 3.0 GB)

1. Quit the game, click **STOP SERVER**, and close the launcher.
2. Under **Assets** below, download **`Download-OfflineDAoC-True165-v0.7.zip`** (a few KB: just the downloader),
   extract it, and double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If Windows SmartScreen appears, click
   **More info → Run anyway**.
3. Press **1 = Update my game**. It finds your `OfflineDAoC-True165` folder (or asks you to pick it), checks every
   file it will replace, and updates the game where it is. Your account, characters, companions, bots and settings
   stay; the files it replaces are backed up in `patch-backups\`. One update covers every earlier version, v0.1 to
   v0.6. (It was test-run from v0.2 through v0.6 this time; the v0.1 update is the same patch but was not test-run.)
4. Start the game as usual. The first **START SERVER** after the update brings your world up to date and can take a
   few minutes.

If it finds files that were changed on your PC, it changes nothing and can make a fresh install that brings your save
over.

## New players: the complete game (about 36 GB)

1. Download **`Download-OfflineDAoC-True165-v0.7.zip`** below and put it in a folder with a short path, such as
   `C:\Games\OfflineDAoC`. Right-click it, choose **Extract All…**, and open the extracted folder.
2. Double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`** and press **2 = New install**. It checks your PC, shows the
   download size and the folder it will use, and installs the game into a new folder, `OfflineDAoC-True165`. If it
   stops, run it again; it continues where it stopped.
3. Open that folder and double-click **`START OFFLINE DAOC TRUE 1.65.cmd`**. Click **START SERVER**, wait for
   **RUNNING**, then click **ENTER REALM**. Your account is created the first time.

You **don't** need to download the other files on this page by hand: the downloader fetches and checks them.

## What you need

- 64-bit Windows 10 or 11, with a CPU that supports **AVX2** (most from 2014 onward). The downloader checks.
- A graphics card that supports **Vulkan**, with a current driver.
- 16 GB RAM recommended, and about 49 GB of free disk space for a new install.
- Nothing to install or turn on in Windows: .NET comes with the game, and the Windows ".NET Framework 3.5" feature,
  the Visual C++ Redistributable and an original DAoC client are **not** needed.

## More

- The full guide with troubleshooting: [docs/PLAY.md](https://github.com/zomboden/OfflineDAoC-True165/blob/main/docs/PLAY.md)
- Bugs: [open an issue](https://github.com/zomboden/OfflineDAoC-True165/issues). Say what you were doing, and
  attach the logs from `new-client\save\Godot\app_userdata\MMOEngine\logs` (and `spell_log.txt`, `death_log.txt` there) if you can.
- The source code, and the guides for developers and AI assistants, are in this repository; start with the
  [README](https://github.com/zomboden/OfflineDAoC-True165#readme).

Dark Age of Camelot is the property of Electronic Arts and Broadsword. This is a free, non-commercial fan project,
not affiliated with them.
