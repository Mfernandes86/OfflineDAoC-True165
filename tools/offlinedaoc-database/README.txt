OFFLINEDAOC Database
====================

A look-up tool for the True 1.65 game on this PC: item upgrades and where they drop (with the real drop
chances), monsters to hunt for your level and con colour, camps on the zone maps, merchants and what they
sell, and the stable routes (Shrouded Isles flights and reputation included).

It is a set of plain files. Nothing is installed, nothing goes online, and nothing in it changes the game.


HOW TO OPEN IT

  Double-click   Open OFFLINEDAOC Database.bat
  (or double-click index.html; it opens in your browser, Edge or Chrome)

  Set your level, realm and class in the box at the top right once. The finders use them as defaults and
  the browser remembers them.

  The About page (last entry in the top bar) says when the data was built, from which world, how each
  number is worked out, and what the tool does not know.


WHAT IS IN THE FOLDER

  index.html                        the page you open
  Open OFFLINEDAOC Database.bat     opens index.html
  app\                              the pages (plain JavaScript and one stylesheet)
  data\                             the data the pages read, made by the builders below
  maps\  icons\                     zone maps and item icons, copied and converted from the game client
  source\                           the builders and checks (Python), notes and reports
  README.txt                        this file


HOW TO REBUILD IT AFTER THE GAME'S CONTENT CHANGES

  You need Python 3.10 with Pillow and numpy (already on this PC). Open a terminal in this folder and run:

      python -I source\build_all.py

  It reads the game (read-only), rewrites every file in data\ (and maps\, icons\), runs the checks and
  prints a PASS / FAIL list. It takes a few minutes. The game can stay open while it runs; it runs one
  step at a time.

  By default it reads the clean True 1.65 world of the last release:
      DAOC HIATUS\Claude Takeover GitHub\release-true165-v0.7-server\runtime\data\opendaoc-165.sqlite3.db
  To build from another world file (for example a newer release), name it:

      python -I source\build_all.py --db "C:\path\to\opendaoc-165.sqlite3.db"

  The server rules (drop rolls, con colours, prices, routes) are read from the server source in
      DAOC HIATUS\new class test - CLAUDE VERSION\development-source\server
  and the maps from the Godot client's converted content. These paths are at the top of
  source\wgcommon.py if a folder ever moves.

  Other switches:
      --only drops,mobs     rebuild only these parts (then run the checks)
      --skip-build          run only the checks
      --no-browser          skip the checks that start Edge in the background
      --keep-going          do not stop at the first part that fails

  The checks on their own:
      python -I source\verify_drops.py     drop chances against a second, separate reading of the server's
                                           loot code and a simulation of its dice
      python -I source\verify_con.py       con colours against the server's table (add --table to print it)
      python -I source\verify_prices.py    merchant prices and stable routes against the server's rules
      python -I source\verify_data.py      every data file against the others and against the world


IF A PAGE SAYS "Data file missing"

  A file in data\ is not there. Run  python -I source\build_all.py  and read what it prints.

ON ANOTHER PC (OR FOR AN AI ASSISTANT)

  README-SETUP.md says how to point the builders at another PC's game files (environment variables or
  source\paths.local.json) and build the data there. This PC's paths are in source\paths.local.json.
