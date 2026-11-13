/* OFFLINEDAOC Database - one monster kind (#/mob/<id>), lane M-U.
   Levels and con for my level, where it lives (zone tabs on the shared map: red pins for camps, dots for single
   spawns), every camp with its neighbour warning, and what it drops with the real chances from data/drops.js.
   Uses the shared model and helpers of hunt.js (WG.hunt). #/mob/<name> also works (the lowercased name). */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, con = WG.con, ui = WG.ui, hunt = WG.hunt;
  var live = null;

  function findKind(M, p) { return hunt.find(M, p); }
  function classEl(k) {
    var kc = hunt.classOf(k);
    return h("span", kc === "normal" ? (k.scripted ? "Script-made monster" : "Ordinary monster") : h("span.mu-tag" + (kc === "named" || kc === "quest" ? "" : ".epic"), {title: hunt.classHelp(kc)}, hunt.classLabel(kc)),
      hunt.isGroup(k) ? [" ", h("span.mu-tag.group", {title: hunt.GROUP_HELP}, "Group content")] : null,
      k.scripted && kc !== "normal" ? [" ", h("span.mu-tag", {title: "Not a standing spawn: a script or a quest step creates it"}, "Script-made")] : null,
      k.rankWhy ? h("div.mu-help", k.rankWhy.charAt(0).toUpperCase() + k.rankWhy.slice(1) + ".") : null);
  }
  function eventRows(k) {
    return hunt.eventsOf(k).map(function (e, i, all) {
      if (i && all[i - 1].name === e.name) return null;       // Summoner's Hall and Darkness Falls run once per realm
      var z = hunt.model.zoneById.get(e.zone);
      return ["Realm event", h("span", h("b", e.name), " (" + e.kind + "), in ", ui.zoneLink(z || e.zone), ". ",
        (e.bosses || []).indexOf(k.name) >= 0 ? "It is the final boss. " : "It is one of the named encounters on the way. ",
        h("span.wg-dim", "The server musters " + e.suggested + " for it; a player's own raid is /raid 40 or /raid 80. When the event runs is decided while the game runs."))];
    }).filter(Boolean);
  }

  /* A monster that only a script or a quest step creates (mobs.scripted): no camps, no stance in the data. */
  function renderScripted(el, k, M, level, me, query) {
    var lvl = ui.levelInput({value: level, label: "My level", onChange: function (n) {
      if (query.level != null) { var q = {}; for (var x in query) q[x] = query[x]; q.level = n; WG.go("mob/" + k.id, q); }
      else WG.setMe({level: n});
    }});
    ui.page(el, {
      title: k.name, crumbs: [["Monsters", WG.href("mobs")], ["Bosses and epic encounters", WG.href("hunt", {mode: "bosses"})], [k.name]],
      sub: h("span", "Level ", hunt.levelEl(k, level), k.levelMin != null ? ", " + conWords(level, k.levelMin, k.levelMax) + " to a level " + level + " character" : "",
        ". Not a standing spawn: a script or a quest step creates it."),
      actions: [lvl]
    });
    var facts = h("div.wg-card"), mapCard = h("div.wg-card"), mapEl = h("div");
    el.appendChild(h("div", {style: {display: "grid", gridTemplateColumns: "minmax(280px, 380px) minmax(0, 1fr)", gap: "14px", alignItems: "start", marginBottom: "14px"}}, facts, mapCard));
    var pairs = [
      ["Class", classEl(k)],
      ["Levels", hunt.levelEl(k, level)],
      ["How it appears", h("span", k.how || "A script creates it.")]
    ];
    var by = (k.by || []).map(function (id) { return M.kindById.get(id); }).filter(Boolean);
    var others = (k.byNames || []).filter(function (n) { return !by.some(function (b) { return b.name === n; }); });
    if (by.length || others.length) pairs.push(["Created by", h("span", by.map(function (b, i) { return [i ? ", " : null, h("a", {href: WG.href("mob/" + b.id)}, b.name)]; }),
      others.length ? h("span.wg-dim", (by.length ? ", " : "") + others.join(", ") + " (a controller, not a monster)") : null)]);
    if (k.quest) pairs.push(["Quest", String(k.quest)]);
    eventRows(k).forEach(function (p) { pairs.push(p); });
    var xp = k.levelMin != null ? hunt.xpEl(k.levelMin, k.levelMax, level) : null;
    if (xp) pairs.push(["Base experience", xp]);
    var body = M.meta.bodyTypes && M.meta.bodyTypes[k.body];
    if (body) pairs.push(["Body type", body]);
    if (k.where && k.where.length) pairs.push(["Where", h("span", k.where.slice(0, 6).map(function (w, i) {
      return [i ? "; " : null, ui.zoneLink(M.zoneById.get(w[0]) || w[0], {loc: [w[1], w[2]]}), hunt.zoneTagEls(w[0]).map(function (t) { return [" ", t]; }), h("span.wg-dim", " loc " + ui.num(w[1]) + ", " + ui.num(w[2]))];
    }))]);
    else pairs.push(["Where", h("span.wg-dim", "The script or quest works the place out when it runs.")]);
    pairs.push(["Stance", h("span.wg-dim", "Not in the data: the script sets it up when the monster appears.")]);
    if (k.file && k.file.length) pairs.push(["Read from", h("span.wg-dim", k.file.join(", "))]);
    facts.appendChild(ui.kv(pairs));
    mapCard.appendChild(mapEl);
    if (k.where && k.where.length && WG.Map && WG.Map.multi) {
      var gz = new Map();
      k.where.forEach(function (w) { var a = gz.get(w[0]); if (!a) gz.set(w[0], (a = [])); a.push({x: w[1], y: w[2], kind: "point", color: "red", label: k.name, sub: "appears here\n" + (k.how || ""), legend: "Where it appears"}); });
      var groups = []; gz.forEach(function (pins, zone) { groups.push({zone: zone, pins: pins}); });
      me.multi = WG.Map.multi(mapEl, groups, {height: 440});
      mapCard.appendChild(h("div.mu-help", {style: {marginTop: "6px"}}, "The mark is the spot of the monster or controller whose script creates it, or the spot the quest names."));
    } else mapEl.appendChild(h("div.wg-empty", "No fixed place in the data: the script or quest decides where it appears."));
    dropsSection(el, k, level, me);
    questsSection(el, k);
  }

  function conWords(level, min, max) {
    var s = con.span(level, min, max);
    return s.mixed ? s.low + " to " + s.high : s.low;
  }
  function plural(n, one, many) { return ui.num(n) + " " + (n === 1 ? one : many); }
  function coinRange(L) { var min = 2 + Math.floor(Math.pow(L + 1, 3) / 8); return [min, min + Math.floor(min / 2)]; }   // drops.md "coin amount"

  /* ---- drops (data/drops.js; a script-made monster's table is under "unspawned") ---- */
  function dropsSection(el, k, level, me) {
      var dropSec = ui.section("Drops"), dropBox = h("div");
      dropSec.body.appendChild(dropBox);
      el.appendChild(dropSec);
      ui.loading(dropBox, "Loading drops...");
      hunt.drops().then(function (D) { if (me.alive) paintDrops(D); }, function (e) {
        if (!me.alive) return;
        WG.clear(dropBox); dropBox.appendChild(ui.note("The drop data is not built yet (data/drops.js is missing). Run source/build_all.py.", "warn"));
      });
      function paintDrops(D) {
        WG.clear(dropBox);
        var kd = D.of(k.key), realm = WG.me.realm || 0, FL = D.flags;
        if (!kd) { dropBox.appendChild(h("div.wg-empty", k.scripted ? "No loot table carries this monster's name: it drops no items." : "The drop data has no entry for this monster.")); return; }
        var rows = (kd.d || []).map(function (r) {
          var it = D.item(r[0]), mine = Array.isArray(r[4]) && realm ? r[4][realm - 1] : r[1];
          return {it: it, name: it.name, level: it.level || 0, chance: mine, base: r[1], n: r[2] || 1, flags: r[3] || 0, byRealm: Array.isArray(r[4]) ? r[4] : null,
                  quests: (r[5] || []).map(function (id) { return D.quests[id] || "quest " + id; })};
        });
        // the lines above the table: coin, one-time drops, quest hand-overs, what never drops
        var lines = [];
        if (kd.none) {
          var nn = kd.none;
          lines.push(ui.note(nn[0] >= nn[1] ? "This monster never drops anything, coin included (" + (nn[2] === "keep guard" ? "keep guards do not drop loot" : "its script turns loot off") + ")."
            : nn[0] + " of its " + nn[1] + " spawns never drop anything, coin included (" + (nn[2] === "keep guard" ? "keep guards" : "a script turns loot off") + ").", "warn"));
        }
        if (kd.coin != null && k.levelMin != null && !(kd.none && kd.none[0] >= kd.none[1])) {
          var cp = Array.isArray(kd.coin) ? kd.coin[0] + "% to " + kd.coin[1] + "%" : kd.coin + "%";
          var lo = coinRange(k.levelMin), hi = coinRange(k.levelMax);
          lines.push(h("div", h("b", "Coin: "), (kd.coin === 0 ? "never drops coin" : cp + " of kills drop a bag of " + WG.money(lo[0]) + " to " + WG.money(hi[1])) +
            (kd.coin !== 0 && k.levelMin !== k.levelMax ? " (level " + k.levelMin + ": " + WG.money(lo[0]) + " to " + WG.money(lo[1]) + "; level " + k.levelMax + ": " + WG.money(hi[0]) + " to " + WG.money(hi[1]) + ")" : "") + ".",
            Array.isArray(kd.coin) ? h("span.wg-dim", " The chance depends on the body type of the spawn.") : null));
        }
        (kd.otd || []).forEach(function (o) {
          lines.push(h("div", h("b", "One-time drop: "), ui.itemLink(D.item(o[0])), " goes straight into the backpack the first time a character of level " + o[1] + " or higher takes part in a kill. Once per character.",
            level < o[1] ? h("span.wg-warn", " You are level " + level + ".") : null));
        });
        (kd.q || []).forEach(function (o) {
          lines.push(h("div", h("b", "Quest: "), ui.itemLink(D.item(o[0])), " " + WG.pct(o[1]) + " per kill while on the quest ", h("i", D.quests[o[2]] || "quest " + o[2]), o[3] && o[3].length ? " (step " + o[3].join(", ") + ")" : "", ". It goes into the backpack, it is not loot."));
        });
        if (lines.length) dropBox.appendChild(h("div", {style: {display: "flex", flexDirection: "column", gap: "4px", marginBottom: "10px"}}, lines));
        if (!rows.length) { dropBox.appendChild(h("div.wg-empty", "This monster has no loot table: it drops no items" + (kd.coin ? ", only coin." : "."))); return; }
        var info = h("div.mu-help", {style: {marginBottom: "8px"}},
          "Chance = one kill drops the item, as the server works it out" + (realm ? " for a " + WG.REALMS[realm] + " character" : "") + ". One kill gives at most " + plural(kd.dc, "random item", "random items") +
          (rows.some(function (r) { return r.flags & FL.always; }) ? " plus the fixed ones" : "") + ". Nothing drops when the monster is grey to anyone who hurt it. In a group the leader's realm decides realm-locked loot.");
        dropBox.appendChild(info);
        if (!realm && rows.some(function (r) { return r.byRealm; })) dropBox.appendChild(ui.note("Some items here drop only for one realm. Set your realm in the header to see your own chances.", "info"));
        var tb = h("div"); dropBox.appendChild(tb);
        ui.table(tb, {
          columns: [
            {key: "name", label: "Item", render: function (r) { return r.it.missing ? h("span", r.it.id, h("span.wg-dim", " (not in the item data)")) : ui.itemLink(r.it); }},
            {key: "level", label: "Level", num: true, width: 70, render: function (r) { return r.level || ""; }},
            {key: "type", label: "Kind", sort: function (r) { return typeText(r.it); }, render: function (r) { return typeText(r.it); }},
            {key: "chance", label: "Chance per kill", num: true, width: 170, render: function (r) {
              if (r.flags & FL.never) return h("span.wg-dim", "never");
              if (!(r.chance > 0)) return h("span.wg-dim", "not for your realm");
              return ui.bar(r.chance, {text: WG.pct(r.chance), width: 70});
            }},
            {key: "kills", label: "About 1 in", num: true, width: 90, title: "Kills for one drop on average (1 / chance)", sort: function (r) { return r.chance > 0 ? 1 / r.chance : null; },
              render: function (r) { return r.chance > 0 && !(r.flags & FL.never) ? ui.num(Math.max(1, Math.round(1 / r.chance))) : ""; }},
            {key: "note", label: "Note", sort: false, render: function (r) {
              var out = [];
              if (r.n > 1) out.push(r.n + " pieces each time");
              if (r.flags & FL.always) out.push("Fixed drop, every kill");
              if (r.flags & FL.quest) out.push("Only while on the quest " + r.quests.join(" or "));
              if (r.flags & FL.never) out.push("Quest item that no quest uses: it never falls");
              if (r.flags & FL.realm) { var ir = r.it.realm || (r.byRealm ? r.byRealm.indexOf(Math.max.apply(null, r.byRealm)) + 1 : 0); out.push(ir ? "Only for " + WG.REALMS[ir] + " characters" : "Realm-locked"); }
              else if (r.byRealm) out.push("Alb " + WG.pct(r.byRealm[0]) + ", Mid " + WG.pct(r.byRealm[1]) + ", Hib " + WG.pct(r.byRealm[2]));
              return h("span.wg-dim", out.join(". "));
            }}
          ],
          rows: rows, sortKey: "chance", sortDir: "desc", pageSize: 50, count: rows.length === 1 ? "item" : "items",
          rowHref: function (r) { return r.it.missing ? null : WG.href("item/" + encodeURIComponent(r.it.id)); }
        });
      }
      function typeText(it) {
        if (it.missing) return "";
        try { if (WG.item && WG.item.typeSlot) return WG.item.typeSlot(it) || it.cat || ""; } catch (e) {}
        return it.cat || "";
      }
  }

  /* ---- quests that send you to kill it (lane Q: app/lib/quests.js, source/contract/quests.md) ----
     The card goes into its own box, so a late answer never lands on another page. Nothing is added when no
     quest names the monster. */
  function questsSection(el, k) {
    if (!WG.quest || typeof WG.quest.mobSection !== 'function') return;
    var box = h('div');
    el.appendChild(box);
    try { var p = WG.quest.mobSection(box, k.key); if (p && p.then) p.then(null, function () {}); } catch (e) { console.error(e); }
  }

  function render(el, params, query) {
    hunt.css();
    var me = (live = {alive: true, multi: null});
    ui.loading(el, "Loading the monster...");
    return hunt.load().then(function (M) {
      if (!me.alive) return;
      WG.clear(el);
      var k = findKind(M, params[0]);
      if (!k) {
        ui.page(el, {title: "Monster not found", crumbs: [["Monsters", WG.href("mobs")]]});
        el.appendChild(h("div.wg-empty", "There is no monster \"" + (params[0] || "") + "\" in the data. ", h("a", {href: WG.href("mobs")}, "Search the monster list")));
        return;
      }
      document.title = k.name + " - OFFLINEDAOC Database";
      var level = hunt.clampLevel(query.level != null ? query.level : WG.me.level, 1);
      if (k.scripted) return renderScripted(el, k, M, level, me, query);
      var camps = (M.byKind.get(k.id) || []).slice().sort(function (a, b) { return b.n - a.n || a.id - b.id; });
      var sel = query.camp != null ? M.campById.get(+query.camp) : null;
      if (sel && sel.kind !== k.id) sel = null;

      /* ---- head ---- */
      var lvl = ui.levelInput({value: level, label: "My level", onChange: function (n) {
        if (query.level != null) { var q = {}; for (var x in query) q[x] = query[x]; q.level = n; WG.go("mob/" + k.id, q); }
        else WG.setMe({level: n});
      }});
      var zonesN = k.zoneIds.length;
      ui.page(el, {
        title: k.name, crumbs: [["Monsters", WG.href("mobs")], [k.name]],
        sub: h("span", "Level ", con.el(level, k.levelMin, k.levelMax), ", " + conWords(level, k.levelMin, k.levelMax) + " to a level " + level + " character. " +
          plural(k.spawns, "spawn", "spawns") + " in " + plural(camps.length, "camp", "camps") + ", " + plural(zonesN, "zone", "zones") + "."),
        actions: [lvl, h("a.wg-btn", {href: WG.href("hunt", {level: level, con: "custom", min: k.levelMin, max: k.levelMax, q: k.name, size: 1, match: "any", realm: 0}), title: "Open the hunting finder with this monster's camps"}, "Its camps in the hunting finder")]
      });

      /* ---- facts + map ---- */
      var facts = h("div.wg-card"), mapCard = h("div.wg-card"), mapEl = h("div");
      var topRow = h("div", {style: {display: "grid", gridTemplateColumns: "minmax(280px, 340px) minmax(0, 1fr)", gap: "14px", alignItems: "start", marginBottom: "14px"}}, facts, mapCard);
      el.appendChild(topRow);

      var hist = {}, aggroN = 0, total = 0;
      camps.forEach(function (c) { c.hist.forEach(function (e) { hist[e[0]] = (hist[e[0]] || 0) + e[1]; }); aggroN += c.aggroN; total += c.n; });
      var histList = Object.keys(hist).map(function (L) { return [+L, hist[L]]; }).sort(function (a, b) { return a[0] - b[0]; });
      var wide = k.levelMax - k.levelMin;
      var pairs = [
        ["Levels", h("span", con.el(level, k.levelMin, k.levelMax), wide > 0 ? h("span.wg-dim", hunt.gappy(histList) ? " (only the levels below)" : " (" + (wide + 1) + " levels wide)") : null)],
        ["Con for level " + level, h("span", conWords(level, k.levelMin, k.levelMax).split(" to ").map(function (c, i) { return [i ? " to " : null, h("span." + con.cls(c), c)]; }))],
        histList.length > 1 ? ["Level spread", hunt.spread(histList, level, 48)] : null,
        ["Spawns", plural(k.spawns, "spawn", "spawns") + " in " + plural(camps.length, "camp", "camps")],
        ["Stance", h("span", hunt.stanceEl(k.stance), h("span.wg-dim", " towards a new character"))],
        k.ag > 0 ? ["", h("span.mu-help", (k.ag >= k.spawns ? "It attacks" : k.ag + " of its " + ui.num(k.spawns) + " spawns attack") + " when you come within " + ui.num(k.aggroRange) + ". It leaves you alone while it is grey to you.")] : null
      ];
      // class (ordinary, named, boss, epic or raid boss, realm event boss, quest target), how it appears, zone tags
      var kc = hunt.classOf(k);
      pairs.unshift(["Class", classEl(k)]);
      if (kc !== "normal") pairs.push(["How it appears", hunt.appearEl(k)]);
      eventRows(k).forEach(function (p) { pairs.push(p); });
      var makes = M.extras.filter(function (x) { return (x.by || []).indexOf(k.id) >= 0; });
      if (makes.length) pairs.push(["Its script creates", h("span", makes.slice(0, 12).map(function (x, i) { return [i ? ", " : null, h("a", {href: WG.href("mob/" + x.id)}, x.name)]; }), makes.length > 12 ? h("span.wg-dim", " and " + (makes.length - 12) + " more") : null)]);
      var tagged = k.zoneIds.filter(function (z) { return M.zoneTags.has(z); });
      if (tagged.length || k.zoneIds.length > 1) {
        pairs.push([k.zoneIds.length === 1 ? "Zone" : "Zones", h("span", k.zoneIds.slice(0, 8).map(function (zid, i) {
          return [i ? ", " : null, ui.zoneLink(M.zoneById.get(zid) || zid), hunt.zoneTagEls(zid).map(function (t) { return [" ", t]; })];
        }), k.zoneIds.length > 8 ? h("span.wg-dim", " and " + (k.zoneIds.length - 8) + " more") : null)]);
      }
      var xpEl = hunt.xpEl(k.levelMin, k.levelMax, level);
      if (xpEl) pairs.push(["Base experience", xpEl]);
      else if (con.color(level, k.levelMax) === "grey") pairs.push(["Base experience", h("span.wg-dim", "Grey to you: no experience and no loot")]);
      hunt.kindFacts(k).forEach(function (p) { pairs.push(p); });
      facts.appendChild(ui.kv(pairs));

      /* ---- the map: one tab per zone ---- */
      function campPin(c) {
        var t = hunt.threats(c, level);
        return {x: c.x, y: c.y, kind: c.n > 1 ? "camp" : "spawn", n: c.n, radius: c.n > 1 ? c.radius || null : null, id: "c" + c.id, label: k.name,
          sub: "level " + con.rangeText(c.levelMin, c.levelMax) + ", " + plural(c.n, "spawn", "spawns") + ", " + hunt.stanceText(c.stance) +
            (t.sev === 3 ? "\nstronger monsters can attack at this camp" : t.sev === 2 ? "\nstronger monsters nearby" : t.sev === 1 ? "\nother monsters can join in" : ""),
          legend: c.n > 1 ? "Camp (the number of spawns)" : "Single spawn"};
      }
      var base = new Map();
      camps.forEach(function (c) { var a = base.get(c.zone); if (!a) base.set(c.zone, (a = [])); a.push(campPin(c)); });
      var groups = []; base.forEach(function (pins, zone) {
        var lines = (M.pathsByKind.get(k.id) || []).filter(function (p) { return p.zone === zone; }).map(function (p) { return {pts: p.pts, color: "#ff8a8a", dashed: true, label: k.name + " patrol"}; });
        groups.push({zone: zone, pins: pins, lines: lines});
      });
      mapCard.appendChild(mapEl);
      var mapNote = h("div.mu-help", {style: {marginTop: "6px"}}, camps.length ? "Click a pin or a row of the camp list to see that camp and the monsters around it." : "");
      mapCard.appendChild(mapNote);
      if (!camps.length) mapEl.appendChild(h("div.wg-empty", "This monster has no spawn with a place on a zone map."));
      else if (WG.Map && WG.Map.multi) {
        me.multi = WG.Map.multi(mapEl, groups, {height: 470, onPin: function (pin) {
          if (pin && typeof pin.id === "string" && pin.id.charAt(0) === "c") choose(M.campById.get(+pin.id.slice(1)), true);
          else if (pin && pin.href) location.hash = pin.href;
        }});
      } else mapEl.appendChild(h("div.wg-empty", "The map viewer is not built yet."));

      /* ---- the camp list ---- */
      var campSec = ui.section("Camps", {count: camps.length}), campBox = h("div"), campInfo = h("div");
      campSec.body.appendChild(campInfo); campSec.body.appendChild(campBox);
      el.appendChild(campSec);
      var danger = camps.filter(function (c) { return hunt.threats(c, level).sev === 3; }).length;
      var watch = camps.filter(function (c) { return hunt.threats(c, level).sev === 2; }).length;
      campInfo.appendChild(h("div", {style: {marginBottom: "8px"}},
        danger ? h("span", h("b.wg-danger", plural(danger, "camp has", "camps have")), " a stronger aggressive monster whose aggro range reaches the camp. ") : null,
        watch ? h("span", h("b.wg-warn", plural(watch, "camp has", "camps have")), " a stronger monster nearby. ") : null,
        !danger && !watch && camps.length ? h("span.mu-ok", "No camp has a stronger monster nearby (for a level " + level + " character). ") : null,
        M.nearDist != null ? h("span.mu-help", "Nearby = within " + ui.num(M.nearDist) + " of a spawn of the camp. Monsters that are grey to you are left out.") : h("span.mu-help", "Monsters that are grey to you are left out.")));
      var table = ui.table(campBox, {
        columns: [
          {key: "zone", label: "Zone", sort: function (c) { var z = M.zoneById.get(c.zone); return z ? z.name : ""; }, render: function (c) { return ui.zoneLink(M.zoneById.get(c.zone) || c.zone, {loc: [c.x, c.y]}); }},
          {key: "levelMin", label: "Levels", num: true, width: 100, render: function (c) { return con.el(level, c.levelMin, c.levelMax); }},
          {key: "n", label: "Spawns", num: true, width: 80},
          {key: "stance", label: "Stance", width: 170, sort: function (c) { return c.n ? (c.stance[0] + c.stance[1] * 0.5) / c.n : 0; }, render: function (c) { return hunt.stanceEl(c.stance); }},
          {key: "warn", label: "Nearby", sort: function (c) { var t = hunt.threats(c, level); return t.sev * 1000 + t.list.length; }, render: function (c) { return hunt.warning(c, level, {max: 3}); }},
          {key: "loc", label: "Place", width: 130, sort: false, render: function (c) { return h("span.wg-dim.wg-nowrap", "loc " + ui.num(c.x) + ", " + ui.num(c.y)); }}
        ],
        rows: camps, sortKey: "n", sortDir: "desc", pageSize: 25, count: camps.length === 1 ? "camp" : "camps",
        rowClass: function (c) { return c === sel ? "mu-sel" : ""; },
        onRow: function (c) { choose(c, false); },
        empty: "This monster has no camp in the data."
      });

      function choose(c, fromMap) {
        if (!c) return;
        sel = c;
        var q = {}; for (var x in query) q[x] = query[x]; q.camp = c.id; WG.setQuery(q);
        if (!fromMap) { var i = table.rows().indexOf(c), st = table.state(); if (i >= 0 && st.pageSize > 0) table.page(Math.floor(i / st.pageSize) + 1); }
        else { var j = table.rows().indexOf(c), s2 = table.state(); if (j >= 0 && s2.pageSize > 0 && Math.floor(j / s2.pageSize) + 1 !== s2.page) table.page(Math.floor(j / s2.pageSize) + 1); }
        table.refresh();
        showOnMap(c, !fromMap);
      }
      function showOnMap(c, scroll) {
        if (!me.multi) return;
        me.multi.show(c.zone);
        var extra = hunt.dangerPins(c, level);
        (c.pts || []).forEach(function (p) { extra.push({x: p[0], y: p[1], kind: "spawn", label: k.name, sub: p.length > 2 && p[2] != null ? "level " + p[2] : "", legend: "Spawn points of the chosen camp"}); });
        me.multi.map.setData({pins: extra.concat(base.get(c.zone) || []), lines: hunt.patrolLines(c, level)});
        me.multi.map.focus("c" + c.id, {scroll: !!scroll});
        var t = hunt.threats(c, level), z = M.zoneById.get(c.zone);
        WG.clear(mapNote);
        mapNote.appendChild(h("span", h("b", "Chosen camp: "), z ? z.name : "zone " + c.zone, ", level ", con.el(level, c.levelMin, c.levelMax), ", " + plural(c.n, "spawn", "spawns") + ". ", hunt.warning(c, level, {max: 4}),
          t.sev ? h("span.wg-dim", " The triangles on the map mark them.") : null));
      }
      if (sel) setTimeout(function () { if (me.alive) { table.refresh(); showOnMap(sel, false); } }, 0);

      dropsSection(el, k, level, me);
    questsSection(el, k);
    });
  }

  WG.view("mob", {
    title: function (params) { var M = hunt.model, k = M && findKind(M, params[0]); return k ? k.name : "Monster"; },
    navAs: "mobs",
    render: render,
    leave: function () {
      if (!live) return;
      live.alive = false;
      if (live.multi) { try { live.multi.destroy(); } catch (e) {} }
      live = null;
    }
  });

  /* My level or realm changed in the header: draw the page again (con colours, warnings and realm chances change). */
  WG.onMe(function () {
    if (!live || !live.alive) return;
    var r = WG.route();
    if (r.view !== "mob") return;
    var main = document.getElementById("wg-main");
    if (!main) return;
    var y = window.scrollY;
    WG.views.mob.leave();
    WG.clear(main);
    var p = render(main, r.params, r.query);
    if (p && p.then) p.then(function () { window.scrollTo(0, y); }, function (e) { WG.showError(main, e); });
  });
})();
