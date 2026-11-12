extends Node
## Stutter probe (owner 2026-11-12: "it always stutters" walking south from Camelot's vault down the main path).
## Walks the player along a real street route (found on the zone's own floors and walls), several passes, and records
## EVERY frame: wall time, where the frame's time went (script process / draw / the rest), shader pipelines compiled
## (by kind), nodes added (by class), resources, video memory, draw calls, NPC views built - then says which frames
## hitched, where on the route, and what was happening in them. Read only: nothing in the game is changed.
##   Godot --path godot -- --skip-login --zone=26 --ui-test --probe=stutter_probe
##       --from=27308,15900 --to=27550,23242      zone coordinates (/loc units) of the walk's ends
##       [--passes=4] [--speed=4.775] [--out=<abs folder>] [--label=camelot] [--light] [--line] [--settle=10]
##   --light   no per-frame read-backs from the render thread (pipelines, video memory, draws): a control run that
##             shows whether the measuring itself changes the pacing
##   --line    walk the straight line on the ground instead of a street route (open country)
const U := 0.025                                    # DAoC units -> metres
var v: Node
var p: Player
var mgr: NpcManager
var out_dir := ""
var label := "walk"
var light := false
var speed := 191.0 * 0.025
var passes := 4
var settle := 10.0
var straight := false
var pass_doors := false                              # --doors: the route may go through door leaves (into buildings)
var reverse := false                                 # --reverse: trace the street from --to back toward --from (stays outdoors)
var route: PackedVector3Array = PackedVector3Array()   # the street route, a point about every metre
var route_len := 0.0

# per-frame record (flat arrays: nothing allocated per frame while walking)
const COLS := ["phase", "t_ms", "wall_ms", "proc_ms", "draw_ms", "rest_ms", "ticks", "x", "y", "pipe_canvas", "pipe_mesh",
		"pipe_surface", "pipe_draw", "pipe_spec", "nodes", "nodes_added", "resources", "vram_mb", "tex_mb", "draws",
		"prims", "views_built", "t_process_ms", "t_physics_ms", "scripts_ms", "sync_ms", "gpu_pump_ms", "gpu_jobs", "views_alive", "deferred_ms", "build_ms", "grp_max_ms", "grp_count", "grp_start_ms", "grp_span_ms"]
var rec: PackedFloat32Array = PackedFloat32Array()
var slow_detail: Array = []                          # [frame index, parts, node classes] for frames over 20 ms
var phase := -1                                      # -1 not recording; else the phase's number
var phase_names: Array = []
var _t0 := 0
var _t_proc := 0
var _t_pre := 0
var _t_post := 0
var _t_last := 0                                     # the last node's _process this frame (LastTick): scripts end here
var _jobs_last := 0
var _t_def := 0                                      # the deferred calls queued up to the last node's _process are done
var _build_us_last := 0
var _ticks := 0
var _pipe_last := [0, 0, 0, 0, 0]
var _added := 0
var _added_classes := {}
var _built_last := 0
var _frame := 0
const PIPE := [RenderingServer.RENDERING_INFO_PIPELINE_COMPILATIONS_CANVAS, RenderingServer.RENDERING_INFO_PIPELINE_COMPILATIONS_MESH,
		RenderingServer.RENDERING_INFO_PIPELINE_COMPILATIONS_SURFACE, RenderingServer.RENDERING_INFO_PIPELINE_COMPILATIONS_DRAW,
		RenderingServer.RENDERING_INFO_PIPELINE_COMPILATIONS_SPECIALIZATION]


class LastTick extends Node:
	var probe: Node
	func _process(_d: float) -> void:
		probe._t_last = Time.get_ticks_usec()
		_after_deferred.call_deferred()
	func _after_deferred() -> void:
		probe._t_def = Time.get_ticks_usec()


func _physics_process(_d: float) -> void:
	_ticks += 1


func _on_node_added(n: Node) -> void:
	if phase < 0:
		return
	_added += 1
	var c := n.get_class()
	_added_classes[c] = int(_added_classes.get(c, 0)) + 1


