extends SceneTree
## Issue #50 item 11: which drags of a companion's own items the companion window takes, and the command each sends.
##   --headless --path godot -s res://tests/companion_own_move_check.gd


func _initialize() -> void:
	await process_frame
	var W = load("res://game/companions/srv_companion_window.gd")
	var bad := 0
	var own := {"kind": "companion_item", "slot": 40, "row": 2, "name": "x", "take": Callable()}
	var checks := [
		# [what, got, want]
		["own item, same row", W.is_own_item(own, 2), true],
		["own item, other companion's window", W.is_own_item(own, 3), false],
		["an old drag with no row", W.is_own_item({"kind": "companion_item", "slot": 40, "take": Callable()}, 0), false],
		["your backpack item is not its own", W.is_own_item({"kind": "item", "slot": 40}, 2), false],
		["bag -> doll slot", W.own_move_command(2, 40, 25), "move 2 40 25"],
		["bag -> figure", W.own_move_command(2, 41, W.ONTO_FIGURE), "move 2 41 wear"],
		["bag -> bag row", W.own_move_command(2, 40, 49), "move 2 40 49"],
		["worn -> bag row", W.own_move_command(2, 25, 43), "move 2 25 43"],
		["worn -> bag panel", W.own_move_command(2, 10, W.ONTO_BAG), "move 2 10 bag"],
		["hand -> hand", W.own_move_command(2, 10, 11), "move 2 10 11"],
		["same bag slot: nothing", W.own_move_command(2, 40, 40), ""],
		["same worn slot: nothing", W.own_move_command(2, 25, 25), ""],
		["bag item on the bag panel: nothing", W.own_move_command(2, 44, W.ONTO_BAG), ""],
		["worn item on the figure: nothing", W.own_move_command(2, 21, W.ONTO_FIGURE), ""],
	]
	for c in checks:
		if c[1] != c[2]:
			print("[companion_own_move] FAIL ", c[0], ": got ", c[1], " want ", c[2])
			bad += 1
	print("[companion_own_move] %d checks, %d failed" % [checks.size(), bad])
	quit(bad)
