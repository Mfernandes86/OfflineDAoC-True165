/* OFFLINEDAOC Database - zone list (#/zones) and zone page (#/zones/<id>) (P-U).
   Data: zones.js (P-D), mobs.js + camps.js (M-D), vendors.js + routes.js (V-D). Only the zone list needs zones and
   mobs; the zone page shows the map at once and fills in the rest as the files arrive. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = WG.ui;

  var KIND_ORDER = ["outdoor", "dungeon", "city", "frontier", "battleground", "housing"];
  var KIND_MANY = {outdoor: "Outdoor zones", dungeon: "Dungeons", city: "Capital cities", frontier: "Frontier", battleground: "Battlegrounds", housing: "Housing"};
  var KIND_ONE = {outdoor: "Outdoor zone", dungeon: "Dungeon", city: "Capital city", frontier: "Frontier zone", battleground: "Battleground", housing: "Housing zone"};
  var REALM_HEAD = ["Shared by the realms", "Albion", "Midgard", "Hibernia"];
  var CONS = ["grey", "green", "blue", "yellow", "orange", "red", "purple"];
  var CON_LABEL = {grey: "Grey", green: "Green", blue: "Blue", yellow: "Yellow", orange: "Orange", red: "Red", purple: "Purple"};

  function opt(name) { return WG.load(name).then(function (d) { return d[name]; }, function () { return null; }); }
  function rowsOf(t) { return !t ? [] : (t.cols ? WG.rows(t) : t); }
  function conOf(level, my) { return ui.conName(level, my) || ""; }
  /* The level most of a camp or zone has: [[level, expected count], ...] -> {lo, hi, mode, total}; lo..hi holds the
     middle 90 % of the monsters. */
  function spread(levels) {
    var total = 0, i, best = -1, mode = null;
    for (i = 0; i < levels.length; i++) { total += levels[i][1]; if (levels[i][1] >= best) { best = levels[i][1]; mode = levels[i][0]; } }
    if (!total) return null;
    var acc = 0, lo = null, hi = null;
    for (i = 0; i < levels.length; i++) { acc += levels[i][1]; if (lo === null && acc >= total * 0.05) lo = levels[i][0]; if (hi === null && acc >= total * 0.95) hi = levels[i][0]; }
    return {lo: lo, hi: hi == null ? levels[levels.length - 1][0] : hi, mode: mode, total: total};
  }
  function nearMyLevel(levels, my) {   // expected monsters that con blue, yellow or orange
    if (!WG.con || !levels) return null;
    var n = 0;
    for (var i = 0; i < levels.length; i++) { var c = conOf(levels[i][0], my); if (c === "blue" || c === "yellow" || c === "orange") n += levels[i][1]; }
    return n;
  }
  function zoneStats(mobs) { return mobs && mobs.zones ? WG.index(rowsOf(mobs.zones), "zone") : new Map(); }

  /* ================================================================== the list */
  var LIST_DEFS = [
    {key: "q", label: "Zone name", type: "text", placeholder: "part of a name", width: 190},
    {key: "realm", label: "Realm", type: "chips", options: [{value: 1, label: "Albion", color: 1}, {value: 2, label: "Midgard", color: 2}, {value: 3, label: "Hibernia", color: 3}, {value: 0, label: "Shared", title: "Zones of no single realm: Darkness Falls, the battlegrounds, Summoner's Hall"}]},
    {key: "kind", label: "Kind", type: "chips", options: KIND_ORDER.map(function (k) { return {value: k, label: KIND_MANY[k]}; })},
    {key: "tag", label: "Special", type: "chips", options: [], hidden: true},
    {key: "sort", label: "Order", type: "select", def: "level", options: [["level", "By monster level"], ["name", "By name"], ["mine", "Most monsters near my level"]]}
  ];
  function renderList(el, query) {
    ui.page(el, {title: "Zones", sub: "Every zone of True 1.65, Classic and Shrouded Isles. Open one for its map, its monster camps, merchants and stable masters."});
    var bar = h("div"), body = h("div");
    WG.append(el, [bar, body]);
    ui.loading(body, "Loading the zones...");
    var mine = current;
    return Promise.all([WG.load("zones"), opt("mobs")]).then(function (res) {
      if (current !== mine) return;
      var zones = rowsOf(res[0].zones.zones), stats = zoneStats(res[1]);
      var q0 = {}; for (var k in query) q0[k] = query[k];
      if (!("realm" in q0) && WG.me.realm) q0.realm = String(WG.me.realm);
      var defs = ui.filters.fromQuery(LIST_DEFS, q0);
      var f = ui.filters(bar, defs, function (v) { var q = f.query(); if (v.realm === "" && WG.me.realm) q.realm = "-"; WG.setQuery(q); counts(v); draw(v); });
      function counts(v) {   // how many zones each chip would show, so dungeons, frontiers and battlegrounds are easy to find
        var text = (v.q || "").trim().toLowerCase(), byRealm = [0, 0, 0, 0], byKind = {}, byTag = {};
        zones.forEach(function (z) {
          if (text && z.name.toLowerCase().indexOf(text) < 0) return;
          var realmOk = v.realm === "" || v.realm == null || +v.realm === (z.realm || 0) || (+v.realm > 0 && !z.realm);
          if (!v.kind || v.kind === z.kind) byRealm[z.realm || 0]++;
          if (realmOk) { byKind[z.kind] = (byKind[z.kind] || 0) + 1; tagsOf(z, stats.get(z.id)).forEach(function (t) { byTag[t] = (byTag[t] || 0) + 1; }); }
        });
        f.setOptions("realm", LIST_DEFS[1].options.map(function (o) { return {value: o.value, label: o.label, color: o.color, title: o.title, count: byRealm[o.value]}; }));
        f.setOptions("kind", KIND_ORDER.map(function (k) { return {value: k, label: KIND_MANY[k], count: byKind[k] || 0}; }));
        var tags = Object.keys(byTag).sort();
        f.setOptions("tag", tags.map(function (t) { return {value: t, label: TAG_LABEL[t.toLowerCase()] || cap(t), count: byTag[t]}; }));
        f.show("tag", tags.length > 0);
      }
      counts(f.values());
      current.onMe = function () { draw(f.values()); };
      draw(f.values());

      function draw(v) {
        WG.clear(body);
        var my = WG.me.level || 1, text = (v.q || "").trim().toLowerCase();
        var list = zones.filter(function (z) {
          if (text && z.name.toLowerCase().indexOf(text) < 0) return false;
          // a realm also shows the zones no realm owns (Darkness Falls, battlegrounds): its players go there too
          if (v.realm !== "" && v.realm != null && +v.realm !== (z.realm || 0) && !(+v.realm > 0 && !z.realm)) return false;
          if (v.kind && v.kind !== z.kind) return false;
          if (v.tag && tagsOf(z, stats.get(z.id)).indexOf(v.tag) < 0) return false;
          return true;
        }).map(function (z) {
          var st = stats.get(z.id), sp = st && st.levels ? spread(st.levels) : null;
          return {z: z, st: st, sp: sp, near: st ? nearMyLevel(st.levels, my) : null};
        });
        if (!list.length) { body.appendChild(ui.empty("No zone matches. Clear a filter to see more.")); return; }
        var cmp = v.sort === "name" ? function (a, b) { return a.z.name < b.z.name ? -1 : a.z.name > b.z.name ? 1 : 0; }
          : v.sort === "mine" ? function (a, b) { return (b.near || 0) - (a.near || 0) || (a.z.id - b.z.id); }
          : function (a, b) { return (a.sp ? a.sp.lo : 999) - (b.sp ? b.sp.lo : 999) || (a.sp ? a.sp.hi : 0) - (b.sp ? b.sp.hi : 0) || (a.z.name < b.z.name ? -1 : 1); };
        if (v.sort === "mine") {   // one ranked grid: where to go at my level
          body.appendChild(h("h2.wg-zone-group", "Most monsters that con blue, yellow or orange to level " + my));
          body.appendChild(grid(list.filter(function (e) { return e.near > 0; }).sort(cmp), my, true));
          return;
        }
        var realms = [1, 2, 3, 0];
        if (WG.me.realm) realms = [WG.me.realm].concat(realms.filter(function (r) { return r !== WG.me.realm; }));
        realms.forEach(function (r) {
          var inRealm = list.filter(function (e) { return (e.z.realm || 0) === r; });
          if (!inRealm.length) return;
          var sec = h("section.wg-zone-realm", h("h2", {class: r ? "realm-" + r : ""}, REALM_HEAD[r], h("span.wg-count", inRealm.length + (inRealm.length === 1 ? " zone" : " zones"))));
          KIND_ORDER.forEach(function (kind) {
            var of = inRealm.filter(function (e) { return e.z.kind === kind; }).sort(cmp);
            if (!of.length) return;
            sec.appendChild(h("h3.wg-zone-group", KIND_MANY[kind]));
            sec.appendChild(grid(of, my, false));
          });
          body.appendChild(sec);
        });
      }
    });
  }
  function grid(list, my, showNear) {
    var g = h("div.wg-zone-grid");
    list.forEach(function (e) {
      var z = e.z, st = e.st;
      var thumb = z.thumb ? h("img", {src: z.thumb, alt: "", loading: "lazy", width: 64, height: 64}) : h("span.nothumb", "no map");
      var line;
      if (e.sp) line = h("div.lv", "Level ", ui.level(e.sp.lo, e.sp.hi, my), e.st.lmin < e.sp.lo || e.st.lmax > e.sp.hi ? h("span.wg-dim", {title: "The middle 90 % of the monsters are level " + e.sp.lo + " to " + e.sp.hi + "; the lowest is " + e.st.lmin + ", the highest " + e.st.lmax}, " (all " + e.st.lmin + " to " + e.st.lmax + ")") : null);
      else if (z.bg) line = h("div.lv.wg-dim", "For levels " + z.bg[0] + " to " + z.bg[1]);
      else line = h("div.lv.wg-dim", "No monsters");
      var facts = [];
      if (showNear && e.near != null) facts.push(ui.num(e.near) + " near your level");
      if (st && st.spawns) facts.push(ui.num(st.spawns) + " monsters"); if (st && st.camps && !showNear) facts.push(ui.num(st.kinds) + " kinds");
      var tags = tagBadges(z, st);
      var a = h("a.wg-zone-card", {href: WG.href("zones/" + z.id)}, h("span.thumb", thumb),
        h("span.body", h("span.name", {class: z.realm ? "realm-" + z.realm : ""}, h("span.nm", z.name), z.expansion === "si" ? h("span.wg-badge", {title: "Shrouded Isles"}, "SI") : null), line,
          h("div.facts.wg-dim", (showNear ? [KIND_ONE[z.kind] || z.kind] : []).concat(facts).join(" · ")), tags.length ? h("div.tags", tags) : null));
      g.appendChild(a);
    });
    return g;
  }

  /* ================================================================== one zone */
  function campsOfZone(camps, zoneId) {
    if (!camps) return [];
    if (!camps._byZone) { var m = (camps._byZone = new Map()); rowsOf(camps.camps).forEach(function (c) { var a = m.get(c.zone); if (!a) m.set(c.zone, a = []); a.push(c); }); }
    return camps._byZone.get(zoneId) || [];
  }
  function mapHeight() { return Math.max(440, Math.min(800, window.innerHeight - 330)); }
  function cap(t) { return t ? t.charAt(0).toUpperCase() + t.slice(1) : t; }
  /* Zone tags (M-D adds them to mobs.zones: `tags`): "epic" dungeon, "raid" zone ... Shown as badges; a zone with
     none shows none. Accepts an array or a comma separated text. */
  var TAG_LABEL = {epic: "Epic dungeon", raid: "Raid zone", df: "Darkness Falls", "frontier dungeon": "Frontier dungeon", "starter dungeon": "Starter dungeon"};   // mobs.md 5b
  var TAG_TIP = {epic: "One of the three epic dungeons: a realm event ends here", raid: "A realm event (a raid) ends in this zone", df: "Darkness Falls", "frontier dungeon": "A dungeon under the frontier that the realms share", "starter dungeon": "A first dungeon for new characters"};
  function tagsOf(z, st) {
    var t = (st && st.tags) || (z && z.tags) || [];
    if (typeof t === "string") t = t.split(",");
    return Array.isArray(t) ? t.map(function (x) { return String(x).trim(); }).filter(Boolean) : [];
  }
  /* Badges for a card or a page head: the most telling tags only ("Epic dungeon" already says raid; the zone
     called Darkness Falls needs no badge that says so). The filter chips on the list offer every tag. */
  function tagBadges(z, st) {
    var tags = tagsOf(z, st);
    return tags.filter(function (t) { return t !== "df" && !(t === "raid" && tags.indexOf("epic") >= 0); })
      .map(function (t) { return h("span.wg-badge.tag-" + t.toLowerCase().replace(/[^a-z0-9]+/g, "-"), {title: TAG_TIP[t] || null}, TAG_LABEL[t.toLowerCase()] || cap(t)); });
  }

  function renderZone(el, id, query) {
    ui.loading(el, "Loading the zone...");
    return WG.load("zones").then(function (d) {
      WG.clear(el);
      var zone = WG.Map.zoneById(id);
      if (!zone) { el.appendChild(ui.empty("There is no zone " + id + " in True 1.65. It may belong to a later expansion.")); el.appendChild(h("p", {style: {textAlign: "center"}}, h("a", {href: WG.href("zones")}, "All zones"))); return; }
      document.title = zone.name + " - OFFLINEDAOC Database";
      var regions = rowsOf(d.zones.regions), region = regions.filter(function (r) { return r.id === zone.region; })[0];
      var sub = [h("span", {class: zone.realm ? "realm-" + zone.realm : "wg-dim"}, zone.realm ? WG.REALMS[zone.realm] : "No realm"), " · ", KIND_ONE[zone.kind] || zone.kind,
        " · ", zone.expansion === "si" ? "Shrouded Isles" : "Classic", zone.bg ? " · for levels " + zone.bg[0] + " to " + zone.bg[1] : null,
        region && region.zones.length > 1 ? [" · region ", region.name] : null];
      ui.page(el, {title: zone.name, sub: sub, crumbs: [["Zones", WG.href("zones")], [zone.realm ? WG.REALMS[zone.realm] : "Shared", WG.href("zones", {realm: zone.realm || 0})], [KIND_MANY[zone.kind] || zone.kind, WG.href("zones", {realm: zone.realm || 0, kind: zone.kind})]]});

      var S = {kind: query.kind ? +query.kind : 0, show: {camps: true, singles: query.singles !== "0", vendors: true, stables: true, gates: true, scripted: true, quests: true, routes: query.routes === "1"}, pins: [], data: {}, pinIndex: {}};
      var showPref = WG.pref("zone.show"); if (showPref) for (var k in showPref) if (k in S.show && !(k in {routes: 1})) S.show[k] = !!showPref[k];
      if (query.singles === "0") S.show.singles = false;
      var layerBar = h("div.wg-zone-layers"), mapBox = h("div"), side = h("div.wg-zone-side"), only = h("div.wg-zone-only");
      el.appendChild(h("div.wg-zone-page", h("div.wg-zone-main", layerBar, only, mapBox, zone.desc ? h("p.wg-zone-desc.wg-dim", zone.desc) : null), side));
      var map = WG.Map.create(mapBox, {zone: zone, height: mapHeight(), pins: [], onPin: function (pin, i) { map.focus(i, {zoom: false, scroll: false}); }});
      current.map = map;
      if (query.x != null && query.y != null && isFinite(+query.x) && isFinite(+query.y)) map.mark(+query.x, +query.y);
      ui.loading(side, "Loading monsters, merchants and stables...");

      // lane Q's library, when it is there: the quests given in this zone and the quest steps that happen here
      var questsReady = WG.quest && typeof WG.quest.ready === "function" && typeof WG.quest.forZone === "function" ? WG.quest.ready().then(function () { return true; }, function () { return false; }) : Promise.resolve(false);
      return Promise.all([opt("mobs"), opt("camps"), opt("vendors"), opt("routes"), questsReady]).then(function (r) {
        if (current.map !== map) return;
        var mobs = r[0], vendors = r[2], routes = r[3];
        var D = S.data;
        D.kinds = mobs ? WG.index(rowsOf(mobs.kinds), "id") : new Map();
        D.stat = zoneStats(mobs).get(zone.id) || null;
        D.camps = campsOfZone(r[1], zone.id);
        D.campById = new Map(); D.camps.forEach(function (c) { D.campById.set(c.id, c); });
        D.vendorKinds = {}; if (vendors && vendors.kinds) vendors.kinds.forEach(function (kv) { D.vendorKinds[kv[0]] = kv[1]; });
        D.vendors = vendors ? rowsOf(vendors.vendors).filter(function (v) { return v.zone === zone.id && !v.stable; }) : [];
        D.stables = routes ? rowsOf(routes.stables).filter(function (s) { return s.zone === zone.id; }) : [];
        D.stableById = routes ? WG.index(rowsOf(routes.stables), "id") : new Map();
        D.routes = routes ? rowsOf(routes.routes) : [];
        D.gates = [];   // zone points that start here; several that lead to the same zone from the same doorway (within 300 units) are one way out
        if (routes && routes.gates) routes.gates.forEach(function (g) {
          if (!g.from || !g.to || g.from.zone !== zone.id) return;
          if (D.gates.some(function (o) { return o.to.zone === g.to.zone && Math.abs(o.from.x - g.from.x) < 300 && Math.abs(o.from.y - g.from.y) < 300; })) return;
          D.gates.push(g);
        });
        D.teleporters = routes && routes.teleporters ? routes.teleporters.filter(function (t) { return t.zone === zone.id; }) : [];
        D.paths = mobs && mobs.paths ? rowsOf(mobs.paths).filter(function (p) { return p.zone === zone.id; }) : [];
        // monsters that only a script creates (summoned bosses, encounter and quest spawns: mobs.md 5d) and appear in this zone
        D.scripted = mobs && mobs.scripted ? rowsOf(mobs.scripted).filter(function (m) { return (m.where || []).some(function (w) { return w[0] === zone.id; }); }) : [];
        D.events = mobs && mobs.events ? rowsOf(mobs.events).filter(function (e) { return e.zone === zone.id; }) : [];
        D.byName = function (name) {   // a boss name of an event -> its kind or script-made monster
          var key = String(name).toLowerCase(), all = rowsOf(mobs && mobs.kinds).concat(rowsOf(mobs && mobs.scripted));
          for (var i = 0; i < all.length; i++) if (String(all[i].key).trim() === key || String(all[i].name).toLowerCase() === key) return all[i];
          return null;
        };
        var tb = tagBadges(zone, D.stat); if (tb.length) { var hl = el.querySelector(".wg-head-line"); if (hl) hl.appendChild(h("span.wg-zone-tags", tb)); }
        D.givers = []; D.quests = null;   // quest givers standing here: one pin each, with the quests they give
        if (r[4]) { try {
          D.quests = WG.quest.forZone(zone.id) || {gives: [], steps: []};
          var gmap = {};
          (D.quests.gives || []).forEach(function (q) {
            var g = q.giver && q.giver.zone === zone.id ? q.giver : (q.givers || []).filter(function (x) { return x.zone === zone.id; })[0];
            if (!g || g.x == null) return;
            var k = g.name + "|" + g.x + "|" + g.y, e = gmap[k];
            if (!e) { e = gmap[k] = {name: g.name, x: g.x, y: g.y, quests: [], seen: {}}; D.givers.push(e); }
            var one = q.grp != null ? "g" + q.grp : "q" + q.id;   // class versions of one quest count once
            if (!e.seen[one]) { e.seen[one] = 1; e.quests.push(q); }
          });
        } catch (e) { console.error(e); D.quests = null; D.givers = []; } }
        D.missing = [!mobs || !r[1] ? "monsters" : null, !vendors ? "merchants" : null, !routes ? "stables" : null].filter(Boolean);
        if (S.kind && !(D.stat && D.stat.kindList.some(function (e) { return e[0] === S.kind; }))) S.kind = 0;   // not a monster of this zone
        buildLayerBar(); buildSide(); refresh(true);
        if (D.quests && ((D.quests.gives || []).length || (D.quests.steps || []).length) && typeof WG.quest.zoneSection === "function") {
          var qbox = h("div.wg-zone-quests");
          el.querySelector(".wg-zone-main").appendChild(qbox);
          try { var qp = WG.quest.zoneSection(qbox, zone.id, {title: "Quests in " + zone.name}); if (qp && qp.catch) qp.catch(function (e) { console.error(e); }); } catch (e) { console.error(e); }
        }
        current.onMe = function () { refresh(false); if (S.table) S.table.refresh(); if (S.hist) S.hist(); };
        current.onResize = function () { var hgt = mapHeight(); var v = mapBox.querySelector(".wg-map-view"); if (v && v.offsetHeight !== hgt) { v.style.height = hgt + "px"; map.resize(); } };
      });

      /* ---- what the map shows */
      function setQuery() { WG.setQuery({kind: S.kind || null, singles: S.show.singles ? null : "0", routes: S.show.routes ? "1" : null, x: query.x, y: query.y}); }
      function isMonsterPin(p) { return p.kind === "camp" || p.kind === "spawn"; }
      /* The word for the members of a kind that are not aggressive (its rows in a zone say how many are, in `ag`). */
      function calmWord(kd) { var c = kd && kd.st; if (!Array.isArray(c)) return undefined; return c[3] > c[2] && c[3] >= c[1] ? "friendly" : c[1] > c[2] ? "hostile" : "neutral"; }
      /* "593 aggressive, 1,242 neutral" with a bar for the aggressive share (zone row `st`, mobs.md 5a). */
      function stanceLine(st) {
        var c = Array.isArray(st.st) ? st.st : [st.ag || 0, 0, Math.max(0, st.spawns - (st.ag || 0)), 0], total = c[0] + c[1] + c[2] + c[3] || 1, parts = [];
        ui.STANCES.forEach(function (w, i) { if (c[i]) { if (parts.length) parts.push(", "); parts.push(h("span.wg-stance." + w, ui.num(c[i]) + " " + w)); } });
        return h("span", ui.bar(c[0] / total, {color: "red", width: 70, title: Math.round(c[0] / total * 100) + "% are aggressive: only those attack first (never when they are grey to you)"}), " ", parts);
      }
      function campLevel(c) { var sp = spread(c.levels); return sp ? sp.mode : c.lmax; }
      function refresh(first) {
        var D = S.data, my = WG.me.level || 1, pins = [], lines = [], legend = [], seenCon = {};
        function conPin(level) { var c = conOf(level, my); if (c) seenCon[c] = 1; return c; }
        if (S.show.camps || S.kind) D.camps.forEach(function (c) {
          if (S.kind ? c.kind !== S.kind : (c.n === 1 && !S.show.singles)) return;
          var kd = D.kinds.get(c.kind) || {name: "Monster " + c.kind}, con = conPin(campLevel(c));
          var base = {color: con || "pin", group: con ? "con-" + con : "camps", z: c.z};
          if (S.kind && c.n > 1) c.pts.forEach(function (p) { pins.push({x: p[0], y: p[1], z: p[4] == null ? c.z : p[4], kind: "spawn", color: base.color, group: base.group, label: kd.name, sub: "Spawn point, level " + (p[2] === p[3] ? p[2] : p[2] + " to " + p[3]) + (p[5] != null && ui.stanceWord(p[5]) ? " \u00B7 " + ui.stanceWord(p[5]) : "")}); });
          pins.push({x: c.x, y: c.y, z: c.z, kind: c.n > 1 ? "camp" : "spawn", n: c.n, radius: c.r, color: base.color, group: base.group, id: "c" + c.id, href: WG.href("mob/" + c.kind),
            label: kd.name, sub: "Level " + (c.lmin === c.lmax ? c.lmin : c.lmin + " to " + c.lmax) + " · " + (c.n === 1 ? "single spawn" : c.n + " spawn here") + "\n" + cap(ui.stanceText({st: c.st, ag: c.ag, n: c.n, faction: kd.faction})) +
              (c.rup ? "\n" + c.rup + " stronger aggressive " + (c.rup === 1 ? "neighbour" : "neighbours") + " in reach, up to level " + c.rhi : "")});
        });
        if (S.kind) D.paths.forEach(function (p) { if (p.kind === S.kind) lines.push({pts: p.pts, color: "#ffffff", dashed: true, label: "Patrol route", legend: "Patrol route"}); });
        CONS.slice().reverse().forEach(function (c) { if (seenCon[c]) legend.push({kind: "camp", color: c, group: "con-" + c, label: CON_LABEL[c] + " to level " + my}); });
        if (!WG.con && pins.length) legend.push({kind: "camp", color: "pin", group: "camps", label: "Monster camp (the number is how many spawn there)"});
        if (S.show.vendors) { D.vendors.forEach(function (v) { pins.push({x: v.x, y: v.y, kind: "vendor", id: "v" + v.id, label: v.name, sub: (v.guild || "Merchant") + (D.vendorKinds[v.kind] ? " · " + D.vendorKinds[v.kind] : ""), href: WG.href("vendor/" + v.id)}); });
          if (D.vendors.length) legend.push({kind: "vendor", label: "Merchant"}); }
        if (S.show.stables) { D.stables.forEach(function (s) { pins.push({x: s.x, y: s.y, kind: "stable", id: "s" + s.id, label: s.name, sub: "Stable master" + (s.place ? " · " + s.place : "") + "\n" + (s.tickets ? s.tickets + (s.tickets === 1 ? " ticket" : " tickets") : "sells no ticket"), href: WG.href("routes", {from: s.id})}); });
          if (D.stables.length) legend.push({kind: "stable", label: "Stable master"}); }
        if (S.show.quests && D.givers.length) {
          D.givers.forEach(function (g, i) {
            var qs = g.quests.slice().sort(function (a, b) { return a.lmin - b.lmin; });
            pins.push({x: g.x, y: g.y, kind: "point", color: "yellow", group: "quests", id: "q" + i, href: qs.length === 1 ? WG.quest.href(qs[0]) : null,
              label: g.name, sub: "Gives " + (qs.length === 1 ? "a quest" : qs.length + " quests") + ":\n" + qs.slice(0, 6).map(function (q) { return q.name + " (level " + (q.lmin === q.lmax ? q.lmin : q.lmin + " to " + q.lmax) + ")"; }).join("\n") + (qs.length > 6 ? "\nand " + (qs.length - 6) + " more" : "")});
          });
          legend.push({kind: "point", color: "yellow", group: "quests", label: "Quest giver"});
        }
        if (S.show.scripted && D.scripted.length) {
          D.scripted.forEach(function (m) {
            var k = 0;
            (m.where || []).forEach(function (w) {
              if (w[0] !== zone.id) return;
              pins.push({x: w[1], y: w[2], kind: "point", color: "#ff8f7a", group: "scripted", id: "x" + m.id + (k ? "-" + k : ""), href: WG.href("mob/" + m.id),
                label: m.name, sub: (m.lmin != null ? "Level " + (m.lmin === m.lmax ? m.lmin : m.lmin + " to " + m.lmax) + " \u00B7 " : "") + (ui.rankLabel(ui.rank(m)) || "Monster") + ", appears by script" + (m.how ? "\n" + m.how : "")});
              k++;
            });
          });
          legend.push({kind: "point", color: "#ff8f7a", group: "scripted", label: "Appears by script (boss, encounter or quest spawn)"});
        }
        if (S.show.gates) {
          D.gates.forEach(function (g) { var to = WG.Map.zoneById(g.to.zone); pins.push({x: g.from.x, y: g.from.y, kind: "point", id: "g" + g.id, label: "Way to " + (to ? to.name : "zone " + g.to.zone), sub: "Walk in here to arrive at loc=" + g.to.x + "," + g.to.y, href: to ? WG.href("zones/" + to.id, {x: g.to.x, y: g.to.y}) : null}); });
          D.teleporters.forEach(function (t, i) { pins.push({x: t.x, y: t.y, kind: "point", color: "#c9a6ff", group: "tele", id: "t" + i, label: t.name, sub: "Teleporter" + (t.to && t.to.length ? " to " + t.to.map(function (x) { return x.name; }).join(", ") : "")}); });
          if (D.gates.length) legend.push({kind: "point", label: "Way to another zone"});
          if (D.teleporters.length) legend.push({kind: "point", color: "#c9a6ff", group: "tele", label: "Teleporter"});
        }
        if (S.show.routes) D.routes.forEach(function (rt) {
          (rt.path || []).forEach(function (seg) {
            if (seg.zone !== zone.id || !seg.pts || seg.pts.length < 2) return;
            lines.push({pts: seg.pts, color: rt.kind === "flying" ? "#c96bff" : "stable", dashed: rt.kind === "flying", label: (rt.fromName || "") + " to " + (rt.toName || ""), sub: (rt.kind === "flying" ? "Flying route" : "Horse route") + (rt.seconds ? " · " + Math.round(rt.seconds) + " seconds" : ""), legend: rt.kind === "flying" ? "Flying route" : "Horse route"});
          });
        });
        if (lines.some(function (l) { return l.color === "stable"; })) legend.push({kind: "line", color: "stable", label: "Horse route"});
        if (lines.some(function (l) { return l.color === "#c96bff"; })) legend.push({kind: "line", color: "#c96bff", dashed: true, label: "Flying route"});
        if (lines.some(function (l) { return l.color === "#ffffff"; })) legend.push({kind: "line", color: "#ffffff", dashed: true, label: "Patrol route"});
        S.pins = pins;
        map.setData({pins: pins, lines: lines, legend: legend});
        WG.clear(only);
        if (S.kind) {
          var kd = S.data.kinds.get(S.kind);
          only.appendChild(h("span", "Showing only ", h("b", kd ? kd.name : "one monster"), ": its camps and every spawn point. "));
          only.appendChild(h("a.wg-btn.small", {href: WG.href("mob/" + S.kind)}, "Monster page"));
          only.appendChild(h("button.wg-btn.small", {type: "button", onclick: function () { selectKind(0); }}, "Show all monsters"));
        }
        if (first) {
          if (query.focus) map.focus(query.focus, {scroll: false});
          else if (query.x != null && query.y != null && isFinite(+query.x)) { /* the mark is set; stay on the whole zone */ }
          else if (S.kind) map.fitPins(isMonsterPin);
        }
      }
      function selectKind(kindId) {
        S.kind = kindId === S.kind ? 0 : kindId;
        setQuery(); refresh(false);
        if (S.table) S.table.refresh();
        if (S.kind) map.fitPins(isMonsterPin); else map.fit();
      }
      function focusPin(pinId, layer) {
        if (layer && !S.show[layer]) { S.show[layer] = true; buildLayerBar(); refresh(false); }
        if (S.kind && pinId.charAt(0) === "c") { var c = S.data.campById.get(+pinId.slice(1)); if (c && c.kind !== S.kind) { S.kind = 0; setQuery(); refresh(false); if (S.table) S.table.refresh(); } }
        if (pinId.charAt(0) === "c" && !S.show.singles) { var c2 = S.data.campById.get(+pinId.slice(1)); if (c2 && c2.n === 1) { S.show.singles = true; buildLayerBar(); refresh(false); } }
        map.focus(pinId);
      }

      /* ---- the layer switches above the map */
      function buildLayerBar() {
        WG.clear(layerBar);
        var D = S.data, n1 = D.camps.filter(function (c) { return c.n === 1; }).length;
        function sw(key, label, count, title) {
          if (!count) return;
          layerBar.appendChild(ui.chip(label, {active: S.show[key], count: count, title: title, onclick: function () {
            S.show[key] = !S.show[key]; var p = {}; for (var k in S.show) if (k !== "routes") p[k] = S.show[k]; WG.pref("zone.show", p);
            buildLayerBar(); setQuery(); refresh(false);
          }}));
        }
        var showLabel = h("span.wg-dim", "Show:"); layerBar.appendChild(showLabel);
        sw("camps", "Monster camps", D.camps.length - n1, "Groups of the same monster. The pin colour is the con of the camp's commonest level to your level.");
        sw("singles", "Single spawns", n1, "Monsters that stand alone");
        sw("vendors", "Merchants", D.vendors.length);
        sw("stables", "Stable masters", D.stables.length);
        sw("quests", "Quest givers", D.givers.length, "People who give a quest here");
        sw("scripted", "Script spawns", D.scripted.length, "Monsters that are not standing in the world: a boss, an encounter or a quest makes them appear at this spot");
        sw("gates", "Ways out", D.gates.length + D.teleporters.length, "Zone exits, dungeon entrances and teleporters");
        var nr = 0; D.routes.forEach(function (rt) { if ((rt.path || []).some(function (s) { return s.zone === zone.id; })) nr++; });
        sw("routes", "Stable routes", nr, "The paths the horses and flying mounts take through this zone");
        if (layerBar.children.length === 1) layerBar.removeChild(showLabel);   // nothing to switch in an empty zone
        if (WG.con && D.camps.length) layerBar.appendChild(h("span.wg-dim.wg-zone-conhint", {title: "A camp's colour is the con of its commonest level to your character. Change your level in the header."}, "Con colours for level ", h("b", String(WG.me.level || 1))));
        if (D.missing.length) layerBar.appendChild(h("span.wg-warn", "Not built yet: " + D.missing.join(", ") + "."));
      }

      /* ---- the lists beside the map */
      function buildSide() {
        WG.clear(side);
        var D = S.data, st = D.stat, my = function () { return WG.me.level || 1; };
        // summary
        var sum = h("div.wg-card.wg-zone-sum");
        if (st && st.spawns) {
          var histBox = h("div");
          S.hist = function () { WG.clear(histBox); histBox.appendChild(ui.histogram(st.levels, {myLevel: my(), height: 54, unit: "monsters"})); };
          S.hist();
          var sp = spread(st.levels);
          sum.appendChild(h("div.wg-zone-sumhead", h("b", "Monster levels"), h("span.wg-dim", sp ? "most are " + sp.lo + " to " + sp.hi + (st.lmin < sp.lo || st.lmax > sp.hi ? ", all " + st.lmin + " to " + st.lmax : "") : "")));
          sum.appendChild(histBox);
          sum.appendChild(ui.kv([
            ["Monsters", ui.num(st.spawns) + " in " + ui.num(st.camps) + " camps, " + ui.num(st.kinds) + " kinds"],
            ["Stance", stanceLine(st)],
            st.guards || st.keepGuards ? ["Guards", ui.num(st.guards + st.keepGuards) + (st.keepGuards ? " (" + ui.num(st.keepGuards) + " keep guards)" : "")] : null
          ]));
        } else sum.appendChild(h("div.wg-dim", D.missing.indexOf("monsters") >= 0 ? "The monster data is not built yet." : "No monsters live in this zone."));
        if (D.events.length) {   // realm events that end here (mobs.events): one line per different event, realms joined
          var seenEv = {};
          D.events.forEach(function (ev) {
            var k = ev.kind + "|" + (ev.bosses || []).join(",");
            if (seenEv[k]) { if (ev.realm && seenEv[k].realms.indexOf(ev.realm) < 0) seenEv[k].realms.push(ev.realm); return; }
            seenEv[k] = {ev: ev, realms: ev.realm ? [ev.realm] : []};
          });
          Object.keys(seenEv).forEach(function (k) {
            var ev = seenEv[k].ev, realms = seenEv[k].realms;
            function who(name) { var m = D.byName(name); return m ? h("a.wg-named", {href: WG.href("mob/" + m.id)}, name) : h("b", name); }
            function names(list) { var out = []; list.forEach(function (n, i) { if (i) out.push(i === list.length - 1 ? " and " : ", "); out.push(who(n)); }); return out; }
            var box = h("div.wg-note.wg-zone-event", h("b", "Realm event"), " (" + ev.kind + (realms.length ? ", " + realms.map(function (r) { return WG.REALMS[r]; }).join(", ") : "") + "). ",
              ev.bosses && ev.bosses.length ? ["Final encounter: ", names(ev.bosses), ". "] : null,
              ev.objectives && ev.objectives.length ? ["On the way: ", names(ev.objectives), ". "] : null,
              ev.suggested ? h("span.wg-dim", "The server sends a raid of " + ev.suggested + " to it.") : null);
            sum.appendChild(box);
          });
        }
        side.appendChild(sum);

        var tabsBox = h("div.wg-zone-tabs"); side.appendChild(tabsBox);
        var kindRows = st ? st.kindList.map(function (e) { var kd = D.kinds.get(e[0]) || {}; return {id: e[0], name: kd.name || "Monster " + e[0], n: e[1], lmin: e[2], lmax: e[3], ag: e[4], faction: kd.faction, stance: calmWord(kd), kd: kd, rank: ui.rank(kd), named: !!(kd.f & 1)}; }) : [];
        var RANK_ORDER = {event: 0, epic: 1, boss: 2, named: 3, quest: 4};
        var scriptRows = D.scripted.map(function (m) { return {id: m.id, name: m.name, lmin: m.lmin, lmax: m.lmax == null ? m.lmin : m.lmax, kd: m, rank: ui.rank(m) || "named", scripted: true}; });
        var special = kindRows.filter(function (r) { return r.rank; }).concat(scriptRows).sort(function (a, b) { return (RANK_ORDER[a.rank] == null ? 1 : RANK_ORDER[a.rank]) - (RANK_ORDER[b.rank] == null ? 1 : RANK_ORDER[b.rank]) || (b.lmax - a.lmax) || (a.name < b.name ? -1 : 1); });
        var travelN = D.stables.length + D.gates.length + D.teleporters.length;
        ui.tabs(tabsBox, [
          {key: "mobs", label: "Monsters", count: kindRows.length, render: function (p) {
            if (!kindRows.length) { p.appendChild(ui.empty("No monsters here.")); return; }
            if (special.length) {   // bosses and named monsters first (owner)
              var nb = special.filter(function (r) { return r.rank === "event" || r.rank === "epic" || r.rank === "boss"; }).length;
              var sbox = h("div.wg-zone-special");
              special.forEach(function (r) {
                var show = function () { if (r.scripted) focusPin("x" + r.id, "scripted"); else selectKind(r.id); };
                sbox.appendChild(h("div.wg-zone-row", {tabIndex: 0, role: "button", title: r.scripted ? (r.kd.how || "Appears by script") + " Click to see the spot." : "Show it on the map", onclick: show, onkeydown: function (e) { if (e.key === "Enter") show(); }},
                  ui.rankBadge(r.kd, {all: r.rank === "quest", short: true}), h("span.wg-grow.wg-named", r.name, r.scripted ? h("span.wg-dim.wg-small", " by script") : null), ui.level(r.lmin, r.lmax, my()), r.scripted ? null : ui.stance(r, {count: false}), h("a", {href: WG.href("mob/" + r.id), title: "Open the monster page", onclick: function (e) { e.stopPropagation(); }}, "page")));
              });
              p.appendChild(ui.section(nb ? "Bosses and named monsters" : "Named and quest monsters", {count: special.length, collapsed: !nb && special.length > 12, cls: "wg-zone-specialsec"}, sbox));
            }
            p.appendChild(h("div.wg-dim.wg-small.wg-zone-tip", "Click a row to see only that monster on the map."));
            var box = h("div.wg-scrollbox"); p.appendChild(box);
            S.table = ui.table(box, {dense: true, pageSize: 0, rows: kindRows, count: "kinds",
              columns: [
                {key: "name", label: "Monster", render: function (r) { return [h("span", {class: r.named ? "wg-named" : ""}, r.name), r.rank && r.rank !== "named" ? [" ", ui.rankBadge(r.kd)] : null]; }},
                {key: "lmin", label: "Level", num: true, width: 78, sort: function (r) { return r.lmin + r.lmax / 1000; }, render: function (r) { return ui.level(r.lmin, r.lmax, my()); }},
                {key: "n", label: "Spawns", num: true, width: 60},
                {key: "ag", label: "Stance", width: 96, title: "How it stands towards a new character, in the game's words. Only aggressive monsters attack first.", sort: function (r) { return ui.stanceWord(r) + (1 - r.ag / r.n).toFixed(3); }, render: function (r) { return ui.stance(r); }},
                {key: "go", label: "", sort: false, width: 44, render: function (r) { return h("a", {href: WG.href("mob/" + r.id), title: "Open the monster page"}, "page"); }}
              ],
              sortKey: "lmin", sortDir: "asc",
              rowClass: function (r) { return r.id === S.kind ? "active" : ""; },
              onRow: function (r) { selectKind(r.id); }});
          }},
          {key: "camps", label: "Best camps", count: st && st.densest.length ? null : 0, render: function (p) {
            function campList(title, note, ids) {
              var ul = h("div.wg-list");
              ids.forEach(function (cid) {
                var c = D.campById.get(cid); if (!c) return; var kd = D.kinds.get(c.kind) || {name: "Monster " + c.kind};
                ul.appendChild(h("div.wg-zone-row", {tabIndex: 0, role: "button", onclick: function () { focusPin("c" + c.id, "camps"); }, onkeydown: function (e) { if (e.key === "Enter") focusPin("c" + c.id, "camps"); }},
                  h("span.wg-grow", kd.name), ui.level(c.lmin, c.lmax, my()), h("span.wg-dim.n", c.n + " spawns"),
                  ui.stance({st: c.st, ag: c.ag, n: c.n, faction: kd.faction}, {count: false}), c.rup ? h("span.wg-warn", {title: c.rup + " stronger aggressive " + (c.rup === 1 ? "neighbour" : "neighbours") + " in reach, up to level " + c.rhi}, "!") : null));
              });
              p.appendChild(h("h3", title)); p.appendChild(h("div.wg-dim.wg-small", note)); p.appendChild(ul);
            }
            if (!st || !st.densest.length) { p.appendChild(ui.empty("No camp of three or more monsters here.")); return; }
            campList("Biggest camps", "The most spawn points in one camp.", st.densest);
            campList("Quietest camps", "Three or more spawns and the fewest stronger aggressive neighbours in reach.", st.safest);
          }},
          {key: "vendors", label: "Merchants", count: D.vendors.length, render: function (p) {
            if (!D.vendors.length) { p.appendChild(ui.empty("No merchant stands in this zone.")); return; }
            var box = h("div.wg-scrollbox"); p.appendChild(box);
            ui.table(box, {dense: true, pageSize: 0, rows: D.vendors, count: "merchants",
              columns: [
                {key: "name", label: "Merchant"},
                {key: "kind", label: "Sells", render: function (v) { return D.vendorKinds[v.kind] || v.kind || ""; }},
                {key: "n", label: "Goods", num: true, width: 56},
                {key: "go", label: "", sort: false, width: 44, render: function (v) { return h("a", {href: WG.href("vendor/" + v.id), title: "Open the merchant page"}, "page"); }}
              ], sortKey: "kind", sortDir: "asc",
              onRow: function (v) { focusPin("v" + v.id, "vendors"); }});
          }},
          {key: "travel", label: "Travel", count: travelN, render: function (p) {
            if (!travelN) { p.appendChild(ui.empty("No stable master, zone exit or teleporter here.")); return; }
            if (D.stables.length) {
              p.appendChild(h("h3", "Stable masters"));
              D.stables.forEach(function (s) {
                var out = D.routes.filter(function (rt) { return rt.from === s.id; });
                var card = h("div.wg-zone-stable", h("div.wg-zone-row", {tabIndex: 0, role: "button", onclick: function () { focusPin("s" + s.id, "stables"); }, onkeydown: function (e) { if (e.key === "Enter") focusPin("s" + s.id, "stables"); }},
                  h("b.wg-grow", s.name), h("span.wg-dim", s.place || "")));
                if (s.reputation) card.appendChild(h("div.wg-warn.wg-small", "Asks for reputation before it sells a ticket."));
                var chips = h("div.wg-zone-tickets");
                out.forEach(function (rt) { chips.appendChild(ui.chip((rt.kind === "flying" ? "Fly to " : "To ") + (rt.toName || "?"), {href: WG.href("routes", {from: rt.from, to: rt.to}), title: (rt.name || "Ticket") + (rt.seconds ? ", " + Math.round(rt.seconds) + " seconds" : ""), color: rt.kind === "flying" ? "purple" : null})); });
                if (!out.length) chips.appendChild(h("span.wg-dim.wg-small", "Sells no ticket."));
                card.appendChild(chips); p.appendChild(card);
              });
            }
            if (D.gates.length || D.teleporters.length) {
              p.appendChild(h("h3", "Ways out"));
              var ul = h("div.wg-list");
              D.gates.forEach(function (g) {
                var to = WG.Map.zoneById(g.to.zone);
                ul.appendChild(h("div.wg-zone-row", {tabIndex: 0, role: "button", onclick: function () { focusPin("g" + g.id, "gates"); }, onkeydown: function (e) { if (e.key === "Enter") focusPin("g" + g.id, "gates"); }},
                  h("span.wg-grow", "To ", to ? h("b", {class: to.realm ? "realm-" + to.realm : ""}, to.name) : "zone " + g.to.zone), h("span.wg-dim.wg-small", "loc=" + g.from.x + "," + g.from.y),
                  to ? h("a", {href: WG.href("zones/" + to.id, {x: g.to.x, y: g.to.y}), title: "Open that zone at the arrival point"}, "go") : null));
              });
              D.teleporters.forEach(function (t, i) {
                ul.appendChild(h("div.wg-zone-row", {tabIndex: 0, role: "button", onclick: function () { focusPin("t" + i, "gates"); }, onkeydown: function (e) { if (e.key === "Enter") focusPin("t" + i, "gates"); }},
                  h("span.wg-grow", h("b", t.name), h("span.wg-dim", " teleporter" + (t.to && t.to.length ? " to " + t.to.map(function (x) { return x.name; }).join(", ") : "")))));
              });
              p.appendChild(ul);
            }
          }}
        ], {active: S.kind ? "mobs" : null, pref: "zone.tab"});
      }
    });
  }

  var current = {map: null, onMe: null, onResize: null};
  WG.onMe(function () { if (current.onMe) { try { current.onMe(); } catch (e) { console.error(e); } } });
  window.addEventListener("resize", function () { if (current.onResize) current.onResize(); });

  WG.view("zones", {
    title: "Zones", nav: true, order: 50,
    render: function (el, params, query) {
      current = {map: null, onMe: null, onResize: null};
      return params.length ? renderZone(el, params[0], query) : renderList(el, query);
    },
    leave: function () { if (current.map) current.map.destroy(); current = {map: null, onMe: null, onResize: null}; }
  });
})();
