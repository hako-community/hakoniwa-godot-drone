extends Node
## ★★★★ 惑星の空（2026-09-12）—— **同じシーンで 地球／火星／タイタン を切り替える。**
##
## ★★★★ **なぜスクリプトで切り替えるか**: 惑星ごとにシーンを 3 つ作ると、
##   機体の配線（PDU・プロペラ・カメラ）が 3 か所に増えて必ずずれる。
##   **変えたいのは「空・光・地面の色・霧」だけ**なので、そこだけを外から差し替える。
##
## 使い方: 環境変数 `HAKO_PLANET=earth|mars|titan`（既定 earth）。
##   ★ プラント側の `--atmosphere` と**同じ名前**を渡すこと。ここは**絵だけ**で、
##     物理（重力・大気密度・推力）は 1 ビットも変えない。
##
## ★★★ 色の根拠（見た目の当たりを付けるための実測寄りの値）:
##   * 火星 … 空は淡い橙〜バタースコッチ（塵の散乱）。地面は酸化鉄の赤褐色。
##     **影は濃い**（大気が薄いので散乱光が少ない）。太陽光は地球の約 43%。
##   * タイタン … 分厚いオレンジの霞。**地表から空は見えない**ので霧を強くかける。
##     太陽光は地球の **約 1/100**（昼でも地球の夕暮れより暗い）。地面は暗い氷と有機物。
##
## ★ Godot の色は sRGB 表示（`Color(...)`）だが、シェーダの `source_color` はリニア。
##   ここは StandardMaterial3D / 環境の側なので sRGB のつもりで置く（[[godot-drone-linux-run]]）。

@export var ground_material_path: NodePath
@export var far_ground_path: NodePath
@export var drone_visual_path: NodePath
@export var camera_path: NodePath
@export var world_environment_path: NodePath
@export var sun_path: NodePath
@export var label_path: NodePath

