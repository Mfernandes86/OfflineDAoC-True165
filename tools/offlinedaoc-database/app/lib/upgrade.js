/* OFFLINEDAOC Database - upgrade ranking (lane I-U). Pure functions, no DOM.

   The ranking is the server's own gear value: the number gamebots use to decide what to wear. Ported from
     GameServer/bots/autonomous/AutonomousBotEconomy.cs:603-620   EquipmentValue (bot_weighted_gear_ranking is off, :627)
     GameServer/bots/BotWeaponStats.cs:13-23, 28-29               a melee weapon with no working damage fields is worth 0
     GameServer/bots/autonomous/BotGearValue165.cs:34-73          Weight  (per class)
     GameServer/bots/autonomous/BotGearValue165.cs:92-107         Cap     (item bonus caps by level)
     GameServer/bots/autonomous/BotGearValue165.cs:110-114        WeaponDpsCap, ArmorFactorCap
     GameServer/bots/autonomous/BotGearValue165.cs:120-167        Adjustment
     GameServer/bots/autonomous/AutonomousBotEconomy.cs:836-849   WastedFocus (focus for a spell line the class lacks)
     GameServer/bots/autonomous/ItemValue165.cs:36-58             Weights (used here only for the "stat focus" ranking)

   Where this differs from the server, and the pages say so:
   - The bot counts a bonus only up to the room left under its cap after its OTHER worn pieces. This tool does not know
     the rest of your gear, so every item is capped on its own (as if nothing else gave that bonus).
   - "The class has the skill line" means the class can train it (the bot checks the levels it has trained).
   - WeaponSpecBonus (the bot's trained weapon spec, AutonomousBotEconomy.cs:789) is not included: this tool does not
     know your spec points.
   - Shields: the server adds 2 x DPS_AF for a shield as for a weapon. A shield's DPS_AF is the damage of its shield
     styles and the stored numbers are uneven (a level 2 bronze shield carries 16.5, see source/contract/items.md),
     so a mixed list would open with low shields. This tool leaves a shield's DPS out of the value; shields then rank
     by level, quality and bonuses. The item page shows the line with 0 points and says why. */
