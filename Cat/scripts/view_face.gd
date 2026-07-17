extends Node3D

## p3_koha9face.glb のインタラクティブビューア（P5-0: 全10アクションの目視確認）。
## 実行: godot --path . res://Cat/scenes/ViewFace.tscn
## キー: 1-9,0 = アニメーション切替, Space = 次, R = 回転ON/OFF, Esc = 終了

const ANIMS := [
	"Armature|Idle1", "Armature|Idle2", "Armature|Walk", "Armature|Walkback",
	"Armature|Run", "Armature|Jump", "Armature|Atk1", "Armature|Atk2",
	"Armature|Loaf", "Armature|Sit",
]

var _player: AnimationPlayer
var _cat: Node3D
var _label: Label
var _index := 0
var _rotate := true


func _ready() -> void:
	var packed_scene := load("res://Cat/p3_koha9face.glb")
	_cat = packed_scene.instantiate()
	add_child(_cat)

	_player = _cat.find_child("AnimationPlayer", true, false) as AnimationPlayer
	if _player == null:
		push_error("AnimationPlayer not found")
		return

	# ビューア用途: 全アニメーションをループ再生に（ゲーム側では one-shot と分ける）
	for anim_name in _player.get_animation_list():
		_player.get_animation(anim_name).loop_mode = Animation.LOOP_LINEAR

	# 床
	var floor_mesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(3.0, 3.0)
	var floor_mat := StandardMaterial3D.new()
	floor_mat.albedo_color = Color(0.5, 0.52, 0.54)
	floor_mat.roughness = 1.0
	plane.material = floor_mat
	floor_mesh.mesh = plane
	add_child(floor_mesh)

	# ライト
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
	var world_env := WorldEnvironment.new()
	world_env.environment = env
	add_child(world_env)

	# カメラ
	var cam := Camera3D.new()
	cam.position = Vector3(0.42, 0.16, 0.30)
	add_child(cam)
	cam.look_at(Vector3(0.0, 0.06, 0.0))
	cam.current = true

	# 操作ガイド + 現在のアニメーション表示
	var ui := CanvasLayer.new()
	add_child(ui)
	_label = Label.new()
	_label.position = Vector2(16, 12)
	_label.add_theme_font_size_override("font_size", 20)
	_label.add_theme_color_override("font_color", Color(0.15, 0.13, 0.11))
	ui.add_child(_label)
	var guide := Label.new()
	guide.position = Vector2(16, 44)
	guide.add_theme_font_size_override("font_size", 14)
	guide.add_theme_color_override("font_color", Color(0.35, 0.32, 0.28))
	guide.text = "1-9,0: アニメ切替 / Space: 次 / R: 回転 / Esc: 終了\n1:Idle1 2:Idle2 3:Walk 4:Walkback 5:Run 6:Jump 7:Atk1 8:Atk2 9:Loaf 0:Sit"
	ui.add_child(guide)

	_play(0)


func _process(delta: float) -> void:
	if _rotate and _cat != null:
		_cat.rotate_y(deg_to_rad(15.0) * delta)


func _input(event: InputEvent) -> void:
	if not (event is InputEventKey):
		return
	var key := event as InputEventKey
	if not key.pressed or key.echo:
		return
	match key.keycode:
		KEY_1: _play(0)
		KEY_2: _play(1)
		KEY_3: _play(2)
		KEY_4: _play(3)
		KEY_5: _play(4)
		KEY_6: _play(5)
		KEY_7: _play(6)
		KEY_8: _play(7)
		KEY_9: _play(8)
		KEY_0: _play(9)
		KEY_SPACE: _play(_index + 1)
		KEY_R: _rotate = not _rotate
		KEY_ESCAPE: get_tree().quit()


func _play(index: int) -> void:
	_index = wrapi(index, 0, ANIMS.size())
	var anim_name: String = ANIMS[_index]
	if _player.has_animation(anim_name):
		_player.play(anim_name)
		_player.seek(0.0, true)
		_label.text = "再生中: %s (%d/%d)" % [anim_name.replace("Armature|", ""), _index + 1, ANIMS.size()]
	else:
		_label.text = "アニメーションが見つかりません: " + anim_name
