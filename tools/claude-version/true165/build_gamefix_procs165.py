"""True 1.65 0.8 gamefix PROCS (2026-11-12): obtainable items whose proc / charge spell id has no Spell row.

    python -I build_gamefix_procs165.py            writes add-gamefix-procs165.json (both copies) and the PROCS
                                                   entries of true165-rules.json (through <lead workspace>/gamefix/locks.py)
    python -I build_gamefix_procs165.py --table    also prints the decision table (markdown) for the report

What was wrong: 130 spell references on 128 obtainable items (drops, the Angau one-time drop, a DF seal merchant,
script-made bosses) point at spell ids this world's Spell table does not have. The server resolves item spells with
SkillBase.FindSpell (Item Effects line first, then the plain SpellID index, GameLiving.cs:795/860/911,
GameInventoryItem.cs:606/689): an id with no Spell row resolves to nothing and the proc / charge never fires.
Most of the ids are the old Public_DB "Item Effects" numbers (32100-32304, 40002-40025, ...); the same world carries
working rows for the same effects (the Public_DB 321xx DD series, the Atlas 311xx proc set, and the 92xxx rows that
are the old 32xxx ids + 60000, e.g. 92210 = 32210 "Life Drain", 92127 = 32127 "ProcDirectDamageSpirit47").

Each item is decided on its OWN period evidence (see DECISIONS): the period Allakhazam item page (Wayback copy
before 2003-11-01, development-source/research/period-data/archive/wayback-allakhazam/period_items.jsonl.gz), the
Uthgard item page (archive/uthgard), the current Allakhazam page only where nothing older exists, and the 1.64
patch note (epic dungeon armour legs/sleeves: reactive DD proc -> ablative proc). Where the item's own pages are
silent, an item is fixed only when the old id means the same effect on every period-attested item that uses it, or
its twin template / same-set sibling says so; otherwise it is left and listed. Nothing from Trials of Atlantis or
later is fixed.

Fixes: ItemTemplate updates in true165-rules.json (guarded by the old value, so a re-run or an already-fixed row is
a no-op), pointing at an existing Spell row with the period numbers, or at a new Spell row (972001-972013) in
add-gamefix-procs165.json when this world has no row with the period numbers. No flagged item is an add-file row,
so the rules (which run before the add-files) reach every one of them; the ItemTemplate update needs no Spell row
at the time it runs.
"""
import json, os, sqlite3, sys, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
OUTS = [os.path.join(ROOT, "runtime", "true165", "add-gamefix-procs165.json"),
        os.path.join(HERE, "add-gamefix-procs165.json")]