func _ready() -> void:
	var planet := OS.get_environment("HAKO_PLANET").to_lower()
	if planet == "":
		planet = "earth"

	var sky_top := Color(0.38, 0.55, 0.80)
	var sky_horizon := Color(0.70, 0.80, 0.92)
	var ground_base := Color(0.55, 0.56, 0.55)
	var ground_line := Color(0.78, 0.79, 0.78)
	var sun_energy := 1.1
	var sun_color := Color(1, 1, 1)
	var ambient := Color(0.85, 0.87, 0.90)
	var ambient_energy := 0.6
	var fog_on := false
	var fog_color := Color(1, 1, 1)
	var fog_density := 0.0
	var terrain_mode := 0
	var terrain_amp := 0.0
	var terrain_len := 120.0
	var high_color := Color(0.62, 0.63, 0.62)
	var title := "Earth  g=9.81 m/s^2  rho=1.225 kg/m^3"

	match planet:
		"mars":
			# ★ 塵の散乱で空が橙。地平線のほうが明るい（地球と逆ではないが色が違う）。
			sky_top = Color(0.62, 0.42, 0.30)
			sky_horizon = Color(0.86, 0.66, 0.45)
			ground_base = Color(0.55, 0.28, 0.16)
			ground_line = Color(0.72, 0.45, 0.30)
			sun_energy = 0.75          # 太陽定量は地球の 43% だが、絵として暗すぎない所に置く
			sun_color = Color(1.0, 0.90, 0.78)
			ambient = Color(0.72, 0.48, 0.34)
			ambient_energy = 0.35      # ★ 大気が薄い ＝ 散乱光が少ない ＝ **影が濃い**
			fog_on = true              # 遠景だけ薄く（塵）
			fog_color = Color(0.80, 0.58, 0.42)
			fog_density = 0.0006
			# ★ 岩石平原 ＋ 浅いクレータ。起伏 3 m・代表波長 90 m（★ 想像であって実測地形ではない）
			terrain_mode = 1
			terrain_amp = 6.0
			terrain_len = 80.0
			high_color = Color(0.68, 0.38, 0.24)
			title = "Mars  g=3.721 m/s^2  rho=0.020 kg/m^3 (1/61)"
		"titan":
			# ★★★ **地表から空は見えない**（分厚い橙の霞）。遠景を霧で潰すのが正しい絵である。
			sky_top = Color(0.55, 0.34, 0.12)
			sky_horizon = Color(0.78, 0.55, 0.22)
			ground_base = Color(0.30, 0.24, 0.18)
			ground_line = Color(0.46, 0.38, 0.26)
			sun_energy = 0.25          # ★ 太陽光は地球の約 1/100。絵としては薄暮
			sun_color = Color(1.0, 0.82, 0.55)
			ambient = Color(0.78, 0.54, 0.26)
			ambient_energy = 0.9       # 霞が全方向から照らすので**影は柔らかい**
			fog_on = true
			fog_color = Color(0.72, 0.48, 0.20)
			fog_density = 0.0022       # ★ 数百 m で溶ける（濃すぎると地形が読めない）
			# ★★★ **平行な縦列砂丘**（タイタン赤道帯の実際の地形）。
			#   実物は高さ 80〜130 m・間隔 1〜3 km。高度 15 m を飛ぶ絵では壁になるので **1/10** にした。
			terrain_mode = 2
			terrain_amp = 12.0
			terrain_len = 110.0
			high_color = Color(0.52, 0.40, 0.24)
			title = "Titan  g=1.352 m/s^2  rho=5.4 kg/m^3 (x4.4)"

	var env_node := get_node_or_null(world_environment_path) as WorldEnvironment
	if env_node != null and env_node.environment != null:
		var env: Environment = env_node.environment
		var sky_mat := env.sky.sky_material as ProceduralSkyMaterial
		if sky_mat != null:
			sky_mat.sky_top_color = sky_top
			sky_mat.sky_horizon_color = sky_horizon
			sky_mat.ground_bottom_color = ground_base
			sky_mat.ground_horizon_color = sky_horizon
		env.ambient_light_color = ambient
		env.ambient_light_energy = ambient_energy
		env.fog_enabled = fog_on
		env.fog_light_color = fog_color
		env.fog_density = fog_density

	# ★ 地形（近く）と遠景（平ら）に同じ色を配る。**terrain_mode は近くだけ立てる。**
	_paint_ground(ground_material_path, ground_base, ground_line, high_color,
			terrain_mode, terrain_amp, terrain_len)
	_paint_ground(far_ground_path, ground_base, ground_line, high_color, 0, 0.0, terrain_len)

	# ★★★★ **機体の大きさは絵の嘘になりやすい。** 地面に 10 m 方眼があるので、
	#   模型（Origin-01 ＝ 差し渡し 1.7 m）のまま小さな機体を描くと**4 倍大きく見える**。
	#   → `HAKO_MODEL_SCALE` で見た目だけ縮める（物理には 1 ビットも関係しない）。
	var ms := OS.get_environment("HAKO_MODEL_SCALE")
	if ms != "" and ms.is_valid_float():
		var k := float(ms)
		var drone := get_node_or_null(drone_visual_path) as Node3D
		if drone != null and k > 0.0:
			# ★ 機体ノードそのものは DroneAvatar が毎フレーム位置と姿勢を書くので触らない。
			#   **見た目の子（本体とプロペラ）だけ**を縮める。
			for child in drone.get_children():
				if child is Node3D and not (child is RigidBody3D):
					(child as Node3D).scale = Vector3(k, k, k)
			print("[planet] model scale = ", k)
			# ★★★ **模型を縮めたらカメラも寄せる。** そうしないと機体が点になり、
			#   「飛んでいる」以外の情報（姿勢・ロータ）が絵から消える。
			#   ★ 1 未満でも寄せすぎないように下限を置く（0.35）。
			var cam := get_node_or_null(camera_path)
			if cam != null:
				var f: float = max(k, 0.35)
				cam.set("Offset", Vector3(0.0, 2.2 * f, 9.0 * f))

	var sun := get_node_or_null(sun_path) as DirectionalLight3D
	if sun != null:
		sun.light_energy = sun_energy
		sun.light_color = sun_color

	var label := get_node_or_null(label_path) as Label
	if label != null:
		# ★★★ **絵に土俵を焼き込む。** あとで PNG を見たとき、
		#   どの惑星の絵かが分からなくなるのがいちばん困る（実験台と同じ理由）。
		label.text = title

	# ★★★ 転がっている岩（火星）／丸い礫（タイタン）。**大きさの物差しになる。**
	#   ★ Huygens が撮ったタイタンの地表は「川で丸められた氷の礫」だった。火星は角張った岩。
	if terrain_mode != 0:
		_scatter_rocks(terrain_mode, terrain_amp, terrain_len, ground_base)

	print("[planet] ", planet, " :: ", title)


