extends Node3D

## 任意の GLB を「4方向 × 全アニメ × 複数フレーム」で撮る汎用キャプチャ。
## P1 以降の目視ゲートの素材を作る。
##
## 実行は pipeline_v2/p1_capture.py 経由。
## カメラのフレーミングは Python 側が GLB から実測して --pivot / --radius で渡す。
## Godot の MeshInstance3D.get_aabb() はスキン付きメッシュで当てにならない
## （アーマチュアの 0.0009 スケールが二重に効いて 1/1000 の箱が返る）。
##
## 出力: <out>/<prefix>_<anim>_<t>_<view>.png

const VIEWS := {
	"front": Vector3(0.0, 0.55, 1.0),
	"back": Vector3(0.0, 0.55, -1.0),
	"left": Vector3(-1.0, 0.45, 0.0),
	"right": Vector3(1.0, 0.45, 0.0),
}
const TIMES := [0.15, 0.45, 0.80]

var _model := "res://p1a_koha.glb"
var _prefix := "p1a"
var _out := "D:/source/repos/3DModelDevPJ/image2sim-framework/output_v2/reports/p1_shots"
var _pivot := Vector3(0.0, 0.09, 0.0)
var _radius := 0.5
var _extent := 0.5  # モデルの最大寸法。床のサイズに使う
var _fov := 45.0
var _res := 720  # 正方形。横長のままだと猫が小さく写ってシートで潰れる
# 頭部のクローズアップ（レスト姿勢）。マズルの長さや頭頂の柄は、
# ポーズで頭が傾いたショットから切り出すと判定できない。
var _headpivot := Vector3.ZERO
var _headradius := 0.0

var _player: AnimationPlayer
var _cams := {}


func _parse_vec3(s: String) -> Vector3:
	var p := s.split(",")
	return Vector3(float(p[0]), float(p[1]), float(p[2]))


func _parse_args() -> void:
	for arg in OS.get_cmdline_user_args():
		var kv := arg.split("=", true, 1)
		if kv.size() != 2:
			continue
		match kv[0]:
			"--model": _model = kv[1]
			"--prefix": _prefix = kv[1]
			"--out": _out = kv[1]
			"--pivot": _pivot = _parse_vec3(kv[1])
			"--radius": _radius = float(kv[1])
			"--extent": _extent = float(kv[1])
			"--fov": _fov = float(kv[1])
			"--res": _res = int(kv[1])
			"--headpivot": _headpivot = _parse_vec3(kv[1])
			"--headradius": _headradius = float(kv[1])


func _ready() -> void:
	_parse_args()
	DirAccess.make_dir_recursive_absolute(_out)
	get_window().size = Vector2i(_res, _res)

	var root: Node3D = (load(_model) as PackedScene).instantiate()
	add_child(root)
	_player = root.find_child("AnimationPlayer", true, false) as AnimationPlayer
	if _player == null:
		push_error("AnimationPlayer not found in " + _model)
		get_tree().quit(2)
		return

	var pivot := _pivot
	var radius := _radius

	# 床（接地とめり込みが見えるように）
	var floor_mesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(_extent * 3.0, _extent * 3.0)
	var fm := StandardMaterial3D.new()
	fm.albedo_color = Color(0.42, 0.44, 0.46)
	fm.roughness = 1.0
	plane.material = fm
	floor_mesh.mesh = plane
	add_child(floor_mesh)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-42.0, 25.0, 0.0)
	sun.light_energy = 1.5
	sun.shadow_enabled = true
	add_child(sun)

	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.30, 0.52, 0.32)  # 緑背景: 欠損・穴が一目で分かる
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.92, 0.92, 0.95)
	env.ambient_light_energy = 0.75
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)

	for name in VIEWS:
		var cam := Camera3D.new()
		cam.fov = _fov
		cam.position = pivot + (VIEWS[name] as Vector3).normalized() * radius
		add_child(cam)
		cam.look_at(pivot)
		cam.current = false
		_cams[name] = cam

	# 上面（背中の柄の確認用）。真上は look_at の既定 UP と平行になるので up=-Z を渡す。
	# up=-Z なので頭(+Z)が画面下・画面右が +X になり、俯瞰写真の規約と一致する。
	var topcam := Camera3D.new()
	topcam.fov = _fov
	topcam.position = pivot + Vector3(0.0, 1.0, 0.06).normalized() * radius
	add_child(topcam)
	topcam.look_at(pivot, Vector3(0, 0, -1))
	topcam.current = false
	_cams["top"] = topcam

	print("FRAMING pivot=", pivot, " radius=", radius, " fov=", _fov, " res=", _res)
	_run.call_deferred()


func _head_shots() -> void:
	"""アニメーションを再生する前（＝レスト姿勢）に頭部を撮る。"""
	if _headradius <= 0.0:
		return
	var cam := Camera3D.new()
	cam.fov = 30.0
	add_child(cam)
	var dirs := {"front": Vector3(0, 0.10, 1), "left": Vector3(-1, 0.10, 0),
		"right": Vector3(1, 0.10, 0), "top": Vector3(0, 1, 0.22)}
	for view in dirs:
		cam.position = _headpivot + (dirs[view] as Vector3).normalized() * _headradius
		cam.look_at(_headpivot, Vector3.UP if view != "top" else Vector3(0, 0, -1))
		cam.current = true
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		var path := "%s/%s_head_%s.png" % [_out, _prefix, view]
		if img.save_png(path) != OK:
			push_error("save failed: " + path)
	cam.current = false
	cam.queue_free()
	print("HEADSHOTS=", dirs.size())


func _run() -> void:
	await get_tree().process_frame
	await _head_shots()
	var anims := _player.get_animation_list()
	anims.sort()
	var shots := 0
	for anim_name in anims:
		var anim := _player.get_animation(anim_name)
		for t in TIMES:
			_player.play(anim_name)
			_player.seek(anim.length * t, true)
			_player.pause()
			for view in _cams:
				(_cams[view] as Camera3D).current = true
				await RenderingServer.frame_post_draw
				await RenderingServer.frame_post_draw
				var img := get_viewport().get_texture().get_image()
				var safe: String = anim_name.replace("Armature|", "").replace("|", "_").to_lower()
				var path := "%s/%s_%s_t%02d_%s.png" % [_out, _prefix, safe, int(t * 100), view]
				if img.save_png(path) != OK:
					push_error("save failed: " + path)
				(_cams[view] as Camera3D).current = false
				shots += 1
	print("SHOTS=", shots)
	print("DONE")
	get_tree().quit(0)
