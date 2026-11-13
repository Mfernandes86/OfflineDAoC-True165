/* OFFLINEDAOC Database - stable route graph and search (V-U). Pure functions, no DOM, no page state.

   WG.routes.build(routesData, opts)        -> graph            (routesData = data/routes.js as V-D writes it)
   WG.routes.plan(graph, from, to, opts)    -> {ok, legs, tickets, teleports, price, seconds, ...} or {ok: false}
   WG.routes.options(graph, from, to, opts) -> {ok, options: [...]} or {ok: false, why: {...}}
   WG.routes.reach(graph, from, opts)       -> Map stable id -> the fewest-steps answer for it
   WG.routes.why(graph, from, to, opts)     -> why there is no route (a code and the facts, the page writes the words)
   WG.routes.hasReturn(graph, leg)          -> is there a ticket straight back (false = one way)
   WG.routes.time(seconds)                  -> "4 min 10 s"

   opts: {by: "tickets" | "price" | "time", noRep: bool, walk: bool (default true), tele: bool (default true)}

   A leg is one ticket: it runs ONE WAY, from the stable master that sells it to where the ride ends. A ride back
   exists only when the other stable sells its own ticket. A leg with `rep` needs reputation with a faction.

   Two kinds of leg are not tickets:
   - "walk": two stable masters of one town (the same `town` in the data; without that field: the same zone and at
     most WALK_MAX game units apart in a straight line) are joined both ways, so a trip can change from one to
     the other. A walk is not counted as a step.
   - "teleport": the Shrouded Isles teleporter NPCs (data/routes.js `teleporters`). A stable master that stands
     within TELE_MAX units of a teleporter is joined, one way, to every stable master within TELE_MAX units of a
     place that teleporter sends you. Free and instant; each counts as one step, like a ticket. */
