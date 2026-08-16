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
		}

		private void EnsureRotors()
		{
			if (rotors != null)
			{
				return;
			}
			var list = new List<Node3D>();

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
					list.Add(node);
				}
			}
			else
			{
				foreach (var node in new Node3D[] { propeller1, propeller2, propeller3, propeller4, propeller5, propeller6 })
				{
					if (node != null)
					{
						list.Add(node);
					}
				}
			}

			rotors = list.ToArray();
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

		private void PlayAudio(float my_controls)
		{
			if (audioSource == null || target_camera == null) return;

			float distance = (target_camera.GlobalPosition - GlobalPosition).Length();
			// Godot's AudioStreamPlayer3D handles attenuation automatically based on unit size and max distance.
			// But we can manually adjust volume if needed.
			// float volume = 1.0f - Mathf.Clamp((distance - minDistance) / (maxDistance - minDistance), 0, 1);

			if (audioSource.Playing == false && my_controls > 0)
			{
				audioSource.Play();
			}
			else if (audioSource.Playing == true && my_controls == 0)
			{
				audioSource.Stop();
			}
		}

		private void RotatePropeller(Node3D propeller, float dutyRate)
		{
			if (propeller == null) return;
			float rotationSpeed = maxRotationSpeed * dutyRate;
			// Assuming Y-axis is rotation axis. RotateY takes radians.
			propeller.RotateY(rotationSpeed * (float)GetProcessDeltaTime());
		}

		/// <summary>
		/// N 発ぶんの制御値を与えて回す。controls がロータ本数より短いときは
		/// 折り返して使う（4 発ぶんの値で 6 発機を回していた従来挙動と同じ）。
		/// </summary>
		public void Rotate(float[] controls)
		{
			if (controls == null || controls.Length == 0) return;
			EnsureRotors();

			for (int i = 0; i < rotors.Length; i++)
			{
				float c = controls[i < controls.Length ? i : i % controls.Length];
				RotatePropeller(rotors[i], signs[i] * c);
			}

			if (enableAudio)
			{
				PlayAudio(controls[0]);
			}
		}

		/// <summary>4 発ぶんの旧 API。既存の呼び出し元の互換のために残している。</summary>
		public void Rotate(float c1, float c2, float c3, float c4)
		{
			Rotate(new float[] { c1, c2, c3, c4 });
		}
	}
}
