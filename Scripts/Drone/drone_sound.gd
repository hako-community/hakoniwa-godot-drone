class_name DroneSound
extends Node3D

## drone_sound2.py の音響合成アルゴリズムを再現した Godot 用ドローン効果音ジェネレータ

@export_group("Target Drone Tracking")
## 効果音のパラメータを連動させる対象ドローンノード (未指定の場合は親ノードを自動ターゲット)
@export var drone_node: Node3D
## 最大動作速度 (m/s)。この速度に達したときスロットルが 1.0 に達します
@export var max_speed: float = 10.0
## プロペラ制御信号が無い場合に物理移動速度からの自動トラッキングにフォールバックするか
@export var auto_track_speed: bool = false

@export_group("Sound Synthesis Parameters")
## プロペラの枚数
@export var blades: float = 4.0
## 基準回転数 (RPS: Revs Per Second). 80 RPS = 4800 RPM (BPF = 320 Hz)
@export var base_rps: float = 80.0
## サンプリング周波数 (Hz)
@export var sample_rate: int = 44100
## ループ再生用サンプルの長さ (秒)
@export var duration: float = 1.5

@export_group("Playback & Response Controls")
## 全体音量倍率 (0.5 = 半分のボリューム)
@export var master_volume: float = 0.5
## 低速回転時のピッチ倍率
@export var min_pitch_scale: float = 0.6
## 高速回転時のピッチ倍率
@export var max_pitch_scale: float = 1.6
## 低速回転時の音量 (dB)
@export var min_volume_db: float = -24.0
## 高速回転時の音量 (dB)
@export var max_volume_db: float = -3.0
## スロットル変化の追従速度 (線形補間)
@export var response_speed: float = 8.0

# 内部 3D オーディオプレイヤー
var audio_player: AudioStreamPlayer3D

# スロットル目標値 (0.0: 完全停止, 1.0: フルスロットル)
var target_throttle: float = 0.0
var current_throttle: float = 0.0

# 位置変化による移動速度算出用
var _prev_position: Vector3 = Vector3.ZERO
var _has_prev_pos: bool = false

func _ready() -> void:
	audio_player = AudioStreamPlayer3D.new()
	audio_player.name = "InternalAudioPlayer"
	audio_player.unit_size = 10.0
	audio_player.max_distance = 100.0
	add_child(audio_player)
	
	_rebuild_sound()
	
	if drone_node == null and get_parent() is Node3D:
		drone_node = get_parent() as Node3D

func _process(delta: float) -> void:
	if audio_player == null:
		return
	
	# プロペラ入力信号がない場合の自動移動速度トラッキング
	if auto_track_speed and drone_node != null and delta > 0.0 and target_throttle <= 0.001:
		var speed: float = 0.0
		if drone_node is CharacterBody3D:
			speed = (drone_node as CharacterBody3D).velocity.length()
		elif drone_node is RigidBody3D:
			speed = (drone_node as RigidBody3D).linear_velocity.length()
		else:
			var curr_pos = drone_node.global_position
			if _has_prev_pos:
				speed = (curr_pos - _prev_position).length() / delta
			_prev_position = curr_pos
			_has_prev_pos = true
		
		if speed > 0.05:
			target_throttle = clampf(speed / max_speed, 0.15, 1.0)
	
	# スロットルのスムーズな追従補間
	current_throttle = move_toward(current_throttle, target_throttle, response_speed * delta)
	
	# プロペラ回転が 0 の場合は消音・停止
	if current_throttle <= 0.001:
		audio_player.volume_db = -80.0
		if audio_player.playing:
			audio_player.stop()
	else:
		if not audio_player.playing:
			audio_player.play()
		
		# スロットル(回転数)に応じてピッチと音量をリアルタイム更新
		audio_player.pitch_scale = lerp(min_pitch_scale, max_pitch_scale, current_throttle)
		
		# 基本音量とマスターボリューム(音量半分調整)の合成
		var base_db = lerp(min_volume_db, max_volume_db, current_throttle)
		var master_db = linear_to_db(clampf(master_volume, 0.001, 1.0))
		audio_player.volume_db = base_db + master_db
	
	# 直接指示の次回フレームに向けた減衰 (毎フレーム set_controls が呼ばれる前提)
	target_throttle = move_toward(target_throttle, 0.0, delta * 3.0)