func _process(_delta: float) -> void:
	var now := Time.get_ticks_usec()
	if phase >= 0 and _t_proc > 0:
		var wall := (now - _t_proc) / 1000.0
		var proc := (_t_pre - _t_proc) / 1000.0 if _t_pre > _t_proc else -1.0
		var draw := (_t_post - _t_pre) / 1000.0 if _t_post > _t_pre and _t_pre > _t_proc else -1.0
		var rest := (now - _t_post) / 1000.0 if _t_post > _t_proc else -1.0
		var pd := [0, 0, 0, 0, 0]
		var vram := 0.0
		var tex := 0.0
		var draws := 0.0
		var prims := 0.0
		if not light:
			for i in 5:
				var c := int(RenderingServer.get_rendering_info(PIPE[i]))
				pd[i] = c - int(_pipe_last[i])
				_pipe_last[i] = c
			vram = RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_VIDEO_MEM_USED) / 1048576.0
			tex = RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED) / 1048576.0
			draws = RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TOTAL_DRAW_CALLS_IN_FRAME)
			prims = RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TOTAL_PRIMITIVES_IN_FRAME)
		var built := mgr.prof_built - _built_last if mgr else 0
		_built_last = mgr.prof_built if mgr else 0
		var gp := p.global_position
		rec.append_array(PackedFloat32Array([phase, (now - _t0) / 1000.0, wall, proc, draw, rest, _ticks, gp.x / U, gp.z / U,
				pd[0], pd[1], pd[2], pd[3], pd[4], Performance.get_monitor(Performance.OBJECT_NODE_COUNT), _added,
				Performance.get_monitor(Performance.OBJECT_RESOURCE_COUNT), vram, tex, draws, prims, built,
				Performance.get_monitor(Performance.TIME_PROCESS) * 1000.0, Performance.get_monitor(Performance.TIME_PHYSICS_PROCESS) * 1000.0,
				(_t_last - _t_proc) / 1000.0 if _t_last > _t_proc else -1.0,
				(_t_pre - _t_last) / 1000.0 if _t_pre > _t_last and _t_last > _t_proc else -1.0,
				GpuJobs.max_pump_usec / 1000.0, GpuJobs.jobs_run - _jobs_last, mgr.active.size() if mgr else 0,
				(_t_def - _t_last) / 1000.0 if _t_def > _t_last and _t_last > _t_proc else -1.0,
				((mgr.prof_build_us - _build_us_last) / 1000.0) if mgr else 0.0,
				0.0, 0.0, -1.0, -1.0]))                 # (the per-view group timers need probe-only fields in Npc: see the notes)
		_build_us_last = mgr.prof_build_us if mgr else 0
		GpuJobs.max_pump_usec = 0
		_jobs_last = GpuJobs.jobs_run
		if wall > 20.0:
			slow_detail.append([_frame, Perf.frame_parts.duplicate(), _added_classes.duplicate()])
		_frame += 1
	Perf.frame_parts = {}
	_added = 0
	_added_classes.clear()
	_ticks = 0
	_t_proc = now


func _arg(name: String, def: String) -> String:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--" + name + "="):
			return a.get_slice("=", 1)
		if a == "--" + name:
			return "1"
	return def


