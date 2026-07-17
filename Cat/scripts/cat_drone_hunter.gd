class_name CatDroneHunter
extends Node

## AI: 猫(CatController)にドローンを追跡させ、間合いに入ったら跳んで一撃を狙う。
## 猫の「意図API」だけを叩く（play_test.gd と同じ流儀で、猫本体は入力/AIを知らない）。
## 実ドローン(DRAvatar2 等の Node3D)でもダミーでも、対象の global_position を追う。
##
## 状態: CHASE(追う) → 間合い＆高度条件で JUMP か SWIPE を仕掛ける → COOLDOWN。
## 命中(撃墜)そのもの＝ドローン側への反映(impulse PDU 等)は次段階。ここでは
## 接触を検出して drone_hit を emit するところまで。

@export var cat_path: NodePath
@export var drone_path: NodePath          # 追跡対象（DRAvatar2 等）

@export_group("Hakoniwa 連携")
@export var wait_for_sim_start := true    # 箱庭の START(Running) まで待つ。それまではお座りで待機
@export var hako_asset_path: NodePath     # 空なら自動で "Hakoniwa"(HakoAsset) ノードを探す

@export_group("Tuning (m, s)")
@export var allow_run := true             # 追跡中に走ることを許可（false=常に歩き）
@export var start_walk_time := 1.5        # START直後、この秒数だけ走らずゆっくり歩き出す
@export var run_distance := 0.9           # 水平でこれより遠ければ走り出す
@export var run_stop_distance := 0.55     # 走行中はここまで近づくまで走り続ける（走り↔歩きのちらつき防止）
@export var stop_distance := 0.30         # ここまで詰めたら足を止めて仕掛ける
@export var jump_reach := 0.75            # ドローン高度がこれ以下なら跳んで狙う
@export var swipe_reach := 0.34           # これ以下なら地上パンチで狙う
@export var hit_distance := 0.35          # 猫の前足リーチとドローンがこの3D距離で命中
@export var head_height := 0.28           # 猫原点からの前足/頭リーチ高
@export var strike_cooldown := 1.1        # 仕掛け後の待ち

signal drone_hit(distance: float)

enum St { CHASE, COOLDOWN }

const HAKO_RUNNING := 2                    # HakoSimState.Running（Stopped=0/Runnable=1/Running=2）

var _cat: CatController
var _drone: Node3D
var _hako: Node                            # 箱庭 HakoAsset ノード（START 状態の参照用）
var _state: St = St.CHASE
var _cooldown := 0.0
var _struck := false                      # この仕掛けサイクルで命中済みか
var _sat := false                         # START 前にお座りさせたか
var _prev_running := false                # 前フレームの Running 状態（START立ち上がり検出用）
var _start_walk := 0.0                    # START直後のゆっくり歩き出し残り秒


func _ready() -> void:
	if cat_path != NodePath():
		_cat = get_node_or_null(cat_path) as CatController
	if drone_path != NodePath():
		_drone = get_node_or_null(drone_path) as Node3D
	# 保険: シーン保存で export パスが外れても動くよう自動探索する
	var scene := get_tree().current_scene
	if _cat == null and scene != null:
		_cat = _find_cat(scene)
	if _drone == null and scene != null:
		_drone = scene.find_child("DRAvatar2", true, false) as Node3D
	# 箱庭 HakoAsset ノード（START 状態の参照用）
	if hako_asset_path != NodePath():
		_hako = get_node_or_null(hako_asset_path)
	if _hako == null and scene != null:
		_hako = scene.find_child("Hakoniwa", true, false)
	print("CatDroneHunter: cat=", _cat, "  drone=", _drone, "  hako=", _hako)
	_refresh_active()


## 箱庭が Running(=START済) かどうか。wait_for_sim_start=false や箱庭が無い環境では常に true。
func _sim_running() -> bool:
	if not wait_for_sim_start:
		return true
	if _hako == null or not _hako.has_method("GetState"):
		return true
	return int(_hako.call("GetState")) == HAKO_RUNNING


func _find_cat(n: Node) -> CatController:
	if n is CatController:
		return n as CatController
	for c in n.get_children():
		var r := _find_cat(c)
		if r != null:
			return r
	return null


## コードから直接ぶら下げる用（テスト台やAI差し替え時）。
func setup(cat: CatController, drone: Node3D) -> void:
	_cat = cat
	_drone = drone
	_refresh_active()


func _refresh_active() -> void:
	var ok := _cat != null and _drone != null
	set_physics_process(ok)
	if not ok:
		push_warning("CatDroneHunter: cat/drone が未設定です")


func _physics_process(delta: float) -> void:
	# START 前: お座りで待機（移動入力を出さない）。START(Running)で解除して追跡開始。
	if not _sim_running():
		_cat.move_dir = Vector3.ZERO
		_cat.run_held = false
		if not _sat:
			_cat.toggle_sit()
			_sat = true
		_prev_running = false
		return
	_sat = false
	# START の立ち上がりで、しばらく走らず“ゆっくり歩き出す”猶予をセット
	if not _prev_running:
		_start_walk = start_walk_time
	_prev_running = true
	if _start_walk > 0.0:
		_start_walk -= delta

	var cat_pos := _cat.global_position
	var drone_pos := _drone.global_position
	var flat := Vector3(drone_pos.x - cat_pos.x, 0.0, drone_pos.z - cat_pos.z)
	var flat_dist := flat.length()

	# 前足/頭のリーチ点とドローンの3D距離で命中判定（跳躍中は cat_pos.y が上がる）
	var strike_point := cat_pos + Vector3.UP * head_height
	var reach := strike_point.distance_to(drone_pos)
	if reach <= hit_distance and not _struck:
		_struck = true
		drone_hit.emit(reach)
		print("CAT HIT DRONE  d=%.2f" % reach)

	match _state:
		St.CHASE:
			if flat_dist > stop_distance:
				# ドローン直下へ水平に詰める。走り↔歩きはヒステリシスでちらつきを防ぐ
				_cat.move_dir = flat / maxf(flat_dist, 0.001)
				# START直後の猶予中は走らない（ゆっくり歩き出し）。以降は 近く歩き/遠く走り。
				var may_run := allow_run and _start_walk <= 0.0
				if _cat.run_held:
					_cat.run_held = may_run and flat_dist > run_stop_distance
				else:
					_cat.run_held = may_run and flat_dist > run_distance
			else:
				# 間合い。高度で仕掛けを選ぶ
				_cat.move_dir = Vector3.ZERO
				_cat.run_held = false
				if drone_pos.y <= swipe_reach:
					_struck = false
					_cat.try_attack(1)          # 地上パンチ
					_enter_cooldown()
				elif drone_pos.y <= jump_reach:
					_struck = false
					_cat.try_jump()             # 跳んで狙う
					_enter_cooldown()
				# 高すぎる時は直下で待機（追跡継続）
		St.COOLDOWN:
			_cat.move_dir = Vector3.ZERO
			_cat.run_held = false
			_cooldown -= delta
			if _cooldown <= 0.0:
				_state = St.CHASE


func _enter_cooldown() -> void:
	_state = St.COOLDOWN
	_cooldown = strike_cooldown
