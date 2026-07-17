class_name DroneDemoPilot
extends Node

## デモ用: 箱庭コンダクタ(PDU)無しでも drone_cat_1 で猫の狩りを確認できるよう、
## 対象ドローン(DRAvatar2 等)を自動で旋回＋上下させる。
## ★実サーバ(PDU)でドローンを飛ばす時は enabled=false にすること（位置が競合するため）。

@export var drone_path: NodePath
@export var enabled := true

@export_group("Flight")
@export var center := Vector3(0.0, 0.0, 0.0)
@export var radius := 1.4
@export var ang_speed := 0.7           # 旋回速度 rad/s
@export var height_base := 0.9
@export var height_amp := 0.6          # → 高度は 0.3〜1.5m を上下（低い時に猫が跳んで届く）
@export var height_speed := 0.9

var _drone: Node3D
var _t := 0.0


func _ready() -> void:
	_drone = get_node_or_null(drone_path) as Node3D
	set_physics_process(enabled and _drone != null)
	if enabled and _drone == null:
		push_warning("DroneDemoPilot: drone_path が解決できません")


func _physics_process(delta: float) -> void:
	_t += delta
	var a := _t * ang_speed
	var pos := center + Vector3(cos(a) * radius, 0.0, sin(a) * radius)
	pos.y = height_base + height_amp * sin(_t * height_speed)
	_drone.global_position = pos