## 外部 (C# / GDScript) から直接スロットル値を設定 (0.0: 停止, 1.0: フルスロットル)
func set_throttle(value: float) -> void:
	target_throttle = clampf(value, 0.0, 1.0)

## プロペラ各基の回転・制御値 (0.0 ~ 1.0 等) の平均値からスロットルを設定
func set_controls(c1: float, c2: float = 0.0, c3: float = 0.0, c4: float = 0.0) -> void:
	var avg = (abs(c1) + abs(c2) + abs(c3) + abs(c4)) / 4.0
	set_throttle(avg)

## 音声波形の生成と Stream へのセット
func _rebuild_sound() -> void:
	if audio_player != null:
		audio_player.stream = generate_drone_sound_stream(duration, sample_rate, base_rps, blades)

## drone_sound2.py のアルゴリズムに準拠した AudioStreamWAV の生成
func generate_drone_sound_stream(dur: float, s_rate: int, rps: float, blade_count: float) -> AudioStreamWAV:
	var total_samples = int(s_rate * dur)
	var dt = 1.0 / s_rate
	var bpf = blade_count * rps
	
	var pcm_byte_array = PackedByteArray()
	pcm_byte_array.resize(total_samples * 2)
	
	var phase: float = 0.0
	var filter_size = 15
	var noise_buf: Array[float] = []
	noise_buf.resize(filter_size)
	noise_buf.fill(0.0)
	var noise_idx = 0
	var local_noise_sum: float = 0.0
	
	var max_amplitude: float = 0.0001
	var temp_samples: PackedFloat32Array = PackedFloat32Array()
	temp_samples.resize(total_samples)
	
	var rng = RandomNumberGenerator.new()
	rng.randomize()
	
	for i in range(total_samples):
		var t = float(i) * dt
		
		# 1. モーター/風による周期的な揺らぎ
		var rpm_wobble = 1.0 + 0.015 * sin(2.0 * PI * 2.0 * t) + 0.005 * rng.randfn(0.0, 1.0)
		var instant_bpf = bpf * rpm_wobble
		phase += 2.0 * PI * instant_bpf * dt
		
		# 2. 羽音
		var tones = (
			0.45 * sin(phase) +
			0.35 * sin(2.0 * phase) +
			0.20 * sin(3.0 * phase) +
			0.10 * sin(4.0 * phase) +
			0.05 * sin(5.0 * phase)
		)
		
		var flutter_pulse = pow(0.5 + 0.5 * sin(phase), 2.5)
		var tones_fluttered = tones * (0.4 + 0.6 * flutter_pulse)
		
		# 3. 風切りノイズ
		var raw_noise = rng.randfn(0.0, 1.0)
		
		local_noise_sum -= noise_buf[noise_idx]
		noise_buf[noise_idx] = raw_noise
		local_noise_sum += raw_noise
		noise_idx = (noise_idx + 1) % filter_size
		var smoothed_noise = local_noise_sum / float(filter_size)
		
		var noise_pulse = pow(0.5 + 0.5 * sin(phase + PI * 0.25), 3.0)
		var flutter_noise = smoothed_noise * (0.2 + 0.8 * noise_pulse)
		
		# 4. 全体合成
		var audio_sig = (tones_fluttered * 0.65) + (flutter_noise * 0.35)
		var hover_lfo = 0.88 + 0.12 * sin(2.0 * PI * 0.7 * t)
		audio_sig *= hover_lfo
		
		temp_samples[i] = audio_sig
		if abs(audio_sig) > max_amplitude:
			max_amplitude = abs(audio_sig)
			
	var norm_factor = 0.85 / max_amplitude
	for i in range(total_samples):
		var val = clampf(temp_samples[i] * norm_factor, -1.0, 1.0)
		var sample_i16 = int(val * 32767.0)
		pcm_byte_array[i * 2] = sample_i16 & 0xFF
		pcm_byte_array[i * 2 + 1] = (sample_i16 >> 8) & 0xFF
		
	var wav_stream = AudioStreamWAV.new()
	wav_stream.format = AudioStreamWAV.FORMAT_16_BITS
	wav_stream.stereo = false
	wav_stream.mix_rate = s_rate
	wav_stream.data = pcm_byte_array
	
	wav_stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
	wav_stream.loop_begin = 0
	wav_stream.loop_end = total_samples
	
	return wav_stream
