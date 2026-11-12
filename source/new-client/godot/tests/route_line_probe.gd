extends SceneTree
## Headless probe (owner 2026-11-11: quest patrol routes on the map): the dash cutter and the route id layout.
##   Godot --headless --path godot -s res://tests/route_line_probe.gd

func _initialize() -> void:
	await process_frame                          # (the autoloads the map script names are up after a frame)
	var MapWindowScript = load("res://game/ui/map_window.gd")
	var fails := 0
	var line := PackedVector2Array([Vector2(0, 0), Vector2(20, 0), Vector2(20, 12)])   # 32 px with one corner
	var parts: Array = MapWindowScript.MapView.route_dashes(line, 5.0, 3.0)
	var drawn := 0.0
	for d in parts:
		var pts: PackedVector2Array = d
		for i in range(1, pts.size()):
			drawn += pts[i - 1].distance_to(pts[i])
	# 32 px of 5 on / 3 off: four full dashes
	print("[route_probe] dashes=", parts.size(), " drawn=", snappedf(drawn, 0.01))
	if parts.size() != 4 or absf(drawn - 20.0) > 0.01:
		fails += 1
	var corner: PackedVector2Array = parts[2]                                          # 16..21 px runs round the corner
	if corner.size() != 3 or corner[1] != Vector2(20, 0):
		print("[route_probe] FAIL: the dash at the corner does not bend there: ", corner)
		fails += 1
	if not MapWindowScript.MapView.route_dashes(PackedVector2Array([Vector2.ZERO]), 5.0, 3.0).is_empty():
		fails += 1
	var ServerScript = load("res://game/net/server.gd")
	if ServerScript.ROUTE_MARKERS + 2048 * ServerScript.ROUTE_POINTS > ServerScript.GIVER_MARKERS:
		print("[route_probe] FAIL: route ids reach the quest giver markers")
		fails += 1
	print("[route_probe] ", "ok" if fails == 0 else "FAILED %d" % fails)
	quit(fails)