(function () {
  "use strict";
  var WG = window.WG, I = WG.item;
  var U = (WG.upgrade = WG.upgrade || {});

  var MELEE = {2: 1, 3: 1, 4: 1, 6: 1, 7: 1, 8: 1, 11: 1, 12: 1, 13: 1, 17: 1, 14: 1, 25: 1, 19: 1, 20: 1, 21: 1, 22: 1, 23: 1, 24: 1, 26: 1, 28: 1, 27: 1};
  /* focus bonus -> the skill line it belongs to (same names in eProperty.cs: Focus_X / Skill_X) */
  var FOCUS_SKILL = {120: 60, 121: 61, 122: 62, 123: 47, 124: 32, 125: 51, 126: 34, 127: 29, 128: 65, 129: 21, 130: 37,
    132: 38, 133: 66, 134: 67, 135: 70, 136: 76, 137: 98, 138: 86, 139: 39, 140: 27, 141: 26, 142: 87, 143: 88, 144: 89};
  U.FOCUS_SKILL = FOCUS_SKILL;
  var BASE_WEIGHT = 3;

  function isStat(p) { return p >= 1 && p <= 8; }
  function isResist(p) { return p >= 11 && p <= 19; }
  function isSkill(p) { return p >= 20 && p <= 115; }
  function isAllSkill(p) { return p === 163 || p === 164 || p === 167 || p === 168; }

  /* ---------- EquipmentValue ---------- */
  U.works = function (it) {            // BotWeaponStats.HasFunctionalMeleeStats (MaxCondition is not in the data: every template has it)
    return !MELEE[it.type] || (it.dpsaf >= 10 && it.spdabs > 0 && it.quality > 0);
  };
  U.base = function (it) {
    if (!U.works(it)) return 0;
    var pts = Math.abs(it.bonus || 0), b = it.bonuses || [];
    for (var i = 0; i < b.length; i++) pts += Math.abs(b[i][1]);
    var absorb = it.type >= 31 && it.type <= 38 ? Math.max(0, it.spdabs || 0) * 2 : 0;
    var dpsaf = it.type === 42 ? 0 : Math.max(0, it.dpsaf || 0);      // not for shields: see the header
    return Math.max(1, it.level || 0) * 10 + Math.max(1, it.quality || 0) + dpsaf * 2 + BASE_WEIGHT * pts + absorb;
  };

  /* ---------- BotGearValue165 ---------- */
  /* Does the class meta carry what the class weights need? */
  U.classKnown = function (cls) { return !!(cls && (cls.primary || cls.secondary || cls.skills)); };
  function caster(cls) { return !!cls.mana; }
  function hasSkill(cls, p) { return !cls.skills || cls.skills.indexOf(p) >= 0; }
  U.weight = function (cls, p) {
    if (!U.classKnown(cls)) return BASE_WEIGHT;
    if (isStat(p)) {
      if (p === cls.primary) return 4;
      if (cls.mana && p === cls.mana) return 4;
      if (p === cls.secondary || p === 3) return 3;
      if (p === cls.tertiary) return 2;
      return 1;
    }
    if (p === 156) return caster(cls) ? 4 : 0;          // Acuity
    if (p === 10) return 1;                              // MaxHealth
    if (p === 9 || p === 196) return caster(cls) ? 3 : 0; // MaxMana, PowerPool
    if (p === 148) return 2;                             // ArmorFactor
    if (isResist(p)) return 3;
    if (isSkill(p)) return hasSkill(cls, p) ? 6 : 0;
    if (p === 163) return caster(cls) ? 6 : 0;           // AllMagicSkills
    if (p === 164 || p === 167 || p === 168) return 4;
    return BASE_WEIGHT;
  };
  U.cap = function (level, p) {
    level = Math.max(1, level);
    if (isStat(p) || p === 156) return Math.floor(level * 1.5);
    if (p === 10) return level * 4;
    if (isResist(p)) return Math.floor(level / 2) + 1;
    if (isSkill(p) || isAllSkill(p)) return Math.floor(level / 5) + 1;
    if (p === 9) return Math.floor(level / 2) + 1;
    return Infinity;
  };
  U.dpsCap = function (level) { return 12 + 3 * Math.max(1, level); };                       // in DPS x 10
  U.afCap = function (level, type) { return type === 32 ? Math.max(1, level) : 2 * Math.max(1, level); };
  /* The cap that applies to an item's DPS / AF at a level, or Infinity (shields, instruments, jewelry). */
  U.dpsafCap = function (level, type) {
    if (type >= 1 && type <= 28) return U.dpsCap(level);
    if (type >= 31 && type <= 38) return U.afCap(level, type);
    return Infinity;
  };
  /* Focus points for spell lines the class does not have are worth nothing (WastedFocus). The class's own focus list
     comes from itemmeta (classes[].focus); without it, the focus is matched to the class's skill lines by name. */
  function wastedFocus(cls, it) {
    if (!cls || !(cls.focus || cls.skills)) return 0;
    var w = 0, b = it.bonuses || [];
    for (var i = 0; i < b.length; i++) {
      var p = b[i][0], sk = FOCUS_SKILL[p];
      if (!sk || !b[i][1]) continue;
      if (cls.focus ? cls.focus.indexOf(p) < 0 : cls.skills.indexOf(sk) < 0) w += Math.abs(b[i][1]);
    }
    return w;
  }

  /* ---------- the value for a character ----------
     ctx: {level: my level or 0, cls: class id or 0}. Returns {value, parts: [[label, points], ...]}. */
  U.detail = function (it, ctx) {
    var level = ctx && ctx.level > 0 ? ctx.level : 0, cls = ctx && ctx.cls ? I.cls(ctx.cls) : null, parts = [];
    var base = U.base(it);
    if (!base) return {value: 0, parts: [["A melee weapon with no working damage numbers", 0]]};
    var b = it.bonuses || [], pts = 0, i;
    for (i = 0; i < b.length; i++) pts += Math.abs(b[i][1]);
    parts.push(["Item level " + (it.level || 0) + " x 10", Math.max(1, it.level || 0) * 10]);
    parts.push(["Quality", Math.max(1, it.quality || 0)]);
    if (it.dpsaf > 0) {
      if (it.type === 42) parts.push(["Shield style DPS " + (it.dpsaf / 10).toFixed(1) + ": left out (uneven in the item data)", 0]);
      else parts.push([(it.type >= 31 && it.type <= 38 ? "Armor factor " + it.dpsaf + " x 2" : it.type === 45 ? "Instrument kind number x 2 (the server counts the field as it is)" : "DPS " + (it.dpsaf / 10).toFixed(1) + " x 20"), it.dpsaf * 2]);
    }
    if (it.type >= 31 && it.type <= 38 && it.spdabs > 0) parts.push(["Absorption " + it.spdabs + "% x 2", it.spdabs * 2]);
    if (it.bonus) parts.push(["Bonus " + it.bonus + "% x 3", BASE_WEIGHT * Math.abs(it.bonus)]);
    var value = base;
    if (!level) {
      if (pts) parts.push([pts + " bonus points x 3", BASE_WEIGHT * pts]);
      return {value: value, parts: parts};
    }
    if (it.level > level) { var d = (it.level - level) * 10; value -= d; parts.push(["Item level above yours (counts up to " + level + ")", -d]); }
    var cap = U.dpsafCap(level, it.type);
    if (it.dpsaf > cap) {
      var over = (it.dpsaf - cap) * 2; value -= over;
      parts.push([(it.type >= 31 ? "Armor factor over your cap of " + cap : "DPS over your cap of " + (cap / 10).toFixed(1)), -over]);
    }
    var known = U.classKnown(cls);
    for (i = 0; i < b.length; i++) {
      var p = b[i][0], v = b[i][1];
      if (p <= 0 || !v) continue;
      var w = known ? U.weight(cls, p) : BASE_WEIGHT, counted = v;
      if (v > 0) { var c = U.cap(level, p); if (c !== Infinity) counted = Math.max(0, Math.min(c, v)); }
      var got = w * counted;
      value += got - BASE_WEIGHT * Math.abs(v);
      parts.push([I.bonusShort(p, v) + (counted !== v ? " (cap " + U.cap(level, p) + " at level " + level + ")" : "") + " x " + w, got]);
    }
    var wf = known ? wastedFocus(cls, it) : 0;
    if (wf) { value -= BASE_WEIGHT * wf; parts.push(["Focus for spell lines your class does not have", -BASE_WEIGHT * wf]); }
    return {value: value, parts: parts};
  };
  /* Fast path for ranking thousands of items (same number as detail().value). */
  U.value = function (it, ctx) {
    var base = it._ev != null ? it._ev : (it._ev = U.base(it));
    if (!base) return 0;
    var level = ctx && ctx.level > 0 ? ctx.level : 0;
    if (!level) return base;
    var cls = ctx.cls ? I.cls(ctx.cls) : null, known = U.classKnown(cls), value = base, b = it.bonuses || [];
    if (it.level > level) value -= (it.level - level) * 10;
    var cap = U.dpsafCap(level, it.type);
    if (it.dpsaf > cap) value -= (it.dpsaf - cap) * 2;
    for (var i = 0; i < b.length; i++) {
      var p = b[i][0], v = b[i][1];
      if (p <= 0 || !v) continue;
      var counted = v;
      if (v > 0) { var c = U.cap(level, p); if (c !== Infinity) counted = Math.max(0, Math.min(c, v)); }
      value += (known ? U.weight(cls, p) : BASE_WEIGHT) * counted - BASE_WEIGHT * Math.abs(v);
    }
    if (known) value -= BASE_WEIGHT * wastedFocus(cls, it);
    return value;
  };

  /* ---------- stat focus ----------
     The owner picks which bonuses matter; the ranking is then the sum of those bonuses on the item, each point
     weighted as the server's Realm Exchange weighs it (ItemValue165.cs:36-58: skill 5, resist and power 2,
     stat, focus and armor factor 1, hits 1/4). Ties fall back to the gear value. */
  U.FOCI = [
    {key: "str", label: "Str", props: [1]}, {key: "con", label: "Con", props: [3]}, {key: "dex", label: "Dex", props: [2]},
    {key: "qui", label: "Qui", props: [4]}, {key: "int", label: "Int", props: [5, 156]}, {key: "pie", label: "Pie", props: [6, 156]},
    {key: "emp", label: "Emp", props: [7, 156]}, {key: "cha", label: "Cha", props: [8, 156]},
    {key: "hits", label: "Hits", props: [10]}, {key: "pow", label: "Power", props: [9, 196]},
    {key: "res", label: "Resists", cat: "resist"}, {key: "skill", label: "Skills", cat: "skill"}, {key: "af", label: "AF", props: [148]}
  ];
  U.exchangeWeight = function (p) {
    if (p === 10) return 0.25;
    if (p === 9 || p === 196) return 2;
    if (isResist(p)) return 2;
    if (isSkill(p) || p === 163 || p === 164 || p === 213 || p === 167 || p === 168) return 5;
    return 1;
  };
  /* focus: array of keys from U.FOCI -> a matcher, or null when empty */
  U.focusSet = function (keys, classId) {
    if (!keys || !keys.length) return null;
    var props = {}, cats = {}, any = false;
    U.FOCI.forEach(function (f) {
      if (keys.indexOf(f.key) < 0) return;
      any = true;
      if (f.props) f.props.forEach(function (p) { props[p] = 1; });
      if (f.cat) cats[f.cat] = 1;
    });
    if (!any) return null;
    var cls = classId ? I.cls(classId) : null;
    return function (p) {
      if (props[p]) return true;
      if (cats.resist && isResist(p)) return true;
      if (cats.skill && (isSkill(p) || isAllSkill(p) || p === 213)) return !(cls && cls.skills && isSkill(p) && cls.skills.indexOf(p) < 0);
      return false;
    };
  };
  U.focusScore = function (it, match) {
    var s = 0, b = it.bonuses || [];
    for (var i = 0; i < b.length; i++) if (b[i][1] > 0 && match(b[i][0])) s += U.exchangeWeight(b[i][0]) * b[i][1];
    return s;
  };

  /* ---------- one sentence for the page ---------- */
  U.explain = function (ctx, focusKeys) {
    var level = ctx && ctx.level > 0 ? ctx.level : 0, cls = ctx && ctx.cls ? I.cls(ctx.cls) : null;
    if (focusKeys && focusKeys.length) {
      var names = U.FOCI.filter(function (f) { return focusKeys.indexOf(f.key) >= 0; }).map(function (f) { return f.label; });
      return "Ranked by the bonuses you picked (" + names.join(", ") + "): their points on the item, weighted the way the server's Realm Exchange weighs bonuses (a skill level 5, a resist or power point 2, a stat point 1, a hit point 1/4). Ties go to the gear value. This is a sorting aid, not a game rule.";
    }
    var s = "Ranked by gear value, the number the server's gamebots use to choose gear: 10 per item level + quality + 2 per point of DPS (in tenths; not for shields) or AF + 2 per point of absorption + 3 per bonus point";
    if (level) {
      if (U.classKnown(cls)) s += ", counted only up to what a level " + level + " " + cls.name + " can use, each bonus weighed for that class (main stats 4, Constitution 3, resists 3, its own skill lines 6, the rest less or nothing)";
      else s += ", counted only up to what a level " + level + " character can use";
    }
    return s + ". It does not know the rest of your gear.";
  };
})();
