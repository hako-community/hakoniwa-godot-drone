class_name CatController
extends CharacterBody3D

## P5-1: 猫のキャラクターコントローラ本体。
## 入力デバイスを一切知らない「意図 API」だけを公開する:
##   move_dir / run_held / back_held を毎フレーム設定し、
##   try_jump() / try_attack(n) / toggle_sit() / toggle_loaf() を呼ぶ。
## プレイヤー入力(play_test.gd)からも AI からも同じ API で駆動できる。
##
## アニメ10本の用途: Idle1(待機) Idle2(長尺見回し・放置時) Walk/Run/Walkback(移動)
## Jump(ワンショット+物理アーク+着地めり込み相殺) Atk1/Atk2(ワンショット)
## Sit/Loaf(トグルポーズ)。ループ設定は glb.import 側で永続化済み。

const ANIM := {
	idle = "Armature|Idle1", idle2 = "Armature|Idle2",
	walk = "Armature|Walk", run = "Armature|Run", back = "Armature|Walkback",
	jump = "Armature|Jump", atk1 = "Armature|Atk1", atk2 = "Armature|Atk2",
	sit = "Armature|Sit", loaf = "Armature|Loaf",
}

@export_group("Locomotion")
@export var walk_speed := 0.45   # m/s（体長0.35mの実猫スケール）
@export var run_speed := 1.35
@export var back_speed := 0.25
@export var turn_speed := 8.0    # 旋回の追従率 (rad/s 相当の lerp 係数)
@export var blend := 0.15        # アニメのクロスフェード秒
## すり足対策: 歩き/走りの足運び速度倍率。足が地面に対して滑る（すり足）なら上げる（1.3〜1.6目安）。
@export var walk_anim_speed := 1.4
@export var run_anim_speed := 1.4

# --- Jump（P5-3 実測値。jump_test.gd と同じ）---
const JUMP_LAUNCH_T := 0.58   # アニメのこの時刻で蹴り出し（それまではタメ）
const JUMP_VELOCITY := 2.21   # 高さ0.25m相当 (sqrt(2*g*h))
# 着地めり込みの相殺テーブル (アニメ時刻s, メッシュ持ち上げm)。Blender実測 2026-07-16
const COMP := [
	Vector2(1.000, 0.000), Vector2(1.033, 0.013), Vector2(1.067, 0.042),
	Vector2(1.100, 0.065), Vector2(1.133, 0.089), Vector2(1.167, 0.118),
	Vector2(1.200, 0.134), Vector2(1.233, 0.138), Vector2(1.267, 0.130),
	Vector2(1.300, 0.109), Vector2(1.333, 0.070), Vector2(1.367, 0.033),
	Vector2(1.400, 0.016), Vector2(1.433, 0.009), Vector2(1.467, 0.009),
	Vector2(1.500, 0.005), Vector2(1.533, 0.000),
]

const IDLE2_AFTER := 10.0  # 放置がこの秒数を超えたら Idle2 を混ぜる

enum State { IDLE, MOVE, JUMP, ATTACK, SIT, LOAF }

# --- 意図 API（毎フレーム外から設定/呼び出し）---
var move_dir := Vector3.ZERO   # ワールド水平の移動したい向き（ゼロ=停止）
var run_held := false
var back_held := false

var state: State = State.IDLE
var _player: AnimationPlayer
var _mesh: Node3D
var _gravity: float = ProjectSettings.get_setting("physics/3d/default_gravity")
var _jump_launched := false
var _idle_time := 0.0


func try_jump() -> void:
	if state in [State.IDLE, State.MOVE, State.SIT, State.LOAF]:
		_enter_jump()


func try_attack(n: int) -> void:
	if state in [State.IDLE, State.MOVE]:
		state = State.ATTACK
		_play(ANIM.atk1 if n == 1 else ANIM.atk2)


func toggle_sit() -> void:
	if state == State.SIT:
		_to_ground_state()
	elif state in [State.IDLE, State.MOVE, State.LOAF]:
		state = State.SIT
		_play(ANIM.sit)


