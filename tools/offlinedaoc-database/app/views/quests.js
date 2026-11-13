/* OFFLINEDAOC Database - quest finder (#/quests), lane Q.
   Opens on "quests my character can take now" (level, realm and class of the header box). Every filter is in the
   address: lv, any, realm, cls, min (quest level range), zone, kind, rw (reward kind), slot, use, rep, all, q, sort. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, Q = WG.quest;
  var live = null;
  var RW = [["", "Any reward"], ["item", "Gives an item"], ["weapon", "A weapon"], ["armor", "Armor"], ["shield", "A shield"],
    ["jewelry", "Jewelry"], ["cloak", "A cloak"], ["instrument", "An instrument"], ["coin", "Coin"]];

  function classesOf(realm) {
    var ui = WG.ui, list = ui.classes ? ui.classes() : [], used = {};
    Q.all().forEach(function (q) { q.cls.forEach(function (c) { used[c] = 1; }); });
    /* every class of the realm can take the quests without a class limit, so list them all (not the disabled ones) */
    var off = {33: 1, 34: 1, 39: 1, 58: 1, 59: 1, 60: 1, 61: 1, 62: 1, 63: 1};
    return list.filter(function (c) { return !off[c.id] && (!realm || c.realm === realm); });
  }

  function defs(query) {
    var me = WG.me, ui = WG.ui;
    var realm = query.realm != null ? (query.realm === "-" ? 0 : +query.realm) : (me.realm || 0);
    var zones = new Map();
    Q.all().forEach(function (q) { if (q.giver) zones.set(q.giver.zone, (zones.get(q.giver.zone) || 0) + 1); });
    var zopts = [];
    zones.forEach(function (n, z) { zopts.push([z, Q.zoneName(z)]); });
    zopts.sort(function (a, b) { return a[1].localeCompare(b[1]); });
    var copts = [["-", "Any class"]].concat(classesOf(realm).map(function (c) { return [c.id, c.name + (realm ? "" : " (" + WG.REALMS[c.realm].slice(0, 3) + ")")]; }));
    return ui.filters.fromQuery([
      {key: "q", label: "Quest or giver", type: "text", value: "", placeholder: "part of a name", width: 200},
      {key: "lv", label: "My level", type: "number", value: me.level || 1, def: me.level || 1, min: 1, max: 50, title: "Shows the quests a character of this level can take"},
      {key: "any", label: "Any level", type: "toggle", value: false, title: "Ignore my level: list the quests of every level"},
      {key: "realm", label: "Realm", type: "select", value: me.realm || "-", def: me.realm || "-", options: [["-", "Any realm"], [1, "Albion"], [2, "Midgard"], [3, "Hibernia"]]},
      {key: "cls", label: "Class", type: "select", value: me.cls || "-", def: me.cls || "-", options: copts, width: 150, title: "Quests this class may take"},
      {key: "min", label: "Quest level", type: "range", value: [null, null], min: 1, max: 50, title: "The level a quest opens at"},
      {key: "zone", label: "Giver's zone", type: "select", value: "", options: [["", "Any zone"]].concat(zopts), width: 180},
      {key: "kind", label: "Kind", type: "chips", multi: true, value: [], options: Q.KINDS.map(function (k) { return {value: k[0], label: k[1]}; })},
      {key: "rw", label: "Reward", type: "select", value: "", options: RW, title: "What the quest pays. With a class picked, class versions of a reward count as that class's item."},
      {key: "use", label: "My class can use it", type: "toggle", value: false, title: "Only quests with a reward item this class can wear or wield at my level"},
      {key: "rep", label: "Repeatable", type: "toggle", value: false, title: "Quests and turn-ins that can be done more than once"},
      {key: "all", label: "Show every class version", type: "toggle", value: false, title: "Many quests exist once per class. Off: one row per quest."}
    ], query);
  }

  function prep(q, cls, level) {
    var items = Q.rewardItems(q, cls).map(function (r) { var it = Q.item(r.id); return {r: r, it: it}; });
    return items;
  }
  function catOf(it) { return it.cat || (it.stub ? "" : "other"); }

  function apply(v) {
    var text = (v.q || "").trim().toLowerCase(), any = v.any, level = any ? 0 : (v.lv || 0);
    var realm = v.realm === "-" || v.realm === "" ? 0 : +v.realm, cls = v.cls === "-" || v.cls === "" ? 0 : +v.cls;
    var lo = v.min[0], hi = v.min[1], kinds = v.kind || [], zone = v.zone === "" ? null : +v.zone;
    var out = [], seen = new Map();
    Q.all().forEach(function (q) {
      if (level && (level < q.lmin || level > q.lmax)) return;
      if (realm && q.realm && q.realm !== realm) return;
      if (cls && q.cls.length && q.cls.indexOf(cls) < 0) return;
      if (lo != null && q.lmin < lo) return;
      if (hi != null && q.lmin > hi) return;
      if (zone != null && !(q.giver && q.giver.zone === zone)) return;
      if (kinds.length && kinds.indexOf(q.kind) < 0) return;
      if (v.rep && !(q.rep === 0 || q.rep > 1)) return;
      if (text && q._lc.indexOf(text) < 0 && !(q.giver && q.giver.name.toLowerCase().indexOf(text) >= 0)) return;
      var items = prep(q, cls, level);
      if (v.rw === "coin") { if (!(q.coin > 0)) return; }
      else if (v.rw || v.use) {
        var ok = items.some(function (x) {
          if (v.rw && v.rw !== "item" && catOf(x.it) !== v.rw) return false;
          if (v.use && cls && WG.item && WG.item.usableBy && !x.it.stub && !WG.item.usableBy(x.it, cls, level || undefined)) return false;
          if (v.use && !(x.it.slot >= 10 && x.it.slot <= 37)) return false;
          return true;
        });
        if (!ok) return;
      }
      var row = {q: q, items: items, xp: Q.xpAt(q, level || WG.me.level || 1), n: 1};
      if (!v.all && q.grp != null && q.src === "data") {
        var e = seen.get(q.grp);
        if (e) { e.n++; e.cls = e.cls.concat(q.cls); if (q.giver && e.givers.indexOf(q.giver.name) < 0) e.givers.push(q.giver.name); return; }
        row.cls = q.cls.slice(); row.givers = q.giver ? [q.giver.name] : []; seen.set(q.grp, row);
      }
      out.push(row);
    });
    return out;
  }

  function columns(level, cls) {
    var ui = WG.ui;
    return [
      {key: "name", label: "Quest", sort: function (r) { return r.q._lc; }, render: function (r) { return Q.link(r.q); }},
      {key: "lmin", label: "Level", num: true, width: 84, first: "desc", sort: function (r) { return r.q.lmin; }, title: "The levels at which it can be taken. Click to sort by the lowest.",
        render: function (r) { return h("span.wg-nowrap", Q.levelText(r.q)); }},
      {key: "cls", label: "Class", width: 150, sort: function (r) { return r.q.cls.length ? Q.classText(r.q) : ""; },
        render: function (r) {
          if (r.n > 1) { var names = r.cls.map(Q.className); return h("span.wg-dim", {title: names.join(", ")}, r.cls.length ? r.cls.length + " classes" : r.n + " versions"); }
          return h("span" + (r.q.cls.length ? "" : ".wg-dim"), Q.classText(r.q, 2));
        }},
      {key: "giver", label: "Giver", sort: function (r) { return r.q.giver ? r.q.giver.name : ""; },
        render: function (r) {
          if (!r.q.giver) return h("span.wg-dim", "a courier");
          if (r.givers && r.givers.length > 1) return h("span", {title: r.givers.join(", ")}, r.q.giver.name, h("span.wg-dim", " and " + (r.givers.length - 1) + " more, one per class"));
          return r.q.giver.name;
        }},
      {key: "zone", label: "Where", sort: function (r) { return r.q.giver ? Q.zoneName(r.q.giver.zone) : ""; },
        render: function (r) { return r.q.giver ? Q.zoneEl(r.q.giver.zone, [r.q.giver.x, r.q.giver.y]) : ""; }},
      {key: "steps", label: "Steps", num: true, width: 96, sort: function (r) { return r.q.steps.length; }, title: "Number of steps, and how many of them are kills",
        render: function (r) {
          if (r.q.turnin) return h("span.wg-dim", "turn-in");
          var kills = r.q.steps.filter(function (s) { return s.t === "kill"; }).length;
          return [String(r.q.steps.length), kills ? h("span.wg-dim", " (" + kills + (kills === 1 ? " kill)" : " kills)")) : null];
        }},
      {key: "reward", label: "Reward", sort: function (r) { return r.items.length ? r.items[0].it.name : ""; },
        render: function (r) {
          var out = [], n = 0, seen = {};
          r.items.forEach(function (x) {
            if (seen[x.it.id]) return; seen[x.it.id] = 1;
            if (n < 2) { if (n) out.push(h("span.wg-dim", x.r.how === "choice" ? " or " : ", ")); out.push(Q.itemEl(x.it.id)); }
            n++;
          });
          if (n > 2) out.push(h("span.wg-dim", " +" + (n - 2)));
          if (r.q.coin) { if (out.length) out.push(h("span.wg-dim", ", ")); out.push(ui.money ? ui.money(r.q.coin) : WG.money(r.q.coin)); }
          return out.length ? h("span", out) : h("span.wg-dim", r.xp ? "experience" : "none");
        }},
      {key: "xp", label: level ? "XP at " + level : "XP", num: true, width: 110, sort: function (r) { return r.xp; },
        title: "Experience the whole quest pays a character of this level (per item for a turn-in). The server's own numbers.",
        render: function (r) { return r.xp ? [ui.num(r.xp), r.q.turnin ? h("span.wg-dim", " each") : null] : ""; }}
    ];
  }

  function render(el, params, query) {
    var me = (live = {});
    var ui = WG.ui, body = h("div"); el.appendChild(body);
    ui.loading(body, "Loading the quests...");
    return Q.full().then(function () {
      if (live !== me) return;
      WG.clear(body);
      Q.css();
      var meta = Q.meta();
      ui.page(body, {title: "Quests", sub: ui.num(meta.counts.total) + " quests a True 1.65 character can take, with their steps, kill hand-overs and rewards. Opens on what your character can take now."});
      var bar = h("div"), count = h("div.wg-dim", {style: {margin: "8px 2px"}}), tbox = h("div");
      WG.append(body, [bar, count, tbox]);
      var d = defs(query), sort = (query.sort || "lmin.desc").split(".");
      var f = ui.filters(bar, d, function (v, key) {
        if (key === "realm") {          // the class list follows the realm
          var realm = v.realm === "-" ? 0 : +v.realm, list = classesOf(realm);
          f.setOptions("cls", [["-", "Any class"]].concat(list.map(function (c) { return [c.id, c.name + (realm ? "" : " (" + WG.REALMS[c.realm].slice(0, 3) + ")")]; })));
          if (v.cls !== "-" && !list.some(function (c) { return c.id === +v.cls; })) f.set("cls", "-");
        }
        run();
      });
      var table = ui.table(tbox, {
        columns: columns(0, 0), rows: [], sortKey: sort[0], sortDir: sort[1] || "desc", pageSize: 60, count: "quests",
        empty: "No quest matches. Try \"Any level\", another class, or clear the reward filter.",
        rowHref: function (r) { return Q.href(r.q); },
        onSort: function () { address(); }
      });
      function address() {
        var st = table.state(), q = Object.assign({}, f.query());
        if (st.sortKey && (st.sortKey + "." + st.sortDir) !== "lmin.desc") q.sort = st.sortKey + "." + st.sortDir;
        WG.setQuery(q);
      }
      function run() {
        var v = f.values(), level = v.any ? 0 : (v.lv || 0), cls = v.cls === "-" ? 0 : +v.cls, realm = v.realm === "-" ? 0 : +v.realm;
        var rows = apply(v);
        table.setColumns(columns(level, cls));
        table.setRows(rows);
        WG.clear(count);
        var who = (level ? "a level " + level : "any level") + (realm ? " " + WG.REALMS[realm] : "") + " " + (cls ? Q.className(cls) : "character");
        WG.append(count, [h("b", ui.num(rows.length) + (rows.length === 1 ? " quest" : " quests")), " for " + who,
          v.all ? "" : ", one row per quest", ". ",
          level && rows.length ? "Highest quest level first: the ones nearest your level are on top." : " "]);
        if (!rows.length && (v.q || "").trim()) {
          /* nothing for this character: say how many match when level, realm and class are left open */
          var wide = Object.assign({}, v, {any: true, realm: "-", cls: "-"}), n = apply(wide).length;
          if (n) count.appendChild(h("a", {href: WG.href("quests", Object.assign({}, f.query(), {any: 1, realm: "-", cls: "-"}))},
            n + (n === 1 ? " quest matches" : " quests match") + " \"" + v.q.trim() + "\" for other levels, realms or classes: show"));
        }
        address();
      }
      me.again = function () { WG.clear(el); render(el, params, WG.route().query); };
      run();
      body.appendChild(h("div.wg-dim.wg-small", {style: {marginTop: "10px"}},
        "Not in True 1.65, so not listed: " + (meta.rules.off || ""), " ",
        h("a", {href: WG.href("about")}, "How the numbers are computed")));
    });
  }

  WG.onMe(function () { if (live && live.again && WG.route().view === "quests") { try { live.again(); } catch (e) { console.error(e); } } });
  WG.view("quests", {title: "Quests", nav: true, order: 30, render: render, leave: function () { live = null; }});
})();
