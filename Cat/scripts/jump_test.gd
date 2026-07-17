extends Node3D

## P5-3: Jump の放物線対応テスト。
## in-place の Jump アニメに、コードで (a) 空中の放物線アーク と
## (b) 着地めり込み相殺オフセット（Blender 実測 2026-07-16）を加える。
## 実行: godot --path . res://Cat/scenes/JumpTest.tscn
## キー: Space = ジャンプ, T = 補正ON/OFF, Esc = 終了
## 検証用: --autoshot=<dir> [--nocomp] で自動ジャンプ+連番PNG保存→終了

# 着地めり込みの相殺テーブル (t秒, 持ち上げ量m)。
# Blender で Jump 全フレームの最下点を実測し、毛スカートの通常接地(-30mm)を
# 差し引いた不足分。ピークは t=1.233s の +0.138m（実測めり込み -167.5mm）。
const COMP := [
	Vector2(1.000, 0.000), Vector2(1.033, 0.013), Vector2(1.067, 0.042),
	Vector2(1.100, 0.065), Vector2(1.133, 0.089), Vector2(1.167, 0.118),
	Vector2(1.200, 0.134), Vector2(1.233, 0.138), Vector2(1.267, 0.130),
	Vector2(1.300, 0.109), Vector2(1.333, 0.070), Vector2(1.367, 0.033),
	Vector2(1.400, 0.016), Vector2(1.433, 0.009), Vector2(1.467, 0.009),
	Vector2(1.500, 0.005), Vector2(1.533, 0.000),
]
# 空中アークの放物線（タメ→蹴り出し 0.58s、接地開始 1.03s。ビート実測より）
const ARC_T0 := 0.58
const ARC_T1 := 1.03
const ARC_H := 0.25   # ジャンプの見た目の高さ (m)。ゲームでは velocity で置き換える

const JUMP_ANIM := "Armature|Jump"
const IDLE_ANIM := "Armature|Idle1"

var _player: AnimationPlayer
var _cat: Node3D
var _label: Label
var _comp_on := true
var _jumping := false


func _offset(t: float) -> float:
	var y := 0.0
	# 空中アーク（滑らかに 0→H→0）
	if t > ARC_T0 and t < ARC_T1:
		var s := (t - ARC_T0) / (ARC_T1 - ARC_T0)
		y += 4.0 * ARC_H * s * (1.0 - s)
	# 着地めり込み相殺（テーブル線形補間）
	if t >= COMP[0].x:
		for i in range(COMP.size() - 1):
			if t < COMP[i + 1].x:
				var a: Vector2 = COMP[i]
				var b: Vector2 = COMP[i + 1]
				y += lerpf(a.y, b.y, (t - a.x) / (b.x - a.x))
				break
	return y


func _ready() -> void:
	_cat = (load("res://Cat/p3_koha9face.glb") as PackedScene).instantiate()
	add_child(_cat)
	_player = _cat.find_child("AnimationPlayer", true, false) as AnimationPlayer
	_player.get_animation(IDLE_ANIM).loop_mode = Animation.LOOP_LINEAR
	_player.animation_finished.connect(_on_anim_finished)

	var floor_mesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(3.0, 3.0)
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.5, 0.52, 0.54)
	mat.roughness = 1.0
	plane.material = mat
	floor_mesh.mesh = plane
	add_child(floor_mesh)

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

	# 真横（-X）からのカメラ。めり込み・アークが床のラインで読めるように
	var cam := Camera3D.new()
	cam.position = Vector3(-0.95, 0.22, 0.0)
	add_child(cam)
	cam.look_at(Vector3(0.0, 0.16, 0.0))
	cam.current = true

	var ui := CanvasLayer.new()
	add_child(ui)
	_label = Label.new()
	_label.position = Vector2(16, 12)
	_label.add_theme_font_size_override("font_size", 18)
	_label.add_theme_color_override("font_color", Color(0.15, 0.13, 0.11))
	ui.add_child(_label)
	_update_label()

	_player.play(IDLE_ANIM)

	# 検証モード
	for arg in OS.get_cmdline_user_args():
		if arg == "--nocomp":
			_comp_on = false
			_update_label()
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--autoshot="):
			_autoshot(arg.split("=", true, 1)[1])
			break


func _update_label() -> void:
	_label.text = "Space=ジャンプ / T=補正 %s / Esc=終了" % ("ON" if _comp_on else "OFF(生アニメ)")


func _start_jump() -> void:
	if _jumping:
		return
	_jumping = true
	_player.play(JUMP_ANIM)
	_player.seek(0.0, true)


func _on_anim_finished(anim: StringName) -> void:
	if anim == JUMP_ANIM:
		_jumping = false
		_cat.position.y = 0.0
		_player.play(IDLE_ANIM)


func _process(_delta: float) -> void:
	if _jumping and _comp_on:
		_cat.position.y = _offset(_player.current_animation_position)
	elif not _jumping:
		_cat.position.y = 0.0


func _input(event: InputEvent) -> void:
	if not (event is InputEventKey):
		return
	var key := event as InputEventKey
	if not key.pressed or key.echo:
		return
	match key.keycode:
		KEY_SPACE: _start_jump()
		KEY_T:
			_comp_on = not _comp_on
			_update_label()
		KEY_ESCAPE: get_tree().quit()


func _autoshot(outdir: String) -> void:
	# 自動: 少し待って1回ジャンプし、全行程を連番PNGで保存して終了
	await get_tree().create_timer(0.4).timeout
	_start_jump()
	var idx := 0
	var frame := 0
	var suffix := "comp" if _comp_on else "nocomp"
	while _jumping:
		await RenderingServer.frame_post_draw
		if frame % 4 == 0:  # 約15fps相当で保存
			var img := get_viewport().get_texture().get_image()
			img.save_png("%s/jump_%s_%03d.png" % [outdir, suffix, idx])
			idx += 1
		frame += 1
	print("AUTOSHOT DONE shots=", idx)
	get_tree().quit()
