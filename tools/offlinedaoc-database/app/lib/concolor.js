/* OFFLINEDAOC Database - con colours (lane M-U).
   A straight port of the server's con table. Server file (read only):
     GameServer/gameutils/ConLevels.cs
       :8       MAX_LEVEL = 127 + 1 (levels 0..127 are valid indexes)
       :19-69   one row per player level 0..50: how many monster levels, counted up from level 0, show as
                grey (-3), green (-2), blue (-1), yellow (0), orange (1), red (2)
       :74-77   player levels 51..127 reuse the level 50 row with the grey span one longer per level
       :79-92   the table is filled in that order; every monster level after the red span is purple (3)
       :95-104  GetConLevel(userLevel, targetLevel): either level outside 0..127 answers 0 (yellow)
       :112-122 enum ConColor: GREY -3, GREEN -2, BLUE -1, YELLOW 0, ORANGE 1, RED 2, PURPLE 3
     GameServer/gameobjects/GameObject.cs:896-904  GetConLevel(compare) = ConLevels.GetConLevel(levels)
   True 1.65 mode (GameServer/mode165) does not change the table; it only calls it.
   Checked against source/tests/con_vectors.json (made from the C# text by a separate Python script).

   Pure functions, plus one small DOM helper at the end (WG.con.el) so every page writes level ranges the same way.

   WG.con.color(myLevel, mobLevel)       -> "grey"|"green"|"blue"|"yellow"|"orange"|"red"|"purple"
   WG.con.value(myLevel, mobLevel)       -> -3..3, the server's number
   WG.con.levels(myLevel, color)         -> [min, max] monster levels of that colour, or null when there are none
   WG.con.bands(myLevel)                 -> [{color, min, max}, ...] lowest first, only colours that exist
   WG.con.span(myLevel, min, max)        -> {low, high, mixed}  colours of both ends of a level range
   WG.con.presets                        -> the hunting choices [{id, label, colors, part, hint}]
   WG.con.presetLevels(myLevel, id)      -> [min, max] or null
   WG.con.name(color) / WG.con.cls(color)-> "Low red" style label / css class "con-red"
   WG.con.rangeText(min, max)            -> "24" or "20 to 26"
   WG.con.el(myLevel, min, max)          -> a <span> "20 to 26" with each end in its con colour
*/
(function () {
  "use strict";
  var WG = (window.WG = window.WG || {});
  var MAX_LEVEL = 128;                                   // ConLevels.cs:8
  var COLORS = ["grey", "green", "blue", "yellow", "orange", "red", "purple"];   // index = con value + 3

  // ConLevels.cs:19-69. Spans per player level 0..50 in the order grey, green, blue, yellow, orange, red.
  // (A colour the C# row leaves out has span 0 here: level 0 has no grey, green or blue; level 1 no grey or green;
  //  level 2 no grey.)
  var SPANS = [
    [0,0,0,1,1,1], [0,0,1,1,1,1], [0,1,1,1,1,1], [1,1,1,1,1,1], [2,1,1,1,1,1], [3,1,1,1,1,1],     //  0-5
    [4,1,1,1,1,1], [5,1,1,1,1,1], [6,1,1,1,1,1], [7,1,1,1,1,1], [7,1,2,1,1,2], [7,1,2,2,2,2],     //  6-11
    [7,2,2,2,2,2], [8,2,2,2,2,2], [9,2,2,2,2,2], [10,2,2,2,2,2], [11,2,2,2,2,2], [12,2,2,2,2,2],  // 12-17
    [13,2,2,2,2,2], [14,2,2,2,2,2], [14,2,3,2,2,3], [14,2,3,3,3,3], [14,3,3,3,3,3], [15,3,3,3,3,3], // 18-23
    [16,3,3,3,3,3], [17,3,3,3,3,3], [18,3,3,3,3,3], [19,3,3,3,3,3], [20,3,3,3,3,3], [21,3,3,3,3,3], // 24-29
    [22,3,3,3,3,3], [23,3,3,3,3,3], [24,3,3,3,3,3], [25,3,3,3,3,3], [26,3,3,3,3,3], [26,3,3,4,4,3], // 30-35
    [26,3,3,5,5,4], [26,4,3,5,5,5], [26,4,4,5,5,5], [26,4,5,5,5,5], [26,5,5,5,5,5], [27,5,5,5,5,5], // 36-41
    [28,5,5,5,5,5], [29,5,5,5,5,5], [30,5,5,5,5,5], [31,5,5,5,5,5], [32,5,5,5,5,5], [33,5,5,5,5,5], // 42-47
    [34,5,5,5,5,5], [35,5,5,5,5,5], [36,5,5,5,5,5]                                                  // 48-50
  ];

  function spans(level) {                                // ConLevels.cs:74-77
    if (level <= 50) return SPANS[level];
    var s = SPANS[50].slice(); s[0] += level - 50; return s;
  }

  // Bands per player level, made once: BANDS[level] = [{color, min, max}, ...] (ConLevels.cs:79-92).
  var BANDS = [];
  for (var lv = 0; lv < MAX_LEVEL; lv++) {
    var s = spans(lv), at = 0, list = [];
    for (var c = 0; c < 6 && at < MAX_LEVEL; c++) {
      if (s[c] <= 0) continue;
      var end = Math.min(MAX_LEVEL, at + s[c]);
      list.push({color: COLORS[c], min: at, max: end - 1});
      at = end;
    }
    if (at < MAX_LEVEL) list.push({color: "purple", min: at, max: MAX_LEVEL - 1});
    BANDS.push(list);
  }

  function int(v) { v = Math.floor(Number(v)); return isFinite(v) ? v : 0; }

  var con = (WG.con = {});
  con.COLORS = COLORS.slice();
  con.MAX_LEVEL = MAX_LEVEL - 1;                         // the highest level the server's table knows (127)

  /* The server's number: -3 grey .. 3 purple. Out-of-range levels answer 0 (yellow), as the server does. */
  con.value = function (myLevel, mobLevel) {             // ConLevels.cs:95-104
    myLevel = int(myLevel); mobLevel = int(mobLevel);
    if (myLevel < 0 || myLevel > MAX_LEVEL - 1) return 0;
    if (mobLevel < 0 || mobLevel > MAX_LEVEL - 1) return 0;
    var b = BANDS[myLevel];
    for (var i = 0; i < b.length; i++) if (mobLevel <= b[i].max) return COLORS.indexOf(b[i].color) - 3;
    return 3;
  };
  con.color = function (myLevel, mobLevel) { return COLORS[con.value(myLevel, mobLevel) + 3]; };

  con.bands = function (myLevel) {
    myLevel = int(myLevel);
    if (myLevel < 0 || myLevel > MAX_LEVEL - 1) return [];
    return BANDS[myLevel].map(function (b) { return {color: b.color, min: b.min, max: b.max}; });
  };
  /* Monster levels that show as this colour, [min, max], or null when no level does (a level 1 player sees no
     grey or green). Purple runs to 127, the top of the server's table. */
  con.levels = function (myLevel, color) {
    myLevel = int(myLevel);
    if (myLevel < 0 || myLevel > MAX_LEVEL - 1) return null;
    var b = BANDS[myLevel];
    for (var i = 0; i < b.length; i++) if (b[i].color === color) return [b[i].min, b[i].max];
    return null;
  };
  /* Both ends of a level range (a camp of 20 to 26): {low: colour of min, high: colour of max, mixed}. */
  con.span = function (myLevel, min, max) {
    if (max == null) max = min;
    if (max < min) { var t = min; min = max; max = t; }
    var low = con.color(myLevel, min), high = con.color(myLevel, max);
    return {low: low, high: high, mixed: low !== high};
  };

  /* ---- hunting choices ----
     Whole colours are the server's bands. "Low red" and "Low purple" are this tool's own cut, shown on the page
     with the levels they mean: the lower half (rounded up) of the red band, and the same number of levels at the
     bottom of purple (purple has no upper end, so the red band's width is used). "General" is blue to orange. */
  con.presets = [
    {id: "low-purple", label: "Low purple", colors: ["purple"], part: "low", hint: "The first purple levels, just above red"},
    {id: "low-red", label: "Low red", colors: ["red"], part: "low", hint: "The lower half of the red levels"},
    {id: "red", label: "Red", colors: ["red"], part: "all", hint: "Every red level"},
    {id: "orange", label: "Orange", colors: ["orange"], part: "all", hint: "Every orange level"},
    {id: "yellow", label: "Yellow", colors: ["yellow"], part: "all", hint: "Every yellow level"},
    {id: "blue", label: "Blue", colors: ["blue"], part: "all", hint: "Every blue level"},
    {id: "general", label: "General", colors: ["blue", "yellow", "orange"], part: "all", hint: "Blue, yellow and orange: what a solo player normally hunts"}
  ];
  con.preset = function (id) {
    for (var i = 0; i < con.presets.length; i++) if (con.presets[i].id === id) return con.presets[i];
    return null;
  };
  function lowCount(myLevel) {                           // how many levels "low" means: half the red band, rounded up
    var red = con.levels(myLevel, "red");
    return red ? Math.max(1, Math.ceil((red[1] - red[0] + 1) / 2)) : 1;
  }
  con.presetLevels = function (myLevel, id) {
    var p = typeof id === "string" ? con.preset(id) : id;
    if (!p) return null;
    var lo = null, hi = null;
    p.colors.forEach(function (c) {
      var r = con.levels(myLevel, c);
      if (!r) return;
      if (lo == null || r[0] < lo) lo = r[0];
      if (hi == null || r[1] > hi) hi = r[1];
    });
    if (lo == null) return null;
    if (p.part === "low") hi = Math.min(hi, lo + lowCount(myLevel) - 1);
    return [lo, hi];
  };

  con.name = function (color) { return color ? color.charAt(0).toUpperCase() + color.slice(1) : ""; };
  con.cls = function (color) { return "con-" + color; };
  con.rangeText = function (min, max) { return max == null || max === min ? String(min) : min + " to " + max; };

  /* <span>20 to 26</span> with each end in its con colour for my level; a single level is one coloured number.
     opts.title = false leaves out the hover text. */
  con.el = function (myLevel, min, max, opts) {
    if (max == null) max = min;
    var sp = con.span(myLevel, min, max), h = WG.h;
    var tip = min === max
      ? "Level " + min + ": " + sp.low + " to a level " + myLevel
      : "Levels " + min + " to " + max + ": " + (sp.mixed ? sp.low + " to " + sp.high : sp.low) + " to a level " + myLevel;
    var el = h("span.wg-conrange", {title: opts && opts.title === false ? null : tip, style: {whiteSpace: "nowrap", fontWeight: "600"}});
    el.appendChild(h("span." + con.cls(sp.low), String(min)));
    if (min !== max) {
      el.appendChild(h("span.wg-dim", {style: {fontWeight: "400"}}, " to "));
      el.appendChild(h("span." + con.cls(sp.high), String(max)));
    }
    return el;
  };
})();