func _ready() -> void:
	v = get_tree().current_scene
	out_dir = _arg("out", "")
	label = _arg("label", "walk")
	light = _arg("light", "") != ""
	straight = _arg("line", "") != ""
	pass_doors = _arg("doors", "") != ""
	reverse = _arg("reverse", "") != ""
	speed = float(_arg("speed", str(speed)))
	passes = int(_arg("passes", "4"))
	settle = float(_arg("settle", "10"))
	RenderingServer.frame_pre_draw.connect(func(): _t_pre = Time.get_ticks_usec())
	RenderingServer.frame_post_draw.connect(func(): _t_post = Time.get_ticks_usec())
	get_tree().node_added.connect(_on_node_added)
	process_priority = -100000                           # first in the frame; LastTick is last: the scripts run between
	var last := LastTick.new()
	last.probe = self
	last.process_priority = 100000
	get_tree().root.add_child.call_deferred(last)
	await get_tree().create_timer(settle).timeout       # the zone, its NPCs and the first frames
	p = v.player
	mgr = v.npcs
	if v.get("ui"):
		v.ui.visible = _arg("ui", "") != ""
	var a := _arg("from", "").split(",")
	var b := _arg("to", "").split(",")
	var from := Vector3(float(a[0]) * U, 0.0, float(a[1]) * U)
	var to := Vector3(float(b[0]) * U, 0.0, float(b[1]) * U)
	from.y = float(a[2]) * U if a.size() > 2 else v.zone._grid_height(from.x, from.z)
	to.y = float(b[2]) * U if b.size() > 2 else v.zone._grid_height(to.x, to.z)
	var seed := to if reverse else from
	p.global_position = seed + Vector3(0, 0.3, 0)
	await get_tree().create_timer(1.5).timeout          # the floors' physics under the start
	seed.y = _ground(Vector3(seed.x, seed.y + 1.0, seed.z))
	var t_route := Time.get_ticks_msec()
	if straight:
		route = _line_route(from, to)
	elif reverse:                                       # the street as far up toward --from as it goes, walked from there
		route = _street_route(seed, from)
		route.reverse()
	else:
		route = _street_route(seed, to)
	if route.size() < 2:
		print("[stutter] NO ROUTE from %s to %s - falling back to the straight line" % [a, b])
		route = _line_route(from, to)
	for i in range(1, route.size()):
		route_len += route[i].distance_to(route[i - 1])
	print("[stutter] %s: zone %s, route %d points, %.0f m (%.0f ms to find), %s, window %s, light=%s" % [label,
			v.zone.info.get("name", "?"), route.size(), route_len, Time.get_ticks_msec() - t_route,
			"straight line" if straight else "street route", get_window().size, light])
	print("[stutter] renderer %s on %s; vsync %s; max fps %d; settings file %s" % [RenderingServer.get_current_rendering_method(),
			RenderingServer.get_video_adapter_name(), DisplayServer.window_get_vsync_mode(), Engine.max_fps, UiArt.settings_path])
	_place(0.0, 1.0)
	await get_tree().create_timer(2.0).timeout
	_t0 = Time.get_ticks_usec()
	_sync_counters()
	await _phase_still("still", 4.0)
	await _phase_spin("spin 1", 6.0)
	await _phase_spin("spin 2", 6.0)
	for i in passes:
		var back := i == 2                               # the third pass walks back up, facing the other way
		await _phase_walk("pass %d %s" % [i + 1, "north" if back else "south"], back)
	await _phase_still("still after", 3.0)
	phase = -1
	_report()
	get_tree().quit()


var _built0 := 0
var _build_us0 := 0
var _parsed0 := 0
var _parse_us0 := 0


func _sync_counters() -> void:
	Npc.prof_build.clear()
	Npc.prof_peak.clear()
	if mgr == null:
		return
	_built0 = mgr.prof_built
	_build_us0 = mgr.prof_build_us
	_parsed0 = mgr.prof_parsed
	_parse_us0 = mgr.prof_parse_us
	if not light:
		for i in 5:
			_pipe_last[i] = int(RenderingServer.get_rendering_info(PIPE[i]))
	_built_last = mgr.prof_built if mgr else 0


func _begin(name: String) -> void:
	phase_names.append(name)
	_t_proc = 0                                         # (the first frame of a phase is not measured against the pause)
	phase = phase_names.size() - 1


func _phase_still(name: String, secs: float) -> void:
	_begin(name)
	await get_tree().create_timer(secs).timeout
	phase = -1


func _phase_spin(name: String, secs: float) -> void:
	_begin(name)
	var t := 0.0
	var yaw0 := p.cam_yaw
	while t < secs:
		await get_tree().process_frame
		t += get_process_delta_time()
		p.cam_yaw = yaw0 + TAU * t / secs
		p.char_yaw = p.cam_yaw
	phase = -1


func _phase_walk(name: String, back: bool) -> void:
	_place(route_len if back else 0.0, -1.0 if back else 1.0)
	await get_tree().create_timer(1.5).timeout
	_begin(name)
	var d := 0.0
	while d < route_len:
		await get_tree().process_frame
		d += speed * minf(get_process_delta_time(), 0.1)
		_place(route_len - d if back else d, -1.0 if back else 1.0)
	phase = -1


