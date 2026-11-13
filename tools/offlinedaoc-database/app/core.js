/* OFFLINEDAOC Database - core (owned by the lead; do not edit, ask the lead for changes).
   Classic script, no modules, no network: the tool must work when index.html is opened from disk (file://). */
(function () {
  "use strict";
  var WG = (window.WG = window.WG || {});
  WG.version = "0.1";

  /* ---------- small DOM helper ----------
     WG.h("div.card", {onclick: fn, title: "x"}, child, "text", [more children]) */
  WG.h = function (spec, attrs) {
    var parts = String(spec).split("."), el = document.createElement(parts[0] || "div");
    if (parts.length > 1) el.className = parts.slice(1).join(" ");
    var i = 1;
    if (attrs && typeof attrs === "object" && !(attrs instanceof Node) && !Array.isArray(attrs)) {
      for (var k in attrs) {
        var v = attrs[k];
        if (v == null || v === false) continue;
        if (k.slice(0, 2) === "on" && typeof v === "function") el.addEventListener(k.slice(2), v);
        else if (k === "style" && typeof v === "object") Object.assign(el.style, v);
        else if (k === "class") el.className += (el.className ? " " : "") + v;
        else if (k === "html") el.innerHTML = v;
        else if (k in el && k !== "list") { try { el[k] = v; } catch (e) { el.setAttribute(k, v); } }
        else el.setAttribute(k, v);
      }
      i = 2;
    }
    for (; i < arguments.length; i++) WG.append(el, arguments[i]);
    return el;
  };
  WG.append = function (el, c) {
    if (c == null || c === false) return el;
    if (Array.isArray(c)) { for (var j = 0; j < c.length; j++) WG.append(el, c[j]); return el; }
    el.appendChild(c instanceof Node ? c : document.createTextNode(String(c)));
    return el;
  };
  WG.clear = function (el) { while (el.firstChild) el.removeChild(el.firstChild); return el; };
  WG.esc = function (s) { return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) { return {"&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;"}[c]; }); };

  /* ---------- data loading ----------
     A data file is data/<name>.js and contains exactly:  WG.provide("<name>", <JSON value>);
     WG.load("items", "drops").then(function (d) { d.items ... d.drops ... })   (cached; each file loads once) */
  var store = {}, waiting = {};
  WG.data = store;
  WG.provide = function (name, value) {
    store[name] = value;
    if (waiting[name]) { waiting[name].resolve(value); }
  };
  function loadOne(name) {
    if (waiting[name]) return waiting[name].promise;
    var w = (waiting[name] = {});
    w.promise = new Promise(function (resolve, reject) { w.resolve = resolve; w.reject = reject; });
    if (name in store) { w.resolve(store[name]); return w.promise; }
    var s = document.createElement("script");
    s.src = "data/" + name + ".js";
    s.onerror = function () { delete waiting[name]; w.reject(new Error("Data file missing: data/" + name + ".js (run source/build_all.py)")); };
    s.onload = function () { if (!(name in store)) { delete waiting[name]; w.reject(new Error("data/" + name + ".js did not call WG.provide('" + name + "', ...)")); } };
    document.head.appendChild(s);
    return w.promise;
  }
  WG.load = function () {
    var names = Array.prototype.slice.call(arguments);
    if (names.length === 1 && Array.isArray(names[0])) names = names[0];
    return Promise.all(names.map(loadOne)).then(function () {
      var out = {}; names.forEach(function (n) { out[n] = store[n]; }); return out;
    });
  };
  /* Compact tables: {cols:["id","name",...], rows:[[1,"x",...],...]} -> array of objects (made once, cached on the table). */
  WG.rows = function (t) {
    if (!t || !t.cols) return t || [];
    if (t._objs) return t._objs;
    var cols = t.cols, n = cols.length;
    t._objs = t.rows.map(function (r) { var o = {}; for (var i = 0; i < n; i++) o[cols[i]] = r[i]; return o; });
    return t._objs;
  };
  /* Index an array of objects by a key (cached per array+key). */
  WG.index = function (arr, key) {
    arr._idx = arr._idx || {};
    if (!arr._idx[key]) { var m = (arr._idx[key] = new Map()); for (var i = 0; i < arr.length; i++) m.set(arr[i][key], arr[i]); }
    return arr._idx[key];
  };

  /* ---------- "my character" (persisted in the browser) ---------- */
  var ME_KEY = "offlinedaoc-db.me";
  WG.me = {level: 1, realm: 0, cls: 0};   // realm 0 = any, 1 Albion, 2 Midgard, 3 Hibernia; cls = class id, 0 = any
  try { Object.assign(WG.me, JSON.parse(localStorage.getItem(ME_KEY) || "{}")); } catch (e) {}
  var meListeners = [];
  WG.setMe = function (patch) {
    Object.assign(WG.me, patch);
    try { localStorage.setItem(ME_KEY, JSON.stringify(WG.me)); } catch (e) {}
    meListeners.forEach(function (f) { try { f(WG.me); } catch (e) { console.error(e); } });
  };
  WG.onMe = function (f) { meListeners.push(f); };
  /* Per-view saved settings: WG.pref("hunt.con", "low-purple") sets, WG.pref("hunt.con") gets. */
  WG.pref = function (key, value) {
    var k = "offlinedaoc-db.pref." + key;
    try {
      if (arguments.length > 1) { localStorage.setItem(k, JSON.stringify(value)); return value; }
      var v = localStorage.getItem(k); return v == null ? undefined : JSON.parse(v);
    } catch (e) { return undefined; }
  };

  /* ---------- formatting ---------- */
  WG.REALMS = ["Any", "Albion", "Midgard", "Hibernia"];
  WG.money = function (copper) {           // 123456 -> "12g 34s 56c"; platinum above 1000g
    copper = Math.max(0, Math.round(copper || 0));
    var c = copper % 100, s = Math.floor(copper / 100) % 100, g = Math.floor(copper / 10000) % 1000, p = Math.floor(copper / 10000000);
    var out = [];
    if (p) out.push(p + "p"); if (g) out.push(g + "g"); if (s) out.push(s + "s"); if (c || !out.length) out.push(c + "c");
    return out.join(" ");
  };
  WG.pct = function (p) {                   // probability 0..1 -> "12.5%", "0.3%", "<0.01%"
    if (p == null) return "";
    var v = p * 100;
    if (v >= 99.95) return "100%"; if (v >= 10) return v.toFixed(1).replace(/\.0$/, "") + "%";
    if (v >= 1) return v.toFixed(1) + "%"; if (v >= 0.01) return v.toFixed(2) + "%"; return v > 0 ? "<0.01%" : "0%";
  };

  /* ---------- views and routing ----------
     Hash routes: #/<view>/<param>/<param>?key=value&key=value
     WG.view("hunt", {title:"Hunt", nav:true, order:20, render:function(el, params, query){...}, leave:function(){...}}) */
  var views = {};
  WG.views = views;
  WG.view = function (name, def) { def.name = name; views[name] = def; if (WG._started) { buildNav(); route(); } };
  WG.href = function (path, query) {
    var q = [];
    if (query) for (var k in query) { var v = query[k]; if (v == null || v === "" || v === false) continue; q.push(encodeURIComponent(k) + "=" + encodeURIComponent(v)); }
    return "#/" + String(path).replace(/^[#\/]+/, "") + (q.length ? "?" + q.join("&") : "");
  };
  WG.go = function (path, query) { location.hash = WG.href(path, query); };
  /* Update the query part of the current hash without re-rendering (keeps filters in the address bar / back button). */
  var silent = false;
  WG.setQuery = function (query) {
    var cur = parse(), h = WG.href(cur.view + (cur.params.length ? "/" + cur.params.map(encodeURIComponent).join("/") : ""), query);
    if (h !== location.hash) { silent = true; history.replaceState(null, "", h); silent = false; }
  };
  function parse() {
    var h = location.hash.replace(/^#\/?/, ""), qi = h.indexOf("?"), path = qi < 0 ? h : h.slice(0, qi), q = {};
    if (qi >= 0) h.slice(qi + 1).split("&").forEach(function (kv) { if (!kv) return; var e = kv.indexOf("="); q[decodeURIComponent(e < 0 ? kv : kv.slice(0, e))] = e < 0 ? "" : decodeURIComponent(kv.slice(e + 1).replace(/\+/g, " ")); });
    var seg = path.split("/").filter(Boolean).map(decodeURIComponent);
    return {view: seg[0] || "home", params: seg.slice(1), query: q};
  }
  WG.route = parse;
  var current = null;
  function route() {
    if (silent) return;
    var r = parse(), def = views[r.view], main = document.getElementById("wg-main");
    if (!main) return;
    if (current && current.leave) { try { current.leave(); } catch (e) { console.error(e); } }
    current = def || null;
    WG.clear(main);
    main.scrollTop = 0; window.scrollTo(0, 0);
    Array.prototype.forEach.call(document.querySelectorAll("#wg-nav a"), function (a) { a.classList.toggle("active", a.dataset.view === (def && (def.navAs || def.name))); });
    if (!def) { main.appendChild(WG.h("div.wg-empty", "This page is not built yet: " + r.view)); return; }
    document.title = (typeof def.title === "function" ? def.title(r.params, r.query) : def.title || r.view) + " - OFFLINEDAOC Database";
    try {
      var p = def.render(main, r.params, r.query);
      if (p && typeof p.then === "function") p.catch(function (e) { showError(main, e); });
    } catch (e) { showError(main, e); }
  }
  function showError(main, e) {
    console.error(e);
    main.appendChild(WG.h("div.wg-error", WG.h("b", "Something went wrong on this page."), WG.h("pre", String(e && e.stack || e))));
  }
  WG.showError = showError;
  function buildNav() {
    var nav = document.getElementById("wg-nav"); if (!nav) return;
    WG.clear(nav);
    Object.keys(views).map(function (k) { return views[k]; }).filter(function (v) { return v.nav; })
      .sort(function (a, b) { return (a.order || 99) - (b.order || 99); })
      .forEach(function (v) { nav.appendChild(WG.h("a", {href: WG.href(v.name), "data-view": v.name}, typeof v.title === "string" ? v.title : v.name)); });
  }

  /* ---------- start: load the scripts named in app/manifest.js, then route ---------- */
  WG.start = function () {
    var list = (WG.manifest || []).slice(), i = 0;
    function next() {
      if (i >= list.length) { WG._started = true; buildNav(); route(); window.addEventListener("hashchange", route); if (WG.onStart) WG.onStart(); return; }
      var s = document.createElement("script");
      s.src = list[i++]; s.onload = next;
      s.onerror = function () { console.warn("not built yet: " + s.src); next(); };
      document.head.appendChild(s);
    }
    next();
  };
})();
