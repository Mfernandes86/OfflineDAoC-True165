# Getting started on a fresh Windows 11 PC

This guide starts from a newly installed Windows 11 PC with no Dark Age of Camelot client and no development
tools. It takes you from that PC to playing, then covers what works, what doesn't, and what's worth knowing before
you put hours into a character.

It's written for **v0.7**, the current release (published 14 November 2026). Where this guide adds advice of its
own rather than repeating the project's docs, it says so ("Tip"). The official short versions are
[PLAY.md](PLAY.md) and [NEW-CLIENT.md](NEW-CLIENT.md).

---

## 1. What you're installing

Offline DAoC True 1.65 is a **single-player** Dark Age of Camelot that runs entirely on your PC. It recreates the
game as it was at **patch 1.65** (Classic plus Shrouded Isles, November 2003) and fills the world with
**gamebots**: AI players that level, group, craft, trade, siege keeps and raid relics on their own.

It has four parts. All four come in one download, and you never install them one by one:

| Part | What it is |
|---|---|
| **Launcher** (`OfflineDAoC.exe`) | A Windows app. It starts and stops the server and opens the game. It also holds the server-side settings and the bot dashboard. |
| **Server** | A modified OpenDAoC server (C#/.NET), with the world in a local SQLite database. It runs on your PC and listens only on `127.0.0.1`. |
| **New client** | A game client built in Godot 4 (Vulkan) for this project. It draws the 1.64/1.65 look from content converted from the original game's files. |
| **Bundled .NET** | Its own copy of .NET 10, in the game folder. Nothing is installed in Windows. |

**You do not need:**
- an original DAoC client or a DAoC account;
- the Windows ".NET Framework 3.5" feature;
- the Visual C++ Redistributable;
- Git, Python, or any developer tools (unless you want to modify the game; see [section 14](#14-optional-working-on-your-fork)).

---

## 2. Check your PC first

| Requirement | Details |
|---|---|
| **Windows** | 64-bit Windows 10 or 11. Windows 11 is fine. |
| **CPU with AVX2** | **Required.** The bots' navigation needs it. Nearly every Intel or AMD CPU from 2014 onward has it, and the downloader checks it for you. Without AVX2 the game can't run on that PC. |
| **Graphics card with Vulkan** | Needs a current driver. It's developed and tested on an NVIDIA RTX 2060 (6 GB). Without Vulkan, the client falls back to DirectX 12. |
| **Memory** | 16 GB recommended. The downloader warns you below 15 GB. |
| **Disk** | About **49 GB free** for a new install (about 43 GB once installed), on a drive where you can use a short path such as `C:\Games`. |
| **Internet** | About **36 GB** to download, in 24 verified parts. The download resumes if it stops. |

**Tip:** to see your CPU and graphics card on Windows 11, open **Task Manager** (Ctrl+Shift+Esc), then
**Performance**. Choose **CPU** for the processor name and **GPU** for the graphics card. An SSD isn't a stated
requirement, but with 37 GB of game content it will load much faster than a hard disk.

---

## 3. Prepare the fresh Windows 11 PC

Do these once, before you download anything.

### 3.1 Bring Windows up to date
**Settings → Windows Update → Check for updates.** Install everything, including the optional driver updates, and
restart.

### 3.2 Install your graphics card's own driver
Windows Update installs a basic graphics driver. Install the latest one straight from your card's maker instead, so
you have full Vulkan support:
- **NVIDIA:** nvidia.com → Drivers (or the NVIDIA App).
- **AMD:** amd.com → Drivers & Support (Adrenalin).
- **Intel:** intel.com → Driver & Support Assistant.

Restart after you install it. The downloader then checks for Vulkan (it looks for `vulkan-1.dll` in
`C:\Windows\System32`).

**Tip for laptops with two GPUs** (integrated plus NVIDIA or AMD): after installing, go to **Settings → System →
Display → Graphics** and set the game's engine program (in `new-client\tools\godot_net\` in the game folder) to
**High performance**. Then it runs on the stronger GPU.

### 3.3 Show file extensions
**Tip:** in File Explorer, click **View → Show → File name extensions.** This makes it easy to tell
`GET-OFFLINE-DAOC-TRUE165.cmd` from `READ ME.txt`.

### 3.4 Make a short game folder outside OneDrive
Create **`C:\Games\OfflineDAoC`**. Avoid these places:
- **OneDrive.** On a fresh Windows 11 PC, Desktop and Documents are often synced to OneDrive. OneDrive can lock or
  move the game's files while it runs, and the downloader warns you if it sees OneDrive in the path.
- **Program Files** (it needs admin rights).
- **Deep folders.** The game folder's own path should be **42 characters or fewer**. The client keeps its graphics
  (shader) cache up to 200 characters deeper, Windows refuses paths over 260, and a long path means the shaders
  recompile on every start.
- **Paths with non-English letters.**

### 3.5 Know about SmartScreen (and Smart App Control)
The game's files are **unsigned community builds**, so Windows will be wary of them:
- **SmartScreen** ("Windows protected your PC"): click **More info → Run anyway**. This is expected.
- **Microsoft Defender** may flag or quarantine an unsigned game file. The project's advice is to restore the file
  and allow the game folder (**Windows Security → Virus & threat protection → Manage settings → Exclusions**).
  Allowing a folder is a security trade-off, and it's your call.
- **Tip, Smart App Control:** some fresh Windows 11 installs turn on *Smart App Control*, which blocks unsigned
  programs outright, with **no "Run anyway" button**. The project's docs don't mention it. If a block message names
  Smart App Control, the only way past it is turning Smart App Control off (**Windows Security → App & browser
  control → Smart App Control settings**). On some Windows 11 versions it can't be turned back on afterwards without
  resetting Windows, so read what Windows tells you before you decide.

---

## 4. Download and install

1. Open the latest release: **https://github.com/zomboden/OfflineDAoC-True165/releases/latest**
2. Under **Assets**, download only **`Download-OfflineDAoC-True165-v0.7.zip`** (about 10 KB, just the downloader).
   You don't download the big `...-part01.zip` files by hand; the downloader fetches and checks them.
   > The green **Code → Download ZIP** button gives you the *source code*, not the playable game.
3. Move the zip into `C:\Games\OfflineDAoC`.
4. **Right-click the zip → Extract All… → Extract.** Don't run anything from inside the zip; if you do, the
   downloader tells you to extract it first.
5. Open the extracted folder (it may be one folder deeper than you expect, because the zip has its own
   `Download-OfflineDAoC-True165-v0.7` folder inside). Double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If SmartScreen
   appears, click **More info → Run anyway**.
6. A console window checks your PC (64-bit Windows, AVX2, Vulkan, memory) and asks what to do:
   - **1 = Update my game**
   - **2 = New install**

   Press **2**.
7. It shows the folder it will install into, the download size (about 36 GB in 24 parts) and the installed size, then
   asks **Start the download? (Y/N)**. Press **Y**.
   - It puts the game in a new folder named **`OfflineDAoC-True165`**, next to the downloader. If that path would be
     longer than 42 characters, it uses **`C:\Games\OfflineDAoC-True165`** instead. Either is fine.
   - It never overwrites an existing folder.
8. Wait. Every part is checked (SHA-256) and then unpacked. If the download stops, or you close the window, run the
   same `.cmd` again: finished parts are kept and it continues from where it stopped.
9. At the end it offers a **desktop shortcut**; press **Y** if you want one. It clears its own download cache when
   it finishes, and once it says the game is installed you can delete the downloader folder.

Your installed folder looks like this:

```
OfflineDAoC-True165\
  START OFFLINE DAOC TRUE 1.65.cmd   <- start here
  READ ME FIRST.txt
  QUICK COMMANDS.txt                 <- everyday commands
  ALL SERVER COMMANDS.txt            <- every command (GM-only ones marked)
  CHANGELOG.md, docs\
  runtime\                           <- launcher, server, world database (your save)
  new-client\                        <- the game client, its engine, and your client settings (new-client\save)
  tools\dotnet\                      <- the bundled .NET
```

---

## 5. First launch

1. Double-click **`START OFFLINE DAOC TRUE 1.65.cmd`** (or the desktop shortcut). The launcher opens and shows
   **VERSION 0.7**.
2. Click **START SERVER**. The server card says **STARTING…**, then **RUNNING**. The first start sets up the world
   and can take a few minutes. **ENTER REALM** stays greyed out until the server is ready.
   - **Tip:** if a Windows Firewall prompt appears for the server, it's safe to allow it on private networks or to
     cancel. The server only listens on your own PC (`127.0.0.1`), so it never needs outside access.
3. Click **ENTER REALM**. The client opens and logs you in. **Your local account is created automatically the first
   time**, with no sign-up and no password to choose.
4. You go through the period 1.64 pre-game screens:
   - **Choose Your Realm:** Albion, Midgard or Hibernia. Each realm has **8 character slots**.
   - The standing stones, then the **one-page creation**. You choose race, gender and **base class** (you choose
     your final class at a trainer at level 5), spend your own **stat points**, and choose height, face and hair.
5. You start in your race and class's period starting town, with a starting weapon and a training shield.

**To quit properly:** quit the game (`/quit`), then click **STOP SERVER** in the launcher and **wait for it to
save** before you close the launcher. Only one server can run at a time.

---

## 6. The launcher, in detail

### The cards along the top
- **SERVER:** stopped, starting, running or stopping.
- **ONLINE BOTS:** how many gamebots are logged in right now.
- **ALBION / MIDGARD / HIBERNIA:** each realm's live gamebot count, with a **population goal** box. Click the box,
  type a number and press **Enter**. It saves at once, applies while the server runs, and is kept until you change
  it.
- **BOT AI DELAY:** the average time the server spends on bot and monster AI each game tick, over the last minute.
  Lower is better. Watch it when you raise the bot goals.

> **Heads-up: the docs inside your v0.7 game folder are out of date here.** Their copies of `READ ME FIRST.txt`,
> `docs\PLAY.md` and `QUICK COMMANDS.txt` still describe a **"+ Lv.1" button** (Shift = 10, Ctrl = 100). That button
> was **removed in v0.5** and replaced by the population goal boxes above. The repository's copies are corrected
> and reach the game folder with the next release.

### The tabs
| Tab | What it shows |
|---|---|
| **Active Population** | The gamebots online now, with their class, level, zone and activity. **SEARCH SAVED** finds offline ones, and you can delete one bot or **DELETE ALL BOTS…**. |
| **World Population** | Goals, live and saved counts per level bracket, and **Advanced settings** (the level-50 share, 35% by default, and the per-bracket minimum). |
| **Active Groups** | Bot groups, their leader and puller, and their meet-up timers. |
| **Active RvR** | What the frontier armies are doing: sieges, escorts, relic raids. |
| **Battlegrounds** | The battleground population share (0 by default). |
| **Realm Events** | Raids and great hunts that are running, and the bots assigned to them. |
| **Realm Records** | The history of realm events. |
| **Options** | See below. |
| **Bot Goals Setting** | What the gamebots aim for. |

### Options
| Option | What it does | Default |
|---|---|---|
| **Make Me a GM** | Gives your account GM powers at the **next server start**: `/tele`, `/tc`, `/spawn`, `/level` and the rest, plus "Teleport to" on a bot. With `/gmstealth` on, monsters ignore you (type `/gmstealth off`). This breaks the period experience, so treat it as a debug or cheat switch. | Off |
| **RvR and battleground announcements** | Screen and chat messages when keeps and relics fall or sieges start. | On |
| **PvE realm event announcements** | Messages when raids form and when final bosses fall. | On |
| **PLAYER XP RATE** | 1×, 2×, 3×, 5× or 10×. It applies to you and your hired companions, everywhere, the frontiers, battlegrounds and Darkness Falls included. | 1× (original) |
| **GAMEBOT XP RATE** | The same choices, for the autonomous gamebots. | 1× |
| **CRAFTING SKILL RATE** | 1× to 10× skill points per crafting gain, for your characters only. | 1× |

The XP and crafting rates can only be changed while the **server is stopped**.

---

## 7. Bots: how many, and what to expect

- **A new world starts with a goal of 500 gamebots per realm (1,500 in all).** Every gamebot is simulated by the
  server on your PC. **Tip:** if the game stutters or **BOT AI DELAY** keeps climbing, lower the goals. They apply
  live and need no restart. Bots log out gradually (at most two a second), and each finishes what it's doing first.
  The project's advice is to start small and add more as your PC allows.
- **Every gamebot starts at level 1** and levels in the world by the same rules as you. That means **a new world has
  no level-50 gamebots**: frontier RvR, keep sieges, relic raids and Horn of Rallying raids stay quiet until the
  population has levelled. Raising **GAMEBOT XP RATE** gets them there sooner.
- Bots live by the player rules. They rest at player regeneration rates, walk to their loot, buy and repair gear,
  buy ammo, suffer death penalties, and travel by foot and horse routes, with no teleports. They also chat on the
  period channels, look for groups (`/lfg`), answer questions, and about 1 in 5 of them craft.
- Gamebots log in and out like real players, and new level-1s keep arriving.

### Gamebots vs. hired companions (two different things)
| | **Gamebots** | **Hired companions** |
|---|---|---|
| Who controls them | Their own AI. They live their own lives. | You. They're your group. |
| How you get them | The launcher's population goals | Hired at your realm's traveler's inn |
| Count | Hundreds to thousands | Up to 7 (a full group of 8 with you) |
| Levels | Level themselves from 1 | Join at 5 and level with you, stopping at your level |

---

## 8. Your first hours in game

### Levels 1-5
- **Fight, quest and do tasks.** `/task` gives period tasks for levels 1-19: target a named guard (a Captain, a
  Sir…) for a kill task, or another townsperson for a delivery. Your task's target gets a violet marker.
- **Quests** have markers, objective highlights and a period walkthrough: `/questguide` (or `/questguide X` for
  quest X). `/quest` lists your quests.
- At **level 5**, visit your class trainer to **promote** to your final class.

### Level 5: companions
1. At level 5 a **courier** brings you the quest **"The Traveler's Inn."**
2. Go to your realm's inn: **near Caer Witrin (Albion), near Huginfell (Midgard), near Connla (Hibernia).**
3. Turn the quest in at the **innkeeper** (right-click), who opens the **hiring window**.
4. **Your first companion is free**; later ones cost coin. New slots open at levels **5, 10, 15, 20, 30, 40 and
   45**.
5. You choose each companion's race, class, gender, look, height and name. It joins at level 5 with a training
   weapon. You give it gear, train its specs (with a free respec) and pick its realm abilities.
6. Companions never loot, and they stop at your level. Any healer of your realm revives them for free, and a smith
   repairs their gear for free.

Useful companion commands (see `QUICK COMMANDS.txt` for more):
- `/aggressive`: they attack what you attack. This is the default.
- `/defensive`: they stay near you and fight what comes within about 350 units.
- `/passive`, and `/camp` to set a hold point.
- `/pull`, or `/pull <name>`: your tank (or the named companion) pulls your target. Make it a quickbar button with
  `/macro pull /pull Isolde`.
- `/charm`, or `/charm <name>`: a charm-capable companion charms your target.
- Right-click a companion to open its page (gear, stats, spec, realm abilities). Drag an item onto it to equip it.
- `/classes` lists your realm's classes and their party roles.

### Getting around
- **No click teleporters, no personal horses, no bind recall stone.** You travel on foot or by the
  **stablemasters' horse routes.** `/stables` lists them, and `/stables classic` or `/stables si` jumps to that set.
- The Shrouded Isles entrances are the 1.64 client's jump boxes (Avalon Marsh, Gotar/Nalliten, Shannon Estuary).
- `/loc` shows your position, **M** opens the map, and **Shift+M** opens the realm war map.

### Money, gear and death
- **Coin belongs to each character**, not the account. Each realm also has an **account vault keeper** in its
  capital (Camelot, Jordheim, Tir na Nog), shared by your characters in that realm. That vault is a convenience
  addition; 1.65 didn't have one.
- **Gear wears out** and a smith repairs it for coin. **Arrows, throwing weapons and poisons are used up**; buy them
  or craft them.
- **Death costs Constitution** (on a PvE release) and gives resurrection sickness, as in 1.65.
- **Songs need their own instrument** (drum, lute or flute).
- **Loot falls at the monster's feet** for you to pick up (**G**, or **F7** for the nearest loot), unless you turn on
  Auto Loot (see [section 9](#9-the-client-controls-and-settings)).

### Trading: the Realm Exchange
An auction house in a small building by each inn, where you trade with the gamebots. Right-click the broker:
**Browse the exchange / Manage my listings / Claim sale proceeds.** Bots list what they craft and loot, and buy from
you. Only crafted and spellcrafted gear, potions, poisons, period drops and Darkness Falls items can be listed.

### Crafting
`/craft` opens the tradeskill window. The recipes and success rates are the 1.64 client's, but **the crafting system
itself deliberately differs from 1.65**: you can craft anywhere without tools, and MAKE buys the materials you're
missing. There's also an optional **crafting assistant** and a **Craft All** button (both off by default, under
Options → INTERFACE → CRAFTING).

### Level 50: the Horn of Rallying
At level 50 a messenger brings **"The Horn of Rallying."** Turn it in at the inn's Horn Handler, then right-click the
horn to start a **great hunt**: a dragon, an epic dungeon, Summoner's Hall or Darkness Falls. `/horn list` shows the
hunts. These PvE raids **never start on their own.** Keep sieges and relic raids, on the other hand, run by
themselves and also start when you attack a keep.

---

## 9. The client: controls and settings

### Default keys (RPG preset)
| Action | Key |
|---|---|
| Move / turn / strafe | **W A S D**, **Q / E** |
| Jump / sit / sprint | **Space** / **X** / **V** |
| Auto-run (run lock) | **NumLock** |
| Attack mode | **`** (backtick) |
| Nearest enemy / last attacker | **Tab** / **Shift+Tab** |
| Consider (con) the target | **F6** |
| Target group members | **F1-F4**, **Shift+F1-F4** |
| Pick up / nearest loot | **G** / **F7** |
| Chat / reply | **-** / **R** |
| Stats / inventory / skills / spells | **C** / **I** / **P** / **K** |
| Group / map / quest journal | **O** / **M** / **J** |
| Hide the interface | **Ctrl+Z** |
| Quickbars | **1-0**, **Alt+1-0**, **Ctrl+1-0** (Shift+1-0 switches quickbar 1's bank) |

- **Mouse look** is off by default. Turn on **Options → CONTROLS → MOUSE LOOK TOGGLE**; then a right-click on open
  ground toggles it, and Esc turns it off.
- **Options → CONTROLS** also has **FPS** and **MMO** key presets, and every action can take two bindings.
- The full list is in `source/new-client/docs/CLIENT COMMANDS AND KEYS.txt` in the repository.

### Options worth setting on day one
- **VIDEO:** resolution, window mode (windowed, fullscreen or borderless windowed), frame-rate cap, an **FPS
  target** (detail drops only while you're below it, for example in raid crowds), a quality preset with a
  "recommended for this PC" hint, view distance, shadows, lighting, anti-aliasing, textures and grass.
- **INTERFACE:**
  - **LOOT → AUTO LOOT** (off by default; a quality-of-life option that isn't in 1.65);
  - **CRAFTING** (Craft All, crafting assistant);
  - **GRINDING** (`/grind`, where your companions pull for you; off by default);
  - **BOTS** (show companion and gamebot names; show your realm's bots as green dots on the map).
- **CHAT:** two separate boxes, combat on top and chat below.

Your settings, key bindings and logs live in `new-client\save`. Delete that folder to reset the client to its
defaults.

---

## 10. What works (supported)

| Area | Status in v0.7 |
|---|---|
| **The world** | The Classic and Shrouded Isles world, including the three islands added in 1.65 and the period Darkness Falls and SI entrances. |
| **Classes** | All 39 Classic and SI classes. **Albion (14):** Armsman, Cabalist, Cleric, Friar, Infiltrator, Mercenary, Minstrel, Necromancer, Paladin, Reaver, Scout, Sorcerer, Theurgist, Wizard. **Midgard (12):** Berserker, Bonedancer, Healer, Hunter, Runemaster, Savage, Shadowblade, Shaman, Skald, Spiritmaster, Thane, Warrior. **Hibernia (13):** Animist, Bard, Blademaster, Champion, Druid, Eldritch, Enchanter, Hero, Mentalist, Nightshade, Ranger, Valewalker, Warden. |
| **Character creation** | The 1.64 pre-game: a base class at creation, promotion at level 5, your own stat points, and height, face and hair. 8 slots per realm. |
| **Combat, spells and styles** | The OpenDAoC systems, with 1.64 spell visuals. |
| **Quests** | Classic quests, after a 2,084-fix audit in 0.7. There are quest markers, objective highlights and `/questguide`, and the level-50 epics follow the 1.65 routes. |
| **Tasks** | Period `/task` for levels 1-19. |
| **Loot** | From period sources only (Allakhazam's period pages, CapnBry's radar data, Uthgard's drop rates). |
| **Crafting** | The 1.64 recipe list, the period success rates, salvage and spellcrafting (with the system-level differences above). |
| **Housing** | The period housing zones, houses, rent and the 1.64 housing windows. |
| **Travel** | Stablemaster horse routes, the SI jump boxes, and frontier portal masters. |
| **RvR** | The frontiers, keep sieges (siege engines are crafted, not bought), relic raids, and the period battlegrounds: Abermenai (15-19), Thidranki (20-24), Murdaigean (25-29), Caledonia (30-35). |
| **PvE raids** | The dragons, epic dungeons, Summoner's Hall and Darkness Falls, through the Horn of Rallying. |
| **Gamebots** | A living population that levels, groups, crafts, trades, chats, sieges and raids. |
| **Companions** | Up to 7 hired, persistent, fully geared and specced companions. |
| **Realm Exchange** | Trading with the gamebots. |
| **Updates** | In place, keeping your save, with a backup of every file they replace. |

---

## 11. What's different from real 1.65 (on purpose)

These deviations are deliberate. Everything else aims to match 1.65.

- **The gamebots, the hired companions and the Realm Exchange.** These are the project's core additions.
- **The crafting system:** crafting works anywhere, needs no tools, and MAKE buys missing materials, plus the optional
  assistant and Craft All.
- **Quest markers, objective highlights, `/questguide`** and the task target marker.
- **An account vault per realm**, at a vault keeper in each capital.
- **Boss changes that let bots finish raids**, and faster respawns for named and quest monsters.
- **Optional, off by default:** auto loot, `/grind`, and the 2×-10× XP and crafting rates.
- **Cheat and convenience commands are GM-only** (`/tele`, `/tc`, `/level`, `/spawn`, `/raid`, `/buff`, `/mobs`…).
  A normal player gets *"That command does not exist in True 1.65 Mode."*
- A few quest and boss reward items had no surviving stats in any source, so they were given invented ones. They
  are listed in `source/new-client/docs/1.65 EXCEPTIONS MADE.txt`.

---

## 12. What isn't there (not supported)

| Not included | Why or what instead |
|---|---|
| **Trials of Atlantis and everything later** (Catacombs, Darkness Rising, Labyrinth of the Minotaur, New Frontiers) | Out of scope. That means no ToA regions, artifacts, master levels or champion levels, no post-1.65 classes or races, and no ToA bonus types. |
| **Multiplayer, LAN, or friends joining** | The server listens only on `127.0.0.1`. The project says a public server would need its own security work. |
| **macOS or Linux** | Windows 10/11 64-bit only (the launcher is a Windows app). |
| **Gamepads and controllers** | The client has no controller support; it's keyboard and mouse only. (Tip: this is from reading the client's code. The docs don't mention controllers.) |
| **Playing through the original DAoC client** | Only the new client works. |
| **Importing old characters** | True 1.65 is a fresh start, with no save import from the original Offline DAoC or anywhere else. |
| **Click teleporters, hasteners, personal horses, the bind recall stone** | Removed as post-1.65 or freeshard additions. Travel by horse routes. |
| **Freeshard (Atlas/Phoenix-era) items, merchants and dailies** | Removed. |
| **`/level`, free levels, `/tele`, `/tc` for normal players** | GM-only (use the launcher's *Make Me a GM* if you really want them). |
| **Cathal Valley and the New Frontiers battlegrounds** | Not in 1.65. |

---

## 13. Know before you play

1. **This is an AI-made hobby project.** The owner directed the AI tools and play-tested, and **large parts of the
   game haven't been played through by a person.** Expect bugs.
2. **Back up your save regularly** (see below), especially before an update or a mod.
3. **A new world fills slowly at the top.** Bots start at level 1, so the frontiers and raids feel empty at first.
   The 0.7 raid and siege fixes are "tested in code but not yet seen in a live raid" for exactly that reason.
4. **Quests, epic quests and epic armour are still being brought to their 1.65 versions.** If one breaks, check
   `/questguide`, and report it. **Housing** was checked offline when it was added (0.2) but hasn't been played
   through on a live server.
5. **Difficulty is the original's.** Monster levels, HP, damage and XP are never "buffed for playability." Solo play
   can be hard at times; that's what companions are for.
6. **Releases have no schedule.** Gaps of weeks or months are normal. 0.8 is already in progress in the repository
   (battleground fixes, more reachable spawns, a steadier bot population).
7. **Always STOP SERVER before you close the launcher or shut the PC down**, so the world saves cleanly.

### Your save, and how to back it up
| What | Where (in the game folder) |
|---|---|
| World, characters, companions, bots | `runtime\data\opendaoc-165.sqlite3.db` |
| Your local login | `runtime\account.txt` |
| Client settings, key bindings, logs | `new-client\save\` |

**To back up:** quit the game, click **STOP SERVER** and wait, close the launcher, then copy `runtime\data`,
`runtime\account.txt` and `new-client\save` somewhere safe. Copy the whole `runtime\data` folder rather than only the
`.db` file, because SQLite may keep extra `-wal`/`-shm` files beside it while the server runs.

### Updating to a new version
1. Quit the game, **STOP SERVER**, and close the launcher.
2. Download the new `Download-OfflineDAoC-True165-vX.Y.zip` from the
   [latest release](https://github.com/zomboden/OfflineDAoC-True165/releases/latest), extract it, and run
   `GET-OFFLINE-DAOC-TRUE165.cmd`.
3. Press **1 = Update my game.** It finds your folder, checks every file it will replace, backs them up in
   `patch-backups\`, and updates in place (about 3 GB rather than 36 GB). Your save and settings are never touched.
4. The first **START SERVER** afterwards brings the world up to date and can take a few minutes.

If you've modified game files, the updater refuses to patch them. Instead it offers a fresh install in a new folder
that brings your save over, and your old folder is only read.

---

## 14. Optional: working on your fork

Skip this section if you only want to play. If you've forked the project and want to change the game on the Windows
PC, here's what the project's own docs ask for.

### Tools to install
| Tool | For |
|---|---|
| **Git for Windows** | Cloning your fork. Then run `git config --global core.longpaths true`. |
| **.NET 10 SDK** | Building the server and the launcher. |
| **Godot 4.7.2 stable, .NET build** (`Godot_v4.7.2-stable_mono_win64`) | Only for working on the client. |
| **Python 3.10+** | The content generators and the database scripts. |
| An AI assistant such as Claude Code (optional) | See [LLM-QUICKSTART.md](LLM-QUICKSTART.md) for a ready-made starting prompt. |

### The two-folder setup
1. **A playable folder:** install the release as above. The source alone isn't playable: the 37 GB of client
   content, the 2.1 GB of navigation meshes and the binaries come only with the release.
2. **A source checkout** of your fork in a short path: `git clone <your fork's URL> C:\src\OfflineDAoC-True165`.
3. Edit in the checkout, and test in a **copy** of the playable folder (or at least back up its `runtime\data`).

### Build and test
```bat
cd C:\src\OfflineDAoC-True165\development-source\server
dotnet build "Dawn of Light.sln" -c Release
dotnet test Tests/Tests.csproj -c Release --no-build
```
```bat
cd C:\src\OfflineDAoC-True165\development-source\tools\OfflineDaoc.Launcher
dotnet build OfflineDaoc.Launcher.csproj -c Release
```
- **Deploying a server build:** with the server stopped, copy `Release\lib\GameServer.dll` and `CoreDatabase.dll`
  into all three of `runtime\server`, `runtime\server\lib` and `runtime\server\win-x64`. Back up what you replace.
- **World content changes** go in the JSON in `runtime\true165` (and its generator in `tools\claude-version\true165`;
  keep the two copies identical). **START SERVER** applies them and keeps every save. Never hand-edit the world
  database.
- Builds and tests on the same PC stall a running server, so stop the server first.

The full rules (period sources first, nothing from ToA, no difficulty changes, bots on the player rules) are in
[AGENTS.md](../AGENTS.md), and the details are in [DEVELOPMENT.md](DEVELOPMENT.md) and
[source/new-client/README.md](../source/new-client/README.md).

---

## 15. Troubleshooting

| Problem | Try this |
|---|---|
| The downloader says it was started from inside the zip | Right-click the zip → **Extract All…**, then run the `.cmd` from the extracted folder. |
| "Your CPU does not support AVX2" | The game can't run on that PC. |
| "Vulkan was not found" | Install the latest driver from NVIDIA, AMD or Intel and restart. |
| "needs about N GB of free space" | Free up space, or move the downloader folder to another drive and run it again (it installs on the same drive as the downloader). |
| A download stops, or a part fails its check | Run the `.cmd` again. Finished parts are kept. |
| "The folder … already exists" | It never overwrites. Rename or move the old folder, or run the downloader from somewhere else. |
| SmartScreen blocks a file | **More info → Run anyway.** |
| Smart App Control blocks a file (no Run anyway) | See [section 3.5](#35-know-about-smartscreen-and-smart-app-control). |
| Defender removes a game file | Restore it from **Protection history** and allow the game folder. |
| ENTER REALM stays greyed out | Wait for **RUNNING**. The first start takes a few minutes. |
| START SERVER shows an error | The launcher couldn't apply the world content, and in that case it changes nothing. Note the message and report it. |
| "Port 10300 is in use", or the server won't start | Another server is already running. Stop it (check Task Manager for `CoreServer`). |
| The game window stays black or closes at once | Update your graphics driver (the game needs Vulkan). On a two-GPU laptop, set the engine to High performance (section 3.2). |
| Slow loading on every start | The game folder's path is probably too long (over 42 characters), so the shader cache can't be kept. Reinstall to `C:\Games\OfflineDAoC-True165`. |
| Stutter with many bots | Lower the realm population goals in the launcher, and set an FPS target in Options → VIDEO. |
| The client's settings are messed up | Delete `new-client\save` (you lose your key bindings and client settings, not your characters). |

### Reporting a bug
Open an issue at **https://github.com/zomboden/OfflineDAoC-True165/issues**. Say what you were doing, and attach:
- `new-client\save\Godot\app_userdata\MMOEngine\logs\godot.log` (after a crash, grab it **before** restarting the
  game, because each start renames the previous log to a dated file);
- `spell_log.txt` and `death_log.txt`, from the same `MMOEngine` folder;
- the newest files in `runtime\logs` and `runtime\server\logs`, if the server stopped or the game disconnected.

A crash that leaves nothing in `godot.log` is usually in **Event Viewer → Windows Logs → Application**.

---

## 16. Quick reference card

```
Install:   GitHub release -> Download-OfflineDAoC-True165-v0.7.zip -> extract to C:\Games\OfflineDAoC
           -> GET-OFFLINE-DAOC-TRUE165.cmd -> 2 (New install) -> Y
Play:      START OFFLINE DAOC TRUE 1.65.cmd -> START SERVER -> wait for RUNNING -> ENTER REALM
Quit:      /quit in game -> STOP SERVER -> wait -> close the launcher
Bots:      realm cards' goal boxes (type a number, Enter); 500 per realm by default
Lv 5:      promote at your trainer; the courier sends you to the inn -> innkeeper -> hire companions
Help:      /questguide  /task  /stables  /classes  /horn list   QUICK COMMANDS.txt   ALL SERVER COMMANDS.txt
Save:      runtime\data\  runtime\account.txt  new-client\save\   (back up with the server stopped)
Update:    new downloader -> 1 (Update my game)
```
