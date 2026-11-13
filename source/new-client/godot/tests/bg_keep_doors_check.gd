extends Node
## Check (headless, no window; gamefix ABER 2026-11-12): a battleground's central keep doors against the server's door
## rows. The zone is built the way the game builds it; for every server door id given, prints how far the client's door
## leaves with that id stand from the server's door spot - for the keep as zone.json places it now, and (--was=x,z,yaw)
## for the same keep at another placement (the leaves carried along with the keep). A keep turned or moved away from
## the period placement leaves its doorways metres away from where the server opens and blocks them.
##   Godot --headless --path godot -- --skip-login --zone=253 --probe=bg_keep_doors_check
##        --doors=253000301:33849:39604,253000302:33659:39059 [--was=627.2,752.0,-90]
## (server door id : region x : region y; the battlegrounds' zone offset in their region is 8192, 8192)
var v: Node


func _keep_xf(x: float, y: float, z: float, yaw: float) -> Transform3D:
	return Transform3D(Basis(Vector3.UP, deg_to_rad(yaw)), Vector3(x, y, z))   # as ZoneLoader._build_fixtures


func _report(label: String, leaves: Dictionary, spots: Dictionary, carry: Transform3D) -> void:
	var worst := 0.0
	for id in spots:
		var at: Vector3 = spots[id]
		var best := INF
		for d in leaves.get(id, []):
			var c: Vector3 = (carry * (d as ZoneDoor).global_transform).origin
			best = minf(best, Vector2(c.x - at.x, c.z - at.z).length())
		worst = maxf(worst, best)
		print("[door] %s %d: %d client leaves, nearest %.1f m from the server's door spot" % [label, id, leaves.get(id, []).size(), best])
	print("[door] %s worst %.1f m" % [label, worst])


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(8.0).timeout
	var zone: ZoneLoader = v.zone
	var spots := {}
	for spec in String(v.args.get("doors", "")).split(",", false):
		var p := spec.split(":")
		spots[int(p[0])] = Vector3((float(p[1]) - 8192.0) * 0.025, 0.0, (float(p[2]) - 8192.0) * 0.025)
	var keep: Dictionary = {}
	for f in zone.info.fixtures:
		if int(f.get("id", -1)) == 3 and not f.has("source"):
			keep = f
	print("[keep] zone %s fixture 3: %s pos %s yaw %s" % [str(zone.info.id), zone.info.models[int(keep.m)].file, str(keep.pos), str(keep.yaw)])
	var now := _keep_xf(keep.pos[0], keep.pos[1], keep.pos[2], float(keep.yaw))
	_report("now", zone.door_by_id, spots, Transform3D.IDENTITY)
	if v.args.has("was"):
		var w := String(v.args["was"]).split(",")
		var was := _keep_xf(float(w[0]), keep.pos[1], float(w[1]), float(w[2]))
		_report("was", zone.door_by_id, spots, was * now.affine_inverse())
	get_tree().quit()
