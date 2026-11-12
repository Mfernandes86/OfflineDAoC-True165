extends SceneTree
## Issue #50 item 5 (berserk stall): how long your own shape change holds the main thread.
##   --headless --path godot -s res://tests/morph_load_probe.gd
## "old path": what Player.morph did per change before the fix - NpcManager._pack_model (glTF parse, textures,
## materials, scene generate + pack) and instantiate, on the main thread, every time.
## "new path" (when Player has the form cache): the form packed on a worker ahead of time (Player.prewarm_forms),
## then a change costs the main thread only the cache lookup and instantiate.

const FORMS := {
	"vendo (berserk, models 3 / 12)": "res://content/monsters/vendo_man__vendo_man/model.gltf",
	"necromancer shade (822)": "res://content/monsters/necroshade__shade/model.gltf",
}


func _initialize() -> void:
	var stop := [false]
	var watchdog := Thread.new()                             # hard timeout: 120 s
	watchdog.start(func():
		var t0 := Time.get_ticks_msec()
		while Time.get_ticks_msec() - t0 < 120000:
			if stop[0]:
				return
			OS.delay_msec(200)
		print("[morph_probe] TIMEOUT")
		OS.kill(OS.get_process_id()))
	await process_frame
	# (loaded now, not named: a -s script compiles before the autoloads exist - load_check.gd does the same)
	var NM: Script = load("res://game/npcs/npc_manager.gd")
	var P: Script = load("res://game/player.gd")
	if NM == null or P == null:
		print("[morph_probe] FAIL: scripts did not load")
		stop[0] = true
		watchdog.wait_to_finish()
		quit(1)
		return
	for label in FORMS:
		var path: String = FORMS[label]
		var runs: Array = []
		for i in 3:
			var t := Time.get_ticks_usec()
			var ps: PackedScene = NM.call("_pack_model", path)
			var n := ps.instantiate() as Node3D if ps else null
			runs.append((Time.get_ticks_usec() - t) / 1000.0)
			if n:
				n.free()
		print("[morph_probe] old path  %-32s %s ms (main thread, every change)" % [label, ", ".join(runs.map(func(x): return "%.0f" % x))])
	var names: Array = P.get_script_method_list().map(func(m): return String(m.name))
	if names.has("prewarm_paths"):
		for label in FORMS:
			var path: String = FORMS[label]
			var tw := Time.get_ticks_usec()
			P.call("prewarm_paths", [path])
			var frames := 0
			var worst := 0
			var worst_at := 0
			while not P.call("form_ready", path) and frames < 3000:
				var tf := Time.get_ticks_usec()
				await process_frame
				if Time.get_ticks_usec() - tf > worst:
					worst = Time.get_ticks_usec() - tf
					worst_at = frames
				frames += 1
			var bg := (Time.get_ticks_usec() - tw) / 1000.0
			var runs: Array = []
			for i in 3:
				var t := Time.get_ticks_usec()
				var n: Node3D = P.call("form_instance", path)
				runs.append((Time.get_ticks_usec() - t) / 1000.0)
				if n:
					n.free()
			print("[morph_probe] new path  %-32s %s ms (main thread per change); background load %.0f ms over %d frames, longest frame %.1f ms (frame %d)" % [
					label, ", ".join(runs.map(func(x): return "%.1f" % x)), bg, frames, worst / 1000.0, worst_at])
	else:
		print("[morph_probe] new path: Player has no form cache yet")
	stop[0] = true
	watchdog.wait_to_finish()
	quit(0)
