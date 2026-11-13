/* OFFLINEDAOC Database - #/items : the item and upgrade finder (lane I-U).
   One screen: who it is for (class, level, realm), what (slot, type, item level, name, source), how to rank.
   Every filter lives in the address, so Back works and a search can be bookmarked.
   Data: WG.item.db() (app/lib/itemtext.js). Ranking: WG.upgrade (app/lib/upgrade.js). */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, I = WG.item, U = WG.upgrade;

  var SLOT_IDS = [25, 21, 28, 22, 27, 23, 10, 11, 12, 13, 26, 29, 24, 32, 35, 33];
  var SRC = [["d", "Monster drops", 1 | 2 | 128], ["v", "Merchants", 4], ["q", "Quest rewards", 8], ["c", "Crafted", 16]];   // items.md section 1: 2 one-time drop, 128 summoned boss
  var REALM_SHORT = ["", "Alb", "Mid", "Hib"];
  var ARMOR_SLOTS = {21: 1, 22: 1, 23: 1, 25: 1, 27: 1, 28: 1}, HAND_SLOTS = {10: 1, 11: 1, 12: 1, 13: 1};
  var DEFAULT_SLOT = 25;      // chest: where the upgrade mode starts

  /* Does the item go in the slot the owner picked? (items.md section 3 rule 7: a slot 11 weapon can also be held in
     the right hand; in the left hand it needs a class that may hold a weapon there. Shields are left hand only.) */
  function slotIs(it, want, cls) {
    var s = it.slot;
    if (want === 35) return s === 35 || s === 36;
    if (want === 33) return s === 33 || s === 34;
    if (want === 10) return s === 10 || (s === 11 && it.type !== 42);
    if (want === 11) return s === 11 && (it.type === 42 || !cls || !!cls.leftHand);
    return s === want;
  }
  function slotKey(s) { return s === 36 ? 35 : s === 34 ? 33 : s; }
  function clampInt(v, lo, hi, dflt) { v = Math.round(+v); if (isNaN(v)) v = dflt; return Math.max(lo, Math.min(hi, v)); }
  function defRange(lvl) { return [Math.max(1, lvl - 5), Math.min(51, lvl + 2)]; }
  function sameRange(a, b) { return (a[0] == null ? "" : a[0]) === (b[0] == null ? "" : b[0]) && (a[1] == null ? "" : a[1]) === (b[1] == null ? "" : b[1]); }
  function parseRange(s) {
    var m = /^(\d*)-(\d*)$/.exec(String(s));
    return m ? [m[1] === "" ? null : +m[1], m[2] === "" ? null : +m[2]] : [null, null];
  }
  function typeFits(type, slot) {          // can the type filter and the slot filter both be true for one item?
    if (!slot || type === "") return true;
    var kind = typeof type === "number" ? I.kind(type) : type;
    if (kind === "other") return false;
    if (ARMOR_SLOTS[slot]) return kind === "armor";
    if (HAND_SLOTS[slot]) return kind === "weapon" || kind === "shield" || kind === "instrument";
    return kind === "jewelry";
  }

  /* ---------- state <-> address ---------- */
  function defRealm(cls) { var c = cls ? I.cls(cls) : null; return c && c.realm ? c.realm : (+WG.me.realm || 0); }
  function readState(q) {
    var me = WG.me, S = {};
    S.m = q.m === "up" ? "up" : "";
    S.cls = q.cls != null ? (+q.cls || 0) : (+me.cls || 0);
    if (S.cls && !I.cls(S.cls)) S.cls = 0;
    S.lvl = clampInt(q.lvl != null ? q.lvl : me.level, 1, 50, 1);
    S.realm = q.realm != null ? clampInt(q.realm, 0, 3, 0) : defRealm(S.cls);
    S.slot = +q.slot || 0; if (SLOT_IDS.indexOf(S.slot) < 0) S.slot = 0;
    S.type = q.type == null || q.type === "" ? "" : (isNaN(+q.type) ? String(q.type) : +q.type);
    S.il = q.il != null ? parseRange(q.il) : defRange(S.lvl);
    S.src = q.src ? String(q.src).split(",").filter(function (k) { return SRC.some(function (s) { return s[0] === k; }); }) : [];
    S.q = q.q || "";
    S.focus = q.focus ? String(q.focus).split(",").filter(function (k) { return U.FOCI.some(function (f) { return f.key === k; }); }) : [];
    S.cur = q.cur || "";
    S.sort = q.sort || ""; S.dir = q.dir === "asc" || q.dir === "desc" ? q.dir : "";
    if (S.m === "up" && !S.slot) S.slot = DEFAULT_SLOT;
    return S;
  }
  function writeQuery(S) {
    var me = WG.me, q = {};
    if (S.m) q.m = S.m;
    if (S.cls !== (+me.cls || 0)) q.cls = String(S.cls);
    if (S.lvl !== clampInt(me.level, 1, 50, 1)) q.lvl = S.lvl;
    if (S.realm !== defRealm(S.cls)) q.realm = String(S.realm);
    if (S.slot && !(S.m === "up" && S.slot === DEFAULT_SLOT)) q.slot = S.slot;
    if (S.type !== "") q.type = S.type;
    if (!sameRange(S.il, defRange(S.lvl))) q.il = (S.il[0] == null ? "" : S.il[0]) + "-" + (S.il[1] == null ? "" : S.il[1]);
    if (S.src.length) q.src = S.src.join(",");
    if (S.q) q.q = S.q;
    if (S.focus.length) q.focus = S.focus.join(",");
    if (S.cur && S.m === "up") q.cur = S.cur;
    if (S.sort && !(S.sort === "value" && (S.dir || "desc") === "desc")) { q.sort = S.sort; if (S.dir) q.dir = S.dir; }
    return q;
  }

  /* ---------- the view ---------- */
  var live = null;     // the page that is on screen (for WG.onMe)
  WG.onMe(function () {
    if (!live || WG.route().view !== "items") return;
    var q = WG.route().query;
    if (q.cls != null && q.lvl != null && q.realm != null) return;   // the address pins all three: the header box does not change this search
    window.dispatchEvent(new Event("hashchange"));
  });

  WG.view("items", {
    title: "Items", nav: true, order: 10,
    leave: function () { live = null; },
    render: function (el, params, query) {
      var ui = WG.ui;
      if (!ui || !ui.table || !ui.filters) { el.appendChild(h("div.wg-empty", "The shared page parts (app/components/widgets.js) are not there yet.")); return; }
      ui.loading(el, "Loading the item catalogue ...");
      var token = (live = {});
      return I.db().then(function (db) { if (live === token) { WG.clear(el); build(el, db, query, token); } });
    }
  });

  function build(el, db, query, token) {
    var ui = WG.ui, S = readState(query), table = null, f = null, picker = null;
    var classes = I.classes(), D = WG.data.items || {}, craftSkills = I.meta().craftSkills || {};
    function slotLabel(s) { return I.slotName(s); }

    ui.page(el, {title: "Items and upgrades",
      sub: "Say who it is for and what you are looking for. The list shows the best pieces first, and for each one the best way to get it."});
    var barEl = h("div"), wearEl = h("div"), headEl = h("div", {style: {margin: "4px 0 4px", display: "flex", gap: "14px", alignItems: "center", flexWrap: "wrap"}});
    var explainEl = h("div.wg-dim.wg-small", {style: {margin: "0 0 8px", maxWidth: "1150px"}}), listEl = h("div");
    WG.append(el, [barEl, wearEl, headEl, explainEl, listEl]);
    if (db.missing.length) el.insertBefore(ui.note("Not built yet: " + db.missing.map(function (n) { return "data/" + n + ".js"; }).join(", ") + ". What depends on it is left empty.", "warn"), barEl);
    if (!(+WG.me.cls) && query.cls == null && (+WG.me.level || 1) <= 1 && query.lvl == null)
      el.insertBefore(ui.note("Tip: set your level, realm and class in the box at the top right. The finder then starts from what your character can wear and wield."), barEl);

    /* ----- type options follow the class, the level and the realm ----- */
    var typesByRealm = {};
    function typesInRealm(realm) {
      if (typesByRealm[realm]) return typesByRealm[realm];
      var seen = {};
      db.items.forEach(function (it) { if (I.isGear(it) && (!realm || !it.realm || it.realm === realm)) seen[it.type] = (seen[it.type] || 0) + 1; });
      return (typesByRealm[realm] = seen);
    }
    function typeOptions() {
      var seen = typesInRealm(S.realm), c = S.cls ? I.cls(S.cls) : null, ct = c ? I.classTypes(S.cls, S.lvl) : null, t;
      var out = [["", c ? "Anything a level " + S.lvl + " " + c.name + " can use" : "Any gear"]];
      function ok(x) { return seen[x] && (!ct || I.classCanUseType(S.cls, x, S.lvl, 1)); }
      var armor = (I.meta().armorTypes || [32, 33, 34, 37, 35, 38, 36]).filter(ok), weapons = [];
      for (t = 2; t <= 28; t++) if (ok(t)) weapons.push([t, "Weapon: " + I.typeName(t)]);
      weapons.sort(function (a, b) { return a[1].localeCompare(b[1]); });
      if (armor.length > 1) out.push(["armor", "All armor"]);
      armor.forEach(function (x) { out.push([x, "Armor: " + I.typeName(x)]); });
      if (weapons.length > 1) out.push(["weapon", "All weapons"]);
      out = out.concat(weapons);
      if (seen[42] && (!ct || ct.shield > 0)) out.push(["shield", "Shields"]);
      if (seen[45] && (!ct || I.classCanUseType(S.cls, 45, S.lvl))) out.push(["instrument", "Instruments"]);
      if (seen[41]) out.push(["jewelry", "Jewelry and cloaks"]);
      if (S.m !== "up") out.push(["other", "Not gear: loot, potions, arrows, materials ..."]);
      return out;
    }
    function typeStillOffered() { return typeOptions().some(function (o) { return o[0] === S.type; }); }
    if (!typeStillOffered()) S.type = "";

    /* ----- the filter bar ----- */
    var defs = [
      {key: "m", type: "chips", required: true, value: S.m, options: [
        {value: "", label: "Find items", title: "Search everything that drops, is sold, crafted or given by a quest"},
        {value: "up", label: "Upgrades for a slot", title: "Pick a slot and, if you like, the piece you wear now: you get what beats it"}]},
      {key: "cls", label: "Class", type: "select", value: S.cls, hidden: !classes.length, title: "Only what this class can wear and wield at your level; bonuses are weighed for it",
        options: [[0, "Any class"]].concat(classes.map(function (c) { return [c.id, c.name + (c.realm ? " (" + REALM_SHORT[c.realm] + (c.isBase ? ", base class" : "") + ")" : "")]; }))},
      {key: "lvl", label: "My level", type: "number", value: S.lvl, min: 1, max: 50, width: 64, title: "Your character's level: sets the item level range and the caps used in the ranking"},
      {key: "realm", label: "Realm", type: "select", value: S.realm, options: [[0, "Any realm"], [1, "Albion"], [2, "Midgard"], [3, "Hibernia"]]},
      {key: "il", label: "Item level", type: "range", value: S.il, min: 0, max: 51, title: "Level of the item itself. Starts from 5 below to 2 above your level."},
      {key: "type", label: "Type", type: "select", value: S.type, options: typeOptions()},
      {key: "q", label: "Name", type: "text", value: S.q, placeholder: "part of a name", width: 170},
      {key: "slot", label: "Slot", type: "chips", value: S.slot || "", group: " ", options: SLOT_IDS.map(function (s) { return {value: s, label: slotLabel(s)}; })},
      {key: "src", label: "From (none picked = anywhere)", type: "chips", multi: true, value: S.src, group: " ", options: SRC.map(function (s) { return {value: s[0], label: s[1]}; })},
      {key: "focus", label: "Rank by these bonuses (none picked = gear value)", type: "chips", multi: true, value: S.focus,
        options: U.FOCI.map(function (x) { return {value: x.key, label: x.label}; })}
    ];
    f = ui.filters(barEl, defs, onFilter);
    Array.prototype.forEach.call(f.el.querySelectorAll(".wg-filter-group"), function (g) { g.style.margin = "0"; g.style.height = "0"; g.textContent = ""; });
    f.el.appendChild(h("div.wg-filter", {style: {marginLeft: "auto"}}, h("button.wg-btn.small.ghost", {type: "button", title: "Back to the plain search for your character", onclick: function () {
      history.replaceState(null, "", WG.href("items")); window.dispatchEvent(new Event("hashchange"));
    }}, "Reset")));

    function onFilter(v, key) {
      var oldLvl = S.lvl, oldCls = S.cls;
      S.m = v.m === "up" ? "up" : "";
      S.cls = +v.cls || 0;
      S.lvl = v.lvl == null ? oldLvl : clampInt(v.lvl, 1, 50, oldLvl);
      S.realm = +v.realm || 0;
      S.slot = +v.slot || 0;
      S.type = v.type;
      S.il = v.il; S.q = v.q; S.src = v.src; S.focus = v.focus;
      if (key === "cls" && S.cls !== oldCls) { S.realm = defRealm(S.cls) || S.realm; f.set("realm", S.realm); }
      if (key === "lvl" && S.lvl !== oldLvl && sameRange(S.il, defRange(oldLvl))) { S.il = defRange(S.lvl); f.set("il", S.il); }
      if (key === "slot" && S.slot && !typeFits(S.type, S.slot)) S.type = "";
      if (key === "type" && S.slot && !typeFits(S.type, S.slot)) { S.slot = S.m === "up" && S.type !== "other" ? pickSlotFor(S.type) : 0; f.set("slot", S.slot || ""); }
      if (S.m === "up" && S.type === "other") S.type = "";
      if (S.m === "up" && !S.slot) { S.slot = S.type !== "" ? pickSlotFor(S.type) : DEFAULT_SLOT; f.set("slot", S.slot); }
      f.setOptions("type", typeOptions());
      if (!typeStillOffered()) S.type = "";
      f.set("type", S.type);
      var cur = S.cur ? db.get(S.cur) : null;
      if (cur && (S.m !== "up" || slotKey(cur.slot) !== slotKey(S.slot) && !slotIs(cur, S.slot, null))) S.cur = "";
      if (key === "m" || key === "slot" || key === "cls" || key === "realm" || key === "type" || key === "lvl") drawWear();
      apply();
    }
    function pickSlotFor(type) {
      var kind = typeof type === "number" ? I.kind(type) : type;
      if (typeof type === "number" && kind === "weapon") { var n = {}; db.items.forEach(function (it) { if (it.type === type) n[it.slot] = (n[it.slot] || 0) + 1; }); var best = 10; [10, 11, 12, 13].forEach(function (s) { if ((n[s] || 0) > (n[best] || 0)) best = s; }); return best; }
      return kind === "armor" ? DEFAULT_SLOT : kind === "jewelry" ? 35 : kind === "shield" ? 11 : kind === "instrument" ? 12 : 10;
    }

    /* ----- "what I wear now" (upgrade mode) ----- */
    function candidates() {
      var use = usable(), c = S.cls ? I.cls(S.cls) : null;
      return db.items.filter(function (it) { return I.isGear(it) && slotIs(it, S.slot, c) && use(it); });
    }
    function drawWear() {
      WG.clear(wearEl); picker = null;
      if (S.m !== "up") return;
      var cur = S.cur ? db.get(S.cur) : null, box = h("div.wg-card", {style: {display: "flex", gap: "10px", alignItems: "center", flexWrap: "wrap", marginBottom: "12px", borderColor: "var(--accent)"}});
      box.appendChild(h("b", "What you wear now: " + slotLabel(S.slot).toLowerCase()));
      if (cur) {
        box.appendChild(ui.itemLink(cur, {realm: false}));
        box.appendChild(h("span.wg-dim", "level " + cur.level + (I.statText(cur) ? " · " + I.statText(cur) : "") + (cur.bonuses.length ? " · " + I.bonusText(cur) : "")));
        box.appendChild(h("button.wg-btn.small", {type: "button", onclick: function () { S.cur = ""; drawWear(); apply(); if (picker) picker.focus(); }}, "Change"));
        box.appendChild(h("span.wg-dim", "Only pieces that rank above it are listed."));
      } else {
        picker = itemPicker(candidates, function (it) { S.cur = it.id; drawWear(); apply(); });
        box.appendChild(picker.el);
        box.appendChild(h("span.wg-dim", "Optional. Leave it empty and you get every piece for the slot, best first."));
      }
      wearEl.appendChild(box);
    }

    /* ----- filtering and ranking ----- */
    function usable() {
      var cls = S.cls, lvl = S.lvl, realm = S.realm, c = cls ? I.cls(cls) : null, memo = {};
      return function (it) {
        if (realm && it.realm && it.realm !== realm) return false;
        if (!c) return true;
        if (it.classes.length && it.classes.indexOf(cls) < 0) return false;
        if (it.realm && c.realm && it.realm !== c.realm) return false;
        var k = it.type === 42 ? "s" + it.size : it.type, ok = memo[k];
        if (ok === undefined) ok = memo[k] = I.classCanUseType(cls, it.type, lvl, it.size);
        return ok;
      };
    }
    function typeTest() {
      var t = S.type;
      if (t === "" || t === "other") return null;
      if (typeof t === "number") return function (it) { return it.type === t; };
      return function (it) { return I.kindOf(it) === t; };
    }
    function rankOf(it, ctx, match) {
      it._v = U.value(it, ctx);
      it._f = match ? U.focusScore(it, match) : 0;
      return match ? it._f * 100000 + it._v : it._v;
    }
    var lastRows = [], lastCur = null, lastMatch = null;
    function apply() {
      var t0 = performance.now();
      var use = usable(), tt = typeTest(), lo = S.il[0], hi = S.il[1], slot = S.slot, c = S.cls ? I.cls(S.cls) : null;
      var words = S.q.toLowerCase().split(/\s+/).filter(Boolean), nw = words.length;
      var srcMask = 0; S.src.forEach(function (k) { SRC.forEach(function (s) { if (s[0] === k) srcMask |= s[2]; }); });
      var ctx = {level: S.lvl, cls: S.cls}, match = U.focusSet(S.focus, S.cls);
      var cur = S.m === "up" && S.cur ? db.get(S.cur) : null, curRank = cur ? rankOf(cur, ctx, match) : null, curV = cur ? cur._v : 0, curF = cur ? cur._f : 0;
      // what kinds of thing are wanted: gear only, things that are not gear only, or (a name search with nothing else said) both
      var want = S.type === "other" ? "other" : (S.m === "up" || slot || tt || !nw) ? "gear" : "all";
      var levelTouched = !sameRange(S.il, defRange(S.lvl));
      var rows = [], items = db.items, n = items.length;
      for (var i = 0; i < n; i++) {
        var it = items[i], gear = I.isGear(it);
        if (want === "gear" ? !gear : (want === "other" && gear)) continue;
        if (slot && !slotIs(it, slot, c)) continue;
        if (tt && !tt(it)) continue;
        if (gear || levelTouched) { if (lo != null && it.level < lo) continue; if (hi != null && it.level > hi) continue; }
        if (srcMask ? !(it.src & srcMask) : (it.src === 64 && !nw)) continue;      // a quest step item is not an upgrade (items.md section 1)
        if (gear ? !use(it) : (S.realm && it.realm && it.realm !== S.realm)) continue;
        if (nw) { var okw = true; for (var w = 0; w < nw; w++) if (it._lc.indexOf(words[w]) < 0) { okw = false; break; } if (!okw) continue; }
        it._rank = gear ? rankOf(it, ctx, match) : -1;
        if (cur) { if (it === cur || it._rank <= curRank) continue; it._dv = it._v - curV; it._df = it._f - curF; }
        rows.push(it);
      }
      lastRows = rows; lastCur = cur; lastMatch = match;
      drawHead();
      if (!table) makeTable(rows); else { table.setColumns(columns()); table.setRows(rows); }
      WG.setQuery(writeQuery(S));
      listEl.setAttribute("data-ms", Math.round(performance.now() - t0));     // how long the last filter pass took (for the checks)
    }

    function drawHead() {
      WG.clear(headEl);
      var n = lastRows.length, what = S.m === "up" ? (lastCur ? (n === 1 ? "upgrade over " : "upgrades over ") + lastCur.name : (n === 1 ? "piece" : "pieces") + " for " + slotLabel(S.slot).toLowerCase()) : (n === 1 ? "item" : "items");
      headEl.appendChild(h("b", {style: {fontSize: "16px"}}, ui.num(n) + " " + what));
      var sortBox = h("span", {style: {display: "inline-flex", gap: "4px", alignItems: "center", flexWrap: "wrap"}}, h("span.wg-dim", "Sort by"));
      [["value", "desc", "Best first"], ["level", "desc", "Item level"], ["source", "desc", "Drop chance"], ["name", "asc", "Name"]].forEach(function (s) {
        var on = (S.sort || "value") === s[0];
        sortBox.appendChild(ui.chip(s[2], {active: on, onclick: function () { S.sort = s[0]; S.dir = s[1]; if (table) table.sort(s[0], s[1]); WG.setQuery(writeQuery(S)); drawHead(); }}));
      });
      headEl.appendChild(sortBox);
      explainEl.textContent = U.explain({level: S.lvl, cls: S.cls}, S.focus) + " A * after AF or DPS: more than counts at level " + S.lvl + ".";
    }

    /* ----- the result table ----- */
    function sourceCell(it) {
      var b = db.best(it, S.realm), out = [], lead = {display: "inline-block", minWidth: "50px"};
      if (b.drop) {
        var k = b.drop.kind, line = h("div");
        line.appendChild(h("b", {style: lead, title: "Chance that one kill drops it" + (S.realm ? " for a " + WG.REALMS[S.realm] + " character" : "")}, b.drop.always ? "100%" : WG.pct(b.drop.chance)));
        line.appendChild(k ? ui.mobLink(k, {level: true, myLevel: S.lvl}) : h("span", {title: I.whereText(b.drop.where)}, b.drop.key, h("span.wg-dim", " · not a monster you can hunt")));
        if (b.drop.where === "script") line.appendChild(h("span.wg-dim", {title: I.whereText("script", k)}, " · made by a script"));
        var zl = b.drop.zones;
        if (zl.length) { line.appendChild(h("span.wg-dim", " · ")); line.appendChild(ui.zoneLink(zl[0], {realm: !b.drop.near, query: k ? {kind: k.id} : null})); if (zl.length > 1) line.appendChild(h("span.wg-dim", {title: zl.slice(1).map(db.zoneName).join(", ")}, " +" + (zl.length - 1))); }
        if (!b.drop.near) { var zr = db.zone(zl[0]); line.appendChild(h("span.wg-warn", {title: "No monster in your realm drops it"}, " · " + (zr && zr.realm ? WG.REALMS[zr.realm] : "another realm"))); }
        if (b.drop.quest) line.appendChild(h("span.wg-warn", {title: "Falls only while you are on the quest" + (b.drop.quests ? ": " + b.drop.quests.join(", ") : "")}, " · quest"));
        out.push(line);
        if (b.drops > 1) out.push(h("div.wg-dim.wg-small", {style: {paddingLeft: "50px"}}, "and " + (b.drops - 1) + " more " + (b.drops === 2 ? "monster" : "monsters")));
      } else if (it.src & 2) {
        var o = db.otd(it.id)[0];
        if (o) out.push(h("div", h("b", {style: lead}, "Once"), o.kind ? ui.mobLink(o.kind, {level: true, myLevel: S.lvl}) : o.key, h("span.wg-dim", " · first kill at level " + o.minLevel + " or higher")));
      }
      if (b.vendor) {
        var v = b.vendor.v, vl = h("div", h("span", {style: lead}, "Buy"), ui.vendorLink(v), h("span.wg-dim", " · "), I.priceEl(b.vendor));
        if (v.zone != null) { vl.appendChild(h("span.wg-dim", " · ")); vl.appendChild(ui.zoneLink(v.zone, {realm: false})); }
        if (!b.vendor.usable) vl.appendChild(h("span.wg-warn", " · trades with " + WG.REALMS[v.realm] + " only"));
        else if (!b.vendor.own) vl.appendChild(h("span.wg-dim", " · in " + WG.REALMS[v.realm]));
        out.push(vl);
        if (b.vendors > 1) out.push(h("div.wg-dim.wg-small", {style: {paddingLeft: "50px"}}, "and " + (b.vendors - 1) + " more " + (b.vendors === 2 ? "merchant" : "merchants")));
      }
      if (it.src & 8) {
        var qs = (D.quests && D.quests[it.id] || []).filter(function (q) { return q[3] !== "step"; }), q0 = qs[0];
        out.push(h("div", h("span", {style: lead}, "Quest"), q0 ? (q0[0] || "reward") + (q0[2] ? " (" + q0[2] + (q0[1] ? ", level " + q0[1] : "") + ")" : "") : "reward", qs.length > 1 ? h("span.wg-dim", " +" + (qs.length - 1)) : null));
      }
      if (it.src & 16) {
        var cr = D.craft && D.craft[it.id];
        out.push(h("div", h("span", {style: lead}, "Craft"), cr ? (craftSkills[cr[0]] || "Crafting") + " " + cr[1] : "by a crafter"));
      }
      if (it.src & 32) out.push(h("div", h("span", {style: lead}, "Start"), "starting equipment"));
      if (!out.length && (it.src & 64)) out.push(h("span.wg-dim", "Quest step item"));
      if (!out.length) out.push(h("span.wg-dim", (it.src & 129) ? "On a loot table that cannot fall for this realm, or whose monster is not in the world" : "No source"));
      return out;
    }
    function columns() {
      var cur = lastCur, match = lastMatch;
      return [
        {key: "name", label: "Item", render: function (it) { return ui.itemLink(it, {realm: !S.realm}); }, sort: function (it) { return it._lc; }},
        {key: "level", label: "Lvl", num: true, width: 46, title: "Item level"},
        {key: "type", label: "Type", cls: "wg-nowrap", render: function (it) { return I.typeSlot(it); }, sort: function (it) { return I.typeSlot(it); }},
        {key: "stats", label: "AF / DPS", cls: "wg-nowrap", title: "Armor factor and absorption, or damage per second and speed",
          render: function (it) { return statCell(it); }, sort: function (it) { return I.statText(it) ? it.dpsaf : null; }, first: "desc"},
        {key: "quality", label: "Qual", num: true, width: 52, title: "Quality", render: function (it) { return it.quality && I.isGear(it) ? it.quality + "%" : ""; }},
        {key: "bonuses", label: "Bonuses", sort: false, render: function (it) { return bonusCell(it, match); }},
        {key: "value", label: cur ? "Better by" : (match ? "Picked bonuses" : "Gear value"), num: true, width: 92,
          title: cur ? "How far it ranks above what you wear" : "The number the list is ranked by (explained above the table)",
          sort: function (it) { return it._rank < 0 ? null : it._rank; },
          render: function (it) {
            if (it._rank < 0) return "";
            if (match) { var fv = cur ? it._df : it._f; return h("span", {title: "Gear value " + it._v}, (cur && fv > 0 ? "+" : "") + fmt(fv) + (cur && !fv ? " (value +" + it._dv + ")" : "")); }
            return cur ? "+" + it._dv : String(it._v);
          }},
        {key: "source", label: "Best way to get it", title: "Sorts by the chance per kill", sort: function (it) { return db.best(it, S.realm).chance; }, first: "desc", render: sourceCell}
      ];
    }
    function fmt(n) { return (Math.round(n * 100) / 100).toString(); }
    function statCell(it) {
      var t = I.statText(it); if (!t) return "";
      var cap = U.dpsafCap(S.lvl, it.type);
      if (it.dpsaf > cap) return h("span", {title: "At level " + S.lvl + " it counts only up to " + (it.type >= 31 ? "AF " + cap : (cap / 10).toFixed(1) + " dps")}, t, h("span.wg-dim", " *"));
      return t;
    }
    function bonusCell(it, match) {
      var fx = (it.procs || it.charges) ? I.effects(it) : [];
      if (!it.bonuses.length && !fx.length) return "";
      var out = [];
      it.bonuses.forEach(function (b, i) {
        if (i) out.push(", ");
        var t = I.bonusShort(b[0], b[1]);
        out.push(match && match(b[0]) ? h("b", {style: {color: "var(--accent)"}}, t) : t);
      });
      fx.forEach(function (p) { out.push(h("span", {style: {color: "#c9a6ff"}, title: p.text || ""}, (out.length ? ", " : "") + (p.kind === "Proc" ? "proc: " : "charge: ") + p.name)); });
      return h("span", out);
    }
    function makeTable(rows) {
      table = ui.table(listEl, {
        columns: columns(), rows: rows, pageSize: 50, count: "items",
        sortKey: S.sort || "value", sortDir: S.dir || (S.sort === "name" ? "asc" : "desc"),
        rowHref: function (it) { return I.href(it); },
        onSort: function (key, dir) {
          S.sort = key; S.dir = dir; WG.setQuery(writeQuery(S)); drawHead();
        },
        empty: " "
      });
      // the empty text depends on the filters: write it whenever the list is empty
      var setRows = table.setRows;
      function fixEmpty() { var td = listEl.querySelector("tr.wg-table-empty td"); if (td) { WG.clear(td); WG.append(td, emptyBlock()); } }
      table.setRows = function (r) { setRows(r); if (!r.length) fixEmpty(); };
      if (!rows.length) fixEmpty();
    }
    /* An empty list says what to change, and each suggestion is a button that does it. */
    function fix(key, value) { f.set(key, value); onFilter(f.values(), key); }
    function emptyBlock() {
      var tips = [];
      function tip(text, label, fn) { tips.push(h("li", {style: {margin: "5px 0"}}, h("button.wg-btn.small", {type: "button", onclick: fn}, label), " ", h("span.wg-dim", text))); }
      if (S.q) tip("the name \"" + S.q + "\" matches nothing with these filters", "Clear the name", function () { fix("q", ""); });
      if (S.il[0] != null || S.il[1] != null) tip(S.m === "up" && S.cur ? "nothing from level " + (S.il[0] == null ? "any" : S.il[0]) + " to " + (S.il[1] == null ? "any" : S.il[1]) + " ranks above what you wear" : "now " + (S.il[0] == null ? "any" : S.il[0]) + " to " + (S.il[1] == null ? "any" : S.il[1]),
        "Any item level", function () { fix("il", [null, null]); });
      if (S.src.length) tip("drops, merchants, quests and crafting", "Any source", function () { fix("src", []); });
      if (S.type !== "") tip("the type is narrowed", "Any type", function () { fix("type", ""); });
      if (S.slot === 11 && S.cls && I.cls(S.cls) && !I.cls(S.cls).leftHand && !(I.classTypes(S.cls, S.lvl) || {}).shield) tips.push(h("li.wg-dim", {style: {margin: "5px 0"}}, (/^[aeiou]/i.test(I.className(S.cls)) ? "An " : "A ") + I.className(S.cls) + " holds nothing in the left hand at level " + S.lvl + "."));
      if (S.slot && S.m !== "up") tip("the slot is " + slotLabel(S.slot).toLowerCase(), "Any slot", function () { fix("slot", ""); });
      if (S.cls) tip("only what a level " + S.lvl + " " + I.className(S.cls) + " can use is listed", "Any class", function () { fix("cls", 0); });
      if (!tips.length) tips.push(h("li.wg-dim", "Press Reset."));
      return h("div", {style: {padding: "18px 8px", textAlign: "left", maxWidth: "680px", margin: "0 auto"}}, h("b", S.m === "up" && S.cur ? "Nothing here beats what you wear." : "No items match."),
        h("ul", {style: {margin: "8px 0 0", paddingLeft: "0", listStyle: "none"}}, tips));
    }

    drawWear();
    apply();
  }

  /* ---------- a small type-ahead to pick one item ---------- */
  function itemPicker(getItems, onPick) {
    var ui = WG.ui, input = h("input", {type: "search", placeholder: "type part of its name", "aria-label": "The item you wear now", spellcheck: false, autocomplete: "off", style: {width: "260px"}});
    var pop = h("div", {style: {position: "absolute", left: "0", top: "calc(100% + 4px)", zIndex: "40", minWidth: "440px", maxHeight: "360px", overflowY: "auto", background: "var(--bg2)", border: "1px solid var(--line)", borderRadius: "6px", boxShadow: "0 8px 24px rgba(0,0,0,.5)", display: "none"}});
    var el = h("span", {style: {position: "relative", display: "inline-block"}}, input, pop), list = [], sel = 0, pool = null;
    function close() { pop.style.display = "none"; }
    function paint() {
      WG.clear(pop);
      if (!list.length) { pop.appendChild(h("div.wg-dim", {style: {padding: "8px 10px"}}, input.value.trim() ? "No piece for this slot has that in its name." : "Type a few letters.")); return; }
      list.forEach(function (it, i) {
        pop.appendChild(h("div", {style: {padding: "5px 10px", cursor: "pointer", background: i === sel ? "var(--bg3)" : "", display: "flex", gap: "10px", justifyContent: "space-between"},
          onmousedown: function (e) { e.preventDefault(); choose(i); }, onmousemove: function () { if (sel !== i) { sel = i; paint(); } }},
          h("span", I.icon(it, 18), " ", it.name), h("span.wg-dim", {style: {whiteSpace: "nowrap"}}, "level " + it.level + (I.statText(it) ? " · " + I.statText(it) : ""))));
      });
    }
    function search() {
      var words = input.value.toLowerCase().split(/\s+/).filter(Boolean);
      pool = pool || getItems();
      if (!words.length) { list = []; paint(); pop.style.display = ""; return; }
      var my = WG.me.level || 1, hits = [];
      for (var i = 0; i < pool.length; i++) {
        var it = pool[i], ok = true;
        for (var w = 0; w < words.length; w++) if (it._lc.indexOf(words[w]) < 0) { ok = false; break; }
        if (ok) hits.push(it);
      }
      hits.sort(function (a, b) {
        var sa = a._lc.indexOf(words[0]) === 0 ? 0 : 1, sb = b._lc.indexOf(words[0]) === 0 ? 0 : 1;
        return (sa - sb) || (Math.abs(a.level - my) - Math.abs(b.level - my)) || a._lc.localeCompare(b._lc);
      });
      list = hits.slice(0, 14); sel = 0; paint(); pop.style.display = "";
    }
    function choose(i) { var it = list[i]; if (!it) return; close(); onPick(it); }
    input.addEventListener("input", ui.debounce(search, 90));
    input.addEventListener("focus", function () { if (input.value.trim()) search(); });
    input.addEventListener("blur", close);
    input.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown") { e.preventDefault(); if (pop.style.display === "none") search(); else { sel = Math.min(list.length - 1, sel + 1); paint(); } }
      else if (e.key === "ArrowUp") { e.preventDefault(); sel = Math.max(0, sel - 1); paint(); }
      else if (e.key === "Enter") { e.preventDefault(); choose(sel); }
      else if (e.key === "Escape") close();
    });
    return {el: el, focus: function () { input.focus(); }};
  }
})();
