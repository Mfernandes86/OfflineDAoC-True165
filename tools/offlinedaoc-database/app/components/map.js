/* OFFLINEDAOC Database - the zone map viewer (P-U). Signatures: source/contract/widgets.md section 1.
   Coordinates: zone-local game units (the /loc numbers). The image placement rule is P-D's (source/contract/zones.md)
   and lives in geom() below, nowhere else. Classic script. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = WG.ui, M = (WG.Map = WG.Map || {});

  var KINDS = {
    spawn:  {order: 0, color: "pin",    name: "Spawn point"},
    camp:   {order: 1, color: "pin",    name: "Camp (the number is how many spawn there)"},
    vendor: {order: 2, color: "vendor", name: "Merchant"},
    stable: {order: 3, color: "stable", name: "Stable master"},
    danger: {order: 4, color: "danger", name: "Stronger monsters close by"},
    point:  {order: 5, color: "point",  name: "Place"}
  };

  /* ---------------------------------------------------------------- zones and the coordinate rule */
  function zoneList() { var d = WG.data.zones; if (!d) return null; var z = d.zones || d; return z && z.cols ? WG.rows(z) : (Array.isArray(z) ? z : null); }
  function zoneById(id) { var l = zoneList(); if (!l) return null; var ix = WG.index(l, "id"); return ix.get(+id) || ix.get(String(id)) || null; }
  M.zoneById = zoneById;
  function resolveZone(z) {
    if (z == null) return null;
    if (typeof z === "object") return z;
    var m = /^r(\d+)$/.exec(String(z));
    return m ? M.overview(+m[1]) : zoneById(z);
  }
  /* P-D's rule (source/contract/zones.md section 1): every picture has px = [W, H] and rect = [x0, y0, w, h] in
     zone-local game units;  pixel = ((x - rect[0]) * px[0] / rect[2], (y - rect[1]) * px[1] / rect[3]);  x east,
     y down, no flip, no margin. In capitals and dungeons rect is NOT the whole zone, so the picture's own rect is
     always used. geom() turns a zone and the chosen layer into: the zone square (W, H), the square to show first
     (zone.view), where the picture sits (ix0..ix1, iy0..iy1) and the area the camera may roam (bx0..by1). */
  /* The pictures to choose from: the zone's own layers, or (7 stacked dungeons) those of one floor.
     floor = index into zone.floors, or -1 / null for the whole zone. */
  function floorsOf(z) { return z && Array.isArray(z.floors) ? z.floors.filter(function (f) { return f && (f.src || (f.layers && f.layers.length)); }) : []; }
  function layersOf(z, floor) {
    if (!z) return [];
    var out = [], f = floor != null && floor >= 0 ? floorsOf(z)[floor] : null;
    if (f) {
      if (Array.isArray(f.layers)) f.layers.forEach(function (l) { if (l && l.src) out.push(l); });
      if (!out.length && f.src) out.push({id: "game", label: "Game map", src: f.src, px: f.px, rect: f.rect});
      if (out.length) return out;
    }
    if (Array.isArray(z.layers)) z.layers.forEach(function (l) { if (l && l.src) out.push(l); });
    if (!out.length && z.map) out.push({id: "map", label: "Map", src: z.map, px: z.px, rect: z.rect});
    return out;
  }
  function geom(z, layer, floor) {
    var W = z && +z.w > 0 ? +z.w : 65536, H = z && +z.h > 0 ? +z.h : 65536;
    var ls = layersOf(z, floor), l = ls[layer || 0] || ls[0] || null;
    var g = {W: W, H: H, src: l ? l.src : null, ix0: 0, iy0: 0, ix1: W, iy1: H, pw: 0, ph: 0, vx: 0, vy: 0, vw: W, vh: H, layers: ls, layer: l, floors: floorsOf(z)};
    if (l) {
      if (l.px) { g.pw = +l.px[0]; g.ph = +l.px[1]; }
      if (Array.isArray(l.rect) && l.rect.length === 4 && l.rect[2] > 0 && l.rect[3] > 0) { g.ix0 = +l.rect[0]; g.iy0 = +l.rect[1]; g.ix1 = g.ix0 + (+l.rect[2]); g.iy1 = g.iy0 + (+l.rect[3]); }
    }
    if (z && Array.isArray(z.view) && z.view.length === 4 && z.view[2] > 0 && z.view[3] > 0) { g.vx = +z.view[0]; g.vy = +z.view[1]; g.vw = +z.view[2]; g.vh = +z.view[3]; }
    g.bx0 = Math.min(0, g.ix0, g.vx); g.by0 = Math.min(0, g.iy0, g.vy);
    g.bx1 = Math.max(W, g.ix1, g.vx + g.vw); g.by1 = Math.max(H, g.iy1, g.vy + g.vh);
    return g;
  }
  M.geom = geom;
  M.layers = function (zone) { return layersOf(resolveZone(zone)); };
  M.project = function (zone, x, y, layer) {
    var g = geom(resolveZone(zone), layer), pw = g.pw || 2048, ph = g.ph || 2048;
    return [(x - g.ix0) * pw / (g.ix1 - g.ix0), (y - g.iy0) * ph / (g.iy1 - g.iy0)];
  };
  /* Region overviews (zones.md section 4). M.overview(regionId) gives a zone-like object the viewer takes as `zone`
     (so does zone: "r<regionId>"). On an overview a pin or line may carry `zone` (a zone id): its x, y are then that
     zone's /loc numbers and the viewer places them; a line point may be [x, y, zoneId]. Without `zone`, x and y are
     overview-local (region X - overview.rect[0], region Y - overview.rect[1]). */
  M.overview = function (regionId) {
    var d = WG.data.zones, list = d && d.overviews || [];
    for (var i = 0; i < list.length; i++) if (String(list[i].id) === String(regionId)) {
      var o = list[i];
      if (!o._zone) o._zone = {id: "r" + o.id, name: o.name, realm: o.realm, w: o.rect[2], h: o.rect[3], map: o.src, px: o.px, rect: [0, 0, o.rect[2], o.rect[3]], view: [0, 0, o.rect[2], o.rect[3]], overview: o};
      return o._zone;
    }
    return null;
  };
  M.overviewOf = function (zoneId) {
    var d = WG.data.zones, list = d && d.overviews || [];
    for (var i = 0; i < list.length; i++) for (var j = 0; j < list[i].zones.length; j++) if (String(list[i].zones[j].id) === String(zoneId)) return list[i];
    return null;
  };
  M.toOverview = function (ov, zoneId, x, y) {
    var z = zoneById(zoneId); if (!z || !ov) return null;
    return [z.ox + (+x) - ov.rect[0], z.oy + (+y) - ov.rect[1]];
  };
  /* The real zone under an overview-local position: {zone, x, y} or null. */
  M.fromOverview = function (ov, x, y) {
    if (!ov) return null;
    var X = x + ov.rect[0], Y = y + ov.rect[1];
    for (var i = 0; i < ov.zones.length; i++) { var z = zoneById(ov.zones[i].id); if (z && X >= z.ox && Y >= z.oy && X < z.ox + z.w && Y < z.oy + z.h) return {zone: z, x: X - z.ox, y: Y - z.oy}; }
    return null;
  };
  M.loc = function (zone, x, y) {
    var z = resolveZone(zone);
    return (z && z.name ? z.name + ": " : "") + "loc=" + Math.round(x) + "," + Math.round(y);
  };
  M.groupByZone = function (places) {
    var by = {}, out = [];
    (places || []).forEach(function (p) { if (!p || p.zone == null) return; var g = by[p.zone]; if (!g) { g = by[p.zone] = {zone: p.zone, pins: [], lines: []}; out.push(g); } g.pins.push(p); });
    return out;
  };

  var imgCache = {};
  function loadImage(src, cb) {
    var e = imgCache[src];
    if (e) { if (e.state === "loading") e.cbs.push(cb); else cb(e); return e; }
    e = imgCache[src] = {state: "loading", img: new Image(), cbs: [cb]};
    e.img.onload = function () { e.state = "ok"; e.cbs.splice(0).forEach(function (f) { f(e); }); };
    e.img.onerror = function () { e.state = "error"; e.cbs.splice(0).forEach(function (f) { f(e); }); };
    e.img.src = src;
    return e;
  }
  function luminance(c) {
    var m = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(c || "");
    if (!m) { var r = /rgba?\((\d+)[, ]+(\d+)[, ]+(\d+)/.exec(c || ""); if (!r) return 0.3; return (0.299 * r[1] + 0.587 * r[2] + 0.114 * r[3]) / 255; }
    var x = m[1]; if (x.length === 3) x = x[0] + x[0] + x[1] + x[1] + x[2] + x[2];
    return (0.299 * parseInt(x.slice(0, 2), 16) + 0.587 * parseInt(x.slice(2, 4), 16) + 0.114 * parseInt(x.slice(4, 6), 16)) / 255;
  }
  function rgba(c, a) {
    var m = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(c || "");
    if (!m) return c;
    var x = m[1]; if (x.length === 3) x = x[0] + x[0] + x[1] + x[1] + x[2] + x[2];
    return "rgba(" + parseInt(x.slice(0, 2), 16) + "," + parseInt(x.slice(2, 4), 16) + "," + parseInt(x.slice(4, 6), 16) + "," + a + ")";
  }
  function niceStep(min) { var steps = [100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000]; for (var i = 0; i < steps.length; i++) if (steps[i] >= min) return steps[i]; return 100000; }
  function swatch(kind, color) {
    var s = h("span.wg-map-swatch.k-" + kind); s.style.setProperty("--c", color); return s;
  }

  /* ---------------------------------------------------------------- the viewer */
  M.create = function (el, opts) {
    opts = opts || {};
    var mini = !!opts.mini, height = opts.height || (mini ? 220 : 560), k = mini ? 0.85 : 1;
    var canvas = h("canvas", {tabIndex: 0, "aria-label": "Zone map. Drag to move, wheel to zoom."}), ctx = canvas.getContext("2d");
    var titleEl = h("div.wg-map-title"), msgEl = h("div.wg-map-msg");
    var labelHover = h("div.wg-map-label"), labelFocus = h("div.wg-map-label.focus");
    var ctl = h("div.wg-map-ctl"), legendEl = h("div.wg-map-legend"), locEl = h("span.wg-map-loc"), markEl = h("span.wg-map-mark");
    var copyBtn = h("button.wg-btn.small", {type: "button", title: "Copy the marked place (or the place under the cursor) in the game's /loc form"}, "Copy /loc");
    var clearBtn = h("button.wg-btn.small.ghost", {type: "button", title: "Remove the mark"}, "Clear mark");
    var hintEl = h("span.wg-map-hint.wg-dim", "Drag to move, wheel to zoom, click to mark a place");
    /* The picture is an <img> under a transparent canvas (moved with a CSS transform): panning stays smooth and
       the pins are drawn alone. (Drawing the picture into the canvas together with text also comes out black in
       headless Edge with --disable-gpu, which is what source/shot.py uses.) */
    var landEl = h("div.wg-map-land"), imgEl = h("img.wg-map-img", {alt: "", draggable: false}), layerEl = h("span.wg-map-layers"), topEl = h("div.wg-map-top", titleEl, layerEl);
    var view = h("div.wg-map-view", {style: {height: height + "px"}}, landEl, imgEl, canvas, topEl, msgEl, ctl, labelHover, labelFocus);
    var barEl = h("div.wg-map-bar", locEl, markEl, copyBtn, clearBtn, hintEl);
    if (opts.locBar === false) barEl.style.display = "none";
    var foot = h("div.wg-map-foot", legendEl, barEl);
    var root = h("div.wg-map" + (mini ? ".mini" : ""), view, mini ? null : foot);
    WG.clear(el); el.appendChild(root);

    var zone = null, zoneReq = null, G = geom(null), P = [], L = [], order = [];
    var cam = {x: G.W / 2, y: G.H / 2, s: 0.008}, cw = 0, ch = 0, dpr = 1, fitS = 0.008, minS = 0.004, maxS = 0.35;
    var hover = -1, hoverLine = -1, focus = -1, hl = -1, mark = null, cursor = null, gridOn = null;
    var hidden = {}, layersOff = {}, imgE = null, needFit = true, pendingFocus = null, raf = 0, anim = null, pulse = 0, destroyed = false;
    var userPins = [], userLines = [], legendDef = opts.legend, layerIx = 0, floorIx = -1, floorZ = null;

    /* ---- controls */
    function cbtn(text, title, fn, cls) { var b = h("button" + (cls ? "." + cls : ""), {type: "button", title: title, "aria-label": title, onclick: function (e) { e.stopPropagation(); fn(); canvas.focus({preventScroll: true}); }}, text); ctl.appendChild(b); return b; }
    var gridBtn = null;
    if (!mini && opts.controls !== false) {
      cbtn("+", "Zoom in", function () { zoomAt(cw / 2, ch / 2, 1.6, true); });
      cbtn("−", "Zoom out", function () { zoomAt(cw / 2, ch / 2, 1 / 1.6, true); });
      cbtn("Zone", "Show the whole zone", function () { api.fit(); }, "wide");
      cbtn("Pins", "Zoom to the pins", function () { api.fitPins(); }, "wide");
      gridBtn = cbtn("Grid", "Show or hide the /loc grid", function () { gridOn = !showGrid(); gridBtn.classList.toggle("on", gridOn); redraw(); }, "wide");
    }
    function showGrid() { return gridOn == null ? !(imgE && imgE.state !== "error") : gridOn; }   // grid by default only where there is no picture

    /* ---- data */
    function pinKey(kind, color) { return kind + "|" + color; }
    function prep() {
      var ov = zone && zone.overview || null;
      function place(zid, x, y) {   // on an overview, a position given with its zone id is moved to overview-local
        if (zid == null || !ov) return [+x, +y];
        var q = M.toOverview(ov, zid, x, y); return q || [NaN, NaN];
      }
      P = userPins.map(function (p, i) {
        var kind = KINDS[p.kind] ? p.kind : "point", K = KINDS[kind], at = place(p.zone, p.x, p.y);
        var cName = p.color == null || p.color === "" ? K.color : p.color, c = ui.color(cName, true) || "#ff2d2d";
        var n = +p.n || 0, r;
        if (kind === "camp") r = n <= 1 ? 6 : n < 5 ? 8 : n < 20 ? 9.5 : n < 100 ? 11 : 12.5;
        else if (kind === "spawn") r = 3.2; else if (kind === "vendor") r = 7; else if (kind === "stable") r = 6.5; else if (kind === "danger") r = 8.5; else r = 6;
        return {p: p, i: i, x: at[0], y: at[1], z: p.z == null ? null : +p.z, kind: kind, c: c, cName: String(cName), r: r * k, n: n, rad: +p.radius || 0, key: p.group != null ? "g|" + p.group : pinKey(kind, cName), dark: luminance(c) > 0.6, layer: p.layer || null};
      });
      L = userLines.map(function (l, i) {
        var cName = l.color == null || l.color === "" ? "stable" : l.color, pts = [];
        (l.pts || []).forEach(function (pt) { var q = place(pt[2] != null ? pt[2] : l.zone, pt[0], pt[1]); if (isFinite(q[0]) && isFinite(q[1])) pts.push(q); });
        return {l: l, i: i, pts: pts, c: ui.color(cName, true) || "#5ad1c8", cName: String(cName), w: l.width || 2, dashed: !!l.dashed, arrow: !!l.arrow, key: "line|" + cName + "|" + (l.dashed ? 1 : 0), layer: l.layer || null};
      });
      order = P.map(function (_, i) { return i; }).filter(function (i) { return isFinite(P[i].x) && isFinite(P[i].y); });
      order.sort(function (a, b) { var A = P[a], B = P[b]; return (KINDS[A.kind].order - KINDS[B.kind].order) || (B.n - A.n) || (a - b); });
      buildLegend();
    }
    /* A pin with a height (pin.z) is shown only on its floor when a floor is picked (P-D: z0 < Z <= z1). */
    function visible(e) { return !hidden[e.key] && !(e.layer && layersOff[e.layer]) && !(floorZ && e.z != null && !(e.z > floorZ[0] && e.z <= floorZ[1])); }
    function buildLegend() {
      WG.clear(legendEl);
      if (mini || legendDef === false) return;
      var seen = {}, items = [];
      if (Array.isArray(legendDef)) legendDef.forEach(function (d) { var kind = d.kind || "point", cName = d.color == null ? (KINDS[kind] ? KINDS[kind].color : "stable") : d.color; var key = d.group != null ? "g|" + d.group : kind === "line" ? "line|" + cName + "|" + (d.dashed ? 1 : 0) : pinKey(kind, cName); if (!seen[key]) { seen[key] = {key: key, kind: kind, color: cName, label: d.label, dashed: d.dashed, count: 0}; items.push(seen[key]); } });
      P.forEach(function (e) {
        var s = seen[e.key];
        if (!s) { if (Array.isArray(legendDef)) return; s = seen[e.key] = {key: e.key, kind: e.kind, color: e.cName, label: null, count: 0}; items.push(s); }
        if (!s.label && e.p.legend) s.label = e.p.legend;
        s.count++;
      });
      L.forEach(function (e) {
        var s = seen[e.key];
        if (!s) { if (Array.isArray(legendDef)) return; s = seen[e.key] = {key: e.key, kind: "line", color: e.cName, dashed: e.dashed, label: null, count: 0}; items.push(s); }
        if (!s.label && e.l.legend) s.label = e.l.legend;
        s.count++;
      });
      if (!Array.isArray(legendDef)) items.sort(function (a, b) { return ((KINDS[a.kind] ? KINDS[a.kind].order + 1 : 9) - (KINDS[b.kind] ? KINDS[b.kind].order + 1 : 9)); });
      items.forEach(function (s) {
        var label = s.label || (s.kind === "line" ? (s.dashed ? "Route (dashed)" : "Route") : KINDS[s.kind].name);
        var b = h("button.wg-map-leg" + (hidden[s.key] ? ".off" : ""), {type: "button", title: "Click to hide or show these on the map", onclick: function () { hidden[s.key] = !hidden[s.key]; b.classList.toggle("off", !!hidden[s.key]); if (focus >= 0 && !visible(P[focus])) setFocus(-1); redraw(); }},
          swatch(s.kind + (s.dashed ? ".dashed" : ""), ui.color(s.color)), h("span", label), s.count ? h("span.n", ui.num(s.count)) : null);
        legendEl.appendChild(b);
      });
    }
    function pickLayer(ls, id) { if (id != null) for (var i = 0; i < ls.length; i++) if (ls[i].id === id) return i; return -1; }
    function setZone(z) {
      zone = z; needFit = true; mark = null; floorIx = -1;
      var ls = layersOf(z, -1), a = pickLayer(ls, opts.layer);
      if (a < 0) a = pickLayer(ls, WG.pref("map.layer." + (z && z.kind || "zone")));   // the owner's own choice for this kind of zone
      if (a < 0) a = pickLayer(ls, opts.preferLayer);                                    // the page's suggestion, used until he chooses
      layerIx = a < 0 ? 0 : a;
      WG.clear(titleEl);
      if (opts.title !== false && !mini) {
        var t = opts.title || (z && z.name) || "";
        if (t) titleEl.appendChild(z && typeof z.id === "number" && !opts.title && WG.route().view !== "zones" ? h("a", {href: WG.href("zones/" + z.id), title: "Open the zone page"}, t) : h("span", t));
      }
      prep();   // an overview moves pins that carry a zone id, so the pins depend on the zone
      setLayer(layerIx, false);
    }
    function setFloor(fx) {   // -1 = the whole dungeon; keeps the kind of picture (plan / game map) when the floor has it
      var cur = G.layer ? G.layer.id : null;
      floorIx = fx;
      var a = pickLayer(layersOf(zone, fx), cur);
      setLayer(a < 0 ? 0 : a, false);
      if (focus >= 0 && P[focus] && !visible(P[focus])) setFocus(-1);
      if (opts.onFloor) opts.onFloor(fx, fx >= 0 ? G.floors[fx] : null);
    }
    function setLayer(ix, user) {
      layerIx = ix; G = geom(zone, ix, floorIx); imgE = null; imgEl.style.display = "none";
      floorZ = floorIx >= 0 && G.floors[floorIx] && Array.isArray(G.floors[floorIx].z) ? G.floors[floorIx].z : null;
      if (user && G.layer) WG.pref("map.layer." + (zone && zone.kind || "zone"), G.layer.id);
      WG.clear(layerEl);
      if (!mini && opts.layers !== false) {
        if (G.layers.length > 1) layerEl.appendChild(h("span.seg", G.layers.map(function (l, i) {
          return h("button" + (i === ix ? ".on" : ""), {type: "button", title: "Show this picture of the zone", onclick: function (e) { e.stopPropagation(); if (i !== layerIx) { setLayer(i, true); layout(); redraw(); } }}, l.label || l.id);
        })));
        if (G.floors.length) layerEl.appendChild(h("span.seg", [h("button" + (floorIx < 0 ? ".on" : ""), {type: "button", title: "The whole dungeon in one picture", onclick: function (e) { e.stopPropagation(); if (floorIx >= 0) { setFloor(-1); layout(); redraw(); } }}, "All floors")].concat(G.floors.map(function (f, i) {
          return h("button" + (i === floorIx ? ".on" : ""), {type: "button", title: "Show this floor. Places with a known height are shown only on their floor.", onclick: function (e) { e.stopPropagation(); if (i !== floorIx) { setFloor(i); layout(); redraw(); } }}, f.name || "Floor " + (i + 1));
        }))));
      }
      msgEl.textContent = "";
      if (G.src) {
        imgE = loadImage(G.src, function (e) {
          if (destroyed || imgE !== e) return;
          if (e.state === "error") msgEl.textContent = "The map picture for this zone could not be opened; places are drawn on a /loc grid.";
          else { imgEl.src = e.img.src; imgEl.style.width = e.img.naturalWidth + "px"; imgEl.style.height = e.img.naturalHeight + "px"; imgEl.style.display = "block"; placeImage(); }
          if (gridBtn) gridBtn.classList.toggle("on", showGrid());
          if (e.state === "error") redraw();   // the grid comes on; a picture that arrives needs no canvas work
        });
      } else if (zone) msgEl.textContent = "The game has no map picture for this zone. Places are drawn on a /loc grid with the zone's real proportions.";
      else if (zoneReq != null) msgEl.textContent = "Zone " + zoneReq + " is not in the zone data; places are drawn on a plain grid.";
      if (gridBtn) gridBtn.classList.toggle("on", showGrid());
    }
    function applyZone(req) {
      zoneReq = req;
      if (req == null) { setZone(null); after(); return; }
      if (typeof req === "object") { zoneReq = req.id; setZone(req); after(); return; }
      var z = resolveZone(req);
      if (z) { setZone(z); after(); return; }
      setZone(null); msgEl.textContent = "Loading the zone...";
      WG.load("zones").then(function () { if (destroyed || zoneReq !== req) return; setZone(resolveZone(req)); if (!zone) growToPins(); after(); },
        function () { if (destroyed || zoneReq !== req) return; msgEl.textContent = "The zone data is not built yet (data/zones.js); places are drawn on a plain grid."; growToPins(); after(); });
    }
    function growToPins() {   // no zone record: make the grid large enough for the pins
      var mx = 65536, my = 65536; P.forEach(function (e) { if (e.x > mx) mx = e.x; if (e.y > my) my = e.y; });
      G = geom({w: Math.ceil(mx / 8192) * 8192, h: Math.ceil(my / 8192) * 8192});
    }
    function after() {
      layout();
      if (pendingFocus != null && cw) { var pf = pendingFocus; pendingFocus = null; api.focus(pf.i, pf.o); }
      redraw(); updateBar();
    }

    /* ---- geometry */
    function layout() {
      var w = view.clientWidth, hh = view.clientHeight;
      if (!w || !hh) return;
      var d = window.devicePixelRatio || 1;
      if (w !== cw || hh !== ch || d !== dpr) { cw = w; ch = hh; dpr = d; canvas.width = Math.round(cw * dpr); canvas.height = Math.round(ch * dpr); canvas.style.width = cw + "px"; canvas.style.height = ch + "px"; }
      fitS = Math.min(cw / G.vw, ch / G.vh) * 0.97;
      minS = Math.min(fitS * 0.6, Math.min(cw / (G.bx1 - G.bx0), ch / (G.by1 - G.by0)) * 0.9); maxS = Math.max(0.2, fitS * 8);
      if (needFit) { needFit = false; cam.x = G.vx + G.vw / 2; cam.y = G.vy + G.vh / 2; cam.s = fitS; }
      clampCam();
    }
    function clampCam() {
      cam.s = Math.max(minS, Math.min(maxS, cam.s));
      cam.x = Math.max(G.bx0, Math.min(G.bx1, cam.x)); cam.y = Math.max(G.by0, Math.min(G.by1, cam.y));
    }
    function sx(gx) { return (gx - cam.x) * cam.s + cw / 2; }
    function sy(gy) { return (gy - cam.y) * cam.s + ch / 2; }
    function gx(px) { return (px - cw / 2) / cam.s + cam.x; }
    function gy(py) { return (py - ch / 2) / cam.s + cam.y; }
    function zoomAt(px, py, f, smooth) {
      var s2 = Math.max(minS, Math.min(maxS, cam.s * f)), wx = gx(px), wy = gy(py);
      var tx = wx - (px - cw / 2) / s2, ty = wy - (py - ch / 2) / s2;
      if (smooth) animate({x: tx, y: ty, s: s2}, 180); else { anim = null; cam.s = s2; cam.x = tx; cam.y = ty; clampCam(); redraw(); }
    }
    function animate(to, ms) {
      to.s = Math.max(minS, Math.min(maxS, to.s)); to.x = Math.max(G.bx0, Math.min(G.bx1, to.x)); to.y = Math.max(G.by0, Math.min(G.by1, to.y));
      anim = {from: {x: cam.x, y: cam.y, s: cam.s}, to: to, t0: performance.now(), ms: ms || 300};
      redraw();
    }
    function redraw() { if (!raf && !destroyed) raf = requestAnimationFrame(draw); }

    /* ---- drawing */
    function draw(now) {
      raf = 0; if (destroyed) return;
      if (!cw) { layout(); if (!cw) return; }
      now = performance.now();   // not the frame's own timestamp: that one can be older than the times taken in animate() and focus()
      if (anim) {
        var t = Math.max(0, Math.min(1, (now - anim.t0) / anim.ms)), e = t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
        // zoom is interpolated in log space so it feels even
        cam.s = Math.exp(Math.log(anim.from.s) + (Math.log(anim.to.s) - Math.log(anim.from.s)) * e);
        cam.x = anim.from.x + (anim.to.x - anim.from.x) * e; cam.y = anim.from.y + (anim.to.y - anim.from.y) * e;
        if (t >= 1) anim = null;
      }
      var s = cam.s, i, e2;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, cw, ch);
      var zx = sx(0), zy = sy(0), zw = G.W * s, zh = G.H * s;
      landEl.style.transform = "translate(" + zx.toFixed(2) + "px," + zy.toFixed(2) + "px)"; landEl.style.width = zw.toFixed(2) + "px"; landEl.style.height = zh.toFixed(2) + "px";
      placeImage();
      if (showGrid()) drawGrid(zx, zy, zw, zh);
      ctx.lineWidth = 1; ctx.strokeStyle = "rgba(255,255,255,.22)"; ctx.strokeRect(Math.round(zx) + 0.5, Math.round(zy) + 0.5, Math.round(zw), Math.round(zh));
      if (zone && zone.overview) drawOverviewZones();

      // lines
      ctx.lineJoin = "round"; ctx.lineCap = "round";
      for (i = 0; i < L.length; i++) {
        e2 = L[i]; if (!visible(e2) || e2.pts.length < 2) continue;
        var hot = hoverLine === i;
        ctx.beginPath();
        for (var j = 0; j < e2.pts.length; j++) { var X = sx(e2.pts[j][0]), Y = sy(e2.pts[j][1]); if (j) ctx.lineTo(X, Y); else ctx.moveTo(X, Y); }
        ctx.setLineDash([]); ctx.strokeStyle = "rgba(0,0,0,.65)"; ctx.lineWidth = e2.w + 2.5 + (hot ? 1.5 : 0); ctx.stroke();
        ctx.setLineDash(e2.dashed ? [8, 6] : []); ctx.strokeStyle = e2.c; ctx.lineWidth = e2.w + (hot ? 1.5 : 0); ctx.stroke();
        ctx.setLineDash([]);
        if (e2.arrow) arrowHead(e2);
      }
      // camp areas (real size)
      for (i = 0; i < order.length; i++) {
        e2 = P[order[i]]; if (!e2.rad || !visible(e2)) continue;
        var R = e2.rad * s; if (R < e2.r + 2) continue;
        var cx = sx(e2.x), cy = sy(e2.y); if (cx + R < 0 || cy + R < 0 || cx - R > cw || cy - R > ch) continue;
        ctx.beginPath(); ctx.arc(cx, cy, R, 0, 6.2832);
        ctx.fillStyle = rgba(e2.c, 0.13); ctx.fill(); ctx.lineWidth = 1.25; ctx.strokeStyle = rgba(e2.c, 0.7); ctx.stroke();
      }
      // pins
      ctx.textAlign = "center"; ctx.textBaseline = "middle";
      var top = focus >= 0 ? focus : -1, top2 = hl >= 0 ? hl : hover;
      for (i = 0; i < order.length; i++) { var oi = order[i]; if (oi === top || oi === top2) continue; e2 = P[oi]; if (visible(e2)) drawPin(e2, false); }
      if (top2 >= 0 && top2 !== top && P[top2] && visible(P[top2])) drawPin(P[top2], true);
      if (top >= 0 && P[top] && visible(P[top])) {
        var fp = P[top], fx = sx(fp.x), fy = sy(fp.y), age = Math.max(0, (now - pulse) / 1000);   // a frame time can be older than the performance.now() taken in focus()
        if (age < 1.8) { var ph = (age % 0.6) / 0.6; ctx.beginPath(); ctx.arc(fx, fy, fp.r + 4 + ph * 22, 0, 6.2832); ctx.strokeStyle = "rgba(255,255,255," + (0.85 * (1 - ph)).toFixed(3) + ")"; ctx.lineWidth = 2.5; ctx.stroke(); }
        ctx.beginPath(); ctx.arc(fx, fy, fp.r + 5, 0, 6.2832); ctx.strokeStyle = "rgba(0,0,0,.7)"; ctx.lineWidth = 4; ctx.stroke();
        ctx.strokeStyle = "#fff"; ctx.lineWidth = 2; ctx.stroke();
        drawPin(fp, true);
      }
      if (opts.showAll) drawAllLabels();
      if (mark) {
        var mx = Math.round(sx(mark[0])) + 0.5, my = Math.round(sy(mark[1])) + 0.5;
        ctx.beginPath(); ctx.moveTo(mx - 9, my); ctx.lineTo(mx - 3, my); ctx.moveTo(mx + 3, my); ctx.lineTo(mx + 9, my); ctx.moveTo(mx, my - 9); ctx.lineTo(mx, my - 3); ctx.moveTo(mx, my + 3); ctx.lineTo(mx, my + 9);
        ctx.strokeStyle = "rgba(0,0,0,.8)"; ctx.lineWidth = 4; ctx.stroke(); ctx.strokeStyle = "#fff"; ctx.lineWidth = 1.5; ctx.stroke();
      }
      if (!mini) drawScale();
      placeLabels();
      if (anim || (focus >= 0 && now - pulse < 1800)) redraw();
    }
    function placeImage() {   // the picture follows the camera by a CSS transform; no canvas work
      if (!(imgE && imgE.state === "ok" && imgE.img.naturalWidth) || !cw) return;
      imgEl.style.transform = "translate(" + sx(G.ix0).toFixed(2) + "px," + sy(G.iy0).toFixed(2) + "px) scale(" + ((G.ix1 - G.ix0) * cam.s / imgE.img.naturalWidth).toFixed(6) + "," + ((G.iy1 - G.iy0) * cam.s / imgE.img.naturalHeight).toFixed(6) + ")";
    }
    function drawGrid(zx, zy, zw, zh) {
      var s = cam.s, step = niceStep(80 / s), x0 = Math.max(0, Math.floor(gx(0) / step) * step), y0 = Math.max(0, Math.floor(gy(0) / step) * step), v, p;
      var hasImg = imgE && imgE.state === "ok";
      ctx.save(); ctx.beginPath(); ctx.rect(zx, zy, zw, zh); ctx.clip();
      ctx.lineWidth = 1; ctx.strokeStyle = hasImg ? "rgba(255,255,255,.22)" : "rgba(255,255,255,.085)";
      ctx.beginPath();
      for (v = x0; v <= G.W && sx(v) <= cw; v += step) { p = Math.round(sx(v)) + 0.5; ctx.moveTo(p, Math.max(0, zy)); ctx.lineTo(p, Math.min(ch, zy + zh)); }
      for (v = y0; v <= G.H && sy(v) <= ch; v += step) { p = Math.round(sy(v)) + 0.5; ctx.moveTo(Math.max(0, zx), p); ctx.lineTo(Math.min(cw, zx + zw), p); }
      ctx.stroke();
      ctx.font = "10px 'Segoe UI', Tahoma, sans-serif"; ctx.textBaseline = "top"; ctx.textAlign = "left";
      var ty = Math.max(zy, 0) + 3, tx = Math.max(zx, 0) + 3;
      for (v = x0; v <= G.W && sx(v) <= cw; v += step) { if (v === 0) continue; label(ui.num(v), sx(v) + 3, ty); }
      for (v = y0; v <= G.H && sy(v) <= ch; v += step) { if (v === 0) continue; label(ui.num(v), tx, sy(v) + 3); }
      ctx.restore();
      function label(t, X, Y) { if (hasImg) { ctx.strokeStyle = "rgba(0,0,0,.8)"; ctx.lineWidth = 3; ctx.strokeText(t, X, Y); } ctx.fillStyle = hasImg ? "rgba(255,255,255,.85)" : "rgba(255,255,255,.4)"; ctx.fillText(t, X, Y); }
    }
    function drawOverviewZones() {   // an overview has no text painted on it: outline each zone and write its name
      var ov = zone.overview, ux = ov.rect[2] / ov.px[0], uy = ov.rect[3] / ov.px[1];
      ctx.font = "600 11px 'Segoe UI', Tahoma, sans-serif"; ctx.textAlign = "left"; ctx.textBaseline = "top";
      for (var i = 0; i < ov.zones.length; i++) {
        var q = ov.zones[i], z = zoneById(q.id), x = sx(q.px[0] * ux), y = sy(q.px[1] * uy), w = q.px[2] * ux * cam.s, hh = q.px[3] * uy * cam.s;
        if (x > cw || y > ch || x + w < 0 || y + hh < 0) continue;
        ctx.lineWidth = 1; ctx.strokeStyle = "rgba(255,255,255,.16)"; ctx.strokeRect(Math.round(x) + 0.5, Math.round(y) + 0.5, Math.round(w), Math.round(hh));
        if (!z || w < 70) continue;
        var name = z.name; while (name.length > 3 && ctx.measureText(name).width > w - 8) name = name.slice(0, -2);
        if (name !== z.name) name += "…";
        ctx.strokeStyle = "rgba(0,0,0,.85)"; ctx.lineWidth = 3; ctx.strokeText(name, Math.max(x, 0) + 5, Math.max(y, 0) + 4);
        ctx.fillStyle = "rgba(255,255,255,.9)"; ctx.fillText(name, Math.max(x, 0) + 5, Math.max(y, 0) + 4);
      }
    }
    function arrowHead(e2) {
      var n = e2.pts.length, a = e2.pts[n - 2], b = e2.pts[n - 1], ax = sx(a[0]), ay = sy(a[1]), bx = sx(b[0]), by = sy(b[1]);
      var ang = Math.atan2(by - ay, bx - ax), len = 10 + e2.w;
      if (Math.abs(bx - ax) + Math.abs(by - ay) < 2) return;
      ctx.beginPath(); ctx.moveTo(bx, by); ctx.lineTo(bx - len * Math.cos(ang - 0.42), by - len * Math.sin(ang - 0.42)); ctx.lineTo(bx - len * Math.cos(ang + 0.42), by - len * Math.sin(ang + 0.42)); ctx.closePath();
      ctx.fillStyle = e2.c; ctx.strokeStyle = "rgba(0,0,0,.65)"; ctx.lineWidth = 1.5; ctx.stroke(); ctx.fill();
    }
    function drawPin(e, big) {
      var x = sx(e.x), y = sy(e.y), r = e.r + (big ? 1.5 : 0);
      if (x < -20 || y < -20 || x > cw + 20 || y > ch + 20) return;
      ctx.setLineDash([]);
      if (e.kind === "spawn") {
        ctx.beginPath(); ctx.arc(x, y, r, 0, 6.2832); ctx.fillStyle = e.c; ctx.fill(); ctx.lineWidth = 1; ctx.strokeStyle = "rgba(0,0,0,.85)"; ctx.stroke();
        return;
      }
      ctx.beginPath();
      if (e.kind === "vendor") { ctx.moveTo(x, y - r); ctx.lineTo(x + r, y); ctx.lineTo(x, y + r); ctx.lineTo(x - r, y); ctx.closePath(); }
      else if (e.kind === "stable") { var q = r * 0.9; ctx.rect(x - q, y - q, q * 2, q * 2); }
      else if (e.kind === "danger") { ctx.moveTo(x, y - r); ctx.lineTo(x + r * 0.95, y + r * 0.72); ctx.lineTo(x - r * 0.95, y + r * 0.72); ctx.closePath(); }
      else ctx.arc(x, y, r, 0, 6.2832);
      ctx.lineJoin = "round";
      ctx.strokeStyle = "rgba(0,0,0,.8)"; ctx.lineWidth = 3.5; ctx.stroke();
      if (e.kind === "point") { ctx.fillStyle = "rgba(0,0,0,.45)"; ctx.fill(); ctx.strokeStyle = e.c; ctx.lineWidth = 2.2; ctx.stroke(); ctx.beginPath(); ctx.arc(x, y, 1.6, 0, 6.2832); ctx.fillStyle = e.c; ctx.fill(); return; }
      ctx.fillStyle = e.c; ctx.fill();
      ctx.strokeStyle = e.dark ? "rgba(255,255,255,.95)" : "rgba(255,255,255,.92)"; ctx.lineWidth = 1.2; ctx.stroke();
      var txt = e.kind === "camp" ? (e.n > 1 ? (e.n > 999 ? "999+" : String(e.n)) : "") : e.kind === "danger" ? "!" : "";
      if (txt) {
        ctx.font = "700 " + (txt.length > 2 ? 9 : 10.5) * k + "px 'Segoe UI', Tahoma, sans-serif"; ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillStyle = e.dark ? "#14161a" : "#fff";
        ctx.fillText(txt, x, y + (e.kind === "danger" ? 2 : 0.5));
      }
    }
    function drawAllLabels() {
      var placed = [], shown = 0, list = order.slice().reverse();
      ctx.font = "11px 'Segoe UI', Tahoma, sans-serif"; ctx.textAlign = "left"; ctx.textBaseline = "middle";
      for (var i = 0; i < list.length && shown < 120; i++) {
        var e = P[list[i]]; if (!visible(e) || !e.p.label || e.kind === "spawn") continue;
        var x = sx(e.x) + e.r + 4, y = sy(e.y); if (x < 0 || y < 8 || x > cw - 20 || y > ch - 8) continue;
        var w = ctx.measureText(e.p.label).width, box = [x - 2, y - 7, x + w + 2, y + 7], hit = false;
        for (var j = 0; j < placed.length; j++) { var b = placed[j]; if (box[0] < b[2] && box[2] > b[0] && box[1] < b[3] && box[3] > b[1]) { hit = true; break; } }
        if (hit) continue;
        placed.push(box); shown++;
        ctx.strokeStyle = "rgba(0,0,0,.85)"; ctx.lineWidth = 3; ctx.strokeText(e.p.label, x, y); ctx.fillStyle = "#fff"; ctx.fillText(e.p.label, x, y);
      }
    }
    function drawScale() {
      var units = niceStep(70 / cam.s), w = units * cam.s; if (w > cw * 0.5) return;
      var x = cw - 12 - w, y = ch - 14;
      ctx.beginPath(); ctx.moveTo(x, y - 4); ctx.lineTo(x, y); ctx.lineTo(x + w, y); ctx.lineTo(x + w, y - 4);
      ctx.strokeStyle = "rgba(0,0,0,.8)"; ctx.lineWidth = 3.5; ctx.stroke(); ctx.strokeStyle = "#fff"; ctx.lineWidth = 1.5; ctx.stroke();
      ctx.font = "10px 'Segoe UI', Tahoma, sans-serif"; ctx.textAlign = "center"; ctx.textBaseline = "bottom";
      var t = ui.num(units) + " units";
      ctx.strokeStyle = "rgba(0,0,0,.8)"; ctx.lineWidth = 3; ctx.strokeText(t, x + w / 2, y - 3); ctx.fillStyle = "#fff"; ctx.fillText(t, x + w / 2, y - 3);
    }

    /* ---- labels (HTML, so the text is crisp and can hold a link) */
    function fillLabel(elm, e, isFocus) {
      WG.clear(elm);
      var p = e.p;
      elm.appendChild(h("div.t", p.label || (KINDS[e.kind].name)));
      if (p.sub) String(p.sub).split("\n").forEach(function (line) { elm.appendChild(h("div.s", line)); });
      if (isFocus) {
        var row = h("div.a", h("span.wg-dim", locText([e.x, e.y], true)));
        if (p.href) row.appendChild(h("a", {href: p.href}, "Open"));
        row.appendChild(h("button", {type: "button", title: "Close this label", onclick: function (ev) { ev.stopPropagation(); setFocus(-1); redraw(); }}, "×"));
        elm.appendChild(row);
      } else if (p.href && !opts.onPin) elm.appendChild(h("div.s.wg-dim", "Click to open"));
    }
    function putLabel(elm, x, y, r) {
      var w = elm.offsetWidth, hh = elm.offsetHeight, left = x - w / 2, top = y - r - 8 - hh;
      if (top < 4) top = y + r + 8;
      left = Math.max(4, Math.min(cw - w - 4, left)); top = Math.max(4, Math.min(ch - hh - 4, top));
      elm.style.left = Math.round(left) + "px"; elm.style.top = Math.round(top) + "px";
    }
    var shownHover = -2, shownFocus = -2, shownLine = -1;
    function placeLabels() {
      var hv = hl >= 0 ? hl : hover;
      if (hv === focus) hv = -1;
      if (focus >= 0 && P[focus] && visible(P[focus])) {
        var f = P[focus], fx = sx(f.x), fy = sy(f.y);
        if (shownFocus !== focus) { fillLabel(labelFocus, f, true); shownFocus = focus; }
        if (fx < -10 || fy < -10 || fx > cw + 10 || fy > ch + 10) labelFocus.classList.remove("show"); else { labelFocus.classList.add("show"); putLabel(labelFocus, fx, fy, f.r + 5); }
      } else { labelFocus.classList.remove("show"); shownFocus = -2; }
      if (hv >= 0 && P[hv] && visible(P[hv])) {
        var e = P[hv];
        if (shownHover !== hv) { fillLabel(labelHover, e, false); shownHover = hv; }
        labelHover.classList.add("show"); putLabel(labelHover, sx(e.x), sy(e.y), e.r);
      } else if (hoverLine >= 0 && L[hoverLine] && L[hoverLine].l.label && cursor) {
        if (shownHover !== -100 - hoverLine) { WG.clear(labelHover); labelHover.appendChild(h("div.t", L[hoverLine].l.label)); if (L[hoverLine].l.sub) labelHover.appendChild(h("div.s", L[hoverLine].l.sub)); shownHover = -100 - hoverLine; }
        labelHover.classList.add("show"); putLabel(labelHover, sx(cursor[0]), sy(cursor[1]), 6);
      } else { labelHover.classList.remove("show"); shownHover = -2; }
    }
    function setFocus(i) { focus = i; shownFocus = -2; }

    /* ---- status bar */
    /* The /loc text of a viewer position. On a region overview the numbers are those of the real zone under it. */
    function locText(p, short) {
      var z = zone, x = p[0], y = p[1];
      if (zone && zone.overview) { var r = M.fromOverview(zone.overview, x, y); if (!r) return ""; z = r.zone; x = r.x; y = r.y; short = false; }
      return short ? "loc=" + Math.round(x) + "," + Math.round(y) : M.loc(z, x, y);
    }
    function inZone(p) { return !!p && p[0] >= 0 && p[1] >= 0 && p[0] <= G.W && p[1] <= G.H && (!(zone && zone.overview) || !!M.fromOverview(zone.overview, p[0], p[1])); }
    function updateBar() {
      if (mini) return;
      if (cursor && inZone(cursor)) { locEl.textContent = locText(cursor); locEl.classList.remove("wg-dim"); }
      else { locEl.textContent = cursor ? (zone && zone.overview ? "Between zones" : "Outside the zone") : (zone && zone.name ? zone.name : ""); locEl.classList.add("wg-dim"); }
      if (mark) { markEl.textContent = "Marked: " + locText(mark, true); markEl.style.display = ""; clearBtn.style.display = ""; }
      else { markEl.style.display = "none"; clearBtn.style.display = "none"; }
    }
    var lastLoc = null;
    copyBtn.addEventListener("click", function () {
      var p = mark || lastLoc || (focus >= 0 && P[focus] ? [P[focus].x, P[focus].y] : null);
      if (!p) { var old = copyBtn.textContent; copyBtn.textContent = "Click the map first"; setTimeout(function () { copyBtn.textContent = old; }, 1300); return; }
      ui.copy(locText(p), copyBtn);
    });
    clearBtn.addEventListener("click", function () { mark = null; updateBar(); redraw(); });

    /* ---- pointer */
    function hitPin(px, py) {
      for (var i = order.length - 1; i >= 0; i--) {
        var e = P[order[i]]; if (!visible(e)) continue;
        var dx = sx(e.x) - px, dy = sy(e.y) - py, r = Math.max(e.r, 5) + 3;
        if (dx * dx + dy * dy <= r * r) return order[i];
      }
      return -1;
    }
    function hitLine(px, py) {
      for (var i = L.length - 1; i >= 0; i--) {
        var e = L[i]; if (!visible(e) || !e.l.label) continue;
        for (var j = 1; j < e.pts.length; j++) {
          var ax = sx(e.pts[j - 1][0]), ay = sy(e.pts[j - 1][1]), bx = sx(e.pts[j][0]), by = sy(e.pts[j][1]);
          var vx = bx - ax, vy = by - ay, len = vx * vx + vy * vy, t = len ? Math.max(0, Math.min(1, ((px - ax) * vx + (py - ay) * vy) / len)) : 0;
          var dx = ax + t * vx - px, dy = ay + t * vy - py;
          if (dx * dx + dy * dy <= 36) return i;
        }
      }
      return -1;
    }
    function pos(e) { var r = canvas.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; }
    var down = null;
    function onDown(e) {
      if (e.button !== 0) return;
      var p = pos(e); down = {x: p[0], y: p[1], cx: cam.x, cy: cam.y, moved: false, id: e.pointerId};
      try { canvas.setPointerCapture(e.pointerId); } catch (err) {}
      anim = null;
    }
    function onMove(e) {
      var p = pos(e);
      if (down) {
        var dx = p[0] - down.x, dy = p[1] - down.y;
        if (!down.moved && Math.abs(dx) + Math.abs(dy) < 4) return;
        down.moved = true; canvas.style.cursor = "grabbing";
        cam.x = down.cx - dx / cam.s; cam.y = down.cy - dy / cam.s; clampCam(); redraw();
        return;
      }
      cursor = [gx(p[0]), gy(p[1])]; if (inZone(cursor)) lastLoc = cursor;
      var hp = hitPin(p[0], p[1]), hln = hp < 0 ? hitLine(p[0], p[1]) : -1;
      if (hp !== hover || hln !== hoverLine) {
        hover = hp; hoverLine = hln; canvas.style.cursor = hp >= 0 ? "pointer" : "";
        if (opts.onHover) opts.onHover(hp >= 0 ? userPins[hp] : null, hp);
        redraw();
      } else if (hoverLine >= 0) redraw();
      updateBar();
    }
    function onUp(e) {
      if (!down) return;
      var d = down; down = null; canvas.style.cursor = hover >= 0 ? "pointer" : "";
      try { canvas.releasePointerCapture(d.id); } catch (err) {}
      if (d.moved) return;
      var p = pos(e), hp = hitPin(p[0], p[1]);
      if (hp >= 0) {
        var pin = userPins[hp];
        if (opts.onPin) opts.onPin(pin, hp, e);
        else if (pin.href) { if (e.ctrlKey || e.metaKey || e.shiftKey) window.open(pin.href, "_blank"); else location.hash = pin.href; }
        else api.focus(hp, {zoom: false, scroll: false});
        return;
      }
      if (mini) return;
      var g = [gx(p[0]), gy(p[1])];
      if (inZone(g)) { mark = g; updateBar(); redraw(); }
    }
    function onLeave() { if (down) return; cursor = null; if (hover >= 0 || hoverLine >= 0) { hover = -1; hoverLine = -1; if (opts.onHover) opts.onHover(null, -1); redraw(); } updateBar(); }
    function onWheel(e) {
      e.preventDefault();
      var p = pos(e), dy = e.deltaMode === 1 ? e.deltaY * 33 : e.deltaY;
      zoomAt(p[0], p[1], Math.exp(-Math.max(-240, Math.min(240, dy)) * 0.0022), false);
      cursor = [gx(p[0]), gy(p[1])]; updateBar();
    }
    function onDbl(e) { var p = pos(e); if (hitPin(p[0], p[1]) >= 0) return; zoomAt(p[0], p[1], e.shiftKey ? 0.5 : 2, true); }
    function onKey(e) {
      var step = 80 / cam.s, used = true;
      if (e.key === "+" || e.key === "=") zoomAt(cw / 2, ch / 2, 1.5, true);
      else if (e.key === "-" || e.key === "_") zoomAt(cw / 2, ch / 2, 1 / 1.5, true);
      else if (e.key === "0") api.fit();
      else if (e.key === "ArrowLeft") { cam.x -= step; clampCam(); redraw(); }
      else if (e.key === "ArrowRight") { cam.x += step; clampCam(); redraw(); }
      else if (e.key === "ArrowUp") { cam.y -= step; clampCam(); redraw(); }
      else if (e.key === "ArrowDown") { cam.y += step; clampCam(); redraw(); }
      else if (e.key === "Escape") { if (focus >= 0 || mark) { setFocus(-1); mark = null; updateBar(); redraw(); } else used = false; }
      else used = false;
      if (used) e.preventDefault();
    }
    if (opts.interactive !== false) {
      canvas.addEventListener("pointerdown", onDown); canvas.addEventListener("pointermove", onMove);
      canvas.addEventListener("pointerup", onUp); canvas.addEventListener("pointercancel", function () { down = null; });
      canvas.addEventListener("pointerleave", onLeave);
      canvas.addEventListener("wheel", onWheel, {passive: false});
      canvas.addEventListener("dblclick", onDbl); canvas.addEventListener("keydown", onKey);
    }
    var ro = null;
    function onResize() { if (destroyed) return; var w0 = cw; layout(); if (cw && !w0 && pendingFocus != null) { var pf = pendingFocus; pendingFocus = null; api.focus(pf.i, pf.o); } redraw(); }
    if (window.ResizeObserver) { ro = new ResizeObserver(onResize); ro.observe(view); } else window.addEventListener("resize", onResize);

    function findPin(i) {
      if (i == null) return -1;
      if (typeof i === "number" && i >= 0 && i < userPins.length && Math.floor(i) === i) { for (var a = 0; a < userPins.length; a++) if (userPins[a].id === i) return a; return i; }
      for (var j = 0; j < userPins.length; j++) if (userPins[j].id === i || (userPins[j].id != null && String(userPins[j].id) === String(i))) return j;
      return -1;
    }
    function bringIntoView() {
      var r = root.getBoundingClientRect(), hd = document.getElementById("wg-header"), top = (hd ? hd.offsetHeight : 0) + 10;
      if (r.top < top || r.top + Math.min(r.height, 300) > window.innerHeight) window.scrollBy({top: r.top - top, behavior: "smooth"});
    }

    var api = {
      el: root,
      setData: function (d) {
        d = d || {};
        if ("pins" in d) userPins = d.pins || [];
        if ("lines" in d) userLines = d.lines || [];
        if ("legend" in d) legendDef = d.legend;
        if ("pins" in d || "lines" in d) { hover = -1; hoverLine = -1; hl = -1; setFocus(-1); shownHover = -2; prep(); }
        if ("zone" in d && d.zone !== undefined) {
          // the same zone again keeps the pan and zoom; only a different zone (or picture) refits
          var nz = d.zone, isObj = nz != null && typeof nz === "object", newId = isObj ? nz.id : nz;
          var same = String(newId) === String(zoneReq) && (nz == null) === (zoneReq == null) && (!isObj || nz === zone || (zone && nz.map === zone.map));
          if (!same) { applyZone(nz); return api; }
        }
        if (!zone && zoneReq != null && !zoneById(zoneReq) && WG.data.zones === undefined) { /* still loading */ }
        else if (!zone) growToPins();
        layout(); redraw(); updateBar();
        return api;
      },
      focus: function (i, o) {
        o = o || {};
        if (i == null) { setFocus(-1); redraw(); return api; }
        if (!cw || (zoneReq != null && !zone && WG.data.zones === undefined)) { pendingFocus = {i: i, o: o}; return api; }
        var idx = findPin(i); if (idx < 0 || !P[idx]) return api;
        var e = P[idx];
        if (hidden[e.key]) { hidden[e.key] = false; buildLegend(); }
        setFocus(idx); pulse = performance.now();
        var s = cam.s;
        if (o.zoom !== false) { var want = e.rad ? Math.min(cw, ch) / (e.rad * 7) : fitS * 3.2; s = Math.max(cam.s, Math.min(maxS, Math.max(want, fitS * 2))); }
        animate({x: e.x, y: e.y, s: s}, 380);
        if (o.scroll !== false) bringIntoView();
        return api;
      },
      highlight: function (i) { var idx = i == null ? -1 : findPin(i); if (idx !== hl) { hl = idx; redraw(); } return api; },
      fit: function () { animate({x: G.vx + G.vw / 2, y: G.vy + G.vh / 2, s: fitS}, 300); return api; },
      layer: function (id) { if (id == null) return G.layer ? G.layer.id : null; for (var i = 0; i < G.layers.length; i++) if (G.layers[i].id === id) { setLayer(i, false); layout(); redraw(); break; } return api; },
      floor: function (ix) { if (ix === undefined) return floorIx; if (ix == null) ix = -1; if (ix !== floorIx && ix < G.floors.length) { setFloor(ix); layout(); redraw(); } return api; },
      fitPins: function (test) {   // test: optional function (pin) -> true for the pins to frame (lines are then left out)
        var x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
        function add(x, y, r) { if (!isFinite(x) || !isFinite(y)) return; if (x - r < x0) x0 = x - r; if (y - r < y0) y0 = y - r; if (x + r > x1) x1 = x + r; if (y + r > y1) y1 = y + r; }
        P.forEach(function (e) { if (visible(e) && (!test || test(e.p))) add(e.x, e.y, e.rad || 0); });
        if (!test) L.forEach(function (e) { if (visible(e)) e.pts.forEach(function (p) { add(p[0], p[1], 0); }); });
        if (x0 === Infinity) return api.fit();
        var least = Math.min(8000, G.vw * 0.45), bw = Math.max(x1 - x0, least), bh = Math.max(y1 - y0, least);   // keep some country around a lone camp
        animate({x: (x0 + x1) / 2, y: (y0 + y1) / 2, s: Math.max(fitS, Math.min(maxS * 0.6, Math.min(cw / bw, ch / bh) * 0.82))}, 320);
        return api;
      },
      setLayer: function (name, vis) { layersOff[name] = vis === false; if (focus >= 0 && P[focus] && !visible(P[focus])) setFocus(-1); redraw(); return api; },
      zone: function () { return zone; },
      resize: function () { onResize(); return api; },
      view: function () { return {x: cam.x, y: cam.y, scale: cam.s}; },
      mark: function (x, y) { mark = x == null ? null : [+x, +y]; updateBar(); redraw(); return api; },
      destroy: function () { destroyed = true; if (ro) ro.disconnect(); else window.removeEventListener("resize", onResize); if (raf) cancelAnimationFrame(raf); }
    };
    userPins = opts.pins || []; userLines = opts.lines || [];
    prep();
    if (opts.focus != null) pendingFocus = {i: opts.focus, o: {scroll: false}};
    applyZone(opts.zone == null && opts.overview != null ? "r" + opts.overview : opts.zone);
    return api;
  };

  M.mini = function (el, opts) { var o = {}; for (var k in (opts || {})) o[k] = opts[k]; o.mini = true; if (o.height == null) o.height = 220; return M.create(el, o); };

  /* ---------------------------------------------------------------- several zones: one tab per zone */
  M.multi = function (el, groups, opts) {
    opts = opts || {};
    var strip = h("div.wg-tabs.wg-map-tabs", {role: "tablist"}), holder = h("div"), root = h("div.wg-map-multi", strip, holder);
    WG.clear(el); el.appendChild(root);
    var list = [], current = null, dead = false;
    var mopts = {}; for (var k in opts) mopts[k] = opts[k];
    mopts.zone = null; mopts.pins = []; mopts.lines = [];
    mopts.onPin = opts.onPin ? function (pin, i, ev) { opts.onPin(pin, i, ev, current); } : null;
    var map = M.create(holder, mopts);
    function weight(g) { return (g.pins ? g.pins.length : 0) + (g.lines ? g.lines.length : 0); }
    function zid(g) { return g.zone != null && typeof g.zone === "object" ? g.zone.id : g.zone; }
    function name(g) { if (g.label) return g.label; var z = typeof g.zone === "object" ? g.zone : zoneById(g.zone); return z && z.name ? z.name : "Zone " + zid(g); }
    function paint() {
      WG.clear(strip);
      strip.style.display = list.length > 1 ? "" : "none";
      list.forEach(function (g) {
        var z = typeof g.zone === "object" ? g.zone : zoneById(g.zone);
        var b = h("button.wg-tab" + (g === current ? ".active" : ""), {type: "button", role: "tab", onclick: function () { show(g); }}, h("span", {class: z && z.realm >= 1 && z.realm <= 3 ? "realm-" + z.realm : ""}, name(g)), h("span.wg-tab-count", ui.num(g.pins ? g.pins.length : 0)));
        strip.appendChild(b);
      });
    }
    function show(g, focus) {
      if (!g) return;
      var changed = g !== current; current = g;
      if (changed) { map.setData({zone: g.zone, pins: g.pins || [], lines: g.lines || []}); paint(); }
      if (focus != null) map.focus(focus);
      if (changed && opts.onZone) opts.onZone(zid(g), g);
    }
    function find(zoneId) { for (var i = 0; i < list.length; i++) if (String(zid(list[i])) === String(zoneId)) return list[i]; return null; }
    function setData(gs) {
      list = (gs || []).filter(function (g) { return g && g.zone != null; });
      if (opts.order !== "given") list = list.slice().sort(function (a, b) { return weight(b) - weight(a); });
      var keep = current ? find(zid(current)) : null; current = null;
      if (list.length) show(keep || list[0]); else { map.setData({zone: null, pins: [], lines: []}); paint(); }
    }
    setData(groups);
    if (!zoneList()) WG.load("zones").then(function () { if (!dead) paint(); }, function () {});
    return {
      el: root, map: map, setData: setData,
      show: function (zoneId) { show(find(zoneId)); },
      focus: function (zoneId, i) { var g = find(zoneId); if (g) show(g, i); },
      current: function () { return current; },
      destroy: function () { dead = true; map.destroy(); }
    };
  };
})();
