extends Node3D

## P5-1/2: CatController の遊び場。プレイヤー入力層（コントローラ本体は入力を知らない）。
## 実行: godot --path . res://Cat/scenes/PlayTest.tscn
## キー: WASD=移動(カメラ基準) Shift=走る S=後退 Space=ジャンプ
##       J=パンチ1(振り下ろし) K=パンチ2(フック) C=お座り V=香箱
##       Q/E=カメラ回転 Esc=終了
## 検証用: --selftest=<dir> で意図APIを自動駆動して連番PNG保存→終了
##（プレイヤーと同じ API を時系列で呼ぶ = AI 駆動できることの証明を兼ねる）

var _cat: CatController
var _cam_rig: Node3D
var _cam: Camera3D
var _label: Label
var _cam_yaw := 0.0


func _ready() -> void:
	# 床（広い盤 + 乗れる箱）
	var floor_body := StaticBody3D.new()
	var floor_col := CollisionShape3D.new()
	var floor_shape := BoxShape3D.new()
	floor_shape.size = Vector3(8.0, 0.2, 8.0)
	floor_col.shape = floor_shape
	floor_col.position.y = -0.1
	floor_body.add_child(floor_col)
	var floor_mesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(8.0, 8.0)
	var fmat := StandardMaterial3D.new()
	fmat.albedo_color = Color(0.48, 0.52, 0.5)
	fmat.roughness = 1.0
	plane.material = fmat
	floor_mesh.mesh = plane
	floor_body.add_child(floor_mesh)
	add_child(floor_body)

	var box := StaticBody3D.new()
	var box_col := CollisionShape3D.new()
	var box_shape := BoxShape3D.new()
	box_shape.size = Vector3(0.5, 0.18, 0.5)
	box_col.shape = box_shape
	box.add_child(box_col)
	var box_mesh := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = box_shape.size
	var bmat := StandardMaterial3D.new()
	bmat.albedo_color = Color(0.62, 0.5, 0.38)
	bm.material = bmat
	box_mesh.mesh = bm
	box.add_child(box_mesh)
	box.position = Vector3(0.9, 0.09, -0.9)
	add_child(box)

	# ライト・環境
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-45.0, 30.0, 0.0)
	sun.light_energy = 1.4
	sun.shadow_enabled = true
	add_child(sun)
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.70, 0.72, 0.75)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.9, 0.9, 0.92)
	env.ambient_light_energy = 0.7
	var wenv := WorldEnvironment.new()
	wenv.environment = env
	add_child(wenv)

	# 猫
	_cat = (load("res://Cat/scenes/CatController.tscn") as PackedScene).instantiate()
	add_child(_cat)

	# 追従カメラ（位置追従+Q/Eで回転）
	_cam_rig = Node3D.new()
	add_child(_cam_rig)
	var arm := SpringArm3D.new()
	arm.spring_length = 1.2
	arm.position.y = 0.35
	arm.rotation_degrees.x = -12.0
	_cam_rig.add_child(arm)
	_cam = Camera3D.new()
	arm.add_child(_cam)
	_cam.current = true

	# UI
	var ui := CanvasLayer.new()
	add_child(ui)
	_label = Label.new()
	_label.position = Vector2(16, 12)
	_label.add_theme_font_size_override("font_size", 15)
	_label.add_theme_color_override("font_color", Color(0.15, 0.13, 0.11))
	_label.text = "WASD=移動 Shift=走る S=後退 Space=ジャンプ J/K=パンチ C=お座り V=香箱 Q/E=カメラ Esc=終了"
	ui.add_child(_label)

	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--selftest="):
			_selftest(arg.split("=", true, 1)[1])
			break


func _process(delta: float) -> void:
	# カメラ: 猫の位置を滑らかに追従
	_cam_rig.position = _cam_rig.position.lerp(_cat.position, minf(1.0, 6.0 * delta))
	if Input.is_key_pressed(KEY_Q):
		_cam_yaw += 1.6 * delta
	if Input.is_key_pressed(KEY_E):
		_cam_yaw -= 1.6 * delta
	_cam_rig.rotation.y = _cam_yaw

	if _selftest_running:
		return
	# --- プレイヤー入力 → 意図 API ---
	var x := 0.0
	var z := 0.0
	if Input.is_key_pressed(KEY_W):
		z -= 1.0
	if Input.is_key_pressed(KEY_A):
		x -= 1.0
	if Input.is_key_pressed(KEY_D):
		x += 1.0
	_cat.back_held = Input.is_key_pressed(KEY_S)
	var dir := Vector3.ZERO
	if x != 0.0 or z != 0.0:
		dir = (Basis(Vector3.UP, _cam_yaw) * Vector3(x, 0, z)).normalized()
	_cat.move_dir = dir
	_cat.run_held = Input.is_key_pressed(KEY_SHIFT)


func _input(event: InputEvent) -> void:
	if _selftest_running or not (event is InputEventKey):
		return
	var key := event as InputEventKey
	if not key.pressed or key.echo:
		return
	match key.keycode:
		KEY_SPACE: _cat.try_jump()
		KEY_J: _cat.try_attack(1)
		KEY_K: _cat.try_attack(2)
		KEY_C: _cat.toggle_sit()
		KEY_V: _cat.toggle_loaf()
		KEY_ESCAPE: get_tree().quit()


# ---- 自動検証: 意図 API を時系列で駆動し 5fps で撮影 ----
var _selftest_running := false

func _selftest(outdir: String) -> void:
	_selftest_running = true
	var idx := 0
	var shoot := func(tag: String, seconds: float) -> void:
		var until := Time.get_ticks_msec() + int(seconds * 1000)
		while Time.get_ticks_msec() < until:
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			var img := get_viewport().get_texture().get_image()
			img.save_png("%s/pt_%03d_%s.png" % [outdir, idx, tag])
			idx += 1
	await get_tree().create_timer(0.5).timeout
	# 歩く（前）
	_cat.move_dir = Vector3(0, 0, -1)
	await shoot.call("walk", 1.6)
	# 走る
	_cat.run_held = true
	await shoot.call("run", 1.6)
	# 走りながらジャンプ
	_cat.try_jump()
	await shoot.call("jump", 1.8)
	_cat.run_held = false
	_cat.move_dir = Vector3.ZERO
	# 後退
	_cat.back_held = true
	await shoot.call("back", 1.4)
	_cat.back_held = false
	# パンチ2種
	_cat.try_attack(1)
	await shoot.call("atk1", 1.4)
	_cat.try_attack(2)
	await shoot.call("atk2", 1.4)
	# お座り → 香箱
	_cat.toggle_sit()
	await shoot.call("sit", 1.2)
	_cat.toggle_loaf()
	await shoot.call("loaf", 1.2)
	print("SELFTEST DONE shots=", idx)
	get_tree().quit()
