extends Node3D

const HunterScript := preload("res://Cat/scripts/cat_drone_hunter.gd")

## AI追跡の検証台（実ドローン不要）。ダミードローンが旋回しながら高度を上下し、
## CatDroneHunter が猫を駆動して追跡→跳躍→命中を試みる。
## 実行: godot --path . res://Cat/scenes/HuntTest.tscn
## 撮影: 環境変数 HUNT_OUT にディレクトリを渡すと連番PNGを保存して終了。

var _cat: CatController
var _drone: Node3D
var _cam: Camera3D
var _label: Label
var _t := 0.0
var _hits := 0


func _ready() -> void:
	# 床・ライト・環境
	var floor_body := StaticBody3D.new()
	var floor_col := CollisionShape3D.new()
	var fs := BoxShape3D.new()
	fs.size = Vector3(10, 0.2, 10)
	floor_col.shape = fs
	floor_col.position.y = -0.1
	floor_body.add_child(floor_col)
	var fmesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(10, 10)
	var fmat := StandardMaterial3D.new()
	fmat.albedo_color = Color(0.5, 0.54, 0.52)
	plane.material = fmat
	fmesh.mesh = plane
	floor_body.add_child(fmesh)
	add_child(floor_body)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 35, 0)
	sun.shadow_enabled = true
	add_child(sun)
	var we := WorldEnvironment.new()
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.55, 0.62, 0.70)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.9, 0.9, 0.92)
	env.ambient_light_energy = 0.7
	we.environment = env
	add_child(we)

	# 猫
	_cat = (load("res://Cat/scenes/CatController.tscn") as PackedScene).instantiate()
	add_child(_cat)

	# ダミードローン（本体球＋小プロペラ枠）
	_drone = Node3D.new()
	var dmesh := MeshInstance3D.new()
	var dsphere := SphereMesh.new()
	dsphere.radius = 0.09
	dsphere.height = 0.18
	var dmat := StandardMaterial3D.new()
	dmat.albedo_color = Color(0.15, 0.15, 0.18)
	dsphere.material = dmat
	dmesh.mesh = dsphere
	_drone.add_child(dmesh)
	add_child(_drone)

	# AI ブレイン
	var hunter := HunterScript.new()
	add_child(hunter)
	hunter.setup(_cat, _drone)
	hunter.drone_hit.connect(func(d): _hits += 1)

	# カメラ（猫を斜め後方から追従）
	_cam = Camera3D.new()
	add_child(_cam)
	_cam.current = true

	# UI
	var ui := CanvasLayer.new()
	add_child(ui)
	_label = Label.new()
	_label.position = Vector2(16, 12)
	_label.add_theme_font_size_override("font_size", 16)
	_label.add_theme_color_override("font_color", Color(0.1, 0.1, 0.1))
	ui.add_child(_label)

	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--capture="):
			_run_capture(arg.split("=", true, 1)[1])
			return


func _process(delta: float) -> void:
	_t += delta
	# ダミードローン: 半径1.4で旋回しつつ高度を 0.3〜1.5 で上下（低いとき猫が跳んで届く）
	var ang := _t * 0.75
	var h: float = 0.9 + 0.6 * sin(_t * 0.9)
	_drone.global_position = Vector3(cos(ang) * 1.4, h, sin(ang) * 1.4)
	# カメラ追従（ワールド固定オフセット）
	var target := _cat.global_position
	_cam.global_position = target + Vector3(0, 1.7, 2.6)
	_cam.look_at(target + Vector3.UP * 0.25, Vector3.UP)
	if _label:
		_label.text = "drone_y=%.2f  hits=%d" % [_drone.global_position.y, _hits]


# ---- 撮影: 一定時間 5fps 相当で連番保存 ----
func _run_capture(outdir: String) -> void:
	var idx := 0
	var until := Time.get_ticks_msec() + 12000
	while Time.get_ticks_msec() < until:
		for i in 6:
			await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		img.save_png("%s/hunt_%03d.png" % [outdir, idx])
		idx += 1
	print("HUNT CAPTURE DONE shots=", idx, " hits=", _hits)
	get_tree().quit()
