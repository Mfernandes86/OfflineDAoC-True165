/* Scripts loaded at start, in this order (owned by the lead). A file that does not exist yet is skipped.
   Each agent's files are already listed here: create the file with exactly this name and it is picked up. */
WG.manifest = [
  // shared libraries (pure functions, no DOM)
  "app/lib/concolor.js",          // M-U: con colours, level bands (ported from the server)
  "app/lib/itemtext.js",     // I-U: item types, slots, bonus names, stat formatting
  "app/lib/upgrade.js",      // I-U: upgrade scoring
  "app/lib/routes.js",       // V-U: route graph search
  "app/lib/quests.js",       // Q: quest lookups and the "quests" sections other pages embed
  // shared components
  "app/components/widgets.js", // P-U: table, filter bar, chips, level input, tabs, tooltips
  "app/components/map.js",     // P-U: the zone map viewer
  "app/components/me.js",      // P-U: the "my character" box in the header
  // pages
  "app/views/home.js",       // P-U
  "app/views/search.js",     // P-U: global search
  "app/views/zones.js",      // P-U: zone list + zone page
  "app/views/items.js",      // I-U: item finder, upgrade finder
  "app/views/item.js",       // I-U: one item
  "app/views/hunt.js",       // M-U: hunting finder
  "app/views/mobs.js",       // M-U: mob list
  "app/views/mob.js",        // M-U: one mob
  "app/views/vendors.js",    // V-U: merchants and what they sell
  "app/views/vendor.js",     // V-U: one merchant
  "app/views/routes.js",     // V-U: stable routes
  "app/views/quests.js",     // Q: quest finder
  "app/views/quest.js",      // Q: one quest
  "app/views/about.js"       // D-V: data sources, how the numbers are computed, build date
];