## 地面のシェーダに色と地形を配る（近景・遠景で同じ関数を使う）。
func _paint_ground(path: NodePath, base: Color, line: Color, high: Color,
		mode: int, amp: float, len_m: float) -> void:
	var node := get_node_or_null(path) as MeshInstance3D
	if node == null:
		return
	var mat := node.mesh.surface_get_material(0) as ShaderMaterial
	if mat == null:
		return
	# ★ シェーダの uniform は **リニア**。sRGB で置いた色を変換して渡す。
	mat.set_shader_parameter("base_color", _lin(base))
	mat.set_shader_parameter("line_color", _lin(line))
	mat.set_shader_parameter("high_color", _lin(high))
	mat.set_shader_parameter("terrain_mode", mode)
	mat.set_shader_parameter("terrain_amp", amp)
	mat.set_shader_parameter("terrain_len", len_m)
	# ★★★★ 2026-09-13: 離陸点のまわりは平ら（物理の地面と一致させる）。起伏がある地形だけ。
	mat.set_shader_parameter("pad_radius", PAD_RADIUS if mode != 0 else 0.0)
	mat.set_shader_parameter("pad_blend", PAD_BLEND)


func _lin(c: Color) -> Vector3:
	var l := c.srgb_to_linear()
	return Vector3(l.r, l.g, l.b)


# ---------------------------------------------------------------------------
# ★★★★ 転がっている岩を撒く（MultiMesh 1 個 ＝ 描画は 1 回）。
#
#   ★★★ **高さの式はシェーダと同じものを写している**（下の `_height_at`）。
#     式が 2 か所にあるのは本来まずいが、**GPU の頂点と CPU の配置**の両方で要るため。
#     ★ ずれても**絵が少し浮くだけ**で、飛行にも判定にも一切関係しない（置き場所専用）。
#     ずらしたくなったら **シェーダ側を正**として写し直すこと。
# ---------------------------------------------------------------------------
func _scatter_rocks(mode: int, amp: float, len_m: float, tint: Color) -> void:
	var count := 700
	var area := 420.0            # この半径の中だけ置く（遠くは霧で見えない）
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	var mesh: Mesh
	if mode == 2:
		# タイタン: 丸い礫（水氷の玉石）
		var sp := SphereMesh.new()
		sp.radial_segments = 6
		sp.rings = 3
		mesh = sp
	else:
		# 火星: 角張った岩
		mesh = BoxMesh.new()
	var mat := StandardMaterial3D.new()
	mat.albedo_color = tint.darkened(0.35)
	mat.roughness = 1.0
	mat.metallic = 0.0
	mesh.surface_set_material(0, mat)
	mm.mesh = mesh
	mm.instance_count = count

	var rng := RandomNumberGenerator.new()
	rng.seed = 20260912          # ★ 種を固定する（走るたびに岩が動くと絵を比べられない）
	for i in count:
		var x := rng.randf_range(-area, area)
		var z := rng.randf_range(-area, area)
		var r := rng.randf_range(0.25, 1.6)
		if Vector2(x, z).length() < PAD_ROCK_FREE:
			continue
		if mode == 2:
			r *= 0.6             # 礫は小さめ
		var y := _height_at(Vector2(x, z), mode, amp, len_m)
		var t := Transform3D(Basis(), Vector3(x, y + r * 0.35, z))
		t = t.rotated_local(Vector3.UP, rng.randf_range(0.0, TAU))
		t = t.scaled_local(Vector3(r, r * rng.randf_range(0.5, 0.9), r))
		mm.set_instance_transform(i, t)

	var node := MultiMeshInstance3D.new()
	node.multimesh = mm
	node.name = "Rocks"
	add_child(node)


