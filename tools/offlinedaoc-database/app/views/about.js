/* OFFLINEDAOC Database - About page (lane D-V).
   Where the data comes from, when it was built, how each number is worked out (short versions of the lanes' notes
   in source/contract and source/droprules.md, with their caveats), what the tool does not know, and the result of
   the checks build_all.py ran. Reads only data/meta.js (written by source/build_all.py). */
(function () {
  "use strict";
  var h = WG.h;

  function num(n) { return n == null ? "" : String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ","); }
  function mb(bytes) { return bytes == null ? "" : (bytes / 1e6 >= 0.1 ? (bytes / 1e6).toFixed(1) + " MB" : Math.max(1, Math.round(bytes / 1000)) + " KB"); }
  function section(title) {
    var card = h("div.wg-card", {style: {marginBottom: "12px"}}, h("h2", {style: {marginTop: "0"}}, title));
    for (var i = 1; i < arguments.length; i++) WG.append(card, arguments[i]);
    return card;
  }
  function p(text) { return h("p", {style: {margin: "6px 0", maxWidth: "980px"}}, text); }
  function list(items) {
    return h("ul", {style: {margin: "6px 0", paddingLeft: "20px", maxWidth: "980px"}}, items.map(function (t) { return h("li", {style: {margin: "3px 0"}}, t); }));
  }
  function b(t) { return h("b", t); }
  function code(t) { return h("code", {style: {background: "var(--bg3)", padding: "1px 5px", borderRadius: "4px", fontSize: "12.5px"}}, t); }
  function note(text, kind) {
    if (WG.ui && WG.ui.note) return WG.ui.note(text, kind);
    return h("div.wg-note" + (kind ? "." + kind : ""), text);
  }
  function kv(rows) {
    var t = h("table", {style: {borderCollapse: "collapse", margin: "6px 0"}});
    rows.forEach(function (r) {
      if (!r || r[1] == null || r[1] === "") return;
      t.appendChild(h("tr", h("td.wg-dim", {style: {padding: "2px 16px 2px 0", verticalAlign: "top", whiteSpace: "nowrap"}}, r[0]),
                           h("td", {style: {padding: "2px 0", wordBreak: "break-word"}}, r[1])));
    });
    return t;
  }
  function fileOf(meta, name) {
    var f = (meta && meta.files || []).filter(function (x) { return x.name === name; })[0];
    return f || {};
  }
  function summary(meta, check) { return (meta && meta.checks && meta.checks[check] && meta.checks[check].summary) || {}; }
  function passed(meta, check) { return meta && meta.checks && meta.checks[check] ? meta.checks[check].ok : null; }
  function verdict(ok) {
    if (ok == null) return h("span.wg-dim", "not run");
    return ok ? h("span.con-green", "passed") : h("span.con-red", "failed");
  }

  var CHECK_TITLES = {
    drops: ["Drop chances", "Every monster and every item, against a second reading of the server's loot code written without looking at the builder, and against a simulation that rolls the server's dice."],
    con: ["Con colours", "The page's con library as the browser runs it, against the table rebuilt from the server's ConLevels.cs."],
    prices: ["Merchants and stable routes", "Every merchant, every price, every ticket route, against the server's rules applied to the world tables."],
    data: ["Data files against each other and the world", "Ids, positions inside zones, camp points on real spawns, level ranges, picture files, source flags."]
  };

  function render(el, meta) {
    WG.clear(el);
    var db = (meta && meta.db) || {};
    el.appendChild(h("h1", "About this database"));

    if (!meta) {
      el.appendChild(note("The build record (data/meta.js) is not there yet, so this page cannot say when the data was built. "
        + "Run  python -I source\\build_all.py  in the tool's folder. The explanations below still hold.", "warn"));
    } else if (meta.ok) {
      el.appendChild(note("Built " + meta.built + " from the True 1.65 world on this PC. Every check passed.", "info"));
    } else {
      el.appendChild(note("Built " + meta.built + ". At least one build step or check failed: see \"Checks\" below before trusting a number.", "danger"));
    }

    /* ---------- where it comes from ---------- */
    el.appendChild(section("Where the data comes from",
      p("Everything shown is read from the game on this PC: the True 1.65 world database, the server's own source code for "
        + "the rules (drop rolls, con colours, prices, aggro, routes, reputation) and the game client's maps and icons. Nothing is "
        + "downloaded and nothing here changes the game. Where a number is this tool's own estimate and not a server rule, the page says so."),
      meta ? kv([
        ["Built", meta.built + (meta.partial ? " (partial rebuild)" : "")],
        ["World database", db.path],
        ["World content stamp", db.contentStamp ? db.contentStamp.slice(0, 16) + "…  (applied " + (db.contentApplied || "?") + ")" : "none in this world"],
        ["World file", db.bytes ? mb(db.bytes) + ", changed " + db.modified : null],
        ["Server rules read from", meta.serverSource],
        ["World size", db.rows ? num(db.rows.Mob) + " spawn rows, " + num(db.rows.ItemTemplate) + " item templates, " + num(db.rows.LootTemplate)
          + " loot rows, " + num(db.rows.MerchantItem) + " merchant rows, " + num(db.rows.PathPoints) + " path points" : null]
      ]) : null,
      p("The world is the clean True 1.65 world of the last release, not your played world: your characters, your reputation "
        + "and anything you changed in game are not read. The game's content is the same in both.")
    ));

    /* ---------- drops ---------- */
    var ds = summary(meta, "drops"), dc = ds.compare || {}, mc = ds.mc || {};
    el.appendChild(section("Drop chances",
      p("The chance next to an item is the chance that one kill of that monster drops it, worked out the way the server rolls it. "
        + "It is not the raw number in the loot table."),
      list([
        "A monster rolls every item on its table once, each against its own percentage.",
        "A monster also has a drop limit (1 to 12). When more items win their roll than the limit allows, the server keeps that many winners, picked at random with equal weight. So an item on a crowded table drops less often than its table number says. The chance shown already includes this.",
        "Items marked \"always\" (100% in the table) drop on every kill, on top of the limit.",
        [b("Grey gives nothing. "), "If the monster is grey to anyone who damaged it, the kill gives no item, no coin and no experience."],
        "An item made for one realm falls only for a killer of that realm (in a group: the leader's realm). It then also stops competing for the limit, so the other items on that table are slightly more likely for the other realms. Where that changes a number the page uses your realm.",
        "A quest item falls only while you are on a quest that uses it. It still uses up one of the limit's places when it wins its roll.",
        "One-time drops are not a chance: the first kill at or above the listed level puts the item in your backpack, once per character.",
        "Coin is a separate roll: a chance per monster (the monster's own measured rate where the game data has one, otherwise its body type's; many animals, insects and plants carry none) and an amount set by the monster's level.",
        "Your level, the monster's level and the con colour change no chance (grey is the one exception). There are no randomly generated items in True 1.65."
      ]),
      p(["Checked: ", dc.pairs ? num(dc.pairs) + " monster and item pairs on " + num(dc.kinds) + " monsters compared with a second, separate reading of the loot code; "
        + (dc.pairs_differ ? dc.pairs_differ + " differ" : "none differ") + " (largest difference " + (dc.largest_difference != null ? (dc.largest_difference * 100).toFixed(4) + " percentage points, rounding" : "?") + "). " : "",
        mc.entries ? "The formula was also played out with the server's own dice procedure: " + num(mc.plans) + " loot tables, " + num(mc.kills) + " simulated kills each, no deviation beyond chance." : ""]),
      p(["The full rules with the server lines they come from: ", code("source/droprules.md"), "."])
    ));

    /* ---------- con ---------- */
    var cs = summary(meta, "con");
    el.appendChild(section("Con colours",
      p("Grey, green, blue, yellow, orange, red and purple are the server's own table (ConLevels.cs), copied level by level. "
        + "The bands are not the same width at every level: one level wide below 10, widening to five levels at 40 and above."),
      list([
        "\"Low red\" and \"Low purple\" are this tool's own cut, not a server term: low red is the lower half of the red levels (rounded up), low purple the same number of levels just above red. The hunt page always shows the levels it means.",
        "\"General\" means blue, yellow and orange.",
        "A monster with a level range (\"20 to 26\") is drawn with the colour of both ends. Most monsters have a range: the server draws the level again at every respawn."
      ]),
      p(cs.js_cells ? "Checked: " + num(cs.js_cells) + " level pairs answered by the page's con code equal the server table, and every band and preset." : "")
    ));

    /* ---------- monsters and camps ---------- */
    var mobs = fileOf(meta, "mobs"), camps = fileOf(meta, "camps");
    el.appendChild(section("Monsters, level ranges, camps and neighbours",
      list([
        "A monster's name and level are the ones the running server gives it. Most spawns take them from a monster template; the level is then drawn from the template's list (for example 20 to 26) each time the monster respawns. The pages never reduce such a monster to one number.",
        "Only things you can hunt are listed: guards, merchants, trainers, realm townsfolk, peaceful and untargetable creatures are left out, and so is everything in regions the server keeps closed (later expansions, the tutorial, the unused battlegrounds).",
        [b("A camp"), " is this tool's grouping, modelled on how the bots see camps: spawn points of one monster in one zone standing within 1,500 units of each other (about one pull), cut when the group would stretch more than 2,500 from its centre. A lone spawn is a camp of one."],
        [b("Neighbours"), " are the other monsters within 1,500 units of a camp. A neighbour is \"in reach\" when it is aggressive and a spawn point of the camp lies within its aggro range plus its roaming range plus 200 units: it will notice you while you fight there. Patrols count by their path."],
        [b("Aggressive, hostile, neutral, friendly"), " are the game's own words, the ones it prints when you look at a monster. Only an aggressive monster comes for you unprovoked. A monster with no faction is aggressive when the server gives it an aggro level and an aggro range above zero, and neutral otherwise. A monster with a faction takes the word from your standing with that faction: aggressive, hostile, neutral or friendly. The pages show the standing a new character starts with; your real standing is not known here. No monster comes for a player to whom it is grey."],
        "Bring a friend: a solo player never gets adds from pulling. In a group the chance is 50% per extra member.",
        "Respawn times are the server's: the spawn's own setting, otherwise 5 to 10 minutes (longer for named and very high monsters)."
      ]),
      p(mobs.counts ? "In this build: " + num(mobs.counts.kinds) + " monsters, " + num(camps.counts ? camps.counts.camps : null) + " camps." : ""),
      note("This tool's own judgement, not a server fact: where a camp's edge is, which neighbour will really join a fight (line of sight, where exactly you stand and what roams by are not computed), and the \"safest\" and \"densest\" rankings.", "estimate")
    ));

    /* ---------- merchants ---------- */
    var ps = summary(meta, "prices"), pm = ps.merchants || {}, pr = ps.routes || {};
    el.appendChild(section("Merchants and prices",
      list([
        "A merchant's price is the item's value in the item table: that is what the server charges for one purchase. Arrows, bolts and some supplies come as a pack for that price.",
        "Bounty merchants take the same number in bounty points, seal merchants in seals. There is no realm tax and no True 1.65 price change.",
        "Any merchant buys what you can drop for half its value.",
        "A merchant that belongs to a faction and to no realm refuses you while that faction is hostile to you. The page says whether a new character starts out refused; your real standing is not known here.",
        "Merchants in closed regions and merchants whose list is empty are left out."
      ]),
      p(pm.rows ? "Checked: " + num(pm.merchants) + " merchants and " + num(pm.rows) + " list rows against the world tables (item, page, slot and price)." : "")
    ));

    /* ---------- routes ---------- */
    el.appendChild(section("Stable routes and reputation",
      list([
        "A route exists exactly where the server honours a ticket: the ticket is on that stable master's list and its ride starts within 500 units of the master. Routes are one-way; the way back is its own ticket where there is one.",
        "Tickets cost nothing in True 1.65 (the ticket items have no price).",
        "Ride times are computed: path length at the mount's fixed speed of 1,000 units a second, plus the 4 seconds before it starts.",
        "The Shrouded Isles flying routes (dragonflies, wyverns, gryphons) belong to three factions. Their stable masters fly you only at reputation -50 or better; a new character starts at -100. In True 1.65 reputation is earned by kills only: each kill of one of the faction's enemies has a 20% chance of one point, so about 250 kills for a new character, and killing the faction's own members costs it the same way. The route page lists the monsters that count.",
        "The destination shown is the stable nearest to where the ride ends. A few rides end some way from it; the page shows the gap.",
        "Not covered: the old-frontier and battleground teleporters and the medallion merchants. There are no boat routes in this world."
      ]),
      p(pr.routes ? "Checked: " + num(pr.routes) + " routes in the file, " + num(pr.server) + " that the server's rule yields, ticket by ticket." : "")
    ));

    /* ---------- maps ---------- */
    var img = (meta && meta.images) || {};
    el.appendChild(section("Maps and positions",
      list([
        "Positions are zone coordinates, the numbers /loc shows in game. A pin is drawn where the spawn row puts the monster; a monster that roams or patrols moves around it.",
        "Outdoor zones use the client's own view from above and the painted game map. Dungeons and capitals have a floor plan drawn from the server's navigation mesh (the floor monsters can walk on), because the client has no picture of them. Some housing zones have no picture: pins are drawn on a plain grid.",
        "Height is not drawn. In a dungeon with several floors, two pins on top of each other can be on different floors."
      ]),
      p(img.maps ? "In this build: " + num(img.maps.files) + " map pictures (" + mb(img.maps.bytes) + ")" + (img.icons && img.icons.files ? ", " + num(img.icons.files) + " icon files (" + mb(img.icons.bytes) + ")." : ".") : "")
    ));

    /* ---------- bosses and script-made monsters ---------- */
    el.appendChild(section("Bosses and monsters a script creates",
      list([
        "Dungeons, frontiers, battlegrounds, Darkness Falls and the epic dungeons are covered like any other zone: every zone that has monsters in the world has camps.",
        "Some monsters have no spawn row of their own: a boss script, an encounter script or a quest step creates them (summoned adds, event bosses, quest monsters). They are listed with what the script says: who summons them and where. They have no camp, a few have no level the script states, and their loot is worked out by the same rules from the table under their name.",
        "A boss's own script can set its level after the tables are read; the level shown is then the script's.",
        "Summoned adds of many bosses never drop anything. The monster page says so.",
        "When a realm expedition of gamebots helps kill a scripted epic boss, its loot is shared with the expedition. That changes who receives an item, not the chance that it drops."
      ])
    ));

    /* ---------- quests ---------- */
    var qs = fileOf(meta, "quests");
    el.appendChild(section("Quests",
      list([
        "The quest pages list the quests of the world's quest table and the scripted quests that run in True 1.65, with their steps, where each step happens, what each step hands you and the rewards.",
        "An item a quest hands you for a kill is quest progress, not loot: it comes only while you are on that step, with the chance the step states, and does not compete with the monster's ordinary drops.",
        "Left out, with the reason in the data: quests only for classes True 1.65 does not have, quests whose level range their classes can never reach, and quests that are switched off in True 1.65 (the bounty quests and the reputation hunts among them).",
        "Whether your character may take a quest is judged by level, realm and class only. What you have already finished is not known here."
      ]),
      p(qs.counts && qs.counts.quests ? "In this build: " + num(qs.counts.quests) + " quests." : "")
    ));

    /* ---------- what it does not know ---------- */
    el.appendChild(section("What this tool does not know",
      list([
        "Your character: what you wear, your reputation with any faction, your quests, your money. Upgrade rankings compare items on their own; reputation and stance notes describe a new character.",
        "What is alive right now. It shows where monsters spawn, not what somebody just killed.",
        "Line of sight, terrain and water. Distances are straight lines.",
        "Whether each scripted boss encounter really starts in True 1.65 was not established one by one; a script-made monster is listed when the script that creates it is part of the server.",
        "Changes made to the game after the build date above. Rebuild after the content changes."
      ])
    ));

    /* ---------- checks ---------- */
    if (meta && meta.checks) {
      var box = section("Checks run at build time",
        p("The build runs these after writing the data. Each compares the tool with the game, not with itself."));
      Object.keys(CHECK_TITLES).forEach(function (name) {
        var c = meta.checks[name], t = CHECK_TITLES[name];
        var det = h("details", {style: {margin: "8px 0"}},
          h("summary", {style: {cursor: "pointer"}}, b(t[0] + ": "), verdict(c ? c.ok : null), h("span.wg-dim", "  " + t[1])));
        if (c && c.lines && c.lines.length) {
          det.appendChild(h("div", {style: {margin: "6px 0 0 16px", fontSize: "12.5px"}}, c.lines.map(function (ln) {
            var cls = /^FAIL/.test(ln) ? "con-red" : /^WARN/.test(ln) ? "con-orange" : /^PASS/.test(ln) ? "" : "wg-dim";
            return h("div", {"class": cls, style: {margin: "2px 0"}}, ln);
          })));
        }
        box.appendChild(det);
      });
      el.appendChild(box);
    }

    /* ---------- files ---------- */
    if (meta && meta.files && meta.files.length) {
      var tbl = h("table.wg-table", h("thead", h("tr", h("th", "Data file"), h("th", "Size"), h("th", "Holds"), h("th", "Written"))));
      var tb = h("tbody");
      meta.files.forEach(function (f) {
        var holds = f.error ? "cannot be read: " + f.error : Object.keys(f.counts || {}).map(function (k) { return num(f.counts[k]) + " " + k; }).join(", ");
        tb.appendChild(h("tr", h("td", "data/" + f.name + ".js"), h("td.wg-nowrap", mb(f.bytes)), h("td", holds), h("td.wg-nowrap.wg-dim", f.modified || "")));
      });
      tbl.appendChild(tb);
      var total = meta.files.reduce(function (a, f) { return a + (f.bytes || 0); }, 0);
      el.appendChild(section("Data files", tbl, p("Total " + mb(total) + ". The build took " + (meta.seconds != null ? meta.seconds + " seconds." : "an unknown time."))));
    }

    /* ---------- rebuild ---------- */
    el.appendChild(section("Rebuilding after the game changes",
      p(["Open a terminal in the tool's folder and run ", code("python -I source\\build_all.py"), ". It reads the game (never writes to it), rewrites the data, "
        + "runs the checks above and prints what passed. The game can stay open. ", code("README.txt"), " has the details."])
    ));
  }

  WG.view("about", {
    title: "About",
    nav: true,
    order: 90,
    render: function (el) {
      el.appendChild(h("div.wg-empty", "Loading…"));
      return WG.load("meta").then(function (d) { render(el, d.meta); }, function () { render(el, null); });
    }
  });
})();
