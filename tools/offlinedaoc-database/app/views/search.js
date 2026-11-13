/* OFFLINEDAOC Database - search (P-U): the header box, the box on the home page (WG.ui.searchBox) and #/search?q=.
   Monsters, items, merchants and zones together. The data files are loaded the first time a box gets the focus;
   results appear per kind as each file arrives. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = WG.ui;

  /* ------------------------------------------------------------------ the index */
  var TYPES = [
    {key: "mob", label: "Monsters", one: "Monster", files: ["mobs", "zones"], top: 6},
    {key: "item", label: "Items", one: "Item", files: ["items", "itemmeta"], top: 6},
    {key: "vendor", label: "Merchants", one: "Merchant", files: ["vendors", "zones"], top: 4},
    {key: "zone", label: "Zones", one: "Zone", files: ["zones", "mobs"], top: 4}
  ];
  /* Quests come from lane Q's library (app/lib/quests.js: WG.quest.ready / search), not from an index of mine. */
  var HAS_QUESTS = !!(WG.quest && typeof WG.quest.ready === "function" && typeof WG.quest.search === "function");
  if (HAS_QUESTS) TYPES.splice(2, 0, {key: "quest", label: "Quests", one: "Quest", files: [], top: 4});
  var index = {}, state = {}, waiters = [], started = false;
  function rowsOf(t) { return !t ? [] : (t.cols ? WG.rows(t) : (Array.isArray(t) ? t : [])); }
  function opt(name) { return WG.load(name).then(function (d) { return d[name]; }, function () { return null; }); }
  function entries(list, nameOf) { var out = new Array(list.length); for (var i = 0; i < list.length; i++) { var n = nameOf ? nameOf(list[i]) : list[i].name; out[i] = {n: String(n == null ? "" : n).toLowerCase(), o: list[i]}; } return out; }
  var BUILD = {
    mob: function (d) {   // the kinds of the world, and the monsters only a script creates (mobs.md 5d: summoned bosses, encounter and quest spawns)
      if (!d.mobs) return null;
      var scripted = rowsOf(d.mobs.scripted);
      scripted.forEach(function (m) { m._scripted = true; });
      return entries(rowsOf(d.mobs.kinds)).concat(entries(scripted));
    },
    item: function (d) { return d.items ? entries(rowsOf(d.items)) : null; },
    vendor: function (d) { return d.vendors ? entries(rowsOf(d.vendors.vendors)) : null; },
    zone: function (d) { return d.zones ? entries(rowsOf(d.zones.zones)) : null; }
  };
  function start() {
    if (started) return; started = true;
    TYPES.forEach(function (t) {
      state[t.key] = "loading";
      if (t.key === "quest") {
        Promise.all([WG.quest.ready(), opt("zones")]).then(function () { state.quest = "ready"; }, function () { state.quest = "missing"; })
          .then(function () { waiters.slice().forEach(function (f) { try { f("quest"); } catch (e) { console.error(e); } }); });
        return;
      }
      Promise.all(t.files.map(opt)).then(function (vals) {
        var d = {}; t.files.forEach(function (f, i) { d[f] = vals[i]; });
        var ix = null; try { ix = BUILD[t.key](d); } catch (e) { console.error(e); }
        index[t.key] = ix; state[t.key] = ix ? "ready" : "missing";
        waiters.slice().forEach(function (f) { try { f(t.key); } catch (e) { console.error(e); } });
      });
    });
  }
  function onData(f) { waiters.push(f); return function () { var i = waiters.indexOf(f); if (i >= 0) waiters.splice(i, 1); }; }
  function tokens(q) { return String(q || "").toLowerCase().split(/\s+/).filter(Boolean); }
  /* rank: 0 the whole name, 1 the name starts with it, 2 a word starts with it, 3 anywhere */
  function find(type, q, limit) {
    if (type === "quest") {   // Q's own search (name or giver); one row per quest, class versions merged by Q
      if (state.quest !== "ready" || !String(q || "").trim()) return {list: [], total: 0};
      try { var r = WG.quest.search(q, limit || 100000); return {list: r.list || [], total: r.total || 0}; } catch (e) { console.error(e); return {list: [], total: 0}; }
    }
    var ix = index[type], tk = tokens(q), out = [];
    if (!ix || !tk.length) return {list: [], total: 0};
    var first = tk[0], full = tk.join(" ");
    for (var i = 0; i < ix.length; i++) {
      var n = ix[i].n, p = n.indexOf(first), ok = p >= 0;
      for (var j = 1; ok && j < tk.length; j++) if (n.indexOf(tk[j]) < 0) ok = false;
      if (!ok) continue;
      var score = n === full ? 0 : p === 0 ? 1 : /[\s\-'(]/.test(n.charAt(p - 1)) ? 2 : 3;
      out.push({e: ix[i], s: score});
    }
    out.sort(function (a, b) { return (a.s - b.s) || weight(type, b.e.o) - weight(type, a.e.o) || (a.e.n.length - b.e.n.length) || (a.e.n < b.e.n ? -1 : a.e.n > b.e.n ? 1 : 0); });
    var total = out.length;
    if (limit && out.length > limit) out.length = limit;
    return {list: out.map(function (x) { return x.e.o; }), total: total};
  }
  function weight(type, o) {   // within the same rank: bosses, then common monsters; obtainable items first
    if (type === "mob") { var rk = ui.rank ? ui.rank(o) : ""; return Math.min(o.spawns || 0, 50) + (rk === "event" || rk === "epic" || rk === "boss" ? 100 : 0); }
    if (type === "item") return o.src ? 1 : 0;
    if (type === "zone") return o.kind === "housing" ? 0 : 1;
    return 0;
  }
  WG.search = {start: start, find: find, types: TYPES, state: state, onData: onData};

  /* ------------------------------------------------------------------ how a hit looks */
  function zoneName(id) { var z = WG.Map && WG.Map.zoneById ? WG.Map.zoneById(id) : null; return z ? z.name : ""; }
  function marked(name, q) {
    var tk = tokens(q), low = name.toLowerCase(), p = tk.length ? low.indexOf(tk[0]) : -1;
    if (p < 0) return h("span", name);
    return h("span", name.slice(0, p), h("mark", name.slice(p, p + tk[0].length)), name.slice(p + tk[0].length));
  }
  function itemType(it) { try { if (WG.item && WG.item.typeSlot) return WG.item.typeSlot(WG.item.norm ? WG.item.norm(it) : it); } catch (e) {} return it.cat || ""; }
  var KIND_ONE = {outdoor: "Outdoor zone", dungeon: "Dungeon", city: "Capital city", frontier: "Frontier zone", battleground: "Battleground", housing: "Housing zone"};
  function questLevel(q) { return q ? (q.lmin === q.lmax ? String(q.lmin) : q.lmin + " to " + q.lmax) : ""; }
  function hrefOf(type, o) {
    if (type === "quest") return o.href || WG.href("quest/" + encodeURIComponent(o.id));
    return type === "mob" ? WG.href("mob/" + o.id) : type === "item" ? WG.href("item/" + encodeURIComponent(o.id)) : type === "vendor" ? WG.href("vendor/" + o.id) : WG.href("zones/" + o.id);
  }
  function mobZones(o) {   // zone ids, busiest first; a script-made monster has spots instead of spawns
    if (o.zones) return o.zones.map(function (z) { return Array.isArray(z) ? z[0] : z; });
    var out = []; (o.where || []).forEach(function (w) { if (out.indexOf(w[0]) < 0) out.push(w[0]); });
    return out;
  }
  function mobWhere(o) {
    var zs = mobZones(o); if (!zs.length) return o._scripted ? "appears by script" : "";
    var first = zoneName(Array.isArray(zs[0]) ? zs[0][0] : zs[0]);
    return first + (zs.length > 1 ? " +" + (zs.length - 1) : "");
  }
  function infoOf(type, o) {
    if (type === "mob") return [ui.rankBadge ? ui.rankBadge(o) : null, ui.level(o.lmin, o.lmax), h("span.wg-dim", mobWhere(o))];
    if (type === "item") return [o.level ? h("span.wg-dim", "level " + o.level) : null, h("span.wg-dim", itemType(o))];
    if (type === "quest") return [h("span.wg-dim", "level " + questLevel(o.q)), h("span.wg-dim", o.q && o.q.giver ? "from " + o.q.giver.name : "")];
    if (type === "vendor") return [h("span.wg-dim", o.guild || ""), h("span", {class: o.realm ? "realm-" + o.realm : "wg-dim"}, zoneName(o.zone))];
    return [h("span.wg-dim", KIND_ONE[o.kind] || o.kind || ""), ui.realm(o.realm || 0)];
  }
  function iconOf(type, o) {
    if (type === "item") { try { var ic = WG.item && WG.item.icon ? WG.item.icon(o, 20) : null; if (ic) return ic; } catch (e) {} }
    return h("span.wg-search-dot.t-" + type);
  }

  /* ------------------------------------------------------------------ the box
     WG.ui.searchBox({large, placeholder, value, autofocus, onQuery}) -> element (el.input, el.set(text)).
     With onQuery the box shows no list of its own: it reports the text (the search page uses this). */
  ui.searchBox = function (opts) {
    opts = opts || {};
    var input = h("input", {type: "search", placeholder: opts.placeholder || (HAS_QUESTS ? "Search monsters, items, quests, merchants, zones" : "Search monsters, items, merchants, zones"), spellcheck: false, autocomplete: "off", "aria-label": "Search", value: opts.value || ""});
    var pop = h("div.wg-search-pop", {role: "listbox"}), box = h("div.wg-search" + (opts.large ? ".large" : ""), input, opts.onQuery ? null : pop);
    var rows = [], sel = -1, open = false, off = null;
    function close() { open = false; pop.classList.remove("open"); sel = -1; }
    function paint() {
      var q = input.value.trim();
      WG.clear(pop); rows = []; sel = -1;
      if (!q) { close(); return; }
      var any = false, pending = [];
      TYPES.forEach(function (t) {
        if (state[t.key] === "loading" || !state[t.key]) { pending.push(t.label.toLowerCase()); return; }
        var r = find(t.key, q, opts.large ? t.top + 2 : t.top);
        if (!r.total) return;
        any = true;
        pop.appendChild(h("div.wg-search-head", h("span", t.label), h("a", {href: WG.href("search", {q: q, type: t.key}), tabIndex: -1, onclick: close}, r.total > r.list.length ? "all " + ui.num(r.total) : ui.num(r.total))));
        r.list.forEach(function (o) {
          var a = h("a.wg-search-row", {href: hrefOf(t.key, o), role: "option", tabIndex: -1, onclick: function () { close(); input.blur(); }},
            iconOf(t.key, o), h("span.nm", {class: (t.key === "item" || t.key === "zone") && o.realm ? "realm-" + o.realm : t.key === "quest" && o.q && o.q.realm ? "realm-" + o.q.realm : ""}, marked(o.name || String(o.id), q)), h("span.info", infoOf(t.key, o)));
          rows.push(a); pop.appendChild(a);
        });
      });
      if (pending.length) pop.appendChild(h("div.wg-search-note.wg-loading", "Loading " + pending.join(", ") + "..."));
      else if (!any) pop.appendChild(h("div.wg-search-note", "Nothing is called that. Try part of a name."));
      if (any) { var all = h("a.wg-search-row.all", {href: WG.href("search", {q: q}), tabIndex: -1, onclick: function () { close(); input.blur(); }}, "All results for “" + q + "”", h("span.info.wg-dim", "Enter")); rows.push(all); pop.appendChild(all); }
      open = true; pop.classList.add("open");
    }
    function select(i) {
      if (!rows.length) return;
      if (sel >= 0 && rows[sel]) rows[sel].classList.remove("sel");
      sel = i < -1 ? rows.length - 1 : i >= rows.length ? -1 : i;
      if (sel >= 0) { rows[sel].classList.add("sel"); rows[sel].scrollIntoView({block: "nearest"}); }
    }
    var report = opts.onQuery ? ui.debounce(function () { opts.onQuery(input.value.trim()); }, 140) : null;
    var repaint = ui.debounce(paint, 70);
    input.addEventListener("focus", function () { start(); if (!opts.onQuery) { if (!off) off = onData(function () { if (!document.body.contains(input)) { off(); off = null; return; } if (document.activeElement === input) paint(); }); if (input.value.trim()) paint(); } });
    input.addEventListener("input", function () { start(); if (report) report(); else repaint(); });
    input.addEventListener("keydown", function (e) {
      if (opts.onQuery) { if (e.key === "Enter") { report.cancel(); opts.onQuery(input.value.trim()); } return; }
      if (e.key === "ArrowDown") { e.preventDefault(); if (!open) paint(); else select(sel + 1); }
      else if (e.key === "ArrowUp") { e.preventDefault(); select(sel - 1); }
      else if (e.key === "Escape") { if (open) { e.preventDefault(); close(); } else input.blur(); }
      else if (e.key === "Enter") {
        var q = input.value.trim(); if (!q) return;
        e.preventDefault(); repaint.cancel();
        var href = sel >= 0 && rows[sel] ? rows[sel].getAttribute("href") : WG.href("search", {q: q});
        close(); input.blur(); location.hash = href;
      }
    });
    input.addEventListener("blur", function () { setTimeout(function () { if (document.activeElement !== input && !pop.contains(document.activeElement)) close(); }, 160); });
    pop.addEventListener("mousedown", function (e) { e.preventDefault(); });   // keep the focus in the box while a row is clicked
    box.input = input;
    box.set = function (text) { input.value = text == null ? "" : text; };
    if (opts.autofocus) setTimeout(function () { try { input.focus({preventScroll: true}); var n = input.value.length; input.setSelectionRange(n, n); } catch (e) {} }, 0);
    return box;
  };

  /* the header box, and "/" to jump to it */
  var slot = document.getElementById("wg-search"), headBox = null;
  if (slot) { WG.clear(slot); headBox = ui.searchBox({placeholder: (HAS_QUESTS ? "Search monsters, items, quests, merchants, zones" : "Search monsters, items, merchants, zones") + "  ( / )"}); slot.appendChild(headBox); }
  document.addEventListener("keydown", function (e) {
    if (e.key !== "/" || e.ctrlKey || e.metaKey || e.altKey || !headBox) return;
    var t = e.target, tag = t && t.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || (t && t.isContentEditable)) return;
    var pageBox = document.querySelector("#wg-main .wg-search input");
    e.preventDefault(); (pageBox || headBox.input).focus(); (pageBox || headBox.input).select();
  });

  /* ------------------------------------------------------------------ #/search?q= */
  var SRC = [[1, "drop"], [2, "one-time drop"], [4, "merchant"], [8, "quest"], [16, "craft"], [32, "starting gear"], [64, "quest step"], [128, "summoned boss"]];   // items.srcBits
  function tableFor(type, box, list, my) {
    var cols;
    if (type === "mob") cols = [
      {key: "name", label: "Monster", render: function (o) { var b = ui.rankBadge ? ui.rankBadge(o) : null; return b ? [ui.mobLink(o), " ", b] : ui.mobLink(o); }},
      {key: "lmin", label: "Level", num: true, width: 90, sort: function (o) { return o.lmin + o.lmax / 1000; }, render: function (o) { return ui.level(o.lmin, o.lmax, my); }},
      {key: "zones", label: "Where", sort: function (o) { return mobWhere(o); }, render: function (o) { var zs = mobZones(o); if (!zs.length) return o._scripted ? h("span.wg-dim", {title: o.how || null}, "the script decides") : ""; return [ui.zoneLink(zs[0], {query: o._scripted ? {focus: "x" + o.id} : {kind: o.id}}), zs.length > 1 ? h("span.wg-dim", " and " + (zs.length - 1) + " more") : null]; }},
      {key: "spawns", label: "Spawns", num: true, width: 80, render: function (o) { return o._scripted ? h("span.wg-dim", {title: o.how || "Not standing in the world: a script makes it appear"}, "by script") : o.spawns; }},
      {key: "ag", label: "Stance", width: 150, title: "How it stands towards a new character, in the game's words. Only aggressive monsters attack first.", sort: function (o) { return ui.stanceWord(o) + (1 - (o.spawns ? o.ag / o.spawns : 0)).toFixed(3); }, render: function (o) { return ui.stance(o); }}];
    else if (type === "item") cols = [
      {key: "name", label: "Item", render: function (o) { return ui.itemLink(o); }},
      {key: "level", label: "Level", num: true, width: 70, render: function (o) { return o.level || ""; }},
      {key: "type", label: "Type", sort: function (o) { return itemType(o); }, render: function (o) { return itemType(o); }},
      {key: "realm", label: "Realm", width: 90, render: function (o) { return o.realm ? ui.realm(o.realm) : h("span.wg-dim", "all"); }},
      {key: "src", label: "Comes from", sort: function (o) { return o.src || 0; }, render: function (o) { var s = SRC.filter(function (b) { return o.src & b[0]; }).map(function (b) { return b[1]; }); return s.join(", "); }}];
    else if (type === "quest") cols = [
      {key: "name", label: "Quest", render: function (o) { return WG.quest.link ? WG.quest.link(o.q) : h("a", {href: hrefOf("quest", o)}, o.name); }},
      {key: "lmin", label: "Level", num: true, width: 90, sort: function (o) { return o.q.lmin + o.q.lmax / 1000; }, render: function (o) { return questLevel(o.q); }},
      {key: "realm", label: "Realm", width: 90, sort: function (o) { return o.q.realm || 0; }, render: function (o) { return ui.realm(o.q.realm || 0); }},
      {key: "giver", label: "Given by", sort: function (o) { return o.q.giver ? o.q.giver.name : ""; }, render: function (o) { var g = o.q.giver; return g ? [g.name, g.zone != null ? [h("span.wg-dim", " in "), ui.zoneLink(g.zone, {loc: [g.x, g.y]})] : null] : h("span.wg-dim", "no giver: it starts another way"); }},
      {key: "can", label: "For my character", width: 140, sort: function (o) { return WG.quest.canTake && WG.quest.canTake(o.q) ? 0 : 1; }, render: function (o) { return !WG.quest.canTake ? "" : WG.quest.canTake(o.q) ? "yes" : h("span.wg-dim", "no"); }}];
    else if (type === "vendor") cols = [
      {key: "name", label: "Merchant", render: function (o) { return ui.vendorLink(o); }},
      {key: "guild", label: "Title"},
      {key: "n", label: "Goods", num: true, width: 70},
      {key: "zone", label: "Zone", sort: function (o) { return zoneName(o.zone); }, render: function (o) { return ui.zoneLink(o.zone, {query: {focus: "v" + o.id}}); }},
      {key: "realm", label: "Realm", width: 90, render: function (o) { return ui.realm(o.realm || 0); }}];
    else cols = [
      {key: "name", label: "Zone", render: function (o) { return ui.zoneLink(o); }},
      {key: "kind", label: "Kind", render: function (o) { return KIND_ONE[o.kind] || o.kind; }},
      {key: "realm", label: "Realm", width: 90, render: function (o) { return ui.realm(o.realm || 0); }},
      {key: "expansion", label: "Part of", render: function (o) { return o.expansion === "si" ? "Shrouded Isles" : "Classic"; }},
      {key: "spawns", label: "NPCs", num: true, width: 80, title: "Everything that stands in the zone: monsters, merchants, guards"}];
    return ui.table(box, {columns: cols, rows: list, pageSize: 10, rowHref: function (o) { return hrefOf(type, o); }, stickyHead: false});
  }
  function renderSearch(el, params, query) {
    var q = query.q || "", only = query.type || "", body = h("div.wg-stack");
    ui.page(el, {title: "Search"});
    var box = ui.searchBox({large: true, value: q, autofocus: true, onQuery: function (text) { q = text; WG.setQuery({q: q || null, type: only || null}); draw(); }});
    el.appendChild(box); el.appendChild(body);
    start();
    var off = onData(function () { draw(); });
    live = {off: off};
    function draw() {
      WG.clear(body);
      document.title = (q ? q + " - " : "") + "Search - OFFLINEDAOC Database";
      if (!q) { body.appendChild(ui.empty("Type part of a name: a monster, an item, " + (HAS_QUESTS ? "a quest, " : "") + "a merchant or a zone.")); return; }
      var any = false, pending = [], my = WG.me.level || 1;
      var chips = h("div.wg-search-types");
      body.appendChild(chips);
      TYPES.forEach(function (t) {
        if (state[t.key] === "loading" || !state[t.key]) { pending.push(t.label.toLowerCase()); return; }
        if (state[t.key] === "missing") { if (!only || only === t.key) body.appendChild(ui.note("The " + t.label.toLowerCase() + " data is not built yet.", "warn")); return; }
        var r = find(t.key, q, 0);
        chips.appendChild(ui.chip(t.label, {count: r.total, active: only === t.key, onclick: function () { only = only === t.key ? "" : t.key; WG.setQuery({q: q, type: only || null}); draw(); }}));
        if (!r.total || (only && only !== t.key)) return;
        any = true;
        var tb = h("div");
        body.appendChild(ui.section(t.label, {count: r.total}, tb));
        tableFor(t.key, tb, r.list, my);
      });
      if (pending.length) body.appendChild(h("div.wg-empty.wg-loading", "Loading " + pending.join(", ") + "..."));
      else if (!any) body.appendChild(ui.empty(only ? "No " + only + " is called that. Click the chip again to search everything." : "Nothing is called “" + q + "”. Names are matched by their parts: try a shorter piece."));
    }
    draw();
  }
  var live = null;
  WG.view("search", {
    title: "Search",
    render: renderSearch,
    leave: function () { if (live && live.off) live.off(); live = null; }
  });
})();
