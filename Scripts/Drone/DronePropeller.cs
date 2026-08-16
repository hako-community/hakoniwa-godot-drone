using System;
using System.Collections.Generic;
using Godot;

namespace hakoniwa.drone
{
	/// <summary>
	/// プロペラの見た目の回転。ロータ本数 N は可変（4/6/8...）。
	///
	/// ロータの指定方法は 3 通りあり、上から順に優先される:
	///   1. propellers          … NodePath の配列。N 発機はこれを使う
	///   2. propellerNodeNames  … ノード名の配列。glb をインスタンス化したモデルの
	///                            内部ノード（例 rotor_front_inner_left_spin）を名前で引く
	///   3. propeller1..6       … 旧来の個別指定（既存シーンの後方互換）
	///
	/// 回転方向は spinDirections（+1=CCW / -1=CW、上から見て）で指定する。
	/// 省略時は +1,-1,+1,-1... の交互（クアッドの慣例）になるが、
	/// 交互でない機体（8 発機など）もあるので、機体ごとに必ず明示すること。
	///
	/// ★ 実際の回転は _Process で行い、指令値は Rotate() で受けて保持する
	///   （音や DroneSound と同じ更新周期に載せるため）。
	/// </summary>
	public partial class DronePropeller : Node3D
	{
		/* 旧来の個別指定（4/6 発シーンの後方互換。N 発機では使わない） */
		[Export]
		public Node3D propeller1;
		[Export]
		public Node3D propeller2;
		[Export]
		public Node3D propeller3;
		[Export]
		public Node3D propeller4;
		[Export]
		public Node3D propeller5;
		[Export]
		public Node3D propeller6;

		/* N 発機用の指定 */
		[Export]
		public NodePath[] propellers = new NodePath[0];
		[Export]
		public string[] propellerNodeNames = new string[0];
		/// <summary>propellerNodeNames を探す起点。未指定ならこのノード配下を探す。</summary>
		[Export]
		public NodePath propellerSearchRoot;
		/// <summary>+1 = CCW / -1 = CW（上から見て）。要素数がロータ数と一致しないときは交互で補う。</summary>
		[Export]
		public int[] spinDirections = new int[0];

		[Export]
		public bool enableAudio = true;
		[Export]
		public float maxRotationSpeed = 1f;

		private AudioStreamPlayer3D audioSource;
		private Node droneSoundNode;
		[Export]
		public string audio_path;
		[Export]
		public Camera3D target_camera;
		[Export]
		public float maxDistance = 5.0f;
		[Export]
		public float minDistance = 0.0f;

		private Node3D[] rotors;
		private int[] signs;
		/// <summary>各ロータが参照する指令値の添字。旧来指定では 0,1,2,3,0,1（従来の割り当て）。</summary>
		private int[] sources;
		private float[] current;

		/// <summary>解決できたロータ本数。呼び出し側が渡す制御値の本数を決めるのに使う。</summary>
		public int RotorCount
		{
			get
			{
				EnsureRotors();
				return rotors.Length;
			}
		}

		/// <summary>ch 番号 i に対応するロータのノード。対応づけの検証用。</summary>
		public Node3D GetRotor(int index)
		{
			EnsureRotors();
			return (index >= 0 && index < rotors.Length) ? rotors[index] : null;
		}

		/// <summary>ch 番号 i の回転方向（+1 = CCW / -1 = CW、上から見て）。</summary>
		public int GetSpinSign(int index)
		{
			EnsureRotors();
			return (index >= 0 && index < signs.Length) ? signs[index] : 0;
		}

		public override void _Ready()
		{
			EnsureRotors();

			audioSource = NodeUtil.FindNodeByInterface<AudioStreamPlayer3D>(this);
			if (audioSource == null && enableAudio)
			{
				audioSource = new AudioStreamPlayer3D();
				AddChild(audioSource);
			}

			if (enableAudio)
			{
				LoadAudio();
			}

			if (GetParent() != null)
			{
				droneSoundNode = GetParent().FindChild("DroneSound", true, false);
			}
			if (droneSoundNode == null)
			{
				droneSoundNode = FindChild("DroneSound", true, false);
			}
		}

