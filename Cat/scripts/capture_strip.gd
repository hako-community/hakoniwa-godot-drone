extends Node3D

## P4 用: 各アニメーションのフレーム連番を撮る（動きの目視ゲート素材）。
## capture_grid.gd の3コマ版では動き（歩様・振り・タメ）が判断できないため、
## 等間隔 N フレームを撮り、Python 側で GIF / コンタクトシートに組む。
##
## 実行は pipeline_v2/p4_capture_strips.py 経由。
## 出力: <out>/<prefix>_<anim>_f<idx>_<view>.png

const VIEWS := {
	"front": Vector3(0.0, 0.55, 1.0),
	"back": Vector3(0.0, 0.55, -1.0),
	"left": Vector3(-1.0, 0.45, 0.0),
	"right": Vector3(1.0, 0.45, 0.0),
}

var _model := "res://Cat/p3_koha9face.glb"
var _prefix := "p4s"
var _out := "D:/source/repos/3DModelDevPJ/image2sim-framework/output_v2/reports/p4_strips"
var _pivot := Vector3(0.0, 0.09, 0.0)
var _radius := 0.5
var _extent := 0.5
var _fov := 45.0
var _res := 480
var _views := "left,front"   # 撮る視点（カンマ区切り）
var _fps := 12.0             # サンプリングfps
var _maxframes := 60
var _only := ""              # 指定時はこの部分文字列を含むアニメだけ撮る

var _player: AnimationPlayer


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
			"--views": _views = kv[1]
			"--fps": _fps = float(kv[1])
			"--maxframes": _maxframes = int(kv[1])
			"--only": _only = kv[1]


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
	env.background_color = Color(0.30, 0.52, 0.32)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.92, 0.92, 0.95)
	env.ambient_light_energy = 0.75
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)

	print("FRAMING pivot=", _pivot, " radius=", _radius, " fov=", _fov,
		" res=", _res, " fps=", _fps)
	_run.call_deferred()


func _run() -> void:
	await get_tree().process_frame
	var cam := Camera3D.new()
	cam.fov = _fov
	add_child(cam)

	var anims := _player.get_animation_list()
	anims.sort()
	var shots := 0
	for anim_name in anims:
		if _only != "" and not anim_name.to_lower().contains(_only.to_lower()):
			continue
		var anim := _player.get_animation(anim_name)
		var nframes: int = clampi(int(round(anim.length * _fps)), 12, _maxframes)
		var safe: String = anim_name.replace("Armature|", "").replace("|", "_").to_lower()
		print("STRIP ", safe, " len=", anim.length, " frames=", nframes)
		for view in _views.split(","):
			cam.position = _pivot + (VIEWS[view] as Vector3).normalized() * _radius
			cam.look_at(_pivot)
			cam.current = true
			for i in nframes:
				_player.play(anim_name)
				_player.seek(anim.length * float(i) / float(nframes), true)
				_player.pause()
				await RenderingServer.frame_post_draw
				await RenderingServer.frame_post_draw
				var img := get_viewport().get_texture().get_image()
				var path := "%s/%s_%s_f%03d_%s.png" % [_out, _prefix, safe, i, view]
				if img.save_png(path) != OK:
					push_error("save failed: " + path)
				shots += 1
			cam.current = false
	print("SHOTS=", shots)
	print("DONE")
	get_tree().quit(0)