CLEAN = os.environ.get("OFFLINE_DAOC_CLEAN_DB") or os.path.join(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."), "runtime", "data", "opendaoc-165.sqlite3.db")   # the clean world (database/unpack_db.py)
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lead_tools"))
import locks  # noqa: E402

LANE = "PROCS"
TAG = locks.TAG % LANE
PACKAGE = "true165-gamefix-procs"
STAMP = "2026-11-12 00:00:00"

# ---- new Spell rows: copied from an existing item-effect row of the same kind, then the period numbers set ----------
# (base, overrides, why). ClientEffect / Icon ids all exist in the real 1.64 client's spells.csv.
NEW = {
    972001: (31129, dict(Name="57 DD (350 AoE) Heat", Description="Damages the target and enemies near it for 57 heat.",
                         Damage=57.0, Value=0.0, ClientEffect=4052, Icon=4052),
             "Book of Chaos: DD 57 heat, radius 350, instant (Allakhazam 3343, 2003-07; Uthgard 3348); no such row. "
             "Range 1500 like the world's other targeted 350-radius item DD (31129); the pages give no range."),
    972002: (32108, dict(Name="ProcDirectDamageHeat64", Damage=64.0, ClientEffect=4053, Icon=4053),
             "Alfrigg's Dwarf-Smasher / -Splitter: DD 64 heat, range 1350, instant (Uthgard 4464, 4444); no 64 heat row."),
    972003: (32120, dict(Name="ProcDirectDamageHeat88", Damage=88.0, Range=1350, CastTime=0.0),
             "Fire Heatstone Stalagmite: DD 88, range 1350 (Allakhazam 2480, 2003-10); heat (Uthgard 1875)."),
    972004: (32123, dict(Name="ProcDirectDamageSpirit88", Damage=88.0, CastTime=0.0),
             "Twilight Soul Searer: DD 88 spirit, range 1500 (Allakhazam 2067, 2003-09)."),
    972005: (32124, dict(Name="ProcDirectDamageHeat97", Damage=97.0),
             "Golden Alloy Kite Shield / Immolated Dragonfire Cleaver: DD 97 heat (Allakhazam 2845, 2744, 2003-09)."),
    972006: (31114, dict(Name="331 DD Energy", Description="Damages the target for 331", DamageType=12,
                         ClientEffect=4070, Icon=4070),
             "Rotting Staff: DD 331 energy, range 1500, 2.0 s cast, 6 charges (Allakhazam 4674, 2003-08; Uthgard 2651)."),
    972007: (32180, dict(Name="ProcDamageShieldMatter4", Damage=4.0, Duration=20),
             "Fiery Revenger / Brilliant Adremel's Daemon Fire Spear: damage shield 4, 20 s (Allakhazam 4997, 8266, "
             "2003-09); matter as the old id's own name (32181 = ProcDamageShieldMatter47, its twin 92181)."),
    972008: (31104, dict(Name="150 Ablative (50%)", Damage=50.0),
             "Health buffer 150, absorbs 50%, 10 min (Allakhazam 10226, 10303, 10210 and the post-1.64 epic dungeon "
             "leg/sleeve pages); 31104 has the 150 but absorbs only the default 25% (AblativeArmorSpellHandler.cs:19)."),
    972009: (31022, dict(Name="17% Haste Charge", Duration=600),
             "Bracer of Wonderous Alacrity: combat speed 17%, 3 s cast, 10 charges (Uthgard 7009); 10 min as the period "
             "haste charges (Allakhazam 5853, 6158). 31022 lasts 20 min."),
    972010: (31110, dict(Name="3 Power Regen Charge", Duration=600, CastTime=3.0),
             "Greater Staff of the Creator / Fulmination / Dominance: power regen 3, 10 min, 3 s cast "
             "(Allakhazam 10289, 2003-10; Uthgard 5182, 4030, 7010). 31110 lasts 20 min."),
    972011: (31105, dict(Name="2 Power Regen Charge", Duration=600, CastTime=3.0),
             "Staff of the Storm / Shroud: power regen 2, 10 min, 3 s cast (Allakhazam 10231; Uthgard 7011, 6999)."),
    972012: (31122, dict(Name="6 Health Regen Charge", Duration=600, CastTime=3.0),
             "Fairche Crios: health regen 6, 10 min, 3 s cast, 10 charges (Allakhazam 10314, 2003-09). 31122 lasts 20 min."),
    972013: (31101, dict(Name="11.3 Dmg Add Matter", DamageType=14),
             "Runic Giant-Bone Gloves / Pikeman's Belt: damage add 11(.3), 30 s (Allakhazam 10267, 10265); matter "
             "(Uthgard 7001, 5060). 31101 is energy."),
}
FIRST_TOOLTIP = 64001   # DbSpell.TooltipId is a ushort; these are checked unused below

# ---- decisions: (Id_nb, column, old id, new id or None, evidence) -------------------------------------------------
# Evidence keys: P = period Allakhazam page (citem, Wayback capture), U = Uthgard item page id, Z = current Allakhazam,
# I = what the old id means on every period-attested item using it, T = twin template, S = same-set sibling,
# N164 = the 1.64 patch note. A None new id = left alone (reason given).
P164 = ("1.64 patch note on the period pages: the reactive proc on the leg and sleeve pieces of the epic dungeon "
        "armour suits became an ablative proc instead of a DD proc; post-1.64 pages: health buffer 150, 50%, 10 min")
D = [
    # lifedrain 65 body, range 300 -> 92210 (= old 32210 + 60000, "Life Drain", 100% returned)
    ("Archaic_Granite_Long_Sword", "ProcSpellID", 32210, 92210, "P 10208 (2003-10) lifedrain 65 body; U 4067"),
    ("Archaic_Granite_Rapier", "ProcSpellID", 32210, 92210, "P 10272 (2003-09) lifedrain 65 body; U 5180"),
    ("Runic_Granite_Great_Sword", "ProcSpellID", 32210, 92210, "P 10241 (2003-10) lifedrain 65 body; U 2628"),
    ("Runic_Granite_Great_hammer", "ProcSpellID", 32210, 92210, "P (2003-10-23) lifedrain 65 body; U 5030"),
    ("Runic_Granite_War_Pick", "ProcSpellID", 32210, 92210, "P (2003-10-23) lifedrain 65 body; U 4425"),
    ("Runic_Granite_Blade", "ProcSpellID", 32210, 92210, "U 4062 lifedrain 65 body"),
    ("Dread_Soul_Splitter", "ProcSpellID", 32210, 92210, "P 4524 (2003-10) lifedrain 65; I 32210 = body on 10 items"),
    ("Wind_Whisper", "ProcSpellID", 32210, 92210, "I 32210 = lifedrain 65 body on all 10 period items (Dartmoor set); no own page"),
    ("Furious_Pious_Defender", "ProcSpellID", 32210, 92210,
     "P 10234 (2003-10) lifedrain 65 body; U 7005 (both say 10 charges: left a proc, column not changed)"),
    ("Dire_Discordant_Flute", "SpellID", 32210, 92210, "P 7922 (2003-10) lifedrain 65 body, 10 charges"),
    # lifedrain 65 cold -> 31126
    ("Eiorharn's_Skull_Cracker", "ProcSpellID", 32211, 31126, "P 10256 (2003-09) lifedrain 65 cold"),
    ("Dread_Bone_Great_Hammer", "ProcSpellID", 32302, 31126, "P 6540 (2003-07) lifedrain 65 cold"),
    ("Frenzied_Blackhearted_Axe", "ProcSpellID", 40016, 31126, "P 6194 (2003-09) lifedrain 65 cold"),
    ("icy_great_hammer", "ProcSpellID", 40016, 31126, "P 5389 (2003-09) lifedrain 65 cold"),
    ("Skufs_Hauberk", "ProcSpellID", 40016, 31126, "P 5701 (2003-09) lifedrain 65; I 40016 = cold on 5 items"),
    ("Protector_of_the_Storm", "ProcSpellID", 40016, 31126, "U 4440 lifedrain 65 cold"),
    ("Laoch_Meadhon_Targaid", "ProcSpellID", 32211, 31126, "U 8254 lifedrain 65 cold"),
    # str/con debuff 56, 1 min, body -> 92220 (= old 32220 + 60000)
    ("Archaic_Granite_Short_Sword", "ProcSpellID", 32220, 92220, "P 10373 (2003-09) str/con -56 1 min body; U 4417"),
    ("Archaic_Granite_Stiletto", "ProcSpellID", 32220, 92220, "P 10627 (2003-10) str/con -56 1 min body; U 4719"),
    ("Runic_Granite_Gauntlets", "ProcSpellID", 32220, 92220, "P 10374 (2003-09) str/con -56 1 min body; U 5176"),
    ("Malevolent_Great_Sword", "ProcSpellID", 32220, 92220, "P 4177 (2003-10) str/con -56 1 min body"),
    ("Malevolent_Hammer", "ProcSpellID", 32220, 92220, "S Malevolent Great Sword (same set, same id); I 32220 = str/con -56 on 6 items"),
    ("Fairich_Meadhon_Targaid", "SpellID", 32220, 92220, "P 10379 (2003-09) str/con -56 1 min body, 10 charges; U 8261"),
    ("Bracer of Dire Omen", "SpellID", 32220, 92220, "U 6602 str/con -56 body, 5 charges; Z 4379"),
    ("Render`s Bone Breaker", "ProcSpellID", 14365, 92220,
     "T the item's other template Renders_Bone_Breaker has 32220 (= str/con -56 on 6 period items); P 6485 page gives no delve"),
    # dex/qui debuff 56 -> 92230 (= old 32230 + 60000)
    ("Swarm_Callers_Lute", "SpellID", 32230, 92230, "P 7920 (2003-10) dex/qui -56 body, 10 charges"),
    # DD 95 heat -> 32124 ProcDirectDamageHeat47
    ("Ellyll_Rapier", "ProcSpellID", 655177, 32124, "P 979 (2003-10) DD 95 heat"),
    ("Ellyll_Sword", "ProcSpellID", 655177, 32124, "U 4057 DD 95 heat; Z 2811"),
    ("Gladius_of_the_Battlelord", "ProcSpellID", 655177, 32124, "U 2872, 7081 DD 95 heat"),
    ("Kraggon_Sword", "ProcSpellID", 655177, 32124, "U 4413 DD 95 heat; Z 900"),
    ("Golden_Alloy_Axe", "ProcSpellID", 40025, 32124, "U 2754 DD 95 heat; Z 2583"),
    ("Shimmering_Golden_Alloy_Great_Spear", "ProcSpellID", 40002, 32124, "U 3399 DD 95 heat; Z 4146"),
    ("Halberd of the Stonewatch", "ProcSpellID", 40002, 32124, "U 6997 DD 95 heat; T Shimmering_Halberd_of_the_Stonewatch has 32124"),
    ("shimmering_Glimmer_Wrath_Great_Hammer", "ProcSpellID", 40002, 32124, "U 768 DD 95 heat; T Glimmer Wrath Great Hammer has 32124"),
    # DD 95 energy -> 32126, DD 95 spirit -> 92127, DD 86 cold -> 32121, DD 41 heat -> 32100, DD 59 heat -> 32108
    ("3_Glimmerspirit_Sword", "ProcSpellID", 40025, 32126, "P 2816 (2003-05) DD 95 energy"),
    ("shimmering_Glimmerstrike_Crusher", "ProcSpellID", 40025, 32126, "U 756 DD 95 energy"),
    ("gnarledwitherwoodstaff", "ProcSpellID", 32127, 32126, "U 5090 DD 95 energy (P 2810 page: 'procs at 48', no delve)"),
    ("Sanguine Bowyer`s Gauntlets", "ProcSpellID", 32127, 92127, "P 6754 (2003-08) DD 95 spirit"),
    ("Facimil", "ProcSpellID", 32127, 92127, "U 4717 DD 95 spirit"),
    ("Shimmering_Feather_Light_Granite_Hammer", "ProcSpellID", 32127, 92127,
     "I 32127 = ProcDirectDamageSpirit47 (its twin 92127; 6 period items DD 95 spirit); no own page"),
    ("Deathly Cabalist Staff", "ProcSpellID", 4075, 32121, "P 9713 (2003-10) DD 86 cold, range 1500"),
    ("Fathomless_Short_Sword", "ProcSpellID", 65552, 32100, "U 3190 DD 41 heat, range 1350; T 'Fathomless Short Sword' has 32100"),
    ("Soul_Silencer", "ProcSpellID", 53463452, 32108, "P 9715 (2003-08) DD 59 heat, range 1350; U 1559"),
    # new DD rows
    ("Golden Alloy Kite Shield", "ProcSpellID", 32031, 972005, "P 2845 (2003-09) DD 97 heat, range 1500"),
    ("Immolated_Dragonfire_Cleaver", "ProcSpellID", 32031, 972005, "P 2744 (2003-09) DD 97 heat, range 1350; U 2763"),
    ("Fire Heatstone Stalagmite", "ProcSpellID", 40010, 972003, "P 2480 (2003-10) DD 88, range 1350; U 1875 heat"),
    ("Shimmering_Twilight_Soul_Searer", "ProcSpellID", 32031, 972004, "P 2067 (2003-09) DD 88 spirit, range 1500"),
    ("Alfriggs_Dwarf-Smasher", "ProcSpellID", 55449, 972002, "U 4464 DD 64 heat, range 1350"),
    ("Alfriggs_Dwarf-Splitter", "ProcSpellID", 55449, 972002, "U 4444 DD 64 heat, range 1350"),
    ("Book_of_Chaos", "SpellID", 30, 972001, "P 3343 (2003-07) DD 57 heat radius 350, 2 charges; U 3348"),
    # DD 331 charges
    ("Black_Diamond_Staff", "SpellID", 40012, 31114, "P 3235 (2003-07) DD 331 cold, 2 s cast; U 4451"),
    ("Warshadow_Staff_of_the_Moon", "SpellID", 40012, 31114, "P 4088 (2003-09) DD 331 cold; U 742"),
    ("Bitten_Staff", "SpellID", 65537, 31114, "P 4681 (2003-10) DD 331 cold; U 2653"),
    ("Rotting_Staff", "SpellID", 65527, 972006, "P 4674 (2003-08) DD 331 energy; U 2651"),
    ("2_sesoi_staff", "SpellID", 32127, 31115, "P 4171 (2003-09) Elder Staff of Iceshadow: DD 331 spirit, 5 charges; U 4507"),
    # DoT 64
    ("Brilliant Saeor`s Barbed Hammer", "ProcSpellID", 32006, 31154, "P 3403 (2003-10) DoT 64 heat 20 s"),
    ("Ensorcelled_Robes_of_Celerity", "ProcSpellID", 32006, 31154, "U 6170 DoT 64 heat"),
    ("Baln`s Flawless Sulfuric Slicer", "ProcSpellID", 32006, 31154, "Z 19083, 17574 DoT 64 heat 20 s; I 32006 = heat DoT on 2 period items"),
    ("Dommel's_Flawless_Incendiary_Seal", "SpellID", 32006, 31154, "U 5070 DoT 64 heat, 2 charges"),
    ("Dommel's_Incendiary_Seal", "SpellID", 32006, 31154, "S Dommel's Flawless Incendiary Seal (same drop, same id)"),
    ("Kvasirs_Sword_of_Blight", "ProcSpellID", 32012, 31124, "P 6153 (2003-09) DoT 64 body, 4 s"),
    ("Miasmatic_Axe", "ProcSpellID", 32012, 31124, "P 10063 (2003-09) DoT 64 body, 4 s"),
    ("Sinister_Miasmatic_Axe", "ProcSpellID", 32012, 31124, "S Miasmatic Axe (same drop, same id)"),
    ("Verdant_Sword", "ProcSpellID", 32012, 31153, "P 6028 (2003-05) DoT 64 energy, 4 s"),
    # damage shields
    ("Brilliant Spear of Malice", "ProcSpellID", 55447, 31117, "P 3771 (2003-09) damage shield 5, 20 s"),
    ("Nokkvis_Mauler", "ProcSpellID", 32233, 32180, "P 6314 (2003-09) damage shield 5, 15 s"),
    ("Spiked_Jailors_Gloves", "ProcSpellID", 32233, 32180, "I 32233 = damage shield 5 on both period items; P (2003-07) page no delve"),
    ("Shadow Whisper`s Long Bow", "ProcSpellID", 32233, 32180, "P 6248 (2003-10) damage shield 5 (10 s); T Shadow_Whispers_Long_Bow has 32180"),
    ("Fiery_Revenger", "ProcSpellID", 32181, 972007, "P 4997 (2003-09) damage shield 4, 20 s"),
    ("Brilliant Adremel`s Daemon Fire Spear", "ProcSpellID", 55443, 972007, "P 8266 (2003-09) damage shield 4, 20 s"),
    # AF 75 ("Shield"), 10 min -> 31131
    ("Afanc_Scale_Cloak", "SpellID", 32161, 31131, "P 5234 (2003-10) Shield 75, 10 min, 2 charges"),
    ("Enchanted_Afanc_Cloak", "SpellID", 32161, 31131, "P 3270 (2003-08) Shield 75, 10 min, 2 charges"),
    ("Afanc_Hide_Cloak", "SpellID", 32161, 31131, "S Legendary Afanc cloak set (same id); P 2920 comment asks how to use its charges"),
    ("Holy_Afanc_Cloak", "SpellID", 32161, 31131, "S Legendary Afanc cloak set (same id)"),
    ("Dire_Discordant_Drum", "SpellID", 32161, 31131, "P 7921 (2003-10) Shield 75, 10 min, 10 charges"),
    ("Small_Runic_Defender", "ProcSpellID", 32161, 31131, "P 10237 (2003-10) Shield 75, 10 min; U 5179"),
    ("Warlord`s Golden Flail", "ProcSpellID", 32161, 31131, "P 8858 (2003-10) Shield 75"),
    # heals
    ("Soul-Scarred_Vest", "ProcSpellID", 31002, 31145, "P 6239 (2003-09) heal 180 self"),
    ("Sanguine Bowyer`s Vest", "ProcSpellID", 32155, 31145, "P 6753 (2003-10) comment '180 pt self heal proc'; chest pieces of the set heal 180"),
    ("Rain", "ProcSpellID", 32304, 31157, "P 10232 (2003-10) heal 80 self; U 6506"),
    # ablative 150 (50%)
    ("Nightfire", "ProcSpellID", 32162, 972008, "P 10303 (2003-10) health buffer 150, 50%, 10 min"),
    ("Rockcrusher", "ProcSpellID", 32162, 972008, "P (2003-10-27) health buffer 150, 50%, 10 min"),
    ("Widowmaker", "ProcSpellID", 32152, 972008, "P 10210 (2003-10) health buffer 150, 50%, 10 min"),
    ("Thunder", "ProcSpellID", 32303, 972008, "P 11835 (2003-10) health buffer 150, 10 min; U 6503"),
    ("Bracer_of_Dazzling_Allurement", "SpellID", 32162, 972008, "P 10226 (2003-09) health buffer 150, 50%, 10 charges; U 7003"),
    ("Belt of Coiled Destruction", "SpellID", 32152, 972008, "U 4722 health buffer 150, 10 charges"),
    ("Foe_Hammer's_Sleeves", "ProcSpellID", 32155, 972008, "N164 (Tuscaren sleeves); P 7086 is pre-1.64 (2003-05, DD 95 cold)"),
    ("Livid_Deceiver's_Leggings", "ProcSpellID", 32152, 972008, "N164 (Caer Sidi legs); P (2003-05-27) is pre-1.64 (2003-05, DD 95 cold)"),
    ("Sanguine Bowyer`s Leggings", "ProcSpellID", 32152, 972008, "N164 (Caer Sidi legs); P 6755 carries the note"),
    # damage add 11.3 matter, 30 s
    ("Runic_Giant-Bone_Gloves", "ProcSpellID", 32200, 972013, "P 10267 (2003-10) damage add 11, 30 s; U 7001 matter"),
    ("Pikeman's_Belt", "SpellID", 32200, 972013, "P 10265 (2003-09) damage add 11, 10 charges; U 5060 11.3 matter"),
    ("Hakrs_Hauberk", "ProcSpellID", 32200, 972013, "I 32200 = damage add 11, 30 s on 5 period items; P 5394 page no delve"),
    # buffs on charges
    ("Bracer of Wonderous Alacrity", "SpellID", 32171, 972009, "U 7009 combat speed 17, 3 s cast, 10 charges"),
    ("Greater_Staff_of_Fulmination", "SpellID", 31033, 972010, "P (2003-10-27) power regen 3, 10 min; U 4030"),
    ("Greater_Staff_of_the_Creator", "SpellID", 31033, 972010, "P 10289 (2003-10) power regen 3, 10 min, 10 charges; U 5182"),
    ("Greater_Staff_of_Dominance", "SpellID", 31033, 972010, "U 7010 power regen 3, 10 charges"),
    ("Staff_of_the_Storm", "SpellID", 40018, 972011, "P 10231 (2003-09) power regen 2, 10 min, 10 charges; U 7011"),
    ("Staff_of_the_Shroud", "SpellID", 40018, 972011, "U 6999 power regen 2, 10 charges"),
    ("Fairche_Crios", "SpellID", 32231, 972012, "P 10314 (2003-09) health regen 6, 10 min, 10 charges"),
    ("Rune_Inscribed_Sleeves", "SpellID", 32190, 31130, "P 10498 (2003-10) acuity 75, 10 min"),
    ("Cloth_Cap_Alb", "SpellID", 40024, 31130, "U 4674 (Golestandt) acuity 75, 10 charges; P 4431 (Mid twin) stat enhancement 75"),
    ("Inntinn_Bioran", "SpellID", 32190, 31130, "U 744 acuity 75, 10 charges"),
    ("Lesser_Staff_of_the_Sigils", "SpellID", 32190, 31130, "U 4701 acuity 75, 5 charges"),
    ("Fas_Bioran", "SpellID", 32190, 31130, "S Inntinn Bioran (same staff family, same id)"),
    ("Lesser_Staff_of_the_Pack", "SpellID", 32190, 31130, "S Lesser Staff of the Sigils (same family and drop, same id)"),
    # ---- left alone ----
    ("Ancient Vitriolic Fang", "SpellID", 4075, None, "unknown: no period, Uthgard or Allakhazam page"),
    ("Bracer_Of_Zoarkat", "SpellID", 30102, None, "unknown: P 6437 (2003-05) shows no magical ability (DB gives it 100 charges)"),
    ("Bracer_Of_Zoarkat", "SpellID1", 30165, None, "unknown: as above"),
    ("Dire_Medallion", "SpellID", 31028, None, "unknown: P 6466 (2003-05) shows no magical ability (DB gives it 999 charges)"),
    ("Blackened_Granite_Shield", "SpellID", 32190, None, "unknown: P 10369 lists only its proc (32180, which works); old id 32190 is mixed"),
    ("Burr's_Prey_Seeker", "SpellID", 40003, None, "unknown as charges: U 4454 shows a heal-80 PROC, not charges"),
    ("Cloak of Webweaver`s Silk", "SpellID", 32156, None, "unknown: U 3744 'enhancement 40', stat not named"),
    ("Cloth_Cap_Alb", "SpellID1", 40006, None, "unknown: the period pages show one ability only (fixed in SpellID)"),
    ("Daemon_Fire_Ring", "SpellID", 32190, None, "unknown: U 5695 acuity 41 vs Z 6366 acuity 75; no period page"),
    ("Flawless_Daemon_Fire_Ring", "SpellID", 32190, None, "unknown: U 5695 acuity 41 vs Z 18747 acuity 75 15 min (and 0 charges)"),
    ("Dread_Bone_Spear", "ProcSpellID", 32158, None, "unknown: P 6325 shows no ability; the Atlas twin's endurance proc is later"),
    ("Fate_Sealer", "ProcSpellID", 32031, None, "unknown type: P 8670 DD 97 with no damage type; old id 32031 is mixed"),
    ("Shimmering_Fate_Stealer", "ProcSpellID", 32031, None, "unknown: P 2497 (2003-01) shows no ability"),
    ("Guthlac_Great_Shield", "SpellID", 32151, None, "unknown: P 6618 no delve; one period use of 32151 (ablative 100) vs Atlas twin 50"),
    ("Icy_War_Spear", "ProcSpellID", 32151, None, "unknown: P 5825 no delve; old id 32151 has one period use only"),
    ("Legions_Flawless_Soul_Crusher", "ProcSpellID", 32181, None, "unknown: current Allakhazam pages disagree (DS 5.1 vs lifedrain 65 cold)"),
    ("2_noh_necklace", "SpellID", 55446, None, "unknown: P 9593 Necklace of Hoarfrost 'Imp Stat Buff 75', which stat pair not given"),
    ("Nokkvis_Vile_Slayer", "ProcSpellID", 32012, None, "unknown type: 32012 is DoT 64 with a different damage type per item"),
    ("Oro`s Helm of Fiery Might", "ProcSpellID", 32156, None, "unknown: no page"),
    ("Brilliant Oro`s Helm of Fiery Might", "ProcSpellID", 32156, None, "unknown: no page"),
    ("Baln`s Mephitic Bludgeoner", "ProcSpellID", 32230, None, "unknown: no page; old id 32230 is mixed (dex/qui vs str/con)"),
    ("Flawless_Lecherous_Gress_Skin", "SpellID", 55443, None,
     "unknown: only the current Allakhazam pages (Z 24187, 17739, 16202: damage shield 3.8, 20 s, 2 charges, no "
     "damage type); no period or Uthgard delve, and no row with those numbers"),
    ("Bane_of_Life", "SpellID", 33333, None, "unknown value: P 5853 haste 10 min, 10 charges, % not given"),
    ("Seething_Ember", "SpellID", 32171, None, "unknown value: P 6158 haste 10 min, 10 charges, % not given (Uthgard 17 vs Atlas 7 for 32171)"),
    ("Exquisite_Avernal_Malison_Quarterstaff", "ProcSpellID", 16000, None,
     "later expansion: the 'Exquisite' DF seal merchant set (with Catacombs hand wraps / mauler staves) is not on any "
     "period page; period DF sold 'Avernal Malison' without the prefix"),
]


def sql_str(s):
    return "'" + s.replace("'", "''") + "'"


def spell_rows(con):
    con.row_factory = sqlite3.Row
    used_tt = {r[0] for r in con.execute("SELECT TooltipId FROM Spell")}
    have = {r[0] for r in con.execute("SELECT SpellID FROM Spell")}
    rows, tt = [], FIRST_TOOLTIP
    for sid, (base, over, why) in sorted(NEW.items()):
        assert sid not in have, sid
        while tt in used_tt:
            tt += 1
        row = dict(con.execute("SELECT * FROM Spell WHERE SpellID=?", (base,)).fetchone())
        row.update(over)
        row.update(SpellID=sid, PackageID=PACKAGE, TooltipId=tt, LastTimeRowUpdated=STAMP,
                   Spell_ID=str(uuid.uuid5(uuid.NAMESPACE_URL, "offlinedaoc/true165/gamefix-procs/spell/%d" % sid)),
                   _why=why, _base=base)
        used_tt.add(tt)
        rows.append(row)
    return rows


def check(con):
    """Every decision matches the clean world; every flagged reference is decided exactly once."""
    con.row_factory = None
    spells = {r[0] for r in con.execute("SELECT SpellID FROM Spell")} | set(NEW)
    seen = set()
    for idnb, col, old, new, _ in D:
        cur = con.execute(f"SELECT {col} FROM ItemTemplate WHERE Id_nb=?", (idnb,)).fetchone()
        assert cur and cur[0] == old, (idnb, col, old, cur)
        assert old not in spells or old in NEW, (idnb, old)
        assert new is None or new in spells, (idnb, new)
        assert (idnb, col) not in seen, (idnb, col)
        seen.add((idnb, col))
    return seen


def main():
    con = sqlite3.connect("file:" + CLEAN + "?mode=ro", uri=True)
    decided = check(con)
    rows = spell_rows(con)
    con.close()
    data = {"table": "Spell", "key": "Spell_ID",
            "reason": TAG + "Spell rows for item procs / charges whose period effect this world had no row for "
                            "(written by build_gamefix_procs165.py)",
            "rows": [{k: v for k, v in r.items() if not k.startswith("_")} for r in rows]}
    text = json.dumps(data, indent=1, ensure_ascii=False) + "\n"
    for p in OUTS:
        with open(p, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)

    groups = {}
    for idnb, col, old, new, ev in D:
        if new is not None:
            groups.setdefault((col, old, new), []).append(idnb)
    names = {sid: r["Name"] for sid, r in zip(sorted(NEW), rows)}

    def fn(rules):
        rules["updates"] = [u for u in rules["updates"] if not u.get("reason", "").startswith(TAG)]
        for (col, old, new), ids in sorted(groups.items(), key=lambda g: (g[0][2], g[0][0], g[0][1])):
            rules["updates"].append({
                "sql": f"UPDATE ItemTemplate SET {col} = {new} WHERE {col} = {old} AND Id_nb IN "
                       f"({','.join(sql_str(i) for i in sorted(ids))})",
                "reason": TAG + f"{col} {old} has no Spell row, so it never fired; period effect = spell {new}"
                                f"{' (' + names[new] + ', added by add-gamefix-procs165.json)' if new in NEW else ''}"
                                f": {', '.join(sorted(ids))} (evidence per item in gamefix/reports/PROCS.md)"})
    size = locks.edit_rules(LANE, fn)
    fixed = sum(1 for d in D if d[3] is not None)
    print(f"decided {len(decided)} references: {fixed} fixed, {len(D) - fixed} left; {len(rows)} new Spell rows; "
          f"{len(groups)} rules updates; rules file {size} bytes")
    if "--table" in sys.argv:
        lvl = {}
        c2 = sqlite3.connect("file:" + CLEAN + "?mode=ro", uri=True)
        for idnb, col, old, new, ev in D:
            r = c2.execute("SELECT Name, Level FROM ItemTemplate WHERE Id_nb=?", (idnb,)).fetchone()
            lvl[idnb] = r
        for idnb, col, old, new, ev in D:
            name, level = lvl[idnb]
            verdict = ("left" if new is None else
                       (f"new spell {new} ({names[new]})" if new in NEW else f"fixed to {new}"))
            print(f"| {name} (`{idnb}`) | {level} | {col} | {old} | {verdict} | {ev} |")


if __name__ == "__main__":
    main()