(function () {
  "use strict";
  var WG = (window.WG = window.WG || {});
  var R = (WG.routes = {});

  R.WALK_MAX = 2500;        // game units; the four same-town pairs in the True 1.65 world are 1,364 to 2,243 apart
  R.TELE_MAX = 3000;        // how far a stable master may stand from a teleporter, or from where it puts you down
  R.WALK_SPEED = 191;       // a player's base run speed in units per second (only used to order answers by time)

  function rowsOf(t) {
    if (!t) return [];
    if (Array.isArray(t)) return t;
    if (t.cols && t.rows) {
      var cols = t.cols, n = cols.length;
      return t.rows.map(function (r) { var o = {}; for (var i = 0; i < n; i++) o[cols[i]] = r[i]; return o; });
    }
    return [];
  }
  function pick(o) { for (var i = 1; i < arguments.length; i++) { var v = o[arguments[i]]; if (v !== undefined && v !== null) return v; } return null; }
  function num(v) { return v == null || v === "" || isNaN(v) ? null : +v; }

  /* reputation: null, a string, or an object -> {text, how, factionName, faction, need, emissary} or null */
  function normRep(r) {
    if (r == null || r === false || r === "" || r === 0) return null;
    if (typeof r === "string") return {text: r, how: "", factionName: "", faction: null, need: null, emissary: null};
    var name = pick(r, "factionName", "name", "faction_name") || "";
    var need = num(pick(r, "need", "aggro", "required"));
    var text = pick(r, "text", "label", "requirement");
    if (!text) text = (name ? name + " reputation" : "Reputation") + (need != null ? " " + need + " or better" : " needed");
    return {text: String(text), how: String(pick(r, "how", "earn", "earned") || ""), factionName: String(name),
            faction: pick(r, "faction", "factionId", "id"), need: need, emissary: pick(r, "emissary") || null, raw: r};
  }

  /* Build the graph. Unknown fields are kept on the stable / leg objects untouched (the page may show them). */
  R.build = function (data, opts) {
    opts = opts || {};
    data = data || {};
    var g = {stables: [], byId: new Map(), legs: [], out: new Map(), inn: new Map(), broken: rowsOf(data.broken),
             notes: data.notes || "", walks: 0, teleports: 0, dropped: 0};
    rowsOf(data.stables).forEach(function (s) {
      var id = pick(s, "id");
      if (id == null || g.byId.has(id)) return;
      var name = typeof s.name === "string" ? s.name : "";
      var st = Object.assign({}, s, {
        id: id, name: name,
        place: String(pick(s, "place", "label") || ""), zone: pick(s, "zone"), x: num(pick(s, "x")) || 0,
        y: num(pick(s, "y")) || 0, realm: num(pick(s, "realm")) || 0, vendor: pick(s, "vendor", "merchant"),
        master: !(s.master === false || s.master === 0 || name === ""),   // false = an arrival point, nobody sells tickets
        rep: normRep(pick(s, "reputation", "rep"))
      });
      g.stables.push(st); g.byId.set(id, st); g.out.set(id, []); g.inn.set(id, []);
    });
    function add(leg) { leg.i = g.legs.length; g.legs.push(leg); g.out.get(leg.from).push(leg); g.inn.get(leg.to).push(leg); }
    rowsOf(data.routes).forEach(function (r) {
      var from = pick(r, "from"), to = pick(r, "to");
      if (!g.byId.has(from) || !g.byId.has(to) || from === to) { g.dropped++; return; }
      var rep = normRep(pick(r, "reputation", "rep")) || g.byId.get(from).rep;
      add(Object.assign({}, r, {
        from: from, to: to, price: num(pick(r, "price")) || 0, ticket: pick(r, "ticket", "item"),
        name: String(pick(r, "name", "ticketName") || ""), kind: String(pick(r, "kind") || "horse"),
        mount: String(pick(r, "mount") || ""), realm: num(pick(r, "realm")) || 0, rep: rep,
        seconds: num(pick(r, "seconds", "time")), length: num(pick(r, "length", "dist")), path: pick(r, "path") || [],
        gap: num(pick(r, "gap")), walk: false, tele: false
      }));
    });
    if (opts.walk !== false) {
      var max = opts.walkMax || R.WALK_MAX, masters = g.stables;
      for (var i = 0; i < masters.length; i++) for (var j = i + 1; j < masters.length; j++) {
        var a = masters[i], b = masters[j], same = false, d = 0;
        var townA = a.town != null && a.town !== "", townB = b.town != null && b.town !== "";
        if (townA && townB) { same = a.town === b.town; d = same && a.zone === b.zone ? Math.hypot(a.x - b.x, a.y - b.y) : 0; }
        else if (a.zone != null && a.zone === b.zone) { d = Math.hypot(a.x - b.x, a.y - b.y); same = d <= max; }
        if (!same) continue;
        d = Math.round(d);
        [[a, b], [b, a]].forEach(function (p) {
          add({from: p[0].id, to: p[1].id, price: 0, ticket: null, name: "", kind: "walk", mount: "", realm: 0, rep: null,
               seconds: d / R.WALK_SPEED, length: d, walk: true, tele: false,
               path: p[0].zone === p[1].zone ? [{zone: p[0].zone, pts: [[p[0].x, p[0].y], [p[1].x, p[1].y]]}] : []});
        });
        g.walks++;
      }
    }
    if (opts.tele !== false) {
      var tmax = opts.teleMax || R.TELE_MAX;
      var near = function (zone, x, y) {
        return g.stables.map(function (st) { return {s: st, d: st.zone === zone ? Math.hypot(st.x - x, st.y - y) : Infinity}; })
          .filter(function (e) { return e.d <= tmax; });
      };
      rowsOf(data.teleporters).forEach(function (t) {
        var starts = near(t.zone, t.x, t.y);
        if (!starts.length) return;
        rowsOf(t.to).forEach(function (dst) {
          if (dst.zone === t.zone && Math.hypot(dst.x - t.x, dst.y - t.y) <= tmax) return;      // the town it stands in
          near(dst.zone, dst.x, dst.y).forEach(function (e) {
            starts.forEach(function (st) {
              if (st.s.id === e.s.id) return;
              if (g.out.get(st.s.id).some(function (l) { return l.tele && l.to === e.s.id; })) return;
              var walk = Math.round(st.d + e.d);
              add({from: st.s.id, to: e.s.id, price: 0, ticket: null, name: String(t.name || "Teleporter"), toName: String(dst.name || ""),
                   kind: "teleport", mount: "", realm: num(t.realm) || 0, rep: null, seconds: walk / R.WALK_SPEED, length: walk,
                   walk: false, tele: true, teleporter: t, dest: dst, path: []});
              g.teleports++;
            });
          });
        });
      });
    }
    return g;
  };

  function usable(leg, opts) {
    if (leg.walk && opts.walk === false) return false;
    if (leg.tele && opts.tele === false) return false;
    if (leg.rep && opts.noRep) return false;
    return true;
  }
  /* cost of one leg as a tuple compared left to right: steps (tickets and teleports), price, legs with no known
     time, seconds, units on foot */
  function legCost(leg, by) {
    var t = leg.walk ? 0 : 1, p = leg.price, unknown = leg.seconds == null ? 1 : 0, s = leg.seconds || 0, w = leg.walk || leg.tele ? leg.length || 1 : 0;
    if (by === "price") return [p, t, unknown, s, w];
    if (by === "time") return [unknown, s, t, p, w];
    return [t, p, unknown, s, w];
  }
  function addCost(a, b) { var o = new Array(a.length); for (var i = 0; i < a.length; i++) o[i] = a[i] + b[i]; return o; }
  function less(a, b) { for (var i = 0; i < a.length; i++) { if (a[i] < b[i] - 1e-9) return true; if (a[i] > b[i] + 1e-9) return false; } return false; }

  /* Dijkstra over tuple costs (all parts are >= 0). The network is small (about a hundred stables), so the plain
     O(n^2) form is used: no heap, no surprises. Returns {dist: Map, prev: Map id -> leg}. */
  function search(g, from, opts, stopAt) {
    var by = opts.by || "tickets", dist = new Map(), prev = new Map(), done = new Set();
    if (!g.byId.has(from)) return {dist: dist, prev: prev};
    dist.set(from, [0, 0, 0, 0, 0]);
    for (;;) {
      var best = null, bestId = null;
      dist.forEach(function (c, id) { if (!done.has(id) && (best === null || less(c, best))) { best = c; bestId = id; } });
      if (bestId === null) break;
      done.add(bestId);
      if (stopAt !== undefined && bestId === stopAt) break;
      var out = g.out.get(bestId) || [];
      for (var i = 0; i < out.length; i++) {
        var leg = out[i];
        if (done.has(leg.to) || !usable(leg, opts)) continue;
        var c = addCost(best, legCost(leg, by)), cur = dist.get(leg.to);
        if (!cur || less(c, cur)) { dist.set(leg.to, c); prev.set(leg.to, leg); }
      }
    }
    return {dist: dist, prev: prev};
  }
  function trace(prev, from, to) {
    var legs = [], at = to, guard = 0;
    while (at !== from) { var leg = prev.get(at); if (!leg || guard++ > 10000) return null; legs.unshift(leg); at = leg.from; }
    return legs;
  }
  function summary(legs) {
    var o = {ok: true, legs: legs, tickets: 0, teleports: 0, price: 0, seconds: 0, secondsKnown: true, walk: 0, rep: [], kinds: {}};
    legs.forEach(function (l) {
      if (l.walk) { o.walk += l.length || 0; return; }
      if (l.tele) { o.teleports++; o.walk += l.length || 0; return; }
      o.tickets++; o.price += l.price; o.kinds[l.kind] = (o.kinds[l.kind] || 0) + 1;
      if (l.seconds == null) o.secondsKnown = false; else o.seconds += l.seconds;
      if (l.rep) o.rep.push(l);
    });
    o.steps = o.tickets + o.teleports;
    o.key = legs.map(function (l) { return l.i; }).join(",");
    return o;
  }
  function flags(opts) { return {noRep: !!opts.noRep, walk: opts.walk !== false, tele: opts.tele !== false}; }

  /* One answer. opts.by picks what is kept smallest: tickets (steps, then price, then time), price (then steps,
     then time) or time (legs with no known time count as longer than any known time). */
  R.plan = function (g, from, to, opts) {
    opts = opts || {};
    if (!g.byId.has(from) || !g.byId.has(to)) return {ok: false};
    if (from === to) return summary([]);
    var s = search(g, from, opts, to);
    if (!s.dist.has(to)) return {ok: false};
    var legs = trace(s.prev, from, to);
    return legs ? summary(legs) : {ok: false};
  };

  /* The answers worth showing: fewest tickets, cheapest, and fastest when its times are all known. Answers that
     are the same list of legs are merged and carry every label. */
  R.options = function (g, from, to, opts) {
    var base = flags(opts || {});
    var first = R.plan(g, from, to, Object.assign({by: "tickets"}, base));
    if (!first.ok) return {ok: false, why: R.why(g, from, to, base)};
    var list = [], seen = {};
    function push(label, plan) {
      if (!plan.ok) return;
      if (seen[plan.key]) { seen[plan.key].labels.push(label); return; }
      plan.labels = [label]; seen[plan.key] = plan; list.push(plan);
    }
    push("tickets", first);
    push("price", R.plan(g, from, to, Object.assign({by: "price"}, base)));
    var fast = R.plan(g, from, to, Object.assign({by: "time"}, base));
    if (fast.ok && fast.secondsKnown) push("time", fast);
    return {ok: true, options: list};
  };

  /* Everything that can be reached from one stable, with the fewest-steps answer for each. */
  R.reach = function (g, from, opts) {
    opts = Object.assign({by: "tickets"}, opts || {});
    var s = search(g, from, opts), out = new Map();
    s.dist.forEach(function (c, id) {
      if (id === from) return;
      var legs = trace(s.prev, from, id);
      if (legs) out.set(id, summary(legs));
    });
    return out;
  };

  /* Stables joined to `id` when direction and reputation are ignored (one "network"). */
  R.network = function (g, id) {
    var seen = new Set([id]), todo = [id];
    while (todo.length) {
      var at = todo.pop();
      (g.out.get(at) || []).forEach(function (l) { if (!seen.has(l.to)) { seen.add(l.to); todo.push(l.to); } });
      (g.inn.get(at) || []).forEach(function (l) { if (!seen.has(l.from)) { seen.add(l.from); todo.push(l.from); } });
    }
    return seen;
  };

  /* Why there is no route. Codes, most specific first:
       unknown    a stable id is not in the data
       same       from and to are the same stable
       norep      a route exists, but every one needs reputation      -> {plan: the best such route, legs: its reputation legs}
       notele     a route exists only with a Shrouded Isles teleporter -> {plan}
       nowalk     a route exists only with a walk between two masters  -> {plan}
       nosale     the start sells no ticket at all
       noarrival  no ticket anywhere ends at the destination
       separate   the two are on networks no ticket joins              -> {fromNet: Set, toNet: Set}
       oneway     joined, but only against the direction of travel     -> {back: plan for the trip the other way, or null}  */
  R.why = function (g, from, to, opts) {
    opts = opts || {};
    if (!g.byId.has(from) || !g.byId.has(to)) return {code: "unknown"};
    if (from === to) return {code: "same"};
    var f = flags(opts);
    if (f.noRep) {
      var withRep = R.plan(g, from, to, {by: "tickets", walk: f.walk, tele: f.tele, noRep: false});
      if (withRep.ok) return {code: "norep", plan: withRep, legs: withRep.rep};
    }
    if (!f.tele) {
      var withTele = R.plan(g, from, to, {by: "tickets", walk: f.walk, tele: true, noRep: false});
      if (withTele.ok) return {code: "notele", plan: withTele, needsRep: withTele.rep.length > 0};
    }
    if (!f.walk) {
      var withWalk = R.plan(g, from, to, {by: "tickets", walk: true, tele: true, noRep: false});
      if (withWalk.ok) return {code: "nowalk", plan: withWalk, needsRep: withWalk.rep.length > 0};
    }
    var counts = function (l) { return (!l.walk || f.walk) && (!l.tele || f.tele); };
    if (!(g.out.get(from) || []).filter(counts).length) return {code: "nosale"};
    if (!(g.inn.get(to) || []).filter(counts).length) return {code: "noarrival"};
    var net = R.network(g, from);
    if (!net.has(to)) return {code: "separate", fromNet: net, toNet: R.network(g, to)};
    var back = R.plan(g, to, from, {by: "tickets", walk: f.walk, tele: f.tele, noRep: false});
    return {code: "oneway", back: back.ok ? back : null};
  };

  /* The stable masters of one town: the stable itself and those a walk leg joins to it. */
  R.townOf = function (g, id) {
    var out = [id];
    (g.out.get(id) || []).forEach(function (l) { if (l.walk && out.indexOf(l.to) < 0) out.push(l.to); });
    return out;
  };
  /* Is there a ticket straight back? (false = one way). A town with two stable masters counts as one stop: a
     ticket from either master of the arrival town to either master of the starting town is a return. */
  R.hasReturn = function (g, leg) {
    var home = R.townOf(g, leg.from);
    return R.townOf(g, leg.to).some(function (id) {
      return (g.out.get(id) || []).some(function (l) { return !l.walk && !l.tele && home.indexOf(l.to) >= 0; });
    });
  };

  R.time = function (seconds) {
    if (seconds == null) return "";
    var s = Math.round(seconds), m;
    if (s < 60) return s + " s";
    if (s < 600) { m = Math.floor(s / 60); return m + " min" + (s % 60 ? " " + (s % 60) + " s" : ""); }
    m = Math.round(s / 60);
    return m >= 60 ? Math.floor(m / 60) + " h " + (m % 60) + " min" : m + " min";
  };
  R.KINDS = {horse: "Horse", flying: "Flying", boat: "Boat", walk: "Walk", teleport: "Teleporter"};
  R.kindName = function (leg) {
    if (leg.walk) return "Walk";
    if (leg.tele) return "Teleporter";
    var k = R.KINDS[leg.kind] || (leg.kind ? leg.kind.charAt(0).toUpperCase() + leg.kind.slice(1) : "Horse");
    return leg.mount && leg.mount.toLowerCase() !== leg.kind ? k + " (" + leg.mount.toLowerCase() + ")" : k;
  };
})();