func _hash12(p: Vector2) -> float:
	var p3 := Vector3(p.x, p.y, p.x) * 0.1031
	p3 = Vector3(p3.x - floor(p3.x), p3.y - floor(p3.y), p3.z - floor(p3.z))
	var d := p3.dot(Vector3(p3.y, p3.z, p3.x) + Vector3(33.33, 33.33, 33.33))
	p3 += Vector3(d, d, d)
	var v := (p3.x + p3.y) * p3.z
	return v - floor(v)


func _vnoise(p: Vector2) -> float:
	var i := Vector2(floor(p.x), floor(p.y))
	var f := p - i
	var u := f * f * (Vector2(3, 3) - 2.0 * f)
	var a := _hash12(i)
	var b := _hash12(i + Vector2(1, 0))
	var c := _hash12(i + Vector2(0, 1))
	var d := _hash12(i + Vector2(1, 1))
	return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y)


func _fbm(p: Vector2) -> float:
	var v := 0.0
	var a := 0.5
	for i in 4:
		v += a * _vnoise(p)
		p *= 2.03
		a *= 0.5
	return v


# ★★★★ 2026-09-13: **離陸点のまわりを平らにする半径**。
#   物理（MuJoCo）の地面は平らな z=0 なので、絵の砂丘（タイタンは高さ 12 m）が原点で盛り上がると
#   **離陸前の機体が砂丘に埋まって見える**（実際にそうなった）。台本は原点で離陸し、半径 40 m の点へ飛ぶので
#   その範囲を平らな「発着場」にし、外側へなめらかにつなぐ。★ シェーダ（height_at）と同じ式。
const PAD_RADIUS := 50.0
const PAD_BLEND := 40.0
const PAD_ROCK_FREE := 8.0     # ★ この半径には岩を置かない（機体の足元に岩が刺さらないように）


func _height_at(xz: Vector2, mode: int, amp: float, len_m: float) -> float:
	var h := _raw_height_at(xz, mode, amp, len_m)
	if mode != 0:
		h *= smoothstep(PAD_RADIUS, PAD_RADIUS + PAD_BLEND, xz.length())
	return h


func _raw_height_at(xz: Vector2, mode: int, amp: float, len_m: float) -> float:
	if mode == 1:
		var h := amp * (_fbm(xz / len_m) - 0.5) * 2.0
		var cell := Vector2(floor(xz.x / (len_m * 3.0)), floor(xz.y / (len_m * 3.0)))
		var c := (cell + Vector2(_hash12(cell), _hash12(cell + Vector2(7.1, 7.1)))) * (len_m * 3.0)
		var r := len_m * (0.35 + 0.5 * _hash12(cell + Vector2(3.7, 3.7)))
		var d := (xz - c).length() / r
		h += amp * 1.6 * (exp(-d * d * 2.2) * -1.0 + exp(-(d - 1.0) * (d - 1.0) * 9.0) * 0.45)
		return h
	if mode == 2:
		var ridge: float = sin(xz.x / len_m * TAU + _fbm(xz / (len_m * 4.0)) * 3.0)
		ridge = pow(0.5 + 0.5 * ridge, 2.2)
		var wobble := _fbm(xz / (len_m * 1.7)) - 0.5
		return amp * (ridge + 0.25 * wobble)
	return 0.0