## The player at `d` metres along the route, looking the way it walks (dir 1 = toward the end).
func _place(d: float, dir: float) -> void:
	var acc := 0.0
	var i := 1
	while i < route.size() - 1 and acc + route[i].distance_to(route[i - 1]) < d:
		acc += route[i].distance_to(route[i - 1])
		i += 1
	var seg := route[i].distance_to(route[i - 1])
	var at := route[i - 1].lerp(route[i], clampf((d - acc) / maxf(seg, 0.001), 0.0, 1.0))
	var ahead := route[mini(i + 3, route.size() - 1)] if dir > 0.0 else route[maxi(i - 4, 0)]
	var look := ahead - at
	p.global_position = at + Vector3(0, 0.05, 0)
	p.velocity = Vector3.ZERO
	if Vector2(look.x, look.z).length() > 0.5:
		var yaw := atan2(-look.x, -look.z)
		p.char_yaw = lerp_angle(p.char_yaw, yaw, 0.08)
		p.cam_yaw = p.char_yaw


func _ground(at: Vector3) -> float:
	if mgr:
		return mgr.ground_at(at)
	return v.region.height_at(at.x, at.z, at.y) if v.get("region") else v.zone.height_at(at.x, at.z, at.y)


func _line_route(from: Vector3, to: Vector3) -> PackedVector3Array:
	var pts := PackedVector3Array()
	var n := int(ceil(from.distance_to(to)))
	var y := from.y
	for i in n + 1:
		var q := from.lerp(to, float(i) / maxf(n, 1))
		y = _ground(Vector3(q.x, y + 1.0, q.z))
		pts.append(Vector3(q.x, y, q.z))
	return pts


## A walkable route on the zone's own collision: a flood over a 1 m grid from the start (a step is allowed when the
## floor under the next cell is within 0.9 m and no wall stands between the two at knee and chest height), then the
## shortest chain of cells to the end.
func _street_route(from: Vector3, to: Vector3) -> PackedVector3Array:
	var space: PhysicsDirectSpaceState3D = (v as Node3D).get_world_3d().direct_space_state
	var margin := 45.0
	var x0 := minf(from.x, to.x) - margin
	var z0 := minf(from.z, to.z) - margin
	var w := int(absf(from.x - to.x) + 2.0 * margin) + 1
	var h := int(absf(from.z - to.z) + 2.0 * margin) + 1
	var height := PackedFloat32Array()
	height.resize(w * h)
	height.fill(-1e9)
	var prev := PackedInt32Array()
	prev.resize(w * h)
	prev.fill(-1)
	var sx := int(from.x - x0)
	var sz := int(from.z - z0)
	var gx := int(to.x - x0)
	var gz := int(to.z - z0)
	var start := sz * w + sx
	height[start] = from.y
	prev[start] = start
	var queue := PackedInt32Array([start])
	var head := 0
	var best := start
	var best_d := 1e18
	while head < queue.size():
		var c := queue[head]
		head += 1
		var cx := c % w
		var cz := c / w
		var dgoal := float((cx - gx) * (cx - gx) + (cz - gz) * (cz - gz))
		if dgoal < best_d:
			best_d = dgoal
			best = c
		if cx == gx and cz == gz:
			break
		var cy := height[c]
		for dd in [[1, 0], [-1, 0], [0, 1], [0, -1]]:
			var nx: int = cx + dd[0]
			var nz: int = cz + dd[1]
			if nx < 0 or nz < 0 or nx >= w or nz >= h:
				continue
			var n := nz * w + nx
			if prev[n] >= 0:
				continue
			var wx := x0 + nx + 0.5
			var wz := z0 + nz + 0.5
			var q := PhysicsRayQueryParameters3D.create(Vector3(wx, cy + 1.2, wz), Vector3(wx, cy - 3.0, wz), ZoneLoader.LAYER_FLOOR)
			var hit: Dictionary = space.intersect_ray(q)
			if hit.is_empty():
				continue
			var ny := float(hit.position.y)
			if absf(ny - cy) > 0.9:
				continue
			var a := Vector3(x0 + cx + 0.5, cy, z0 + cz + 0.5)
			var b := Vector3(wx, ny, wz)
			var blocked := false
			for up in [0.45, 1.2]:
				var wq := PhysicsRayQueryParameters3D.create(a + Vector3(0, up, 0), b + Vector3(0, up, 0), ZoneLoader.LAYER_WALL)
				wq.hit_from_inside = true
				var wh: Dictionary = space.intersect_ray(wq)
				if not wh.is_empty() and not (pass_doors and _is_door(wh.get("collider"))):
					blocked = true
					break
			if blocked:
				continue
			height[n] = ny
			prev[n] = c
			queue.append(n)
	var cells := PackedInt32Array()
	var c2 := best
	while prev[c2] != c2:
		cells.append(c2)
		c2 = prev[c2]
	cells.append(c2)
	cells.reverse()
	print("[stutter] flood: %d cells reached of %d, the end %s (nearest reached cell %.1f m from it)" % [queue.size(), w * h,
			"reached" if best_d < 1.5 else "NOT reached", sqrt(best_d)])
	var pts := PackedVector3Array()
	for c3 in cells:
		pts.append(Vector3(x0 + (c3 % w) + 0.5, height[c3], z0 + (c3 / w) + 0.5))
	# a breadth-first chain is a staircase of 1 m steps: average each point with its neighbours a few times
	for _k in 6:
		var sm := pts.duplicate()
		for i in range(1, pts.size() - 1):
			sm[i] = (pts[i - 1] + pts[i] * 2.0 + pts[i + 1]) * 0.25
		pts = sm
	return pts


