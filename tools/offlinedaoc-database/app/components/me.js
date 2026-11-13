/* OFFLINEDAOC Database - the "my character" box (P-U). Edits WG.me through WG.setMe; see widgets.md section 6. */
(function () {
  "use strict";
  var WG = window.WG, h = WG.h, ui = (WG.ui = WG.ui || {});
  var boxes = [];

  /* Class list from data/itemmeta.js (I-D). Accepts {classes: [{id, name, realm}]}, {classes: {id: {name, realm}}}
     or {classes: {id: name}, classRealm: {id: realm}}; returns [] until the file is there. */
  function classes() {
    var m = WG.data.itemmeta, out = [];
    if (!m || !m.classes) return out;
    var c = m.classes;
    if (c.cols) c = WG.rows(c);
    if (Array.isArray(c)) c.forEach(function (x) { if (x && x.id != null && x.name) out.push({id: +x.id, name: String(x.name), realm: +x.realm || 0}); });
    else Object.keys(c).forEach(function (id) {
      var x = c[id];
      if (x && typeof x === "object") out.push({id: +id, name: String(x.name || id), realm: +x.realm || 0});
      else if (x != null) out.push({id: +id, name: String(x), realm: +(m.classRealm && m.classRealm[id]) || 0});
    });
    out.sort(function (a, b) { return (a.realm - b.realm) || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0); });
    return out;
  }
  ui.classes = classes;
  ui.className = function (id) { var l = classes(); for (var i = 0; i < l.length; i++) if (l[i].id === +id) return l[i].name; return ""; };

  function build(opts) {
    opts = opts || {};
    var level = ui.levelInput({value: WG.me.level, onChange: function (n) { WG.setMe({level: n}); }});
    level.input.title = "Your character's level: con colours and the finders use it";
    var realm = h("select", {title: "Your realm", "aria-label": "Your realm", onchange: function () {
      var r = +realm.value, patch = {realm: r}, cl = classes().filter(function (c) { return c.id === WG.me.cls; })[0];
      if (cl && r && cl.realm && cl.realm !== r) patch.cls = 0;
      WG.setMe(patch);
    }}, WG.REALMS.map(function (n, i) { return h("option", {value: i}, i ? n : "Any realm"); }));
    var cls = h("select", {title: "Your class", "aria-label": "Your class", onchange: function () {
      var id = +cls.value, patch = {cls: id}, cl = classes().filter(function (c) { return c.id === id; })[0];
      if (cl && cl.realm) patch.realm = cl.realm;
      WG.setMe(patch);
    }});
    function fillClasses() {
      var list = classes(), r = WG.me.realm;
      WG.clear(cls);
      cls.appendChild(h("option", {value: 0}, "Any class"));
      if (!list.length) { cls.style.display = "none"; return; }
      cls.style.display = "";
      [1, 2, 3].forEach(function (rl) {
        if (r && r !== rl) return;
        var of = list.filter(function (c) { return c.realm === rl; }); if (!of.length) return;
        var grp = r ? cls : h("optgroup", {label: WG.REALMS[rl]});
        of.forEach(function (c) { grp.appendChild(h("option", {value: c.id}, c.name)); });
        if (!r) cls.appendChild(grp);
      });
      list.filter(function (c) { return !c.realm; }).forEach(function (c) { cls.appendChild(h("option", {value: c.id}, c.name)); });
      cls.value = WG.me.cls || 0; if (cls.selectedIndex < 0) cls.value = 0;
    }
    var el = h("div.wg-me" + (opts.large ? ".large" : ""),
      opts.title !== false ? h("span.wg-me-title", opts.title || "My character") : null,
      h("label.wg-me-field", h("span", "Level"), level), h("label.wg-me-field", opts.large ? h("span", "Realm") : null, realm), h("label.wg-me-field", opts.large ? h("span", "Class") : null, cls));
    function sync() {
      level.set(WG.me.level || 1);
      realm.value = WG.me.realm || 0; realm.className = WG.me.realm ? "realm-" + WG.me.realm : "";
      fillClasses();
    }
    sync();
    boxes.push({el: el, sync: sync, keep: !!opts.keep});
    return el;
  }
  WG.onMe(function () {
    boxes = boxes.filter(function (b) { return b.keep || document.body.contains(b.el); });
    boxes.forEach(function (b) { b.sync(); });
  });
  ui.meBox = function (opts) { var o = {large: true}; for (var k in (opts || {})) o[k] = opts[k]; return build(o); };

  var slot = document.getElementById("wg-me");
  if (slot) { WG.clear(slot); slot.appendChild(build({keep: true, title: "My character"})); }
  /* Class names arrive with the item data; load it quietly so the box is complete on every page. */
  WG.load("itemmeta").then(function () { boxes.forEach(function (b) { b.sync(); }); if (ui.measureHeader) ui.measureHeader(); }, function () {});
})();
