extends Camera3D
## ★★★★ 惑星デモの追従カメラ（2026-09-13）—— **機首を回しても、いつも機体の後ろから機体を見る。**
##
## ★★★★ なぜ別に作ったか: `Scripts/CameraGimbal.cs` は「位置は世界座標の Offset・向きは機首のヨー」なので、
##   **機首を 90 度回すとカメラは世界の同じ側に残ったまま横を向き、機体が画面から消える**
##   （ゲームパッドのデモで実際にそうなった）。CameraGimbal は他のシーンでも使っているので触らない。
## ★ ずらし量 `offset` は **機体のヨーで回した座標**（x = 右・y = 上・z = 後ろ。Godot の機首は −Z）。
## ★ 位置もヨーも**なめらかに追う**（機体の小さな揺れで画面が揺れないように）。
## ★ 高度が低いときは、地面に潜らないようカメラの高さを下限で止める。

@export var target_path: NodePath
@export var offset := Vector3(0.0, 2.8, 11.0)
@export var look_up := 0.6          ## 機体の中心より少し上を見る [m]
@export var follow_rate := 3.0      ## 位置の追従の速さ [1/s]
@export var yaw_rate := 1.5         ## ヨーの追従の速さ [1/s]
@export var min_height := 1.2       ## カメラの高さの下限 [m]

var _yaw := 0.0
var _init := false


func _process(delta: float) -> void:
	var t := get_node_or_null(target_path) as Node3D
	if t == null:
		return
	var yaw := t.global_rotation.y
	if not _init:
		_yaw = yaw
	else:
		_yaw = lerp_angle(_yaw, yaw, clampf(yaw_rate * delta, 0.0, 1.0))
	var want := t.global_position + Basis(Vector3.UP, _yaw) * offset
	want.y = maxf(want.y, min_height)
	if not _init:
		global_position = want
		_init = true
	else:
		global_position = global_position.lerp(want, clampf(follow_rate * delta, 0.0, 1.0))
	look_at(t.global_position + Vector3(0.0, look_up, 0.0), Vector3.UP)
