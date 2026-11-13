/* OFFLINEDAOC Database - quest lookups and the "Quests" sections other pages embed (lane Q).
   WG.quest.*   signatures: source/contract/quests.md.   Data: data/quests.js (source/build_quests.py).

   One line for another page:
     WG.quest.itemSection(el, itemId)        quests that reward the item, need it, or hand it over on a kill
     WG.quest.mobSection(el, kindKey)        quests that send you to kill the monster, and what it hands over
     WG.quest.zoneSection(el, zoneId)        quests given in the zone and quests with a step there
     WG.quest.npcSection(el, name, zoneId)   quests the NPC gives or takes part in
   Each loads the data itself, appends nothing when there is nothing to say, and returns a Promise of the count. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h;
  var Q = (WG.quest = WG.quest || {});
  var S = null, readyP = null;

  function ui() { return WG.ui || {}; }
  function low(s) { return String(s == null ? "" : s).toLowerCase(); }
  function push(map, key, v) { var a = map.get(key); if (!a) map.set(key, (a = [])); a.push(v); return a; }

  /* ---------- loading and indexes ---------- */
  function build(d) {
    if (S) return S;
    var s = {d: d, list: d.quests, byId: new Map(), item: new Map(), mob: new Map(), npc: new Map(), zone: new Map(), cr: d.classRewards || {}};
    function it(id) { var k = low(id), e = s.item.get(k); if (!e) s.item.set(k, (e = {rewards: [], needs: [], hands: []})); return e; }
    function zn(z) { var e = s.zone.get(z); if (!e) s.zone.set(z, (e = {gives: [], steps: new Map()})); return e; }
    function np(name) { var k = low(name), e = s.npc.get(k); if (!e) s.npc.set(k, (e = {gives: [], steps: []})); return e; }
    s.list.forEach(function (q) {
      s.byId.set(String(q.id), q);
      q._lc = low(q.name);
      var seenR = {}, seenN = {};
      function reward(id, how, step, cls) { var k = low(id) + "|" + how + "|" + (cls || ""); if (seenR[k]) return; seenR[k] = 1; it(id).rewards.push({q: q, how: how, step: step || 0, cls: cls || 0}); }
      function need(id, step, role) { if (!id) return; var k = low(id) + "|" + step + "|" + role; if (seenN[k]) return; seenN[k] = 1; it(id).needs.push({q: q, step: step, role: role}); }
      (q.rw || []).forEach(function (r) {
        reward(r[0], r[1], r[2]);
        var by = s.cr[r[0]];
        if (by) Object.keys(by).forEach(function (c) { if (!q.cls.length || q.cls.indexOf(+c) >= 0) reward(by[c], r[1], r[2], +c); });
      });
      if (q.rwClass) Object.keys(q.rwClass).forEach(function (c) { q.rwClass[c].forEach(function (id) { reward(id, "final", 0, +c); }); });
      if (q.turnin) {
        need(q.turnin.item, 0, "turn-in");
        if (q.turnin.roll) (q.turnin.roll.items || []).forEach(function (id) { reward(id, "random", 0); });
      }
      (q.side || []).forEach(function (e) { need(e.item, e.fromStep, "side turn-in"); });
      (q.hand || []).forEach(function (hd) {
        it(hd[2]).hands.push({q: q, step: hd[0], mob: hd[1], chance: hd[3], how: hd[4]});
        push(s.mob, hd[1], {q: q, step: hd[0], item: hd[2], chance: hd[3], how: hd[4]});
      });
      if (q.giver) { zn(q.giver.zone).gives.push(q); np(q.giver.name).gives.push(q); }
      (q.givers || []).forEach(function (g, i) { if (i) { zn(g.zone).gives.push(q); np(g.name).gives.push(q); } });
      (q.steps || []).forEach(function (st) {
        var t = st.target;
        if (st.t === "kill" && t) {
          (t.keys || [t.key]).forEach(function (k) { if (k) push(s.mob, k, {q: q, step: st.n, kill: 1}); });
          (st.alt || []).forEach(function (k) { push(s.mob, k, {q: q, step: st.n, kill: 1}); });
        } else if (t && t.name && t.k !== "trainer") np(t.name).steps.push({q: q, n: st.n, t: st.t});
        if (st.t === "die_to" && t && t.key) push(s.mob, t.key, {q: q, step: st.n, die: 1});
        var z = st.at ? st.at[0] : (t && t.zone != null ? t.zone : null);
        if (z != null) { var m = zn(z).steps, a = m.get(q); if (!a) m.set(q, (a = [])); a.push(st.n); }
        if (st.col) need(st.col, st.n, "hand in");
        else if (st.t === "deliver" && st.item) need(st.item, st.n, "deliver");
        if (st.use) need(st.use, st.n, st.t === "has_item" ? "have" : "use");
        (st.trades || []).forEach(function (tr) { need(tr[0], st.n, "hand in"); });
        (st.takes || []).forEach(function (id) { need(id, st.n, "hand in"); });
        (st.wear || []).forEach(function (id) { need(id, st.n, "wear"); });
      });
    });
    S = s;
    return s;
  }
  /* WG.quest.ready() -> Promise: loads data/quests.js and builds the indexes (once). */
  Q.ready = function () {
    if (!readyP) readyP = WG.load("quests").then(function (d) { build(d.quests); return Q; });
    return readyP;
  };
  function opt(name) { return WG.load(name).then(function (d) { return d[name]; }, function () { return null; }); }
  /* Everything the quest pages use; a missing file of another lane is tolerated. */
  Q.full = function () { return Promise.all([Q.ready(), opt("itemmeta"), opt("zones"), opt("mobs"), opt("items")]).then(function () { return Q; }); };
  Q.loaded = function () { return !!S; };
  Q.all = function () { return S ? S.list : []; };
  Q.get = function (id) { return S ? S.byId.get(String(id)) || null : null; };
  Q.meta = function () { return S ? S.d.meta : {}; };
  Q.href = function (q) { return WG.href("quest/" + encodeURIComponent(typeof q === "object" ? q.id : q)); };

  /* ---------- names of other lanes' things ---------- */
  var itemIdx = null, kindIdx = null;
  Q.item = function (id) {
    var t = WG.data.items;
    if (t && !itemIdx) { itemIdx = new Map(); WG.rows(t.items || t).forEach(function (r) { itemIdx.set(low(r.id), r); }); }
    var r = itemIdx && itemIdx.get(low(id));
    return r || {id: id, name: (S && S.d.names[id]) || String(id), stub: 1};
  };
  Q.itemName = function (id) { return Q.item(id).name; };
  Q.itemEl = function (id, opts) { var u = ui(), it = Q.item(id); return u.itemLink ? u.itemLink(it, opts) : h("a", {href: WG.href("item/" + encodeURIComponent(id))}, it.name); };
  Q.kind = function (key) {
    var t = WG.data.mobs;
    if (t && !kindIdx) { kindIdx = new Map(); WG.rows(t.kinds).forEach(function (r) { kindIdx.set(r.key, r); }); }
    return (kindIdx && kindIdx.get(key)) || null;
  };
  /* A monster by its key: a link with its level range when it is a kind of mobs.js, plain text otherwise. */
  Q.mobEl = function (key, name, opts) {
    var k = Q.kind(key), u = ui();
    if (k && u.mobLink) return u.mobLink(k, {level: !(opts && opts.level === false)});
    return h("span", {title: k ? "" : "Not a monster of the hunting data (a quest's own monster, or a townsperson)"}, name || key);
  };
  Q.zoneName = function (id) { var z = WG.data.zones, r = null; if (z) { r = WG.index(z.zones, "id").get(id); } return r ? r.name : "zone " + id; };
  Q.zoneEl = function (id, loc) { var u = ui(); return u.zoneLink ? u.zoneLink(id, loc ? {loc: loc} : undefined) : h("span", Q.zoneName(id)); };
  Q.className = function (id) { var u = ui(), n = u.className ? u.className(id) : ""; return n || (WG.item && WG.item.className ? WG.item.className(id) : "class " + id); };

  /* ---------- facts about one quest ---------- */
  var KIND = {"class": "Class and guild", epic: "Epic", chain: "Quest chain", town: "Town", killtask: "Kill task"};
  Q.KINDS = [["class", "Class and guild"], ["epic", "Epic"], ["chain", "Chain"], ["town", "Town"], ["killtask", "Kill task"]];
  Q.kindName = function (k) { return KIND[k] || k; };
  var STEP = {kill: "Kill", deliver: "Deliver", talk: "Talk", whisper: "Say", search: "Search", collect: "Bring", use_item: "Use item",
    choose: "Choose reward", trade: "Hand in", quest: "Finish quests", travel: "Go to", has_item: "Get item", say: "Speak aloud",
    group: "Join a group", die_to: "Be slain", other: "Do"};
  Q.stepName = function (t) { return STEP[t] || t; };
  Q.levelText = function (q) { return q.lmin === q.lmax ? String(q.lmin) : q.lmin + " to " + q.lmax; };
  Q.classText = function (q, max) {
    if (!q.cls.length) return "Any class";
    if (q.cls.length > (max || 3)) return q.cls.length + " classes";
    return q.cls.map(Q.className).join(", ");
  };
  /* May a character {level, realm, cls} take it now? Level, realm and class only: what he has finished is not known. */
  Q.canTake = function (q, me) {
    me = me || WG.me;
    if (me.level && (me.level < q.lmin || me.level > q.lmax)) return false;
    if (me.realm && q.realm && me.realm !== q.realm) return false;
    if (me.cls && q.cls.length && q.cls.indexOf(+me.cls) < 0) return false;
    return true;
  };
  /* Experience of one level: XPForLevel[level] - XPForLevel[level - 1] (GamePlayer.cs); 0 at level 50. */
  Q.levelSpan = function (level) { var t = Q.meta().xpForLevel || []; return level >= 1 && level < 50 ? t[level] - t[level - 1] : 0; };
  /* Experience the whole quest pays a character of that level (ClassicQuests.ScaleXP per step). For a turn-in: per item. */
  Q.xpAt = function (q, level) {
    level = level || WG.me.level || 1;
    var span = Q.levelSpan(level), t = Q.meta().xpForLevel || [];
    if (q.turnin) return q.turnin.xp > 0 ? q.turnin.xp : Math.floor((t[Math.max(0, level - 1)] || 0) / 50);
    if (q.src === "script") return q.xp || 0;
    var sum = 0;
    (q.steps || []).forEach(function (s) { var fixed = s.xp || 0; if (fixed > 0) sum += Math.max(fixed, s.share && span ? Math.floor(span * s.share) : 0); });
    return sum;
  };
  Q.stepXp = function (s, level) { var span = Q.levelSpan(level || WG.me.level || 1), fixed = s.xp || 0; return fixed > 0 ? Math.max(fixed, s.share && span ? Math.floor(span * s.share) : 0) : 0; };
  /* Reward items for a class (0 = show the plain reward): [{id, how, step, own}] with class versions resolved. */
  Q.rewardItems = function (q, cls) {
    var out = [], cr = S ? S.cr : {};
    (q.rw || []).forEach(function (r) {
      var by = cr[r[0]], id = r[0], own = false;
      if (by && cls && by[cls]) { id = by[cls]; own = true; }
      out.push({id: id, how: r[1], step: r[2], own: own, versions: by || null});
    });
    if (q.rwClass) {
      var keys = cls && q.rwClass[cls] ? [String(cls)] : Object.keys(q.rwClass);
      keys.forEach(function (c) { q.rwClass[c].forEach(function (id) { out.push({id: id, how: "final", step: 0, own: true, cls: +c}); }); });
    }
    if (q.turnin && q.turnin.roll) (q.turnin.roll.items || []).forEach(function (id) { out.push({id: id, how: "random", step: 0}); });
    return out;
  };
  Q.chanceText = function (c) { return c >= 0.9995 ? "every kill" : WG.pct(c) + " a kill"; };

  /* ---------- lookups ---------- */
  var NONE = {rewards: [], needs: [], hands: [], total: 0};
  /* WG.quest.forItem(itemId) -> {rewards: [{q, how, step, cls}], needs: [{q, step, role}], hands: [{q, step, mob, chance, how}], total} */
  Q.forItem = function (id) {
    var e = S && S.item.get(low(id));
    if (!e) return NONE;
    return {rewards: e.rewards, needs: e.needs, hands: e.hands, total: e.rewards.length + e.needs.length + e.hands.length};
  };
  /* WG.quest.forMob(kindKey) -> [{q, steps: [n, ...], kill: bool, hands: [{item, chance, how, step}]}], one entry per quest */
  Q.forMob = function (key) {
    var rows = S && S.mob.get(low(key)), out = [], by = new Map();
    (rows || []).forEach(function (r) {
      var e = by.get(r.q);
      if (!e) { e = {q: r.q, steps: [], kill: false, hands: []}; by.set(r.q, e); out.push(e); }
      if (r.kill) { e.kill = true; if (e.steps.indexOf(r.step) < 0) e.steps.push(r.step); }
      if (r.item) e.hands.push({item: r.item, chance: r.chance, how: r.how, step: r.step});
      if (r.die) e.die = true;
    });
    out.sort(function (a, b) { return a.q.lmin - b.q.lmin || (a.q._lc < b.q._lc ? -1 : 1); });
    return out;
  };
  /* WG.quest.forNpc(name, zoneId) -> {gives: [quest], steps: [{q, n, t}]}; zoneId (optional) keeps only that zone's NPC */
  Q.forNpc = function (name, zone) {
    var e = S && S.npc.get(low(name));
    if (!e) return {gives: [], steps: []};
    if (zone == null) return e;
    return {gives: e.gives.filter(function (q) { return (q.giver && q.giver.zone === +zone) || (q.givers || []).some(function (g) { return g.zone === +zone && low(g.name) === low(name); }); }),
      steps: e.steps.filter(function (s) { var st = s.q.steps[s.n - 1], t = st && st.target; return t && t.zone === +zone; })};
  };
  /* WG.quest.forZone(zoneId) -> {gives: [quest], steps: [{q, ns: [step numbers]}]} */
  Q.forZone = function (zone) {
    var e = S && S.zone.get(+zone), out = {gives: [], steps: []};
    if (!e) return out;
    out.gives = e.gives.slice();
    e.steps.forEach(function (ns, q) { out.steps.push({q: q, ns: ns}); });
    return out;
  };
  /* WG.quest.search(text, limit) -> {list: [{id, name, sub, href, q}], total}: quests by part of the name (or of the giver's name). */
  Q.search = function (text, limit) {
    var t = low(text).trim(), hits = [], seen = {};
    if (!S || !t) return {list: [], total: 0};
    S.list.forEach(function (q) {
      var a = q._lc.indexOf(t), b = a < 0 && q.giver ? low(q.giver.name).indexOf(t) : -1;
      if (a < 0 && b < 0) return;
      var g = q.grp != null ? "g" + q.grp : "q" + q.id;
      if (seen[g]) return; seen[g] = 1;
      hits.push({q: q, rank: a === 0 ? 0 : a > 0 ? 1 : 2});
    });
    hits.sort(function (x, y) { return x.rank - y.rank || x.q.lmin - y.q.lmin || (x.q._lc < y.q._lc ? -1 : 1); });
    return {total: hits.length, list: hits.slice(0, limit || 20).map(function (x) {
      var q = x.q;
      return {id: q.id, name: q.name, q: q, href: Q.href(q),
        sub: "Quest, level " + Q.levelText(q) + (q.realm ? ", " + WG.REALMS[q.realm] : "") + (q.giver ? ", from " + q.giver.name : "")};
    })};
  };

  /* ---------- drawing ---------- */
  var cssDone = false;
  Q.css = function () {
    if (cssDone) return; cssDone = true;
    var st = document.createElement("style");
    st.textContent = [
      ".q-list{display:grid;gap:2px}",
      ".q-row{display:grid;grid-template-columns:minmax(0,1.5fr) 78px minmax(0,1fr) minmax(0,1.6fr);gap:10px;align-items:baseline;padding:4px 8px;border-radius:4px}",
      ".q-row:hover{background:var(--bg3)}",
      ".q-row .q-note{color:var(--text2)}",
      ".q-off{opacity:.55}",
      ".q-tag{display:inline-block;font-size:11px;padding:0 6px;border-radius:9px;border:1px solid var(--line,#3a4150);color:var(--dim);margin-left:6px;white-space:nowrap;vertical-align:1px}",
      ".q-tag.epic{color:var(--con-purple);border-color:var(--con-purple)}",
      ".q-more{margin-top:6px}",
      "@media (max-width:900px){.q-row{grid-template-columns:minmax(0,1fr) 70px minmax(0,1fr)}.q-row .q-where{display:none}}"
    ].join("\n");
    document.head.appendChild(st);
  };
  /* WG.quest.link(quest, opts) -> <a>: the name, realm tinted. opts.tags === false hides the kind tag. */
  Q.link = function (q, opts) {
    opts = opts || {};
    Q.css();                 // the tags need their style on every page that uses the link (search, zones, ...)
    var a = h("a.wg-link" + (q.realm ? ".realm-" + q.realm : ""), {href: Q.href(q)}, opts.text || q.name);
    if (opts.tags === false) return a;
    var out = [a];
    if (q.part) out.push(h("span.q-tag", {title: "Part " + q.part[0] + " of " + q.part[1] + " quests of this name"}, "part " + q.part[0] + "/" + q.part[1]));
    if (q.kind === "epic") out.push(h("span.q-tag.epic", "epic"));
    else if (q.kind === "killtask") out.push(h("span.q-tag", "kill task"));
    return h("span", out);
  };
  /* WG.quest.section(el, list, opts) -> the section element (or null when the list is empty and opts.empty is not set).
     list: quests, or rows {q: quest, note: Node or text}. opts: title ("Quests"), max (12 rows before "show all"),
     empty (text for no rows), me (false = do not dim quests the character cannot take), plain (true = no card around it). */
  Q.section = function (el, list, opts) {
    opts = opts || {};
    Q.css();
    var u = ui(), rows = (list || []).map(function (r) { return r && r.q ? r : {q: r}; }).filter(function (r) { return r.q; });
    if (!rows.length && !opts.empty) return null;
    var box = h("div.q-list"), max = opts.max || 12, more = null;
    function draw(all) {
      WG.clear(box);
      rows.slice(0, all ? rows.length : max).forEach(function (r) {
        var q = r.q, off = opts.me !== false && !Q.canTake(q);
        box.appendChild(h("div.q-row" + (off ? ".q-off" : ""), {title: off ? "Not for your character's level, realm or class" : ""},
          h("span", Q.link(q)),
          h("span.wg-dim.wg-nowrap", "level " + Q.levelText(q)),
          h("span.q-where", q.giver ? [h("span.wg-dim", "from "), q.giver.name, h("span.wg-dim", ", "), Q.zoneEl(q.giver.zone, [q.giver.x, q.giver.y])] : h("span.wg-dim", "given by a courier")),
          h("span.q-note", r.note || Q.classText(q))));
      });
      if (!rows.length) box.appendChild(u.empty ? u.empty(opts.empty) : h("div.wg-dim", opts.empty));
      if (!all && rows.length > max) {
        more = h("button.wg-btn.small.q-more", {type: "button", onclick: function () { draw(true); }}, "Show all " + rows.length);
        box.appendChild(more);
      }
    }
    draw(false);
    var sec = opts.plain || !u.section ? h("div", opts.title ? h("h3", opts.title) : null, box) : u.section(opts.title || "Quests", {count: rows.length}, box);
    if (el) el.appendChild(sec);
    return sec;
  };

  function step(n) { return n ? "step " + n : "any step"; }
  var HOW = {"final": "reward", choice: "reward, one of a choice", trade: "given in a trade", grant: "given during the quest", side: "side turn-in reward", step: "given by a kill step", random: "random turn-in reward"};
  /* Rows for WG.quest.section from the lookups (also useful on their own). */
  Q.itemRows = function (id) {
    var f = Q.forItem(id), rows = [];
    f.rewards.forEach(function (r) { rows.push({q: r.q, note: [h("b", "Reward"), " (" + (HOW[r.how] || r.how) + (r.cls ? ", " + Q.className(r.cls) + " version" : "") + ")"]}); });
    var groups = new Map();           // one row per quest, source and chance: the monsters are listed together
    f.hands.forEach(function (r) {
      var k = r.q.id + "|" + (r.how === "loot" ? "loot" : "hand") + "|" + r.chance + "|" + r.step, g = groups.get(k);
      if (!g) groups.set(k, (g = {r: r, mobs: []}));
      if (g.mobs.indexOf(r.mob) < 0) g.mobs.push(r.mob);
    });
    groups.forEach(function (g) {
      var r = g.r, names = [];
      g.mobs.slice(0, 4).forEach(function (m, i) { if (i) names.push(", "); names.push(Q.mobEl(m, m, {level: false})); });
      if (g.mobs.length > 4) names.push(" and " + (g.mobs.length - 4) + " more");
      rows.push({q: r.q, note: [h("b", r.how === "loot" ? "Drops from " : "Handed over by "), names, ": " + Q.chanceText(r.chance) +
        (r.how === "loot" ? (r.q.turnin ? " (ordinary loot)" : " (loot, only while on the quest)") : ", " + step(r.step))]});
    });
    f.needs.forEach(function (r) { rows.push({q: r.q, note: [h("b", "Needed"), ": " + r.role + (r.step ? ", step " + r.step : "")]}); });
    return rows;
  };
  Q.mobRows = function (key) {
    return Q.forMob(key).map(function (e) {
      var note = [];
      if (e.kill) note.push(h("b", "Kill it"), " (step " + e.steps.sort(function (a, b) { return a - b; }).join(", ") + ")");
      else if (e.die) note.push(h("b", "Be slain by it"));
      var seen = {};
      e.hands.forEach(function (hd) {
        var k = hd.item + "|" + hd.chance; if (seen[k]) return; seen[k] = 1;
        note.push(note.length ? "; " : "", hd.how === "loot" ? "drops " : "hands over ", Q.itemEl(hd.item, {icon: false}), " " + Q.chanceText(hd.chance));
      });
      return {q: e.q, note: note};
    });
  };
  Q.zoneRows = function (zone) {
    var f = Q.forZone(zone), rows = [], given = new Set(f.gives);
    f.gives.slice().sort(function (a, b) { return a.lmin - b.lmin || (a._lc < b._lc ? -1 : 1); }).forEach(function (q) { rows.push({q: q, note: [h("b", "Given here"), " - " + Q.classText(q)]}); });
    f.steps.filter(function (s) { return !given.has(s.q); }).sort(function (a, b) { return a.q.lmin - b.q.lmin || (a.q._lc < b.q._lc ? -1 : 1); })
      .forEach(function (s) { rows.push({q: s.q, note: "Step " + s.ns.join(", ") + " is here"}); });
    return rows;
  };
  Q.npcRows = function (name, zone) {
    var f = Q.forNpc(name, zone), rows = [], given = new Set(f.gives), by = new Map();
    f.gives.forEach(function (q) { rows.push({q: q, note: [h("b", "Gives it"), " - " + Q.classText(q)]}); });
    f.steps.forEach(function (s) { if (given.has(s.q)) return; var a = by.get(s.q); if (!a) by.set(s.q, (a = [])); a.push(s.n); });
    by.forEach(function (ns, q) { rows.push({q: q, note: "Step " + ns.join(", ")}); });
    return rows;
  };
  function noteText(n) { return n == null ? "" : Array.isArray(n) ? n.map(noteText).join("") : n instanceof Node ? n.textContent : String(n); }
  /* One row per class-version group: the version the character can take, else the first. */
  Q.mergeVersions = function (rows) {
    var by = new Map(), out = [];
    rows.forEach(function (r) {
      var k = (r.q.grp != null ? r.q.grp : r.q.id) + "|" + noteText(r.note);
      var e = by.get(k);
      if (!e) { by.set(k, r); out.push(r); }
      else if (!Q.canTake(e.q) && Q.canTake(r.q)) { out[out.indexOf(e)] = r; by.set(k, r); }
    });
    return out;
  };
  function embed(el, rowsOf, opts) {
    return Q.full().then(function () {
      var rows = Q.mergeVersions(rowsOf());
      Q.section(el, rows, opts);
      return rows.length;
    });
  }
  Q.itemSection = function (el, id, opts) { return embed(el, function () { return Q.itemRows(id); }, Object.assign({title: "Quests"}, opts)); };
  Q.mobSection = function (el, key, opts) { return embed(el, function () { return Q.mobRows(key); }, Object.assign({title: "Quests"}, opts)); };
  Q.zoneSection = function (el, zone, opts) { return embed(el, function () { return Q.zoneRows(zone); }, Object.assign({title: "Quests"}, opts)); };
  Q.npcSection = function (el, name, zone, opts) { return embed(el, function () { return Q.npcRows(name, zone); }, Object.assign({title: "Quests"}, opts)); };
})();
