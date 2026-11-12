extends SceneTree
## The 1.64 boundary walls (game/world/zone_bounds.gd, content/zones/zoneNNN/bound.json from pipeline/build_bounds.py; GitHub issue
## #50 item 4: out of bounds in Muspelheim). Headless, no game:
##   Godot --headless --path godot -s res://tests/zone_bounds_probe.gd
## 1. Data: lines / points / walls of Muspelheim (Mid), Vale of Mularn (Mid, joins Muspelheim and East Svealand),
##    Camelot Hills (Alb) and Lough Derg (Hib).
## 2. Known places (the world database, mode=ro, 2026-11-12: BindPoint, ZonePoint, Mob) on the playable side; points
##    past the Muspelheim rim not; the walk across the Mularn -> Muspelheim crossing (the two Gokstads) and across the
##    Mularn -> East Svealand border goes out through no wall.
## 3. Physics, with the player's own capsule and mask: walking out at the Muspelheim rim stops at the wall; walking in
##    from past it goes through (a character saved outside is not trapped); Uppland's frontier wall (drawn both ways)
##    stops both ways.

const ZoneBounds := preload("res://game/world/zone_bounds.gd")
const U := 0.025
var _fails := 0
var _body: CharacterBody3D
var _case := 0
var _frames := 0
var _cases := []                      # [name, zone node, start m, heading, check (Callable on the end position)]


func _ok(cond: bool, what: String) -> void:
	print("[bounds] %s  %s" % ["ok  " if cond else "FAIL", what])
	if not cond:
		_fails += 1


static func _lines(z: int) -> Array:
	return ZoneBounds.load_lines("res://content/zones/zone%03d" % z)


## Walls a straight walk from a to b (zone units of zone z) goes out through (crosses from the face's side).
static func _out_crossings(lines: Array, a: Vector2, b: Vector2) -> int:
	var n := 0
	for ln in lines:
		var l: Array = ln
		for i in range(0, l.size() - 2, 2):
			var p := Vector2(l[i], l[i + 1])
			var q := Vector2(l[i + 2], l[i + 3])
			if Geometry2D.segment_intersects_segment(a, b, p, q) != null and (b - a).dot(Vector2(-(q.y - p.y), q.x - p.x)) < 0.0:
				n += 1
	return n


## The same walk in region units, checked in each zone it passes (zones_index.json offsets, 8192-unit blocks).
func _walk_region(zones: Array, a: Vector2, b: Vector2) -> int:
	var idx: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zones_index.json"))
	var n := 0
	for z in zones:
		var o := Vector2(float(idx[str(z)].x), float(idx[str(z)].y)) * 8192.0
		n += _out_crossings(_lines(z), a - o, b - o)
	return n


