extends SceneTree
## The Hero's Stag form (StagECSEffect: model 4, a lurikeen 13; the 1.64 client's 583 / 859 Stag Man, Lurikeen Stag
## Man - pipeline/build_stag_164.py, 2026-11-12): each monsters.json row has the glTF, it packs and instances as the NPC
## views and Player's form cache load it, has the clips its row lists (and the ones a fighting form needs), the weapon
## bones the Vendo form uses, and its height at the row's scale. Also the player's morph into it and out again.
##   --headless --path godot -s res://tests/stag_form_probe.gd

const IDS := ["4", "13", "583", "859"]
const NEED_CLIPS := ["idle", "walk", "run", "death", "combat_idle", "attack_med", "attack_2h_med", "flinch",
		"parry_low", "block_low", "evade"]
const NEED_BONES := ["Bip01 R Held", "Bip01 L Held", "Bip01 L Shield", "Bip01 L Back", "Bip01 R Back",
		"Bip01 L Belt", "Bip01 R Belt", "Bip01 Head"]


func _height(m: Node3D) -> float:
	var top := -INF
	var bottom := INF
	for mi in m.find_children("*", "MeshInstance3D", true, false):
		var t := Transform3D.IDENTITY
		var n: Node = mi
		while n != m and n is Node3D:
			t = (n as Node3D).transform * t
			n = n.get_parent()
		var b: AABB = t * (mi as MeshInstance3D).get_aabb()
		top = maxf(top, b.end.y)
		bottom = minf(bottom, b.position.y)
	return top - bottom if top > bottom else 0.0


func _initialize() -> void:
	var stop := [false]
	var watchdog := Thread.new()                             # hard timeout: 120 s
	watchdog.start(func():
		var t0 := Time.get_ticks_msec()
		while Time.get_ticks_msec() - t0 < 120000:
			if stop[0]:
				return
			OS.delay_msec(200)
		print("[stag_probe] TIMEOUT")
		OS.kill(OS.get_process_id()))
	await process_frame
	var NM: Script = load("res://game/npcs/npc_manager.gd")
	var P: Script = load("res://game/player.gd")
	var table = JSON.parse_string(FileAccess.get_file_as_string("res://content/monsters/monsters.json"))
	var fails := 0
	if NM == null or P == null or not (table is Dictionary):
		print("[stag_probe] FAIL: scripts / monsters.json did not load")
		fails += 1
	else:
		var cls_forms: Dictionary = P.get_script_constant_map().get("CLASS_FORMS", {})
		print("[stag_probe] Player.CLASS_FORMS Hero = %s" % [cls_forms.get("Hero")])
		var packed := {}
		for id in IDS:
			var row: Dictionary = (table as Dictionary).get(id, {})
			var path := String(row.get("gltf", ""))
			if path == "":
				print("[stag_probe] FAIL %s: no monsters.json row with a gltf" % id)
				fails += 1
				continue
			if not packed.has(path):
				packed[path] = NM.call("_pack_model", path)
			var ps: PackedScene = packed[path]
			var n := ps.instantiate() as Node3D if ps else null
			if n == null:
				print("[stag_probe] FAIL %s: %s did not instance" % [id, path])
				fails += 1
				continue
			var players := n.find_children("*", "AnimationPlayer", true, false)
			var ap: AnimationPlayer = players[0] if players.size() > 0 else null
			var clips: Array = ap.get_animation_list() if ap else []
			var missing := []
			for c in NEED_CLIPS + (row.get("clip_seconds", {}) as Dictionary).keys():
				if not clips.has(c) and not missing.has(c):
					missing.append(c)
			for c in (row.get("cast_clips", {}) as Dictionary).values():
				if not clips.has(String(c)) and not missing.has(c):
					missing.append(c)
			var sks := n.find_children("*", "Skeleton3D", true, false)
			var no_bone := []
			for b in NEED_BONES:
				if sks.is_empty() or (sks[0] as Skeleton3D).find_bone(b) < 0:
					no_bone.append(b)
			var h := _height(n)
			var sc := float(row.get("scale", 1.0))
			print("[stag_probe] %-4s %-18s clips %d, missing %s, weapon bones missing %s, height %.1f x scale %.2f = %.1f units" % [
					id, String(row.get("name", "")), clips.size(), missing, no_bone, h, sc, h * sc])
			if not missing.is_empty() or not no_bone.is_empty() or h <= 0.0:
				fails += 1
			n.free()
		# your own Hero: into the form (loaded on the worker first, as prewarm_forms does) and out again
		var p: Node3D = P.new()
		root.add_child(p)
		p.call("set_model", "res://content/characters/celt_male_cata/celt_animated.gltf")
		var figure: Node3D = p.get("_model")
		var row4: Dictionary = (table as Dictionary).get("4", {})
		P.call("prewarm_paths", [4])
		var frames := 0
		while not P.call("form_ready", String(row4.get("gltf", ""))) and frames < 3000:
			await process_frame
			frames += 1
		p.call("morph", row4)
		var m: Node3D = p.get("_morph")
		var ok_in: bool = m != null and p.get("_model") == m and figure != null and not figure.visible
		var anim: AnimationPlayer = p.get("_anim")
		print("[stag_probe] player morph into 4: %s (form %s, playing %s)" % ["ok" if ok_in else "FAIL",
				m.name if m else "none", anim.current_animation if anim else "-"])
		p.call("morph", {})
		var ok_out: bool = p.get("_morph") == null and p.get("_model") == figure and figure.visible
		print("[stag_probe] player back to the figure: %s" % ["ok" if ok_out else "FAIL"])
		if not ok_in or not ok_out:
			fails += 1
		p.queue_free()
	print("[stag_probe] %s" % ["PASS" if fails == 0 else "FAIL (%d)" % fails])
	stop[0] = true
	watchdog.wait_to_finish()
	quit(0 if fails == 0 else 1)
