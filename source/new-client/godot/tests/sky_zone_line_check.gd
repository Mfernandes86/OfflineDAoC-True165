extends SceneTree
## Issue #50 item 1 (sky lane, 2026-11-12): the walk from Vale of Mularn (100) into Muspelheim (104) under the 1.64 sky
## and area fog. Prints the sky, fog colour and fog distances before the line, at the crossing and every few metres in.
## Fails if the sky changes at the line or the fog jumps more than a small step between two frames.
##   --headless --path godot -s res://tests/sky_zone_line_check.gd


## (classes loaded at run time, after the autoloads, as load_check.gd does)
func _zone(id: int):
	var z = load("res://game/zone_loader.gd").new()
	z.info = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zone%03d/zone.json" % id))
	return z


func _initialize() -> void:
	await process_frame
	var bad := 0
	var follow := Node3D.new()
	root.add_child(follow)
	var z100 = _zone(100)
	var z104 = _zone(104)
	var Sky = load("res://game/sky_system.gd")
	var sky = Sky.new()
	root.add_child(sky)
	sky.set_process(false)
	sky.storm_forced = 0.0
	follow.global_position = Vector3(1600.0, 0.0, 400.0)       # Vale of Mularn, 38 m short of the east line
	sky.setup(z100, follow)
	sky.set_hour(12.0)
	sky._process(0.05)
	var dt := 0.1                                               # 10 frames a second, walking 10 m/s (a fast horse)
	var prev_c: Color = sky.environment.fog_light_color
	var prev_e: float = sky.environment.fog_depth_end
	var sky_before: String = sky.sky_name
	print("[sky] in 100: sky %s fog %s begin %.0f end %.0f" % [sky.sky_name, sky.environment.fog_light_color, sky.environment.fog_depth_begin, sky.environment.fog_depth_end])
	var size100 := float(z100.info.size_m)
	var x := 1600.0
	var crossed := false
	for step in 160:
		x += 1.0
		if not crossed and x >= size100:
			crossed = true
			x -= size100
			follow.global_position = Vector3(x, 0.0, 400.0)
			sky.set_zone(z104)                                  # as ZoneViewer._cross does after shifting the player
			print("[sky] crossed into 104: sky %s (was %s)" % [sky.sky_name, sky_before])
			if sky.sky_name != sky_before:
				print("[sky] FAIL the sky changed at the zone line")
				bad += 1
		follow.global_position = Vector3(x, 0.0, 400.0)
		sky._process(dt)
		var c: Color = sky.environment.fog_light_color
		var e: float = sky.environment.fog_depth_end
		var jump := maxf(maxf(absf(c.r - prev_c.r), absf(c.g - prev_c.g)), absf(c.b - prev_c.b))
		if jump > 0.06 or absf(e - prev_e) > 25.0:
			print("[sky] FAIL one-frame jump at step %d: colour %.3f, end %.1f m" % [step, jump, absf(e - prev_e)])
			bad += 1
		prev_c = c
		prev_e = e
		if step % 10 == 0 or (crossed and x < 3.0):
			print("[sky] %s x %.0f m: area %.2f fog %s begin %.0f end %.0f" % ["104" if crossed else "100", x,
					sky._area_w, c, sky.environment.fog_depth_begin, e])
	if sky._area_w < 0.99:
		print("[sky] FAIL the area fog never came in fully (%.2f)" % sky._area_w)
		bad += 1
	# load into Muspelheim deep inside the fog: full strength on the first frame, no fade-in after the loading screen
	var sky2 = Sky.new()
	root.add_child(sky2)
	sky2.set_process(false)
	sky2.storm_forced = 0.0
	follow.global_position = Vector3(800.0, 0.0, 800.0)
	sky2.setup(z104, follow)
	sky2.set_hour(12.0)
	sky2._process(0.016)
	print("[sky] loaded into 104 at the centre: sky %s area %.2f fog %s end %.0f" % [sky2.sky_name, sky2._area_w, sky2.environment.fog_light_color, sky2.environment.fog_depth_end])
	if sky2._area_w < 0.99 or sky2.sky_name != "sky164_midgard":
		print("[sky] FAIL load into 104")
		bad += 1
	print("[sky] %s" % ("ok" if bad == 0 else "%d FAIL" % bad))
	quit(bad)
