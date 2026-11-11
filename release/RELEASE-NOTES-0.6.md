**Offline DAoC True 1.65 v0.6**: single-player Dark Age of Camelot as it was at patch 1.65 (Classic and Shrouded
Isles, November 2003), with a world full of gamebots, hired companions, and a new client built in Godot 4.

> **This is an AI-made project.** All of the code was written by AI tools; the project owner has no coding
> experience. Large parts of the game haven't been played through by a person. Expect bugs, and keep a backup of
> your save.

> **Releases are not on a regular schedule.** Expect potentially long gaps between releases. Bug reports and
> ideas are welcome in [Issues](https://github.com/shadowofze/OfflineDAoC-True165/issues), and anyone who wants to
> continue development in a fork is free to do so, no need to ask me.

## What's new in 0.6

- **Companions that do what you need:** `/passive` makes your companions and their pets stop fighting and just follow
  you, for retreating or running through an area; the group window's stance button cycles AGGRESSIVE, DEFENSIVE and
  PASSIVE. `/grind` looks about 25% further for monsters. New AI options: healers that can melee (Shaman, Cleric,
  Druid, Healer, Friar) can "Melee and heal, no attack spells", and casters can use "Save power: Strict" and "Attack
  spells: Area spells only / None - buffs only" (a Runemaster on None keeps its bladeturn up and saves its power).
  Automatic stays as it was.
- **/stuck** (player issue #30): after a confirmation your character dies where it stands, so you can release to your
  bind stone or a city. That death costs no experience, Constitution or resurrection sickness. Not in combat.
- **Gamebots that talk and look the part:** a banter library of more than 3,000 lines with replies, dyes and enamels,
  and coloured names (on by default) that show them light blue like players from the moment they appear.
- **Gamebot groups that work:** the puller gets its turn at camp again, healers and tanks join more often, meetups
  happen where everyone can reach, groups leave places where much stronger monsters keep killing them, healers walk
  over to raise fallen members, low-level bots take safer horse landings, and bots stop re-buying gear they own.
- **XP rate options are back:** Player XP rate and Gamebot XP rate in the launcher's Options tab (1x to 10x, 1x by
  default). Your hired companions use the player rate.
- **Fixes from GitHub issues #28 and #31 to #39:** housing zone maps and buyable lot deeds, a clear "no map" message,
  merchant quantity steps of 10, wall collision for the Jordheim assembly hall and 15 more buildings, realm ability
  text, Spindelhalla's stray merchant and Te'Bui, 933 loot drops that now stack, seal prices shown in seals,
  companions that follow you through Darkness Falls, thinner Darkness Falls spawns, and no useless pet buffs.
- **More fixes:** a shape-changing monster stays targeted, monsters bring their friends on every pull again, seven
  invisible quest monsters have bodies, horse routes over the Innis Carthaig bridge stay on the deck, your character
  stays in the saddle on hills, and monster walk cycles play in full.

The full list is in the [changelog](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/CHANGELOG.md).

## Already playing an earlier version? Update in place (about 3.0 GB)

1. Quit the game, click **STOP SERVER**, and close the launcher.
2. Under **Assets** below, download **`Download-OfflineDAoC-True165-v0.6.zip`** (a few KB: just the downloader),
   extract it, and double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If Windows SmartScreen appears, click
   **More info → Run anyway**.
3. Press **1 = Update my game**. It finds your `OfflineDAoC-True165` folder (or asks you to pick it), checks every
   file it will replace, and updates the game where it is. Your account, characters, companions, bots and settings
   stay; the files it replaces are backed up in `patch-backups\`. One update covers every earlier version, v0.1 to
   v0.5.
4. Start the game as usual. The first **START SERVER** after the update brings your world up to date and can take a
   few minutes.

If it finds files that were changed on your PC, it changes nothing and can make a fresh install that brings your save
over.

## New players: the complete game (about 36 GB)

1. Download **`Download-OfflineDAoC-True165-v0.6.zip`** below and put it in a folder with a short path, such as
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

- The full guide with troubleshooting: [docs/PLAY.md](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/docs/PLAY.md)
- Bugs: [open an issue](https://github.com/shadowofze/OfflineDAoC-True165/issues). Say what you were doing, and
  attach the logs from `new-client\save\Godot\app_userdata\MMOEngine\logs` (and `spell_log.txt`, `death_log.txt` there) if you can.
- The source code, and the guides for developers and AI assistants, are in this repository; start with the
  [README](https://github.com/shadowofze/OfflineDAoC-True165#readme).

Dark Age of Camelot is the property of Electronic Arts and Broadsword. This is a free, non-commercial fan project,
not affiliated with them.
