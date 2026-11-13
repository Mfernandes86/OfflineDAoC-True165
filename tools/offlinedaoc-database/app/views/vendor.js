/* OFFLINEDAOC Database - one merchant (lane V-U).
   #/vendor/<id>   where it stands (zone map with a pin) and everything it sells: level, key stats, price.
   &q=<text> &cat=<kind> &sort=<column>.<asc|desc> keep the table's state in the address. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = WG.ui || {}, vu = WG.vu;
  var live = null;
  var CAT_NAMES = {weapon: "Weapons", armor: "Armor", shield: "Shields", instrument: "Instruments", jewelry: "Jewelry", other: "Other goods"};

  function render(el, params, query) {
    vu.css();
    var me = (live = {maps: []}), id = params[0];
    var body = h("div"); el.appendChild(body);
    if (ui.loading) ui.loading(body, "Loading the merchant...");
    var ready = WG.item && WG.item.ready ? WG.item.ready() : Promise.resolve();
    return ready.then(function () { return WG.load("vendors", "vendoritems", "items", "zones"); }).then(function () {
      return WG.load("routes").then(function () {}, function () {});      // only for the link to a stable master's routes
    }).then(function () {
      if (live !== me) return;
      WG.clear(body);
      var v = vu.vendor(id);
      if (!v) {
        if (ui.page) ui.page(body, {title: "Merchant not found", crumbs: [["Merchants", WG.href("vendors", {mode: "merchants"})]]});
        body.appendChild(h("div.wg-empty", "There is no merchant number " + id + ". ", h("a", {href: WG.href("vendors", {mode: "merchants"})}, "Browse all merchants")));
        return;
      }
      document.title = v.name + " - OFFLINEDAOC Database";
      page(body, v, query, me);
    });
  }

  function page(el, v, query, me) {
    var goods = vu.goods(v.list).map(function (g, i) { return {g: g, it: vu.item(g.id), ord: (g.page || 0) * 1000 + (g.slot || 0), i: i}; });
    var realm = vu.vRealm(v);
    if (ui.page) ui.page(el, {title: v.name, crumbs: [["Merchants", WG.href("vendors", {mode: "merchants"})], [vu.zoneName(v.zone), WG.href("vendors", {mode: "merchants", realm: realm || "-", zone: v.zone})]],
      sub: [v.guild, vu.kindLabel(v.kind)].filter(Boolean).join(" · ")});
    else el.appendChild(h("h1", v.name));
    var split = h("div.vu-split"), left = h("div"), side = h("div.wg-card.vu-side");
    WG.append(split, [left, side]); el.appendChild(split);

    /* ---- where */
    var stable = stableOf(v);
    side.appendChild(h("div.vu-facts",
      h("span", ui.realm ? ui.realm(realm) : WG.REALMS[realm]),
      h("span", ui.zoneLink ? ui.zoneLink(v.zone, {loc: [v.x, v.y]}) : vu.zoneName(v.zone)),
      vu.locLine(v.zone, v.x, v.y)));
    var box = h("div"); side.appendChild(box);
    if (WG.Map) {
      /* zoomed in on one merchant: in a capital the floor plan is sharper than the painted map (zones without one ignore this) */
      me.maps.push(WG.Map.create(box, {zone: v.zone, height: 440, legend: false, focus: 0, preferLayer: "plan",
        pins: [{x: v.x, y: v.y, kind: "vendor", color: "red", id: v.id, label: v.name, sub: v.guild || ""}]}));
    } else box.appendChild(vu.mapNote());
    var notes = [];
    if (!vu.coin(v)) notes.push(h("div", h("b", "Takes " + vu.currencyName(v, 2)), ", not coin."));
    if (v.rep) {
      var meta = (WG.data.vendors && WG.data.vendors.meta) || {};
      notes.push(h("div.wg-warn", h("b", "Reputation: "),
        v.rep.text ? "needs " + v.rep.text + ". " : v.name + " belongs to " + (v.rep.factionName || "a faction") + " and refuses you while that faction is hostile to you. ",
        !v.rep.text && v.rep.startsRefused === true ? "A new character starts out refused." : !v.rep.text && v.rep.startsRefused === false ? "A new character is served." : "",
        meta.reputation ? h("div.vu-sub", meta.reputation) : null));
    }
    if (v.ownRealmOnly) notes.push(h("div", "Serves " + (realm ? WG.REALMS[realm] : "its own realm") + " characters only."));
    if (stable != null || v.stable || v.kind === "stable")
      notes.push(h("div", h("a", {href: WG.href("routes", stable != null ? {stable: stable} : null)}, "Where " + v.name + "'s tickets go (stable routes)")));
    if (notes.length) side.appendChild(h("div", {style: {marginTop: "10px", display: "grid", gap: "6px"}}, notes));
    var same = (vu.vendors().byList.get(v.list) || []).filter(function (o) { return o.id !== v.id; });
    if (same.length) {
      same.sort(function (a, b) { return vu.zoneName(a.zone) < vu.zoneName(b.zone) ? -1 : 1; });
      var ul = h("ul.vu-rows", {style: {maxHeight: "260px", overflow: "auto"}});
      same.forEach(function (o) { ul.appendChild(h("li", {style: {cursor: "default"}}, h("a", {href: WG.href("vendor/" + o.id)}, o.name), h("span.wg-dim", vu.zoneName(o.zone)), h("span"))); });
      side.appendChild(h("div", {style: {marginTop: "12px"}}, h("b", same.length === 1 ? "One other merchant sells the same goods" : same.length + " other merchants sell the same goods"), ul));
    }

    /* ---- what it sells */
    var cats = {}; goods.forEach(function (r) { var k = vu.kindOf(r.it); cats[k] = (cats[k] || 0) + 1; });
    var catKeys = Object.keys(CAT_NAMES).filter(function (k) { return cats[k]; });
    var defs = ui.filters.fromQuery([
      {key: "q", label: "Find in these goods", type: "text", placeholder: "part of a name", width: 220},
      {key: "cat", label: "Kind", type: "chips", hidden: catKeys.length < 2, options: catKeys.map(function (k) { return {value: k, label: CAT_NAMES[k], count: cats[k]}; })}
    ], query);
    var bar = h("div"), count = h("div.vu-count"), tbox = h("div");
    WG.append(left, [bar, count, tbox]);
    var sort = (query.sort || "ord.asc").split(".");
    var f = ui.filters(bar, defs, apply);
    var table = ui.table(tbox, {
      columns: [
        {key: "ord", label: "#", num: true, width: 54, title: "Where it is in the merchant's window: page, then place on the page", sort: function (r) { return r.ord; },
         render: function (r) { return h("span.wg-dim", ((r.g.page || 0) + 1) + "." + ((r.g.slot || 0) + 1)); }},
        {key: "name", label: "Item", sort: function (r) { return r.it.name; }, render: function (r) { var b = vu.bonusText(r.it); return [vu.itemLink(r.it), b ? h("div.vu-sub", b) : null]; }},
        {key: "level", label: "Lvl", num: true, width: 52, sort: function (r) { return r.it.level || 0; }, render: function (r) { return r.it.stub || !r.it.level ? "" : r.it.level; }},
        {key: "type", label: "Type", sort: function (r) { return vu.typeText(r.it); }, render: function (r) { return vu.typeText(r.it); }},
        {key: "stats", label: "Stats", num: true, align: "left", sort: function (r) { return r.it.dpsaf || 0; }, render: function (r) { return h("span.wg-nowrap", vu.statText(r.it)); }},
        {key: "price", label: "Price", num: true, sort: function (r) { return r.g.price; }, render: function (r) { return vu.price(r.g.price, v, r.g.pack); }}
      ],
      rows: [], sortKey: sort[0], sortDir: sort[1] || "asc", pageSize: 100, count: "goods",
      empty: goods.length ? "Nothing here matches. Clear the name or the kind." : v.name + " has nothing for sale.",
      rowHref: function (r) { return WG.href("item/" + encodeURIComponent(r.it.id)); },
      onSort: function () { address(); }
    });
    function address() {
      var st = table.state(), q = Object.assign({}, f.query());
      if (st.sortKey && (st.sortKey + "." + st.sortDir) !== "ord.asc") q.sort = st.sortKey + "." + st.sortDir;
      WG.setQuery(q);
    }
    function apply() {
      var val = f.values(), q = (val.q || "").trim().toLowerCase();
      var rows = goods.filter(function (r) { return (!val.cat || vu.kindOf(r.it) === val.cat) && (!q || String(r.it.name).toLowerCase().indexOf(q) >= 0); });
      WG.clear(count);
      WG.append(count, [h("b", v.name + " sells " + ui.num(goods.length) + (goods.length === 1 ? " thing" : " things")),
        rows.length !== goods.length ? " (" + ui.num(rows.length) + " shown)" : "", vu.coin(v) ? ". Prices are what the merchant charges." : ". Prices are in " + vu.currencyName(v, 2) + "."]);
      table.setRows(rows); address();
    }
    apply();
    var meta2 = (WG.data.vendors && WG.data.vendors.meta) || {};
    if (meta2.sell && meta2.sell.text) left.appendChild(h("div.vu-sub", {style: {marginTop: "10px"}}, "Selling: " + meta2.sell.text));
  }

  /* The stable id of a merchant that is a stable master (data/routes.js `stables[].vendor`), or null. */
  function stableOf(v) {
    var d = WG.data.routes; if (!d || !WG.routes) return null;
    var g = (d._vuGraph = d._vuGraph || WG.routes.build(d));
    for (var i = 0; i < g.stables.length; i++) {
      var s = g.stables[i];
      if (s.vendor != null && String(s.vendor) === String(v.id)) return s.id;
    }
    for (i = 0; i < g.stables.length; i++) {
      s = g.stables[i];
      if (s.vendor == null && s.name === v.name && s.zone === v.zone && Math.abs(s.x - v.x) < 50 && Math.abs(s.y - v.y) < 50) return s.id;
    }
    return null;
  }

  WG.view("vendor", {
    title: function (params) { var v = WG.data.vendors && vu.vendor(params[0]); return v ? v.name : "Merchant"; },
    navAs: "vendors", render: render,
    leave: function () { if (live) vu.destroyMaps(live); live = null; }
  });
})();
