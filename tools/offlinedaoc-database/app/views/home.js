/* OFFLINEDAOC Database - #/home (P-U): the search box, the "my character" box and the four ways in. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = WG.ui;
  var live = null;

  function levelsText(id, my) {
    try { var r = WG.con && WG.con.presetLevels ? WG.con.presetLevels(my, id) : null; if (r && r[0] != null) return r[0] === r[1] ? "level " + r[0] : "level " + r[0] + " to " + r[1]; } catch (e) {}
    return "";
  }
  function entry(o) {
    var card = h("div.wg-home-card", {class: o.cls || ""},
      h("a.wg-home-go", {href: o.href}, h("span.t", o.title), h("span.d", o.text), h("span.open", o.open || "Open")));
    if (o.quick && o.quick.length) card.appendChild(h("div.wg-home-quick", h("span.wg-dim", o.quickLabel || "Straight to:"), o.quick));
    return card;
  }

  function render(el) {
    var token = (live = {});
    var hero = h("div.wg-home-hero",
      h("h1", "What are you looking for?"),
      (ui.searchBox ? ui.searchBox({large: true, placeholder: WG.quest ? "A monster, an item, a quest, a merchant or a zone" : "A monster, an item, a merchant or a zone"}) : null),
      h("div.wg-dim.wg-home-hint", "Type part of a name. Press ", h("kbd", "/"), " on any page to search, ", h("kbd", "↑"), " ", h("kbd", "↓"), " and ", h("kbd", "Enter"), " to pick."));
    el.appendChild(hero);

    var meCard = h("div.wg-card.wg-home-me", ui.meBox ? ui.meBox({title: "My character"}) : null,
      h("div.wg-dim.wg-small", "Set it once. Con colours, the hunting finder and the upgrade finder start from it; it is saved on this PC only."));
    el.appendChild(meCard);

    var grid = h("div.wg-home-grid");
    el.appendChild(grid);
    function paint() {
      if (live !== token) return;
      var my = WG.me.level || 1;
      WG.clear(grid);
      var presets = (WG.con && WG.con.presets ? WG.con.presets : []).filter(function (p) { return p.id === "low-purple" || p.id === "low-red" || p.id === "general"; });
      grid.appendChild(entry({cls: "hunt", href: WG.href("hunt"), title: "What should I hunt?", open: "Open the hunting finder",
        text: "Monster camps for level " + my + ": pick the con you want, see every camp on its zone map, with a warning where stronger monsters stand close.",
        quickLabel: "At level " + my + ":",
        quick: presets.map(function (p) { var lt = levelsText(p.id, my); return ui.chip(p.label + (lt ? " (" + lt + ")" : ""), {href: WG.href("hunt", {con: p.id}), title: p.hint, color: p.colors.length === 1 ? p.colors[0] : null}); })}));
      grid.appendChild(entry({cls: "items", href: WG.href("items", {m: "up"}), title: "Find an upgrade", open: "Open the upgrade finder",
        text: "Armor, weapons and jewelry for your class and level, ranked, with the real chance that each monster drops them.",
        quick: [ui.chip("Drops only", {href: WG.href("items", {m: "up", src: "d"})}), ui.chip("Right hand weapons", {href: WG.href("items", {m: "up", slot: 10})}), ui.chip("All items", {href: WG.href("items")})]}));
      if (WG.views.quests) grid.appendChild(entry({cls: "quests", href: WG.href("quests"), title: "Find a quest", open: "Open the quest finder",
        text: "The quests of your realm for level " + my + ": who gives them, where, what they ask for and what they pay.",
        quick: [ui.chip("Quests for level " + my, {href: WG.href("quests")})]}));
      grid.appendChild(entry({cls: "vendors", href: WG.href("vendors"), title: "Where is this sold?", open: "Open the merchant finder",
        text: "What the merchants of your realm sell, the price they really charge and where they stand.",
        quick: [ui.chip("Find goods", {href: WG.href("vendors")}), ui.chip("Browse merchants", {href: WG.href("vendors", {mode: "merchants"})})]}));
      grid.appendChild(entry({cls: "routes", href: WG.href("routes"), title: "Stable routes", open: "Open the route planner",
        text: "Every horse and flying route the server offers, Shrouded Isles included, with the reputation some stable masters ask for.",
        quick: [ui.chip("Plan a trip", {href: WG.href("routes")})]}));
      var mr = WG.me.realm || 0;
      grid.appendChild(entry({cls: "zones", href: WG.href("zones"), title: "Zones and maps", open: "Open the zone list",
        text: "Every zone with its map: monster camps coloured by con, bosses and named monsters, merchants, stable masters and the ways out.",
        quick: [ui.chip("Dungeons", {href: WG.href("zones", {kind: "dungeon"})}), ui.chip("Frontier", {href: WG.href("zones", {kind: "frontier", realm: mr || null})}), ui.chip("Battlegrounds", {href: WG.href("zones", {kind: "battleground"})}), ui.chip("Best for level " + my, {href: WG.href("zones", {sort: "mine"})})]}));
      grid.classList.toggle("six", grid.children.length > 4);
    }
    paint();
    live.onMe = paint;

    var more = h("div.wg-home-more",
      h("a.wg-btn", {href: WG.href("mobs")}, "All monsters"), h("a.wg-btn", {href: WG.href("about")}, "Where the numbers come from"));
    var stamp = h("span.wg-dim.wg-small.wg-home-stamp");
    more.appendChild(stamp);
    el.appendChild(more);
    WG.load("meta").then(function (d) {
      if (live !== token || !d.meta) return;
      var m = d.meta, by = {};
      (m.files || []).forEach(function (f) { by[f.name] = f.counts || {}; });
      var parts = [];
      if (by.mobs && by.mobs.kinds) parts.push(ui.num(by.mobs.kinds) + " monsters");
      if (by.camps && by.camps.camps) parts.push(ui.num(by.camps.camps) + " camps");
      if (by.items && by.items.rows) parts.push(ui.num(by.items.rows) + " items");
      stamp.textContent = (m.built ? "Data built " + m.built : "") + (parts.length ? ": " + parts.join(", ") : "") + ".";
    }, function () {});
  }

  WG.onMe(function () { if (live && live.onMe) { try { live.onMe(); } catch (e) { console.error(e); } } });
  WG.view("home", {title: "Home", render: render, leave: function () { live = null; }});
})();
