/* OFFLINEDAOC Database - #/item/<id> : one item (lane I-U).
   Left: the item as the game shows it, what it is worth to your character, every way to get it.
   Right: who drops it (real chance per kill, drops.md) and who sells it; the chosen row is shown on the zone map. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, I = WG.item, U = WG.upgrade;
  var live = null;

  WG.onMe(function () { if (live && WG.route().view === "item") window.dispatchEvent(new Event("hashchange")); });

  WG.view("item", {
    title: "Item", navAs: "items",
    leave: function () { if (live && live.map) { try { live.map.destroy(); } catch (e) {} } live = null; },
    render: function (el, params) {
      var ui = WG.ui, id = params.join("/");
      if (!ui || !ui.table) { el.appendChild(h("div.wg-empty", "The shared page parts (app/components/widgets.js) are not there yet.")); return; }
      ui.loading(el, "Loading the item ...");
      var token = (live = {});
      return Promise.all([I.db(), WG.load("camps").then(function (d) { return d.camps; }, function () { return null; })]).then(function (r) {
        if (live !== token) return;
        WG.clear(el);
        var db = r[0], it = db.get(id);
        if (!it) {
          el.appendChild(ui.empty("There is no item with the id \"" + id + "\" among the items that have a source in the True 1.65 world."));
          el.appendChild(h("div", {style: {textAlign: "center"}}, h("a.wg-btn", {href: WG.href("items", {q: id.replace(/_/g, " ")})}, "Search the item finder for it")));
          return;
        }
        document.title = it.name + " - OFFLINEDAOC Database";
        build(el, db, it, campsIndex(r[1]), token);
      });
    }
  });

  function campsIndex(c) {
    if (!c) return null;
    var t = c.camps || c, rows = t.cols ? WG.rows(t) : (Array.isArray(t) ? t : []), by = new Map();
    rows.forEach(function (cp) { var l = by.get(cp.kind); if (!l) by.set(cp.kind, (l = [])); l.push(cp); });
    return by;
  }
  function lv(a, b) { return a === b || b == null ? String(a) : a + " to " + b; }

  function build(el, db, it, camps, token) {
    var ui = WG.ui, my = Math.max(1, Math.min(50, +WG.me.level || 1)), myCls = +WG.me.cls || 0, myRealm = +WG.me.realm || 0;
    var D = WG.data.items || {}, gear = I.isGear(it);
    // the chance shown is for a character who can get the item: the item's realm when it has one, else your realm
    var realm = it.realm || myRealm;
    var loot = db.drops(it.id, realm), sold = db.vendors(it.id, realm);
    var rows = [];
    loot.forEach(function (d) { rows.push({t: "loot", d: d, key: d.key, kind: d.kind, chance: d.never ? 0 : d.chance, off: d.never || d.where === "absent"}); });
    db.otd(it.id).forEach(function (o) { rows.push({t: "otd", key: o.key, kind: o.kind, chance: 1, minLevel: o.minLevel}); });
    db.questDrops(it.id).forEach(function (q) { rows.push({t: "q", key: q.key, kind: q.kind, chance: q.chance, quest: q.quest}); });

    ui.page(el, {title: it.name, crumbs: [["Items", WG.href("items")], [I.typeSlot(it)]]});
    var left = h("div", {style: {flex: "0 0 372px", maxWidth: "100%", display: "flex", flexDirection: "column", gap: "12px"}});
    var right = h("div.wg-grow", {style: {minWidth: "min(100%, 560px)"}});
    el.appendChild(h("div.wg-row", {style: {flexWrap: "wrap", alignItems: "flex-start"}}, left, right));

    /* ----- left: the card ----- */
    var card = I.tooltip(it); card.style.maxWidth = "none";
    left.appendChild(h("div.wg-card", card));

    /* ----- left: how to get it, in words ----- */
    var how = h("div.wg-card"), lines = [], b = db.best(it, realm);
    how.appendChild(h("div.it-sec", {style: {marginTop: "0", paddingTop: "0", borderTop: "0"}}, "How to get it"));
    if (b.drop) lines.push(h("div", "Best drop: ", b.drop.kind ? ui.mobLink(b.drop.kind, {level: true}) : b.drop.key, ", ", h("b", b.drop.always ? "every kill" : WG.pct(b.drop.chance) + " per kill"),
      b.drops > 1 ? h("span.wg-dim", " (" + b.drops + " monsters drop it)") : null));
    db.otd(it.id).forEach(function (o) { lines.push(h("div", "One-time drop: ", o.kind ? ui.mobLink(o.kind, {level: true}) : o.key, h("span.wg-dim", ", put in your backpack the first time you help kill it at level " + o.minLevel + " or higher. Once per character."))); });
    var vb = b.vendor;
    if (vb) lines.push(h("div", "Cheapest merchant: ", ui.vendorLink(vb.v), ", ", I.priceEl(vb), vb.pack > 1 ? " for " + vb.pack : "", sold.length > 1 ? h("span.wg-dim", " (" + sold.length + " sell it)") : null));
    var quests = D.quests && D.quests[it.id] || [];
    quests.forEach(function (q) {
      var role = q[3] === "step" ? "Quest step item" : q[3] === "choice" ? "Quest reward (one of a choice)" : q[3] === "random reward" ? "Quest reward (random)" : q[3] === "trade" ? "Quest trade" : "Quest reward";
      lines.push(h("div", role + (q[0] ? ": " : ""), q[0] ? h("b", q[0]) : null, h("span.wg-dim", [q[2] ? ", from " + q[2] : "", q[1] ? ", level " + q[1] + " or higher" : ""].join(""))));
    });
    if ((it.src & I.SRC.quest) && !quests.length) lines.push(h("div", "Quest reward"));
    var craft = D.craft && D.craft[it.id];
    if (craft) {
      var skills = I.meta().craftSkills || {};
      lines.push(h("div", "Crafted: ", h("b", (skills[craft[0]] || "Crafting") + " " + craft[1]), h("div.wg-dim", {style: {paddingLeft: "12px"}}, (craft[2] || []).map(function (m, i) {
        var mi = db.get(m[0]);
        return [i ? ", " : "", m[1] + " x ", mi ? h("a", {href: I.href(mi)}, mi.name) : ((D.otherNames && D.otherNames[m[0]]) || m[0])];
      }))));
    } else if (it.src & I.SRC.craft) lines.push(h("div", "Crafted by players"));
    if (it.src & I.SRC.start) lines.push(h("div", "Starting equipment of a new character"));
    if (b.drop && b.drop.where === "script") lines.push(h("div.wg-dim", I.whereText("script", b.drop.kind) + "."));
    else if (b.drop && !b.drop.kind) lines.push(h("div.wg-dim", "\"" + b.drop.key + "\" is " + I.whereText(b.drop.where) + "."));
    if (!lines.length) lines.push(h("div.wg-dim", it.src & (I.SRC.drop | I.SRC.script) ? "It is on a loot table, but nothing in the world can drop it for this realm." : "No source."));
    WG.append(how, lines.map(function (l) { l.style.marginTop = "4px"; return l; }));
    if (it.fl & 64) how.appendChild(h("div.wg-warn", {style: {marginTop: "6px"}}, "A quest item: it falls only while you are on its quest."));
    left.appendChild(how);

    /* ----- left: what it is worth ----- */
    if (gear) {
      var ctx = {level: my, cls: myCls}, det = U.detail(it, ctx), vc = h("div.wg-card"), cname = myCls ? I.className(myCls) : "";
      vc.appendChild(h("div", {style: {display: "flex", justifyContent: "space-between", alignItems: "baseline", gap: "8px"}},
        h("span.it-sec", {style: {marginTop: "0", paddingTop: "0", borderTop: "0"}}, "Gear value for a level " + my + (cname ? " " + cname : " character")),
        h("b", {style: {fontSize: "18px", color: "var(--accent)"}}, String(det.value))));
      var tb = h("table", {style: {width: "100%", borderCollapse: "collapse", marginTop: "6px", fontSize: "13px"}});
      det.parts.forEach(function (p) { tb.appendChild(h("tr", h("td.wg-dim", {style: {padding: "1px 0"}}, p[0]), h("td", {style: {textAlign: "right", padding: "1px 0", whiteSpace: "nowrap", color: p[1] < 0 ? "var(--con-orange)" : ""}}, (p[1] > 0 ? "+" : "") + p[1]))); });
      vc.appendChild(tb);
      vc.appendChild(h("div.wg-dim.wg-small", {style: {marginTop: "8px"}}, "The number the server's gamebots use to choose gear" + (myCls ? ", weighed for your class and level" : "") + ". Set your character at the top right and it follows. It does not know the rest of your gear."));
      if (myCls && !I.usableBy(it, myCls, my)) vc.appendChild(h("div.wg-warn", {style: {marginTop: "6px"}}, (/^[aeiou]/i.test(cname) ? "An " : "A ") + cname + (I.usableBy(it, myCls) ? " cannot use this yet at level " + my + "." : " cannot use this.")));
      if (it.fair) vc.appendChild(h("div.wg-dim.wg-small", {style: {marginTop: "6px"}}, "Fair price on the Realm Exchange, as the server works it out: ", ui.money(it.fair)));
      var slotQ = it.slot === 36 ? 35 : it.slot === 34 ? 33 : it.slot;
      vc.appendChild(h("div", {style: {marginTop: "10px", display: "flex", gap: "8px", flexWrap: "wrap"}},
        h("a.wg-btn.small.primary", {href: WG.href("items", {m: "up", slot: slotQ, cur: it.id, realm: it.realm && it.realm !== myRealm ? it.realm : null}), title: "List what ranks above this piece in the same slot"}, "Find upgrades over this"),
        h("a.wg-btn.small", {href: WG.href("items", {slot: slotQ, type: I.kindOf(it) === "jewelry" ? null : it.type, realm: it.realm && it.realm !== myRealm ? it.realm : null}), title: "Other pieces of this type for this slot"}, "More like this")));
      left.appendChild(vc);
    }

    /* ----- right: sources (tabs when there are two kinds), then the map ----- */
    var tabsEl = h("div"), mapCap = h("div", {style: {margin: "12px 0 6px", display: "flex", gap: "10px", alignItems: "baseline", flexWrap: "wrap"}}), mapEl = h("div");
    /* Quests that reward, hand over or need the item: the quests lane draws the card (source/contract/quests.md section 1).
       Under the map when the item has a drop or a merchant, at the top of the column when quests are all there is. */
    var questEl = h("div"), hasPlace = rows.length > 0 || sold.length > 0;
    if (hasPlace) questEl.style.marginTop = "12px"; else questEl.style.marginBottom = "12px";
    WG.append(right, hasPlace ? [tabsEl, mapCap, mapEl, questEl] : [questEl, tabsEl]);
    if (WG.quest && typeof WG.quest.itemSection === "function") {
      try { var qp = WG.quest.itemSection(questEl, it.id); if (qp && qp.then) qp.then(null, function () {}); } catch (e) { console.error(e); }
    }
    var multi = null, selected = {drop: null, vendor: null}, tables = {};

    function showMap(groups, caption, emptyText) {
      WG.clear(mapCap); WG.append(mapCap, caption);
      if (!groups.length) {
        if (multi) { multi.destroy(); multi = null; token.map = null; }
        WG.clear(mapEl); mapEl.appendChild(ui.note(emptyText, "info"));
        return;
      }
      if (!WG.Map || !WG.Map.multi) { WG.clear(mapEl); mapEl.appendChild(ui.note("The map viewer (app/components/map.js) is not there yet.", "warn")); return; }
      if (multi) multi.setData(groups);
      else { WG.clear(mapEl); multi = WG.Map.multi(mapEl, groups, {height: 500}); token.map = multi; }
    }
    function selectDrop(r) {
      selected.drop = r; if (tables.drop) tables.drop.refresh();
      var k = r.kind, list = k && camps ? (camps.get(k.id) || []) : [], spawns = 0;
      if (k && k.scripted) {      // no camps: the spots where the script makes it appear (mobs.scripted.where)
        var spots = (k.where || []).map(function (w) { return {zone: w[0], x: w[1], y: w[2], kind: "camp", label: k.name, sub: "appears here (made by a script)", legend: "Where the script makes it appear", href: WG.href("mob/" + k.id)}; });
        showMap(WG.Map && WG.Map.groupByZone ? WG.Map.groupByZone(spots) : [],
          [h("b", "Where to find "), ui.mobLink(k, {level: true}), h("span.wg-dim", I.whereText("script", k) + (spots.length ? ". The red pin is where it appears." : "."))],
          "The script works out the place when it runs: there is no fixed spot to show.");
        return;
      }
      var places = list.map(function (c) {
        spawns += c.n || 1;
        return {zone: c.zone, x: c.x, y: c.y, kind: "camp", n: c.n, radius: c.r || 0, label: k.name,
          sub: "level " + lv(c.lmin, c.lmax) + " - " + (c.n || 1) + ((c.n || 1) === 1 ? " spawn" : " spawns"), href: WG.href("mob/" + k.id), id: "c" + c.id};
      });
      var cap = [h("b", "Where to find "), k ? ui.mobLink(k, {level: true}) : h("b", r.key)];
      if (places.length) cap.push(h("span.wg-dim", ui.num(spawns) + (spawns === 1 ? " spawn in " : " spawns in ") + places.length + (places.length === 1 ? " camp" : " camps") + ". A red pin is a camp, the number in it is how many spawn there. Click a pin for the monster's page."));
      showMap(WG.Map && WG.Map.groupByZone ? WG.Map.groupByZone(places) : [], cap,
        !k ? "\"" + r.key + "\": " + I.whereText(r.d ? r.d.where : "npc") + ". There is no place to show."
          : !camps ? "The camp data (data/camps.js) is not built yet." : "No camp of this monster is placed in a zone.");
    }
    function selectVendor(s) {
      selected.vendor = s; if (tables.vendor) tables.vendor.refresh();
      var v = s.v, has = v.zone != null && v.x != null;
      showMap(has ? [{zone: v.zone, pins: [{x: v.x, y: v.y, kind: "vendor", label: v.name, sub: (v.guild ? v.guild + "\n" : "") + I.priceText(s), href: WG.href("vendor/" + v.id)}]}] : [],
        [h("b", "Where to find "), ui.vendorLink(v), v.guild ? h("span.wg-dim", v.guild) : null, has ? h("span.wg-dim", "The gold pin is the merchant.") : null], "This merchant has no position in a zone.");
    }

    var tabs = [];
    if (rows.length) tabs.push({key: "drop", label: "Dropped by", count: rows.length, render: dropTab});
    if (sold.length) tabs.push({key: "vendor", label: "Sold by", count: sold.length, render: vendorTab});
    if (!tabs.length) {
      tabsEl.appendChild(h("div.wg-card", h("b", "No monster drops this and no merchant sells it."),
        h("div.wg-dim", {style: {marginTop: "4px"}}, "\"How to get it\" on the left lists its quests and recipe.")));
    } else if (tabs.length === 1) {
      var sec = ui.section(tabs[0].label, {count: tabs[0].count});
      tabsEl.appendChild(sec); tabs[0].render(sec.body);
    } else {
      ui.tabs(tabsEl, tabs, {active: "drop", onChange: function (key) {
        if (key === "drop" && selected.drop) selectDrop(selected.drop);
        if (key === "vendor") selectVendor(selected.vendor || sold[0]);
      }});
    }

    function chanceCell(r) {
      if (r.t === "otd") return h("span", {title: "A one-time drop: certain on your first kill at level " + r.minLevel + " or higher, once per character"}, h("b", "Once"), h("span.wg-dim", " at level " + r.minLevel + "+"));
      var d = r.d;
      if (d && d.never) return h("span.wg-dim", {title: "A quest item that no quest names: it never falls"}, "never");
      if (d && d.where === "absent") return h("span.wg-dim", {title: "The chance on its loot table. Nothing spawns this monster, so it cannot be earned."}, WG.pct(r.chance));
      if (d && d.always) return h("b", {title: "A fixed drop: every kill"}, "100%");
      var title = null;
      if (d) { var m = db.drops(it.id, 0).filter(function (x) { return x.key === d.key; })[0]; var by = [1, 2, 3].map(function (re) { var x = db.drops(it.id, re).filter(function (y) { return y.key === d.key; })[0]; return x ? x.chance : 0; });
        if (m && (by[0] !== by[1] || by[1] !== by[2])) title = "Albion " + WG.pct(by[0]) + ", Midgard " + WG.pct(by[1]) + ", Hibernia " + WG.pct(by[2]) + " (the realm of the killer, or of the group leader)"; }
      return h("span", {title: title}, ui.bar(r.chance, {text: WG.pct(r.chance), width: 64, color: "accent"}));
    }
    function noteCell(r) {
      var out = [];
      if (r.t === "q") out.push(h("span.wg-warn", {title: "Put in your backpack by the quest, only while you are on it"}, "quest: " + r.quest));
      if (r.d && r.d.quest) out.push(h("span.wg-warn", {title: "Falls only while you are on the quest"}, "quest" + (r.d.quests ? ": " + r.d.quests.join(", ") : "")));
      if (r.d && r.d.n > 1) out.push(h("span.wg-dim", (out.length ? " · " : "") + r.d.n + " at a time"));
      var wh = r.d ? r.d.where : (r.kind ? (r.kind.scripted ? "script" : "monster") : "npc");
      if (wh !== "monster") out.push(h("span.wg-dim", {title: I.whereText(wh, r.kind)}, (out.length ? " · " : "") + (wh === "script" ? "made by a script" : wh === "absent" ? "not in the world" : "not a monster you can hunt")));
      return out;
    }
    function dropTab(panel) {
      var rn = realm ? WG.REALMS[realm] : "";
      panel.appendChild(h("div.wg-dim.wg-small", {style: {margin: "0 0 6px"}},
        "Chance = the real chance that one kill of that monster drops this item" + (rn ? " for " + (it.realm ? (rn === "Albion" ? "an" : "a") : "your") + " " + rn + " character" + (it.realm ? " (the item is " + rn + " only)" : "") : "") +
        ". The kills columns are arithmetic from that chance, 1 - (1 - chance) ^ kills, not a promise. A monster that is grey to you drops nothing. Click a row to see its camps on the map."));
      var box = h("div"); panel.appendChild(box);
      tables.drop = ui.table(box, {
        rows: rows, pageSize: 8, dense: true, stickyHead: false, count: rows.length === 1 ? "monster" : "monsters", sortKey: "chance", sortDir: "desc",
        rowClass: function (r) { return r === selected.drop ? "it-sel" : ""; },
        onRow: function (r) { selectDrop(r); },
        columns: [
          {key: "name", label: "Monster", sort: function (r) { return r.kind ? r.kind.name : r.key; }, render: function (r) { return r.kind ? ui.mobLink(r.kind) : r.key; }},
          {key: "level", label: "Level", sort: function (r) { return r.kind ? r.kind.levelMin : null; },
            render: function (r) {
              if (!r.kind || r.kind.levelMin == null) return "";
              var out = [ui.level(r.kind.levelMin, r.kind.levelMax, my)];
              if (WG.con && r.kind.levelMax != null && WG.con.color(my, r.kind.levelMax) === "grey") out.push(h("span.wg-warn.wg-small", {title: "Grey to a level " + my + " character: a grey kill gives no loot"}, " grey to you"));
              return out;
            }},
          {key: "chance", label: "Chance per kill", num: true, sort: function (r) { return r.chance; }, render: chanceCell},
          {key: "k50", label: "Kills for 50%", num: true, title: "Arithmetic: after this many kills the chance of at least one drop reaches 50%",
            sort: function (r) { return r.off ? null : r.t === "otd" ? 1 : I.killsFor(r.chance, 0.5); }, render: function (r) { return r.off ? "" : r.t === "otd" ? "1" : ui.num(I.killsFor(r.chance, 0.5)); }},
          {key: "k90", label: "for 90%", num: true, title: "Arithmetic: kills for a 90% chance of at least one drop",
            sort: function (r) { return r.off ? null : r.t === "otd" ? 1 : I.killsFor(r.chance, 0.9); }, render: function (r) { return r.off ? "" : r.t === "otd" ? "1" : ui.num(I.killsFor(r.chance, 0.9)); }},
          {key: "zones", label: "Where", sort: function (r) { var z = zonesOf(r); return z.length ? db.zoneName(z[0]) : null; }, render: function (r) { return zonesCell(zonesOf(r), r.kind); }},
          {key: "note", label: "", sort: false, render: noteCell}
        ]
      });
      var first = null; for (var i = 0; i < rows.length; i++) if (rows[i].kind && rows[i].chance > 0) { first = rows[i]; break; }   // rows come best first (db.drops)
      selectDrop(first || rows[0]);
    }
    function zonesOf(r) { return r.d ? r.d.zones : (r.kind ? r.kind.zoneIds : []); }
    function zonesCell(zl, k) {
      if (!zl.length) return "";
      var out = [];
      zl.slice(0, 2).forEach(function (z, i) { if (i) out.push(", "); out.push(ui.zoneLink(z, k ? {query: {kind: k.id}} : null)); });
      if (zl.length > 2) out.push(h("span.wg-dim", {title: zl.slice(2).map(db.zoneName).join(", ")}, " +" + (zl.length - 2)));
      return out;
    }

    function vendorTab(panel) {
      var notes = [];
      if (sold.some(function (s) { return s.pack > 1; })) notes.push("The price buys the whole pack.");
      if (sold.some(function (s) { return s.v.rep; })) notes.push("A merchant of a faction refuses you while that faction is hostile to you.");
      if (notes.length) panel.appendChild(h("div.wg-dim.wg-small", {style: {margin: "0 0 6px"}}, notes.join(" ")));
      var box = h("div"); panel.appendChild(box);
      tables.vendor = ui.table(box, {
        rows: sold, pageSize: 8, dense: true, stickyHead: false, count: sold.length === 1 ? "merchant" : "merchants",
        rowClass: function (s) { return s === selected.vendor ? "it-sel" : ""; },
        onRow: function (s) { selectVendor(s); },
        columns: [
          {key: "name", label: "Merchant", sort: function (s) { return s.v.name; }, render: function (s) { return ui.vendorLink(s.v); }},
          {key: "guild", label: "Kind", sort: function (s) { return s.v.guild; }, render: function (s) { return s.v.guild || ""; }},
          {key: "price", label: "Price", num: true, sort: function (s) { return s.price; }, render: function (s) { return [I.priceEl(s), s.pack > 1 ? h("span.wg-dim", " for " + s.pack) : null]; }},
          {key: "realm", label: "Realm", sort: function (s) { return s.v.realm; }, render: function (s) { return [s.v.realm ? ui.realm(s.v.realm) : h("span.wg-dim", "None"), s.v.ownRealmOnly ? h("span.wg-warn", {title: "Trades only with characters of its own realm"}, " only") : null]; }},
          {key: "zone", label: "Where", sort: function (s) { return s.v.zone != null ? db.zoneName(s.v.zone) : null; }, render: function (s) { return s.v.zone != null ? ui.zoneLink(s.v.zone, s.v.x != null ? {loc: [s.v.x, s.v.y]} : null) : ""; }},
          {key: "note", label: "", sort: false, render: function (s) { return s.v.rep ? h("span" + (s.v.rep.startsRefused ? ".wg-warn" : ".wg-dim"), {title: s.v.rep.text || ""}, s.v.rep.factionName + (s.v.rep.startsRefused ? ": refuses a new character" : " reputation")) : ""; }}
        ]
      });
      if (!rows.length) selectVendor(sold[0]);
    }
  }

  if (!document.getElementById("it-css2")) {
    document.head.appendChild(h("style", {id: "it-css2", textContent:
      "table.wg-table tr.it-sel td{background:rgba(212,169,74,.14)}table.wg-table tr.it-sel td:first-child{box-shadow:inset 3px 0 0 var(--accent)}"}));
  }
})();
