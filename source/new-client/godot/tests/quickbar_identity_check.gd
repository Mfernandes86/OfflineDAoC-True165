extends SceneTree
## GitHub issue #50 (a Berserker's Left Axe quickbar styles fired other styles under their old icons; menu tooltips
## one row off). Headless, no server, no files written:
##   --headless --path godot -s res://tests/quickbar_identity_check.gd
## 1. a Left Axe style dropped from list A still names that style after list B (a new ability first, a new Axe style
##    before Left Axe); 2. old-format slots ("srv:skill:<n>" + name only) are converted by name / icon, never by <n>;
##    a style you no longer have is "missing"; 3. a list spell follows its line when a line is inserted before it;
## 4. a late tooltip answer goes to its own skill, never to the next request; an answer naming nothing asked is not kept.

var S: Node
var bad := 0


class FakeClient extends Node:
	var sent: Array = []

	func Detail(t: int, id: int) -> void:
		sent.append([t, id])

	func Command(t: String) -> void:
		sent.append(t)


func _check(ok: bool, what: String) -> void:
	print("[quickbar_identity] ", "ok   " if ok else "FAIL ", what)
	if not ok:
		bad += 1


func _sk(page: int, id: int, name: String, icon := 0, level := 1) -> Dictionary:
	return {"level": level, "id": id, "page": page, "special": 0, "bonus": 0, "icon": icon, "name": name}


