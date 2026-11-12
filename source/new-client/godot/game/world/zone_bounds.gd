extends RefCounted
## The zone's boundary walls: the real 1.64 client's bound.csv (pipeline/build_bounds.py -> content/zones/zoneNNN/
## bound.json), the invisible walls that stopped you at the mountains and the zone's rim (GitHub issue #50: in
## Muspelheim you walked up out of the lava bowl - terrain has no slope limit, in the client either).
##
## As game.dll builds them (0x500c60 / 0x45e325): every segment of a line is an upright quad, as tall as anything can
## stand (here from -50 m to 650 m: the highest ground in any zone is 357 m), met only by the player's movement test.
## Its face looks to the playable side, the right of the line (x east, y south); the data draws a wall that must stop
## both ways twice, once each way (Uppland's and Mount Collory's frontier walls), so a wall stops only who walks out
## through its face. Someone already past one (a character saved outside before these walls existed) walks back in.
## Zone borders: the lines end on the zone's edge where the next zone's walls go on; the gaps between are the open
## crossings, the same on both sides.
##
## (No class_name: ZoneLoader preloads it as ZONE_BOUNDS - a new global class needs an editor rescan first.)
## Built once when the zone loads (ZoneLoader.load_zone, on the zone worker): one shape, nothing per frame. The walls
## are on their own layer (ZoneLoader.LAYER_BOUND) that only the player's capsule tests: the camera arm, line of sight,
## clicks and the ground pick never meet them, and NPCs, bots and companions (placed by the server, which keeps them
## on its navmesh - built with the same walls) never collide with anything here.

const FILE := "bound.json"
const BOTTOM_M := -50.0
const TOP_M := 650.0
const U := 0.025                                     # DAoC units to metres


## The zone's lines ([[x1, y1, x2, y2, ...]] in zone units), or [] when it has none.
static func load_lines(zone_dir: String) -> Array:
	var p := zone_dir.path_join(FILE)
	if not FileAccess.file_exists(p):
		return []
	var d = JSON.parse_string(FileAccess.get_file_as_string(p))
	return d.get("lines", []) if d is Dictionary else []


## Wall faces for the lines, in the zone's metres (x = X, z = Y): two triangles per segment, wound so that the front
## (Godot: clockwise seen from it) looks to the line's right, the playable side.
static func faces(lines: Array) -> PackedVector3Array:
	var out := PackedVector3Array()
	for ln in lines:
		var a: Array = ln
		for i in range(0, a.size() - 2, 2):
			var p := Vector2(float(a[i]), float(a[i + 1])) * U
			var q := Vector2(float(a[i + 2]), float(a[i + 3])) * U
			if p.is_equal_approx(q):
				continue
			var pl := Vector3(p.x, BOTTOM_M, p.y)
			var ph := Vector3(p.x, TOP_M, p.y)
			var ql := Vector3(q.x, BOTTOM_M, q.y)
			var qh := Vector3(q.x, TOP_M, q.y)
			out.append_array([pl, ph, ql, ql, ph, qh])     # the client's own order (0x500c60: i low, i high, i+1 low ...)
	return out


## Adds the walls to a zone being built (no-op without a bound.json), on the given physics layer
## (ZoneLoader.LAYER_BOUND). Returns the body, or null.
static func build(zone: Node3D, zone_dir: String, layer: int) -> StaticBody3D:
	var f := faces(load_lines(zone_dir))
	if f.is_empty():
		return null
	var shape := ConcavePolygonShape3D.new()
	shape.backface_collision = false                 # one-sided: only walking out through the face is stopped
	shape.set_faces(f)
	var cs := CollisionShape3D.new()
	cs.shape = shape
	var body := StaticBody3D.new()
	body.name = "BoundaryWalls"
	body.collision_layer = layer
	body.collision_mask = 0
	body.add_child(cs)
	zone.add_child(body)
	return body


## For tests and tools: [distance in zone units to the nearest wall, true when on its playable side], or [] without
## walls. The nearest segment decides (at a shared corner both normals count) - right near the walls, where it is used.
static func side(lines: Array, x: float, y: float) -> Array:
	var best_d := INF
	var n := Vector2.ZERO
	var at := Vector2.ZERO
	for ln in lines:
		var a: Array = ln
		for i in range(0, a.size() - 2, 2):
			var p := Vector2(float(a[i]), float(a[i + 1]))
			var q := Vector2(float(a[i + 2]), float(a[i + 3]))
			if p.is_equal_approx(q):
				continue
			var c := Geometry2D.get_closest_point_to_segment(Vector2(x, y), p, q)
			var d := c.distance_to(Vector2(x, y))
			var nn := Vector2(-(q.y - p.y), q.x - p.x).normalized()
			if d < best_d - 0.001:
				best_d = d
				n = nn
				at = c
			elif absf(d - best_d) <= 0.001:
				n += nn
	if best_d == INF:
		return []
	return [best_d, (Vector2(x, y) - at).dot(n) >= 0.0]
