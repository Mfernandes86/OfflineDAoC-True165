/* OFFLINEDAOC Database - stable routes (lane V-U).
   #/routes?from=<stable>&to=<stable>   the planner: fewest tickets, cheapest, fastest
           &norep=1                     only rides that need no reputation
           &pick=tickets|price|time     which answer is drawn
           &net=<region id>             which network map is open
           &stable=<stable>             the stable whose tickets are listed (&leg=<ticket id>: one ticket)
           &notele=1                    leave the Shrouded Isles teleporters out
           &fac=<faction id>            the open "what to kill" list of the reputation section
   The search itself is app/lib/routes.js (WG.routes). Data: data/routes.js (V-D), overview maps: data/zones.js (P-D). */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = WG.ui || {}, vu = WG.vu, R = WG.routes;
  var live = null, focusNext = null;

  var COL = {horse: "#5ad1c8", flying: "#c9a6ff", boat: "#6fb1ff", rep: "#ff9a3c", walk: "#b8bcc6", tele: "#8fc1ff", plan: "#ffd84a", from: "#3fd24a", to: "#ff4a4a", sel: "#ffd84a"};
  var LEG_COLORS = ["#ffd84a", "#5ad1c8", "#ff8ad0", "#8fc1ff", "#9fe870", "#ffa25c", "#c9a6ff", "#f2f2f2"];

  function css() {
    vu.css();
    if (document.getElementById("rt-css")) return;
    document.head.appendChild(h("style", {id: "rt-css", textContent: [
      ".rt-plan{display:flex;gap:10px 14px;align-items:flex-end;flex-wrap:wrap}",
      ".rt-pick{flex:1 1 300px;min-width:240px;max-width:460px}",
      ".rt-lab{display:block;color:var(--dim);font-size:12px;text-transform:uppercase;letter-spacing:.4px;margin-bottom:3px}",
      ".rt-box{position:relative}",
      ".rt-in{width:100%;padding:8px 30px 8px 10px;font-size:15px}",
      ".rt-in:focus{outline:none;border-color:var(--accent)}",
      ".rt-x{position:absolute;right:4px;top:50%;transform:translateY(-50%);border:0;background:none;color:var(--dim);font-size:18px;line-height:1;padding:2px 6px;cursor:pointer}",
      ".rt-x:hover{color:var(--text)}",
      ".rt-list{display:none;position:absolute;z-index:40;left:0;right:0;top:100%;margin-top:2px;max-height:380px;overflow:auto;background:var(--bg2);border:1px solid var(--accent);border-radius:var(--radius);box-shadow:0 8px 24px rgba(0,0,0,.5)}",
      ".rt-list.open{display:block}",
      ".rt-group{padding:5px 10px 2px;color:var(--dim);font-size:11px;text-transform:uppercase;letter-spacing:.5px;position:sticky;top:0;background:var(--bg2)}",
      ".rt-opt{padding:4px 10px 4px 18px;cursor:pointer;display:flex;gap:8px;align-items:baseline}",
      ".rt-opt .rt-tk{margin-left:auto;color:var(--dim);font-size:12px;white-space:nowrap}",
      ".rt-opt.on{background:var(--accent);color:#1a1408}.rt-opt.on .wg-dim,.rt-opt.on .rt-tk{color:#3a2c08}",
      ".rt-none{padding:10px;color:var(--dim)}",
      ".rt-swap{padding:8px 12px;font-size:15px}",
      ".rt-check{display:flex;gap:8px;align-items:center;padding:8px 0;cursor:pointer;user-select:none}",
      ".rt-check input{width:16px;height:16px;accent-color:var(--accent)}",
      ".rt-opts{display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));gap:12px;margin-top:12px}",
      ".rt-optcard{border:1px solid var(--line);border-radius:var(--radius);background:var(--bg);padding:10px 12px;cursor:pointer}",
      ".rt-optcard:hover,.rt-optcard:focus-visible{border-color:var(--dim);outline:none}",
      ".rt-optcard.on{border-color:var(--accent);box-shadow:inset 0 0 0 1px var(--accent)}",
      ".rt-optcard h3{margin:0;font-size:15px;color:var(--accent)}",
      ".rt-total{margin:2px 0 8px;font-size:15px}",
      ".rt-steps{list-style:none;margin:0;padding:0}",
      ".rt-steps li{display:grid;grid-template-columns:26px minmax(0,1fr);gap:8px;padding:6px 0;border-top:1px solid var(--line)}",
      ".rt-n{width:24px;height:24px;border-radius:50%;background:var(--bg3);border:1px solid var(--line);text-align:center;line-height:22px;font-size:12px;font-weight:600}",
      ".rt-steps li.arrive .rt-n{background:var(--accent);color:#1a1408;border-color:var(--accent)}",
      ".rt-steps li.walk .rt-n{border-style:dashed}",
      ".rt-link{font:inherit;color:var(--link);background:none;border:0;padding:0;cursor:pointer;text-align:left}",
      ".rt-link:hover,.rt-link:focus-visible{text-decoration:underline;outline:none}",
      ".rt-kind{display:inline-block;padding:0 7px;border-radius:999px;border:1px solid var(--line);font-size:12px;white-space:nowrap}",
      ".rt-kind.flying{border-color:#c9a6ff;color:#c9a6ff;border-style:dashed}.rt-kind.horse{border-color:#5ad1c8;color:#5ad1c8}",
      ".rt-kind.boat{border-color:#6fb1ff;color:#6fb1ff}.rt-kind.walk{color:var(--dim);border-style:dotted}",
      ".rt-kind.teleport{border-color:#8fc1ff;color:#8fc1ff;border-style:dashed}",
      ".rt-kind.rep{border-color:var(--con-orange);color:var(--con-orange)}",
      ".rt-kind.oneway{color:var(--dim)}",
      ".rt-rep{color:var(--con-orange);margin-top:2px}",
      ".rt-why{margin-top:12px;padding:12px 14px;border:1px solid #7a5a30;background:#2a2216;border-radius:var(--radius)}",
      ".rt-why b{color:var(--con-orange)}",
      ".rt-tabs{display:flex;gap:4px;flex-wrap:wrap;margin-bottom:8px}",
      ".rt-tab{font:inherit;padding:5px 12px;border-radius:var(--radius);border:1px solid var(--line);background:var(--bg3);color:var(--text);cursor:pointer}",
      ".rt-tab:hover{border-color:var(--accent)}.rt-tab.on{background:var(--accent);border-color:var(--accent);color:#1a1408;font-weight:600}",
      ".rt-tab small{opacity:.75;margin-left:6px;font-weight:400}",
      ".rt-legend{display:flex;gap:6px 16px;flex-wrap:wrap;color:var(--dim);font-size:12px;margin-top:8px;align-items:center}",
      ".rt-legend i{display:inline-block;width:26px;height:0;border-top:3px solid;vertical-align:middle;margin-right:5px}",
      ".rt-legend i.dash{border-top-style:dashed}.rt-legend i.dot{border-top-style:dotted;border-top-width:2px}",
      ".rt-legend i.sq{width:11px;height:11px;border:0}",
      ".rt-tickets{width:100%;border-collapse:collapse;margin-top:6px}",
      ".rt-tickets th{text-align:left;color:var(--dim);font-weight:600;font-size:12px;padding:4px 6px;border-bottom:1px solid var(--line)}",
      ".rt-tickets td{padding:5px 6px;border-bottom:1px solid var(--line);vertical-align:top}",
      ".rt-tickets tr.row{cursor:pointer}.rt-tickets tr.row:hover td{background:var(--bg3)}",
      ".rt-tickets tr.on td{background:rgba(212,169,74,.14)}.rt-tickets tr.on td:first-child{box-shadow:inset 3px 0 0 var(--accent)}",
      ".rt-stlist{list-style:none;margin:6px 0 0;padding:0;max-height:470px;overflow:auto}",
      ".rt-stlist li{display:flex;gap:8px;justify-content:space-between;padding:4px 6px;border-top:1px solid var(--line)}",
      ".rt-fac{display:grid;grid-template-columns:repeat(auto-fit,minmax(380px,1fr));gap:12px}",
      ".rt-fac h3{margin:0 0 4px;font-size:15px;color:var(--con-orange)}",
      ".rt-fac p{margin:6px 0}",
      ".rt-card{margin-top:14px}",
      ".vu-btn.on{border-color:var(--accent);color:var(--accent)}",
      ".rt-card>h2{margin:0 0 8px}"
    ].join("\n")}));
  }

  /* ------------------------------------------------------------------ names */
  function place(s) { return s.place || vu.zoneName(s.zone); }
  function full(s) { return s.master ? place(s) + " (" + s.name + ")" : place(s) + " (landing)"; }
  function where(s) { return vu.zoneName(s.zone); }
  function stRealm(s) { var z = vu.zone(s.zone); return s.realm || (z && z.realm) || 0; }
  function ticketName(l) { return l.name || ("Ticket to " + (l.toName || "")); }
  function num(n) { return ui.num ? ui.num(n) : String(n); }

  /* ------------------------------------------------------------------ the page */
  function render(el, params, query) {
    css();
    var me = (live = {maps: []});
    var head = ui.page ? ui.page(el, {title: "Stable routes"}) : el.appendChild(h("h1", "Stable routes"));
    var body = h("div"); el.appendChild(body);
    if (ui.loading) ui.loading(body, "Loading the stable routes...");
    return WG.load("routes", "zones").then(function (d) {
      if (live !== me) return;
      WG.clear(body);
      var data = d.routes, g = (data._vuGraph = data._vuGraph || R.build(data));
      page(body, head, me, data, g, query);
    });
  }

  function page(el, head, me, data, g, query) {
    var meta = data.meta || {};
    function isTicket(l) { return !l.walk && !l.tele; }
    var tickets = g.legs.filter(isTicket);
    var free = tickets.every(function (l) { return !l.price; });
    var delay = +meta.startDelay || 0;
    function id(v) { if (v == null || v === "") return null; var n = +v; return g.byId.has(n) ? n : g.byId.has(v) ? v : null; }
    var S = {from: id(query.from), to: id(query.to), noRep: query.norep === "1", noTele: query.notele === "1", pick: query.pick || "",
             stable: id(query.stable), leg: query.leg || "", net: query.net || "", fac: query.fac || "", plan: null};
    function flags() { return {noRep: S.noRep, tele: !S.noTele}; }
    g.stables.forEach(function (s) {
      if (s._hay) return;
      var z = vu.zone(s.zone), reg = regionName(z);
      s._tk = (g.out.get(s.id) || []).filter(isTicket).length;
      s._hay = (place(s) + " " + s.name + " " + where(s) + " " + WG.REALMS[stRealm(s)] + " " + reg).toLowerCase();
    });
    var sub = h("div.wg-head-sub", num(g.stables.filter(function (s) { return s.master; }).length) + " stable masters and " + num(tickets.length) +
      " tickets, exactly as the server has them, the Shrouded Isles flying routes included. A ticket runs one way. " +
      (free ? "Every ticket is free in this world. " : ""));
    head.appendChild(sub);

    function addr(patch) {
      var s = Object.assign({from: S.from, to: S.to, norep: S.noRep ? "1" : "", notele: S.noTele ? "1" : "", pick: S.pick, net: S.net, stable: S.stable, leg: S.leg, fac: S.fac}, patch || {});
      var q = {}; Object.keys(s).forEach(function (k) { if (s[k] != null && s[k] !== "") q[k] = s[k]; });
      return q;
    }
    function go(patch) { WG.go("routes", addr(patch)); }                 // a new step in the history (Back returns)
    function note() { WG.setQuery(addr()); }                              // the same step, address kept up to date

    /* ---------- planner ---------- */
    var groups = pickerGroups(g);
    var card = h("div.wg-card"), result = h("div");
    var fromP = picker({label: "From", placeholder: "Where you are: town, zone or stable master", value: S.from, groups: groups, g: g,
      onPick: function (v) { focusNext = v != null && S.to == null ? "to" : null; go({from: v, pick: "", stable: "", leg: "", net: ""}); }});
    var toP = picker({label: "To", placeholder: "Where you want to go", value: S.to, groups: groups, g: g,
      onPick: function (v) { focusNext = v != null && S.from == null ? "from" : null; go({to: v, pick: "", stable: "", leg: "", net: S.from == null ? "" : S.net}); }});
    var swap = h("button.vu-btn.rt-swap", {type: "button", title: "Swap From and To", "aria-label": "Swap From and To",
      disabled: S.from == null && S.to == null ? true : null, onclick: function () { go({from: S.to, to: S.from, pick: "", stable: "", leg: ""}); }}, "⇄");
    var repBox = h("input", {type: "checkbox", checked: S.noRep ? true : null, onchange: function () { go({norep: repBox.checked ? "1" : "", pick: ""}); }});
    var teleBox = h("input", {type: "checkbox", checked: S.noTele ? null : true, onchange: function () { go({notele: teleBox.checked ? "" : "1", pick: ""}); }});
    var clear = h("button.vu-btn", {type: "button", style: {padding: "8px 12px"}, disabled: S.from == null && S.to == null && !S.noRep && !S.noTele ? true : null,
      onclick: function () { go({from: "", to: "", norep: "", notele: "", pick: "", stable: "", leg: ""}); }}, "Clear");
    card.appendChild(h("div.rt-plan", fromP.el, swap, toP.el, clear));
    card.appendChild(h("div", {style: {display: "flex", gap: "0 28px", flexWrap: "wrap"}},
      h("label.rt-check", repBox, h("span", "Only routes I can ride with no reputation",
        h("span.wg-dim", " (leaves out the " + num(tickets.filter(function (l) { return l.rep; }).length) + " faction rides)"))),
      g.teleports ? h("label.rt-check", {title: "The teleporter NPCs of the Shrouded Isles send their own realm to four towns of the isle, free. A stable master within " + num(R.TELE_MAX) + " units of one can use it."},
        teleBox, h("span", "Use the Shrouded Isles teleporters", h("span.wg-dim", " (free, they stand beside some stables)"))) : null));
    card.appendChild(result);
    el.appendChild(card);

    /* ---------- network + stable ---------- */
    var nets = networks(g), netBox = h("div"), tabStrip = h("div.rt-tabs", {role: "tablist"}), side = h("div.wg-card.vu-side");
    var netCard = h("div.wg-card", h("h2", {style: {marginTop: 0}}, "The network"), tabStrip, netBox, legend());
    var split = h("div.vu-split.rt-card", {style: {gridTemplateColumns: "minmax(0,1fr) minmax(380px,34%)"}}, netCard, side);
    if (window.matchMedia && window.matchMedia("(max-width: 980px)").matches) split.style.gridTemplateColumns = "minmax(0,1fr)";
    el.appendChild(split);
    var pathsCard = h("div.wg-card.rt-card"); el.appendChild(pathsCard);
    var repSec = reputation(data, g, {fac: S.fac, maps: me.maps, selectStable: function (sid) { selectStable(sid, true); },
      onFac: function (fid) { if (S.fac !== fid) { S.fac = fid; note(); } }});
    el.appendChild(repSec);
    if (g.notes && g.notes.length) el.appendChild(h("div.vu-sub.rt-card", h("div", h("b", "Notes that come with the route data")), (Array.isArray(g.notes) ? g.notes : [g.notes]).map(function (n) { return h("div", n); })));

    var netMap = null, pathMulti = null;

    function currentNet() {
      var key = S.net;
      if (!key || !nets.byKey[key]) {
        var s = S.stable != null ? g.byId.get(S.stable) : S.from != null ? g.byId.get(S.from) : S.to != null ? g.byId.get(S.to) : null;
        key = s ? netKeyOf(s) : (nets.list.filter(function (n) { return n.realm === (WG.me.realm || 1) && !n.housing; })[0] || nets.list[0] || {}).key;
      }
      return nets.byKey[key] || null;
    }

    /* ----- planner result */
    function drawResult() {
      WG.clear(result);
      var a = S.from != null ? g.byId.get(S.from) : null, b = S.to != null ? g.byId.get(S.to) : null;
      S.plan = null;
      if (!a && !b) {
        result.appendChild(h("div.vu-hint", {style: {padding: "14px 0 4px", textAlign: "left"}},
          "Type a town, a zone or a stable master's name in From and To. Or click a stable on the map below and use its Start here and Go here buttons."));
        return;
      }
      if (a && !b) { reachTable(a); return; }
      if (!a && b) { arriveTable(b); return; }
      var res = R.options(g, a.id, b.id, flags());
      if (!res.ok) { result.appendChild(whyBox(res.why, a, b)); return; }
      if (res.options[0].legs.length === 0) { result.appendChild(h("div.rt-why", "From and To are the same stable: you are already there.")); return; }
      var opts = res.options;
      opts.forEach(function (o) { o.labels = o.labels.filter(function (l) { return !(free && l === "price"); }); });
      opts = opts.filter(function (o) { return o.labels.length; });
      var chosen = opts.filter(function (o) { return o.labels.indexOf(S.pick) >= 0; })[0] || opts[0];
      S.plan = chosen;
      var wrap = h("div.rt-opts");
      opts.forEach(function (o) { wrap.appendChild(optionCard(o, o === chosen, opts.length > 1)); });
      result.appendChild(wrap);
      if (opts.length === 1 && !free) result.appendChild(h("div.vu-sub", {style: {marginTop: "6px"}}, "This is the route with the fewest tickets, the cheapest and the fastest at once."));
      if (opts.length === 1 && free) result.appendChild(h("div.vu-sub", {style: {marginTop: "6px"}}, "This route has the fewest tickets and is also the fastest."));
    }
    var LABELS = {tickets: "Fewest tickets", price: "Cheapest", time: "Fastest"};
    function stepsText(o) {
      var t = [];
      if (o.tickets || !o.teleports) t.push(o.tickets + (o.tickets === 1 ? " ticket" : " tickets"));
      if (o.teleports) t.push(o.teleports + (o.teleports === 1 ? " teleporter" : " teleporters"));
      return t.join(" and ");
    }
    function totalLine(o) {
      var parts = [h("b", stepsText(o)), " · ", free || !o.price ? "free" : (ui.money ? ui.money(o.price) : WG.money(o.price))];
      if (o.tickets) parts.push(" · ", o.secondsKnown ? "about " + R.time(o.seconds + delay * o.tickets) + " riding" : "ride time not known for every leg");
      if (o.walk) parts.push(" · ", "about " + num(Math.round(o.walk)) + " units on foot");
      return parts;
    }
    function gapNote(l, b) {
      if (!(l.gap > 2500)) return null;
      return h("div.wg-dim", "The ride stops about " + num(Math.round(l.gap / 100) * 100) + " units from " + (b.master ? b.name : place(b)) + ".");
    }
    function teleStep(l, a, b) {
      var t = l.teleporter || {}, d = l.dest || {};
      return h("div",
        h("div", "At ", stBtn(a), h("span.wg-dim", " · " + where(a))),
        h("div", "walk to the teleporter ", h("b", l.name), " and ask for ", h("b", l.toName || place(b)), " ", h("span.rt-kind.teleport", "Teleporter"), h("span.wg-dim", " free, at once")),
        h("div.wg-dim", "It stands at loc " + Math.round(t.x) + ", " + Math.round(t.y) + " and puts you down in " + vu.zoneName(d.zone) + " at loc " + Math.round(d.x) + ", " + Math.round(d.y) +
          ". About " + num(l.length) + " units on foot in all."));
    }
    function optionCard(o, on, many) {
      var c = h("div.rt-optcard" + (on ? ".on" : ""), {tabIndex: 0, role: "button", "aria-pressed": on ? "true" : "false",
        title: many ? "Show this route on the maps" : null});
      c.appendChild(h("h3", o.labels.map(function (l) { return LABELS[l]; }).join(" and ")));
      var tot = h("div.rt-total", totalLine(o));
      if (o.kinds.flying) tot.appendChild(h("span", " ", h("span.rt-kind.flying", o.kinds.flying + " flying")));
      if (o.rep.length) tot.appendChild(h("span", " ", h("span.rt-kind.rep", "needs reputation")));
      c.appendChild(tot);
      var ol = h("ol.rt-steps"), n = 0;
      o.legs.forEach(function (l) {
        var a = g.byId.get(l.from), b = g.byId.get(l.to);
        if (l.walk) {
          ol.appendChild(h("li.walk", h("div.rt-n", "↝"), h("div", "Walk from ", stBtn(a), " to ", stBtn(b),
            h("span.wg-dim", ", about " + num(l.length) + " units in a straight line. Same town, another stable master."))));
          return;
        }
        n++;
        if (l.tele) { ol.appendChild(h("li", h("div.rt-n", String(n)), teleStep(l, a, b))); return; }
        ol.appendChild(h("li", h("div.rt-n", String(n)), h("div",
          h("div", "At ", stBtn(a), h("span.wg-dim", " · " + where(a))),
          h("div", "buy ", h("b", ticketName(l)), " ", kindChip(l), l.seconds != null ? h("span.wg-dim", " " + R.time(l.seconds + delay)) : null,
            free ? null : [" · ", l.price ? (ui.money ? ui.money(l.price) : WG.money(l.price)) : "free"],
            R.hasReturn(g, l) ? null : [" ", h("span.rt-kind.oneway", {title: "No ticket comes straight back from " + place(b) + " to " + place(a)}, "one way")]),
          l.rep ? h("div.rt-rep", "Needs " + l.rep.text) : null, gapNote(l, b))));
      });
      var last = g.byId.get(o.legs[o.legs.length - 1].to);
      ol.appendChild(h("li.arrive", h("div.rt-n", "✓"), h("div", "Arrive at ", stBtn(last), h("span.wg-dim", " · " + where(last)),
        last.master ? null : h("div.wg-dim", "No stable master stands here: nobody sells a ticket onward."))));
      c.appendChild(ol);
      var hows = {}; o.rep.forEach(function (l) { if (l.rep.how) hows[l.rep.factionName || l.rep.text] = l.rep.how; });
      Object.keys(hows).forEach(function (k) { c.appendChild(h("div.vu-sub", {style: {marginTop: "6px"}}, h("b", k + ": "), hows[k])); });
      function choose(e) {
        if (e && e.target.closest("button,a")) return;
        S.pick = o.labels[0]; S.stable = null; S.leg = ""; S.net = "";
        note(); drawResult(); drawNet(); drawSide(); drawPaths();
      }
      c.addEventListener("click", choose);
      c.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); choose(); } });
      return c;
    }
    function stBtn(s) { return h("button.rt-link", {type: "button", title: "Show this stable and its tickets", onclick: function (e) { e.stopPropagation(); selectStable(s.id, true); }}, full(s)); }
    function kindChip(l) {
      var k = l.walk ? "walk" : l.kind, c = h("span.rt-kind." + (k === "flying" || k === "boat" || k === "walk" || k === "teleport" ? k : "horse"), R.kindName(l));
      return l.rep ? [c, " ", h("span.rt-kind.rep", {title: l.rep.text}, "reputation")] : c;
    }

    function reachTable(a) {
      var reach = R.reach(g, a.id, flags()), rows = [];
      reach.forEach(function (p, sid) { rows.push({s: g.byId.get(sid), p: p}); });
      var net = R.network(g, a.id), missing = [];
      net.forEach(function (sid) { if (sid !== a.id && !reach.has(sid)) missing.push(g.byId.get(sid)); });
      if (!rows.length) {
        var why = (g.out.get(a.id) || []).length ? "Every ride from " + full(a) + " needs reputation. Untick the box above to see them." :
          (a.master ? full(a) + " sells no tickets." : "Nobody sells tickets at " + place(a) + ": it is only a place where a ride ends.");
        result.appendChild(h("div.rt-why", h("b", "Nowhere to go. "), why));
        return;
      }
      result.appendChild(h("div", {style: {margin: "12px 0 6px"}}, h("b", "From " + full(a) + " you can reach " + rows.length + (rows.length === 1 ? " stable" : " stables")),
        h("span.wg-dim", S.noRep ? " with no reputation" : "", ". Choose one to plan the trip.")));
      var box = h("div"); result.appendChild(box);
      ui.table(box, {
        columns: [
          {key: "name", label: "Destination", sort: function (r) { return place(r.s); }, render: function (r) {
            return [h("button.rt-link", {type: "button", onclick: function () { go({to: r.s.id, pick: "", stable: "", leg: ""}); }}, h("b", place(r.s))), h("span.wg-dim", r.s.master ? " " + r.s.name : " landing")]; }},
          {key: "zone", label: "Zone", sort: function (r) { return where(r.s); }, render: function (r) { return where(r.s); }},
          {key: "tickets", label: "Tickets", num: true, width: 150, sort: function (r) { return -(r.p.steps * 100 + r.p.teleports); }, render: function (r) { return r.p.teleports ? stepsText(r.p) : r.p.tickets; }},
          {key: "time", label: "Ride", num: true, width: 110, sort: function (r) { return r.p.secondsKnown ? -r.p.seconds : null; }, render: function (r) { return r.p.secondsKnown ? R.time(r.p.seconds + delay * r.p.tickets) : ""; }},
          {key: "needs", label: "Needs", sort: function (r) { return r.p.rep.length; }, render: function (r) {
            if (!r.p.rep.length) return h("span.wg-dim", "nothing");
            var names = {}; r.p.rep.forEach(function (l) { names[l.rep.factionName || "reputation"] = 1; });
            return h("span.wg-warn", Object.keys(names).join(", ") + " reputation"); }}
        ],
        rows: rows, sortKey: "tickets", sortDir: "desc", pageSize: 0, dense: true, stickyHead: false,
        onRow: function (r) { go({to: r.s.id, pick: "", stable: "", leg: ""}); }
      });
      if (missing.length) result.appendChild(h("div.vu-sub", {style: {marginTop: "6px"}},
        "On the same network but not reachable from here" + (S.noRep ? " without reputation" : "") + ": ",
        missing.map(function (s, i) { return [i ? ", " : "", h("button.rt-link", {type: "button", title: "See why", onclick: function () { go({to: s.id, pick: ""}); }}, place(s))]; })));
    }
    function arriveTable(b) {
      var inn = (g.inn.get(b.id) || []).filter(isTicket);
      result.appendChild(h("div", {style: {margin: "12px 0 6px"}}, h("b", "Now choose where you start. "),
        inn.length ? h("span.wg-dim", "Tickets that end at " + full(b) + " are sold by: ") : h("span.wg-warn", "No ticket ends at " + full(b) + "."),
        inn.map(function (l, i) { var s = g.byId.get(l.from); return [i ? ", " : "", h("button.rt-link", {type: "button", onclick: function () { go({from: s.id, pick: ""}); }}, full(s))]; })));
    }

    function whyBox(w, a, b) {
      var box = h("div.rt-why"), A = full(a), B = full(b);
      function link(text, patch) { return h("button.rt-link", {type: "button", onclick: function () { go(patch); }}, text); }
      box.appendChild(h("b", "No route from " + A + " to " + B + ". "));
      if (w.code === "same") { WG.clear(box); box.appendChild(document.createTextNode("From and To are the same stable: you are already there.")); }
      else if (w.code === "norep") {
        box.appendChild(document.createTextNode("Every way there uses a ride that needs reputation: "));
        w.legs.forEach(function (l, i) {
          box.appendChild(h("div", {style: {margin: "4px 0 0 12px"}}, h("b", full(g.byId.get(l.from)) + " to " + place(g.byId.get(l.to))), ": needs " + l.rep.text + "."));
        });
        box.appendChild(h("div", {style: {marginTop: "8px"}}, link("Show the route that needs reputation", {norep: "", pick: ""})));
      } else if (w.code === "notele") {
        box.appendChild(document.createTextNode("The only way there uses a Shrouded Isles teleporter (" + stepsText(w.plan) + "), and the teleporters are switched off. "));
        box.appendChild(h("div", {style: {marginTop: "8px"}}, link("Show the route with the teleporter", {notele: "", pick: ""})));
      } else if (w.code === "nosale") {
        box.appendChild(document.createTextNode(a.master ? a.name + " at " + place(a) + " sells no tickets, and no other stable master stands close by. "
          : "Nobody sells tickets at " + place(a) + ": it is only a place where a ride ends. "));
      } else if (w.code === "noarrival") {
        box.appendChild(document.createTextNode("No ticket anywhere ends at " + B + ". " + ((g.out.get(b.id) || []).length ? "You can only ride away from it. " : "")));
      } else if (w.code === "separate") {
        var ra = regionName(vu.zone(a.zone)), rb = regionName(vu.zone(b.zone));
        box.appendChild(document.createTextNode(ra !== rb
          ? "They are in different lands (" + ra + " and " + rb + "): no ticket joins the two. "
          : "They are on separate networks inside " + ra + ": " + place(a) + "'s network has " + w.fromNet.size + (w.fromNet.size === 1 ? " stable" : " stables") + ", " + place(b) + "'s has " + w.toNet.size + ", and no ticket joins the two. "));
        if (ra !== rb) gateHints(a, b).forEach(function (n) { box.appendChild(n); });
      } else if (w.code === "oneway") {
        box.appendChild(document.createTextNode(w.back
          ? "The tickets only run the other way: you can ride from " + place(b) + " to " + place(a) + " (" + stepsText(w.back) + "), but nothing leads back. "
          : "The two are joined only through one-way rides that do not lead from " + place(a) + " to " + place(b) + ". "));
        if (w.back) box.appendChild(link("Swap From and To", {from: b.id, to: a.id, pick: ""}));
      } else box.appendChild(document.createTextNode("One of the two stables is not in the data."));
      if (w.code === "separate" || w.code === "oneway" || w.code === "nosale" || w.code === "noarrival") { var br = bridge(a, b); if (br) box.appendChild(br); }
      if (w.code !== "same") box.appendChild(h("div", {style: {marginTop: "8px"}}, link("See everywhere you can go from " + place(a), {to: "", pick: ""})));
      return box;
    }

    /* No ticket joins the two: the best trip with ONE walk between two stables of the same land. Ride from A to P,
       walk P to Q, ride Q to B. Chosen to keep the walk short: the cost is the ride time plus the walk at a player's
       run speed counted WALK_WEIGHT times (on foot you cross monster country, on a horse you do not). The straight
       line is all this tool knows about the ground between the two stables, and the page says so. */
    var WALK_WEIGHT = 5;
    function bridge(a, b) {
      var za = vu.zone(a.zone), zb = vu.zone(b.zone);
      if (!za || !zb || za.region !== zb.region) return null;
      var f = flags(), starts = [{s: a, p: null}], ends = [], best = null;
      R.reach(g, a.id, f).forEach(function (p, sid) { starts.push({s: g.byId.get(sid), p: p}); });
      g.stables.forEach(function (q) {
        if (q.id === b.id) { ends.push({s: q, p: null}); return; }
        var zq = vu.zone(q.zone); if (!zq || zq.region !== za.region) return;
        var p = R.plan(g, q.id, b.id, Object.assign({by: "time"}, f));
        if (p.ok) ends.push({s: q, p: p});
      });
      function secs(p) { return p ? (p.secondsKnown ? p.seconds : p.tickets * 300) + delay * p.tickets + p.walk / R.WALK_SPEED : 0; }
      starts.forEach(function (st) {
        var zs = vu.zone(st.s.zone); if (!zs || zs.region !== za.region) return;
        var ps = vu.regionXY(st.s.zone, st.s.x, st.s.y); if (!ps) return;
        ends.forEach(function (en) {
          if (en.s.id === st.s.id) return;
          var pe = vu.regionXY(en.s.zone, en.s.x, en.s.y); if (!pe) return;
          var d = Math.hypot(ps[0] - pe[0], ps[1] - pe[1]), t = secs(st.p) + WALK_WEIGHT * d / R.WALK_SPEED + secs(en.p);
          if (!best || t < best.t) best = {t: t, d: d, st: st, en: en};
        });
      });
      if (!best) return null;
      var P = best.st.s, Q = best.en.s, parts = [h("b", "With one walk: ")];
      if (best.st.p) parts.push("ride to ", stBtn(P), " (" + stepsText(best.st.p) + "), ");
      parts.push((best.st.p ? "walk" : "Walk") + " about " + num(Math.round(best.d / 100) * 100) + " units from ", best.st.p ? place(P) : stBtn(P), " to ", stBtn(Q),
        h("span.wg-dim", " (" + (P.zone === Q.zone ? where(P) : where(P) + " to " + where(Q)) + ", measured in a straight line)"));
      if (best.en.p) parts.push(", then ride on to " + place(b) + " (" + stepsText(best.en.p) + ")");
      parts.push(". ");
      var links = [];
      if (best.st.p) links.push(h("button.rt-link", {type: "button", onclick: function () { go({from: a.id, to: P.id, pick: ""}); }}, "Plan " + place(a) + " to " + place(P)));
      if (best.en.p) links.push(h("button.rt-link", {type: "button", onclick: function () { go({from: Q.id, to: b.id, pick: ""}); }}, "Plan " + place(Q) + " to " + place(b)));
      return h("div", {style: {marginTop: "8px"}}, parts, links.length ? h("div", {style: {marginTop: "4px", display: "flex", gap: "18px", flexWrap: "wrap"}}, links) : null);
    }
    /* Two different lands: the server's gates (zone points) that lead from one to the other, if any. */
    function gateHints(a, b) {
      var za = vu.zone(a.zone), zb = vu.zone(b.zone), out = [];
      if (!za || !zb) return out;
      var seen = {};
      (data.gates || []).forEach(function (gt) {
        var f = vu.zone(gt.from.zone), t = vu.zone(gt.to.zone);
        if (!f || !t || f.region !== za.region || t.region !== zb.region) return;
        var k = gt.from.zone + ">" + gt.to.zone; if (seen[k]) return; seen[k] = 1;
        if (out.length < 3) out.push(h("div", {style: {marginTop: "6px"}}, "You cross on foot: a gate in ",
          ui.zoneLink ? ui.zoneLink(gt.from.zone, {loc: [gt.from.x, gt.from.y]}) : f.name, " at loc " + gt.from.x + ", " + gt.from.y + " leads to ",
          ui.zoneLink ? ui.zoneLink(gt.to.zone, {loc: [gt.to.x, gt.to.y]}) : t.name, " at loc " + gt.to.x + ", " + gt.to.y + "."));
      });
      return out;
    }

    /* ----- network map */
    function drawTabs(cur) {
      WG.clear(tabStrip);
      nets.list.forEach(function (n) {
        tabStrip.appendChild(h("button.rt-tab" + (n === cur ? ".on" : ""), {type: "button", role: "tab", "aria-selected": n === cur ? "true" : "false",
          onclick: function () { S.net = n.key; note(); drawNet(); if (S.stable == null) drawSide(); }}, n.label, h("small", String(n.stables.length))));
      });
    }
    function drawNet() {
      var net = currentNet();
      drawTabs(net);
      if (!net) { WG.clear(netBox); netBox.appendChild(h("div.vu-hint", "No stables in the data.")); return; }
      if (!WG.Map) { WG.clear(netBox); netBox.appendChild(vu.mapNote()); return; }
      var planLegs = new Set((S.plan ? S.plan.legs : []).map(function (l) { return l.i; }));
      var sel = S.stable, hasFocus = planLegs.size > 0 || sel != null;
      var pins = [], lines = [];
      function rgba(hex, a) { var n = parseInt(hex.slice(1), 16); return "rgba(" + (n >> 16) + "," + ((n >> 8) & 255) + "," + (n & 255) + "," + a + ")"; }
      /* lines: dim ones first, the chosen ones on top */
      var later = [], teleSeen = {};
      net.stables.forEach(function (s) {
        (g.out.get(s.id) || []).forEach(function (l) {
          if (l.tele) {                       // one line per teleporter and town, from the NPC to where it puts you down
            if (S.noTele) return;
            var t = l.teleporter, d = l.dest, key = t.name + ">" + l.toName, hot = planLegs.has(l.i) || (sel != null && l.from === sel && !S.leg);
            if (teleSeen[key] && !hot) return;
            teleSeen[key] = 1;
            var tl = {pts: [net.pt(t.zone, t.x, t.y), net.pt(d.zone, d.x, d.y)], label: "Teleporter " + t.name + " to " + l.toName, dashed: true, arrow: true,
                      color: planLegs.has(l.i) ? COL.plan : hot ? COL.tele : rgba(COL.tele, hasFocus ? 0.25 : 0.7), width: planLegs.has(l.i) ? 4 : hot ? 3 : 1.4};
            if (hot) later.push(tl); else lines.push(tl);
            return;
          }
          var pts = net.line(l, g); if (pts.length < 2) return;
          var b = g.byId.get(l.to);
          var base = l.walk ? COL.walk : l.rep ? COL.rep : (COL[l.kind] || COL.horse);
          var onPlan = planLegs.has(l.i), onSel = sel != null && l.from === sel && !l.walk && (!S.leg || String(l.ticket) === S.leg);
          var lbl = l.walk ? "Walk between " + place(s) + " and " + place(b) : place(s) + " to " + place(b) + (l.rep ? " (needs " + (l.rep.factionName || "reputation") + ")" : "");
          var line = {pts: pts, label: lbl, dashed: l.kind === "flying" || l.walk, arrow: !l.walk,
                      color: onPlan ? COL.plan : onSel ? base : hasFocus ? rgba(base, 0.28) : rgba(base, 0.85), width: onPlan ? 4 : onSel ? 3.2 : l.walk ? 1.5 : 1.8};
          if (onPlan || onSel) later.push(line); else lines.push(line);
        });
      });
      lines = lines.concat(later);
      net.stables.forEach(function (s) {
        var p = net.pin(s.zone, s.x, s.y), tk = s._tk;
        var color = s.id === S.from ? COL.from : s.id === S.to ? COL.to : s.id === sel ? COL.sel : s.rep ? COL.rep : null;
        pins.push({zone: p.zone, x: p.x, y: p.y, kind: s.master ? "stable" : "point", id: s.id, label: place(s), color: color,
          sub: (s.master ? s.name + " · " : "Landing, no stable master · ") + where(s) + "\n" + (tk ? tk + (tk === 1 ? " ticket" : " tickets") : "sells no tickets") + (s.rep ? "\nNeeds " + s.rep.text : "") +
               (s.id === S.from ? "\nYour start" : s.id === S.to ? "\nYour destination" : "")});
      });
      (net.teleporters || []).forEach(function (t) {
        pins.push({zone: t.pos.zone, x: t.pos.x, y: t.pos.y, kind: "point", color: COL.tele, label: "Teleporter " + t.name, sub: "Sends its own realm, free, to: " + t.to.join(", ")});
      });
      var opts = {zone: net.zone, pins: pins, lines: lines};
      if (!netMap) {
        WG.clear(netBox);
        netMap = WG.Map.create(netBox, Object.assign(opts, {height: 700, legend: false, showAll: true,
          onPin: function (pin) { if (pin && pin.id != null && g.byId.has(pin.id)) selectStable(pin.id, false); }}));
        me.maps.push(netMap);
      } else netMap.setData(opts);
    }
    function legend() {
      return h("div.rt-legend",
        h("span", h("i", {style: {borderColor: COL.horse}}), "Horse route"),
        h("span", h("i.dash", {style: {borderColor: COL.flying}}), "Flying route"),
        h("span", h("i", {style: {borderColor: COL.rep}}), "Needs reputation"),
        h("span", h("i.dot", {style: {borderColor: COL.walk}}), "Short walk between two stable masters"),
        g.teleports ? h("span", h("i.dash", {style: {borderColor: COL.tele, borderTopWidth: "2px"}}), "Teleporter (Shrouded Isles)") : null,
        h("span", h("i", {style: {borderColor: COL.plan}}), "Your route"),
        h("span", "An arrow head shows which way a ticket runs. Click a stable for its tickets."));
    }

    /* ----- one stable */
    function selectStable(sid, scroll) {
      S.stable = sid; S.leg = "";
      var s = g.byId.get(sid); if (s && netKeyOf(s) !== (currentNet() || {}).key) S.net = netKeyOf(s);
      note(); drawNet(); drawSide(); drawPaths();
      if (netMap) netMap.focus(sid, {zoom: false, scroll: false});
      if (scroll) { var r = split.getBoundingClientRect(); if (r.top < 0 || r.top > window.innerHeight - 250) split.scrollIntoView({behavior: "smooth", block: "start"}); }
    }
    function drawSide() {
      WG.clear(side);
      var s = S.stable != null ? g.byId.get(S.stable) : null;
      if (!s) {
        var net = currentNet();
        side.appendChild(h("div", h("b", net ? net.label : "Stables"), h("span.wg-dim", ": click a stable on the map or in this list to see its tickets.")));
        var ul = h("ul.rt-stlist");
        (net ? net.stables.slice() : []).sort(function (a, b) { return place(a) < place(b) ? -1 : place(a) > place(b) ? 1 : 0; }).forEach(function (st) {
          ul.appendChild(h("li", h("span", h("button.rt-link", {type: "button", onclick: function () { selectStable(st.id, false); }}, place(st)), h("span.wg-dim", st.master ? " " + st.name : " landing")),
            h("span.wg-dim.wg-nowrap", st._tk ? st._tk + (st._tk === 1 ? " ticket" : " tickets") : "no tickets")));
        });
        side.appendChild(ul);
        return;
      }
      var out = (g.out.get(s.id) || []), tk = out.filter(isTicket), walks = out.filter(function (l) { return l.walk; }), teles = out.filter(function (l) { return l.tele; });
      var inn = (g.inn.get(s.id) || []).filter(isTicket);
      side.appendChild(h("div.vu-head", h("div", {style: {flex: "1"}}, h("h2", place(s)),
        h("div.wg-dim", s.master ? (s.guild || "Stable master") + " " + s.name : "A place where rides end. No stable master stands here.")),
        h("button.vu-btn", {type: "button", title: "Back to the list of stables", onclick: function () { S.stable = null; S.leg = ""; note(); drawNet(); drawSide(); drawPaths(); }}, "Close")));
      side.appendChild(h("div.vu-facts", h("span", ui.realm ? ui.realm(stRealm(s)) : null),
        h("span", ui.zoneLink ? ui.zoneLink(s.zone, {loc: [s.x, s.y]}) : where(s)), vu.locLine(s.zone, s.x, s.y)));
      side.appendChild(h("div", {style: {display: "flex", gap: "8px", flexWrap: "wrap", margin: "4px 0 8px"}},
        h("button.vu-btn", {type: "button", disabled: S.from === s.id ? true : null, onclick: function () { go({from: s.id, to: S.to === s.id ? "" : S.to, pick: ""}); }}, "Start here"),
        h("button.vu-btn", {type: "button", disabled: S.to === s.id ? true : null, onclick: function () { go({to: s.id, from: S.from === s.id ? "" : S.from, pick: ""}); }}, "Go here"),
        s.vendor != null ? h("a.vu-btn", {href: WG.href("vendor/" + s.vendor), style: {textDecoration: "none"}}, "Merchant page") : null));
      if (s.rep) side.appendChild(h("div.rt-rep", {style: {marginBottom: "8px"}}, h("b", "Needs " + s.rep.text + ". "), s.rep.how || "",
        s.rep.faction != null && ((data.factions || {})[s.rep.faction] || {}).killMobs ? h("div", {style: {marginTop: "6px"}},
          h("button.vu-btn", {type: "button", onclick: function () { repSec.open(String(s.rep.faction), true); }}, "What to kill for it")) : null));
      if (!tk.length) side.appendChild(h("div.wg-dim", s.master ? s.name + " sells no tickets." : "Nobody sells tickets here."));
      else {
        var t = h("table.rt-tickets", h("thead", h("tr", h("th", "Ticket to"), h("th", "Kind"), h("th", "Ride"), free ? null : h("th", "Price"))));
        var tb = h("tbody"); t.appendChild(tb);
        tk.forEach(function (l) {
          var b = g.byId.get(l.to), on = S.leg && String(l.ticket) === S.leg;
          var tr = h("tr.row" + (on ? ".on" : ""), {title: "Show only this ride on the maps"},
            h("td", h("button.rt-link", {type: "button", title: "Open " + place(b), onclick: function (e) { e.stopPropagation(); selectStable(b.id, false); }}, h("b", place(b))),
              h("div.vu-sub", ticketName(l), " · arrives in " + where(b) + (b.master ? "" : ", no stable master there")),
              l.rep && !s.rep ? h("div.rt-rep", "Needs " + l.rep.text) : null, l.gap > 2500 ? h("div.vu-sub", "Stops about " + num(Math.round(l.gap / 100) * 100) + " units from " + b.name + ".") : null),
            h("td", kindChip(l), R.hasReturn(g, l) ? null : h("div", {style: {marginTop: "3px"}}, h("span.rt-kind.oneway", {title: "No ticket comes straight back"}, "one way"))),
            h("td.wg-nowrap", l.seconds != null ? R.time(l.seconds + delay) : ""),
            free ? null : h("td.wg-nowrap", l.price ? (ui.money ? ui.money(l.price) : WG.money(l.price)) : "free"));
          var pickLeg = function (e) { if (e && e.target.closest("button,a")) return; S.leg = on ? "" : String(l.ticket); note(); drawNet(); drawSide(); drawPaths(); };
          tr.tabIndex = 0;
          tr.addEventListener("click", pickLeg);
          tr.addEventListener("keydown", function (e) { if ((e.key === "Enter" || e.key === " ") && e.target === tr) { e.preventDefault(); pickLeg(); } });
          tb.appendChild(tr);
        });
        side.appendChild(h("div", h("b", tk.length === 1 ? "One ticket" : tk.length + " tickets"), free ? h("span.wg-dim", ", all free") : null));
        side.appendChild(t);
      }
      walks.forEach(function (l) { var b = g.byId.get(l.to); side.appendChild(h("div", {style: {marginTop: "8px"}}, h("span.rt-kind.walk", "walk"), " ", h("button.rt-link", {type: "button", onclick: function () { selectStable(b.id, false); }}, full(b)),
        h("span.wg-dim", " stands " + num(l.length) + " units away in the same town."))); });
      if (teles.length) {
        var byT = {}; teles.forEach(function (l) { (byT[l.name] = byT[l.name] || []).push(l); });
        Object.keys(byT).forEach(function (name) {
          var t = byT[name][0].teleporter, d0 = Math.round(Math.hypot(t.x - s.x, t.y - s.y));
          side.appendChild(h("div", {style: {marginTop: "8px"}}, h("span.rt-kind.teleport", "teleporter"), " ", h("b", name),
            h("span.wg-dim", " stands " + num(d0) + " units away (loc " + Math.round(t.x) + ", " + Math.round(t.y) + ") and sends you, free, to "),
            byT[name].map(function (l, i) { var b = g.byId.get(l.to); return [i ? ", " : "", h("button.rt-link", {type: "button", onclick: function () { selectStable(b.id, false); }}, full(b))]; }), "."));
        });
      }
      (data.broken || []).filter(function (x) { return x.from === s.id; }).forEach(function (x) {
        side.appendChild(h("div.wg-warn", {style: {marginTop: "8px"}}, (x.name || x.ticket) + " is on the list but does not work: " + (x.why || "the server refuses it") + "."));
      });
      side.appendChild(h("div", {style: {marginTop: "12px"}}, h("b", inn.length ? "Rides that end here come from" : "No ride ends here."),
        inn.length ? h("div", inn.map(function (l, i) { var a = g.byId.get(l.from); return [i ? ", " : "", h("button.rt-link", {type: "button", onclick: function () { selectStable(a.id, false); }}, place(a))]; })) : null));
    }

    /* ----- the rides on the real zone maps */
    function drawPaths() {
      if (pathMulti) { try { pathMulti.destroy(); } catch (e) {} pathMulti = null; }
      WG.clear(pathsCard);
      var s = S.stable != null ? g.byId.get(S.stable) : null, legs, title;
      if (s) {
        legs = (g.out.get(s.id) || []).filter(function (l) { return !S.leg || String(l.ticket) === S.leg; });
        var only = S.leg && legs.length === 1 ? legs[0] : null;
        title = only ? "The ride from " + place(s) + " to " + place(g.byId.get(only.to)) + ", along its real path" : "Every ride from " + place(s) + ", along its real path";
      } else if (S.plan && S.plan.legs.length) { legs = S.plan.legs; title = "Your route on the zone maps, along the real paths"; }
      else { pathsCard.style.display = "none"; return; }
      pathsCard.style.display = "";
      pathsCard.appendChild(h("h2", {style: {marginTop: 0}}, title));
      if (!legs.length) { pathsCard.appendChild(h("div.wg-dim", "No ride starts here.")); return; }
      if (!WG.Map || !WG.Map.multi) { pathsCard.appendChild(vu.mapNote()); return; }
      var groups = [], byZone = new Map(), seenPin = {};
      function group(zid) { var gr = byZone.get(zid); if (!gr) { gr = {zone: zid, pins: [], lines: []}; byZone.set(zid, gr); groups.push(gr); } return gr; }
      function pin(st, role) {
        var k = st.zone + "|" + st.id; if (seenPin[k]) return; seenPin[k] = 1;
        group(st.zone).pins.push({x: st.x, y: st.y, kind: st.master ? "stable" : "point", id: st.id, label: place(st), color: role === "start" ? COL.from : role === "end" ? COL.to : null,
          sub: (st.master ? st.name : "Landing, no stable master") + (role === "start" ? "\nStart" : role === "end" ? "\nArrival" : ""), href: WG.href("routes", {stable: st.id})});
      }
      var keyRow = h("div.rt-legend", {style: {margin: "0 0 8px"}});
      var step = 0;
      legs.forEach(function (l, i) {
        var a = g.byId.get(l.from), b = g.byId.get(l.to), color = l.walk ? COL.walk : LEG_COLORS[(l.walk ? i : step) % LEG_COLORS.length];
        if (!l.walk) step++;
        pin(a, s || i === 0 ? "start" : null);
        (l.path || []).forEach(function (seg) { group(seg.zone); });          // tabs in the order the ride crosses the zones
        pin(b, s ? null : i === legs.length - 1 ? "end" : null);
        if (l.tele) {
          /* on foot to the teleporter, then from where it puts you down to the stable master */
          var t = l.teleporter, d = l.dest;
          group(t.zone).pins.push({x: t.x, y: t.y, kind: "point", color: COL.tele, label: "Teleporter " + t.name, sub: "Ask for " + l.toName});
          group(t.zone).lines.push({pts: [[a.x, a.y], [t.x, t.y]], color: color, dashed: true, width: 2, label: "On foot to " + t.name});
          group(d.zone).pins.push({x: d.x, y: d.y, kind: "point", color: COL.tele, label: "Arrival from " + t.name, sub: l.toName});
          group(d.zone).lines.push({pts: [[d.x, d.y], [b.x, b.y]], color: color, dashed: true, width: 2, arrow: true, label: "On foot to " + place(b)});
          keyRow.appendChild(h("span", h("i.dash", {style: {borderColor: color}}), (s ? "" : step + ". ") + "teleporter to " + (l.toName || place(b))));
          return;
        }
        var label = l.walk ? "Walk to " + place(b) : ticketName(l) + (l.seconds != null ? ", " + R.time(l.seconds + delay) : "");
        var segs = l.path && l.path.length ? l.path : (a.zone === b.zone ? [{zone: a.zone, pts: [[a.x, a.y], [b.x, b.y]]}] : []);
        segs.forEach(function (seg, k) { group(seg.zone).lines.push({pts: seg.pts, color: color, label: label, dashed: l.kind === "flying" || l.walk, width: l.walk ? 2 : 3, arrow: k === segs.length - 1 && !l.walk}); });
        keyRow.appendChild(h("span", h("i" + (l.kind === "flying" || l.walk ? ".dash" : ""), {style: {borderColor: color}}), (s || l.walk ? "" : step + ". ") + (l.walk ? "walk to " : "to ") + place(b)));
      });
      pathsCard.appendChild(keyRow);
      var box = h("div"); pathsCard.appendChild(box);
      pathMulti = WG.Map.multi(box, groups, {height: 560, order: "given", legend: false, showAll: true});
    }

    drawResult(); drawNet(); drawSide(); drawPaths();
    me.maps.push({destroy: function () { if (pathMulti) pathMulti.destroy(); }});
    if (S.stable != null && netMap) netMap.focus(S.stable, {zoom: false, scroll: false});
    if (focusNext === "to") toP.input.focus(); else if (focusNext === "from") fromP.input.focus();
    focusNext = null;
  }

  /* ------------------------------------------------------------------ stable picker (a small combo box) */
  function pickerGroups(g) {
    var by = {}, list = [];
    g.stables.forEach(function (s) {
      var z = vu.zone(s.zone), realm = stRealm(s), key = realm + "|" + s.zone;
      if (!by[key]) { by[key] = {realm: realm, zone: where(s), label: (realm ? WG.REALMS[realm] : "No realm") + " · " + where(s), region: z ? z.region : 0, stables: []}; list.push(by[key]); }
      by[key].stables.push(s);
    });
    var mine = WG.me.realm || 0;
    list.sort(function (a, b) {
      var ra = a.realm === mine ? -1 : a.realm || 9, rb = b.realm === mine ? -1 : b.realm || 9;
      return (ra - rb) || (a.region - b.region) || (a.zone < b.zone ? -1 : a.zone > b.zone ? 1 : 0);
    });
    list.forEach(function (gr) { gr.stables.sort(function (a, b) { return place(a) < place(b) ? -1 : place(a) > place(b) ? 1 : 0; }); });
    return list;
  }
  function picker(o) {
    var cur = o.value != null ? o.g.byId.get(o.value) : null;
    var input = h("input.rt-in", {type: "text", role: "combobox", "aria-expanded": "false", "aria-label": o.label, autocomplete: "off", spellcheck: false, placeholder: o.placeholder, value: cur ? full(cur) : ""});
    var list = h("div.rt-list", {role: "listbox"});
    var x = h("button.rt-x", {type: "button", tabIndex: -1, title: "Clear " + o.label, "aria-label": "Clear " + o.label, style: {display: cur ? "" : "none"}}, "×");
    var root = h("div.rt-pick", h("label.rt-lab", o.label), h("div.rt-box", input, x, list));
    var items = [], active = -1, open = false;
    function setActive(i, scroll) {
      if (items[active]) items[active].el.classList.remove("on");
      active = i;
      if (items[active]) { items[active].el.classList.add("on"); if (scroll) items[active].el.scrollIntoView({block: "nearest"}); }
    }
    function build(q) {
      var words = q.toLowerCase().split(/\s+/).filter(Boolean);
      WG.clear(list); items = []; active = -1;
      o.groups.forEach(function (gr) {
        var match = gr.stables.filter(function (s) { return words.every(function (w) { return s._hay.indexOf(w) >= 0; }); });
        if (!match.length) return;
        list.appendChild(h("div.rt-group", gr.label));
        match.forEach(function (s) {
          var el = h("div.rt-opt", {role: "option"}, h("b", place(s)), h("span.wg-dim", s.master ? s.name : "landing"),
            h("span.rt-tk", s._tk ? s._tk + (s._tk === 1 ? " ticket" : " tickets") : "no tickets"));
          var it = {el: el, s: s};
          el.addEventListener("mousedown", function (e) { e.preventDefault(); choose(s); });
          el.addEventListener("mousemove", function () { if (items[active] !== it) setActive(items.indexOf(it), false); });
          items.push(it); list.appendChild(el);
        });
      });
      if (!items.length) list.appendChild(h("div.rt-none", "No stable matches \"" + q + "\". Try part of a town, a zone or a stable master's name."));
      var at = -1;
      if (!words.length && cur) items.forEach(function (it, i) { if (it.s === cur) at = i; });
      setActive(at >= 0 ? at : (words.length && items.length ? 0 : -1), true);
    }
    function show(q) { build(q); open = true; list.classList.add("open"); input.setAttribute("aria-expanded", "true"); }
    function hide() { open = false; list.classList.remove("open"); input.setAttribute("aria-expanded", "false"); }
    function choose(s) { hide(); input.value = s ? full(s) : ""; o.onPick(s ? s.id : ""); }
    input.addEventListener("focus", function () { input.select(); show(""); });
    input.addEventListener("click", function () { if (!open) show(""); });
    input.addEventListener("input", function () { show(input.value); });
    input.addEventListener("blur", function () { hide(); input.value = cur ? full(cur) : ""; });
    input.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown") { e.preventDefault(); if (!open) show(""); else if (items.length) setActive((active + 1) % items.length, true); }
      else if (e.key === "ArrowUp") { e.preventDefault(); if (open && items.length) setActive((active - 1 + items.length) % items.length, true); }
      else if (e.key === "Enter") { if (open && items[active]) { e.preventDefault(); choose(items[active].s); } }
      else if (e.key === "Escape") { if (open) { e.preventDefault(); hide(); input.value = cur ? full(cur) : ""; input.select(); } }
    });
    x.addEventListener("mousedown", function (e) { e.preventDefault(); });
    x.addEventListener("click", function () { choose(null); });
    return {el: root, input: input};
  }

  /* ------------------------------------------------------------------ networks: one per region that has stables */
  function regionName(z) {
    if (!z) return "an unknown land";
    var d = WG.data.zones || {}, regs = d.regions || [];
    for (var i = 0; i < regs.length; i++) if (regs[i].id === z.region) return regs[i].name || ("Region " + z.region);
    return "Region " + z.region;
  }
  function netKeyOf(s) { var z = vu.zone(s.zone); return z ? String(z.region) : "0"; }
  function networks(g) {
    var d = WG.data.zones || {}, ovs = {}, byKey = {}, list = [];
    (d.overviews || []).forEach(function (o) { ovs[o.id] = o; });
    g.stables.forEach(function (s) {
      var key = netKeyOf(s), z = vu.zone(s.zone);
      if (!z) return;
      if (!byKey[key]) { byKey[key] = makeNet(key, z, ovs[z.region] || null); list.push(byKey[key]); }
      byKey[key].stables.push(s);
    });
    var mine = WG.me.realm || 0;
    list.sort(function (a, b) {
      var ra = a.realm === mine ? -1 : a.realm || 9, rb = b.realm === mine ? -1 : b.realm || 9;
      return (ra - rb) || ((a.housing ? 2 : a.si ? 1 : 0) - (b.housing ? 2 : b.si ? 1 : 0)) || (b.stables.length - a.stables.length);
    });
    /* the Shrouded Isles teleporters, as plain pins on their network (data/routes.js `teleporters`) */
    ((WG.data.routes || {}).teleporters || []).forEach(function (t) {
      var z = vu.zone(t.zone), n = z && byKey[String(z.region)];
      if (n) n.teleporters.push({name: t.name, pos: n.pin(t.zone, t.x, t.y), to: (t.to || []).map(function (x) { return x.name; })});
    });
    return {list: list, byKey: byKey};
  }
  function makeNet(key, z, ov) {
    var d = WG.data.zones || {}, x0, y0, w, hh, zoneObj;
    if (ov && ov.rect) { x0 = ov.rect[0]; y0 = ov.rect[1]; w = ov.rect[2]; hh = ov.rect[3]; }
    else {                                   // no overview picture: a plain sheet as large as the region's zones
      var x1 = -Infinity, y1 = -Infinity; x0 = Infinity; y0 = Infinity;
      vu.zones().forEach(function (o) { if (o.region !== z.region || o.ox == null) return; x0 = Math.min(x0, o.ox); y0 = Math.min(y0, o.oy); x1 = Math.max(x1, o.ox + o.w); y1 = Math.max(y1, o.oy + o.h); });
      if (x0 === Infinity) { x0 = 0; y0 = 0; x1 = 65536; y1 = 65536; }
      w = x1 - x0; hh = y1 - y0;
    }
    var name = ov ? ov.name : regionName(z), realm = (ov && ov.realm) || z.realm || 0;
    var si = (ov ? ov.expansion : z.expansion) === "si", housing = /housing/i.test(name) || z.kind === "housing";
    var label = realm && name.indexOf(WG.REALMS[realm]) !== 0 ? WG.REALMS[realm] + ": " + name + (si ? " (Shrouded Isles)" : "") : name;
    /* With an overview picture the shared viewer places zone positions itself (zone: "r<region>", pins and line
       points carry their zone). Without one: a plain sheet in region units, positions worked out here. */
    zoneObj = ov ? "r" + ov.id : {id: "region-" + key, name: label, realm: realm, kind: "overview", w: w, h: hh, map: null, px: [0, 0], rect: [0, 0, w, hh]};
    var net = {key: key, label: label, realm: realm, si: si, housing: housing, zone: zoneObj, stables: [], teleporters: []};
    function local(zid, x, y) { var p = vu.regionXY(zid, x, y); return p ? [p[0] - x0, p[1] - y0] : [0, 0]; }
    net.pin = function (zid, x, y) { if (ov) return {zone: zid, x: x, y: y}; var p = local(zid, x, y); return {x: p[0], y: p[1]}; };
    function pt(zid, x, y) { return ov ? [x, y, zid] : local(zid, x, y); }
    net.pt = pt;
    net.line = function (l, g) {
      var pts = [];
      (l.path || []).forEach(function (seg) { var zz = vu.zone(seg.zone); if (!zz || zz.region !== z.region) return; seg.pts.forEach(function (p) { pts.push(pt(seg.zone, p[0], p[1])); }); });
      if (pts.length < 2) {
        var a = g.byId.get(l.from), b = g.byId.get(l.to), za = vu.zone(a.zone), zb = vu.zone(b.zone);
        if (!za || !zb || za.region !== z.region || zb.region !== z.region) return [];
        pts = [pt(a.zone, a.x, a.y), pt(b.zone, b.x, b.y)];
      }
      return pts;
    };
    return net;
  }

  /* ------------------------------------------------------------------ reputation: who asks, how it is earned
     ctx: {selectStable(id), fac: faction id open at start, onFac(id or ""), maps: array the page destroys on leave}
     Returns the section; section.open(factionId, scroll) shows that faction's "what to kill" list and map. */
  function reputation(data, g, ctx) {
    var facs = data.factions || {}, ids = Object.keys(facs);
    var sec = h("div.wg-card.rt-card", h("h2", {style: {marginTop: 0}}, "Reputation"));
    sec.open = function () {};
    var locked = g.legs.filter(function (l) { return l.rep && !l.walk && !l.tele; });
    if (!ids.length && !locked.length) { sec.appendChild(h("div.wg-dim", "No stable master in this world asks for reputation.")); return sec; }
    sec.appendChild(h("div.wg-dim", {style: {marginBottom: "10px"}}, num(locked.length) + " rides are sold by faction stable masters. They refuse you until the faction trusts you. Realm stable masters never ask."));
    var grid = h("div.rt-fac"), killBox = h("div"); sec.appendChild(grid); sec.appendChild(killBox);
    function stLinks(list) {
      return (list || []).map(function (sid, i) { var s = g.byId.get(sid); return s ? [i ? ", " : "", h("button.rt-link", {type: "button", onclick: function () { ctx.selectStable(s.id); }}, full(s))] : null; });
    }
    if (!ids.length) {
      var seen = {};
      locked.forEach(function (l) { var k = l.rep.factionName || l.rep.text; if (seen[k]) return; seen[k] = 1; grid.appendChild(h("div", h("h3", k), h("p", "Needs " + l.rep.text + "."), l.rep.how ? h("p", l.rep.how) : null)); });
      return sec;
    }
    var buttons = {};
    ids.forEach(function (fid) {
      var f = facs[fid], c = h("div", h("h3", f.name || ("Faction " + fid)));
      var sample = locked.filter(function (l) { return String(l.rep.faction) === String(fid); })[0];
      var text = sample ? sample.rep.text : (f.need != null ? "reputation " + f.need + " or better" : "");
      var how = sample ? sample.rep.how : "";
      if (text) c.appendChild(h("p", h("b", "Needs: "), text + "."));
      if (f.stables && f.stables.length) c.appendChild(h("p", h("b", "Asked by: "), stLinks(f.stables)));
      if (f.openStables && f.openStables.length) c.appendChild(h("p", h("b", "Same roads, no reputation asked: "), stLinks(f.openStables)));
      if (how) c.appendChild(h("p", h("b", "How to earn it: "), how));
      var e = f.emissary, off = f.emissaryOff;
      if (e) c.appendChild(h("p", h("b", "Emissary: "), e.name + (e.title ? ", " + e.title : "") + ", ",
        ui.zoneLink ? ui.zoneLink(e.zone, {loc: [e.x, e.y]}) : vu.zoneName(e.zone), " ", vu.locLine(e.zone, e.x, e.y)));
      else if (off && off.name && how.indexOf(off.name) < 0) c.appendChild(h("p.wg-warn", "The stable master's refusal tells you to speak with " + off.name +
        (off.host ? ", beside " + off.host : "") + (off.town ? " in " + off.town : "") + ". That NPC does not exist in True 1.65."));
      if (f.killsHelp && f.killsHelp.length && how.indexOf(f.killsHelp[0]) < 0) c.appendChild(h("p", h("b", "Killing these helps: "), f.killsHelp.join(", ") + "."));
      if (f.killsHurt && f.killsHurt.length) c.appendChild(h("p", h("b", "Killing these hurts: "), f.killsHurt.join(", ") + "."));
      if (f.hunts && f.hunts.length) {
        var hunts = f.hunts.slice().sort(function (a, b) { return (a.levelMin || a.level || 0) - (b.levelMin || b.level || 0); });
        c.appendChild(h("p", h("b", "The emissary's hunts (" + hunts.length + "): "),
          hunts.map(function (m, i) {
            var lv = m.levelMin != null && m.levelMax != null && m.levelMin !== m.levelMax ? m.levelMin + " to " + m.levelMax : (m.level != null ? m.level : m.levelMin);
            return [i ? ", " : "", h("a", {href: WG.href("search", {q: m.name}), title: vu.zoneName(m.zone) + (m.n ? ", " + m.n + " spawns" : "")}, m.name), h("span.wg-dim", " " + lv)];
          })));
      }
      if (f.killMobs && f.killMobs.length) {
        buttons[fid] = h("button.vu-btn", {type: "button", "aria-expanded": "false", onclick: function () { sec.open(open === fid ? "" : fid, false); }}, "What to kill: " + num(f.killMobs.length) + " monsters, with a map");
        c.appendChild(h("p", buttons[fid]));
      }
      grid.appendChild(c);
    });

    /* ----- what to kill: a table (nearest my level first) and the zone maps with a pin per monster and zone */
    var open = "", multi = null;
    function closeMap() { if (multi) { var i = ctx.maps.indexOf(multi); if (i >= 0) ctx.maps.splice(i, 1); try { multi.destroy(); } catch (x) {} multi = null; } }
    sec.open = function (fid, scroll) {
      fid = fid != null && facs[fid] && facs[fid].killMobs && facs[fid].killMobs.length ? String(fid) : "";
      open = fid; closeMap(); WG.clear(killBox);
      Object.keys(buttons).forEach(function (k) { buttons[k].setAttribute("aria-expanded", k === fid ? "true" : "false"); buttons[k].classList.toggle("on", k === fid); });
      if (ctx.onFac) ctx.onFac(fid);
      if (!fid) return;
      var f = facs[fid], my = Math.max(1, WG.me.level || 1);
      var rows = f.killMobs.map(function (m, i) { return {m: m, pid: "k" + i, fit: my < m.levelMin ? m.levelMin - my : my > m.levelMax ? my - m.levelMax : 0}; });
      rows.sort(function (a, b) { return (a.fit - b.fit) || (b.m.n - a.m.n); });
      var chance = f.killChance != null ? Math.round(f.killChance * 100) : null;
      killBox.appendChild(h("div", {style: {borderTop: "1px solid var(--line)", marginTop: "14px", paddingTop: "12px"}},
        h("div.vu-head", h("div", {style: {flex: "1"}}, h("h2", "What to kill for " + f.name),
          h("div.wg-dim", (chance != null ? "Each kill has a " + chance + "% chance of +1 reputation" : "Each kill can give +1 reputation") +
            (f.killsNeeded ? ": about " + num(f.killsNeeded) + " kills from a new character" + (f.killPoints ? " (" + f.killPoints + " points to earn)" : "") + ", more or fewer as the dice fall" : "") +
            ". Nearest your level (" + my + ") first. Whether a monster that is grey to you still counts is not established.")),
          h("button.vu-btn", {type: "button", onclick: function () { sec.open("", false); }}, "Close"))));
      var split = h("div.vu-split"), left = h("div"), right = h("div");
      WG.append(split, [left, right]); killBox.appendChild(split);
      var kinds = null;
      function mobHref(name) {
        var k = kinds && kinds.get(String(name).toLowerCase());
        return k ? WG.href("mob/" + k.id) : WG.href("search", {q: name});
      }
      function draw() {
        var groups = [], byZone = new Map();
        rows.forEach(function (r) {
          var m = r.m, gr = byZone.get(m.zone);
          if (!gr) { gr = {zone: m.zone, pins: []}; byZone.set(m.zone, gr); groups.push(gr); }
          gr.pins.push({x: m.x, y: m.y, kind: "camp", n: m.n, id: r.pid, label: m.name, color: ui.conName ? (ui.conName(m.levelMax, my) || null) : null,
            sub: "Level " + (m.levelMin === m.levelMax ? m.levelMin : m.levelMin + " to " + m.levelMax) + " · " + m.n + (m.n === 1 ? " spawn" : " spawns") + (m.faction ? "\n" + m.faction : ""), href: mobHref(m.name)});
        });
        closeMap();
        if (WG.Map && WG.Map.multi) { multi = WG.Map.multi(right, groups, {height: 520, legend: false, order: "given"}); ctx.maps.push(multi); }   // tabs in the table's order: nearest my level first
        else { WG.clear(right); right.appendChild(vu.mapNote()); }
        ui.table(left, {
          columns: [
            {key: "name", label: "Monster", sort: function (r) { return r.m.name; }, render: function (r) { return h("a", {href: mobHref(r.m.name)}, r.m.name); }},
            {key: "level", label: "Level", num: true, align: "left", width: 90, sort: function (r) { return r.m.levelMin; }, render: function (r) { return ui.level ? ui.level(r.m.levelMin, r.m.levelMax, my) : r.m.levelMin + " to " + r.m.levelMax; }},
            {key: "zone", label: "Zone", sort: function (r) { return vu.zoneName(r.m.zone); }, render: function (r) { return vu.zoneName(r.m.zone); }},
            {key: "n", label: "Spawns", num: true, width: 80, sort: function (r) { return r.m.n; }, render: function (r) { return r.m.n; }},
            {key: "faction", label: "Faction", sort: function (r) { return r.m.faction; }, render: function (r) { return h("span.wg-dim", r.m.faction || ""); }},
            {key: "map", label: "", sort: false, width: 64, render: function (r) { return h("button.vu-pick", {type: "button", onclick: function () { show(r); }}, "Map"); }}
          ],
          rows: rows, pageSize: 15, dense: true, stickyHead: false, count: "monsters", onRow: show
        });
      }
      function show(r) { if (!multi) return; multi.show(r.m.zone); multi.map.focus(r.pid, {scroll: false}); }
      draw();
      /* monster pages instead of a search, once data/mobs.js is in memory */
      WG.load("mobs").then(function (d) {
        if (open !== fid) return;
        var list = d.mobs && d.mobs.kinds; list = list && list.cols ? WG.rows(list) : list;
        if (!Array.isArray(list)) return;
        kinds = new Map(); list.forEach(function (k) { kinds.set(k.key != null ? k.key : String(k.name).toLowerCase(), k); });
        draw();
      }, function () {});
      if (scroll) killBox.scrollIntoView({behavior: "smooth", block: "start"});
    };
    if (ctx.fac) sec.open(ctx.fac, false);
    return sec;
  }

  WG.view("routes", {title: "Stable routes", nav: true, order: 70, render: render, leave: function () { if (live) vu.destroyMaps(live); live = null; }});
})();
