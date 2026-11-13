/* OFFLINEDAOC Database - one quest (#/quest/<id>), lane Q.
   Who gives it and where, what it needs, the steps as a walk-through with every target linked, a map that follows the
   selected step (the camps of the monster to kill as red pins, the NPC to talk to, the quest's own map marker),
   what the monsters hand over and how often, the rewards, and the chain it sits in.  &step=<n> selects a step. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, Q = WG.quest;
  var live = null, cssDone = false;

  function css() {
    if (cssDone) return; cssDone = true;
    var st = document.createElement("style");
    st.textContent = [
      ".q-two{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.1fr);gap:var(--gap,16px);align-items:start}",
      ".q-mapcol{position:sticky;top:calc(var(--header-h,84px) + 8px)}",
      "@media (max-width:1240px){.q-two{grid-template-columns:minmax(0,1fr)}.q-mapcol{position:static;order:-1}}",
      ".q-steps{display:grid;gap:6px;margin:0;padding:0;list-style:none}",
      ".q-step{display:grid;grid-template-columns:34px minmax(0,1fr);gap:10px;padding:8px 10px;border:1px solid transparent;border-radius:6px;cursor:pointer;background:var(--bg2,rgba(255,255,255,.02))}",
      ".q-step:hover{background:var(--bg3)}",
      ".q-step.on{border-color:var(--focus,#f0cf7a);background:var(--bg3)}",
      ".q-step:focus-visible{outline:2px solid var(--focus,#f0cf7a);outline-offset:1px}",
      ".q-num{width:30px;height:30px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-weight:700;background:var(--bg4,#2b3240);color:var(--text,#e8e8e8)}",
      ".q-step.on .q-num{background:var(--focus,#f0cf7a);color:#1b1b1b}",
      ".q-do{font-weight:600}",
      ".q-say{color:var(--text2);font-style:italic;margin-top:2px}",
      ".q-line{margin-top:3px;color:var(--text2)}",
      ".q-line b{color:var(--text)}",
      ".q-kbd{display:inline-block;padding:0 6px;border:1px solid var(--line,#3a4150);border-radius:4px;background:var(--bg4,#2b3240);font-family:inherit;font-size:.95em}",
      ".q-facts{display:grid;gap:10px}",
      ".q-rw{display:grid;gap:4px}",
      ".q-hand{width:100%;border-collapse:collapse}",
      ".q-hand td,.q-hand th{padding:3px 10px 3px 0;text-align:left;vertical-align:baseline}",
      ".q-hand th{color:var(--dim);font-weight:500}",
      ".q-maphead{display:flex;gap:8px;align-items:baseline;justify-content:space-between;margin-bottom:6px}",
      ".q-cite{color:var(--dim);font-size:12px;margin-top:3px}"
    ].join("\n");
    document.head.appendChild(st);
  }

  /* ---------- small pieces ---------- */
  function headerH() { var hd = document.getElementById("wg-header"), nv = document.getElementById("wg-nav"); return (hd ? hd.offsetHeight : 0) + (nv && nv.parentNode !== hd ? nv.offsetHeight : 0) || 84; }
  function word(w) { return h("span.q-kbd", "[" + w + "]"); }
  function npcEl(t) {
    if (!t) return h("span.wg-dim", "nobody");
    if (t.k === "trainer") return h("b", "your own class trainer");
    var out = [h("b", t.name)];
    if (t.zone != null) out.push(h("span.wg-dim", " in "), Q.zoneEl(t.zone, [t.x, t.y]));
    else if (t.k === "none") out.push(h("span.wg-warn", " (not in this world)"));
    else if (t.k === "event") out.push(h("span.wg-dim", " (appears for the quest)"));
    if (t.made) out.push(h("span.wg-dim", " (placed by the quest script)"));
    return h("span", out);
  }
  function killEl(t) {
    if (!t) return h("span.wg-dim", "the target");
    if (t.keys) { var names = String(t.name).split(", "); return h("span", t.keys.map(function (k, i) { return [i ? ", " : "", mobEl(k, names[i] || k)]; })); }
    if (t.k === "mob") return mobEl(t.key, t.name);
    return h("b", t.name);
  }
  /* monster link with its level range in con colours: "swamp goo (level 0 to 1)" */
  function mobEl(key, name) {
    var k = Q.kind(key), ui = WG.ui;
    if (!k) return h("b", {title: "Not a monster of the hunting data: a quest's own monster, or a townsperson"}, name || key);
    var lo = k.lmin != null ? k.lmin : k.levelMin, hi = k.lmax != null ? k.lmax : k.levelMax;
    return h("span", ui.mobLink(k), h("span.wg-dim", " (level "), ui.level(lo, hi), h("span.wg-dim", ")"));
  }
  function kills(c) { return c >= 0.9995 ? "" : " (about " + (Math.round(10 / c) / 10) + " kills for one)"; }
  function chance(c) { return c >= 0.9995 ? h("b", "guaranteed") : h("b", WG.pct(c) + " a kill"); }
  function locText(zone, x, y) { return WG.Map && WG.Map.loc && WG.data.zones ? WG.Map.loc(zone, x, y) : Q.zoneName(zone) + ": loc=" + x + "," + y; }
  function questsNamed(name) {
    var lc = String(name).toLowerCase(), out = Q.all().filter(function (q) { return q._lc === lc; });
    var mine = out.filter(function (q) { return Q.canTake(q, {level: 0, realm: WG.me.realm, cls: WG.me.cls}); });
    return mine.length ? mine : out;
  }
  function questLinks(ids, name) {
    var qs = (ids || []).map(Q.get).filter(Boolean);
    if (WG.me.cls) { var mine = qs.filter(function (q) { return !q.cls.length || q.cls.indexOf(+WG.me.cls) >= 0; }); if (mine.length) qs = mine; }
    if (!qs.length) return h("span", name || "", h("span.wg-dim", " (not a quest of this world)"));
    var out = [];
    qs.slice(0, 6).forEach(function (q, i) {
      if (i) out.push(", ");
      out.push(Q.link(q, {tags: false}), h("span.wg-dim", " (level " + q.lmin + (q.cls.length && qs.length > 1 ? ", " + Q.classText(q, 2) : "") + ")"));
    });
    if (qs.length > 6) out.push(h("span.wg-dim", " and " + (qs.length - 6) + " more class versions"));
    return h("span", out);
  }

  /* What to do on a step, in one line, with links. */
  function doLine(q, s) {
    var t = s.target, it = s.item || s.col || s.use || (s.takes && s.takes[0]);
    switch (s.t) {
      case "kill": return ["Kill ", s.count > 1 ? s.count + " in all: " : "", killEl(t), t && t.k === "event" ? h("span.wg-dim", " (the quest's own monster)") : null,
        t && t.k === "npc" ? h("span.wg-dim", " (not an ordinary monster)") : null, t && t.k === "none" ? h("span.wg-warn", " (not in this world)") : null,
        t && t.k === "script" ? h("span.wg-dim", " (" + (t.host ? t.host + " takes this name and turns hostile" : "named by a server script") + (s.night ? " at night" : "") + ": " + t.file + ")") : null];
      case "deliver": return ["Give ", it ? Q.itemEl(it) : "the item", " to ", npcEl(t)];
      case "collect": return ["Bring ", s.need > 1 ? s.need + " x " : "", it ? Q.itemEl(it) : "the item", " to ", npcEl(t)];
      case "talk": return ["Talk to ", s.byRealm ? (WG.me.realm && s.byRealm[WG.me.realm] ? h("b", s.byRealm[WG.me.realm].name) : "the NPC of your realm") : npcEl(t)];
      case "whisper": return ["Say ", s.word ? String(s.word).split("|").map(function (w, i) { return [i ? " or " : "", word(w)]; }) : "the word", " to ", npcEl(t)];
      case "search": return ["Type /search at the place", s.area ? [" (within " + s.area[3] + " units, takes " + s.area[4] + " seconds)"] : ""];
      case "use_item": return ["Use ", it ? Q.itemEl(it) : "the item", s.place ? " at the place" : " (anywhere)", s.night ? " at night" : ""];
      case "choose": return ["Choose your reward: say one of the words to ", npcEl(t)];
      case "trade": return ["Hand ", (s.trades || []).map(function (tr, i) { return [i ? " or " : "", Q.itemEl(tr[0])]; }), " to ", npcEl(t)];
      case "quest": return ["Finish ", s.quests && s.quests[1] < s.quests[0].length ? s.quests[1] + " of these quests" : "these quests"];
      case "travel": return ["Go to the place"];
      case "has_item": return ["Get ", it ? Q.itemEl(it) : "the item"];
      case "say": return ["Say aloud \"" + (s.word || "") + "\" at the place", s.night ? " at night" : ""];
      case "group": return ["Be in a group of two or more (a hired companion counts)"];
      case "die_to": return ["Let ", killEl(t), " slay you"];
      default: return [Q.stepName(s.t), t ? [" - ", npcEl(t)] : ""];
    }
  }
  function detailLines(q, s, level) {
    var out = [], ui = WG.ui;
    function line() { out.push(h("div.q-line", Array.prototype.slice.call(arguments))); }
    if (s.count > 1 && s.note == null) line("Needs " + s.count + " kills.");
    if (s.level) line("Level " + s.level + (s.scripted ? " as the quest script sets it" : "") + ".");
    (s.gets || []).forEach(function (g) {
      if (s.t === "kill") line(h("b", "Hands over "), Q.itemEl(g[0]), ": ", chance(g[1]), kills(g[1]), ".", g[1] < 0.9995 ? " A kill without the item does not count." : "");
      else line("You receive ", Q.itemEl(g[0]), g[1] < 0.9995 ? [" (", WG.pct(g[1]), " a try)"] : "", ".");
    });
    if (s.alt && s.alt.length) line("Counts too, with the same item: ", s.alt.map(function (k, i) { return [i ? ", " : "", mobEl(k, k)]; }), ".");
    if (s.own) line("Only the quest's own monster counts, not another of the same name.");
    if (s.drops && s.drops.length) {
      var by = new Map();
      s.drops.forEach(function (d) { var k = d[1] + "|" + d[2]; if (!by.has(k)) by.set(k, {item: d[1], ch: d[2], mobs: []}); by.get(k).mobs.push(d[0]); });
      by.forEach(function (e) {
        line(h("b", "While on this step "), e.mobs.map(function (k, i) { return [i ? ", " : "", mobEl(k, k)]; }), e.mobs.length > 1 ? " each hand over " : " hands over ",
          Q.itemEl(e.item), ": ", chance(e.ch), kills(e.ch), ". It goes straight into the backpack.");
      });
    }
    if (s.from && s.from.length && !(s.drops && s.drops.length)) {
      var seen = {}, list = s.from.filter(function (f) { if (seen[f[0]]) return false; seen[f[0]] = 1; return true; }).slice(0, 6);
      line("Drops as loot from ", list.map(function (f, i) { return [i ? ", " : "", Q.mobEl(f[0], f[0], {level: false}), " " + WG.pct(f[1])]; }), ".");
    }
    if (s.choices && s.choices.length) line(s.choices.map(function (c, i) { return h("div", word(c[0]), " ", Q.itemEl(classVersion(c[1]))); }),
      h("div.wg-dim", "An item your class cannot use is refused."));
    if (s.trades) s.trades.forEach(function (tr) { if (tr[1]) line("For ", Q.itemEl(tr[0]), " you get ", Q.itemEl(tr[1]), "."); });
    if (s.quests) line(s.quests[0].map(function (n, i) { return [i ? ", " : "", questLinks(questsNamed(n).map(function (x) { return x.id; }), n)]; }));
    if (s.spawn) {
      var sp = s.spawn;
      line(sp.peace ? [h("b", sp.name), " appears at the spot to talk to"] : [h("b", sp.n > 1 ? sp.n + " x " + sp.name : sp.name), sp.level ? " (level " + sp.level + ")" : "", sp.walks ? " appears and walks a patrol" : " appears and attacks"],
        sp.r > 0 ? " when you come within " + sp.r + " units of the spot" : " wherever you are",
        sp.escort && sp.escort.length ? [", with ", sp.escort.map(function (e, i) { return (i ? ", " : "") + e[1] + " x " + e[0] + (e[2] ? " (level " + e[2] + ")" : ""); })] : "", ".");
    }
    if (s.grant) line("You are given ", s.grant[0].map(function (id, i) { return [i ? ", " : "", Q.itemEl(classVersion(id))]; }), s.grant[1] ? " by " + s.grant[1] : "", ".");
    if (s.given) line("The quest puts the item in your backpack.");
    if (s.takes && s.takes.length > 1) line("Hands in ", s.takes.map(function (id, i) { return [i ? ", " : "", Q.itemEl(id)]; }), ".");
    if (s.wear && s.wear.length) line(h("b", "You must be wearing "), s.wear.map(function (id, i) { return [i ? ", " : "", Q.itemEl(id)]; }), ".");
    if (s.night && s.t !== "use_item" && s.t !== "say") line(h("b", "Only at night."));
    if (s.hours) line(h("b", "Only up from " + s.hours[0] + ":00 to " + s.hours[1] + ":00"), " game time.");
    if (s.needs && s.needs[1]) line(h("span.wg-dim", "Lost the item? " + s.needs[1] + " gives another."));
    if (s.byRealm) line(Object.keys(s.byRealm).map(function (r) { var b = s.byRealm[r]; return h("div", h("span.realm-" + r, WG.REALMS[r]), ": ", h("b", b.name), " near " + b.town + ", ", Q.zoneEl(b.zone, [b.x, b.y])); }));
    var xp = Q.stepXp(s, level);
    if (xp || s.coin) line("Pays ", xp ? [h("b", ui.num(xp) + " experience"), s.share && xp > s.xp ? " (" + Math.round(s.share * 1000) / 10 + "% of level " + level + ")" : ""] : "", xp && s.coin ? " and " : "", s.coin ? ui.money(s.coin) : "", ".");
    if (s.note) line(String(s.note).replace(/\s*\(see targetByRealm\)\.?/g, "."));
    if (s.cite) out.push(h("div.q-cite", s.cite));
    return out;
  }
  /* The courier quests' journal text is built per realm by the script: fill the names in. */
  function fill(s) {
    var text = s.text, b = s.byRealm && WG.me.realm ? s.byRealm[WG.me.realm] : null;
    if (!s.byRealm) return text;
    var who = b ? b.name : "the NPC named below";
    return text.replace(/\{Courier\}/g, "A courier").replace(/\{Town\}/g, b ? b.town : "the town named below").replace(/\{Zone\}/g, b ? Q.zoneName(b.zone) : "your realm")
      .replace(/\{Innkeeper\}/g, who).replace(/\{HornHandler\}/g, who);
  }
  function classVersion(id) {
    var by = (WG.data.quests.classRewards || {})[id];
    return by && WG.me.cls && by[WG.me.cls] ? by[WG.me.cls] : id;
  }

  /* ---------- map groups for a step ---------- */
  var campIdx = null;
  function campsOf(key) {
    var k = Q.kind(key), d = WG.data.camps;
    if (!k || !d) return [];
    if (!campIdx) { campIdx = new Map(); WG.rows(d.camps).forEach(function (c) { var a = campIdx.get(c.kind); if (!a) campIdx.set(c.kind, (a = [])); a.push(c); }); }
    return (campIdx.get(k.id) || []).map(function (c) { return {c: c, k: k}; });
  }
  function campPins(key, color, legend) {
    return campsOf(key).map(function (e) {
      var c = e.c;
      return {zone: c.zone, x: c.x, y: c.y, z: c.z, kind: c.n > 1 ? "camp" : "spawn", n: c.n, radius: c.r || undefined, color: color || undefined,
        label: e.k.name, sub: "level " + (c.lmin === c.lmax ? c.lmin : c.lmin + " to " + c.lmax) + " - " + c.n + (c.n === 1 ? " spawn" : " spawns"),
        href: WG.href("mob/" + e.k.id), legend: legend || e.k.name};
    });
  }
  function giverPins(q) {
    var list = q.givers || (q.giver ? [q.giver] : []);
    return list.map(function (g) { return {zone: g.zone, x: g.x, y: g.y, kind: "vendor", label: g.name, sub: "gives the quest", legend: "Quest giver"}; });
  }
  function stepPins(q, s) {
    var pins = [], t = s.target;
    if (s.t === "kill" || s.t === "die_to") {
      if (t) (t.keys || [t.key]).forEach(function (k) { if (k) pins = pins.concat(campPins(k)); });
      (s.alt || []).forEach(function (k) { pins = pins.concat(campPins(k, "orange", "Counts too")); });
      if (t && t.zone != null && !pins.length) pins.push({zone: t.zone, x: t.x, y: t.y, kind: "danger", label: t.name, sub: "the target", legend: "Target"});
    } else if (t && t.zone != null) {
      pins.push({zone: t.zone, x: t.x, y: t.y, kind: "vendor", label: t.name, sub: Q.stepName(s.t).toLowerCase() + " (step " + s.n + ")", legend: "Who to see"});
    }
    (s.drops || []).forEach(function (d) { pins = pins.concat(campPins(d[0], null, null)); });
    if (!(s.drops && s.drops.length) && s.from && (s.t === "collect" || s.t === "trade" || s.t === "has_item")) {
      var seen = {};
      s.from.slice(0, 4).forEach(function (f) { if (!seen[f[0]]) { seen[f[0]] = 1; pins = pins.concat(campPins(f[0], "orange", "Drops the item as loot")); } });
    }
    if (s.spawn && s.spawn.zone != null) pins.push({zone: s.spawn.zone, x: s.spawn.x, y: s.spawn.y, kind: s.spawn.peace ? "vendor" : "danger", radius: s.spawn.r > 0 ? s.spawn.r : undefined,
      label: s.spawn.name, sub: (s.spawn.peace ? "appears here" : "appears here and attacks") + (s.spawn.level ? ", level " + s.spawn.level : ""), legend: "Appears for the quest"});
    if (s.place) pins.push({zone: s.place[0], x: s.place[1], y: s.place[2], kind: "point", radius: s.place[3] || undefined, label: s.t === "travel" ? "Go here" : s.t === "say" ? "Speak here" : "Use the item here", sub: "step " + s.n, legend: "The place"});
    if (s.area) pins.push({zone: s.area[0], x: s.area[1], y: s.area[2], kind: "point", radius: s.area[3], label: "/search here", sub: "step " + s.n, legend: "The place"});
    if (s.byRealm) Object.keys(s.byRealm).forEach(function (r) { var b = s.byRealm[r]; if (!WG.me.realm || +r === WG.me.realm) pins.push({zone: b.zone, x: b.x, y: b.y, kind: "vendor", label: b.name, sub: WG.REALMS[r], legend: "Who to see"}); });
    if (s.at) {
      var dup = pins.some(function (p) { return p.zone === s.at[0] && Math.abs(p.x - s.at[1]) < 60 && Math.abs(p.y - s.at[2]) < 60; });
      if (!dup) pins.push({zone: s.at[0], x: s.at[1], y: s.at[2], kind: "point", label: "The game's map marker for step " + s.n, sub: s.t === "kill" ? "where the quest sends you to hunt" : "", legend: "The game's map marker", id: "mk"});
    }
    return pins;
  }
  function turninPins(q) {
    var pins = [], seen = {};
    (q.turnin.from || []).slice(0, 6).forEach(function (f) { if (!seen[f[0]]) { seen[f[0]] = 1; pins = pins.concat(campPins(f[0])); } });
    return pins;
  }
  function group(pins, first) {
    var by = new Map(), order = [];
    pins.forEach(function (p) { var g = by.get(p.zone); if (!g) { by.set(p.zone, (g = {zone: p.zone, pins: []})); order.push(g); } g.pins.push(p); });
    order.sort(function (a, b) { return (b.zone === first) - (a.zone === first) || b.pins.length - a.pins.length; });
    return order;
  }

  /* ---------- the page ---------- */
  function render(el, params, query) {
    var me = (live = {});
    var ui = WG.ui, body = h("div"); el.appendChild(body);
    ui.loading(body, "Loading the quest...");
    return Q.full().then(function () {
      if (live !== me) return;
      WG.clear(body);
      css(); Q.css();
      var q = Q.get(params[0]);
      if (!q) {
        ui.page(body, {title: "Quest not found", crumbs: [["Quests", WG.href("quests")]]});
        body.appendChild(h("div.wg-empty", "There is no quest " + params[0] + " in this world. ", h("a", {href: WG.href("quests")}, "Open the quest finder")));
        return;
      }
      document.title = q.name + " - OFFLINEDAOC Database";
      page(body, q, query, me);
    });
  }

  function page(el, q, query, me) {
    var ui = WG.ui, level = WG.me.level || 1, meta = Q.meta();
    var sub = [q.realm ? ui.realm(q.realm) : h("span.wg-dim", "Any realm"), " · level " + Q.levelText(q), " · " + Q.kindName(q.kind),
      " · ", q.cls.length ? q.cls.map(Q.className).join(", ") : "any class"];
    if (q.part) sub.push(" · part " + q.part[0] + " of " + q.part[1]);
    ui.page(el, {title: q.name, crumbs: [["Quests", WG.href("quests")], [Q.kindName(q.kind), WG.href("quests", {kind: q.kind, any: 1})]], sub: sub});
    var can = Q.canTake(q), why = [];
    if (WG.me.level < q.lmin) why.push("you are level " + WG.me.level + ", it opens at " + q.lmin);
    if (WG.me.level > q.lmax) why.push("you are level " + WG.me.level + ", it closes after " + q.lmax);
    if (WG.me.realm && q.realm && WG.me.realm !== q.realm) why.push("it is a quest of " + WG.REALMS[q.realm]);
    if (WG.me.cls && q.cls.length && q.cls.indexOf(+WG.me.cls) < 0) why.push("it is not for a " + Q.className(WG.me.cls));
    if (!can) el.appendChild(ui.note("Your character cannot take this now: " + why.join("; ") + ".", "warn"));
    (q.warn || []).forEach(function (w) { el.appendChild(ui.note(w, "danger")); });

    var two = h("div.q-two"), left = h("div.wg-stack"), right = h("div.q-mapcol");
    WG.append(two, [left, right]); el.appendChild(two);

    /* ---- start and steps */
    var entries = [];      // {n, node, pins(): [...], first: zone}
    var ol = h("ol.q-steps");
    function entry(num, doNodes, extra, pinsFn, first) {
      var li = h("li.q-step", {tabIndex: 0, role: "button", "aria-label": "Show step " + num + " on the map"}, h("div.q-num", String(num)), h("div", h("div.q-do", doNodes), extra));
      var e = {n: num, node: li, pins: pinsFn, first: first};
      li.addEventListener("click", function (ev) { if (ev.target.closest("a,button")) return; select(e, true); });
      li.addEventListener("keydown", function (ev) {
        var i = entries.indexOf(e);
        if (ev.key === "Enter" || ev.key === " ") { ev.preventDefault(); select(e, true); }
        else if ((ev.key === "ArrowDown" || ev.key === "j") && entries[i + 1]) { ev.preventDefault(); entries[i + 1].node.focus(); select(entries[i + 1]); }
        else if ((ev.key === "ArrowUp" || ev.key === "k") && entries[i - 1]) { ev.preventDefault(); entries[i - 1].node.focus(); select(entries[i - 1]); }
      });
      entries.push(e); ol.appendChild(li);
      return e;
    }
    var g = q.giver, startDo, startExtra = [];
    if (q.start === "turnin") startDo = ["Hand ", Q.itemEl(q.turnin.item), " to ", h("b", g.name), h("span.wg-dim", " in "), Q.zoneEl(g.zone, [g.x, g.y])];
    else if (g) startDo = ["Start: talk to ", h("b", g.name), h("span.wg-dim", " in "), Q.zoneEl(g.zone, [g.x, g.y]), q.accept ? [", then say ", word(q.accept)] : ""];
    else startDo = ["Start: it is given to you"];
    if (g) startExtra.push(h("div.q-line.wg-dim", locText(g.zone, g.x, g.y), g.n > 1 && !q.givers ? " (" + g.n + " NPCs of this name stand in the region)" : ""));
    if (q.giverNote) startExtra.push(h("div.q-line", q.giverNote, q.givers ? [" ", q.givers.map(function (x, i) { return (i ? ", " : "") + x.name; })] : ""));
    if (q.offer) startExtra.push(h("div.q-say", "\"" + q.offer + "\""));
    if (q.how) startExtra.push(h("div.q-line", q.how));
    if (q.startGives && q.startGives.length) startExtra.push(h("div.q-line", "You are given ", q.startGives.map(function (id, i) { return [i ? ", " : "", Q.itemEl(id)]; }), "."));
    if (q.turnin) {
      var t = q.turnin, each = Q.xpAt(q, level);
      startExtra.push(h("div.q-line", "Each one pays ", h("b", ui.num(each) + " experience"), t.xp > 0 ? " (a fixed number)" : " (the experience needed for your level, divided by 50)",
        q.rep ? ", up to " + q.rep + " times" : "", ", while you are level " + Q.levelText(q) + "."));
      if (t.roll) {
        var w = t.roll.weights, parts = [];
        Object.keys(w).forEach(function (k) {
          var p = WG.pct(w[k] / t.roll.total);
          parts.push(k === "xp" ? p + " the experience" : k === "coin" ? p + " coin (" + WG.money(t.roll.coin[0]) + " to " + WG.money(t.roll.coin[1]) + ")" :
            k === "item" ? p + " one of " + t.roll.items.length + " items" : p + " a hostile " + (t.roll.hostile ? t.roll.hostile[0] : "monster") + " appears");
        });
        startExtra.push(h("div.q-line", h("b", "Each item rolls its reward: "), parts.join(", "), "."));
        if (t.roll.items.length) startExtra.push(h("div.q-line", t.roll.items.map(function (id, i) { return [i ? ", " : "", Q.itemEl(id)]; })));
      }
    }
    entry(q.turnin ? "1" : "0", startDo, startExtra, function () { return giverPins(q).concat(q.turnin ? turninPins(q) : []); }, g ? g.zone : null);
    q.steps.forEach(function (s) {
      var extra = [];
      if (s.text) extra.push(h("div.q-say", fill(s)));
      extra = extra.concat(detailLines(q, s, level));
      entry(s.n, doLine(q, s), extra, function () { return stepPins(q, s); }, s.at ? s.at[0] : (s.target && s.target.zone != null ? s.target.zone : null));
    });
    left.appendChild(ui.section(q.turnin ? "Turn-in" : "Walk-through", {count: q.turnin ? null : q.steps.length},
      h("div.wg-dim.wg-small", {style: {marginBottom: "6px"}}, "Click a step (or use the arrow keys) to see it on the map."), ol));

    /* ---- what the monsters hand over */
    if (q.hand && q.hand.length) {
      var rows = q.hand.slice().sort(function (a, b) { return (a[0] || 999) - (b[0] || 999) || b[3] - a[3]; });
      var tb = h("table.q-hand", h("tr", h("th", "Step"), h("th", "Monster"), h("th", "Gives"), h("th", "Chance"), h("th", "How")));
      rows.forEach(function (r) {
        var how = r[4] === "kill" ? "kill step: into the backpack" : r[4] === "drop" ? "on the step: into the backpack" : r[4] === "side" ? "side turn-in: into the backpack" :
          q.turnin ? "ordinary loot" : "loot, only on the quest";
        tb.appendChild(h("tr", h("td", r[0] ? String(r[0]) : h("span.wg-dim", "any")), h("td", mobEl(r[1], r[1])), h("td", Q.itemEl(r[2])),
          h("td.wg-nowrap", chance(r[3]), h("span.wg-dim", kills(r[3]))), h("td.wg-dim", how)));
      });
      left.appendChild(ui.section("What the monsters hand over", {count: rows.length}, tb,
        h("div.wg-dim.wg-small", {style: {marginTop: "6px"}}, "Chances are per kill, as the server rolls them. ", q.turnin ? "" : (meta.rules.kill || ""),
          " Loot chances come from the drop tables (see the monster's page)." )));
    }

    /* ---- rewards */
    var rw = h("div.q-rw"), xpAll = Q.xpAt(q, level);
    if (!q.turnin) {
      if (xpAll) rw.appendChild(h("div", h("b", ui.num(xpAll) + " experience"), q.src === "script" ? "" : " at level " + level,
        q.share && level < 50 ? h("span.wg-dim", " (" + Math.round(q.share * 1000) / 10 + "% of a level, or " + ui.num(q.xp) + " when that is more; fixed " + ui.num(q.xp) + " at 50)") : ""));
      if (q.xpNote) rw.appendChild(h("div.wg-dim", q.xpNote));
      if (q.coin) rw.appendChild(h("div", ui.money(q.coin)));
      var items = Q.rewardItems(q, WG.me.cls && (!q.cls.length || q.cls.indexOf(+WG.me.cls) >= 0) ? +WG.me.cls : 0), byHow = {};
      items.forEach(function (x) { (byHow[x.how + "|" + x.step] = byHow[x.how + "|" + x.step] || []).push(x); });
      Object.keys(byHow).forEach(function (k) {
        var list = byHow[k], how = list[0].how, label = how === "final" ? "At the end: " : how === "choice" ? "Your choice at step " + list[0].step + " (one of): " :
          how === "trade" ? "Traded at step " + list[0].step + ": " : how === "grant" ? "Given at step " + list[0].step + ": " : how === "side" ? "For a side turn-in: " : how === "step" ? "From the kill at step " + list[0].step + ": " : "";
        var byCls = list[0].cls != null && list.some(function (x) { return x.cls !== list[0].cls; });
        if (byCls) {
          var per = {};
          list.forEach(function (x) { (per[x.cls] = per[x.cls] || []).push(x); });
          rw.appendChild(h("div", h("b", "At the end, by class:")));
          Object.keys(per).forEach(function (c) { rw.appendChild(h("div", h("span.wg-dim", Q.className(c) + ": "), per[c].map(function (x, i) { return [i ? ", " : "", Q.itemEl(x.id)]; }))); });
        } else {
          rw.appendChild(h("div", h("b", label), list.map(function (x, i) {
            return [i ? ", " : "", Q.itemEl(x.id), x.versions && !x.own ? h("span.wg-dim", {title: "Each class gets its own version: pick your class in the header to see yours"}, " (a version per class)") : ""];
          })));
        }
      });
      (q.side || []).forEach(function (e) {
        rw.appendChild(h("div.wg-dim", "Side turn-in: give ", Q.itemEl(e.item), " to " + e.to, e.reward ? [" for ", Q.itemEl(classVersion(e.reward))] : "", " (once, from step " + e.fromStep + (e.toStep ? " to " + e.toStep : " on") + ")."));
      });
      if (q.rwNote) rw.appendChild(h("div.wg-dim", q.rwNote));
      if (!rw.firstChild) rw.appendChild(h("div.wg-dim", "No experience, coin or item."));
      left.appendChild(ui.section("Rewards", rw));
    }

    /* ---- requirements and chain */
    var facts = [];
    facts.push(["Level", Q.levelText(q)]);
    facts.push(["Class", q.cls.length ? q.cls.map(Q.className).join(", ") : "Any"]);
    if (q.races) facts.push(["Race", q.races.join(", ")]);
    facts.push(["Can be done", q.rep === 1 ? "once" : q.rep ? q.rep + " times" : "any number of times"]);
    (q.need || []).forEach(function (n) { facts.push(["Needs first", questLinks(n.ids, n.name)]); });
    if (q.reqAny && q.reqAny.length) facts.push(["Needs one of", questLinks(q.reqAny)]);
    if (q.req && q.req.text && !(q.need && q.need.length && q.need[0].ids.length)) facts.push(["Needs first", q.req.text]);
    if (q.during) facts.push(["Only while doing", q.during.map(function (n, i) { return [i ? ", " : "", questLinks(questsNamed(n).map(function (x) { return x.id; }), n)]; })]);
    if (q.excl) facts.push(["Not after starting", q.excl.map(function (n, i) { return [i ? ", " : "", questLinks(questsNamed(n).map(function (x) { return x.id; }), n)]; })]);
    if (q.next) facts.push(["Leads to", questLinks(q.next)]);
    if (q.grpN > 1) {
      var others = Q.all().filter(function (x) { return x.grp === q.grp && x.id !== q.id; });
      facts.push(["Other class versions", h("span", others.map(function (x, i) { return [i ? ", " : "", h("a", {href: Q.href(x)}, Q.classText(x, 4))]; }))]);
    }
    if (q.guide) facts.push(["In the game", "The Quest Guide button (/questguide) has the period walkthrough."]);
    if (q.src === "script") facts.push(["Source", h("span.wg-dim", "A scripted quest: " + q.file)]);
    left.appendChild(ui.section("Requirements and chain", ui.kv(facts),
      h("div.wg-dim.wg-small", {style: {marginTop: "6px"}}, meta.rules.take || "")));
    if (q.notes && q.notes.length) left.appendChild(ui.section("Notes from the quest script", {collapsed: true}, h("ul", q.notes.map(function (n) { return h("li", n); })),
      q.uncertain && q.uncertain.length ? h("div.wg-dim", "Not established: " + q.uncertain.join(" ")) : null));

    /* ---- the map */
    var mapHead = h("div.q-maphead"), mapBox = h("div"), mapNote = h("div.wg-dim", {style: {display: "none", padding: "20px 4px"}});
    right.appendChild(h("div.wg-card", mapHead, mapBox, mapNote));
    var multi = null, current = null;
    function select(e, user) {
      current = e;
      entries.forEach(function (x) { x.node.classList.toggle("on", x === e); });
      WG.clear(mapHead);
      mapHead.appendChild(h("b", e.n === "0" || (q.turnin && e.n === "1") ? (q.turnin ? "Where to hand it in and where it drops" : "Where it starts") : "Step " + e.n));
      var pins = e.pins(), groups = group(pins, e.first);
      mapHead.appendChild(h("span.wg-dim", groups.length ? (pins.length === 1 ? "1 place" : pins.length + " places") + (groups.length > 1 ? " in " + groups.length + " zones" : "") : ""));
      if (!groups.length) {
        mapBox.style.display = "none"; mapNote.style.display = "";
        mapNote.textContent = "This step has no place on a map: " + (e.n === "0" ? "the quest comes to you." : "see the text of the step.");
      } else {
        mapBox.style.display = ""; mapNote.style.display = "none";
        /* preferLayer: in a capital the floor plan is sharper than the painted map; zones without one ignore it */
        if (!multi) { multi = WG.Map.multi(mapBox, groups, {height: 520, order: "given", legend: true, preferLayer: "plan"}); me.multi = multi; }
        else multi.setData(groups);
        multi.show(groups[0].zone);
        try {
          var g0 = groups[0];
          if (g0.pins.length === 1) multi.map.focus(0, {scroll: false});
          else multi.map.fitPins();
        } catch (err) { console.error(err); }
      }
      var qy = {}; if (e.n !== entries[0].n) qy.step = e.n;
      WG.setQuery(qy);
      if (user && window.innerWidth <= 1240) window.scrollTo({top: Math.max(0, right.getBoundingClientRect().top + window.scrollY - headerH() - 8), behavior: "smooth"});
    }
    var first = entries[0];
    if (query.step) entries.forEach(function (e) { if (String(e.n) === String(query.step)) first = e; });
    select(first);
    /* the monsters' camps come from the largest file: show the page first, then add them */
    if (!WG.data.camps) WG.load("camps").then(function () { if (live === me && current) select(current); }, function () {});
  }

  WG.onMe(function () {
    if (!live || WG.route().view !== "quest") return;
    var main = document.getElementById("wg-main"), r = WG.route();
    if (!main) return;
    if (live.multi) { try { live.multi.destroy(); } catch (e) {} }
    WG.clear(main); render(main, r.params, r.query);
  });
  WG.view("quest", {
    title: function (params) { var q = Q.loaded() && Q.get(params[0]); return q ? q.name : "Quest"; },
    navAs: "quests", render: render,
    leave: function () { if (live && live.multi) { try { live.multi.destroy(); } catch (e) {} } live = null; }
  });
})();
