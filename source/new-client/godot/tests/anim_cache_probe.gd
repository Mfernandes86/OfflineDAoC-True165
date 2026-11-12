extends Node
## What a new figure's first animation step costs (the Camelot stutter, 2026-11-12): a fresh copy of each race figure
## already loaded, its AnimationPlayer played and advanced once - with the full clip library, with a small one, and
## without the deterministic mixer.
##   --headless --skip-login --zone=26 --ui-test --probe=anim_cache_probe


func _ms(c: Callable) -> float:
	var t := Time.get_ticks_usec()
	c.call()
	return (Time.get_ticks_usec() - t) / 1000.0


func _first_player(n: Node) -> AnimationPlayer:
	var ps := n.find_children("*", "AnimationPlayer", true, false)
	return ps[0] as AnimationPlayer if ps.size() > 0 else null


func _ready() -> void:
	await get_tree().create_timer(12.0).timeout
	for extra in ["bri_m", "bri_f", "hig_m"]:
		if not Fig3Figure._scenes.has(extra):
			Fig3Figure._scenes[extra] = Fig3Figure.pack_figure(extra)
	var prefixes: Array = Fig3Figure._scenes.keys()
	print("[animcache] figures loaded: ", prefixes)
	var done := 0
	for prefix in prefixes:
		var ps: PackedScene = Fig3Figure._scenes[prefix]
		if ps == null:
			continue
		# the library as the game leaves it (RESET added by the first copy)
		var probe := ps.instantiate() as Node3D
		add_child(probe)
		var ap0 := _first_player(probe)
		if ap0 == null:
			probe.queue_free()
			continue
		Fig3Figure.add_rest_reset(ap0)
		var lib := ap0.get_animation_library("")
		var clips := lib.get_animation_list()
		var tracks := 0
		for c in clips:
			tracks += lib.get_animation(c).get_track_count()
		probe.queue_free()
		await get_tree().process_frame
		var line := "[animcache] %-8s clips %3d tracks %6d |" % [prefix, clips.size(), tracks]
		for variant in ["full deterministic", "full plain", "small deterministic", "small plain"]:
			var t := Time.get_ticks_usec()
			var m := ps.instantiate() as Node3D
			var t_inst := (Time.get_ticks_usec() - t) / 1000.0
			t = Time.get_ticks_usec()
			add_child(m)
			var t_add := (Time.get_ticks_usec() - t) / 1000.0
			var ap := _first_player(m)
			if variant.begins_with("small"):
				var small := AnimationLibrary.new()
				for c in ["RESET", "idle", "walk", "run", "combat_idle", "stand"]:
					if lib.has_animation(c):
						small.add_animation(c, lib.get_animation(c))
				ap.remove_animation_library("")
				ap.add_animation_library("", small)
			ap.deterministic = variant.ends_with("deterministic")
			ap.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
			var clip := "idle" if ap.has_animation("idle") else String(ap.get_animation_list()[0])
			var t_first := _ms(func():
					ap.play(clip)
					ap.advance(0.0))
			var t_second := _ms(func(): ap.advance(0.033))
			var t_switch := 0.0
			if ap.has_animation("run"):
				t_switch = _ms(func():
						ap.play("run")
						ap.advance(0.033))
			line += " %s: instantiate %.1f add %.1f FIRST STEP %.1f next %.2f switch clip %.2f ms |" % [variant, t_inst, t_add,
					t_first, t_second, t_switch]
			m.queue_free()
			await get_tree().process_frame
		print(line)
		done += 1
		if done >= 4:
			break
	get_tree().quit()