func _initialize() -> void:
	await process_frame
	S = root.get_node("Server")
	var pd: Node = root.get_node("PlayerData")
	var was_client = S.client
	var fake := FakeClient.new()
	root.add_child(fake)
	S.client = fake

	# list A: specs, Evade I, Axe styles, Left Axe styles
	var list_a := [_sk(0, 1, "Axe"), _sk(0, 2, "Left Axe"), _sk(1, 50, "Evade I", 12),
			_sk(2, 300, "Plague"), _sk(2, 301, "Doublefrost"), _sk(2, 400, "Assault"), _sk(2, 401, "Bloodletter")]
	# list B at the next login: a new ability first (Sprint), Evade renamed by its level, a new Axe style before Left Axe
	var list_b := [_sk(0, 1, "Axe"), _sk(0, 2, "Left Axe"), _sk(1, 60, "Sprint", 13), _sk(1, 50, "Evade II", 12),
			_sk(2, 300, "Plague"), _sk(2, 301, "Doublefrost"), _sk(2, 302, "Splitter"), _sk(2, 400, "Assault"),
			_sk(2, 401, "Bloodletter")]
	S.on_skills({"first": 0, "more": false, "list": list_a})
	var evade_pic: Array = S.skill_icon("icon", 12)      # (Sprint, 13: the same picture with another border)
	var evade_icon := int(evade_pic[0])

	# a slot dropped from the Combat sheet in list A (row "srv:skill:4" = Bloodletter)
	var dropped := {"kind": "style", "id": "srv:skill:4", "icon": 0, "name": "Bloodletter", "border": -1}
	S.resolve_entry(dropped)
	_check(int(dropped.get("sid", 0)) == 401 and int(dropped.get("page", -1)) == 2, "a dropped style keeps its id (401)")
	# old-format slots from a bar saved before this fix (no sid): their <n> is NOT trusted
	var old_style := {"kind": "style", "id": "srv:skill:4", "icon": 0, "name": "Bloodletter", "border": -1}
	var old_ability := {"kind": "ability", "id": "srv:skill:0", "icon": evade_icon, "name": "Evade I", "border": int(evade_pic[1])}
	var old_gone := {"kind": "style", "id": "srv:skill:3", "icon": 0, "name": "Comeback", "border": -1}
	var bars: Array = [[[dropped, old_style, old_ability, old_gone, {}, {}, {}, {}, {}, {}]]]
	var was_bars = pd.quickbars
	pd.quickbars = bars

	S.on_skills({"first": 0, "more": false, "list": list_b})  # the next login's order
	_check(String(dropped.id) == "srv:skill:6", "Left Axe style slot follows its style: %s (Bloodletter is 6th now)" % dropped.id)
	var skill_at: Dictionary = S.skills[2 + int(String(dropped.id).get_slice(":", 2))]
	_check(String(skill_at.name) == "Bloodletter" and int(skill_at.id) == 401, "the place it fires is Bloodletter")
	_check(String(old_style.id) == "srv:skill:6" and int(old_style.get("sid", 0)) == 401,
			"old-format slot converted by its name, not by <4> (Splitter): %s" % old_style.id)
	_check(String(old_ability.id) == "srv:skill:1" and String(old_ability.name) == "Evade II",
			"old-format ability found by its icon after a level rename: %s %s" % [old_ability.id, old_ability.name])
	_check(old_gone.has("missing") and not S.resolve_entry(old_gone) and S.skill_detail(old_gone).is_empty(),
			"a style you no longer have is missing (not used, no tooltip asked)")
	var d6: Array = S.skill_detail(dropped)
	_check(d6 == [6, 106], "its tooltip is asked for place 106 (style): %s" % str(d6))
	# going back to list A (a respec): the slot goes back with it
	S.on_skills({"first": 0, "more": false, "list": list_a})
	_check(String(dropped.id) == "srv:skill:4", "back to list A: %s" % dropped.id)

	# 3. list spells: a line inserted before the slot's line
	var lines_a := [{"name": "Rejuvenation", "spells": [{"level": 1, "id": 10, "icon": 1, "name": "Minor Heal", "kind": "spell"}]},
			{"name": "Enhancements", "spells": [{"level": 1, "id": 20, "icon": 2, "name": "Strength", "kind": "spell"}]}]
	var lines_b := [{"name": "Smiting", "spells": [{"level": 1, "id": 30, "icon": 3, "name": "Smite", "kind": "spell"}]}]
	lines_b.append_array(lines_a)
	for li in lines_a.size():
		S.on_spell_line({"done": false, "line": li, "name": lines_a[li].name, "spells": lines_a[li].spells})
	S.on_spell_line({"done": true, "line": 0})
	var spell := {"kind": "spell", "id": "srv:spell:1:1", "icon": 2, "name": "Strength", "border": -1}
	var old_spell := {"kind": "spell", "id": "srv:spell:1:1", "icon": 2, "name": "Strength", "border": -1}
	S.resolve_entry(spell)
	(bars[0][0] as Array)[4] = spell
	(bars[0][0] as Array)[5] = old_spell
	for li in lines_b.size():
		S.on_spell_line({"done": false, "line": li, "name": lines_b[li].name, "spells": lines_b[li].spells})
	S.on_spell_line({"done": true, "line": 0})
	_check(String(spell.id) == "srv:spell:2:1" and String(spell.get("line", "")) == "Enhancements",
			"a list spell follows its line: %s" % spell.id)
	_check(String(old_spell.id) == "srv:spell:2:1", "an old-format list spell converted by its name: %s" % old_spell.id)

	# 4. tooltips (list A again: Assault 3, Bloodletter 4)
	var assault := {"kind": "style", "id": "srv:skill:3", "name": "Assault", "icon": 0}
	var blood := {"kind": "style", "id": "srv:skill:4", "name": "Bloodletter", "icon": 0}
	var plague := {"kind": "style", "id": "srv:skill:1", "name": "Plague", "icon": 0}
	S.resolve_entry(assault)
	S.resolve_entry(blood)
	S.resolve_entry(plague)
	S.prefetch_skill(assault)
	S._detail_queue[0]["at"] = Time.get_ticks_msec() - 5000      # its answer is late (more than 3 s)
	S.prefetch_skill(blood)
	_check(S.take_detail({"mode": 0, "caption": "Assault", "lines": ["assault text"]}) == true,
			"the late Assault answer is kept (quietly)")
	_check(String(S.skill_lines(assault).get("caption", "")) == "Assault", "the late answer is filed under Assault")
	_check(S.skill_lines(blood).is_empty(), "... and not under Bloodletter (the next request)")
	S.take_detail({"mode": 0, "caption": "Bloodletter", "lines": ["bloodletter text"]})
	_check(String(S.skill_lines(blood).get("caption", "")) == "Bloodletter", "Bloodletter's own answer is filed under it")
	S.prefetch_skill(plague)
	var shown: bool = S.take_detail({"mode": 0, "caption": "Some Window", "lines": ["x"]})
	_check(not shown and S.skill_lines(plague).is_empty(), "an answer naming something else is not kept under Plague")
	S.take_detail({"mode": 0, "caption": "Plague", "lines": ["plague text"]})
	_check(String(S.skill_lines(plague).get("caption", "")) == "Plague", "Plague's own answer, after it, still lands")

	pd.quickbars = was_bars
	S.client = was_client
	S.skills = []
	S.spell_lines = []
	print("[quickbar_identity] ", "PASS" if bad == 0 else "%d FAILED" % bad)
	quit(bad)