## A door leaf's collider: the player opens doors, so the route may pass them.
func _is_door(c: Variant) -> bool:
	var n := c as Node
	for _i in 4:
		if n == null:
			return false
		if n is ZoneDoor or String(n.name).to_lower().contains("door"):
			return true
		n = n.get_parent()
	return false


func _pct(sorted: Array, q: float) -> float:
	return float(sorted[mini(int(sorted.size() * q), sorted.size() - 1)]) if sorted.size() > 0 else 0.0


func _report() -> void:
	var n := rec.size() / COLS.size()
	var k := COLS.size()
	print("[stutter] %d frames recorded" % n)
	var json := {"label": label, "cols": COLS, "phases": phase_names, "route_m": route_len, "light": light, "slow": []}
	for ph in phase_names.size():
		var walls: Array = []
		var total := 0.0
		var pipes := [0, 0, 0, 0, 0]
		var added := 0
		var built := 0
		var vram0 := -1.0
		var vram1 := 0.0
		var proc_sum := 0.0
		var draw_sum := 0.0
		var rest_sum := 0.0
		for f in n:
			if int(rec[f * k]) != ph:
				continue
			var wms := rec[f * k + 2]
			walls.append(wms)
			total += wms
			proc_sum += maxf(rec[f * k + 3], 0.0)
			draw_sum += maxf(rec[f * k + 4], 0.0)
			rest_sum += maxf(rec[f * k + 5], 0.0)
			for i in 5:
				pipes[i] += int(rec[f * k + 9 + i])
			added += int(rec[f * k + 15])
			built += int(rec[f * k + 21])
			if vram0 < 0.0:
				vram0 = rec[f * k + 17]
			vram1 = rec[f * k + 17]
		if walls.is_empty():
			continue
		var s := walls.duplicate()
		s.sort()
		var med := _pct(s, 0.5)
		var hitch_ms := maxf(2.0 * med, 16.7)
		var c16 := 0
		var c33 := 0
		var c50 := 0
		var c100 := 0
		var hitches := 0
		var lost := 0.0
		for x in walls:
			c16 += int(x > 16.7)
			c33 += int(x > 33.3)
			c50 += int(x > 50.0)
			c100 += int(x > 100.0)
			if x > hitch_ms:
				hitches += 1
				lost += x - med
		print("[stutter] %-14s %5d frames %5.1f s  fps avg %.0f  frame ms: median %.1f p95 %.1f p99 %.1f max %.0f | over 16.7: %d, 33: %d, 50: %d, 100: %d | hitches (over %.0f ms) %d = %.1f per minute, %.0f ms lost | pipelines canvas %d mesh %d surface %d draw %d spec %d | nodes added %d, views built %d, vram %+.0f MB | per frame: process %.1f draw %.1f rest %.1f ms" % [
				phase_names[ph], walls.size(), total / 1000.0, walls.size() / (total / 1000.0), med, _pct(s, 0.95), _pct(s, 0.99),
				float(s[-1]), c16, c33, c50, c100, hitch_ms, hitches, hitches / (total / 60000.0), lost, pipes[0], pipes[1],
				pipes[2], pipes[3], pipes[4], added, built, vram1 - vram0, proc_sum / walls.size(), draw_sum / walls.size(),
				rest_sum / walls.size()])
	# the worst frames, with what was going on in them
	var order: Array = []
	for f in n:
		order.append(f)
	order.sort_custom(func(x, y): return rec[x * k + 2] > rec[y * k + 2])
	var detail := {}
	for d in slow_detail:
		detail[int(d[0])] = d
	print("[stutter] the 25 worst frames [phase | ms = process + draw + rest | /loc | pipelines c/m/s/d/sp | nodes added | views built | vram MB | parts ms | node classes]:")
	for f in order.slice(0, 25):
		var d: Array = detail.get(f, [f, {}, {}])
		var parts := ""
		var pk: Array = (d[1] as Dictionary).keys()
		pk.sort_custom(func(x, y): return int(d[1][x]) > int(d[1][y]))
		for key in pk.slice(0, 4):
			parts += "%s %.1f " % [key, int(d[1][key]) / 1000.0]
		var cls := ""
		var ck: Array = (d[2] as Dictionary).keys()
		ck.sort_custom(func(x, y): return int(d[2][x]) > int(d[2][y]))
		for key in ck.slice(0, 5):
			cls += "%s %d " % [key, int(d[2][key])]
		print("[stutter]   %-12s %6.1f = %5.1f + %5.1f + %5.1f | %6d %6d | %d/%d/%d/%d/%d | %4d | %2d | %5.0f | %s| %s" % [
				phase_names[int(rec[f * k])], rec[f * k + 2], rec[f * k + 3], rec[f * k + 4], rec[f * k + 5], int(rec[f * k + 7]),
				int(rec[f * k + 8]), int(rec[f * k + 9]), int(rec[f * k + 10]), int(rec[f * k + 11]), int(rec[f * k + 12]),
				int(rec[f * k + 13]), int(rec[f * k + 15]), int(rec[f * k + 21]), rec[f * k + 17], parts, cls])
	# what building the NPC views cost, by step (the client's own timers)
	if mgr == null:
		_write_files(n, k, json)
		return
	var keys := Npc.prof_build.keys()
	keys.sort_custom(func(x, y): return int(Npc.prof_build[x]) > int(Npc.prof_build[y]))
	var built_all := maxi(mgr.prof_built - _built0, 1)
	print("[stutter] NPC views built while recording: %d, %.1f ms each on average (%.0f ms in all); model parses %d, %.0f ms" % [
			mgr.prof_built - _built0, (mgr.prof_build_us - _build_us0) / 1000.0 / built_all, (mgr.prof_build_us - _build_us0) / 1000.0,
			mgr.prof_parsed - _parsed0, (mgr.prof_parse_us - _parse_us0) / 1000.0])
	for key in keys:
		print("[stutter]   build step %-16s %8.0f ms total, %6.1f ms per view, longest single %6.1f ms" % [key,
				int(Npc.prof_build[key]) / 1000.0, int(Npc.prof_build[key]) / 1000.0 / built_all, int(Npc.prof_peak.get(key, 0)) / 1000.0])
	print("[stutter] views alive at the end %d, spawns known %d" % [mgr.active.size(), mgr.states.size()])
	for d in slow_detail:
		json["slow"].append({"frame": d[0], "parts": d[1], "classes": d[2]})
	_write_files(n, k, json)


func _write_files(n: int, k: int, json: Dictionary) -> void:
	if out_dir != "":
		DirAccess.make_dir_recursive_absolute(out_dir)
		var fcsv := FileAccess.open(out_dir.path_join(label + ".csv"), FileAccess.WRITE)
		fcsv.store_line(",".join(COLS))
		for f in n:
			var row := PackedStringArray()
			for c in k:
				row.append(str(snappedf(rec[f * k + c], 0.01)))
			fcsv.store_line(",".join(row))
		fcsv.close()
		var fj := FileAccess.open(out_dir.path_join(label + ".json"), FileAccess.WRITE)
		fj.store_string(JSON.stringify(json))
		fj.close()
		var fr := FileAccess.open(out_dir.path_join(label + ".route.csv"), FileAccess.WRITE)
		for q in route:
			fr.store_line("%d,%d,%d" % [int(q.x / U), int(q.z / U), int(q.y / U)])
		fr.close()
		print("[stutter] written to ", out_dir)