func toggle_loaf() -> void:
	if state == State.LOAF:
		_to_ground_state()
	elif state in [State.IDLE, State.MOVE, State.SIT]:
		state = State.LOAF
		_play(ANIM.loaf)


func _ready() -> void:
	_mesh = $MeshRoot
	_player = find_child("AnimationPlayer", true, false) as AnimationPlayer
	_player.animation_finished.connect(_on_anim_finished)
	_play(ANIM.idle)


func _play(anim: String) -> void:
	if _player.current_animation != anim:
		_player.play(anim, blend)


func _enter_jump() -> void:
	state = State.JUMP
	_jump_launched = false
	_play(ANIM.jump)
	_player.seek(0.0, true)


func _to_ground_state() -> void:
	_mesh.position.y = 0.0
	if move_dir.length_squared() > 0.0001 or back_held:
		state = State.MOVE
	else:
		state = State.IDLE
		_play(ANIM.idle)
		_idle_time = 0.0


func _on_anim_finished(anim: StringName) -> void:
	if anim == ANIM.jump or anim == ANIM.atk1 or anim == ANIM.atk2:
		_to_ground_state()
	elif anim == ANIM.idle2:
		_play(ANIM.idle)
		_idle_time = 0.0


func _comp_offset(t: float) -> float:
	if t < COMP[0].x:
		return 0.0
	for i in range(COMP.size() - 1):
		if t < COMP[i + 1].x:
			var a: Vector2 = COMP[i]
			var b: Vector2 = COMP[i + 1]
			return lerpf(a.y, b.y, (t - a.x) / (b.x - a.x))
	return 0.0


func _face_toward(dir: Vector3, delta: float) -> void:
	# モデルは MeshRoot で 180°回してあるので、-Z が正面
	var target := atan2(-dir.x, -dir.z)
	rotation.y = lerp_angle(rotation.y, target, minf(1.0, turn_speed * delta))


func _physics_process(delta: float) -> void:
	var moving := move_dir.length_squared() > 0.0001
	_player.speed_scale = 1.0   # 既定は等速（Jump 等の時間依存処理を壊さないため）

	match state:
		State.IDLE, State.MOVE:
			if back_held:
				state = State.MOVE
				_play(ANIM.back)
				# 向きは変えず、正面(-Z)の逆へ下がる
				var back_dir := transform.basis.z  # -Z が正面なので +Z が後ろ
				velocity.x = back_dir.x * back_speed
				velocity.z = back_dir.z * back_speed
			elif moving:
				state = State.MOVE
				var speed := run_speed if run_held else walk_speed
				_play(ANIM.run if run_held else ANIM.walk)
				_player.speed_scale = run_anim_speed if run_held else walk_anim_speed
				_face_toward(move_dir, delta)
				velocity.x = move_dir.x * speed
				velocity.z = move_dir.z * speed
			else:
				velocity.x = 0.0
				velocity.z = 0.0
				if state == State.MOVE:
					state = State.IDLE
					_play(ANIM.idle)
					_idle_time = 0.0
				# 放置時の Idle2
				_idle_time += delta
				if _idle_time > IDLE2_AFTER and _player.current_animation == ANIM.idle:
					_player.play(ANIM.idle2, 0.4)
					_idle_time = -_player.get_animation(ANIM.idle2).length
		State.JUMP:
			var t := _player.current_animation_position
			if not _jump_launched and t >= JUMP_LAUNCH_T:
				_jump_launched = true
				velocity.y = JUMP_VELOCITY
			_mesh.position.y = _comp_offset(t)
			# 空中は水平速度を保持（タメの間は停止）
			if not _jump_launched:
				velocity.x = 0.0
				velocity.z = 0.0
		State.ATTACK, State.SIT, State.LOAF:
			velocity.x = 0.0
			velocity.z = 0.0
			# 座り・香箱は移動入力で解除
			if state in [State.SIT, State.LOAF] and (moving or back_held):
				_to_ground_state()

	if not is_on_floor():
		velocity.y -= _gravity * delta
	elif state != State.JUMP:
		velocity.y = 0.0
	move_and_slide()
