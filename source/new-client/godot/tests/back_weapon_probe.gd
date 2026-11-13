extends Node
## Headless, offline (owner 2026-11-13: "why are large weapons sitting so low on the classic models backs for hibernia
## classic models?"): every True 1.65 Mode classic race with a two-hander sheathed on its back (Bip01 L Back, the
## weapon's BACK frame), in its idle at 1 s - where the weapon's grip (its NIF root), its top and its bottom sit against
## the figure's shoulders, pelvis, knees and feet. Heights in metres above the figure's feet; "rel" = (y - knee) /
## (shoulder - knee): 1 = shoulder height, 0 = knee height, below 0 = under the knee. Read only.
##   Godot --headless --path godot res://tests/back_weapon_probe.tscn [-- --weapons=459,907,6,314 --races=cel_m,elf_m]

const RACES := ["bri_m", "bri_f", "ava_m", "ava_f", "hig_m", "hig_f", "sar_m", "sar_f",
		"cel_m", "cel_f", "fir_m", "fir_f", "lur_m", "lur_f", "elf_m", "elf_f",
		"nor_m", "nor_f", "tro_m", "tro_f", "dwa_m", "dwa_f", "kob_m", "kob_f"]
## Celtic Two Handed Sword (h_2h_sword01, items.csv Back Offset -10), Hib 2H Elf Great Sword (-10), briton 2h sword
## (0), Norse Two Sword (0), Hib 2H Hammer (h_2h_celtichammer01: Back Offset 0 in 1.64 too - a control)
const WEAPONS := [459, 907, 6, 314, 904]


func _ready() -> void:
	Server.world_mode = "1.65"
	Fig3Figure._classic = -1
	var races: Array = RACES
	var weapons: Array = WEAPONS
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--races="):
			races = Array(a.substr(8).split(","))
		elif a.begins_with("--weapons="):
			weapons = Array(a.substr(10).split(",")).map(func(x): return int(x))
	print("[backweapon] classic mode: ", Fig3Figure.classic())
	for model_id in weapons:
		var wi := WeaponModels.info(model_id)
		print("[backweapon] weapon %d %s back_frame z %s back_offset %s" % [model_id, String(wi.get("nif", "?")),
				str((wi.get("back_frame", []) as Array).slice(9)), str(wi.get("back_offset", 0))])
		for prefix in races:
			await _measure(prefix, model_id)
	print("[backweapon] done")
	get_tree().quit()


func _bone_y(skel: Skeleton3D, names: Array) -> float:
	for n in names:
		var i := skel.find_bone(n)
		if i >= 0:
			return (skel.global_transform * skel.get_bone_global_pose(i)).origin.y
	return NAN


func _measure(prefix: String, model_id: int) -> void:
	var f := Fig3Figure.make()
	add_child(f)
	if not f.load_figure(prefix):
		print("[backweapon] %s did not load" % prefix)
		f.free()
		return
	var item_at := func(slot: int) -> Dictionary: return {"model": model_id, "color": 0} if slot == 12 else {}
	var made: Array = Player.dress(f, item_at, "twohand", false, true, {})
	if f.anim and f.anim.has_animation("idle"):
		f.anim.play("idle")
		f.anim.seek(1.0, true)
		f.anim.speed_scale = 0.0
	for i in 3:
		await get_tree().process_frame
	var skel: Skeleton3D = f.find_children("*", "Skeleton3D", true, false)[0]
	var shoulder := (_bone_y(skel, ["Bip01 L UpperArm"]) + _bone_y(skel, ["Bip01 R UpperArm"])) * 0.5
	var pelvis := _bone_y(skel, ["Bip01 Pelvis"])
	var knee := (_bone_y(skel, ["Bip01 L Calf"]) + _bone_y(skel, ["Bip01 R Calf"])) * 0.5
	var foot := (_bone_y(skel, ["Bip01 L Foot"]) + _bone_y(skel, ["Bip01 R Foot"])) * 0.5
	var back := _bone_y(skel, ["Bip01 L Back"])
	var top := -INF
	var bottom := INF
	var grip := NAN
	for at in made:
		if not (at as BoneAttachment3D).bone_name.ends_with(" Back"):
			continue
		var holder := (at as Node).get_child(0) as Node3D
		grip = (holder.get_child(0) as Node3D).global_transform.origin.y   # the weapon NIF's root: its grip
		for mi in holder.find_children("*", "MeshInstance3D", true, false):
			var t := (mi as MeshInstance3D).global_transform
			var mesh := (mi as MeshInstance3D).mesh
			for s in mesh.get_surface_count():
				for v in (mesh.surface_get_arrays(s)[Mesh.ARRAY_VERTEX] as PackedVector3Array):
					var y := (t * v).y
					top = maxf(top, y)
					bottom = minf(bottom, y)
	var rel := func(y: float) -> float: return (y - knee) / (shoulder - knee)
	if is_nan(grip):
		print("[backweapon] %d %-5s no back weapon" % [model_id, prefix])
	else:
		print("[backweapon] %d %-5s shoulder %.2f back %.2f pelvis %.2f knee %.2f foot %.2f | grip %.2f (rel %+.2f) top %.2f (rel %+.2f) bottom %.2f (rel %+.2f)" % [
				model_id, prefix, shoulder, back, pelvis, knee, foot, grip, rel.call(grip), top, rel.call(top), bottom, rel.call(bottom)])
	f.free()