func _initialize() -> void:
	# 1. data
	for z in [104, 100, 0, 200]:
		var lines := _lines(z)
		var pts := 0
		for l in lines:
			pts += (l as Array).size() / 2
		var faces := ZoneBounds.faces(lines)
		print("[bounds] zone %d: %d lines, %d points, %d walls (%d triangles)" % [z, lines.size(), pts, pts - lines.size(), faces.size() / 3])
		_ok(lines.size() > 0 and faces.size() == (pts - lines.size()) * 6, "zone %d has its walls" % z)
	_ok(_lines(104).size() == 1 and (_lines(104)[0] as Array).size() == 192, "Muspelheim: one line of 96 points (1.64 bound.csv)")
	# 2. places (zone-local units: region X / Y minus the zone's offset)
	var inside := [[104, 2115, 40346, "Muspelheim: Gokstad at the gate from Mularn (Mob)"],
			[100, 64614, 8517, "Vale of Mularn: Gokstad at the gate to Muspelheim (Mob)"],
			[100, 50152, 54743, "Vale of Mularn: a town bind point (BindPoint)"],
			[0, 36419, 48254, "Camelot Hills: a town bind point (BindPoint)"],
			[0, 3116, 28754, "Camelot Hills: Camelot city entrance (ZonePoint 6, 78 units from a wall)"],
			[200, 25300, 44743, "Lough Derg: a town bind point (BindPoint)"],
			[104, 2500, 20000, "Muspelheim: just inside the west rim"]]
	for p in inside:
		var s := ZoneBounds.side(_lines(int(p[0])), float(p[1]), float(p[2]))
		_ok(not s.is_empty() and bool(s[1]), "%s inside (%.0f units from the nearest wall)" % [p[3], float(s[0]) if s.size() else -1.0])
	var outside := [[104, 600, 20000, "Muspelheim: up the west mountains past the rim"],
			[104, 32768, 600, "Muspelheim: north edge past the rim"],
			[104, 40000, 65300, "Muspelheim: south edge past the rim"]]
	for p in outside:
		var s := ZoneBounds.side(_lines(int(p[0])), float(p[1]), float(p[2]))
		_ok(not s.is_empty() and not bool(s[1]), "%s outside (%.0f units)" % [p[3], float(s[0]) if s.size() else -1.0])
	var gok_mularn := Vector2(92 * 8192 + 64614, 82 * 8192 + 8517)
	var gok_musp := Vector2(100 * 8192 + 2115, 78 * 8192 + 40346)
	_ok(_walk_region([100, 104], gok_mularn, gok_musp) == 0 and _walk_region([100, 104], gok_musp, gok_mularn) == 0,
			"Mularn <-> Muspelheim crossing (Gokstad to Gokstad): no wall either way")
	var mularn_s := Vector2(92 * 8192 + 10240, 82 * 8192 + 63000)
	var svea_n := Vector2(88 * 8192 + 43008, 90 * 8192 + 2000)
	_ok(_walk_region([100, 101], mularn_s, svea_n) == 0 and _walk_region([100, 101], svea_n, mularn_s) == 0,
			"Vale of Mularn <-> East Svealand border: no wall either way")
	_ok(_walk_region([104], Vector2(100 * 8192 + 2500, 78 * 8192 + 20000), Vector2(100 * 8192 + 600, 78 * 8192 + 20000)) == 1,
			"Muspelheim: walking west out over the rim meets one wall")
	# 3. physics
	var musp := _zone_node(104, Vector3.ZERO)
	var upp := _zone_node(111, Vector3(5000, 0, 0))
	_body = CharacterBody3D.new()
	_body.collision_mask = ZoneLoader.LAYER_WALL | ZoneLoader.LAYER_BOUND
	_body.motion_mode = CharacterBody3D.MOTION_MODE_FLOATING
	var cap := CapsuleShape3D.new()
	cap.radius = Player.STEP_LEAD
	cap.height = 1.8 - Player.COLLIDER_LIFT
	var bcs := CollisionShape3D.new()
	bcs.shape = cap
	bcs.position = Vector3(0, Player.COLLIDER_LIFT + cap.height * 0.5, 0)
	_body.add_child(bcs)
	root.add_child(_body)
	var rim := 1536.0 * U                                          # Muspelheim's west wall at y 14848..22784: x 1536
	_cases = [["Muspelheim: walking out west stops at the rim", musp, Vector3(2500 * U, 100, 20000 * U), Vector3(-1, 0, 0),
				func(e: Vector3): return absf(e.x - (rim + Player.STEP_LEAD)) < 0.1],
			["Muspelheim: walking in from past the rim goes through", musp, Vector3(600 * U, 100, 20000 * U), Vector3(1, 0, 0),
				func(e: Vector3): return e.x > 2500 * U],
			["Uppland: walking south out of the zone stops at its rim (y 63936)", upp, Vector3(20000 * U, 100, 63000 * U), Vector3(0, 0, 1),
				func(e: Vector3): return absf(e.z - (63936 * U - Player.STEP_LEAD)) < 0.1],
			["Uppland: frontier wall (drawn both ways) stops walking south", upp, Vector3(20000 * U, 100, 64300 * U), Vector3(0, 0, 1),
				func(e: Vector3): return absf(e.z - (64640 * U - Player.STEP_LEAD)) < 0.1],
			["Uppland: frontier wall stops walking north", upp, Vector3(20000 * U, 100, 65300 * U), Vector3(0, 0, -1),
				func(e: Vector3): return absf(e.z - (64640 * U + Player.STEP_LEAD)) < 0.1]]
	_start()


func _zone_node(z: int, at: Vector3) -> Node3D:
	var n := Node3D.new()
	n.position = at
	root.add_child(n)
	ZoneBounds.build(n, "res://content/zones/zone%03d" % z, ZoneLoader.LAYER_BOUND)
	return n


func _start() -> void:
	var c: Array = _cases[_case]
	_body.position = (c[1] as Node3D).position + (c[2] as Vector3)   # (root children: position = global)
	_frames = 0


func _physics_process(_delta: float) -> bool:
	var c: Array = _cases[_case]
	_body.velocity = (c[3] as Vector3) * 10.0
	_body.move_and_slide()
	_frames += 1
	if _frames < 360:
		return false
	var end := _body.global_position - (c[1] as Node3D).global_position
	_ok((c[4] as Callable).call(end), "%s (ended at x %.2f z %.2f m)" % [c[0], end.x, end.z])
	_case += 1
	if _case >= _cases.size():
		print("[bounds] %s" % ("PASS" if _fails == 0 else "FAIL (%d)" % _fails))
		quit(0 if _fails == 0 else 1)
		return true
	_start()
	return false
