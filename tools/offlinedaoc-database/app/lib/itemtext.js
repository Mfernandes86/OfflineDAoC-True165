/* OFFLINEDAOC Database - item names, formatting and the in-memory item database (lane I-U).
   WG.item.*  (documented for the other page lanes in source/contract/notes-I-U.md)

   Data: data/items.js + data/itemmeta.js (lane I-D, source/contract/items.md), data/drops.js (D-D, drops.md),
   data/mobs.js (M-D), data/vendors.js + vendoritems.js (V-D), data/zones.js (P-D).
   Names come from itemmeta when it is loaded; the small tables below are the same numbers as the server's enums
   (GameServer/Enums/eObjectType.cs, eProperty.cs, gameutils/IGameInventory.cs) and are only a fallback. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h;
  var I = (WG.item = WG.item || {});

  /* ---------- fallback tables (server enums) ---------- */
  var OBJ = {
    0: "Item", 1: "Weapon", 2: "Crush", 3: "Slash", 4: "Thrust", 5: "Short bow", 6: "Two handed", 7: "Polearm",
    8: "Staff", 9: "Longbow", 10: "Crossbow", 11: "Sword", 12: "Hammer", 13: "Axe", 14: "Spear", 15: "Composite bow",
    16: "Thrown", 17: "Left axe", 18: "Recurved bow", 19: "Blades", 20: "Blunt", 21: "Piercing", 22: "Large weapon",
    23: "Celtic spear", 24: "Flexible", 25: "Hand to hand", 26: "Scythe", 31: "Armor", 32: "Cloth", 33: "Leather",
    34: "Studded", 35: "Chain", 36: "Plate", 37: "Reinforced", 38: "Scale", 41: "Magical", 42: "Shield", 43: "Arrow",
    44: "Bolt", 45: "Instrument", 46: "Poison", 47: "Alchemy tincture", 48: "Spellcraft gem"
  };
  var SLOT = {
    10: "Right hand", 11: "Left hand", 12: "Two handed", 13: "Ranged", 21: "Head", 22: "Hands", 23: "Feet", 24: "Jewel",
    25: "Chest", 26: "Cloak", 27: "Legs", 28: "Arms", 29: "Neck", 32: "Belt", 33: "Bracer", 34: "Bracer", 35: "Ring",
    36: "Ring", 37: "Mythical"
  };
  var DMG = {1: "Crush", 2: "Slash", 3: "Thrust", 10: "Body", 11: "Cold", 12: "Energy", 13: "Heat", 14: "Matter", 15: "Spirit"};
  var SHIELD = {1: "Small", 2: "Medium", 3: "Large"};
  var STAT_SHORT = {1: "Str", 2: "Dex", 3: "Con", 4: "Qui", 5: "Int", 6: "Pie", 7: "Emp", 8: "Cha", 156: "Acuity"};
  var PROP = {1: "Strength", 2: "Dexterity", 3: "Constitution", 4: "Quickness", 5: "Intelligence", 6: "Piety", 7: "Empathy",
    8: "Charisma", 9: "Power", 10: "Hits", 11: "Body", 12: "Cold", 13: "Crush", 14: "Energy", 15: "Heat", 16: "Matter",
    17: "Slash", 18: "Spirit", 19: "Thrust", 148: "Armor factor", 156: "Acuity", 163: "All magic skills",
    164: "All melee weapon skills", 165: "All focus", 167: "All dual wield skills", 168: "All archery skills", 196: "Power pool"};

  /* ---------- meta ---------- */
  function meta() { return WG.data.itemmeta || {}; }
  I.meta = meta;
  /* Load the names. A page that shows item text calls this first: WG.item.ready().then(...) */
  I.ready = function () { return WG.load("itemmeta").then(function () { return I; }, function () { return I; }); };
  function pick(map, id) { return map ? (map[id] != null ? map[id] : map[String(id)]) : undefined; }
  function nameOf(v) { return v == null ? undefined : (Array.isArray(v) ? v[0] : (typeof v === "object" ? (v.name || v.label) : v)); }

  /* ---------- kinds of item ---------- */
  I.isWeaponType = function (t) { return t >= 1 && t <= 28; };
  I.isArmorType = function (t) { return t >= 31 && t <= 38; };
  /* by Object_Type: "weapon" | "armor" | "shield" | "instrument" | "jewelry" | "other" (41 = jewelry, cloaks and other magical things) */
  I.kind = function (t) {
    if (t >= 1 && t <= 28) return "weapon";
    if (t >= 31 && t <= 38) return "armor";
    if (t === 42) return "shield";
    if (t === 45) return "instrument";
    if (t === 41) return "jewelry";
    return "other";
  };
  var CAT_KIND = {weapon: "weapon", armor: "armor", shield: "shield", instrument: "instrument", jewelry: "jewelry", cloak: "jewelry"};
  /* by item (uses I-D's category, so a potion with Object_Type 41 is "other", not jewelry) */
  I.kindOf = function (it) { return it.cat ? (CAT_KIND[it.cat] || "other") : I.kind(it.type != null ? it.type : it.obj); };
  /* something a character wears or wields */
  I.isGear = function (it) { return it.slot >= 10 && it.slot <= 37 && I.kindOf(it) !== "other"; };

  /* ---------- names ---------- */
  I.typeName = function (t) { var m = meta(); return nameOf(pick(m.objTypes, t)) || OBJ[t] || ("Type " + t); };
  I.slotName = function (s) {
    if (s === 34) s = 33; if (s === 36) s = 35;       // left / right bracer and ring read the same
    return nameOf(pick(meta().slots, s)) || SLOT[s] || (s ? "Slot " + s : "");
  };
  I.dmgName = function (d) { return d ? (nameOf(pick(meta().dmgTypes, d)) || DMG[d] || "") : ""; };
  I.shieldSize = function (it) { var s = it && it.type === 42 ? (it.size || 1) : 0; return s ? (nameOf(pick(meta().shieldSizes, s)) || SHIELD[s] || "") : ""; };
  I.realmName = function (r) { return r ? WG.REALMS[r] : "All realms"; };
  I.handText = function (it) { return it.slot === 12 || it.hand === 1 ? "two-handed" : it.slot === 13 ? "ranged" : it.slot === 11 || it.hand === 2 ? "left hand" : "one-handed"; };
  /* One line: "Chain, feet" / "Slash, one-handed" / "Small shield" / "Ring" / "Arrow" */
  I.typeSlot = function (it) {
    I.norm(it);
    var k = I.kindOf(it);
    if (k === "jewelry") return I.slotName(it.slot) || "Jewelry";
    if (k === "shield") return (I.shieldSize(it) ? I.shieldSize(it) + " shield" : "Shield");
    if (k === "instrument") { var ins = nameOf(pick(meta().instruments, it.dpsaf)); return ins ? "Instrument, " + ins.toLowerCase() : "Instrument"; }
    if (k === "armor") return I.typeName(it.type) + ", " + I.slotName(it.slot).toLowerCase();
    if (k === "weapon") { var tn = I.typeName(it.type), ht = I.handText(it); return ht === "two-handed" && /two.hand/i.test(tn) ? tn : tn + ", " + ht; }
    if (it.cat && it.cat !== "other" && it.cat !== "magical") return it.cat.charAt(0).toUpperCase() + it.cat.slice(1);
    return it.type ? I.typeName(it.type) : "Item";
  };

  /* ---------- bonuses ---------- */
  /* "stat" | "hits" | "power" | "resist" | "skill" | "focus" | "af" | "other"  (the id ranges of eProperty.cs) */
  I.bonusCat = function (p) {
    if ((p >= 1 && p <= 8) || p === 156) return "stat";
    if (p === 10) return "hits";
    if (p === 9 || p === 196) return "power";
    if ((p >= 11 && p <= 19) || p === 249) return "resist";
    if ((p >= 20 && p <= 115) || p === 163 || p === 164 || p === 167 || p === 168 || p === 213) return "skill";
    if ((p >= 120 && p <= 144) || p === 165 || (p >= 157 && p <= 162)) return "focus";
    if (p === 148) return "af";
    return "other";
  };
  I.bonusName = function (p) { return nameOf(pick(meta().props, p)) || PROP[p] || ("Bonus " + p); };
  function plain(p) { return I.bonusName(p).replace(/ Focus$/i, ""); }
  var PCT = {149: 1, 153: 1, 154: 1, 155: 1, 169: 1, 170: 1, 171: 1, 173: 1, 174: 1, 188: 1, 190: 1, 191: 1, 195: 1, 196: 1, 198: 1, 199: 1, 200: 1};
  /* Long form, worded like the game's item window: "Strength: 7 pts", "Slash resist: 3%", "Parry: 2 lvls" */
  I.bonusLine = function (p, v) {
    var c = I.bonusCat(p);
    if (c === "resist") return I.bonusName(p) + " resist: " + v + "%";
    if (c === "skill") return I.bonusName(p) + ": " + v + (Math.abs(v) === 1 ? " lvl" : " lvls");
    if (c === "focus") return plain(p) + " focus: " + v + (Math.abs(v) === 1 ? " lvl" : " lvls");
    if (c === "af") return "Armor factor: " + v;
    if (PCT[p]) return I.bonusName(p) + ": " + v + "%";
    return I.bonusName(p) + ": " + v + (Math.abs(v) === 1 ? " pt" : " pts");
  };
  /* Short form for tables: "Str 7", "Slash 3%", "Parry +2", "Fire Magic focus 25", "Hits 12", "AF 10" */
  I.bonusShort = function (p, v) {
    var c = I.bonusCat(p);
    if (c === "stat") return (STAT_SHORT[p] || I.bonusName(p)) + " " + v;
    if (c === "resist") return I.bonusName(p) + " " + v + "%";
    if (c === "skill") return I.bonusName(p) + " " + (v > 0 ? "+" : "") + v;
    if (c === "focus") return plain(p) + " focus " + v;
    if (c === "af") return "AF " + v;
    if (PCT[p]) return I.bonusName(p) + " " + v + "%";
    return I.bonusName(p) + " " + v;
  };
  /* All bonuses of an item (or a [[type, value], ...] list) as one string. opts.short (default true), opts.sep */
  I.bonusText = function (itemOrList, opts) {
    var list = Array.isArray(itemOrList) ? itemOrList : (itemOrList && itemOrList.bonuses) || [];
    var short = !opts || opts.short !== false, sep = (opts && opts.sep) || ", ";
    return list.map(function (b) { return short ? I.bonusShort(b[0], b[1]) : I.bonusLine(b[0], b[1]); }).join(sep);
  };
  I.bonusOf = function (it, p) { var s = 0, b = it.bonuses || []; for (var i = 0; i < b.length; i++) if (b[i][0] === p) s += b[i][1]; return s; };

  /* ---------- the key numbers ---------- */
  function f1(n) { return (Math.round(n) / 10).toFixed(1); }
  /* "16.2 dps, 4.0 spd" / "AF 64, abs 27%" / "" */
  I.statText = function (it) {
    I.norm(it);
    var k = I.kindOf(it);
    if ((k === "weapon" || k === "shield") && it.dpsaf) return f1(it.dpsaf) + " dps" + (it.spdabs ? ", " + f1(it.spdabs) + " spd" : "");
    if (k === "armor" && it.dpsaf) return "AF " + it.dpsaf + (it.spdabs ? ", abs " + it.spdabs + "%" : "");
    return "";
  };

  /* ---------- spells on items ---------- */
  function spell(id) {
    var d = WG.data.items, row = d && d.spells ? d.spells[id] : null;
    if (!row) return null;
    var cols = d.spellCols || ["name", "type", "text", "damage", "dmgType", "value", "duration", "frequency", "radius", "range", "target", "level"], o = {id: id};
    for (var i = 0; i < cols.length; i++) o[cols[i]] = row[i];
    return o;
  }
  I.spell = spell;
  /* [{kind: "Proc" | "Charge", name, text, chance, charges, max}] */
  I.effects = function (it) {
    var out = [], i, s;
    if (it.procs) for (i = 1; i < it.procs.length; i++) { s = spell(it.procs[i]); if (s) out.push({kind: "Proc", name: s.name, text: s.text, chance: it.procs[0]}); }
    if (it.charges) for (i = 0; i < it.charges.length; i++) { s = spell(it.charges[i][0]); if (s) out.push({kind: "Charge", name: s.name, text: s.text, charges: it.charges[i][1], max: it.charges[i][2]}); }
    return out;
  };

  /* ---------- classes ---------- */
  var clsCache = null, clsFrom = null;
  function classMap() {
    var m = meta();
    if (clsFrom !== m.classes) { clsFrom = m.classes; clsCache = new Map(); (m.classes || []).forEach(function (c) { clsCache.set(+c.id, c); }); }
    return clsCache;
  }
  /* The True 1.65 classes, optionally of one realm; advanced classes first by realm and name, base classes last. */
  I.classes = function (realm) {
    return (meta().classes || []).filter(function (c) { return !realm || !c.realm || c.realm === realm; })
      .sort(function (a, b) { return ((a.isBase ? 1 : 0) - (b.isBase ? 1 : 0)) || (a.realm - b.realm) || String(a.name).localeCompare(b.name); });
  };
  I.cls = function (id) { return classMap().get(+id) || null; };
  I.className = function (id) { var c = I.cls(id); return c ? c.name : (id ? ((meta().classNames || {})[id] || "Class " + id) : "Any class"); };
  /* A list entry is [value, from level]. */
  function has(list, t, level) {
    if (!list) return false;
    for (var i = 0; i < list.length; i++) { var e = list[i]; if (Array.isArray(e) ? (e[0] === t && (level == null || level >= e[1])) : e === t) return true; }
    return false;
  }
  function shieldMax(c, level) {
    var best = 0; (c.shield || []).forEach(function (e) { if (level == null || level >= e[1]) best = Math.max(best, e[0]); });
    return best;
  }
  /* Can the class use things of this Object_Type at this level? (items.md section 3, rules 3 to 6) */
  I.classCanUseType = function (classId, type, level, shieldSize) {
    var c = I.cls(classId);
    if (!c) return true;
    if (type >= 31 && type <= 38) return has(c.armor, type, level);
    if ((type >= 2 && type <= 28) || (type >= 43 && type <= 46)) return has(c.weapons, type, level);
    if (type === 42) return shieldMax(c, level) >= (shieldSize || 1);
    return true;
  };
  /* What a class can use at a level: {armor: [types], weapons: [types], shield: largest size, leftHand} */
  I.classTypes = function (classId, level) {
    var c = I.cls(classId); if (!c) return null;
    function types(list) { var out = []; (list || []).forEach(function (e) { if ((level == null || level >= e[1]) && out.indexOf(e[0]) < 0) out.push(e[0]); }); return out; }
    return {armor: types(c.armor), weapons: types(c.weapons), shield: shieldMax(c, level), leftHand: !!c.leftHand};
  };
  /* WG.item.usableBy(item, classId, level): the server's CheckAbilityToUseItem as items.md section 3 states it
     (realm, the item's class list, armor / weapon abilities with the level they arrive, shield size).
     level is optional: without it, abilities the class gets later count as usable. */
  I.usableBy = function (it, classId, level) {
    if (!classId) return true;
    I.norm(it);
    var c = I.cls(classId);
    if (it.classes && it.classes.length && it.classes.indexOf(+classId) < 0) return false;
    if (!c) return true;
    if (it.realm && c.realm && it.realm !== c.realm) return false;
    return I.classCanUseType(classId, it.type, level, it.size);
  };

  /* ---------- links, icon ---------- */
  I.href = function (it) { return WG.href("item/" + encodeURIComponent(typeof it === "string" ? it : it.id)); };
  /* Icon element or null (icons/items.png: 32 px cells, 10 per row, first icon number 900; items.md section 4). */
  I.icon = function (it, size) {
    var n = it && +it.icon, px = size || 32;
    if (!n) return null;
    var ic = (WG.data.items && WG.data.items.icons) || {}, cols = ic.cols || 10, first = ic.first != null ? ic.first : 900;
    if (n < first) return null;
    var big = px > 32 && ic.sheet64, cell = big ? (ic.cell64 || 64) : (ic.cell || 32), sc = px / cell;   // a sharper sheet for large icons
    return h("span.it-icon", {style: {width: px + "px", height: px + "px", backgroundImage: "url(" + (big ? ic.sheet64 : (ic.sheet || "icons/items.png")) + ")",
      backgroundPosition: (-(n % cols) * cell * sc) + "px " + (-Math.floor((n - first) / cols) * cell * sc) + "px",
      backgroundSize: (cols * cell * sc) + "px auto"}});
  };
  /* Name link with icon, for tables on any page (WG.ui.itemLink does the same and adds the hover card). */
  I.link = function (it, opts) {
    var ic = (!opts || opts.icon !== false) ? I.icon(it, (opts && opts.size) || 20) : null;
    return h("a.it-link", {href: I.href(it)}, ic, h("span", it.name));
  };

  /* ---------- the tooltip card ---------- */
  function row(label, value) { return h("div.it-kv", h("span.wg-dim", label), h("b", value)); }
  /* WG.item.tooltip(item, {link: true, full: true}) -> a card built like the game's item window.
     Works on a row of data/items.js as it is (WG.rows) or on an item from WG.item.db(). */
  I.tooltip = function (it, opts) {
    opts = opts || {};
    it = I.norm(it);
    var k = I.kindOf(it), card = h("div.it-card"), d = WG.data.items || {};
    card.appendChild(h("div.it-head", I.icon(it, 40),
      h("div", h("div.it-name", opts.link ? h("a", {href: I.href(it)}, it.name) : it.name),
        h("div.wg-dim", [it.level && I.isGear(it) ? "Level " + it.level : (it.level ? "Level " + it.level : null), I.typeSlot(it)].filter(Boolean).join(" · "),
          " · ", h("span" + (it.realm ? ".realm-" + it.realm : ""), I.realmName(it.realm))))));
    var nums = h("div.it-nums");
    if (k === "shield" && I.shieldSize(it)) nums.appendChild(row("Size", I.shieldSize(it)));
    if (k === "weapon" || k === "shield") {
      if (it.dpsaf) nums.appendChild(row("Damage per second", f1(it.dpsaf)));
      if (it.spdabs) nums.appendChild(row("Speed", f1(it.spdabs) + " s"));
      if (I.dmgName(it.dmg)) nums.appendChild(row("Damage type", I.dmgName(it.dmg)));
    }
    if (k === "armor") {
      if (it.dpsaf) nums.appendChild(row("Armor factor", it.dpsaf));
      if (it.spdabs) nums.appendChild(row("Absorption", it.spdabs + "%"));
    }
    if (it.quality && k !== "other") nums.appendChild(row("Quality", it.quality + "%"));
    if (it.bonus && k !== "other") nums.appendChild(row("Bonus", it.bonus + "%"));
    if (nums.firstChild) card.appendChild(nums);
    if (it.bonuses && it.bonuses.length) {
      card.appendChild(h("div.it-sec", "Magical bonuses"));
      card.appendChild(h("ul.it-list", it.bonuses.map(function (b) { return h("li", I.bonusLine(b[0], b[1])); })));
    }
    var fx = I.effects(it), struck = k === "armor" || k === "shield";
    /* a proc or charge works from the item's level requirement, or from min(50, item level) when it has none
       (GameLiving.cs:866, 902; GamePlayer.cs:7315; itemmeta.rules.procLevel) */
    var fxLevel = it.levelReq > 0 ? it.levelReq : Math.min(50, it.level || 0);
    fx.forEach(function (p) {
      card.appendChild(h("div.it-sec", p.kind === "Proc" ? (struck ? "Proc when struck" : "Proc on hit") : "Charged effect"));
      card.appendChild(h("div.it-spell", h("b", p.name),
        p.chance ? h("span.wg-dim", " · " + p.chance + "% " + (struck ? "per hit taken" : "per hit")) : null,
        p.kind === "Charge" && p.max > 0 ? h("span.wg-dim", " · " + p.charges + (p.max !== p.charges ? " of " + p.max : "") + (p.max === 1 ? " charge" : " charges")) : null,
        p.text ? h("div.wg-dim", p.text) : null));
    });
    if (fx.length && fxLevel > 1) card.appendChild(h("div.wg-dim", {style: {fontSize: "12px", marginTop: "2px"}}, "Works from character level " + fxLevel + "."));
    if (it.classes && it.classes.length) {
      card.appendChild(h("div.it-sec", "Usable by"));
      card.appendChild(h("div", it.classes.map(function (c) { return I.className(c); }).join(", ")));
    }
    var desc = d.descs && d.descs[it.id];
    if (desc && opts.full !== false) card.appendChild(h("div.it-desc.wg-dim", desc));
    var foot = [];
    if (it.fl & 64) foot.push("Quest item: drops only while you are on its quest.");
    if (it.fl & 256) foot.push("Its proc or charge names a spell that is not in the game, so it never fires.");
    if (it.fl & 128) foot.push("No True 1.65 class can use it.");
    if (it.fl & 1) foot.push("Cannot be traded.");
    if (it.price) foot.push("Value " + WG.money(it.price) + (it.pack > 1 ? " for " + it.pack : "") + (it.sell ? "; a merchant pays " + WG.money(it.sell) + (it.pack > 1 ? " each" : "") : "; a merchant will not buy it") + ".");
    if (foot.length) card.appendChild(h("div.it-foot.wg-dim", foot.join(" ")));
    return card;
  };

  /* ---------- the item database in memory ----------
     WG.item.db() -> Promise of db:
       db.items                       every item (objects; the rows of data/items.js with `type` = Object_Type added)
       db.get(id)                     one item or null (ids are matched exactly, then without case)
       db.drops(id, realm)            [{key, kind, where, zones, near, chance, base, n, flags, quest, never, always, locked, unspawned, quests}]
                                      where = "monster" | "script" (only a script creates it: kind is its mobs.scripted row,
                                      kind.scripted = true, kind.where = spots, kind.how = one sentence) | "npc" (in the world,
                                      not a monster to hunt; kind null) | "absent" (a loot table nothing uses; kind null)
                                      chance = for a killer of `realm` (0 / omitted: the natural killer, drops.md);
                                      near = the monster stands in a zone of that realm (or of no realm); best first:
                                      can fall, stands in the world, near, then by chance
       db.otd(id)                     one-time drops [{key, kind, minLevel}]
       db.questDrops(id)              quest hand-overs on a kill [{key, kind, chance, quest}]
       db.vendors(id, realm)          [{v, price, pack, currency, own, usable}] merchants of `realm` first, cheapest first
       db.best(item, realm)           {drop, drops, vendor, vendors, chance}
       db.kind(key), db.kindById(id), db.zone(id), db.zoneName(id), db.missing (data files that are not built) */
  I.SRC = {drop: 1, otd: 2, vendor: 4, quest: 8, craft: 16, start: 32, qitem: 64, script: 128};   // items.md section 1
  /* One place that knows the field names of data/items.js. Safe to call twice. */
  I.norm = function (it) {
    if (it._n) return it;
    it._n = 1;
    if (it.type == null) it.type = it.obj != null ? it.obj : 0;
    if (!it.bonuses) it.bonuses = [];
    if (!it.classes) it.classes = [];
    it.level = it.level || 0; it.realm = it.realm || 0; it.quality = it.quality || 0; it.bonus = it.bonus || 0; it.src = it.src || 0; it.fl = it.fl || 0;
    it._lc = String(it.name || "").toLowerCase();
    return it;
  };
  var dbPromise = null;
  function opt(name, missing) { return WG.load(name).then(function (d) { return d[name]; }, function () { missing.push(name); return null; }); }
  function tableOf(d, key) { if (!d) return []; var t = d.cols ? d : (d[key] || d); return t && t.cols ? WG.rows(t) : (Array.isArray(t) ? t : []); }
  I.db = function () {
    if (dbPromise) return dbPromise;
    var missing = [];
    dbPromise = Promise.all([WG.load("items"), opt("itemmeta", missing), opt("drops", missing), opt("mobs", missing),
      opt("vendors", missing), opt("vendoritems", missing), opt("zones", missing)]).then(function (r) {
      var items = tableOf(r[0].items, "items"), byId = new Map(), byLow = null, i;
      for (i = 0; i < items.length; i++) { I.norm(items[i]); byId.set(items[i].id, items[i]); }
      var kinds = tableOf(r[3], "kinds"), kindByKey = new Map(), kindById = new Map();
      kinds.forEach(function (k) {
        // mobs.js (M-D) names the level range lmin / lmax and gives zones as [[zone id, spawns], ...]; the shared links read levelMin / levelMax
        if (k.levelMin == null && k.lmin != null) { k.levelMin = k.lmin; k.levelMax = k.lmax; }
        if (!k.zoneIds) k.zoneIds = (k.zones || []).map(function (z) { return Array.isArray(z) ? z[0] : z; });
        kindByKey.set(k.key != null ? k.key : String(k.name).toLowerCase(), k); kindById.set(k.id, k);
      });
      var zones = tableOf(r[6], "zones"), zoneById = new Map();
      zones.forEach(function (z) { zoneById.set(z.id, z); });

      /* drops (drops.md): byItem[item] = [[kind key, chance, flags, 1 if unspawned], ...]; the full row is in mobs[key].d */
      var D = r[2] || {}, byItem = D.byItem || {}, dMobs = D.mobs || {}, dUn = D.unspawned || {}, dQuests = D.quests || {};
      var rowIdx = new Map();            // kind key -> Map(item id -> row of d), made when first asked
      function dropRow(key, un, id) {
        var m = rowIdx.get(key);
        if (!m) { m = new Map(); var k = (un ? dUn : dMobs)[key], rows = k && k.d ? k.d : []; for (var j = 0; j < rows.length; j++) if (!m.has(rows[j][0])) m.set(rows[j][0], rows[j]); rowIdx.set(key, m); }
        return m.get(id) || null;
      }
      var dropCache = new Map(), WHERE_ORDER = {monster: 0, script: 1, npc: 2, absent: 3};
      /* Monsters only a script creates (mobs.md section 5d: mobs.scripted). They have an id and a page (#/mob/<id>), a level
         when the script states one, the spots where they appear (`where`) and one sentence on what makes them appear (`how`);
         no camps. Here they get the same aliases as a kind, plus scripted = true. */
      var scriptedByKey = new Map();
      tableOf(r[3] && r[3].scripted, "scripted").forEach(function (k) {
        if (k.levelMin == null && k.lmin != null) { k.levelMin = k.lmin; k.levelMax = k.lmax == null ? k.lmin : k.lmax; }
        if (!k.zoneIds) { k.zoneIds = []; (k.where || []).forEach(function (w) { if (k.zoneIds.indexOf(w[0]) < 0) k.zoneIds.push(w[0]); }); }
        k.scripted = true;
        scriptedByKey.set(k.key, k); if (!kindById.has(k.id)) kindById.set(k.id, k);
      });
      function anyKind(key) { return kindByKey.get(key) || scriptedByKey.get(key) || null; }
      function inRealm(realm) { return function (zid) { var z = zoneById.get(zid); return !z || !z.realm || z.realm === realm; }; }
      function drops(id, realm) {
        realm = realm >= 1 && realm <= 3 ? realm : 0;
        var ck = realm + "|" + id, out = dropCache.get(ck);
        if (out) return out;
        out = [];
        var list = byItem[id] || [];
        for (var j = 0; j < list.length; j++) {
          var e = list[j], key = e[0], fl = e[2] || 0, mark = e[3] || 0, un = mark === 1, row = dropRow(key, un, id);
          var chance = e[1];
          if (realm && row && Array.isArray(row[4])) chance = row[4][realm - 1];
          var kind = un ? null : anyKind(key), zl = kind ? kind.zoneIds : [], near = true;
          if (realm && zl.length && zoneById.size) {      // zones of your realm (or of no realm: Darkness Falls ...) first
            var mine = zl.filter(inRealm(realm));
            near = mine.length > 0;
            if (near && mine.length < zl.length) zl = mine.concat(zl.filter(function (z) { return mine.indexOf(z) < 0; }));
          }
          // where the monster is (drops.md, the 4th element of a byItem row): "monster" = a world spawn kind of mobs.js;
          // "script" = mark 2, a kind only a script creates (mobs.scripted: has a page, no camps);
          // "npc" = stands in the world but mobs.js does not list it as a monster to hunt (realm or peaceful NPC);
          // "absent" = mark 1, a loot table nothing in this world uses
          var where = un ? "absent" : (mark === 2 || (kind && kind.scripted)) ? "script" : kind ? "monster" : "npc";
          out.push({key: key, kind: kind, where: where, zones: zl, near: near, unspawned: un, chance: chance, base: e[1], n: row ? row[2] : 1, flags: fl,
            always: !!(fl & 1), quest: !!(fl & 2), never: !!(fl & 4), locked: !!(fl & 8),
            quests: row && row[5] ? row[5].map(function (q) { return dQuests[q] || ("quest " + q); }) : null});
        }
        // what can really fall first, monsters that stand in the world first, in your realm first, then by chance
        out.sort(function (a, b) { return ((a.never || !a.chance ? 1 : 0) - (b.never || !b.chance ? 1 : 0)) || (WHERE_ORDER[a.where] - WHERE_ORDER[b.where]) || ((a.near ? 0 : 1) - (b.near ? 0 : 1)) || (b.chance - a.chance); });
        dropCache.set(ck, out);
        return out;
      }
      var otdBy = null, qBy = null;
      function scanKinds() {
        otdBy = new Map(); qBy = new Map();
        Object.keys(dMobs).forEach(function (key) {
          var k = dMobs[key];
          if (k.otd) k.otd.forEach(function (o) { var l = otdBy.get(o[0]); if (!l) otdBy.set(o[0], (l = [])); l.push({key: key, kind: anyKind(key), minLevel: o[1]}); });
          if (k.q) k.q.forEach(function (q) { var l = qBy.get(q[0]); if (!l) qBy.set(q[0], (l = [])); l.push({key: key, kind: anyKind(key), chance: q[1], quest: dQuests[q[2]] || ("quest " + q[2]), steps: q[3]}); });
        });
      }

      /* merchants (V-D): vendors table + vendoritems.lists[list] = [[item id, price, page, slot, pack?], ...] */
      var V = r[4] || {}, vendors = tableOf(V, "vendors"), vi = r[5] ? (r[5].lists || r[5]) : {}, soldBy = new Map(), vendorById = new Map();
      var currencies = V.currencies || {};
      vendors.forEach(function (v) {
        vendorById.set(v.id, v);
        var rows = vi[v.list]; if (!Array.isArray(rows)) return;
        var cur = !v.currency || v.currency === "coin" ? "" : (v.currency === "bp" ? "bounty points" : (currencies[v.currencyItem] || v.currencyItem || "tokens"));
        for (var j = 0; j < rows.length; j++) {
          var id = rows[j][0], list = soldBy.get(id);
          if (!list) soldBy.set(id, (list = []));
          list.push({v: v, price: rows[j][1], pack: rows[j][4] || 1, currency: cur});
        }
      });
      var soldCache = new Map();
      function sold(id, realm) {
        realm = realm >= 1 && realm <= 3 ? realm : 0;
        var ck = realm + "|" + id, out = soldCache.get(ck);
        if (out) return out;
        // own = stands in your realm (or has none); usable = false only for a merchant that trades with its own realm only (vendors.md: ownRealmOnly)
        out = (soldBy.get(id) || []).map(function (s) {
          var own = !realm || !s.v.realm || s.v.realm === realm;
          return {v: s.v, price: s.price, pack: s.pack, currency: s.currency, own: own, usable: own || !s.v.ownRealmOnly};
        });
        out.sort(function (a, b) { return ((a.usable ? 0 : 1) - (b.usable ? 0 : 1)) || ((a.own ? 0 : 1) - (b.own ? 0 : 1)) || ((a.currency ? 1 : 0) - (b.currency ? 1 : 0)) || (a.price - b.price) || String(a.v.name).localeCompare(b.v.name); });
        soldCache.set(ck, out);
        return out;
      }
      return {
        items: items, missing: missing, kinds: kinds, vendorList: vendors, dropMeta: D.meta || null,
        get: function (id) {
          var it = byId.get(id); if (it || id == null) return it || null;
          if (!byLow) { byLow = new Map(); items.forEach(function (x) { byLow.set(String(x.id).toLowerCase(), x); }); }
          return byLow.get(String(id).toLowerCase()) || null;
        },
        drops: drops, vendors: sold,
        otd: function (id) { if (!otdBy) scanKinds(); return otdBy.get(id) || []; },
        questDrops: function (id) { if (!qBy) scanKinds(); return qBy.get(id) || []; },
        kind: anyKind,
        kindById: function (id) { return kindById.get(id) || null; },
        vendor: function (id) { return vendorById.get(id) || null; },
        zone: function (id) { return zoneById.get(id) || null; },
        zoneName: function (id) { var z = zoneById.get(id); return z ? z.name : "Zone " + id; },
        /* The best way to get an item for a character of `realm`: the monster in the world with the highest chance
           per kill, and the cheapest merchant that serves the realm. */
        best: function (it, realm) {
          realm = realm >= 1 && realm <= 3 ? realm : 0;
          var c = it._best || (it._best = {}), b = c[realm];
          if (b) return b;
          var d = (it.src & 129) ? drops(it.id, realm) : [], v = (it.src & 4) ? sold(it.id, realm) : [], n = 0;
          var top = d.length && d[0].chance > 0 && !d[0].never && d[0].where !== "absent" ? d[0] : null;
          for (var j = 0; j < d.length; j++) if (d[j].chance > 0 && !d[j].never && d[j].where !== "absent") n++;
          return (c[realm] = {drop: top, drops: n, vendor: v[0] || null, vendors: v.length, chance: top ? top.chance : null});
        }
      };
    });
    dbPromise.catch(function () { dbPromise = null; });
    return dbPromise;
  };
  /* What to print for a drop source that has no monster page (db.drops()[..].where). */
  I.whereText = function (where, kind) {
    if (where === "script" && kind && kind.how) return String(kind.how).replace(/\.$/, "");
    return where === "script" ? "not a standing spawn: a server script creates it" : where === "npc" ? "not a monster you can hunt (a realm or peaceful NPC, or a closed region)"
      : where === "absent" ? "not in the world: nothing in this world uses this loot table" : "";
  };
  /* "12g 34s" or "350 bounty points" for a merchant row from db.vendors() */
  I.priceText = function (s) {
    if (!s.price) return "free";
    if (!s.currency) return WG.money(s.price);
    return (WG.ui && WG.ui.num ? WG.ui.num(s.price) : s.price) + " " + s.currency + (s.price !== 1 && !/s$/.test(s.currency) ? "s" : "");
  };
  /* the same as an element (coloured coins for a coin price) */
  I.priceEl = function (s) { return s.price && !s.currency && WG.ui && WG.ui.money ? WG.ui.money(s.price) : h("span", I.priceText(s)); };
  /* Kills needed for a given chance of at least one drop: arithmetic, 1 - (1 - p)^n >= want. */
  I.killsFor = function (p, want) {
    if (!(p > 0)) return null;
    if (p >= 1) return 1;
    return Math.ceil(Math.log(1 - want) / Math.log(1 - p) - 1e-9);
  };

  /* ---------- styles for item text (small; kept with the code that makes the markup) ---------- */
  if (!document.getElementById("it-css")) {
    document.head.appendChild(h("style", {id: "it-css", textContent:
      ".it-icon{display:inline-block;vertical-align:middle;flex:none;border-radius:3px;background-repeat:no-repeat}" +
      ".it-link{display:inline-flex;align-items:center;gap:6px;color:var(--text)}" +
      ".it-link:hover span{text-decoration:underline}" +
      ".it-card{max-width:380px;line-height:1.5;text-align:left}" +
      ".it-head{display:flex;gap:10px;align-items:center;margin-bottom:8px}" +
      ".it-name{font-size:17px;font-weight:600;color:var(--accent)}" +
      ".it-nums{display:grid;grid-template-columns:1fr 1fr;gap:2px 16px;padding:8px 0 0;border-top:1px solid var(--line)}" +
      ".it-kv{display:flex;justify-content:space-between;gap:8px}" +
      ".it-sec{margin-top:8px;padding-top:8px;border-top:1px solid var(--line);color:var(--dim);font-size:12px;text-transform:uppercase;letter-spacing:.5px}" +
      ".it-list{margin:4px 0 0;padding:0;list-style:none}.it-list li{color:#9fe0a6}" +
      ".it-spell b{color:#c9a6ff}.it-desc{margin-top:8px;font-style:italic}.it-foot{margin-top:8px;font-size:12px}"}));
  }
})();
