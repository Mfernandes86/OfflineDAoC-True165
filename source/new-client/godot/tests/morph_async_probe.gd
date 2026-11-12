extends SceneTree
## Issue #50 item 5: your own shape change when the form is NOT loaded yet - morph() must return at once (your figure
## stays), the form loads on a worker and is taken when it is in; a newer change (back to your figure) cancels it;
## going back is instant and puts the figure back.
##   --headless --path godot -s res://tests/morph_async_probe.gd

const VENDO := {"gltf": "res://content/monsters/vendo_man__vendo_man/model.gltf", "figure": "vendo_man", "scale": 1.4}
const SHADE := {"gltf": "res://content/monsters/necroshade__shade/model.gltf", "figure": "Necroshade", "scale": 0.35,
		"alpha": 0.5}


func _initialize() -> void:
	var stop := [false]
	var watchdog := Thread.new()                             # hard timeout: 120 s
	watchdog.start(func():
		var t0 := Time.get_ticks_msec()
		while Time.get_ticks_msec() - t0 < 120000:
			if stop[0]:
				return
			OS.delay_msec(200)
		print("[morph_async] TIMEOUT")
		OS.kill(OS.get_process_id()))
	await process_frame
	var P: Script = load("res://game/player.gd")
	var fails := 0
	var p: Node3D = P.new()
	root.add_child(p)
	p.call("set_model", "res://content/characters/celt_male_cata/celt_animated.gltf")
	var figure: Node3D = p.get("_model")
	if figure == null:
		print("[morph_async] FAIL no figure model")
		fails += 1
	# 1. a form not loaded: returns at once, figure still shown, loads, then taken
	var t := Time.get_ticks_usec()
	p.call("morph", VENDO)
	var ms := (Time.get_ticks_usec() - t) / 1000.0
	print("[morph_async] morph call (not loaded): %.1f ms, morphed now=%s" % [ms, p.get("_morph") != null])
	if p.get("_morph") != null or ms > 20.0:
		fails += 1
	var frames := 0
	while p.get("_morph") == null and frames < 2000:
		await process_frame
		frames += 1
	print("[morph_async] form taken after %d frames: %s, figure hidden=%s" % [frames, p.get("_morph") != null, not figure.visible])
	if p.get("_morph") == null or figure.visible:
		fails += 1
	# 2. back to your figure: instant, the figure back
	t = Time.get_ticks_usec()
	p.call("morph", {})
	print("[morph_async] back to figure: %.1f ms, figure shown=%s model is figure=%s" % [(Time.get_ticks_usec() - t) / 1000.0,
			figure.visible, p.get("_model") == figure])
	if not figure.visible or p.get("_model") != figure:
		fails += 1
	# 3. cached now: a second berserk is instant
	t = Time.get_ticks_usec()
	p.call("morph", VENDO)
	print("[morph_async] second change (cached): %.1f ms, morphed=%s anim clips=%d" % [(Time.get_ticks_usec() - t) / 1000.0,
			p.get("_morph") != null, (p.get("_anim") as AnimationPlayer).get_animation_list().size() if p.get("_anim") else 0])
	if p.get("_morph") == null:
		fails += 1
	p.call("morph", {})
	# 4. a change while another form loads, then back before it is in: never taken
	p.call("morph", SHADE)
	p.call("morph", {})
	frames = 0
	while not P.call("form_ready", SHADE.gltf) and frames < 2000:
		await process_frame
		frames += 1
	await process_frame
	print("[morph_async] cancelled form loaded after %d frames, taken=%s" % [frames, p.get("_morph") != null])
	if p.get("_morph") != null:
		fails += 1
	print("[morph_async] %s" % ("PASS" if fails == 0 else "FAIL %d" % fails))
	p.free()
	stop[0] = true
	watchdog.wait_to_finish()
	quit(fails)
