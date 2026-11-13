/* OFFLINEDAOC Database - merchants (lane V-U).
   #/vendors                   what merchants sell, and where they stand
     ?mode=items (default)     pick what you want (kind, type, slot, level, realm) -> the goods, their price, who sells them
     ?mode=merchants           browse merchants by realm, zone and kind of goods
     &item=<id> / &vendor=<id> the chosen row: the zone map with a pin on every merchant that sells it

   Also defines WG.vu: small helpers shared by this lane's three pages (vendors.js, vendor.js, routes.js):
   adapters for data/vendors.js, data/vendoritems.js, data/items.js and data/zones.js, read by field name. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = WG.ui || {};
  var vu = (WG.vu = WG.vu || {});

  /* ------------------------------------------------------------------ styles (kept with the markup they style) */
  vu.css = function () {
    if (document.getElementById("vu-css")) return;
    document.head.appendChild(h("style", {id: "vu-css", textContent: [
      ".vu-split{display:grid;grid-template-columns:minmax(0,1fr) minmax(400px,44%);gap:14px;align-items:start}",
      "@media (max-width:980px){.vu-split{grid-template-columns:minmax(0,1fr)}}",
      ".vu-side{position:sticky;top:calc(var(--header-h,49px) + 10px);max-height:calc(100vh - var(--header-h,49px) - 20px);overflow:auto}",
      "@media (max-width:980px){.vu-side{position:static;max-height:none}}",
      ".vu-modes{display:flex;gap:8px;margin:0 0 12px;flex-wrap:wrap}",
      ".vu-mode{display:block;padding:8px 16px;border:1px solid var(--line);border-radius:var(--radius);background:var(--bg2);color:var(--text)}",
      ".vu-mode:hover{border-color:var(--accent);text-decoration:none}",
      ".vu-mode.on{background:var(--accent);border-color:var(--accent);color:#1a1408;font-weight:600}",
      ".vu-mode small{display:block;font-weight:400;opacity:.8}",
      ".vu-sub{color:var(--dim);font-size:12px;line-height:1.35}",
      ".vu-sel td{background:rgba(212,169,74,.14)!important}",
      ".vu-sel td:first-child{box-shadow:inset 3px 0 0 var(--accent)}",
      ".vu-pick{font:inherit;color:var(--link);background:none;border:1px solid var(--line);border-radius:999px;padding:1px 9px;cursor:pointer;white-space:nowrap}",
      ".vu-pick:hover,.vu-pick:focus-visible{border-color:var(--accent);color:var(--accent)}",
      ".vu-rows{list-style:none;margin:8px 0 0;padding:0}",
      ".vu-rows li{display:grid;grid-template-columns:minmax(0,1fr) auto auto;gap:4px 10px;align-items:baseline;padding:5px 8px;border-top:1px solid var(--line);cursor:pointer}",
      ".vu-rows li:hover,.vu-rows li.on{background:var(--bg3)}",
      ".vu-rows li.on{box-shadow:inset 3px 0 0 var(--accent)}",
      ".vu-rows .where{grid-column:1 / -1;color:var(--dim);font-size:12px}",
      ".vu-head{display:flex;gap:10px;align-items:flex-start;margin-bottom:8px}",
      ".vu-head h2{margin:0;font-size:17px;color:var(--accent)}",
      ".vu-count{color:var(--dim);margin:6px 0}",
      ".vu-hint{color:var(--dim);padding:28px 16px;text-align:center}",
      ".vu-loc{font-family:Consolas,monospace;font-size:12px;color:var(--dim)}",
      ".vu-btn{font:inherit;padding:3px 10px;border-radius:var(--radius);border:1px solid var(--line);background:var(--bg3);color:var(--text);cursor:pointer}",
      ".vu-btn:hover,.vu-btn:focus-visible{border-color:var(--accent)}",
      ".vu-facts{display:flex;gap:6px 18px;flex-wrap:wrap;color:var(--dim);margin:2px 0 8px}",
      ".vu-facts b{color:var(--text);font-weight:600}"
    ].join("\n")}));
  };

  /* ------------------------------------------------------------------ data adapters */
  function tableOf(t) { return !t ? [] : t.cols ? WG.rows(t) : Array.isArray(t) ? t : []; }

  vu.zones = function () {
    var d = WG.data.zones;
    if (!d) return new Map();
    if (!d._vuMap) { d._vuMap = new Map(); tableOf(d.zones || d).forEach(function (z) { d._vuMap.set(z.id, z); }); }
    return d._vuMap;
  };
  vu.zone = function (id) { return vu.zones().get(id) || vu.zones().get(+id) || null; };
  vu.zoneName = function (id) { var z = vu.zone(id); return z ? z.name : (id == null ? "" : "Zone " + id); };
  /* A position in its region's own coordinates (zone offset + /loc), or null when zones.js has no offsets. */
  vu.regionXY = function (zoneId, x, y) {
    var z = vu.zone(zoneId); if (!z) return null;
    var ox = z.ox != null ? z.ox : z.offx != null ? z.offx : z.rx != null ? z.rx : Array.isArray(z.off) ? z.off[0] : null;
    var oy = z.oy != null ? z.oy : z.offy != null ? z.offy : z.ry != null ? z.ry : Array.isArray(z.off) ? z.off[1] : null;
    return ox == null || oy == null ? null : [ox + x, oy + y];
  };
  vu.loc = function (zoneId, x, y) { return vu.zoneName(zoneId) + ": loc=" + Math.round(x) + "," + Math.round(y); };
  vu.locLine = function (zoneId, x, y) {
    var text = "/loc " + Math.round(x) + ", " + Math.round(y);
    var b = h("button.vu-btn", {type: "button", title: "Copy this position", style: {padding: "0 8px", fontSize: "12px"}}, "Copy");
    b.addEventListener("click", function (e) { e.stopPropagation(); if (ui.copy) ui.copy(vu.loc(zoneId, x, y), b); });
    return h("span.wg-nowrap", h("span.vu-loc", text), " ", b);
  };

  /* data/vendors.js -> {list, byId, byList, kinds, capitals, names} */
  vu.vendors = function () {
    var d = WG.data.vendors;
    if (!d) return {list: [], byId: new Map(), byList: new Map(), kinds: [], capitals: null, names: {}};
    if (d._vu) return d._vu;
    var list = tableOf(d.vendors || d.merchants || d), byId = new Map(), byList = new Map(), seenKind = {}, kinds = [];
    list.forEach(function (v) {
      byId.set(v.id, v);
      if (!byList.has(v.list)) byList.set(v.list, []);
      byList.get(v.list).push(v);
      var k = v.kind || "";
      if (k && !seenKind[k]) { seenKind[k] = 1; kinds.push(k); }
    });
    var labels = {};
    (d.kinds || []).forEach(function (k) { if (Array.isArray(k)) labels[k[0]] = k[1]; else if (k && typeof k === "object") labels[k.key || k.value || k.id] = k.label || k.name; });
    var order = (d.kinds || []).map(function (k) { return Array.isArray(k) ? k[0] : (k && typeof k === "object" ? (k.key || k.value || k.id) : k); }).filter(function (k) { return seenKind[k]; });
    kinds.sort().forEach(function (k) { if (order.indexOf(k) < 0) order.push(k); });
    d._vu = {list: list, byId: byId, byList: byList, capitals: d.capitals || null, names: d.names || {},
             kinds: order.map(function (k) { return [k, labels[k] || (k.charAt(0).toUpperCase() + k.slice(1))]; }), currencies: d.currencies || null};
    return d._vu;
  };
  vu.vendor = function (id) { var m = vu.vendors().byId; return m.get(id) || m.get(+id) || m.get(String(id)) || null; };
  vu.kindLabel = function (k) { var ks = vu.vendors().kinds; for (var i = 0; i < ks.length; i++) if (ks[i][0] === k) return ks[i][1]; return k || ""; };
  /* The realm a merchant serves: its own, or (for realm 0) the realm of the land it stands in. */
  vu.vRealm = function (v) { if (v.realm) return v.realm; var z = vu.zone(v.zone); return (z && z.realm) || 0; };

  /* The goods on one list: [{id, price, page, slot, pack}] (price = what the merchant charges, in copper or in the
     merchant's own currency). */
  vu.goods = function (listId) {
    var d = WG.data.vendoritems; if (!d) return [];
    var lists = d.lists || d, c = (d._vuGoods = d._vuGoods || {});
    if (c[listId]) return c[listId];
    var raw = lists[listId] || [];
    if (raw.cols) raw = WG.rows(raw);
    c[listId] = raw.map(function (r) {
      return Array.isArray(r) ? {id: r[0], price: r[1], page: r[2], slot: r[3], pack: r[4] || 1}
                              : {id: r.id || r.item, price: r.price, page: r.page, slot: r.slot, pack: r.pack || 1};
    });
    return c[listId];
  };

  /* data/items.js by id. Items sold but missing from items.js get a stub (name from the vendor files' `names`). */
  vu.items = function () {
    var d = WG.data.items;
    if (!d) return new Map();
    if (!d._vuMap) {
      d._vuMap = new Map();
      var norm = WG.item && typeof WG.item.norm === "function" ? WG.item.norm : null;
      tableOf(d.items || d).forEach(function (r) {
        var it = norm ? norm(r) : r;
        if (!it.type && it.obj != null) it.type = it.obj;      // data/items.js names the object type `obj`
        d._vuMap.set(it.id, it);
      });
    }
    return d._vuMap;
  };
  vu.item = function (id) {
    var it = vu.items().get(id);
    if (it) return it;
    var names = (WG.data.vendoritems && WG.data.vendoritems.names) || vu.vendors().names || {};
    return {id: id, name: names[id] || String(id), level: 0, realm: 0, type: 0, slot: 0, stub: true};
  };

  /* Everything a placed merchant sells: Map item id -> {id, it, vs: [{v, price, pack}], min, max} */
  vu.sold = function () {
    var d = WG.data.vendoritems; if (!d) return new Map();
    if (d._vuSold) return d._vuSold;
    var out = (d._vuSold = new Map());
    vu.vendors().byList.forEach(function (vs, listId) {
      vu.goods(listId).forEach(function (g) {
        var e = out.get(g.id);
        if (!e) { e = {id: g.id, it: vu.item(g.id), vs: [], min: Infinity, max: -Infinity, seen: new Set()}; out.set(g.id, e); }
        vs.forEach(function (v) {
          if (e.seen.has(v.id)) return;               // the same item twice on one list
          e.seen.add(v.id);
          e.vs.push({v: v, price: g.price, pack: g.pack});
          if (vu.coin(v)) { if (g.price < e.min) e.min = g.price; if (g.price > e.max) e.max = g.price; }
        });
      });
    });
    out.forEach(function (e) { delete e.seen; if (e.min === Infinity) { e.min = e.max = null; } });
    return out;
  };

  /* A price as the merchant charges it. Merchants that take something other than coin carry `currency`. */
  vu.coin = function (v) { return !v || !v.currency || v.currency === "coin"; };
  /* What a merchant takes instead of coin: "bounty points", "Daemon Sapphire Seals" ... */
  vu.currencyName = function (v, n) {
    if (vu.coin(v)) return "";
    if (v.currency === "bp") return n === 1 ? "bounty point" : "bounty points";
    var names = vu.vendors().currencies || {}, name = names[v.currencyItem] || names[v.currency] || v.currencyItem || v.currency;
    return String(name) + (n === 1 ? "" : "s");
  };
  vu.priceText = function (price, v, pack) {
    var t = vu.coin(v) ? (price ? WG.money(price) : "free") : (ui.num ? ui.num(price) : price) + " " + vu.currencyName(v, price);
    return t + (pack > 1 ? " for " + pack : "");
  };
  vu.price = function (price, v, pack) {
    var node;
    if (!vu.coin(v)) node = h("span.wg-nowrap", (ui.num ? ui.num(price) : price) + " " + vu.currencyName(v, price));
    else if (!price) node = h("span.wg-dim", "free");
    else node = ui.money ? ui.money(price) : h("span", WG.money(price));
    if (pack > 1) return h("span.wg-nowrap", node, h("span.wg-dim", " for " + pack));
    return node;
  };

  /* "Nearest capital first". With `capitals` from V-D and zone offsets from P-D: straight-line distance from the
     capital's gate inside the same region. Without them: capital cities first, then by zone name. Returns the
     sorted list and whether the distance rule could be used. */
  vu.byCapital = function (entries) {
    var caps = vu.vendors().capitals, my = WG.me.realm || 0, exact = false;
    function rank(v) {
      var r = vu.vRealm(v), z = vu.zone(v.zone), cap = caps && (caps[r] || caps[String(r)]), realmFirst = my && r !== my ? 1 : 0;
      if (cap) {
        var capZone = cap.zone != null ? cap.zone : cap.city;
        if (v.zone === capZone) { exact = true; return [realmFirst, r, 0, 0]; }
        var g = cap.gate, gz = g && vu.zone(g.zone);
        if (g && gz && z && gz.region === z.region) {
          var a = vu.regionXY(v.zone, v.x, v.y), b = vu.regionXY(g.zone, g.x, g.y);
          if (a && b) { exact = true; return [realmFirst, r, 1, Math.hypot(a[0] - b[0], a[1] - b[1])]; }
        }
        return [realmFirst, r, 2, 0];
      }
      return [realmFirst, r, z && z.kind === "city" ? 0 : 2, 0];
    }
    var keyed = entries.map(function (e) { return {e: e, k: rank(e.v), n: vu.zoneName(e.v.zone)}; });
    keyed.sort(function (a, b) {
      for (var i = 0; i < 4; i++) if (a.k[i] !== b.k[i]) return a.k[i] - b.k[i];
      return a.n < b.n ? -1 : a.n > b.n ? 1 : (a.e.v.name < b.e.v.name ? -1 : 1);
    });
    return {list: keyed.map(function (k) { return k.e; }), exact: exact};
  };

  vu.itemLink = function (it) {
    if (ui.itemLink) return ui.itemLink(it);
    return h("a", {href: WG.href("item/" + encodeURIComponent(it.id))}, it.name);
  };
  vu.typeText = function (it) { try { return it.stub ? "" : WG.item.typeSlot(it); } catch (e) { return ""; } };
  vu.statText = function (it) { try { return it.stub ? "" : WG.item.statText(it); } catch (e) { return ""; } };
  vu.bonusText = function (it) { try { return it.stub ? "" : WG.item.bonusText(it); } catch (e) { return ""; } };
  vu.kindOf = function (it) { try { return it.stub ? "other" : WG.item.kind(it.type); } catch (e) { return "other"; } };
  vu.mapNote = function () { return h("div.vu-hint", "The map viewer is not loaded (app/components/map.js)."); };

  /* ------------------------------------------------------------------ the page */
  var CATS = [["gear", "All gear"], ["armor", "Armor"], ["weapon", "Weapons"], ["shield", "Shields"], ["instrument", "Instruments"],
              ["jewelry", "Jewelry"], ["other", "Other goods"], ["all", "Everything"]];
  var KIND_ORDER = {armor: 0, weapon: 1, shield: 2, instrument: 3, jewelry: 4, other: 5};
  var REALM_OPTS = [["", "Any realm"], [1, "Albion"], [2, "Midgard"], [3, "Hibernia"]];
  var live = null;     // the page instance now on screen (so a late event from an old one does nothing)

  function levelDefault() { var L = Math.max(1, Math.min(50, WG.me.level || 1)); return [Math.max(1, L - 5), L]; }
  /* "Reset the filters": the page as it opens with nothing in the address (draws again even when the address is already bare). */
  function startOver(query) {
    var target = WG.href("vendors", query);
    if (location.hash === target) window.dispatchEvent(new HashChangeEvent("hashchange")); else location.hash = target;
  }

  function render(el, params, query) {
    vu.css();
    var me = (live = {maps: []});
    var mode = query.mode === "merchants" ? "merchants" : "items";
    if (ui.page) ui.page(el, {title: "Merchants", sub: "What the merchants of the True 1.65 world sell, at the price they charge, and where they stand."});
    else el.appendChild(h("h1", "Merchants"));
    el.appendChild(h("div.vu-modes",
      h("a.vu-mode" + (mode === "items" ? ".on" : ""), {href: WG.href("vendors")}, "Find something to buy", h("small", "armor or weapon, slot, level: who sells it")),
      h("a.vu-mode" + (mode === "merchants" ? ".on" : ""), {href: WG.href("vendors", {mode: "merchants"})}, "Browse merchants", h("small", "by realm, zone and kind of goods"))));
    var body = h("div"); el.appendChild(body);
    if (ui.loading) ui.loading(body, "Loading merchants..."); else body.textContent = "Loading...";
    var ready = WG.item && WG.item.ready ? WG.item.ready() : Promise.resolve();
    return ready.then(function () { return WG.load("vendors", "vendoritems", "items", "zones"); }).then(function () {
      if (live !== me) return;
      WG.clear(body);
      if (mode === "items") itemsMode(body, query, me); else merchantsMode(body, query, me);
    });
  }

  /* ---------- "find something to buy" ---------- */
  function itemsMode(el, query, me) {
    var sold = Array.from(vu.sold().values());
    var hasClass = !!(WG.me.cls && WG.item && WG.item.cls && WG.item.cls(WG.me.cls));
    var defs = [
      {key: "cat", label: "What", type: "chips", required: true, def: "gear", options: CATS},
      {key: "type", label: "Type", type: "select", options: [["", "Any type"]]},
      {key: "slot", label: "Slot", type: "select", options: [["", "Any slot"]]},
      {key: "level", label: "Item level", type: "range", min: 0, max: 51, def: levelDefault(),
       title: "Starts at your level and the five below it (the level in the box at the top right)"},
      {key: "realm", label: "Realm", type: "select", def: WG.me.realm || "", options: REALM_OPTS, title: "The realm of the merchant"},
      {key: "q", label: "Name", type: "text", placeholder: "part of an item name", width: 190},
      {key: "mine", label: "Only what my class can use", type: "toggle", hidden: !hasClass}
    ];
    defs = ui.filters.fromQuery(defs, query);
    var bar = h("div"), split = h("div.vu-split"), left = h("div"), side = h("div.wg-card.vu-side");
    var count = h("div.vu-count"), tbox = h("div");
    WG.append(left, [count, tbox]); WG.append(split, [left, side]); WG.append(el, [bar, split]);
    var sel = query.item || "", sort = (query.sort || "level.desc").split(".");

    /* the type and slot lists follow the chosen kind and hold only what merchants really sell */
    function catOf(e) { return vu.kindOf(e.it); }
    function inCat(e, cat) { var k = catOf(e); return cat === "all" || (cat === "gear" ? k !== "other" : k === cat); }
    function optionLists(cat) {
      var types = {}, slots = {};
      sold.forEach(function (e) {
        if (!inCat(e, cat) || e.it.stub) return;
        if (catOf(e) !== "other" && catOf(e) !== "jewelry") types[e.it.type] = (types[e.it.type] || 0) + 1;
        if (catOf(e) !== "other" && e.it.slot) slots[e.it.slot] = (slots[e.it.slot] || 0) + 1;
      });
      function opts(map, name, any) {
        var o = Object.keys(map).map(function (k) { return {value: +k, label: name(+k), count: map[k]}; });
        o.sort(function (a, b) { return a.label < b.label ? -1 : 1; });
        return [{value: "", label: any}].concat(o);
      }
      return {type: opts(types, WG.item.typeName, cat === "armor" ? "Any armor type" : cat === "weapon" ? "Any weapon type" : "Any type"),
              slot: opts(slots, WG.item.slotName, "Any slot")};
    }
    var f = ui.filters(bar, defs, function (v, key) {
      if (key === "cat" || key == null) {            // a new kind (or Reset): its own type and slot lists
        var o = optionLists(v.cat); f.setOptions("type", o.type); f.setOptions("slot", o.slot);
        if (key === "cat") { f.set("type", ""); f.set("slot", ""); }
      }
      apply();
    });
    (function () { var v = f.values(), o = optionLists(v.cat); f.setOptions("type", o.type); f.setOptions("slot", o.slot); f.set("type", v.type); f.set("slot", v.slot); })();
    bar.appendChild(h("div", {style: {margin: "6px 0 0"}}, h("button.vu-btn", {type: "button", onclick: function () { startOver(null); }}, "Reset the filters")));

    function rowsFor(v) {
      var q = (v.q || "").trim().toLowerCase(), lo = v.level[0], hi = v.level[1], realm = +v.realm || 0, out = [];
      for (var i = 0; i < sold.length; i++) {
        var e = sold[i], it = e.it;
        if (!inCat(e, v.cat)) continue;
        if (v.type !== "" && it.type !== v.type) continue;
        if (v.slot !== "" && it.slot !== v.slot) continue;
        if (lo != null && (it.level || 0) < lo) continue;
        if (hi != null && (it.level || 0) > hi) continue;
        if (q && String(it.name).toLowerCase().indexOf(q) < 0) continue;
        if (realm && !e.vs.some(function (s) { var r = vu.vRealm(s.v); return !r || r === realm; })) continue;
        if (v.mine && hasClass && !WG.item.usableBy(it, WG.me.cls, WG.me.level)) continue;
        out.push(e);
      }
      /* the order under the table's own sort: armor before weapons, then by type (the heaviest armor first), tier and slot */
      out.sort(function (a, b) {
        var byType = catOf(a) === "armor" ? (b.it.type || 0) - (a.it.type || 0) : (a.it.type || 0) - (b.it.type || 0);
        return (KIND_ORDER[catOf(a)] - KIND_ORDER[catOf(b)]) || byType || ((b.it.level || 0) - (a.it.level || 0)) ||
               ((a.it.slot || 0) - (b.it.slot || 0)) || (a.it.name < b.it.name ? -1 : a.it.name > b.it.name ? 1 : 0);
      });
      return out;
    }
    function sellersIn(e, realm) { return realm ? e.vs.filter(function (s) { var r = vu.vRealm(s.v); return !r || r === realm; }) : e.vs; }

    var narrow = window.innerWidth < 1300;        // one column less when the page is narrow: the stats go under the type
    var table = ui.table(tbox, {
      columns: [
        {key: "name", label: "Item", sort: function (e) { return e.it.name; }, render: function (e) {
          var b = vu.bonusText(e.it);
          return [vu.itemLink(e.it), b ? h("div.vu-sub", b) : null];
        }},
        {key: "level", label: "Lvl", num: true, width: 52, sort: function (e) { return e.it.level || 0; }, render: function (e) { return e.it.stub ? "" : e.it.level; }},
        narrow
          ? {key: "type", label: "Type and stats", sort: function (e) { return vu.typeText(e.it); }, render: function (e) { var st = vu.statText(e.it); return [h("span.wg-nowrap", vu.typeText(e.it)), st ? h("div.vu-sub.wg-nowrap", st) : null]; }}
          : {key: "type", label: "Type", sort: function (e) { return vu.typeText(e.it); }, render: function (e) { return h("span.wg-nowrap", vu.typeText(e.it)); }},
        narrow ? null
          : {key: "stats", label: "Stats", sort: function (e) { return e.it.dpsaf || 0; }, num: true, align: "left", render: function (e) { return h("span.wg-nowrap", vu.statText(e.it)); }},
        {key: "price", label: "Price", num: true, sort: function (e) { return e.min == null ? e.vs[0].price : e.min; }, render: function (e) {
          var s = e.vs[0];
          if (e.min == null) return vu.price(s.price, s.v, s.pack);
          if (e.min !== e.max) return h("span.wg-nowrap", vu.price(e.min, null, s.pack), h("span.wg-dim", " to "), vu.price(e.max, null));
          return vu.price(e.min, null, s.pack);
        }},
        {key: "where", label: "Sold by", num: true, sort: function (e) { return e.vs.length; }, render: function (e) {
          var ss = sellersIn(e, +f.values().realm || 0), n = ss.length;
          return h("button.vu-pick", {type: "button", title: "Show on the map", onclick: function () { choose(e.id); }}, n === 1 ? ss[0].v.name : n + " merchants");
        }}
      ].filter(Boolean),
      rows: [], sortKey: sort[0], sortDir: sort[1] || "desc", pageSize: 50, count: "items",
      empty: "No merchant sells this. Widen the level range, pick another type, or set Realm to Any realm.",
      onRow: function (e) { choose(e.id); },
      rowClass: function (e) { return e.id === sel ? "vu-sel" : ""; },
      onSort: function () { address(); }
    });

    function address() {
      var st = table.state(), q = Object.assign({}, f.query());
      if (sel) q.item = sel;
      if (st.sortKey && (st.sortKey + "." + st.sortDir) !== "level.desc") q.sort = st.sortKey + "." + st.sortDir;
      WG.setQuery(q);
    }
    function apply() {
      var v = f.values(), rows = rowsFor(v);
      count.textContent = "";
      WG.append(count, [h("b", ui.num(rows.length)), " of " + ui.num(sold.length) + " things merchants sell match. Choose a row to see where."]);
      table.setRows(rows);
      address();
      panel();
    }
    function choose(id) { sel = id === sel ? sel : id; table.refresh(); address(); panel(true); }

    function panel(scroll) {
      destroyMaps(me);
      WG.clear(side);
      var e = sel ? vu.sold().get(sel) : null;
      if (!e) {
        side.appendChild(h("div.vu-hint", sel ? "No merchant sells " + sel + "." : "Choose an item on the left: the map shows every merchant that sells it, nearest your capital first."));
        return;
      }
      var it = e.it, realm = +f.values().realm || 0;
      var all = sellersIn(e, realm), sorted = vu.byCapital(all), sellers = sorted.list;
      side.appendChild(h("div.vu-head", ui.icon ? ui.icon(it, 36) : null, h("div",
        h("h2", h("a", {href: WG.href("item/" + encodeURIComponent(it.id)), title: "Everything about this item: who drops it, who sells it"}, it.name)),
        h("div.wg-dim", [it.stub ? null : "Level " + it.level, vu.typeText(it), vu.statText(it)].filter(Boolean).join(" · ")),
        vu.bonusText(it) ? h("div.vu-sub", vu.bonusText(it)) : null)));
      var hidden = e.vs.length - all.length;
      side.appendChild(h("div", h("b", sellers.length === 1 ? "One merchant sells it" : sellers.length + " merchants sell it"),
        realm ? h("span.wg-dim", " in " + WG.REALMS[realm] + (hidden ? " (" + hidden + " more in other realms)" : "")) : null,
        sellers.length > 1 ? h("span.wg-dim", sorted.exact ? ", nearest the capital first." : ", capital cities first.") : null));
      var mapBox = h("div", {style: {margin: "8px 0"}}); side.appendChild(mapBox);
      var groups = [], byZone = new Map();
      sellers.forEach(function (s) {
        var g = byZone.get(s.v.zone);
        if (!g) { g = {zone: s.v.zone, pins: []}; byZone.set(s.v.zone, g); groups.push(g); }
        g.pins.push({x: s.v.x, y: s.v.y, kind: "vendor", color: "red", id: s.v.id, label: s.v.name,
                     sub: (s.v.guild ? s.v.guild + "\n" : "") + it.name + ": " + priceText(s), href: WG.href("vendor/" + s.v.id), legend: "Merchant that sells it"});
      });
      var multi = null;
      if (WG.Map && WG.Map.multi && groups.length) { multi = WG.Map.multi(mapBox, groups, {height: 400, order: "given", legend: false, preferLayer: "plan"}); me.maps.push(multi); }
      else if (groups.length) mapBox.appendChild(vu.mapNote());
      var list = h("ul.vu-rows");
      sellers.forEach(function (s, i) {
        var li = h("li", {tabIndex: 0, title: "Show this merchant on the map"},
          h("span", h("b", s.v.name), s.v.guild ? h("span.wg-dim", " " + s.v.guild) : null),
          vu.price(s.price, s.v, s.pack),
          h("a", {href: WG.href("vendor/" + s.v.id), title: "Everything this merchant sells"}, "goods"),
          h("span.where", ui.realm && !WG.me.realm ? [ui.realm(vu.vRealm(s.v), {short: true}), " · "] : null, vu.zoneName(s.v.zone), " · loc " + Math.round(s.v.x) + ", " + Math.round(s.v.y)));
        function show(ev) {
          if (ev && ev.target.closest("a")) return;
          Array.prototype.forEach.call(list.children, function (c) { c.classList.toggle("on", c === li); });
          if (multi) { multi.show(s.v.zone); multi.map.focus(s.v.id, {scroll: false}); }
        }
        li.addEventListener("click", show);
        li.addEventListener("keydown", function (ev) { if (ev.key === "Enter" || ev.key === " ") { ev.preventDefault(); show(); } });
        list.appendChild(li);
        if (i === 0) li.classList.add("on");
      });
      side.appendChild(list);
      if (multi && sellers.length) multi.map.focus(sellers[0].v.id, {scroll: false});
      if (scroll && side.getBoundingClientRect().top > window.innerHeight - 200) side.scrollIntoView({behavior: "smooth", block: "start"});
    }
    function priceText(s) { return vu.priceText(s.price, s.v, s.pack); }
    apply();
  }

  /* ---------- "browse merchants" ---------- */
  function merchantsMode(el, query, me) {
    var V = vu.vendors(), all = V.list;
    all.forEach(function (v) { if (v._n == null) v._n = v.n != null ? v.n : vu.goods(v.list).length; });
    function zoneOpts(realm) {
      var c = {};
      all.forEach(function (v) { if (realm && vu.vRealm(v) && vu.vRealm(v) !== realm) return; c[v.zone] = (c[v.zone] || 0) + 1; });
      var o = Object.keys(c).map(function (k) { return {value: +k, label: vu.zoneName(+k), count: c[k]}; });
      o.sort(function (a, b) { return a.label < b.label ? -1 : 1; });
      return [{value: "", label: "Any zone"}].concat(o);
    }
    var defs = ui.filters.fromQuery([
      {key: "realm", label: "Realm", type: "select", def: WG.me.realm || "", options: REALM_OPTS},
      {key: "zone", label: "Zone", type: "select", options: zoneOpts(0)},
      {key: "kind", label: "Sells", type: "chips", options: V.kinds},
      {key: "q", label: "Name", type: "text", placeholder: "merchant or guild", width: 190}
    ], query);
    var bar = h("div"), split = h("div.vu-split"), left = h("div"), side = h("div.wg-card.vu-side"), count = h("div.vu-count"), tbox = h("div");
    WG.append(left, [count, tbox]); WG.append(split, [left, side]); WG.append(el, [bar, split]);
    var sel = query.vendor ? +query.vendor : 0, sort = (query.sort || "name.asc").split(".");
    var f = ui.filters(bar, defs, function (v, key) {
      if (key === "realm" || key == null) f.setOptions("zone", zoneOpts(+v.realm || 0));      // the zones of that realm only
      if (key === "zone" || key === "realm" || key == null) sel = 0;                           // a new place: back to the zone's own map
      apply();
    });
    (function () { var v = f.values(); f.setOptions("zone", zoneOpts(+v.realm || 0)); f.set("zone", v.zone); })();
    if (!V.kinds.length) f.show("kind", false);
    bar.appendChild(h("div", {style: {margin: "6px 0 0"}}, h("button.vu-btn", {type: "button", onclick: function () { startOver({mode: "merchants"}); }}, "Reset the filters")));

    var table = ui.table(tbox, {
      columns: [
        {key: "name", label: "Merchant", render: function (v) { return [h("a", {href: WG.href("vendor/" + v.id), title: "Everything this merchant sells"}, v.name), v.guild ? h("div.vu-sub", v.guild) : null]; }},
        {key: "kind", label: "Sells", render: function (v) { return vu.kindLabel(v.kind); }, sort: function (v) { return vu.kindLabel(v.kind); }},
        {key: "zone", label: "Zone", sort: function (v) { return vu.zoneName(v.zone); }, render: function (v) { return vu.zoneName(v.zone); }},
        {key: "realm", label: "Realm", width: 80, sort: function (v) { return vu.vRealm(v); }, render: function (v) { return ui.realm(vu.vRealm(v)); }},
        {key: "_n", label: "Goods", num: true, width: 70},
        {key: "where", label: "", sort: false, width: 70, render: function (v) { return h("button.vu-pick", {type: "button", onclick: function () { choose(v.id); }}, "Map"); }}
      ],
      rows: [], sortKey: sort[0], sortDir: sort[1] || "asc", pageSize: 50, count: "merchants",
      empty: "No merchant matches. Set Realm to Any realm, choose Any zone, or clear the name.",
      onRow: function (v) { choose(v.id); },
      rowClass: function (v) { return v.id === sel ? "vu-sel" : ""; },
      onSort: function () { address(); }
    });
    function address() {
      var st = table.state(), q = Object.assign({mode: "merchants"}, f.query());
      if (sel) q.vendor = sel;
      if (st.sortKey && (st.sortKey + "." + st.sortDir) !== "name.asc") q.sort = st.sortKey + "." + st.sortDir;
      WG.setQuery(q);
    }
    function apply() {
      var v = f.values(), q = (v.q || "").trim().toLowerCase(), realm = +v.realm || 0;
      var rows = all.filter(function (m) {
        if (realm && vu.vRealm(m) && vu.vRealm(m) !== realm) return false;
        if (v.zone !== "" && m.zone !== v.zone) return false;
        if (v.kind && m.kind !== v.kind) return false;
        if (q && (m.name + " " + (m.guild || "")).toLowerCase().indexOf(q) < 0) return false;
        return true;
      });
      count.textContent = "";
      WG.append(count, [h("b", ui.num(rows.length)), " of " + ui.num(all.length) + " merchants. Choose a row to see where it stands."]);
      table.setRows(rows); address(); panel(rows);
    }
    function choose(id) { sel = id; table.refresh(); address(); panel(table.rows(), true); }
    function panel(rows, scroll) {
      destroyMaps(me); WG.clear(side);
      var v = sel ? vu.vendor(sel) : null;
      if (!v) {
        /* nothing chosen: when one zone is picked, show all its merchants on the map */
        var z = f.values().zone;
        if (z !== "" && rows.length && WG.Map) {
          side.appendChild(h("div", h("b", vu.zoneName(z)), h("span.wg-dim", ": " + rows.length + (rows.length === 1 ? " merchant" : " merchants") + ". Click a pin or a row.")));
          var box = h("div", {style: {marginTop: "8px"}}); side.appendChild(box);
          me.maps.push(WG.Map.create(box, {zone: z, height: 460, legend: false, onPin: function (p) { choose(p.id); },
            pins: rows.filter(function (m) { return m.zone === z; }).map(function (m) { return {x: m.x, y: m.y, kind: "vendor", id: m.id, label: m.name, sub: (m.guild || "") + (m.kind ? "\n" + vu.kindLabel(m.kind) : "")}; })}));
        } else side.appendChild(h("div.vu-hint", "Choose a merchant on the left to see where it stands. Pick a zone to see all its merchants on one map."));
        return;
      }
      vu.vendorCard(side, v, me, {height: 400, goods: 14});
      if (scroll && side.getBoundingClientRect().top > window.innerHeight - 200) side.scrollIntoView({behavior: "smooth", block: "start"});
    }
    apply();
  }

  /* One merchant as a card: name, where, map with the pin, and the first goods. Used here and on the item page. */
  vu.vendorCard = function (el, v, owner, opts) {
    opts = opts || {};
    el.appendChild(h("div.vu-head", h("div",
      h("h2", h("a", {href: WG.href("vendor/" + v.id)}, v.name)),
      h("div.wg-dim", [v.guild, vu.kindLabel(v.kind)].filter(Boolean).join(" · ")))));
    el.appendChild(h("div.vu-facts", h("span", ui.realm ? ui.realm(vu.vRealm(v)) : null), h("span", ui.zoneLink ? ui.zoneLink(v.zone, {loc: [v.x, v.y]}) : vu.zoneName(v.zone)), vu.locLine(v.zone, v.x, v.y)));
    var box = h("div"); el.appendChild(box);
    if (WG.Map) {
      var map = WG.Map.create(box, {zone: v.zone, height: opts.height || 420, legend: false, focus: 0, preferLayer: "plan",
        pins: [{x: v.x, y: v.y, kind: "vendor", color: "red", id: v.id, label: v.name, sub: v.guild || "", href: WG.href("vendor/" + v.id)}]});
      if (owner && owner.maps) owner.maps.push(map);
    } else box.appendChild(vu.mapNote());
    if (opts.goods) {
      var goods = vu.goods(v.list), shown = goods.slice(0, opts.goods), ul = h("ul.vu-rows");
      shown.forEach(function (g) { var it = vu.item(g.id); ul.appendChild(h("li", {style: {cursor: "default"}}, h("span", vu.itemLink(it)), h("span.wg-dim", it.stub || !it.level ? "" : "level " + it.level), vu.price(g.price, v, g.pack))); });
      el.appendChild(ul);
      el.appendChild(h("div", {style: {marginTop: "8px"}}, h("a", {href: WG.href("vendor/" + v.id)},
        goods.length > shown.length ? "All " + goods.length + " things " + v.name + " sells" : goods.length ? "Open " + v.name + "'s page" : v.name + " has nothing for sale")));
    }
  };

  function destroyMaps(me) { (me.maps || []).forEach(function (m) { try { m.destroy(); } catch (e) {} }); me.maps = []; }
  vu.destroyMaps = destroyMaps;

  WG.view("vendors", {title: "Merchants", nav: true, order: 60, render: render, leave: function () { if (live) destroyMaps(live); live = null; }});
  /* the level default follows the character box while this page is open and the level is not in the address */
  WG.onMe(function () {
    var r = WG.route();
    if (live && r.view === "vendors" && r.query.mode !== "merchants" && !("level" in r.query)) window.dispatchEvent(new HashChangeEvent("hashchange"));
  });
})();