		private void EnsureRotors()
		{
			if (rotors != null)
			{
				return;
			}
			var list = new List<Node3D>();
			var src = new List<int>();

			if (propellers != null && propellers.Length > 0)
			{
				foreach (var path in propellers)
				{
					var node = GetNodeOrNull<Node3D>(path);
					if (node == null)
					{
						GD.PushError($"DronePropeller: propellers path not found: {path}");
						continue;
					}
					src.Add(list.Count);
					list.Add(node);
				}
			}
			else if (propellerNodeNames != null && propellerNodeNames.Length > 0)
			{
				Node root = this;
				if (propellerSearchRoot != null && !propellerSearchRoot.IsEmpty)
				{
					root = GetNodeOrNull<Node>(propellerSearchRoot) ?? this;
				}
				foreach (var name in propellerNodeNames)
				{
					// owned: false … glb をインスタンス化した内部ノードは owner が
					// インスタンス側になるため、true では見つからない
					var node = root.FindChild(name, true, false) as Node3D;
					if (node == null)
					{
						GD.PushError($"DronePropeller: propeller node not found by name: {name}");
						continue;
					}
					src.Add(list.Count);
					list.Add(node);
				}
			}
			else
			{
				// ★ 旧来の割り当てをそのまま維持する:
				//   propeller1..4 は c1..c4、propeller5 は c1、propeller6 は c2 を見る。
				//   （6 発シーンで 4 発ぶんの指令を折り返して使っていた従来挙動）
				var legacy = new Node3D[] { propeller1, propeller2, propeller3, propeller4,
				                            propeller5, propeller6 };
				var legacySource = new int[] { 0, 1, 2, 3, 0, 1 };
				for (int i = 0; i < legacy.Length; i++)
				{
					if (legacy[i] == null)
					{
						continue;
					}
					src.Add(legacySource[i]);
					list.Add(legacy[i]);
				}
			}

			rotors = list.ToArray();
			sources = src.ToArray();
			signs = new int[rotors.Length];
			for (int i = 0; i < rotors.Length; i++)
			{
				if (spinDirections != null && i < spinDirections.Length && spinDirections[i] != 0)
				{
					signs[i] = spinDirections[i] > 0 ? 1 : -1;
				}
				else
				{
					signs[i] = (i % 2 == 0) ? 1 : -1;
				}
			}
			current = new float[Math.Max(rotors.Length, 4)];
			if (rotors.Length == 0)
			{
				GD.PushWarning("DronePropeller: no propeller node is assigned.");
			}
		}

		private void LoadAudio()
		{
			if (string.IsNullOrEmpty(audio_path)) return;

			// Assume audio_path is res://...
			string path = audio_path;
			if (!path.StartsWith("res://"))
			{
				path = "res://" + path;
			}

			AudioStream stream = GD.Load<AudioStream>(path);
			if (stream != null)
			{
				GD.Print("audio found: " + path);
				audioSource.Stream = stream;
				audioSource.Stop();
			}
			else
			{
				GD.PushWarning("audio not found: " + path);
			}
		}

		public override void _Process(double delta)
		{
			EnsureRotors();
			float dt = (float)delta;
			for (int i = 0; i < rotors.Length; i++)
			{
				RotatePropeller(rotors[i], signs[i] * current[sources[i]], dt);
			}

			if (enableAudio)
			{
				PlayAudio(current.Length > 0 ? current[0] : 0f);
			}
		}

		private void PlayAudio(float my_controls)
		{
			if (audioSource == null || target_camera == null) return;

			float distance = (target_camera.GlobalPosition - GlobalPosition).Length();
			// Godot's AudioStreamPlayer3D handles attenuation automatically based on unit size and max distance.

			if (audioSource.Playing == false && my_controls > 0)
			{
				audioSource.Play();
			}
			else if (audioSource.Playing == true && my_controls == 0)
			{
				audioSource.Stop();
			}
		}

		private void RotatePropeller(Node3D propeller, float dutyRate, float dt)
		{
			if (propeller == null || Mathf.Abs(dutyRate) < 0.0001f) return;
			float rotationSpeed = maxRotationSpeed * dutyRate;
			// Assuming Y-axis is rotation axis. RotateY takes radians.
			propeller.RotateY(rotationSpeed * dt);
		}

		/// <summary>
		/// N 発ぶんの制御値を与える。controls がロータ本数より短いときは
		/// 折り返して使う（4 発ぶんの値で 6 発機を回していた従来挙動と同じ）。
		/// </summary>
		public void Rotate(float[] controls)
		{
			if (controls == null || controls.Length == 0) return;
			EnsureRotors();

			for (int i = 0; i < current.Length; i++)
			{
				current[i] = controls[i < controls.Length ? i : i % controls.Length];
			}

			if (droneSoundNode != null)
			{
				droneSoundNode.Call("set_controls", current[0], current[1], current[2], current[3]);
			}
		}

		/// <summary>4 発ぶんの旧 API。既存の呼び出し元の互換のために残している。</summary>
		public void Rotate(float c1, float c2, float c3, float c4)
		{
			Rotate(new float[] { c1, c2, c3, c4 });
		}
	}
}
