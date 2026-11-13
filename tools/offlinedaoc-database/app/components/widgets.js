/* OFFLINEDAOC Database - shared widgets (P-U). Signatures: source/contract/widgets.md. Classic script. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = (WG.ui = WG.ui || {});

  /* ------------------------------------------------------------------ basics */
  ui.num = function (n) {
    if (n == null || n === "" || isNaN(n)) return "";
    return Math.round(n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  };
  ui.debounce = function (fn, ms) {
    var t = 0;
    var d = function () { var a = arguments, self = this; clearTimeout(t); t = setTimeout(function () { fn.apply(self, a); }, ms == null ? 150 : ms); };
    d.cancel = function () { clearTimeout(t); };
    return d;
  };
  var CONS = ["grey", "green", "blue", "yellow", "orange", "red", "purple"];
  var REALM_VAR = [null, "--alb", "--mid", "--hib"];
  var KIND_VAR = {pin: "--pin", vendor: "--pin-vendor", stable: "--pin-stable", danger: "--pin-danger", point: "--pin-point", accent: "--accent"};
  var cssCache = {};
  function cssVar(name) {
    if (!(name in cssCache)) cssCache[name] = (getComputedStyle(document.documentElement).getPropertyValue(name) || "").trim();
    return cssCache[name];
  }
  /* ui.color("red") -> "var(--con-red)"; ui.color(2) -> "var(--mid)"; ui.color("#fff") -> "#fff".
     ui.color(c, true) -> the resolved colour text (for a canvas). */
  ui.color = function (c, resolved) {
    var v = null;
    if (c == null || c === "") return "";
    if (typeof c === "number") v = REALM_VAR[c] || null;
    else if (CONS.indexOf(c) >= 0) v = "--con-" + c;
    else if (KIND_VAR[c]) v = KIND_VAR[c];
    else if (/^--/.test(c)) v = c;
    if (!v) return String(c);
    return resolved ? (cssVar(v) || "#ff2d2d") : "var(" + v + ")";
  };
  ui.conClass = function (mobLevel, myLevel) {
    if (!WG.con || typeof WG.con.color !== "function" || mobLevel == null) return "";
    if (myLevel == null) myLevel = WG.me && WG.me.level || 1;
    try { var c = WG.con.color(myLevel, mobLevel); return c ? "con-" + c : ""; } catch (e) { return ""; }
  };
  ui.conName = function (mobLevel, myLevel) { var c = ui.conClass(mobLevel, myLevel); return c ? c.slice(4) : ""; };
  /* "20 to 26", each end in its con colour */
  ui.level = function (min, max, myLevel) {
    if (min == null && max == null) return h("span.wg-level-text");
    if (min == null) min = max; if (max == null) max = min;
    if (max < min) { var t = min; min = max; max = t; }
    var s = h("span.wg-level-text.wg-nowrap");
    s.appendChild(h("span", {class: ui.conClass(min, myLevel)}, String(min)));
    if (max !== min) { s.appendChild(h("span.wg-dim", " to ")); s.appendChild(h("span", {class: ui.conClass(max, myLevel)}, String(max))); }
    var cn = ui.conName(min, myLevel), cx = ui.conName(max, myLevel);
    if (cn) s.title = (min === max ? "Level " + min + ": " + cn : "Level " + min + " (" + cn + ") to " + max + " (" + cx + ")") + " to a level " + (myLevel == null ? (WG.me.level || 1) : myLevel) + " character";
    return s;
  };
  var REALM_SHORT = ["Any", "Alb", "Mid", "Hib"];
  ui.realm = function (realm, opts) {
    realm = +realm || 0; opts = opts || {};
    return h("span", {class: realm ? "realm-" + realm : "wg-dim"}, (opts.short ? REALM_SHORT : WG.REALMS)[realm] || "Any");
  };
  ui.money = function (copper) {
    var s = h("span.wg-money.wg-nowrap");
    copper = Math.max(0, Math.round(copper || 0));
    var c = copper % 100, sv = Math.floor(copper / 100) % 100, g = Math.floor(copper / 10000) % 1000, p = Math.floor(copper / 10000000);
    var parts = [];
    if (p) parts.push(h("span.coin-p", p + "p")); if (g) parts.push(h("span.coin-g", g + "g"));
    if (sv) parts.push(h("span.coin-s", sv + "s")); if (c || !parts.length) parts.push(h("span.coin-c", c + "c"));
    parts.forEach(function (x, i) { if (i) s.appendChild(document.createTextNode(" ")); s.appendChild(x); });
    return s;
  };
  ui.chip = function (text, opts) {
    opts = opts || {};
    var el = h(opts.href ? "a" : (opts.onclick ? "button" : "span"), {class: "wg-chip" + (opts.cls ? " " + opts.cls : "") + (opts.active ? " active" : ""), title: opts.title, href: opts.href, onclick: opts.onclick, type: opts.onclick && !opts.href ? "button" : null});
    if (opts.color) { el.style.setProperty("--chip", ui.color(opts.color)); el.classList.add("colored"); }
    WG.append(el, text);
    if (opts.count != null) el.appendChild(h("span.wg-chip-count", ui.num(opts.count)));
    return el;
  };
  ui.loading = function (el, text) { WG.clear(el); el.appendChild(h("div.wg-empty.wg-loading", text || "Loading...")); return el; };
  ui.empty = function (text) { return h("div.wg-empty", text || "Nothing to show."); };
  ui.note = function (text, kind) { return h("div.wg-note" + (kind && kind !== "info" ? "." + kind : ""), text); };
  ui.copy = function (text, btn) {
    function done(ok) {
      if (!btn) return;
      var old = btn.getAttribute("data-label") || btn.textContent;
      btn.setAttribute("data-label", old); btn.textContent = ok ? "Copied" : "Copy failed"; btn.classList.add("flash");
      clearTimeout(btn._t); btn._t = setTimeout(function () { btn.textContent = old; btn.classList.remove("flash"); }, 1100);
    }
    function fallback() {
      var ta = h("textarea", {style: {position: "fixed", left: "-999px", top: "0"}}); ta.value = text;
      document.body.appendChild(ta); ta.select(); var ok = false;
      try { ok = document.execCommand("copy"); } catch (e) {}
      document.body.removeChild(ta); done(ok);
    }
    try { if (navigator.clipboard && navigator.clipboard.writeText) { navigator.clipboard.writeText(text).then(function () { done(true); }, fallback); return; } } catch (e) {}
    fallback();
  };

  /* ------------------------------------------------------------------ page furniture */
  ui.page = function (el, o) {
    o = o || {};
    var head = h("div.wg-head");
    if (o.crumbs && o.crumbs.length) {
      var c = h("div.wg-crumbs");
      o.crumbs.forEach(function (cr, i) { if (i) c.appendChild(h("span.sep", "/")); c.appendChild(cr[1] ? h("a", {href: cr[1]}, cr[0]) : h("span", cr[0])); });
      head.appendChild(c);
    }
    var line = h("div.wg-head-line", h("h1", o.title || ""));
    if (o.actions) line.appendChild(h("div.wg-head-actions", o.actions));
    head.appendChild(line);
    if (o.sub) head.appendChild(h("div.wg-head-sub", o.sub));
    if (el) el.appendChild(head);
    return head;
  };
  ui.section = function (title, opts) {
    var i = 1, o = {};
    if (opts && typeof opts === "object" && !(opts instanceof Node) && !Array.isArray(opts)) { o = opts; i = 2; }
    var body = h("div.wg-section-body"), sec = h("section.wg-card.wg-section" + (o.cls ? "." + o.cls.split(" ").join(".") : ""));
    var head = h("div.wg-section-head", h("h2", title, o.count != null ? h("span.wg-count", ui.num(o.count)) : null));
    if (o.actions) head.appendChild(h("div.wg-head-actions", o.actions));
    if (o.collapsed != null) {
      head.classList.add("toggle"); head.tabIndex = 0;
      var set = function (c) { sec.classList.toggle("collapsed", c); };
      set(!!o.collapsed);
      head.addEventListener("click", function (e) { if (e.target.closest("a,button,input,select")) return; set(!sec.classList.contains("collapsed")); });
      head.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); set(!sec.classList.contains("collapsed")); } });
    }
    sec.appendChild(head); sec.appendChild(body);
    for (; i < arguments.length; i++) WG.append(body, arguments[i]);
    sec.body = body;
    return sec;
  };
  ui.kv = function (pairs) {
    var d = h("dl.wg-kv");
    (pairs || []).forEach(function (p) { if (!p || p[1] == null || p[1] === "" || p[1] === false) return; d.appendChild(h("dt", p[0])); d.appendChild(h("dd", p[1])); });
    return d;
  };
  ui.bar = function (share, opts) {
    opts = opts || {};
    var s = Math.max(0, Math.min(1, +share || 0));
    var el = h("span.wg-bar", {title: opts.title || null, style: {width: (opts.width || 80) + "px"}}, h("span.fill", {style: {width: (s * 100).toFixed(1) + "%", background: opts.color ? ui.color(opts.color) : null}}));
    if (opts.text != null) return h("span.wg-bar-wrap.wg-nowrap", el, h("span.wg-bar-text", opts.text));
    return el;
  };
  ui.histogram = function (values, opts) {
    opts = opts || {};
    var list = Array.isArray(values) ? values.slice() : Object.keys(values || {}).map(function (k) { return [+k, values[k]]; });
    list = list.filter(function (v) { return v && v[1] > 0; }).sort(function (a, b) { return a[0] - b[0]; });
    var el = h("div.wg-hist", {style: {height: (opts.height || 56) + "px"}});
    if (!list.length) return el;
    var max = 0, lo = list[0][0], hi = list[list.length - 1][0], by = {};
    list.forEach(function (v) { by[v[0]] = v[1]; if (v[1] > max) max = v[1]; });
    var my = opts.myLevel == null ? null : opts.myLevel, span = hi - lo + 1, step = span > 60 ? Math.ceil(span / 60) : 1, L, k, n;
    if (step > 1) { max = 0; for (L = lo; L <= hi; L += step) { n = 0; for (k = L; k < L + step; k++) n += by[k] || 0; if (n > max) max = n; } }
    for (L = lo; L <= hi; L += step) {
      n = 0; for (k = L; k < L + step; k++) n += by[k] || 0;
      var col = h("div.col", {title: "Level " + L + (step > 1 ? " to " + (L + step - 1) : "") + ": " + ui.num(n) + (opts.unit ? " " + opts.unit : "")},
        h("div.fill", {class: my == null ? "" : "bg-" + (ui.conClass(L, my) || "con-none"), style: {height: (n ? Math.max(4, n / max * 100) : 0) + "%"}}));
      el.appendChild(col);
    }
    var wrap = h("div.wg-hist-wrap", el, h("div.wg-hist-axis", h("span", String(lo)), lo !== hi ? h("span", String(hi)) : null));
    return wrap;
  };

  /* ------------------------------------------------------------------ tooltip */
  var tip = null, tipTimer = 0, tipOwner = null;
  function tipEl() { if (!tip) { tip = h("div.wg-tip", {role: "tooltip"}); document.body.appendChild(tip); } return tip; }
  function placeTip(x, y) {
    var t = tipEl(), w = t.offsetWidth, hh = t.offsetHeight, vw = document.documentElement.clientWidth, vh = document.documentElement.clientHeight;
    var left = x + 16, top = y + 18;
    if (left + w > vw - 8) left = Math.max(8, x - w - 12);
    if (top + hh > vh - 8) top = Math.max(8, y - hh - 12);
    t.style.left = left + "px"; t.style.top = top + "px";
  }
  function fillTip(content, target) {
    var t = tipEl(); WG.clear(t);
    var c = typeof content === "function" ? content(target) : content;
    if (c == null || c === "" || c === false) return false;
    if (c && typeof c.then === "function") {
      t.appendChild(h("span.wg-dim", "Loading..."));
      var owner = tipOwner;
      c.then(function (v) { if (tipOwner !== owner || !t.classList.contains("show")) return; WG.clear(t); if (v == null || v === "") { ui.tooltip.hide(); return; } WG.append(t, v); placeTip(lastX, lastY); }, function () { ui.tooltip.hide(); });
    } else WG.append(t, c);
    return true;
  }
  var lastX = 0, lastY = 0;
  ui.tooltip = function (target, content, opts) {
    opts = opts || {};
    var delay = opts.delay == null ? 120 : opts.delay;
    target.addEventListener("mouseenter", function (e) {
      lastX = e.clientX; lastY = e.clientY; clearTimeout(tipTimer); tipOwner = target;
      tipTimer = setTimeout(function () { if (tipOwner !== target || !document.body.contains(target)) return; if (fillTip(content, target)) { tipEl().classList.add("show"); placeTip(lastX, lastY); } }, delay);
    });
    target.addEventListener("mousemove", function (e) { lastX = e.clientX; lastY = e.clientY; if (tip && tip.classList.contains("show") && tipOwner === target) placeTip(lastX, lastY); });
    var off = function () { if (tipOwner === target) ui.tooltip.hide(); };
    target.addEventListener("mouseleave", off); target.addEventListener("mousedown", off);
    return target;
  };
  ui.tooltip.show = function (content, x, y) { tipOwner = {}; clearTimeout(tipTimer); lastX = x; lastY = y; if (fillTip(content, null)) { tipEl().classList.add("show"); placeTip(x, y); } };
  ui.tooltip.move = function (x, y) { lastX = x; lastY = y; if (tip && tip.classList.contains("show")) placeTip(x, y); };
  ui.tooltip.hide = function () { clearTimeout(tipTimer); tipOwner = null; if (tip) tip.classList.remove("show"); };
  window.addEventListener("hashchange", function () { ui.tooltip.hide(); });
  window.addEventListener("scroll", function () { ui.tooltip.hide(); }, true);

  /* ------------------------------------------------------------------ links */
  function lookup(file, key, id) {
    var d = WG.data[file]; if (!d) return null;
    var arr = d[key] || d; arr = arr && arr.cols ? WG.rows(arr) : arr;
    if (!Array.isArray(arr)) return null;
    try { return WG.index(arr, "id").get(id) || WG.index(arr, "id").get(+id) || WG.index(arr, "id").get(String(id)) || null; } catch (e) { return null; }
  }
  ui.lookup = lookup;
  ui.icon = function (item, size) {
    size = size || 20;
    try { if (WG.item && typeof WG.item.icon === "function") { var ic = WG.item.icon(item, size); if (ic) return ic; } } catch (e) {}
    return h("span.wg-icon.blank", {style: {width: size + "px", height: size + "px"}});
  };
  ui.itemLink = function (item, opts) {
    opts = opts || {};
    if (item == null) return h("span.wg-dim", "");
    if (typeof item !== "object") item = lookup("items", "items", item) || {id: item, name: String(item)};
    var a = h("a.wg-link.wg-item-link", {href: WG.href("item/" + encodeURIComponent(item.id))});
    if (item.realm >= 1 && item.realm <= 3 && opts.realm !== false) a.classList.add("realm-" + item.realm);
    if (opts.icon !== false && WG.item && typeof WG.item.icon === "function") { try { var ic = WG.item.icon(item, opts.iconSize || 20); if (ic) a.appendChild(ic); } catch (e) {} }
    a.appendChild(h("span", opts.text != null ? opts.text : (item.name || String(item.id))));
    if (opts.tooltip !== false) ui.tooltip(a, function () { try { return WG.item && WG.item.tooltip ? WG.item.tooltip(item) : null; } catch (e) { return null; } });
    return a;
  };
  ui.mobLink = function (kind, opts) {
    opts = opts || {};
    if (kind == null) return h("span.wg-dim", "");
    if (typeof kind !== "object") kind = lookup("mobs", "kinds", kind) || {id: kind, name: "Monster " + kind};
    var a = h("a.wg-link.wg-mob-link", {href: WG.href("mob/" + kind.id)}, opts.text != null ? opts.text : kind.name);
    var lo = kind.levelMin != null ? kind.levelMin : kind.lmin, hi = kind.levelMax != null ? kind.levelMax : kind.lmax;   // mobs.js names them lmin / lmax
    if (kind.f & 1) a.classList.add("wg-named");
    if (!opts.level || lo == null) return a;
    return h("span.wg-nowrap", a, " ", ui.level(lo, hi, opts.myLevel));
  };
  ui.zoneLink = function (zone, opts) {
    opts = opts || {};
    if (zone == null) return h("span.wg-dim", "");
    if (typeof zone !== "object") zone = lookup("zones", "zones", zone) || {id: zone, name: "Zone " + zone};
    var q = opts.loc ? {x: Math.round(opts.loc[0]), y: Math.round(opts.loc[1])} : (opts.query || null);
    var a = h("a.wg-link.wg-zone-link", {href: WG.href("zones/" + zone.id, q)}, opts.text != null ? opts.text : zone.name);
    if (zone.realm >= 1 && zone.realm <= 3 && opts.realm !== false) a.classList.add("realm-" + zone.realm);
    return a;
  };
  ui.vendorLink = function (v, opts) {
    opts = opts || {};
    if (v == null) return h("span.wg-dim", "");
    if (typeof v !== "object") v = lookup("vendors", "vendors", v) || {id: v, name: "Merchant " + v};
    return h("a.wg-link.wg-vendor-link", {href: WG.href("vendor/" + v.id)}, opts.text != null ? opts.text : v.name);
  };

  /* ------------------------------------------------------------------ level input */
  ui.levelInput = function (opts) {
    opts = opts || {};
    var min = opts.min == null ? 1 : opts.min, max = opts.max == null ? 50 : opts.max;
    var val = clamp(opts.value == null ? (WG.me.level || 1) : opts.value);
    function clamp(n) { n = Math.round(+n); if (isNaN(n)) n = min; return Math.max(min, Math.min(max, n)); }
    var input = h("input", {type: "number", min: min, max: max, value: val, "aria-label": opts.label || "Level"});
    var minus = h("button", {type: "button", tabIndex: -1, title: "One level lower", "aria-label": "One level lower"}, "−");
    var plus = h("button", {type: "button", tabIndex: -1, title: "One level higher", "aria-label": "One level higher"}, "+");
    var el = h("span.wg-level", opts.label ? h("span.wg-level-label", opts.label) : null, h("span.wg-stepper", minus, input, plus));
    function set(n, fire) { n = clamp(n); var ch = n !== val; val = n; if (String(input.value) !== String(n)) input.value = n; if (ch && fire && opts.onChange) opts.onChange(n); }
    minus.addEventListener("click", function () { set(val - 1, true); });
    plus.addEventListener("click", function () { set(val + 1, true); });
    input.addEventListener("input", function () { if (input.value === "") return; var n = +input.value; if (n >= min && n <= max) { var ch = Math.round(n) !== val; val = Math.round(n); if (ch && opts.onChange) opts.onChange(val); } });
    input.addEventListener("change", function () { set(input.value === "" ? val : input.value, true); input.value = val; });
    input.addEventListener("focus", function () { input.select(); });
    el.get = function () { return val; };
    el.set = function (n) { set(n, false); };
    el.input = input;
    return el;
  };

  /* ------------------------------------------------------------------ table */
  function cmpKeys(a, b) { return a < b ? -1 : a > b ? 1 : 0; }
  /* the direction of the first click on a header: high to low for numbers, or what the column asks for (first: "desc" | "asc") */
  function firstDir(c) { return c.first === "desc" || c.first === "asc" ? c.first : (c.num ? "desc" : "asc"); }
  ui.table = function (el, o) {
    o = o || {};
    var cols = o.columns || [], rows = o.rows || [], order = null;
    var st = {sortKey: o.sortKey || null, sortDir: o.sortDir === "desc" ? "desc" : (o.sortDir === "asc" ? "asc" : null), page: 1, pageSize: o.pageSize == null ? 50 : o.pageSize};
    var table = h("table.wg-table" + (o.dense ? ".dense" : "") + (o.stickyHead === false ? "" : ".sticky")), thead = h("thead"), tbody = h("tbody");
    var pager = h("div.wg-pager"), root = h("div.wg-tablebox", table, pager);
    table.appendChild(thead); table.appendChild(tbody);
    WG.clear(el); el.appendChild(root);

    function col(key) { for (var i = 0; i < cols.length; i++) if (cols[i].key === key) return cols[i]; return null; }
    function doSort() {
      var c = st.sortKey ? col(st.sortKey) : null, n = rows.length, i;
      if (!c || c.sort === false) { order = rows; return; }
      var dir = (st.sortDir || firstDir(c)) === "desc" ? -1 : 1;
      var f = typeof c.sort === "function" ? c.sort : function (r) { return r[c.key]; };
      var keys = new Array(n), idx = new Array(n), numeric = !!c.num;
      for (i = 0; i < n; i++) {
        var v = f(rows[i]);
        if (v == null || v === "" || (typeof v === "number" && isNaN(v))) v = null;
        else if (numeric) { v = +v; if (isNaN(v)) v = null; }
        else if (typeof v === "string") v = v.toLowerCase();
        else if (typeof v === "boolean") v = v ? 1 : 0;
        keys[i] = v; idx[i] = i;
      }
      idx.sort(function (a, b) {
        var x = keys[a], y = keys[b];
        if (x === null || y === null) return x === y ? a - b : (x === null ? 1 : -1);   // empty always last
        if (typeof x !== typeof y) { x = String(x); y = String(y); }
        return (cmpKeys(x, y) * dir) || (a - b);
      });
      order = new Array(n);
      for (i = 0; i < n; i++) order[i] = rows[idx[i]];
    }
    function renderHead() {
      WG.clear(thead);
      var tr = h("tr");
      cols.forEach(function (c) {
        var sortable = c.sort !== false, th = h("th", {title: c.title || null, class: (c.num || c.align === "right" ? "num " : "") + (c.align === "center" ? "center " : "") + (sortable ? "sortable" : "")});
        if (c.width != null) th.style.width = typeof c.width === "number" ? c.width + "px" : c.width;
        th.appendChild(h("span", c.label == null ? c.key : c.label));
        if (sortable) {
          th.tabIndex = 0; th.dataset.key = c.key;
          if (st.sortKey === c.key) { var d = st.sortDir || firstDir(c); th.classList.add("sorted"); th.setAttribute("aria-sort", d === "desc" ? "descending" : "ascending"); th.appendChild(h("span.arrow", d === "desc" ? "▼" : "▲")); }
        }
        tr.appendChild(th);
      });
      thead.appendChild(tr);
    }
    function headClick(e) {
      var th = e.target.closest("th"); if (!th || !th.dataset.key) return;
      if (e.type === "keydown") { if (e.key !== "Enter" && e.key !== " ") return; e.preventDefault(); }
      var c = col(th.dataset.key), first = firstDir(c);
      if (st.sortKey === c.key) st.sortDir = (st.sortDir || first) === "asc" ? "desc" : "asc"; else { st.sortKey = c.key; st.sortDir = first; }
      st.page = 1; doSort(); renderHead(); renderBody();
      if (o.onSort) o.onSort(st.sortKey, st.sortDir);
    }
    thead.addEventListener("click", headClick); thead.addEventListener("keydown", headClick);
    function pageCount() { return st.pageSize > 0 ? Math.max(1, Math.ceil(rows.length / st.pageSize)) : 1; }
    function renderBody() {
      WG.clear(tbody);
      var n = order.length;
      if (!n) { tbody.appendChild(h("tr.wg-table-empty", h("td", {colSpan: Math.max(1, cols.length)}, o.empty || "Nothing matches."))); renderPager(); return; }
      var pc = pageCount(); if (st.page > pc) st.page = pc; if (st.page < 1) st.page = 1;
      var a = st.pageSize > 0 ? (st.page - 1) * st.pageSize : 0, b = st.pageSize > 0 ? Math.min(n, a + st.pageSize) : n;
      var frag = document.createDocumentFragment(), clickable = !!(o.onRow || o.rowHref);
      for (var i = a; i < b; i++) {
        var r = order[i], tr = document.createElement("tr");
        tr._row = r;
        if (clickable) tr.className = "clickable";
        if (o.rowClass) { var rc = o.rowClass(r); if (rc) tr.className += (tr.className ? " " : "") + rc; }
        for (var j = 0; j < cols.length; j++) {
          var c = cols[j], td = document.createElement("td"), v = r[c.key];
          if (c.num || c.align === "right") td.className = "num"; else if (c.align === "center") td.className = "center";
          if (c.cls) td.className += (td.className ? " " : "") + c.cls;
          var out = c.render ? c.render(r, v, i) : v;
          if (out != null && out !== false) { if (out instanceof Node) td.appendChild(out); else if (Array.isArray(out)) WG.append(td, out); else td.textContent = String(out); }
          tr.appendChild(td);
        }
        frag.appendChild(tr);
      }
      tbody.appendChild(frag);
      renderPager();
    }
    function btn(text, title, page, disabled) {
      return h("button.wg-btn.small", {type: "button", title: title, "aria-label": title, disabled: disabled ? true : null, onclick: function () { go(page, true); }}, text);
    }
    function renderPager() {
      WG.clear(pager);
      var n = rows.length, pc = pageCount(), noun = o.count ? " " + o.count : "";
      if (!n) { pager.style.display = "none"; return; }
      pager.style.display = "";
      if (st.pageSize <= 0 || n <= Math.min(st.pageSize, 25)) { pager.appendChild(h("span.wg-pager-info", ui.num(n) + noun + (o.count ? "" : n === 1 ? " row" : " rows"))); return; }
      var a = (st.page - 1) * st.pageSize + 1, b = Math.min(n, st.page * st.pageSize);
      pager.appendChild(h("span.wg-pager-info", ui.num(a) + " to " + ui.num(b) + " of " + ui.num(n) + noun));
      var sizes = [25, 50, 100, 250]; if (sizes.indexOf(st.pageSize) < 0) { sizes.push(st.pageSize); sizes.sort(function (x, y) { return x - y; }); }
      var sel = h("select", {title: "Rows per page", "aria-label": "Rows per page", onchange: function () { var first = (st.page - 1) * st.pageSize; st.pageSize = +sel.value; st.page = Math.floor(first / st.pageSize) + 1; renderBody(); }},
        sizes.map(function (s) { return h("option", {value: s, selected: s === st.pageSize ? true : null}, s + " per page"); }));
      pager.appendChild(h("span.wg-pager-nav",
        btn("«", "First page", 1, st.page <= 1), btn("‹ Previous", "Previous page", st.page - 1, st.page <= 1),
        h("span.wg-pager-page", "Page " + ui.num(st.page) + " of " + ui.num(pc)),
        btn("Next ›", "Next page", st.page + 1, st.page >= pc), btn("»", "Last page", pc, st.page >= pc)));
      pager.appendChild(sel);
    }
    function go(p, scroll) {
      st.page = Math.max(1, Math.min(pageCount(), p)); renderBody();
      if (scroll) { var r = table.getBoundingClientRect(); if (r.top < 60) window.scrollBy(0, r.top - 70); }
    }
    tbody.addEventListener("click", function (e) {
      var tr = e.target.closest("tr"); if (!tr || !tr._row) return;
      if (e.target.closest("a,button,input,select,label,textarea")) return;
      if (window.getSelection && String(window.getSelection())) return;
      if (o.onRow) { o.onRow(tr._row, e); return; }
      if (o.rowHref) { var href = o.rowHref(tr._row); if (!href) return; if (e.ctrlKey || e.metaKey || e.shiftKey) window.open(href, "_blank"); else location.hash = href; }
    });
    tbody.addEventListener("auxclick", function (e) {
      if (e.button !== 1 || !o.rowHref) return; var tr = e.target.closest("tr"); if (!tr || !tr._row || e.target.closest("a")) return;
      var href = o.rowHref(tr._row); if (href) { e.preventDefault(); window.open(href, "_blank"); }
    });
    if (o.onHover) {
      var hovered = null;
      tbody.addEventListener("mouseover", function (e) { var tr = e.target.closest("tr"); var r = tr && tr._row || null; if (r !== hovered) { hovered = r; o.onHover(r); } });
      tbody.addEventListener("mouseleave", function () { if (hovered !== null) { hovered = null; o.onHover(null); } });
    }
    doSort(); renderHead(); renderBody();
    return {
      el: root,
      setRows: function (r) { rows = r || []; st.page = 1; doSort(); renderBody(); },
      setColumns: function (c) { cols = c || []; if (st.sortKey && !col(st.sortKey)) st.sortKey = null; doSort(); renderHead(); renderBody(); },
      sort: function (key, dir) { st.sortKey = key; st.sortDir = dir || null; st.page = 1; doSort(); renderHead(); renderBody(); },
      page: function (n) { go(n, false); },
      refresh: function () { doSort(); renderBody(); },
      rows: function () { return order; },
      state: function () { var c = st.sortKey && col(st.sortKey); return {sortKey: st.sortKey, sortDir: st.sortKey ? (st.sortDir || (c ? firstDir(c) : "asc")) : null, page: st.page, pageSize: st.pageSize}; }
    };
  };

  /* ------------------------------------------------------------------ filter bar */
  function normOptions(options) {
    return (options || []).map(function (o) {
      if (Array.isArray(o)) return {value: o[0], label: o[1] == null ? String(o[0]) : String(o[1])};
      if (o && typeof o === "object") return {value: o.value, label: o.label == null ? String(o.value) : String(o.label), color: o.color, title: o.title, count: o.count};
      return {value: o, label: String(o)};
    });
  }
  function isEmpty(v) { return v == null || v === "" || v === false || (Array.isArray(v) && (v.length === 0 || (v.length === 2 && v[0] == null && v[1] == null))); }
  function same(a, b) { if (isEmpty(a) && isEmpty(b)) return true; return JSON.stringify(a) === JSON.stringify(b); }
  function clone(v) { return Array.isArray(v) ? v.slice() : v; }
  function isMultiDef(d) { return d.type === "multi" || (d.type === "chips" && d.multi); }
  var openPop = null;
  function closePop() { if (openPop) { openPop.classList.remove("open"); openPop = null; } }
  document.addEventListener("mousedown", function (e) { if (openPop && !openPop.contains(e.target)) closePop(); });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") closePop(); });

  ui.filters = function (el, defs, onChange) {
    defs = (defs || []).map(function (d) { var c = {}; for (var k in d) c[k] = d[k]; return c; });
    var vals = {}, initial = {}, setters = {}, optSetters = {}, bar = h("div.wg-filters");
    function fire(key) { if (onChange) onChange(api.values(), key); }
    defs.forEach(function (d) {
      var v = d.value, opts = normOptions(d.options);
      if (isMultiDef(d)) v = Array.isArray(v) ? v.slice() : (v == null || v === "" ? [] : [v]);
      else if (d.type === "range") v = Array.isArray(v) ? [v[0] == null || v[0] === "" ? null : +v[0], v[1] == null || v[1] === "" ? null : +v[1]] : [null, null];
      else if (d.type === "toggle") v = !!v;
      else if (d.type === "number") v = v == null || v === "" ? null : +v;
      else if (d.type === "text") v = v == null ? "" : String(v);
      else if (v == null) v = "";
      vals[d.key] = v; initial[d.key] = clone(v);
      if (d.group) bar.appendChild(h("div.wg-filter-group", d.group));
      var box = h("div.wg-filter.type-" + (d.type || "text"), {title: d.title || null}), ctl;
      if (d.hidden) box.style.display = "none";
      if (d.label && d.type !== "toggle") box.appendChild(h("span.wg-filter-label", d.label));

      if (d.type === "select") {
        ctl = h("select", {"aria-label": d.label || d.key, onchange: function () { vals[d.key] = opts[+ctl.value] ? opts[+ctl.value].value : ""; fire(d.key); }});
        var fillSel = function () { WG.clear(ctl); opts.forEach(function (o, i) { ctl.appendChild(h("option", {value: i}, o.label + (o.count != null ? " (" + ui.num(o.count) + ")" : ""))); }); };
        fillSel();
        setters[d.key] = function (val) { var i = indexOfValue(opts, val); ctl.value = i < 0 ? 0 : i; vals[d.key] = i < 0 ? (opts[0] ? opts[0].value : "") : opts[i].value; };
        optSetters[d.key] = function (o2) { opts = normOptions(o2); fillSel(); setters[d.key](vals[d.key]); };
        setters[d.key](v);
        if (d.width) ctl.style.width = d.width + "px";
        box.appendChild(ctl);
      } else if (d.type === "chips") {
        var row = h("span.wg-chiprow", {role: "group", "aria-label": d.label || d.key});
        var paint = function () {
          WG.clear(row);
          opts.forEach(function (o) {
            var on = d.multi ? indexIn(vals[d.key], o.value) >= 0 : String(vals[d.key]) === String(o.value) && !(vals[d.key] === "" && o.value !== "");
            var chip = ui.chip(o.label, {color: o.color, title: o.title, active: on, count: o.count, onclick: function () {
              if (d.multi) { var arr = vals[d.key].slice(), i = indexIn(arr, o.value); if (i >= 0) arr.splice(i, 1); else arr.push(o.value); vals[d.key] = arr; }
              else if (on && !d.required && o.value !== "") vals[d.key] = "";
              else vals[d.key] = o.value;
              paint(); fire(d.key);
            }});
            chip.setAttribute("aria-pressed", on ? "true" : "false");
            row.appendChild(chip);
          });
        };
        setters[d.key] = function (val) { vals[d.key] = d.multi ? (Array.isArray(val) ? val.slice() : (isEmpty(val) ? [] : [val])) : (val == null ? "" : val); paint(); };
        optSetters[d.key] = function (o2) { opts = normOptions(o2); paint(); };
        paint();
        box.appendChild(row);
      } else if (d.type === "multi") {
        var btn = h("button.wg-multi-btn", {type: "button", "aria-haspopup": "true"}), list = h("div.wg-multi-list"), search = null;
        var pop = h("div.wg-multi", btn, h("div.wg-multi-pop", list));
        var label = function () {
          var sel = vals[d.key], names = opts.filter(function (o) { return indexIn(sel, o.value) >= 0; }).map(function (o) { return o.label; });
          WG.clear(btn);
          btn.appendChild(h("span", names.length === 0 ? (d.placeholder || "Any") : names.length <= 2 ? names.join(", ") : names.length + " selected"));
          btn.appendChild(h("span.caret", "▾"));
          btn.classList.toggle("has", names.length > 0);
        };
        var paintList = function (q) {
          WG.clear(list); q = (q || "").toLowerCase();
          opts.forEach(function (o) {
            if (q && o.label.toLowerCase().indexOf(q) < 0) return;
            var cb = h("input", {type: "checkbox", checked: indexIn(vals[d.key], o.value) >= 0 ? true : null, onchange: function () {
              var arr = vals[d.key].slice(), i = indexIn(arr, o.value);
              if (cb.checked && i < 0) arr.push(o.value); else if (!cb.checked && i >= 0) arr.splice(i, 1);
              vals[d.key] = arr; label(); fire(d.key);
            }});
            list.appendChild(h("label.wg-multi-opt", cb, h("span", {style: o.color ? {color: ui.color(o.color)} : null}, o.label), o.count != null ? h("span.wg-dim.count", ui.num(o.count)) : null));
          });
          if (!list.firstChild) list.appendChild(h("div.wg-dim.wg-multi-none", "No match"));
        };
        var head = h("div.wg-multi-head");
        if (opts.length > 12) { search = h("input", {type: "search", placeholder: "Filter the list", oninput: function () { paintList(search.value); }}); head.appendChild(search); }
        head.appendChild(h("button.wg-btn.small.ghost", {type: "button", onclick: function () { vals[d.key] = []; label(); paintList(search && search.value); fire(d.key); }}, "Clear"));
        pop.lastChild.insertBefore(head, list);
        btn.addEventListener("click", function () { var was = pop.classList.contains("open"); closePop(); if (!was) { pop.classList.add("open"); openPop = pop; paintList(search && search.value); if (search) search.focus(); } });
        setters[d.key] = function (val) { vals[d.key] = Array.isArray(val) ? val.slice() : (isEmpty(val) ? [] : [val]); label(); if (pop.classList.contains("open")) paintList(search && search.value); };
        optSetters[d.key] = function (o2) { opts = normOptions(o2); label(); paintList(search && search.value); };
        label();
        if (d.width) btn.style.minWidth = d.width + "px";
        box.appendChild(pop);
      } else if (d.type === "range") {
        var mk = function (i, ph) {
          var inp = h("input", {type: "number", min: d.min, max: d.max, step: d.step || 1, placeholder: ph, "aria-label": (d.label || d.key) + " " + ph});
          var upd = ui.debounce(function () { var n = inp.value === "" ? null : +inp.value; if (n !== null && isNaN(n)) n = null; var cur = vals[d.key].slice(); if (cur[i] === n) return; cur[i] = n; vals[d.key] = cur; fire(d.key); }, 180);
          inp.addEventListener("input", upd);
          inp.addEventListener("change", function () { upd.cancel(); var n = inp.value === "" ? null : +inp.value; var cur = vals[d.key].slice(); if (cur[i] === n) return; cur[i] = n; vals[d.key] = cur; fire(d.key); });
          return inp;
        };
        var lo = mk(0, "min"), hi = mk(1, "max");
        setters[d.key] = function (val) { val = Array.isArray(val) ? val : [null, null]; vals[d.key] = [val[0] == null || val[0] === "" ? null : +val[0], val[1] == null || val[1] === "" ? null : +val[1]]; lo.value = vals[d.key][0] == null ? "" : vals[d.key][0]; hi.value = vals[d.key][1] == null ? "" : vals[d.key][1]; };
        setters[d.key](v);
        box.appendChild(h("span.wg-range", lo, h("span.wg-dim", "to"), hi));
      } else if (d.type === "toggle") {
        var cbx = h("input", {type: "checkbox", onchange: function () { vals[d.key] = cbx.checked; fire(d.key); }});
        setters[d.key] = function (val) { vals[d.key] = !!val; cbx.checked = !!val; };
        setters[d.key](v);
        box.appendChild(h("label.wg-toggle", cbx, h("span.box"), h("span", d.label || d.key)));
      } else if (d.type === "number") {
        var ni = h("input", {type: "number", min: d.min, max: d.max, step: d.step || 1, placeholder: d.placeholder || "", "aria-label": d.label || d.key});
        var nupd = function () { var n = ni.value === "" ? null : +ni.value; if (n !== null && isNaN(n)) n = null; if (n === vals[d.key]) return; vals[d.key] = n; fire(d.key); };
        var nd = ui.debounce(nupd, 180);
        ni.addEventListener("input", nd); ni.addEventListener("change", function () { nd.cancel(); nupd(); });
        setters[d.key] = function (val) { vals[d.key] = val == null || val === "" ? null : +val; ni.value = vals[d.key] == null ? "" : vals[d.key]; };
        setters[d.key](v);
        if (d.width) ni.style.width = d.width + "px";
        box.appendChild(ni);
      } else {
        var ti = h("input", {type: "search", placeholder: d.placeholder || "", "aria-label": d.label || d.key, spellcheck: false});
        var tupd = function () { if (ti.value === vals[d.key]) return; vals[d.key] = ti.value; fire(d.key); };
        var td = ui.debounce(tupd, 160);
        ti.addEventListener("input", td); ti.addEventListener("change", function () { td.cancel(); tupd(); });
        ti.addEventListener("search", function () { td.cancel(); tupd(); });
        setters[d.key] = function (val) { vals[d.key] = val == null ? "" : String(val); ti.value = vals[d.key]; };
        setters[d.key](v);
        if (d.width) ti.style.width = d.width + "px";
        box.appendChild(ti);
      }
      d._box = box;
      bar.appendChild(box);
    });
    function indexOfValue(opts, val) { for (var i = 0; i < opts.length; i++) if (opts[i].value === val) return i; for (i = 0; i < opts.length; i++) if (String(opts[i].value) === String(val)) return i; return -1; }
    function indexIn(arr, val) { for (var i = 0; i < arr.length; i++) if (arr[i] === val || String(arr[i]) === String(val)) return i; return -1; }
    WG.clear(el); el.appendChild(bar);
    var api = {
      el: bar,
      values: function () { var o = {}; for (var k in vals) o[k] = clone(vals[k]); return o; },
      set: function (key, value) { if (setters[key]) setters[key](value); },
      setOptions: function (key, options) { if (optSetters[key]) optSetters[key](options); },
      show: function (key, visible) { defs.forEach(function (d) { if (d.key === key) d._box.style.display = visible === false ? "none" : ""; }); },
      reset: function () { for (var k in initial) setters[k](clone(initial[k])); fire(null); },
      query: function () {
        var q = {};
        defs.forEach(function (d) {
          var v = vals[d.key];
          if (("def" in d) ? same(v, d.def) : isEmpty(v)) return;
          if (d.type === "range") q[d.key] = (v[0] == null ? "" : v[0]) + "-" + (v[1] == null ? "" : v[1]);
          else if (Array.isArray(v)) q[d.key] = v.length ? v.join(",") : "-";
          else if (d.type === "toggle") q[d.key] = v ? "1" : "0";
          else q[d.key] = v === "" || v == null ? "-" : String(v);
        });
        return q;
      }
    };
    return api;
  };
  /* Fill def values from the route's query. Returns new defs. */
  ui.filters.fromQuery = function (defs, query) {
    query = query || {};
    return (defs || []).map(function (d) {
      var c = {}; for (var k in d) c[k] = d[k];
      if (!(d.key in query)) { if (c.value === undefined && "def" in d) c.value = clone(d.def); return c; }
      var s = String(query[d.key]), opts = normOptions(d.options);
      function typed(x) { for (var i = 0; i < opts.length; i++) if (String(opts[i].value) === x) return opts[i].value; return opts.length ? undefined : x; }
      if (d.type === "range") { var m = /^(-?\d*\.?\d*)-(-?\d*\.?\d*)$/.exec(s); c.value = m ? [m[1] === "" ? null : +m[1], m[2] === "" ? null : +m[2]] : [null, null]; }
      else if (isMultiDef(d)) c.value = s === "-" || s === "" ? [] : s.split(",").map(typed).filter(function (x) { return x !== undefined; });
      else if (d.type === "toggle") c.value = s === "1" || s === "true";
      else if (d.type === "number") c.value = s === "" || s === "-" || isNaN(+s) ? null : +s;
      else if (d.type === "select" || d.type === "chips") { var t = s === "-" ? "" : typed(s); c.value = t === undefined ? (("def" in d) ? d.def : d.value) : t; }
      else c.value = s === "-" ? "" : s;
      return c;
    });
  };

  /* ------------------------------------------------------------------ tabs */
  ui.tabs = function (el, tabs, opts) {
    opts = opts || {}; tabs = tabs || [];
    var strip = h("div.wg-tabs", {role: "tablist"}), panels = h("div.wg-tabpanels"), btns = {}, pans = {}, done = {}, active = null;
    tabs.forEach(function (t) {
      var b = h("button.wg-tab", {type: "button", role: "tab", onclick: function () { select(t.key, true); }}, h("span", t.label == null ? t.key : t.label));
      b._count = h("span.wg-tab-count"); b.appendChild(b._count);
      btns[t.key] = b; strip.appendChild(b);
      var p = h("div.wg-tabpanel", {role: "tabpanel"}); p.style.display = "none";
      pans[t.key] = p; panels.appendChild(p);
      setCount(t.key, t.count);
    });
    function setCount(key, n) { var b = btns[key]; if (!b) return; b._count.textContent = n == null ? "" : ui.num(n); b._count.style.display = n == null ? "none" : ""; b.classList.toggle("zero", n === 0); }
    function select(key, user) {
      if (!btns[key]) return;
      active = key;
      tabs.forEach(function (t) {
        var on = t.key === key; btns[t.key].classList.toggle("active", on); btns[t.key].setAttribute("aria-selected", on ? "true" : "false"); pans[t.key].style.display = on ? "" : "none";
        if (on && (!done[t.key] || opts.rerender)) {
          done[t.key] = true;
          if (opts.rerender) WG.clear(pans[t.key]);
          if (t.content) WG.append(pans[t.key], t.content);
          if (t.render) { try { var r = t.render(pans[t.key]); if (r && r.then) r.catch(function (e) { WG.showError(pans[t.key], e); }); } catch (e) { WG.showError(pans[t.key], e); } }
        }
      });
      if (user && opts.pref) WG.pref(opts.pref, key);
      if (user && opts.onChange) opts.onChange(key);
    }
    strip.addEventListener("keydown", function (e) {
      if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
      var keys = tabs.map(function (t) { return t.key; }), i = keys.indexOf(active) + (e.key === "ArrowRight" ? 1 : -1);
      if (i < 0 || i >= keys.length) return; e.preventDefault(); select(keys[i], true); btns[keys[i]].focus();
    });
    WG.clear(el); el.appendChild(strip); el.appendChild(panels);
    if (tabs.length) {
      var first = opts.active;
      if ((first == null || !btns[first]) && opts.pref) first = WG.pref(opts.pref);
      if (first == null || !btns[first]) { var nz = tabs.filter(function (t) { return t.count !== 0; })[0]; first = (nz || tabs[0]).key; }
      select(first, false);
    }
    return {el: strip, select: function (k) { select(k, false); }, active: function () { return active; }, setCount: setCount, panel: function (k) { return pans[k] || null; }};
  };

  /* ------------------------------------------------------------------ monsters: stance and rank
     The game's own words for how a monster stands towards you: aggressive, hostile, neutral, friendly
     (GameNPC.GetAggroLevelString; the owner asked for these words). Data: mobs.md 5a.
     M-D gives `st` = [aggressive, hostile, neutral, friendly] spawn-point counts on kinds, camps, zone rows and
     neighbour entries, and a single index 0..3 on a spawn point.
     ui.stanceInfo(o) -> {word, n, of} or null: the word to show, and when the thing is mixed how many of how many
     are aggressive. o = a word; an index 0..3; a counts array; or an object with `st` (counts or index), or
     `stance` (a word), or only the older `ag` + `n` / `spawns` (+ `faction`) fields. */
  var STANCES = ["aggressive", "hostile", "neutral", "friendly"];
  ui.STANCES = STANCES.slice();
  function factionOf(id) {
    var m = WG.data.mobs; if (!id || !m || !m.factions) return null;
    try { var rows = m.factions.cols ? WG.rows(m.factions) : m.factions; return WG.index(rows, "id").get(id) || null; } catch (e) { return null; }
  }
  function fromCounts(c) {
    var total = (+c[0] || 0) + (+c[1] || 0) + (+c[2] || 0) + (+c[3] || 0);
    if (!total) return null;
    if (c[0] > 0) return {word: "aggressive", n: c[0], of: total};          // any aggressive member is what matters
    var best = 1; if (c[2] > c[best]) best = 2; if (c[3] > c[best]) best = 3;
    return {word: STANCES[best], n: c[best], of: total};
  }
  ui.stanceInfo = function (o) {
    if (o == null || o === "") return null;
    if (typeof o === "string") { var lw = o.toLowerCase(); return STANCES.indexOf(lw) >= 0 ? {word: lw, n: 1, of: 1} : null; }
    if (typeof o === "number") return STANCES[o] ? {word: STANCES[o], n: 1, of: 1} : null;
    if (Array.isArray(o)) return o.length === 4 ? fromCounts(o) : null;
    if (Array.isArray(o.st) && o.st.length === 4) return fromCounts(o.st);
    if (typeof o.st === "number" && STANCES[o.st]) return {word: STANCES[o.st], n: 1, of: 1};
    var total = o.spawns != null ? o.spawns : (o.n != null ? o.n : 1), ag = o.ag || 0, w = typeof o.stance === "string" ? o.stance.toLowerCase() : "";
    if (ag > 0) return {word: "aggressive", n: Math.min(ag, total), of: total};
    if (STANCES.indexOf(w) >= 0) return {word: w, n: total, of: total};
    if (!("ag" in o) && !o.faction) return null;                            // nothing is known about it (a script-made monster)
    var fa = factionOf(o.faction), fw = fa && fa.standing ? String(fa.standing).toLowerCase() : "";
    return {word: fw && fw !== "aggressive" && STANCES.indexOf(fw) >= 0 ? fw : "neutral", n: total, of: total};
  };
  ui.stanceWord = function (o) { var i = ui.stanceInfo(o); return i ? i.word : ""; };
  var STANCE_TIP = {
    aggressive: "Aggressive: comes at you unprovoked when you are in its aggro range (never when it is grey to you)",
    hostile: "Hostile: its faction dislikes you, but it does not attack first",
    neutral: "Neutral: does not attack first",
    friendly: "Friendly: its faction likes you; it does not attack first"
  };
  /* <span class="wg-stance aggressive">aggressive</span>. When only some are aggressive the share is added
     ("aggressive 3 of 7"); opts.count === false leaves it out; opts.cap capitalises. Empty span when unknown. */
  ui.stance = function (o, opts) {
    opts = opts || {};
    var i = ui.stanceInfo(o); if (!i) return h("span");
    var el = h("span.wg-stance." + i.word, opts.cap ? i.word.charAt(0).toUpperCase() + i.word.slice(1) : i.word);
    if (i.word === "aggressive" && i.n < i.of) {
      el.title = ui.num(i.n) + " of " + ui.num(i.of) + " are aggressive and come at you unprovoked; the others do not attack first";
      if (opts.count !== false) el.appendChild(h("span.part", " " + ui.num(i.n) + " of " + ui.num(i.of)));
    } else el.title = STANCE_TIP[i.word];
    return el;
  };
  ui.stanceText = function (o) { var i = ui.stanceInfo(o); if (!i) return ""; return i.word === "aggressive" && i.n < i.of ? i.n + " of " + i.of + " aggressive" : i.word; };
  /* Rank of a monster kind (mobs.md 5c): "event" (final boss of a realm event), "epic" (raid boss class), "boss",
     "quest", "named" or "" for an ordinary monster. From kind.rank when the data has it, else from the flag bits
     (named 1, epic 4, boss 8). ui.rankBadge(kind) -> a small badge for event / epic / boss (and for quest and
     named with opts.all), else null; its tooltip is the data's own reason (rankWhy). */
  ui.rank = function (kind) {
    if (!kind) return "";
    if (typeof kind.rank === "string") return kind.rank === "normal" ? "" : kind.rank.toLowerCase();
    var f = kind.f || 0;
    return f & 8 ? "boss" : f & 4 ? "epic" : f & 1 ? "named" : "";
  };
  var RANK_LABEL = {event: "Realm event boss", epic: "Epic boss", boss: "Boss", quest: "Quest", named: "Named"};
  ui.rankLabel = function (r) { return RANK_LABEL[r] || (r ? r.charAt(0).toUpperCase() + r.slice(1) : ""); };
  ui.rankBadge = function (kind, opts) {
    var r = ui.rank(kind); if (!r) return null;
    if ((r === "named" || r === "quest") && !(opts && (opts.all || opts.named))) return null;
    return h("span.wg-badge.rank-" + r.replace(/[^a-z0-9]+/g, "-"), {title: kind.rankWhy || null}, opts && opts.short && r === "event" ? "Event boss" : ui.rankLabel(r));
  };

  /* Keep --header-h right (sticky table heads sit under the page header). */
  function measureHeader() { var hd = document.getElementById("wg-header"); if (hd) document.documentElement.style.setProperty("--header-h", hd.offsetHeight + "px"); }
  ui.measureHeader = measureHeader;
  window.addEventListener("resize", measureHeader);
  setTimeout(measureHeader, 0); setTimeout(measureHeader, 400);
})();
