/* OFFLINEDAOC Database - all monster kinds (#/mobs), lane M-U.
   A searchable, sortable list of every kind in data/mobs.js. Filters and the sort live in the address.
   Uses the shared model of hunt.js (WG.hunt.load). */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, con = WG.con, ui = WG.ui, hunt = WG.hunt;
  var CON_OPTS = [["", "Any colour"]].concat(con.COLORS.map(function (c) { return {value: c, label: con.name(c), color: c}; }));
  var live = null;

  function defs(M, level) {
    var zopts = [];
    M.byZone.forEach(function (list, zid) { var z = M.zoneById.get(zid); if (z) zopts.push([zid, z.name]); });
    zopts.sort(function (a, b) { return a[1].localeCompare(b[1]); });
    return [
      {key: "q", label: "Name", type: "text", value: "", placeholder: "part of a name", width: 190},
      {key: "lv", label: "Monster level", type: "range", value: [null, null], min: 0, max: 127, title: "Kinds that have at least one spawn level in this range"},
      {key: "con", label: "Con at level " + level, type: "select", value: "", options: CON_OPTS, title: "Kinds that have at least one spawn level of this colour to you"},
      {key: "realm", label: "Realm", type: "select", value: "", options: [["", "Any realm"], [1, "Albion"], [2, "Midgard"], [3, "Hibernia"]], title: "Kinds that live in a zone of this realm"},
      {key: "zone", label: "Zone", type: "select", value: "", options: [["", "Any zone"]].concat(zopts), width: 190},
      {key: "stance", label: "Stance", type: "select", value: "", options: [["", "Any stance"], ["calm", "Neutral or friendly only"], ["aggr", "Aggressive or hostile"]],
        title: "The word the game uses when you look at a monster. \"Neutral or friendly only\" leaves out a kind that has even one aggressive or hostile spawn."},
      {key: "cls", label: "Class", type: "select", value: "", options: [["", "Any class"], ["normal", "Ordinary monsters"]].concat(hunt.CLASSES.filter(function (c) { return M.kinds.some(function (k) { return hunt.classOf(k) === c[0]; }); })),
        title: "Ordinary monster, named monster, boss, epic or raid boss, realm event boss or quest target"}
    ];
  }

  function apply(M, v, level) {
    var q = (v.q || "").trim().toLowerCase(), lo = v.lv[0], hi = v.lv[1];
    var band = v.con ? con.levels(level, v.con) : null;
    if (v.con && !band) return [];
    var realmZones = null;
    if (v.realm) { realmZones = new Set(); M.zones.forEach(function (z) { if (z.realm === +v.realm) realmZones.add(z.id); }); }
    return M.kinds.filter(function (k) {
      if (q && k.key.indexOf(q) < 0 && String(k.name).toLowerCase().indexOf(q) < 0) return false;
      if (lo != null && k.levelMax < lo) return false;
      if (hi != null && k.levelMin > hi) return false;
      if (band && (k.levelMax < band[0] || k.levelMin > band[1])) return false;
      if (v.zone !== "" && k.zoneIds.indexOf(+v.zone) < 0) return false;
      if (realmZones && !k.zoneIds.some(function (z) { return realmZones.has(z); })) return false;
      if (v.stance && (v.stance === "calm") !== hunt.stanceCalm(k.stance)) return false;
      if (v.cls && hunt.classOf(k) !== v.cls) return false;
      return true;
    });
  }

  function columns(M, level) {
    return [
      {key: "name", label: "Monster", render: function (k) { var t = hunt.tagEls(k); return t.length ? h("span", h("a", {href: WG.href("mob/" + k.id)}, k.name), " ", t) : h("a", {href: WG.href("mob/" + k.id)}, k.name); }},
      {key: "levelMin", label: "Levels", num: true, width: 110, title: "Lowest to highest spawn level, in the con colours for level " + level + ". Click to sort by the lowest level.",
        render: function (k) { return con.el(level, k.levelMin, k.levelMax); }},
      {key: "levelMax", label: "Up to", num: true, width: 64, title: "Highest spawn level. Click to sort by it.", render: function (k) { return h("span." + con.cls(con.color(level, k.levelMax)), String(k.levelMax)); }},
      {key: "body", label: "Type", width: 100, sort: function (k) { return (M.meta.bodyTypes || {})[k.body] || ""; }, render: function (k) { return h("span.wg-dim", (M.meta.bodyTypes || {})[k.body] || ""); }},
      {key: "zones", label: "Where", sort: function (k) { var z = M.zoneById.get(k.zoneIds[0]); return z ? z.name : ""; },
        render: function (k) {
          var zs = k.zoneIds; if (!zs.length) return h("span.wg-dim", "no zone");
          var out = [ui.zoneLink(M.zoneById.get(zs[0]) || zs[0])];
          if (zs.length > 1) out.push(h("span.wg-dim", {title: zs.slice(1).map(function (z) { var zz = M.zoneById.get(z); return zz ? zz.name : "zone " + z; }).join(", ")}, " and " + (zs.length - 1) + " more"));
          return out;
        }},
      {key: "spawns", label: "Spawns", num: true, width: 80, render: function (k) { return ui.num(k.spawns); }},
      {key: "camps", label: "Camps", num: true, width: 80, render: function (k) { return ui.num(k.camps); }},
      {key: "stance", label: "Stance", width: 190, title: "The game's word for how it stands towards a new character; a number in front when its spawns differ",
        sort: function (k) { return k.spawns ? (k.stance[0] + k.stance[1] * 0.5) / k.spawns : 0; },
        render: function (k) { return hunt.stanceEl(k.stance); }}
    ];
  }

  /* ================================================================== #/hunt?mode=bosses
     "Bosses and epic encounters": every kind that is not an ordinary monster (named, boss, epic or raid boss, realm
     event boss, quest target) and the scripted bosses that are not ordinary spawns, by level, realm and zone,
     with how each appears and what it drops. Drawn inside the hunt page (hunt.js calls this). */
  var PLACES = [["", "All kinds"], ["outdoor", "Outdoor"], ["dungeon", "Dungeon"], ["frontier", "Frontier"], ["battleground", "Battleground"], ["df", "Darkness Falls"], ["epic", "Epic dungeon or raid zone"]];
  function zoneIs(M, z, place) {
    if (!place) return true;
    if (place === "df") return z.id === M.dfZone;
    if (place === "epic") return M.epicZones.has(z.id);
    return z.kind === place;
  }
  hunt.bossPage = function (el, query, M, P) {
    var level = hunt.clampLevel(query.level != null ? query.level : WG.me.level, 1);
    var all = hunt.bossRows(M), realmDef = WG.me.realm || 0;
    var hasGroup = all.some(function (k) { return hunt.isGroup(k); }), nScript = all.filter(function (k) { return k.scripted; }).length;
    var zopts = [], zseen = new Set();
    all.forEach(function (k) { (k.zoneIds || []).forEach(function (z) { zseen.add(z); }); });
    zseen.forEach(function (zid) { var z = M.zoneById.get(zid); if (z) zopts.push([zid, z.name]); });
    zopts.sort(function (a, b) { return a[1].localeCompare(b[1]); });
    var top = h("div.wg-card.mu-top"), info = h("div.mu-help"), box = h("div"), fbar = h("div");
    top.appendChild(fbar); top.appendChild(info);
    el.appendChild(top); el.appendChild(box);
    var D = ui.filters.fromQuery([
      {key: "cls", label: "Class", type: "chips", required: true, value: "", options: [["", "All"]].concat(hunt.CLASSES)},
      {key: "realm", label: "Realm", type: "select", value: realmDef, def: realmDef, options: [[0, "Any realm"], [1, "Albion"], [2, "Midgard"], [3, "Hibernia"]],
        title: "This realm's zones and dungeons, plus what every realm can reach: all frontiers, the battlegrounds and Darkness Falls"},
      {key: "where", label: "Zone kind", type: "chips", required: true, value: "", options: PLACES},
      {key: "zone", label: "Zone", type: "select", value: "", options: [["", "Any zone"]].concat(zopts), width: 190},
      {key: "lv", label: "Monster level", type: "range", value: [null, null], min: 0, max: 127},
      {key: "grp", label: "Group content", type: "select", value: "", hidden: !hasGroup, options: [["", "Included"], ["no", "Left out"], ["only", "Only group content"]],
        title: "Monsters the data marks as a fight for a group"},
      {key: "q", label: "Name", type: "text", value: "", placeholder: "part of a name", width: 170}
    ], query);
    var sortKey = query.sort || "levelMin", sortDir = query.dir === "asc" || query.dir === "desc" ? query.dir : "asc";
    var table = null, dropsD = null;

    function rowsFor(v, skip) {
      var q = (v.q || "").trim().toLowerCase(), lo = v.lv[0], hi = v.lv[1], realm = +v.realm || 0;
      return all.filter(function (k) {
        if (skip !== "cls" && v.cls && hunt.classOf(k) !== v.cls) return false;
        if (q && String(k.key).indexOf(q) < 0 && String(k.name).toLowerCase().indexOf(q) < 0) return false;
        if (lo != null && k.levelMax < lo) return false;
        if (hi != null && k.levelMin > hi) return false;
        if (v.grp && (v.grp === "only") !== hunt.isGroup(k)) return false;
        var zs = k.zoneIds || [];
        if (v.zone !== "" && zs.indexOf(+v.zone) < 0) return false;
        if (!zs.length) return skip === "where" || !v.where;      // a script-made monster with no fixed place
        // a kind counts when one of its zones passes the realm rule and the zone-kind choice
        return zs.some(function (zid) {
          var z = M.zoneById.get(zid);
          if (!z) return false;
          if (realm && z.realm && z.realm !== realm && z.kind !== "frontier") return false;
          return skip === "where" || zoneIs(M, z, v.where);
        });
      });
    }
    function counts(v) {
      var byCls = {"": 0}, byPlace = {};
      rowsFor(v, "cls").forEach(function (k) { byCls[""]++; var c = hunt.classOf(k); byCls[c] = (byCls[c] || 0) + 1; });
      var pre = rowsFor(v, "where");
      PLACES.forEach(function (p) {
        byPlace[p[0]] = pre.filter(function (k) { return (k.zoneIds || []).some(function (zid) { var z = M.zoneById.get(zid); return z && zoneIs(M, z, p[0]); }); }).length;
      });
      F.setOptions("cls", [["", "All"]].concat(hunt.CLASSES).filter(function (o) { return !o[0] || byCls[o[0]] || v.cls === o[0]; }).map(function (o) { return {value: o[0], label: o[1], count: byCls[o[0]] || 0, title: hunt.classHelp(o[0]) || null}; }));
      F.setOptions("where", PLACES.filter(function (p) { return p[0] !== "epic" || M.epicZones.size; }).map(function (p) { return {value: p[0], label: p[1], count: byPlace[p[0]]}; }));
    }
    function address() {
      var q = F.query(), st = table ? table.state() : {sortKey: sortKey, sortDir: sortDir};
      q.mode = "bosses";
      if (query.level != null) q.level = level;
      if (st.sortKey && !(st.sortKey === "levelMin" && st.sortDir === "asc")) { q.sort = st.sortKey; q.dir = st.sortDir; }
      WG.setQuery(q);
    }
    function paintInfo(v, n) {
      var rn = +v.realm ? WG.REALMS[+v.realm] : null;
      info.textContent = ui.num(n) + (n === 1 ? " monster" : " monsters") + ". Listed: every named monster, boss, epic or raid boss, realm event boss and quest target in the data" +
        (nScript ? ", with " + nScript + " that only a script or a quest step creates (not standing spawns)" : "") + ". " +
        (v.where || v.zone !== "" ? "" : "Searching " + (rn ? rn + "'s zones and dungeons" : "every realm's zones and dungeons") + ", all three frontiers, the battlegrounds and Darkness Falls. ") +
        "Con colours are for level " + level + ". Click a row for the map, the camps and the full drop table.";
    }
    var F = ui.filters(fbar, D, function (v) { var r = rowsFor(v); table.setRows(r); counts(v); paintInfo(v, r.length); address(); });
    function whereCell(k) {
      var zs = k.zoneIds || [];
      if (!zs.length) return h("span.wg-dim", {title: "The script or quest works the place out when it runs"}, "no fixed place");
      var z = M.zoneById.get(zs[0]), out = [ui.zoneLink(z || zs[0])];
      hunt.zoneTagEls(zs[0]).forEach(function (t) { out.push(" "); out.push(t); });
      if (z && !M.zoneTags.has(zs[0]) && (z.kind === "dungeon" || z.kind === "frontier" || z.kind === "battleground")) out.push(h("span.wg-dim", " (" + z.kind + ")"));
      if (zs.length > 1) out.push(h("span.wg-dim", {title: zs.slice(1).map(function (zz) { var o = M.zoneById.get(zz); return o ? o.name : "zone " + zz; }).join(", ")}, " and " + (zs.length - 1) + " more"));
      return out;
    }
    var cols = [
      {key: "name", label: "Monster", width: "15%", render: function (k) { return h("a", {href: WG.href("mob/" + k.id)}, k.name); }},
      {key: "cls", label: "Class", width: 150, sort: function (k) { return hunt.classLabel(hunt.classOf(k)); },
        render: function (k) {
          var c = hunt.classOf(k), out = [h("span.mu-tag" + (c === "named" || c === "quest" ? "" : ".epic"), {title: hunt.classHelp(c) + (k.rankWhy ? ". Why: " + k.rankWhy : "")}, hunt.classLabel(c))];
          if (hunt.isGroup(k)) { out.push(" "); out.push(h("span.mu-tag.group", {title: hunt.GROUP_HELP}, "Group content")); }
          if (k.scripted) { out.push(" "); out.push(h("span.mu-tag", {title: "Not a standing spawn: a script or a quest step creates it"}, "Script-made")); }
          return out;
        }},
      {key: "levelMin", label: "Levels", num: true, width: 100, title: "In the con colours for level " + level, render: function (k) { return hunt.levelEl(k, level); }},
      {key: "where", label: "Where", sort: function (k) { var z = M.zoneById.get((k.zoneIds || [])[0]); return z ? z.name : ""; }, render: whereCell},
      {key: "appear", label: "How it appears", width: "27%", sort: false, render: function (k) { return h("span.wg-small", hunt.appearEl(k)); }},
      {key: "stance", label: "Stance", width: 100, sort: function (k) { return k.spawns ? (k.stance[0] + k.stance[1] * 0.5) / k.spawns : 0; }, render: function (k) { return k.stance ? hunt.stanceEl(k.stance) : ""; }},
      {key: "drops", label: "Drops", sort: function (k) { var kd = dropsD && dropsD.of(k.key); return kd && kd.d ? kd.d.length : 0; }, num: false, width: "19%",
        render: function (k) {
          if (!dropsD) return h("span.wg-dim", "loading");
          var kd = dropsD.of(k.key), n = kd && kd.d ? kd.d.length : 0;
          if (!n) return h("span.wg-dim", kd && kd.otd ? "one-time drop only" : "no items");
          var g = dropsD.gearFor(k, level), out = [];
          if (g.length) { out.push(ui.itemLink(g[0].it, {iconSize: 16})); out.push(h("span.wg-dim", " " + WG.pct(g[0].chance) + (n > 1 ? ", " + n + " items in all" : ""))); }
          else out.push(h("span.wg-dim", n + (n === 1 ? " item" : " items") + ", no gear for you"));
          return out;
        }}
    ];
    if (!cols.some(function (c) { return c.key === sortKey && c.sort !== false; })) sortKey = "levelMin";
    var first = rowsFor(F.values());
    table = ui.table(box, {
      columns: cols, rows: first, sortKey: sortKey, sortDir: sortDir, pageSize: 50, count: "monsters",
      rowHref: function (k) { return WG.href("mob/" + k.id); },
      empty: "No monster matches. Choose \"All\" classes and \"All kinds\" of zone, clear the name, or widen the levels.",
      onSort: address
    });
    counts(F.values()); paintInfo(F.values(), first.length);
    hunt.drops().then(function (Dd) { if (P.alive) { dropsD = Dd; table.refresh(); } }, function () { dropsD = {of: function () { return null; }, gearFor: function () { return []; }}; if (P.alive) table.refresh(); });
  };

  WG.view("mobs", {
    title: "Monsters", nav: true, order: 25,
    render: function (el, params, query) {
      hunt.css();
      var me = (live = {alive: true});
      ui.loading(el, "Loading monsters...");
      return hunt.load().then(function (M) {
        if (!me.alive) return;
        WG.clear(el);
        var level = hunt.clampLevel(query.level != null ? query.level : WG.me.level, 1);
        ui.page(el, {title: "Monsters", sub: ui.num(M.kinds.length) + " kinds of monster in the True 1.65 world. Con colours are for a level " + level + " character (change it in the header).",
          actions: [h("a.wg-btn", {href: WG.href("hunt", {mode: "bosses"}), title: "Named monsters, bosses, epic and raid bosses, realm event bosses"}, "Bosses and epic encounters")]});
        var bar = h("div.wg-card", {style: {marginBottom: "12px"}}), box = h("div");
        el.appendChild(bar); el.appendChild(box);
        var D = ui.filters.fromQuery(defs(M, level), query);
        var sortKey = query.sort || "spawns", sortDir = query.dir === "asc" || query.dir === "desc" ? query.dir : null;
        var table = null;
        function address() {
          var q = F.query(), st = table ? table.state() : {sortKey: sortKey, sortDir: sortDir};
          if (query.level != null) q.level = level;
          if (st.sortKey && st.sortKey !== "spawns") q.sort = st.sortKey;
          if (st.sortKey && st.sortDir && !(st.sortKey === "spawns" && st.sortDir === "desc")) { q.sort = st.sortKey; q.dir = st.sortDir; }
          WG.setQuery(q);
        }
        var F = ui.filters(bar, D, function (v) { table.setRows(apply(M, v, level)); address(); });
        var cols = columns(M, level);
        if (!cols.some(function (c) { return c.key === sortKey; })) sortKey = "spawns";
        table = ui.table(box, {
          columns: cols, rows: apply(M, F.values(), level), sortKey: sortKey, sortDir: sortDir, pageSize: 50, count: "monsters",
          rowHref: function (k) { return WG.href("mob/" + k.id); },
          empty: "No monster matches. Clear the name, widen the levels, or set the other filters back to Any.",
          onSort: address
        });
        me.relevel = true;
      });
    },
    leave: function () { if (live) live.alive = false; live = null; }
  });

  /* The header box changed my level: redraw with the new con colours (unless the address fixes a level). */
  WG.onMe(function () {
    if (!live || !live.alive || !live.relevel) return;
    var r = WG.route();
    if (r.view !== "mobs" || r.query.level != null) return;
    var main = document.getElementById("wg-main"), def = WG.views.mobs;
    if (!main) return;
    live.alive = false;
    WG.clear(main);
    def.render(main, r.params, r.query);
  });
})();
