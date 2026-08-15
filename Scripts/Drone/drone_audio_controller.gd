class_name DroneAudioController
extends Node

## ドローンの動作入力や移動速度を監視し、DroneSound (GDScript) を直接駆動・制御するクラス

@export var drone_sound: DroneSound
@export var target_node: Node3D
@export var max_speed: float = 10.0

# 座標差分からの速度計算用
var _prev_pos: Vector3 = Vector3.ZERO
var _initialized: bool = false

func _ready() -> void:
	if drone_sound == null:
		drone_sound = get_node_or_null("DroneSound") as DroneSound
		if drone_sound == null and get_parent() != null:
			drone_sound = get_parent().get_node_or_null("DroneSound") as DroneSound
			
	if target_node == null and get_parent() is Node3D:
		target_node = get_parent() as Node3D

func _process(delta: float) -> void:
	if drone_sound == null or target_node == null or delta <= 0.0:
		return
	
	var speed: float = 0.0
	if target_node is CharacterBody3D:
		speed = (target_node as CharacterBody3D).velocity.length()
	elif target_node is RigidBody3D:
		speed = (target_node as RigidBody3D).linear_velocity.length()
	else:
		var curr_pos = target_node.global_position
		if _initialized:
			speed = (curr_pos - _prev_pos).length() / delta
		_prev_pos = curr_pos
		_initialized = true
	
	var throttle = clampf(0.15 + (speed / max_speed) * 0.85, 0.0, 1.0)
	drone_sound.set_throttle(throttle)
