/* OFFLINEDAOC Database - hunting finder (#/hunt) and the shared monster model (lane M-U).

   Part 1, WG.hunt: loads data/mobs.js + data/camps.js + data/zones.js once and gives the three monster pages
   (hunt.js, mobs.js, mob.js) one model, the "how much of this camp is inside these levels" rule and the
   neighbour warning. Other lanes may use it too:
     WG.hunt.load()                     -> Promise of the model {kinds, kindById, kindByKey, camps, campById,
                                           byKind, byZone, zones, zoneById, nearDist}
     WG.hunt.fit(camp, lo, hi)          -> {total, inside, above, below, share, mean}
     WG.hunt.threats(camp, myLevel)     -> {sev: 0..3, list, grey, stronger}
     WG.hunt.warning(camp, myLevel, o)  -> element: the warning line for a camp
     WG.hunt.pinsFor(camp, myLevel)     -> map pins: this camp, the same monster's other camps, dangerous neighbours
     WG.hunt.dangerPins / patrolLines   -> only the neighbour triangles / the patrol paths that touch the camp
     WG.hunt.tagEls(kind, camp)         -> small tags (Named, Epic, Night only, Town resident, ...)
     WG.hunt.kindFacts(kind), campNotes(camp, myLevel), xpFor(mobLevel, myLevel)
     WG.hunt.drops()                    -> Promise of the drop data joined to the items (gearFor(kind, myLevel))
   Part 2: the #/hunt page.

   Rules this file relies on, from the server (read only):
     - con colours: app/lib/concolor.js (GameServer/gameutils/ConLevels.cs)
     - a monster that is grey to you never attacks on sight:
       GameServer/ai/brain/StandardMob/StandardMobBrain.cs:686-688 (CanAggroTarget: IsObjectGreyCon -> false),
       GameServer/gameobjects/GameLiving.cs:647-650
     - a monster attacks on sight only with AggroLevel > 0 and AggroRange > 0: StandardMobBrain.cs:82
     - the stance words (aggressive, hostile, neutral, friendly) are the con message's:
       GameServer/gameobjects/GameNPC.cs:2346-2417
     - a kill gives no experience and no loot when the monster is grey to anyone who damaged it:
       GameServer/serverrules/AbstractServerRules.cs:1219-1221
   Everything about which spawns form a camp and which monsters count as neighbours is M-D's
   (source/contract/mobs.md); this file only reads it. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, con = WG.con, ui = WG.ui;
  var hunt = (WG.hunt = {});

  /* ------------------------------------------------------------------ styles of the three monster pages */
  hunt.css = function () {
    if (document.getElementById("mu-style")) return;
    var s = document.createElement("style");
    s.id = "mu-style";
    s.textContent = [
      ".mu-top{display:flex;flex-direction:column;gap:10px;margin-bottom:12px}",
      ".mu-line{display:flex;flex-wrap:wrap;gap:8px 14px;align-items:center}",
      ".mu-lab{color:var(--dim);font-size:12px;text-transform:uppercase;letter-spacing:.4px}",
      ".mu-cons{display:flex;flex-wrap:wrap;gap:6px}",
      ".mu-con{display:flex;flex-direction:column;align-items:center;min-width:86px;padding:5px 10px 4px;line-height:1.25;border-radius:6px;background:var(--bg3);border:1px solid var(--line);border-top:3px solid var(--c,var(--line));cursor:pointer}",
      ".mu-con b{color:var(--c,var(--text));font-weight:600}",
      ".mu-con small{color:var(--dim);font-size:12px}",
      ".mu-con:hover{border-color:var(--accent);border-top-color:var(--c,var(--accent))}",
      ".mu-con.active{background:#3a3320;border-color:var(--accent);border-top-color:var(--c,var(--accent));box-shadow:0 0 0 1px var(--accent) inset}",
      ".mu-con.active small{color:var(--text)}",
      ".mu-con[disabled]{opacity:.45;cursor:default}",
      ".mu-num{width:62px;text-align:center}",
      ".mu-seg{display:inline-flex}",
      ".mu-seg button{border-radius:0;margin-left:-1px;padding:4px 12px}",
      ".mu-seg button:first-child{border-radius:6px 0 0 6px;margin-left:0}",
      ".mu-seg button:last-child{border-radius:0 6px 6px 0}",
      ".mu-seg button.active{background:var(--accent);color:#1a1408;border-color:var(--accent);font-weight:600;position:relative}",
      ".mu-body{display:grid;grid-template-columns:minmax(0,1fr) minmax(400px,44%);gap:14px;align-items:start}",
      ".mu-side{position:sticky;top:calc(var(--header-h,49px) + 8px);max-height:calc(100vh - var(--header-h,49px) - 16px);overflow:auto;display:flex;flex-direction:column;gap:10px}",
      ".mu-listhead{display:flex;flex-wrap:wrap;gap:8px 14px;align-items:center;justify-content:space-between;margin-bottom:8px}",
      ".mu-rows{outline:none;display:flex;flex-direction:column;gap:4px}",
      ".mu-row{background:var(--bg2);border:1px solid var(--line);border-left:4px solid var(--sev,#3d6b45);border-radius:6px;padding:6px 10px;cursor:pointer}",
      ".mu-row:hover{border-color:#5a6272;border-left-color:var(--sev,#3d6b45)}",
      ".mu-row.sel{background:#2b2a22;border-color:var(--accent);border-left-color:var(--sev,#3d6b45);box-shadow:0 0 0 1px var(--accent)}",
      ".mu-rows:focus-visible .mu-row.sel{box-shadow:0 0 0 2px var(--accent)}",
      ".mu-r1{display:flex;flex-wrap:wrap;gap:2px 12px;align-items:baseline}",
      ".mu-name{font-weight:600;font-size:15px}",
      ".mu-zone{margin-left:auto;text-align:right}",
      ".mu-r2{display:flex;flex-wrap:wrap;gap:2px 10px;align-items:baseline;font-size:13px;margin-top:2px}",
      ".mu-warn{flex:1 1 300px;min-width:0}",
      ".mu-ok{color:#7fbf88}",
      ".mu-note{color:#d9c95a}",
      ".mu-tag{display:inline-block;padding:0 7px;border-radius:999px;border:1px solid var(--line);font-size:12px;white-space:nowrap}",
      ".mu-tag.aggr{border-color:#7a3030;color:#ff9a9a}",
      ".mu-tag.host{border-color:#7a5a24;color:#ffc27a}",
      ".mu-tag.epic{border-color:#7a4fa8;color:#d7b3ff}",
      ".mu-tag.group{border-color:#a8873a;color:#f0d48a;font-weight:600}",
      ".mu-modes{display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap}",
      ".mu-mode{padding:8px 16px;font-size:15px;border-radius:6px;background:var(--bg2);border:1px solid var(--line);color:var(--text);cursor:pointer}",
      ".mu-mode small{color:var(--dim);margin-left:6px}",
      ".mu-mode.active{background:var(--accent);color:#1a1408;border-color:var(--accent);font-weight:600}",
      ".mu-mode.active small{color:#4a3a10}",
      ".mu-tag.calm{border-color:#33543a;color:#8fd19a}",
      ".mu-tag.mixed{border-color:#7a5a24;color:#ffc27a}",
      ".mu-nb{white-space:nowrap}",
      ".mu-more{margin-top:8px;width:100%}",
      ".mu-maphead{display:flex;flex-wrap:wrap;gap:4px 12px;align-items:baseline;justify-content:space-between}",
      ".mu-maphead b{font-size:15px}",
      ".mu-nbl{margin:4px 0 0;padding:0;list-style:none}",
      ".mu-nbl li{padding:3px 0 3px 10px;border-left:3px solid var(--sev,var(--line));margin-bottom:3px}",
      ".mu-h3{font-weight:600;margin:10px 0 4px}",
      ".mu-facts{display:flex;flex-wrap:wrap;gap:6px 22px;align-items:flex-end}",
      ".mu-fact{display:flex;flex-direction:column}",
      ".mu-fact>span:first-child{color:var(--dim);font-size:12px}",
      ".mu-help{color:var(--dim);font-size:12px}",
      "table.wg-table tr.mu-sel td{background:#2b2a22;box-shadow:0 1px 0 var(--accent) inset,0 -1px 0 var(--accent) inset}",
      ".mu-drops{display:flex;flex-wrap:wrap;gap:2px 12px;align-items:center;font-size:13px;margin-top:3px}",
      ".mu-drops .wg-item-link{white-space:nowrap}",
      ".mu-cols{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:14px;align-items:start}",
      "@media (max-width:1000px){.mu-body,.mu-cols{grid-template-columns:minmax(0,1fr)}.mu-side{position:static;max-height:none}}"
    ].join("\n");
    document.head.appendChild(s);
  };

  /* ------------------------------------------------------------------ the model
     Field names are M-D's (source/contract/mobs.md). Short aliases used by the pages are added once here:
       kind: levelMin, levelMax (= lmin, lmax), zoneIds, aggroShare (= ag / spawns), aggroRange (= ar)
       camp: levelMin, levelMax, radius (= r), aggroN (= ag), aggroRange (= ar), hist (= levels), nearList */
  function listOf(d, key) {
    var a = d && (d[key] !== undefined ? d[key] : d);
    if (a && a.cols) a = WG.rows(a);
    return Array.isArray(a) ? a : [];
  }
  /* Stance = the word of the game's con message ("... is aggressive towards you"), for a new character:
     GameServer/gameobjects/GameNPC.cs:2346-2417 (GetAggroLevelString). M-D counts the spawn points per word in the
     field named by SF: [aggressive, hostile, neutral, friendly]. A fifth slot is used only when a data file has no
     such field: then the spawn points that attack on sight are "aggressive" and the rest are not named. */
  var SF = "st";
  hunt.STANCE = ["aggressive", "hostile", "neutral", "friendly", "not aggressive"];
  var STANCE_CLS = ["aggr", "host", "calm", "calm", "calm"];
  function stance5(st, n, ag) {
    if (Array.isArray(st)) return [st[0] || 0, st[1] || 0, st[2] || 0, st[3] || 0, 0];
    return [ag || 0, 0, 0, 0, Math.max(0, (n || 0) - (ag || 0))];
  }
  hunt.stanceParts = function (st) { var out = []; for (var i = 0; i < 5; i++) if (st[i] > 0) out.push([i, st[i]]); return out; };
  /* "aggressive", or "7 aggressive, 5 neutral" for a mixed camp */
  hunt.stanceText = function (st) {
    var p = hunt.stanceParts(st);
    if (!p.length) return "";
    if (p.length === 1) return hunt.STANCE[p[0][0]];
    return p.map(function (e) { return ui.num(e[1]) + " " + hunt.STANCE[e[0]]; }).join(", ");
  };
  hunt.stanceHelp = function (i, range, onSight, n) {
    if (i === 0) return "Aggressive: it attacks when you come within its aggro range" + (range ? " (" + ui.num(range) + ")" : "") + ", unless it is grey to you." +
      (onSight != null && n != null && onSight < n ? " " + (onSight ? "Only " + onSight + " of these " + n + " have" : "None of these " + n + " has") + " an aggro range: the others read aggressive but never come at you." : "");
    if (i === 1) return "Hostile: it leaves you alone unless its faction is aggressive towards you.";
    if (i === 2) return "Neutral: it leaves you alone until you attack it.";
    if (i === 3) return "Friendly: it leaves you alone until you attack it.";
    return "Not aggressive: it leaves you alone until you attack it.";
  };
  /* Tags for a stance split: one "Aggressive" tag, or "7 aggressive" + "5 neutral" for a mixed camp. */
  hunt.stanceTags = function (st, range, onSight) {
    var p = hunt.stanceParts(st);
    return p.map(function (e) {
      var w = hunt.STANCE[e[0]];
      return h("span.mu-tag." + STANCE_CLS[e[0]], {title: hunt.stanceHelp(e[0], range, e[0] === 0 ? onSight : null, e[1])},
        p.length === 1 ? w.charAt(0).toUpperCase() + w.slice(1) : ui.num(e[1]) + " " + w);
    });
  };
  hunt.stanceEl = function (st) {
    var p = hunt.stanceParts(st), el = h("span");
    p.forEach(function (e, i) {
      if (i) el.appendChild(document.createTextNode(", "));
      el.appendChild(h("span", {title: hunt.stanceHelp(e[0]), style: {color: e[0] === 0 ? "#ff9a9a" : e[0] === 1 ? "#ffc27a" : "#8fd19a"}}, (p.length > 1 ? ui.num(e[1]) + " " : "") + hunt.STANCE[e[0]]));
    });
    return el;
  };
  hunt.stanceCalm = function (st) { return st[0] + st[1] === 0; };

  function buildModel(d) {
    var M = {}, mobs = d.mobs || {}, campsD = d.camps || {};
    M.meta = mobs.meta || {};
    M.rules = M.meta.rules || {};
    M.flag = M.meta.flagBits || {};
    M.campFlag = (campsD.meta && campsD.meta.campFlagBits) || M.meta.campFlagBits || {};
    M.dirs = (campsD.meta && campsD.meta.dirs) || M.meta.dirs || ["N", "NE", "E", "SE", "S", "SW", "W", "NW", "among"];
    M.nearDist = (campsD.meta && campsD.meta.nearDistance) || M.rules.nearDistance || null;
    M.zones = listOf(d.zones, "zones");
    M.zoneById = new Map(); M.zones.forEach(function (z) { M.zoneById.set(z.id, z); if (z.name === "Darkness Falls") M.dfZone = z.id; });
    M.factions = mobs.factions ? listOf(mobs.factions) : [];
    M.factionById = new Map(); M.factions.forEach(function (f) { M.factionById.set(f.id, f); });
    M.races = mobs.races || {};
    M.raceCols = mobs.raceCols || null;
    M.xp = mobs.xp || null;
    M.kinds = listOf(mobs, "kinds");
    M.kindById = new Map(); M.kindByKey = new Map();
    M.kinds.forEach(function (k) {
      M.kindById.set(k.id, k); M.kindByKey.set(k.key, k);
      if (typeof k.name === "string" && k.name !== k.name.trim()) k.name = k.name.trim();   // one runtime name ends in a space (the key keeps it)
      if (k.levelMin == null) k.levelMin = k.lmin;
      if (k.levelMax == null) k.levelMax = k.lmax;
      k.zoneIds = (k.zones || []).map(function (z) { return Array.isArray(z) ? z[0] : z; });
      k.aggroShare = k.spawns ? (k.ag || 0) / k.spawns : 0;
      k.aggroRange = k.ar || 0;
      k.stance = stance5(k[SF], k.spawns, k.ag);
    });
    // patrol paths: camp id -> [{kind, zone, camp, pts}] (zone-local points)
    M.pathsByCamp = new Map(); M.pathsByKind = new Map();
    (mobs.paths ? listOf(mobs.paths) : []).forEach(function (p) {
      var a = M.pathsByCamp.get(p.camp); if (!a) M.pathsByCamp.set(p.camp, (a = [])); a.push(p);
      var b = M.pathsByKind.get(p.kind); if (!b) M.pathsByKind.set(p.kind, (b = [])); b.push(p);
    });
    M.camps = listOf(campsD, "camps");
    var nc = (campsD.meta && campsD.meta.nearCols) || M.meta.nearCols || ["kind", "n", "lmin", "lmax", "nAgg", "dist", "dir", "nReach", "nLink", "ar", "patrol"];
    var NI = {}; nc.forEach(function (name, i) { NI[name] = i; });
    M.campById = new Map(); M.byKind = new Map(); M.byZone = new Map();
    M.camps.forEach(function (c) {
      M.campById.set(c.id, c);
      var a = M.byKind.get(c.kind); if (!a) M.byKind.set(c.kind, (a = [])); a.push(c);
      var b = M.byZone.get(c.zone); if (!b) M.byZone.set(c.zone, (b = [])); b.push(c);
      c.levelMin = c.lmin; c.levelMax = c.lmax; c.radius = c.r || 0; c.aggroN = c.ag || 0; c.aggroRange = c.ar || 0;
      c.stance = stance5(c[SF], c.n, c.ag);
      c.hist = c.levels && c.levels.length ? c.levels : [[c.lmin, c.n]];
      c.nearList = (c.near || []).map(function (e) {
        return {id: e[NI.kind], n: e[NI.n], min: e[NI.lmin], max: e[NI.lmax], aggr: e[NI.nAgg] || 0, dist: e[NI.dist], dir: e[NI.dir],
                reach: e[NI.nReach] || 0, link: e[NI.nLink] || 0, ar: e[NI.ar] || 0, patrol: !!e[NI.patrol],
                stance: stance5(NI[SF] != null ? e[NI[SF]] : null, e[NI.n], e[NI.nAgg])};
      });
    });
    adaptClasses(M, mobs);
    hunt.model = M;
    return M;
  }

  /* ---- classes of monsters, zone tags, scripted bosses (all from M-D; see source/contract/mobs.md) ----
     Every kind gets: kclass ("normal" | "named" | "boss" | "epic" | "event" | "quest"), group (true when the data
     marks it as group content), appear (the scripted appearance rule as text, or null).
     M.zoneTags: zone id -> ["Epic dungeon", ...]. M.extras: scripted bosses that are not ordinary spawns, shaped
     like kinds (no camps). When a data file has none of M-D's class fields the class comes from the kind flags. */
  hunt.CLASSES = [["named", "Named"], ["boss", "Boss"], ["epic", "Epic or raid boss"], ["event", "Realm event boss"], ["quest", "Quest target"]];
  var CLASS_HELP = {
    named: "A named monster (its name is written with a capital letter)",
    boss: "A boss monster",
    epic: "An epic or raid boss: a fight for a group or a raid",
    event: "A realm event boss: it appears only as part of a realm event",
    quest: "The target of a quest"
  };
  hunt.classLabel = function (id) { for (var i = 0; i < hunt.CLASSES.length; i++) if (hunt.CLASSES[i][0] === id) return hunt.CLASSES[i][1]; return "Monster"; };
  hunt.classHelp = function (id) { return CLASS_HELP[id] || ""; };
  hunt.classOf = function (k) { return k.kclass || "normal"; };
  hunt.isGroup = function (k) { return !!k.group; };
  hunt.GROUP_HELP = "Group content: its server class is one the server treats and scales as an epic encounter (GameEpicNPC or GameEpicBoss)";
  function adaptClasses(M, mobs) {
    var F = M.flag, groupBits = (F.epic || 0) | (F.boss || 0);
    // kinds: M-D's rank (mobs.md 5c). Group content = the flag bits epic / boss: the classes the server scales as
    // epic encounters. Without a rank field the class falls back to the flags.
    M.kinds.forEach(function (k) {
      var f = k.f || 0;
      k.kclass = k.rank || (F.epic && (f & F.epic) ? "epic" : F.boss && (f & F.boss) ? "boss" : F.named && (f & F.named) ? "named" : F.quest && (f & F.quest) ? "quest" : "normal");
      k.group = !!(f & groupBits);
      k.appear = null;
    });
    // realm events (mobs.md 5b): the 12 raids the server runs; by zone and by boss / objective name
    M.events = mobs.events ? listOf(mobs.events) : [];
    M.eventsByZone = new Map(); M.eventsByName = new Map();
    M.events.forEach(function (e) {
      var a = M.eventsByZone.get(e.zone); if (!a) M.eventsByZone.set(e.zone, (a = [])); a.push(e);
      (e.bosses || []).concat(e.objectives || []).forEach(function (n) {
        var key = String(n).toLowerCase(), b = M.eventsByName.get(key); if (!b) M.eventsByName.set(key, (b = [])); if (b.indexOf(e) < 0) b.push(e);
      });
    });
    // zone tags (mobs.zones.tags): [label, help, css class] per zone; M.epicZones = the epic dungeons and raid zones
    M.zoneTags = new Map(); M.epicZones = new Set();
    (mobs.zones ? listOf(mobs.zones) : []).forEach(function (zr) {
      var tags = zr.tags || [], out = [], evs = M.eventsByZone.get(zr.zone) || [], z = M.zoneById.get(zr.zone);
      var evText = evs.length ? " Realm event: " + evs[0].name + ", final boss " + (evs[0].bosses || []).join(" and ") + "; the server musters " + evs[0].suggested + " for it. A player's own raid is /raid 40 or /raid 80." : "";
      if (tags.indexOf("df") >= 0) M.dfZone = zr.zone;
      if (tags.indexOf("epic") >= 0) out.push(["Epic dungeon", "An epic dungeon: a realm event (a raid) ends here." + evText, "epic"]);
      else if (tags.indexOf("raid") >= 0) {
        if (z && z.kind === "dungeon") out.push(["Raid dungeon", "A realm event (a raid) ends in this dungeon." + evText, "epic"]);
        else out.push(["Dragon zone", "A dragon lives in this zone and a realm event (a raid) ends at it. The ordinary camps of the zone are not part of the raid." + evText, "epic"]);
      }
      if (tags.indexOf("epic") >= 0 || tags.indexOf("raid") >= 0) M.epicZones.add(zr.zone);
      if (tags.indexOf("frontier dungeon") >= 0) out.push(["Frontier dungeon", "A dungeon all three realms reach from the frontier", ""]);
      if (tags.indexOf("starter dungeon") >= 0) out.push(["Starter dungeon", "A low-level dungeon of one realm", ""]);
      if (out.length) M.zoneTags.set(zr.zone, out);
    });
    // monsters only a script creates (mobs.scripted, mobs.md 5d): shaped like kinds, no camps, no stance
    M.extras = (mobs.scripted ? listOf(mobs.scripted) : []).map(function (x) {
      var zs = [], seen = {};
      (x.where || []).forEach(function (w) { if (!seen[w[0]]) { seen[w[0]] = 1; zs.push(w[0]); } });
      x.scripted = true;
      x.name = String(x.name).trim();
      x.levelMin = x.lmin; x.levelMax = x.lmax;
      x.zoneIds = zs; x.spawns = 0; x.camps = 0; x.ag = 0; x.f = 0; x.stance = null; x.aggroShare = 0; x.aggroRange = 0;
      x.kclass = x.rank || "normal";
      x.group = x.kclass === "epic" || x.kclass === "event" || /GameEpic(NPC|Boss)/.test(x.rankWhy || "");
      x.appear = x.how || null;
      return x;
    });
    M.extraById = new Map(); M.extraByKey = new Map();
    M.extras.forEach(function (x) { M.extraById.set(x.id, x); M.extraByKey.set(x.key, x); });
    M._boss = null;
  }
  /* Every monster that is not an ordinary one: named, quest, boss, epic and event kinds, plus the script-made
     monsters of those ranks. */
  hunt.bossRows = function (M) {
    if (!M._boss) M._boss = M.kinds.filter(function (k) { return k.kclass !== "normal"; }).concat(M.extras.filter(function (x) { return x.kclass !== "normal"; }));
    return M._boss;
  };
  /* A kind or a script-made monster by id or by name (the lowercased name is the key). */
  hunt.find = function (M, p) {
    if (p == null) return null;
    if (/^\d+$/.test(p)) return M.kindById.get(+p) || M.extraById.get(+p) || null;
    var key = String(p).toLowerCase();
    return M.kindByKey.get(key) || M.kindByKey.get(key + " ") || M.extraByKey.get(key) || null;
  };
  hunt.zoneTagEls = function (zoneId) {
    var M = hunt.model;
    return (M.zoneTags.get(zoneId) || []).map(function (t) { return h("span.mu-tag" + (t[2] ? "." + t[2] : ""), {title: t[1]}, t[0]); });
  };
  /* The realm events a monster is the final boss or a named objective of. */
  hunt.eventsOf = function (k) { return hunt.model.eventsByName.get(String(k.name).toLowerCase()) || []; };
  /* Levels of a kind or script-made monster in con colours; a script-made one may have no level in the data. */
  hunt.levelEl = function (k, my) {
    if (k.levelMin == null) return h("span.wg-dim", {title: "Its script works the level out when it appears"}, "set by its script");
    return con.el(my, k.levelMin, k.levelMax);
  };
  /* How a monster comes to be there, in one line: its scripted rule, its hours, its respawn. */
  hunt.appearEl = function (k) {
    var M = hunt.model, F = M.flag, f = k.f || 0, bits = [], evs = hunt.eventsOf(k);
    if (evs.length) bits.push(((evs[0].bosses || []).indexOf(k.name) >= 0 ? "final boss" : "a named encounter") + " of the realm event " + evs[0].name + " (a raid: the server musters " + evs[0].suggested + ")");
    if (k.appear) bits.push(k.appear.replace(/\.$/, ""));
    if (F.night && (f & F.night)) bits.push("only at night");
    if (F.day && (f & F.day)) bits.push("only by day");
    if (F.hours && (f & F.hours) && k.hours) bits.push("only from " + k.hours[0] + ":00 to " + k.hours[1] + ":00");
    if (k.spawns > 0) {
      var r = k.resp;
      bits.push((k.appear ? "" : k.spawns === 1 ? "one spawn point" : ui.num(k.spawns) + " spawn points") +
        (r && r.length === 2 ? (k.appear ? "back after " : ", back after ") + (r[0] < 0 ? "never" : r[0] === r[1] ? hunt.seconds(r[0]) : hunt.seconds(r[0]) + " to " + hunt.seconds(r[1])) : ""));
    }
    if (F.scripted && (f & F.scripted) && !k.appear) bits.push("run by a script (its phases and abilities are not in the data)");
    var t = bits.filter(Boolean).join("; ");
    return h("span", t ? t.charAt(0).toUpperCase() + t.slice(1) : "");
  };
  var modelP = null;
  hunt.load = function () {
    if (modelP) return modelP;
    modelP = WG.load("mobs", "camps", "zones").then(buildModel);
    modelP.catch(function () { modelP = null; });
    return modelP;
  };

  /* How much of a camp is inside the levels lo..hi, by spawn, from the camp's level histogram. A spawn that draws
     its level from a range on every respawn counts as fractions (M-D), so the numbers can be "about 3.5". */
  hunt.fit = function (c, lo, hi) {
    var hst = c.hist, tot = 0, inside = 0, above = 0, below = 0, sum = 0;
    for (var i = 0; i < hst.length; i++) {
      var L = hst[i][0], n = hst[i][1];
      tot += n; sum += L * n;
      if (L < lo) below += n; else if (L > hi) above += n; else inside += n;
    }
    return {total: tot, inside: inside, above: above, below: below, share: tot ? inside / tot : 0, mean: tot ? sum / tot : (c.levelMin + c.levelMax) / 2};
  };

  /* Neighbour warning for a camp at my level, from M-D's "near" list (other kinds within nearDistance of a spawn of
     the camp, or closer than their own aggro reach).
     A neighbour whose top level is grey to me is left out: it never attacks on sight (StandardMobBrain.cs:686-688).
     "Stronger" = its top level is above the camp's top level.
       sev 3 danger: stronger, blue or higher to me, and at least one of them attacks on sight from where the camp's
                     monsters stand (M-D's nReach: its aggro range + roaming + 200 covers a spawn of the camp)
       sev 2 warn:   stronger, but out of aggro reach of the camp's spawn points, or it does not attack on sight
       sev 1 note:   not stronger, but attacks on sight within reach of the camp (extra fights)
     Returns {sev, list: [...], grey, stronger}; list items: {id, kind, min, max, n, aggr, reach, link, dist, dir,
     patrol, sev, stronger, color}. */
  hunt.threats = function (c, my) {
    if (c._thrL === my && c._thr) return c._thr;
    var M = hunt.model, list = [], sev = 0, grey = 0, stronger = 0;
    for (var i = 0; i < c.nearList.length; i++) {
      var nb = c.nearList[i], v = con.value(my, nb.max);
      if (v <= -3) { grey++; continue; }
      var isStronger = nb.max > c.levelMax, s = 0;
      if (isStronger) s = nb.reach > 0 && v >= -1 ? 3 : 2;
      else if (nb.reach > 0) s = 1;
      if (isStronger) stronger++;
      if (s > sev) sev = s;
      list.push({id: nb.id, kind: M && M.kindById.get(nb.id) || null, min: nb.min, max: nb.max, n: nb.n, aggr: nb.aggr, reach: nb.reach, link: nb.link,
                 dist: nb.dist, dir: nb.dir, ar: nb.ar, patrol: nb.patrol, stance: nb.stance, sev: s, stronger: isStronger, color: con.color(my, nb.max)});
    }
    list.sort(function (a, b) { return b.sev - a.sev || b.max - a.max || (a.dist || 0) - (b.dist || 0); });
    c._thrL = my;
    return (c._thr = {sev: sev, list: list, grey: grey, stronger: stronger});
  };
  hunt.SEV_COLOR = ["#3d6b45", "#b9a94a", "var(--con-orange)", "var(--con-red)"];
  hunt.SEV_CLASS = ["mu-ok", "mu-note", "wg-warn", "wg-danger"];
  hunt.SEV_HEAD = ["", "Can join in: ", "Stronger nearby: ", "Stronger and can attack at the camp: "];

  function whereWords(t) {
    var M = hunt.model, d = t.dir != null && M ? M.dirs[t.dir] : null;
    if (d === "among") return "among the camp";
    return (t.dist != null ? ui.num(t.dist) + " away" : "") + (d ? " to the " + d : "");
  }
  /* One neighbour as text: "forest giant 32 to 34, 5 spawns, 600 away to the NE"; o.full adds every fact. */
  hunt.neighbour = function (t, my, o) {
    o = o || {};
    var name = t.kind ? t.kind.name : "Monster " + t.id;
    var el = h("span.mu-nb", {style: o.wrap ? {whiteSpace: "normal"} : null});
    el.appendChild(t.kind && o.link !== false ? h("a", {href: WG.href("mob/" + t.id)}, name) : h("span", name));
    el.appendChild(document.createTextNode(" "));
    el.appendChild(con.el(my, t.min, t.max));
    var bits = [];
    if (t.n != null) bits.push(ui.num(t.n) + (t.n === 1 ? " spawn" : " spawns"));
    var stw = hunt.stanceText(t.stance);
    if (o.full) {
      if (stw) bits.push(stw);
      if (t.reach > 0) bits.push((t.reach >= t.n ? (t.n === 1 ? "its" : "all with their") : t.reach + " with their") + " aggro range reaching the camp");
      else if (t.aggr > 0) bits.push("out of aggro reach of the camp's spawn points");
      if (t.link > 0) bits.push("in a group " + (t.link === 1 ? "one" : t.link) + " may come when you pull");
      if (t.patrol) bits.push("patrols past");
    } else if (t.sev === 2) { if (stw) bits.push(stw); }
    else if (t.reach > 0 && t.n > 1) bits.push(t.reach >= t.n ? "all in reach" : t.reach + " in reach");
    var w = whereWords(t); if (w) bits.push(w);
    el.appendChild(h("span.wg-dim", ", " + bits.join(", ")));
    return el;
  };
  /* The warning line of a camp. o.max = how many neighbours to name (default 2). */
  hunt.warning = function (c, my, o) {
    o = o || {};
    var t = hunt.threats(c, my), max = o.max || 2;
    if (!t.sev) {
      return h("span.mu-ok", t.list.length || t.grey ? "Nothing stronger nearby, nothing aggressive in reach" : "No other monsters nearby",
        t.grey ? h("span.wg-dim", " (" + t.grey + " grey " + (t.grey === 1 ? "kind" : "kinds") + " left out)") : null);
    }
    var hot = t.list.filter(function (x) { return x.sev === t.sev; });
    var el = h("span", h("b." + hunt.SEV_CLASS[t.sev], hunt.SEV_HEAD[t.sev]));
    hot.slice(0, max).forEach(function (x, i) {
      if (i) el.appendChild(document.createTextNode("; "));
      el.appendChild(hunt.neighbour(x, my, {wrap: o.wrap}));
    });
    var rest = t.list.filter(function (x) { return x.sev > 0; }).length - Math.min(max, hot.length);
    if (rest > 0) el.appendChild(h("span.wg-dim", "; " + rest + " more"));
    return el;
  };

  /* Map pins (kind "danger") for the neighbours of a camp that carry a warning at my level: the camps of those
     kinds that stand within the near distance (or their aggro range) of this camp. */
  hunt.dangerPins = function (c, my) {
    var M = hunt.model, pins = [];
    var t = hunt.threats(c, my);
    t.list.forEach(function (x) {
      if (!x.sev) return;
      var reach = Math.max(M.nearDist || 1500, x.ar + 400) + (c.radius || 0);
      (M.byKind.get(x.id) || []).forEach(function (o2) {
        if (o2.zone !== c.zone) return;
        var d = Math.sqrt((o2.x - c.x) * (o2.x - c.x) + (o2.y - c.y) * (o2.y - c.y)) - (o2.radius || 0);
        if (d > reach) return;
        pins.push({x: o2.x, y: o2.y, kind: "danger", color: x.sev === 3 ? "red" : x.sev === 2 ? null : "#d9c95a", label: x.kind ? x.kind.name : "Monster " + x.id,
                   sub: "level " + con.rangeText(o2.levelMin, o2.levelMax) + ", " + o2.n + (o2.n === 1 ? " spawn" : " spawns") + ", " + hunt.stanceText(o2.stance),
                   href: WG.href("mob/" + x.id), legend: x.sev === 3 ? "Stronger, can attack at the camp" : x.sev === 2 ? "Stronger nearby" : "Can join in"});
      });
    });
    return pins;
  };
  /* Patrol paths that touch a camp: its own walkers and patrolling neighbours that carry a warning. */
  hunt.patrolLines = function (c, my) {
    var M = hunt.model, lines = [], k = M.kindById.get(c.kind);
    (M.pathsByCamp.get(c.id) || []).forEach(function (p) { lines.push({pts: p.pts, color: "#ff8a8a", dashed: true, label: (k ? k.name : "This monster") + " patrol"}); });
    hunt.threats(c, my).list.forEach(function (x) {
      if (!x.patrol || !x.sev) return;
      (M.pathsByKind.get(x.id) || []).forEach(function (p) { if (p.zone === c.zone) lines.push({pts: p.pts, color: "#ffb04a", dashed: true, label: (x.kind ? x.kind.name : "Monster") + " patrol"}); });
    });
    return lines;
  };

  /* Map pins for one camp: its spawn points, the camp, the same monster's other camps in the zone and the
     neighbours that carry a warning. */
  hunt.pinsFor = function (c, my) {
    var M = hunt.model, k = M.kindById.get(c.kind), name = k ? k.name : "Monster", pins = [];
    if (c.n > 1) (c.pts || []).forEach(function (p) {
      pins.push({x: p[0], y: p[1], kind: "spawn", label: name, sub: "level " + con.rangeText(p[2], p[3]), legend: "Spawn points of this camp"});
    });
    (M.byKind.get(c.kind) || []).forEach(function (o2) {
      if (o2.zone !== c.zone || o2 === c) return;
      pins.push({x: o2.x, y: o2.y, kind: "camp", color: "#a8443c", n: o2.n, label: name, id: "c" + o2.id,
                 sub: "level " + con.rangeText(o2.levelMin, o2.levelMax) + ", " + o2.n + (o2.n === 1 ? " spawn" : " spawns") + "\nclick to choose this camp",
                 legend: "Other " + name + " camps"});
    });
    hunt.dangerPins(c, my).forEach(function (p) { pins.push(p); });
    pins.push({x: c.x, y: c.y, kind: "camp", n: c.n, radius: c.radius > 150 ? c.radius : null, label: name, id: "c" + c.id,
               sub: "level " + con.rangeText(c.levelMin, c.levelMax) + ", " + c.n + (c.n === 1 ? " spawn" : " spawns"), legend: "This camp"});
    return pins;
  };

  /* Short tags of a kind from M-D's flags: [[text, title, css class], ...]. With a camp, "town" and "patrol" are
     the camp's own flags (a kind can have town residents in one camp only). */
  hunt.kindTags = function (k, c, noClass) {
    var M = hunt.model, F = M.flag, CF = M.campFlag, f = k.f || 0, out = [];
    function tag(on, text, title, cls) { if (on) out.push([text, title, cls || ""]); }
    tag(F.noxp && (f & F.noxp), "No experience", "Killing it gives no experience", "aggr");
    var kc = hunt.classOf(k);
    if (kc !== "normal" && !noClass) out.push([hunt.classLabel(kc), hunt.classHelp(kc) + (k.rankWhy ? ". Why: " + k.rankWhy : ""), kc === "named" || kc === "quest" ? "" : "epic"]);
    if (hunt.isGroup(k) && !noClass) out.push(["Group content", hunt.GROUP_HELP, "group"]);
    tag(F.night && (f & F.night), "Night only", "Only in the world while it is night in its region");
    tag(F.day && (f & F.day), "Day only", "Only in the world by day");
    tag(F.hours && (f & F.hours), k.hours ? k.hours[0] + ":00 to " + k.hours[1] + ":00 only" : "Certain hours", "Only in the world during these game hours");
    tag(c ? CF.town && (c.f & CF.town) : F.town && (f & F.town), "Town resident", "It lives in a neutral town of the Shrouded Isles: every kill can cost standing with the faction that runs the town", "mixed");
    tag(F.stealth && (f & F.stealth), "Stealthed", "Hidden until you are within its aggro range");
    tag(F.scripted && (f & F.scripted) && kc === "normal", "Scripted", "A script runs this monster: it can change level, call adds or behave in ways the data does not show");
    tag(c && CF.patrol && (c.f & CF.patrol), "Patrols", "A monster of this camp walks a patrol path (dashed line on the map)");
    return out;
  };
  hunt.tagEls = function (k, c, noClass) {
    return hunt.kindTags(k, c, noClass).map(function (t) { return h("span.mu-tag" + (t[2] ? "." + t[2] : ""), {title: t[1]}, t[0]); });
  };
  /* The base experience of one kill at my level: min(xp[monster level], xp[my level] * 125 / 100)
     (mobs.md "xp": GameLiving.cs:531-637, AbstractServerRules.cs:1380-1418). null for a grey monster (not established). */
  hunt.xpFor = function (mobLevel, my) {
    var M = hunt.model, xp = M.xp, capPct = M.rules.xpCapPercent || 125;
    if (!xp || xp[mobLevel] == null || xp[my] == null || con.value(my, mobLevel) <= -3) return null;
    var cap = Math.floor(xp[my] * capPct / 100);
    return {value: Math.min(xp[mobLevel], cap), capped: xp[mobLevel] > cap};
  };
  /* Extra lines about one camp for a detail panel: respawn, base experience, pack, bring a friend, flags. */
  hunt.campNotes = function (c, my) {
    var M = hunt.model, CF = M.campFlag, out = [], facts = [];
    if (c.resp && c.resp.length === 2) facts.push(h("div.mu-fact", h("span", "Respawn"), h("span", c.resp[0] < 0 ? "does not come back" : c.resp[0] === c.resp[1] ? hunt.seconds(c.resp[0]) : hunt.seconds(c.resp[0]) + " to " + hunt.seconds(c.resp[1]))));
    var xp = hunt.xpEl(c.levelMin, c.levelMax, my);
    if (xp) facts.push(h("div.mu-fact", h("span", "Base experience per kill"), xp));
    if (facts.length) out.push(h("div.mu-facts", {style: {marginTop: "8px"}}, facts));
    if (c.aggroN > 0 && c.pack >= 1) out.push(h("div.mu-help", "They stand close: on average " + c.pack + " others are inside each one's aggro range, so expect more than one at a time."));
    if (c.link > 0) out.push(h("div.mu-help", "Bring a friend: in a group a pull can bring up to " + c.link + (c.link === 1 ? " friend" : " friends") + " standing close enough (50% for one add per extra group member; hired companions count). Alone, a pull never brings one."));
    if (CF.town && (c.f & CF.town)) out.push(h("div.wg-warn", "Town residents: every kill can cost standing with the faction that runs the town."));
    if (CF.quest && (c.f & CF.quest)) out.push(h("div.mu-help", "A spawn of this camp is a quest monster."));
    if (CF.shared && (c.f & CF.shared)) out.push(h("div.mu-help", "Some spawn points here are shared with another kind: the server picks one of the two when it loads, so such a point counts as half a monster."));
    return out;
  };
  /* The level spread of a kind or camp. A chart when the levels run together; plain text when they are few and
     far apart (a spawn that is level 0 or 50 and nothing between would be two hair-thin columns in a chart). */
  hunt.spread = function (hist, my, height) {
    var list = (hist || []).filter(function (e) { return e[1] > 0; });
    if (list.length < 2) return null;
    var span = list[list.length - 1][0] - list[0][0] + 1;
    if ((list.length <= 4 && span > list.length) || list.length * 3 < span) {
      var el = h("span");
      list.slice(0, 8).forEach(function (e, i) {
        var n = Math.abs(e[1] - Math.round(e[1])) < 0.01 ? String(Math.round(e[1])) : "about " + (Math.round(e[1] * 10) / 10);
        el.appendChild(h("span.wg-nowrap", (i ? ", " : "") + n + " at level ", h("b." + con.cls(con.color(my, e[0])), String(e[0]))));
      });
      if (list.length > 8) el.appendChild(document.createTextNode(", and " + (list.length - 8) + " more levels"));
      return el;
    }
    var w = ui.histogram(list, {myLevel: my, height: height || 40, unit: "spawns"});
    if (span > 16) w.style.width = Math.min(340, span * 7) + "px";      // keep the columns wide enough to see
    return w;
  };
  /* True when the levels of a histogram are few and far apart: "levels 0 and 50 only", not "51 levels wide". */
  hunt.gappy = function (hist) {
    var list = (hist || []).filter(function (e) { return e[1] > 0; });
    return list.length >= 2 && list.length <= 4 && list[list.length - 1][0] - list[0][0] + 1 > list.length;
  };
  /* Base experience of one kill for a level range, as an element, or null when all of it is grey to me. */
  hunt.xpEl = function (min, max, my) {
    var a = hunt.xpFor(min, my), b = hunt.xpFor(max, my);
    if (!b) return null;
    var el = h("span", {title: "One solo kill before the camp bonus, other bonuses and your XP rate: the monster's value, at most 125% of the value of a monster of your own level. A grey monster gives none."});
    if (!a) el.appendChild(h("span", h("span.wg-dim", "none while it is grey to you, up to "), ui.num(b.value)));
    else el.appendChild(document.createTextNode(a.value === b.value ? ui.num(a.value) : ui.num(a.value) + " to " + ui.num(b.value)));
    if (b.capped) el.appendChild(h("span.wg-dim", " (capped for level " + my + ")"));
    return el;
  };
  hunt.seconds = function (s) {
    if (s < 0) return "never";
    if (s < 90) return s + " s";
    if (s < 5400) return (Math.round(s / 6) / 10) + " min";
    return (Math.round(s / 360) / 10) + " h";
  };
  /* Extra fact rows for a kind (what M-D provides beyond levels and aggression), as [label, value] pairs. */
  hunt.kindFacts = function (k) {
    var M = hunt.model, F = M.flag, f = k.f || 0, out = [];
    var tags = hunt.tagEls(k, null, true);
    if (tags.length) out.push(["Notes", h("span", tags.map(function (t, i) { return [i ? " " : null, t]; }))]);
    if (k.agIf > 0) out.push(["", h("span.mu-help", k.agIf + " of its " + ui.num(k.spawns) + " spawns have an aggro range but belong to a faction that is not aggressive towards a new character: they turn aggressive when your standing with that faction falls.")]);
    var fac = k.faction ? M.factionById.get(k.faction) : null;
    if (fac) out.push(["Faction", h("span", fac.name, fac.standing ? h("span.wg-dim", " (" + fac.standing + " to a new character)") : null)]);
    var body = M.meta.bodyTypes && M.meta.bodyTypes[k.body];
    if (body) out.push(["Body type", body]);
    var race = M.races[k.race];
    if (race && race[0] && race[0] !== body) out.push(["Race", race[0]]);
    if (race && M.raceCols) {
      var weak = [], strong = [];
      M.raceCols.forEach(function (name, i) { if (!i) return; var v = race[i]; if (v < 0) weak.push(name + " " + v + "%"); else if (v > 0) strong.push(name + " +" + v + "%"); });
      if (strong.length) out.push(["Resists", strong.join(", ")]);
      if (weak.length) out.push(["Weak to", h("span.mu-ok", weak.join(", "))]);
    }
    if (k.resp && k.resp.length === 2) out.push(["Respawn", k.resp[0] < 0 ? "does not come back" : k.resp[0] === k.resp[1] ? hunt.seconds(k.resp[0]) : hunt.seconds(k.resp[0]) + " to " + hunt.seconds(k.resp[1])]);
    if (F.patrol && (f & F.patrol)) out.push(["Movement", "Some walk a patrol path (dashed line on the map)"]);
    else if (k.roam > 0) out.push(["Movement", "Roams up to " + ui.num(k.roam) + " from its spawn point"]);
    return out;
  };

  /* Drops, loaded only when a page asks (data/drops.js + data/items.js for the names; see source/contract/drops.md).
     -> Promise of {mobs, quests, meta, flags, item(id), gearFor(kind, level)} */
  var dropsP = null;
  hunt.drops = function () {
    if (dropsP) return dropsP;
    var itemsP = WG.load("items").then(function (d) { return d.items; }, function () { return null; });
    var metaP = WG.item && WG.item.ready ? WG.item.ready().then(null, function () {}) : Promise.resolve();
    dropsP = Promise.all([WG.load("drops"), itemsP, metaP]).then(function (r) {
      var d = r[0].drops || {}, items = r[1] ? listOf(r[1], "items") : [], byId = new Map();
      items.forEach(function (it) { try { if (WG.item && WG.item.norm) WG.item.norm(it); } catch (e) {} byId.set(it.id, it); });
      var D = {mobs: d.mobs || {}, unspawned: d.unspawned || {}, quests: d.quests || {}, meta: d.meta || {}, hasItems: items.length > 0,
               of: function (key) { return D.mobs[key] || D.unspawned[key] || null; },      // script-made monsters are under "unspawned"
               flags: d.meta && d.meta.flags || {always: 1, quest: 2, never: 4, realm: 8},
               item: function (id) { return byId.get(id) || {id: id, name: String(id), level: 0, missing: true}; }};
      /* The gear a kind drops that my character can use, best first by the server's gear value (app/lib/upgrade.js,
         lane I-U), each with my chance per kill. Quest-only rows and rows another realm gets are left out. */
      D.gearFor = function (k, level) {
        var realm = WG.me.realm || 0, cls = WG.me.cls || 0, sig = level + "/" + realm + "/" + cls;
        if (k._gearSig === sig) return k._gear;
        var kd = D.of(k.key), out = [];
        ((kd && kd.d) || []).forEach(function (row) {
          if (row[3] & (D.flags.quest | D.flags.never)) return;
          var c = Array.isArray(row[4]) && realm ? row[4][realm - 1] : row[1];
          if (!(c > 0)) return;
          var it = byId.get(row[0]); if (!it) return;
          try {
            if (WG.item.isGear && !WG.item.isGear(it)) return;
            if (realm && it.realm && it.realm !== realm) return;
            if (cls && WG.item.usableBy && !WG.item.usableBy(it, cls, level)) return;
          } catch (e) { return; }
          var v = 0;
          try { v = WG.upgrade && WG.upgrade.value ? WG.upgrade.value(it, {level: level, cls: cls}) : it.level; } catch (e) { v = it.level || 0; }
          out.push({it: it, chance: c, value: v});
        });
        out.sort(function (a, b) { return b.value - a.value || b.chance - a.chance; });
        k._gearSig = sig; k._gear = out;
        return out;
      };
      return D;
    });
    dropsP.catch(function () { dropsP = null; });
    return dropsP;
  };
  hunt.gearEl = function (D, k, level, max) {
    var g = D.gearFor(k, level);
    if (!g.length) return null;
    var el = h("div.mu-drops", {title: "Gear this monster drops that your character can use, best first by the server's gear value for your level" + (WG.me.cls ? " and class" : "") + ". The number is the chance per kill."},
      h("span.wg-dim", "Drops for you:"));
    g.slice(0, max).forEach(function (x) { el.appendChild(h("span.wg-nowrap", ui.itemLink(x.it, {iconSize: 16}), h("span.wg-dim", " " + WG.pct(x.chance)))); });
    if (g.length > max) el.appendChild(h("a.wg-dim", {href: WG.href("mob/" + k.id)}, "and " + (g.length - max) + " more"));
    return el;
  };

  hunt.clampLevel = function (v, d) { v = Math.round(+v); if (!isFinite(v)) v = d || 1; return Math.max(1, Math.min(50, v)); };

  /* ================================================================== the #/hunt page */
  var MATCH = [["all", "all of it"], ["most", "most of it"], ["any", "any of it"]];
  var SORTS = [["best", "Best overall"], ["safe", "Safest"], ["most", "Most monsters"], ["close", "Closest to the levels wanted"]];
  var SORT_HELP = {
    best: "Best overall: how much of the camp is in your levels, how many monsters, minus warnings and monsters above your levels.",
    safe: "Safest: fewest and weakest neighbour warnings first, then nothing above your levels, then camps that are not aggressive, then more monsters.",
    most: "Most monsters: the camps with the most spawns inside your levels first.",
    close: "Closest: the camp's average level nearest the middle of the levels you chose."
  };
  var SIZES = [[1, "Any size, single spawns too"], [2, "2 or more"], [3, "3 or more"], [5, "5 or more"], [8, "8 or more"], [12, "12 or more"]];
  var DEF = {con: "general", match: "most", where: "", lands: "", zone: "", size: 3, stance: "", safe: false, sort: "best", q: ""};
  var WHERE = [["", "All kinds"], ["outdoor", "Outdoor"], ["dungeon", "Dungeon"], ["frontier", "Frontier"], ["battleground", "Battleground"], ["df", "Darkness Falls"]];
  var LANDS = [["", "Classic and Shrouded Isles"], ["classic", "Classic"], ["si", "Shrouded Isles"]];
  var PAGE = 50;
  var cur = null;        // the page being shown

  function readState(q) {
    var S = {};
    S.level = hunt.clampLevel(q.level != null ? q.level : WG.me.level, 1);
    S.con = q.con === "custom" || con.preset(q.con) ? q.con : (con.preset(WG.pref("hunt.con")) ? WG.pref("hunt.con") : DEF.con);
    S.min = q.min != null && isFinite(+q.min) ? Math.max(0, Math.min(127, Math.round(+q.min))) : null;
    S.max = q.max != null && isFinite(+q.max) ? Math.max(0, Math.min(127, Math.round(+q.max))) : null;
    if (S.con === "custom" && (S.min == null || S.max == null)) { var g = con.presetLevels(S.level, DEF.con); S.min = S.min == null ? g[0] : S.min; S.max = S.max == null ? g[1] : S.max; }
    if (S.con === "custom" && S.max < S.min) { var t = S.min; S.min = S.max; S.max = t; }
    var m = q.match || WG.pref("hunt.match");
    S.match = m === "all" || m === "most" || m === "any" ? m : DEF.match;
    S.realmSet = q.realm != null && q.realm !== "";
    S.realm = S.realmSet ? Math.max(0, Math.min(3, +q.realm || 0)) : (WG.me.realm || 0);
    var wq = q.where != null ? q.where : q.kind, lq = q.lands != null ? q.lands : q.exp;      // kind / exp: the first names of these filters
    S.where = WHERE.some(function (o) { return o[0] === wq; }) ? wq : DEF.where;
    S.lands = lq === "classic" || lq === "si" ? lq : DEF.lands;
    S.zone = q.zone != null && q.zone !== "" && isFinite(+q.zone) ? +q.zone : "";
    S.size = q.size != null && isFinite(+q.size) && +q.size >= 1 ? Math.round(+q.size) : DEF.size;
    S.stance = q.stance === "calm" || q.aggro === "no" ? "calm" : q.stance === "aggr" || q.aggro === "yes" ? "aggr" : DEF.stance;   // aggro: the first name of this filter
    S.safe = q.safe === "1";
    S.ez = q.ez === "no" || q.ez === "only" ? q.ez : "";
    S.sort = SORT_HELP[q.sort] ? q.sort : DEF.sort;
    S.q = q.q || "";
    S.sel = q.sel != null && isFinite(+q.sel) ? +q.sel : null;
    return S;
  }
  function queryOf(S) {
    return {
      level: S.level, con: S.con,
      min: S.con === "custom" ? S.min : null, max: S.con === "custom" ? S.max : null,
      match: S.match !== DEF.match ? S.match : null,
      realm: S.realm !== (WG.me.realm || 0) || S.realmSet ? S.realm : null,
      where: S.where, lands: S.lands, zone: S.zone === "" ? null : S.zone,
      size: S.size !== DEF.size ? S.size : null, stance: S.stance, safe: S.safe ? 1 : null, ez: S.ez,
      sort: S.sort !== DEF.sort ? S.sort : null, q: S.q, sel: S.sel
    };
  }
  function wanted(S) {
    if (S.con === "custom") return [S.min, S.max];
    return con.presetLevels(S.level, S.con) || [S.level, S.level];
  }
  /* Realm filter: the realm's own zones, every zone that belongs to no realm (Darkness Falls, the battlegrounds)
     and every realm's frontier (anyone can walk into a frontier zone). */
  function realmOk(z, S) { return !S.realm || !z.realm || z.realm === S.realm || z.kind === "frontier"; }
  function whereOk(z, where, M) { return !where || (where === "df" ? z.id === M.dfZone : z.kind === where); }
  function landsOk(z, lands) { return !lands || (z.expansion || "classic") === lands; }
  function zoneOk(z, S, M) { return !!z && realmOk(z, S) && whereOk(z, S.where, M) && landsOk(z, S.lands); }

  function build(P, M) {
    var el = P.el, S = P.S;
    hunt.css();
    var top = h("div.wg-card.mu-top"), body = h("div.mu-body"), listBox = h("div.mu-list"), side = h("div.mu-side");
    el.appendChild(top); el.appendChild(body); body.appendChild(listBox); body.appendChild(side);

    /* ---- line 1: my level + the con choices ---- */
    var lvl = ui.levelInput({value: S.level, label: "My level", onChange: function (n) { S.level = n; if (WG.me.level !== n) WG.setMe({level: n}); changed("level"); }});
    var consEl = h("div.mu-cons", {role: "group", "aria-label": "Con colours to hunt"});
    var minIn = h("input.mu-num", {type: "number", min: 0, max: 127, "aria-label": "Lowest monster level", title: "Lowest monster level"});
    var maxIn = h("input.mu-num", {type: "number", min: 0, max: 127, "aria-label": "Highest monster level", title: "Highest monster level"});
    function customFromInputs() {
      var a = minIn.value === "" ? null : Math.round(+minIn.value), b = maxIn.value === "" ? null : Math.round(+maxIn.value);
      if (a == null || b == null || !isFinite(a) || !isFinite(b)) return;
      a = Math.max(0, Math.min(127, a)); b = Math.max(0, Math.min(127, b));
      if (b < a) { if (document.activeElement === minIn) b = a; else a = b; }
      S.con = "custom"; S.min = a; S.max = b; changed("con");
    }
    minIn.addEventListener("change", customFromInputs); maxIn.addEventListener("change", customFromInputs);
    minIn.addEventListener("input", ui.debounce(customFromInputs, 350)); maxIn.addEventListener("input", ui.debounce(customFromInputs, 350));
    function paintCons() {
      WG.clear(consEl);
      con.presets.forEach(function (p) {
        var r = con.presetLevels(S.level, p.id), on = S.con === p.id;
        var b = h("button.mu-con" + (on ? ".active" : ""), {type: "button", disabled: r ? null : true, "aria-pressed": on ? "true" : "false",
          title: p.hint + (r ? ". At level " + S.level + ": monster level " + con.rangeText(r[0], r[1]) + "." : ". No monster level is this colour at level " + S.level + "."),
          onclick: function () { S.con = p.id; WG.pref("hunt.con", p.id); changed("con"); }},
          h("b", p.label), h("small", r ? con.rangeText(r[0], r[1]) : "none"));
        b.style.setProperty("--c", p.colors.length === 1 ? "var(--con-" + p.colors[0] + ")" : "var(--accent)");
        consEl.appendChild(b);
      });
      var w = wanted(S), onC = S.con === "custom";
      var cb = h("button.mu-con" + (onC ? ".active" : ""), {type: "button", "aria-pressed": onC ? "true" : "false", title: "Your own monster levels: type them in the two boxes",
        onclick: function () { if (S.con !== "custom") { var cw = wanted(S); S.con = "custom"; S.min = cw[0]; S.max = cw[1]; changed("con"); } minIn.focus(); minIn.select(); }},
        h("b", "Custom"), h("small", onC ? con.rangeText(w[0], w[1]) : "your levels"));
      consEl.appendChild(cb);
      if (document.activeElement !== minIn) minIn.value = w[0];
      if (document.activeElement !== maxIn) maxIn.value = w[1];
    }
    var bandsEl = h("span.mu-help");
    function paintBands() {
      WG.clear(bandsEl);
      bandsEl.appendChild(document.createTextNode("At level " + S.level + ": "));
      con.bands(S.level).forEach(function (b, i) {
        if (i) bandsEl.appendChild(document.createTextNode("  "));
        bandsEl.appendChild(h("span." + con.cls(b.color), b.color + " " + (b.color === "purple" ? b.min + "+" : b.color === "grey" ? "to " + b.max : con.rangeText(b.min, b.max))));
      });
      bandsEl.appendChild(document.createTextNode(".  A grey monster gives no experience and no loot, and does not attack you even when it is aggressive."));
    }
    top.appendChild(h("div.mu-line", lvl, consEl));
    /* ---- line 2: the levels this means + how a wide camp counts ---- */
    var seg = h("span.mu-seg", {role: "group", "aria-label": "How much of a camp must be inside these levels"});
    function paintSeg() {
      WG.clear(seg);
      MATCH.forEach(function (m) {
        seg.appendChild(h("button" + (S.match === m[0] ? ".active" : ""), {type: "button", "aria-pressed": S.match === m[0] ? "true" : "false",
          title: m[0] === "all" ? "Every spawn of the camp is inside these levels" : m[0] === "most" ? "More than half of the camp's spawns are inside these levels (the default)" : "At least one spawn of the camp is inside these levels",
          onclick: function () { S.match = m[0]; WG.pref("hunt.match", m[0]); changed("match"); }}, m[1]));
      });
    }
    top.appendChild(h("div.mu-line",
      h("span", h("span.mu-lab", "Monster levels "), minIn, h("span.wg-dim", " to "), maxIn),
      h("span", h("span.mu-lab", "A camp counts when "), seg, h("span.mu-lab", " is inside these levels")),
      h("span.mu-help", "Default: most of it, more than half of the camp's spawns. A wide camp always shows its full range.")));
    top.appendChild(h("div.mu-line", bandsEl));

    /* ---- line 3: the other filters ---- */
    var fbar = h("div");
    top.appendChild(fbar);
    function zoneOptions() {
      var opts = [];
      M.byZone.forEach(function (list, zid) { var z = M.zoneById.get(zid); if (zoneOk(z, S, M)) opts.push([zid, z.name]); });
      opts.sort(function (a, b) { return a[1].localeCompare(b[1]); });
      return [["", "Any zone"]].concat(opts);
    }
    // the place chips carry the number of camps each choice would show with every other filter as it is
    function whereOptions() { return WHERE.map(function (o) { return {value: o[0], label: o[1], count: P.cnt ? P.cnt.where[o[0]] : null, title: o[0] === "df" ? "The dungeon all three realms share" : o[0] === "frontier" ? "The frontier zones of all three realms" : null}; }); }
    function landsOptions() { return LANDS.map(function (o) { return {value: o[0], label: o[1], count: P.cnt ? P.cnt.lands[o[0]] : null}; }); }
    var F = ui.filters(fbar, [
      {key: "realm", label: "Realm", type: "select", value: S.realm, options: [[0, "Any realm"], [1, "Albion"], [2, "Midgard"], [3, "Hibernia"]], title: "This realm's zones and dungeons, plus what every realm can reach: all frontiers, the battlegrounds and Darkness Falls"},
      {key: "where", label: "Zone kind", type: "chips", required: true, value: S.where, options: whereOptions()},
      {key: "lands", label: "Lands", type: "chips", required: true, value: S.lands, options: landsOptions()},
      {key: "zone", label: "Zone", type: "select", value: S.zone, options: zoneOptions(), width: 190},
      {key: "size", label: "Camp size", type: "select", value: S.size, options: SIZES, title: "How many monsters spawn in the camp. The default is 3 or more."},
      {key: "stance", label: "Stance", type: "select", value: S.stance, options: [["", "Any stance"], ["calm", "Neutral or friendly only"], ["aggr", "Aggressive or hostile"]],
        title: "The word the game uses when you look at a monster: aggressive, hostile, neutral or friendly towards you. \"Neutral or friendly only\" hides a camp that has even one aggressive or hostile spawn."},
      {key: "ez", label: "Epic dungeons and raid zones", type: "select", value: S.ez, hidden: !M.epicZones.size, options: [["", "Included"], ["no", "Left out"], ["only", "Only these"]],
        title: "Zones where a realm event (a raid) ends: " + Array.from(M.epicZones).map(function (z) { var o = M.zoneById.get(z); return o ? o.name : z; }).join(", ") + ". Camps there carry a badge."},
      {key: "q", label: "Monster name", type: "text", value: S.q, placeholder: "part of a name", width: 150},
      {key: "safe", label: "No stronger neighbours", type: "toggle", value: S.safe, title: "Hide camps that have a monster nearby whose level can be above the camp's highest level (and that is not grey to you)"}
    ], function (v, key) {
      S.realm = +v.realm || 0; if (key === "realm") S.realmSet = true;
      S.where = v.where || ""; S.lands = v.lands || ""; S.size = +v.size || 1; S.stance = v.stance; S.q = v.q; S.safe = !!v.safe; S.ez = v.ez || "";
      S.zone = v.zone === "" ? "" : +v.zone;
      if (key === "realm" || key === "where" || key === "lands") {
        var zo = zoneOptions();
        if (S.zone !== "" && !zo.some(function (o) { return o[0] === S.zone; })) S.zone = "";
        F.setOptions("zone", zo); F.set("zone", S.zone);
      }
      changed(key);
    });
    P.F = F;
    var placeEl = h("div.mu-help");
    top.appendChild(placeEl);
    function paintPlace() {
      var rn = S.realm ? WG.REALMS[S.realm] : null, t;
      if (S.zone !== "") { var zz = M.zoneById.get(S.zone); t = "Searching one zone: " + (zz ? zz.name : S.zone) + "."; }
      else if (S.where === "df") t = "Searching Darkness Falls only.";
      else if (S.where === "frontier") t = "Searching the frontier zones of all three realms only.";
      else if (S.where === "battleground") t = "Searching the battlegrounds only.";
      else if (S.where) t = "Searching " + (S.where === "outdoor" ? "outdoor zones" : "dungeons") + " only" + (rn ? ", " + rn + "'s and those every realm can reach" : ", all realms") + ".";
      else t = "Searching everywhere a character of " + (rn || "any realm") + " can hunt: " + (rn ? rn + "'s" : "every realm's") +
        " outdoor zones and dungeons, the frontiers of all three realms, the battlegrounds and Darkness Falls. Nothing is left out by kind of place" + (DEF.where === S.where && !S.lands ? " (this is the default)" : "") + ".";
      if (S.lands) t += " " + (S.lands === "si" ? "Shrouded Isles zones only." : "Classic zones only, no Shrouded Isles.");
      if (M.epicZones.size) t += S.ez === "no" ? " Epic dungeons and raid zones are left out." : S.ez === "only" ? " Only epic dungeons and raid zones." : " Epic dungeons and raid zones are included and carry a badge.";
      if (S.size > 1) t += " Camps of " + S.size + " or more" + (S.size === DEF.size ? " (the default; choose \"Any size\" to see single spawns too)" : "") + ".";
      placeEl.textContent = t;
    }

    /* ---- the list ---- */
    var summary = h("div"), sortSel = h("select", {"aria-label": "Sort the camps", onchange: function () { S.sort = sortSel.value; changed("sort"); }},
      SORTS.map(function (s) { return h("option", {value: s[0]}, s[1]); }));
    sortSel.value = S.sort;
    var sortHelp = h("div.mu-help");
    var rowsEl = h("div.mu-rows", {tabIndex: 0, role: "listbox", "aria-label": "Camps. Up and down arrows choose a camp, Enter opens the monster."});
    var moreBtn = h("button.mu-more", {type: "button", onclick: function () { P.shown += PAGE; paintRows(); }});
    listBox.appendChild(h("div.mu-listhead", summary, h("label", h("span.mu-lab", "Sort "), sortSel)));
    listBox.appendChild(sortHelp); listBox.appendChild(rowsEl); listBox.appendChild(moreBtn);

    /* ---- the side: map + the chosen camp ---- */
    var mapHead = h("div.mu-maphead"), mapEl = h("div"), detail = h("div.wg-card");
    side.appendChild(h("div.wg-card", mapHead, mapEl)); side.appendChild(detail);
    function mapHeight() { return Math.max(280, Math.min(480, Math.round(window.innerHeight * 0.44))); }

    P.rows = []; P.shown = PAGE; P.selCamp = null;

    function compute() {
      var w = wanted(S), lo = w[0], hi = w[1], out = [], q = S.q.trim().toLowerCase(), kinds = 0, seen = new Set();
      var zok = new Map(), zw = new Map(), zl = new Map();
      M.zones.forEach(function (z) { zok.set(z.id, realmOk(z, S) && (S.zone === "" || z.id === S.zone)); zw.set(z.id, whereOk(z, S.where, M)); zl.set(z.id, landsOk(z, S.lands)); });
      var cnt = {where: {"": 0, outdoor: 0, dungeon: 0, frontier: 0, battleground: 0, df: 0}, lands: {"": 0, classic: 0, si: 0}};
      var mid = (lo + hi) / 2, camps = M.camps;
      for (var i = 0; i < camps.length; i++) {
        var c = camps[i];
        if (c.levelMax < lo || c.levelMin > hi || c.n < S.size || !zok.get(c.zone)) continue;
        if (S.stance && (S.stance === "calm") !== hunt.stanceCalm(c.stance)) continue;
        if (S.ez && (S.ez === "only") !== M.epicZones.has(c.zone)) continue;
        var k = M.kindById.get(c.kind);
        if (!k) continue;
        if (q && k.key.indexOf(q) < 0 && k.name.toLowerCase().indexOf(q) < 0) continue;
        var f = hunt.fit(c, lo, hi);
        if (S.match === "all" ? f.share < 0.9999 : S.match === "most" ? f.share <= 0.5 : f.inside <= 0) continue;
        var t = hunt.threats(c, S.level);
        if (S.safe && t.stronger > 0) continue;
        // count the camp for the place chips (every filter but the chip's own), then apply the place filters
        var zc = M.zoneById.get(c.zone), wOk = zw.get(c.zone), lOk = zl.get(c.zone);
        if (lOk) { cnt.where[""]++; if (cnt.where[zc.kind] != null) cnt.where[zc.kind]++; if (c.zone === M.dfZone) cnt.where.df++; }
        if (wOk) { cnt.lands[""]++; cnt.lands[zc.expansion === "si" ? "si" : "classic"]++; }
        if (!wOk || !lOk) continue;
        var risk = 0;
        for (var j = 0; j < t.list.length; j++) { var x = t.list[j]; risk += x.sev === 3 ? 10 + Math.min(10, x.max - c.levelMax) : x.sev === 2 ? 3 : x.sev === 1 ? 1 : 0; }
        var aboveShare = f.total ? f.above / f.total : 0, agShare = c.n ? c.aggroN / c.n : 0;
        var r = {c: c, k: k, f: f, t: t, inN: f.inside,
                 risk: risk + aboveShare * 8 + agShare,
                 best: f.share * 50 + Math.min(12, f.inside) / 12 * 30 - (t.sev === 3 ? 25 : t.sev === 2 ? 8 : t.sev === 1 ? 2 : 0) - aboveShare * 30 - agShare * 5,
                 off: Math.abs(f.mean - mid)};
        out.push(r);
        if (!seen.has(k.id)) { seen.add(k.id); kinds++; }
      }
      var by = S.sort;
      out.sort(by === "safe" ? function (a, b) { return a.risk - b.risk || b.inN - a.inN || b.f.share - a.f.share || a.c.id - b.c.id; }
        : by === "most" ? function (a, b) { return b.inN - a.inN || a.risk - b.risk || a.c.id - b.c.id; }
        : by === "close" ? function (a, b) { return a.off - b.off || b.f.share - a.f.share || b.inN - a.inN || a.c.id - b.c.id; }
        : function (a, b) { return b.best - a.best || b.inN - a.inN || a.c.id - b.c.id; });
      P.rows = out; P.kindCount = kinds; P.want = w; P.cnt = cnt;
      F.setOptions("where", whereOptions()); F.setOptions("lands", landsOptions());
      paintPlace();
    }

    function share(f) {
      // "9 of 12" when the counts are whole spawns, a percentage when spawns roll their level from a range
      var whole = Math.abs(f.inside - Math.round(f.inside)) < 0.01 && Math.abs(f.total - Math.round(f.total)) < 0.01;
      return whole ? Math.round(f.inside) + " of " + Math.round(f.total) : Math.round(f.share * 100) + "%";
    }
    function countText(v) { return Math.abs(v - Math.round(v)) < 0.01 ? String(Math.round(v)) : "about " + (Math.round(v * 10) / 10); }
    function rowEl(r) {
      var c = r.c, k = r.k, f = r.f, z = M.zoneById.get(c.zone), w = P.want;
      var row = h("div.mu-row", {role: "option", "aria-selected": "false"});
      row._r = r;
      row.style.setProperty("--sev", hunt.SEV_COLOR[r.t.sev]);
      var sib = (M.byKind.get(k.id) || []).filter(function (o) { return o.zone === c.zone; }).length;
      row.appendChild(h("div.mu-r1",
        h("a.mu-name", {href: WG.href("mob/" + k.id), title: "Open the page of this monster"}, k.name),
        h("span", h("span.wg-dim", "level "), con.el(S.level, c.levelMin, c.levelMax)),
        h("span", {title: "Monsters that spawn in this camp"}, h("b", ui.num(c.n)), h("span.wg-dim", c.n === 1 ? " spawn" : " spawns")),
        h("span.mu-zone", ui.zoneLink(z || c.zone, {loc: [c.x, c.y]}), sib > 1 ? h("span.wg-dim", {title: "This monster has " + sib + " camps in this zone; the map shows them all"}, " (" + sib + " camps here)") : null)));
      var r2 = h("div.mu-r2");
      if (f.share < 0.9999) {
        r2.appendChild(h("span", {title: "How much of this camp is inside monster levels " + con.rangeText(w[0], w[1])},
          h("b", share(f)), h("span.wg-dim", " in your levels"),
          f.above > 0 ? h("span", ", ", h("b." + con.cls(con.color(S.level, c.levelMax)), countText(f.above) + " above"), h("span.wg-dim", " (up to " + c.levelMax + ")")) : null,
          f.below > 0 ? h("span.wg-dim", ", " + countText(f.below) + " below") : null));
      }
      hunt.stanceTags(c.stance, c.aggroRange, c.aggroN).forEach(function (tg) { r2.appendChild(tg); });
      hunt.zoneTagEls(c.zone).forEach(function (tg) { r2.appendChild(tg); });
      hunt.tagEls(k, c).forEach(function (tg) { r2.appendChild(tg); });
      r2.appendChild(h("span.mu-warn", hunt.warning(c, S.level, {wrap: true})));
      row.appendChild(r2);
      if (P.dropLine) { var dl = P.dropLine(k); if (dl) row.appendChild(dl); }
      return row;
    }
    function paintRows() {
      WG.clear(rowsEl);
      var n = P.rows.length, w = P.want;
      WG.clear(summary);
      summary.appendChild(h("span", h("b", ui.num(n)), n === 1 ? " camp of " : " camps of ", h("b", ui.num(P.kindCount)), P.kindCount === 1 ? " monster" : " monsters",
        h("span.wg-dim", " with " + (S.match === "all" ? "all" : S.match === "most" ? "most" : "any") + " of the camp at level "), con.el(S.level, w[0], w[1])));
      sortHelp.textContent = SORT_HELP[S.sort];
      if (!n) {
        rowsEl.appendChild(h("div.wg-empty", h("div", "No camp matches."), h("div", {style: {marginTop: "8px"}}, emptyAdvice())));
        moreBtn.style.display = "none";
        return;
      }
      var selIdx = P.selCamp ? P.rows.findIndex(function (r) { return r.c === P.selCamp; }) : -1;
      if (selIdx >= P.shown) P.shown = Math.ceil((selIdx + 1) / PAGE) * PAGE;
      var frag = document.createDocumentFragment(), lim = Math.min(n, P.shown);
      for (var i = 0; i < lim; i++) {
        var el2 = rowEl(P.rows[i]);
        if (P.rows[i].c === P.selCamp) { el2.classList.add("sel"); el2.setAttribute("aria-selected", "true"); }
        frag.appendChild(el2);
      }
      rowsEl.appendChild(frag);
      moreBtn.style.display = n > lim ? "" : "none";
      moreBtn.textContent = "Show " + Math.min(PAGE, n - lim) + " more (" + ui.num(n - lim) + " not shown)";
    }
    function emptyAdvice() {
      var tips = [];
      if (S.q) tips.push("clear the monster name");
      if (S.zone !== "") tips.push("choose Any zone");
      if (S.safe) tips.push("turn off \"No stronger neighbours\"");
      if (S.stance) tips.push("set \"Stance\" to Any stance");
      if (S.ez) tips.push("set \"Epic dungeons and raid zones\" to Included");
      if (S.match !== "any") tips.push("count a camp when any of it is inside these levels");
      if (S.size > 1) tips.push("allow smaller camps");
      if (S.where || S.lands) tips.push("choose \"All kinds\" of zone and both lands");
      if (S.realm) tips.push("choose Any realm");
      if (!tips.length) tips.push("choose another colour or widen the monster levels");
      return "Try: " + tips.slice(0, 4).join(", or ") + ".";
    }

    /* ---- choosing a camp ---- */
    function select(c, o) {
      o = o || {};
      P.selCamp = c || null;
      if (o.user) { S.sel = c ? c.id : null; WG.setQuery(queryOf(S)); }
      Array.prototype.forEach.call(rowsEl.children, function (rw) {
        var on = !!rw._r && rw._r.c === c;
        rw.classList.toggle("sel", on); if (rw._r) rw.setAttribute("aria-selected", on ? "true" : "false");
        if (on && o.reveal) rw.scrollIntoView({block: "nearest"});
      });
      paintSide(o);
    }
    function paintSide(o) {
      var c = P.selCamp;
      WG.clear(mapHead); WG.clear(detail);
      if (!c) {
        if (P.map) { P.map.destroy(); P.map = null; WG.clear(mapEl); }
        mapHead.appendChild(h("span.wg-dim", "Choose a camp in the list to see it on the zone map."));
        detail.style.display = "none";
        return;
      }
      detail.style.display = "";
      var k = M.kindById.get(c.kind), z = M.zoneById.get(c.zone), pins = hunt.pinsFor(c, S.level);
      mapHead.appendChild(h("span", h("b", k.name), h("span.wg-dim", " in "), ui.zoneLink(z || c.zone, {loc: [c.x, c.y]})));
      mapHead.appendChild(h("a", {href: WG.href("mob/" + k.id)}, "Monster page"));
      if (WG.Map && WG.Map.create) {
        if (!P.map) {
          P.map = WG.Map.create(mapEl, {zone: c.zone, pins: pins, lines: hunt.patrolLines(c, S.level), height: mapHeight(), focus: "c" + c.id,
            onPin: function (pin) {
              if (pin && typeof pin.id === "string" && pin.id.charAt(0) === "c") { var oc = M.campById.get(+pin.id.slice(1)); if (oc && oc !== P.selCamp) select(oc, {user: true, reveal: true}); }
              else if (pin && pin.href) location.hash = pin.href;
            }});
        } else {
          P.map.setData({zone: c.zone, pins: pins, lines: hunt.patrolLines(c, S.level)});
          P.map.focus("c" + c.id, {scroll: false});
        }
      } else if (!mapEl.firstChild) mapEl.appendChild(h("div.wg-empty", "The map viewer is not built yet."));
      paintDetail(c, k, z);
    }
    function paintDetail(c, k, z) {
      var t = hunt.threats(c, S.level), w = P.want, f = hunt.fit(c, w[0], w[1]);
      var locText = WG.Map && WG.Map.loc && z ? WG.Map.loc(z, c.x, c.y) : (z ? z.name : "Zone " + c.zone) + ": loc=" + c.x + "," + c.y;
      var copyBtn = h("button.wg-btn.small", {type: "button", title: "Copy the place of this camp: " + locText, onclick: function () { ui.copy(locText, copyBtn); }}, "Copy /loc");
      var sibs = (M.byKind.get(c.kind) || []).filter(function (o) { return o.zone === c.zone; });
      detail.appendChild(h("div.mu-facts",
        h("div.mu-fact", h("span", "This camp"), h("span", h("b", ui.num(c.n)), c.n === 1 ? " spawn, level " : " spawns, level ", con.el(S.level, c.levelMin, c.levelMax))),
        c.hist.length > 1 ? h("div.mu-fact", h("span", "Level spread"), hunt.spread(c.hist, S.level, 34)) : null,
        h("div.mu-fact", h("span", "In your levels (" + con.rangeText(w[0], w[1]) + ")"), h("span", share(f))),
        h("div.mu-fact", h("span", "Stance"), h("span", hunt.stanceEl(c.stance), c.aggroN && c.aggroRange ? h("span.wg-dim", ", aggro range " + ui.num(c.aggroRange)) : null)),
        h("div.mu-fact", h("span", "Place"), h("span", h("span.wg-nowrap", "loc " + ui.num(c.x) + ", " + ui.num(c.y) + " "), copyBtn))));
      hunt.campNotes(c, S.level).forEach(function (n) { detail.appendChild(n); });
      if (sibs.length > 1) detail.appendChild(h("div.mu-help", {style: {marginTop: "6px"}}, k.name + " has " + sibs.length + " camps in this zone (" + ui.num(sibs.reduce(function (a, o) { return a + o.n; }, 0)) + " spawns). Click a pin to choose another one."));
      detail.appendChild(h("div.mu-h3", "Nearby monsters" + (M.nearDist != null ? " (within " + ui.num(M.nearDist) + " of a spawn of this camp)" : "")));
      var shown = t.list;
      if (!shown.length) detail.appendChild(h("div.mu-ok", t.grey ? "Only monsters that are grey to you: they will not attack you." : "None."));
      else {
        var ul = h("ul.mu-nbl");
        shown.slice(0, 12).forEach(function (x) {
          var li = h("li", x.sev === 3 ? h("b.wg-danger", "Stronger, can attack at the camp: ") : x.sev === 2 ? h("b.wg-warn", "Stronger: ") : x.sev === 1 ? h("b.mu-note", "Can join in: ") : null,
            hunt.neighbour(x, S.level, {wrap: true, full: true}));
          li.style.setProperty("--sev", x.sev ? hunt.SEV_COLOR[x.sev] : "var(--line)");
          ul.appendChild(li);
        });
        detail.appendChild(ul);
        if (shown.length > 12) detail.appendChild(h("div.mu-help", (shown.length - 12) + " more kinds nearby; the monster page lists them all."));
        if (t.grey) detail.appendChild(h("div.mu-help", t.grey + " more " + (t.grey === 1 ? "kind" : "kinds") + " nearby " + (t.grey === 1 ? "is" : "are") + " grey to you and will not attack you."));
      }
      if (P.dropBox) P.dropBox(detail, k);
    }

    /* ---- any change: address, lists, map ---- */
    function changed(key) {
      if (!P.alive) return;
      if (key === "level" || key === "con") { paintCons(); paintBands(); }
      if (key === "level" && lvl.get() !== S.level) lvl.set(S.level);
      if (key === "match") paintSeg();
      compute();
      P.shown = PAGE;
      // keep the chosen camp while it is still in the list; else take the first result
      var keep = P.selCamp && P.rows.some(function (r) { return r.c === P.selCamp; });
      if (!keep) { P.selCamp = P.rows.length ? P.rows[0].c : null; S.sel = null; }
      paintRows();
      paintSide({});
      WG.setQuery(queryOf(S));
    }
    P.changed = changed;
    P.repaint = function () { paintRows(); paintSide({}); };

    rowsEl.addEventListener("click", function (e) {
      if (e.target.closest("a,button")) return;
      var rw = e.target.closest(".mu-row"); if (!rw || !rw._r) return;
      select(rw._r.c, {user: true});
    });
    P.onKey = function (e) {
      if (!P.alive || e.ctrlKey || e.altKey || e.metaKey) return;
      var a = document.activeElement;
      if (a && a !== document.body && a !== rowsEl && !rowsEl.contains(a)) return;      // typing in a field, or the map has the keys
      var d = e.key === "ArrowDown" || e.key === "j" ? 1 : e.key === "ArrowUp" || e.key === "k" ? -1 : 0;
      if (e.key === "Enter" && P.selCamp && (a === rowsEl || a === document.body)) { WG.go("mob/" + P.selCamp.kind); return; }
      if (!d || !P.rows.length) return;
      e.preventDefault();
      var i = P.selCamp ? P.rows.findIndex(function (r) { return r.c === P.selCamp; }) : -1;
      i = Math.max(0, Math.min(P.rows.length - 1, i + d));
      if (i >= P.shown) { P.shown += PAGE; P.selCamp = P.rows[i].c; paintRows(); }
      select(P.rows[i].c, {user: true, reveal: true});
    };
    document.addEventListener("keydown", P.onKey);

    /* ---- first paint ---- */
    paintCons(); paintBands(); paintSeg();
    compute();
    var first = S.sel != null ? M.campById.get(S.sel) : null;
    P.selCamp = first || (P.rows.length ? P.rows[0].c : null);
    paintRows();
    paintSide({});
    if (first) { var selRow = rowsEl.querySelector(".mu-row.sel"); if (selRow) selRow.scrollIntoView({block: "nearest"}); }
    WG.setQuery(queryOf(S));
    if (hunt.attachDrops) hunt.attachDrops(P, M);
  }

  /* After the first paint: load the drops and add each monster's best gear for my character to the rows. */
  hunt.attachDrops = function (P, M) {
    setTimeout(function () {
      if (!P.alive) return;
      hunt.drops().then(function (D) {
        if (!P.alive || !D.hasItems) return;
        P.dropLine = function (k) { return hunt.gearEl(D, k, P.S.level, 3); };
        P.dropBox = function (box, k) {
          var kd = D.of(k.key), n = kd && kd.d ? kd.d.length : 0;
          box.appendChild(h("div.mu-h3", "Drops"));
          var g = hunt.gearEl(D, k, P.S.level, 8);
          if (g) box.appendChild(g);
          box.appendChild(h("div.mu-help", n ? [(g ? "" : "No gear your character can use. "), h("a", {href: WG.href("mob/" + k.id)}, "All " + n + " drops with their chances")] : "This monster has no loot table" + (kd && kd.coin ? ": only coin." : ".")));
        };
        P.repaint();
      }, function () {});
    }, 30);
  };

  /* The header box changed my level or realm while the page is open. */
  WG.onMe(function (me) {
    var P = cur; if (!P || !P.alive) return;
    if (P.mode === "bosses") { if (P.rerender && WG.route().query.level == null) P.rerender(); return; }
    if (!P.changed) return;
    var S = P.S, ch = false;
    if (me.level && me.level !== S.level) { S.level = hunt.clampLevel(me.level, S.level); ch = true; }
    if (!S.realmSet && (me.realm || 0) !== S.realm) {
      S.realm = me.realm || 0; P.F.set("realm", S.realm); ch = true;
    }
    if (ch) P.changed("level"); else if (P.repaint) P.repaint();     // class or realm changed: the "drops for you" lines
  });

  WG.view("hunt", {
    title: "Hunt", nav: true, order: 20,
    render: function (el, params, query) {
      hunt.css();
      var P = (cur = {el: el, alive: true, map: null, S: readState(query), mode: query.mode === "bosses" ? "bosses" : "camps"});
      ui.loading(el, "Loading monsters and camps...");
      return hunt.load().then(function (M) {
        if (!P.alive) return;
        WG.clear(el);
        // two ways in: ordinary camps to hunt, and the list of named, boss, epic and event monsters
        var nBoss = hunt.bossRows(M).length;
        function modeBtn(id, label, small, q) {
          return h("button.mu-mode" + (P.mode === id ? ".active" : ""), {type: "button", "aria-pressed": P.mode === id ? "true" : "false",
            onclick: function () { if (P.mode !== id) WG.go("hunt", q); }}, label, small ? h("small", small) : null);
        }
        var lv = query.level != null ? P.S.level : null;
        el.appendChild(h("div.mu-modes", {role: "group", "aria-label": "What to look for"},
          modeBtn("camps", "Camps to hunt", ui.num(M.camps.length) + " camps", {level: lv}),
          modeBtn("bosses", "Bosses and epic encounters", ui.num(nBoss) + " monsters", {mode: "bosses", level: lv})));
        var body = h("div"); el.appendChild(body); P.el = body;
        if (P.mode === "bosses") {
          P.rerender = function () { var r = WG.route(); WG.views.hunt.leave(); WG.clear(el); WG.views.hunt.render(el, r.params, r.query); };
          return hunt.bossPage(body, query, M, P);
        }
        build(P, M);
      });
    },
    leave: function () {
      var P = cur; cur = null;
      if (!P) return;
      P.alive = false;
      if (P.onKey) document.removeEventListener("keydown", P.onKey);
      if (P.map) { try { P.map.destroy(); } catch (e) {} P.map = null; }
    }
  });
})();
