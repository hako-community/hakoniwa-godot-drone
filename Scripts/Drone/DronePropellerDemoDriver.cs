using System;
using System.Text;
using Godot;

namespace hakoniwa.drone
{
	/// <summary>
	/// DronePudu を介さずに DronePropeller を回すデモ/検証用ドライバ。
	///
	/// 目的は 2 つ:
	///   1. 可視化の目視確認（PDU 統合 = M2 が入る前でも 8 発機の見た目を確認できる）
	///   2. ch 番号 → ロータノード → 回転方向 の対応づけの自動検証（selfTest）
	///
	/// selfTest は 1 ch ずつ指令を入れ、各ロータの実際の回転量を測って
	///   - 指令した ch のロータだけが回ったか
	///   - その符号が spinDirections と一致するか
	/// を判定する。--headless でも動く（描画不要）。
	/// </summary>
	public partial class DronePropellerDemoDriver : Node3D
	{
		[Export]
		public NodePath propellerPath;
		/// <summary>全 ch 同時に入れる指令値（0..1）。</summary>
		[Export]
		public float control = 0.6f;
		/// <summary>true なら 1 本ずつ順に回す（対応づけの目視確認用）。</summary>
		[Export]
		public bool sequential = false;
		/// <summary>sequential/selfTest の 1 ch あたりの秒数。</summary>
		[Export]
		public float cycleSeconds = 1.0f;
		/// <summary>ch → ロータ → 回転方向の対応づけを自動検証して終了する。</summary>
		[Export]
		public bool selfTest = false;
		/// <summary>0 より大きければその秒数で終了する。</summary>
		[Export]
		public float quitAfterSeconds = 0.0f;
		/// <summary>0 より大きければその秒数の時点で PNG を保存する。</summary>
		[Export]
		public float screenshotAtSeconds = 0.0f;
		[Export]
		public string screenshotPath = "user://propeller_viz.png";
		[Export]
		public Label statusLabel;
		/// <summary>真上から見るカメラ。`--top-view` で切り替える（ロータ配置の確認用）。</summary>
		[Export]
		public NodePath topCameraPath;
		/// <summary>カメラを回す中心（機体）。`--orbit` で使う。</summary>
		[Export]
		public NodePath orbitTargetPath;
		/// <summary>カメラの周回速度 [deg/s]。0 = 回さない。動画用。</summary>
		[Export]
		public float orbitDegPerSec = 0.0f;
		/// <summary>周回時に見る高さ（機体の原点からの上向き）[m]。</summary>
		[Export]
		public float orbitLookHeight = 0.7f;
		/// <summary>
		/// 見た目の回転速度の倍率。**表示だけ**の係数で、指令値も物理も変えない。
		/// 実回転数（約 1900 rpm）は 30/60 fps では折り返して止まって見えるので、
		/// 動画では 0.1 前後に落として回っていることが分かるようにする。
		/// </summary>
		[Export]
		public float visualSpinScale = 1.0f;

		private DronePropeller propeller;
		private float[] controls;
		private double elapsed = 0.0;
		private int activeCh = -1;

		// selfTest 用
		private float[] prevAngle;      // 前フレームの各ロータの Rotation.Y
		private float[] accumAngle;     // 現 ch の間に積算した回転量（rad, 符号つき）
		private int checkedCh = 0;
		private int okCount = 0;
		private bool finished = false;

		/// <summary>
		/// `godot ... -- --selftest` のように、`--` の後ろの引数で挙動を上書きできる。
		/// シーンを編集せずに自動検証を回すため。
		/// </summary>
		private void ApplyCmdlineArgs()
		{
			foreach (var arg in OS.GetCmdlineUserArgs())
			{
				if (arg == "--selftest")
				{
					selfTest = true;
				}
				else if (arg == "--sequential")
				{
					sequential = true;
				}
				else if (arg.StartsWith("--screenshot="))
				{
					screenshotPath = arg.Substring("--screenshot=".Length);
					if (screenshotAtSeconds <= 0.0f) screenshotAtSeconds = 2.0f;
				}
				else if (arg.StartsWith("--screenshot-at="))
				{
					screenshotAtSeconds = float.Parse(arg.Substring("--screenshot-at=".Length));
				}
				else if (arg.StartsWith("--quit-after="))
				{
					quitAfterSeconds = float.Parse(arg.Substring("--quit-after=".Length));
				}
				else if (arg == "--top-view")
				{
					var cam = GetNodeOrNull<Camera3D>(topCameraPath);
					if (cam != null) cam.MakeCurrent();
				}
				else if (arg.StartsWith("--spin-scale="))
				{
					visualSpinScale = float.Parse(arg.Substring("--spin-scale=".Length));
				}
				else if (arg.StartsWith("--orbit="))
				{
					orbitDegPerSec = float.Parse(arg.Substring("--orbit=".Length));
				}
				else if (arg == "--orbit")
				{
					orbitDegPerSec = 30.0f;
				}
				else if (arg.StartsWith("--control="))
				{
					control = float.Parse(arg.Substring("--control=".Length));
				}
			}
		}

		public override void _Ready()
		{
			ApplyCmdlineArgs();
			propeller = GetNodeOrNull<DronePropeller>(propellerPath);
			if (propeller == null)
			{
				GD.PushError($"DronePropellerDemoDriver: DronePropeller not found: {propellerPath}");
				return;
			}
			if (!Mathf.IsEqualApprox(visualSpinScale, 1.0f))
			{
				propeller.maxRotationSpeed *= visualSpinScale;
				GD.Print($"DronePropellerDemoDriver: 見た目の回転速度を x{visualSpinScale} にした（表示のみ）");
			}
			int n = propeller.RotorCount;
			controls = new float[n];
			prevAngle = new float[n];
			accumAngle = new float[n];
			for (int i = 0; i < n; i++)
			{
				prevAngle[i] = RotorAngle(i);
			}
			GD.Print($"DronePropellerDemoDriver: rotors={n} selfTest={selfTest} sequential={sequential}");
			for (int i = 0; i < n; i++)
			{
				var rotor = propeller.GetRotor(i);
				GD.Print($"  ch{i} -> {(rotor != null ? rotor.Name.ToString() : "(null)")} spin={(propeller.GetSpinSign(i) > 0 ? "CCW(+)" : "CW(-)")}");
			}
			if (selfTest)
			{
				sequential = true;
			}
		}

		private float RotorAngle(int i)
		{
			var rotor = propeller.GetRotor(i);
			return rotor != null ? rotor.Rotation.Y : 0.0f;
		}

		public override void _Process(double delta)
		{
			if (propeller == null || finished) return;
			int n = propeller.RotorCount;
			if (n == 0) return;

			elapsed += delta;

			int ch = -1;
			if (sequential)
			{
				ch = (int)(elapsed / Math.Max(0.001f, cycleSeconds));
				// selfTest は最後の ch まで来たら終わる。目視用は巡回させる
				if (!selfTest) ch %= n;
			}

			if (selfTest && ch != activeCh && activeCh >= 0)
			{
				// ch が切り替わる直前に、直前の ch の結果を判定する
				JudgeChannel(activeCh, n);
				checkedCh++;
				Array.Clear(accumAngle, 0, accumAngle.Length);
				if (checkedCh >= n)
				{
					Finish(n);
					return;
				}
			}
			activeCh = ch;

			for (int i = 0; i < n; i++)
			{
				controls[i] = (!sequential || i == ch) ? control : 0.0f;
			}
			propeller.Rotate(controls);

			// 実際の回転量を積算（±π で折り返すので差分をたたむ）
			for (int i = 0; i < n; i++)
			{
				float cur = RotorAngle(i);
				float d = cur - prevAngle[i];
				while (d > Mathf.Pi) d -= Mathf.Tau;
				while (d < -Mathf.Pi) d += Mathf.Tau;
				accumAngle[i] += d;
				prevAngle[i] = cur;
			}

			if (statusLabel != null)
			{
				string scale = Mathf.IsEqualApprox(visualSpinScale, 1.0f)
					? "" : $"   [propeller spin x{visualSpinScale} for display]";
				statusLabel.Text = sequential
					? $"t={elapsed:F1}s   ch{ch} = {control:F2}, others 0   ->  {propeller.GetRotor(ch)?.Name}{scale}"
					: $"t={elapsed:F1}s   all ch = {control:F2}{scale}";
			}

			Orbit(delta);

			if (screenshotAtSeconds > 0.0f && elapsed >= screenshotAtSeconds)
			{
				SaveScreenshot();
				screenshotAtSeconds = 0.0f;
			}
			if (quitAfterSeconds > 0.0f && elapsed >= quitAfterSeconds)
			{
				finished = true;
				GetTree().Quit();
			}
		}

		/// カメラを機体のまわりに回す（動画用）。現在有効なカメラを回す。
		private void Orbit(double delta)
		{
			if (Mathf.IsZeroApprox(orbitDegPerSec)) return;
			var cam = GetViewport()?.GetCamera3D();
			if (cam == null) return;
			var target = GetNodeOrNull<Node3D>(orbitTargetPath);
			Vector3 center = target != null ? target.GlobalPosition : Vector3.Zero;
			Vector3 rel = cam.GlobalPosition - center;
			rel = rel.Rotated(Vector3.Up, Mathf.DegToRad(orbitDegPerSec * (float)delta));
			cam.GlobalPosition = center + rel;
			cam.LookAt(center + new Vector3(0.0f, orbitLookHeight, 0.0f), Vector3.Up);
		}

		private void JudgeChannel(int ch, int n)
		{
			int expected = propeller.GetSpinSign(ch);
			float moved = accumAngle[ch];
			float othersMax = 0.0f;
			for (int i = 0; i < n; i++)
			{
				if (i == ch) continue;
				othersMax = Math.Max(othersMax, Math.Abs(accumAngle[i]));
			}
			bool ok = Math.Abs(moved) > 0.1f
				&& Math.Sign(moved) == expected
				&& othersMax < 1e-4f;
			if (ok) okCount++;
			var rotor = propeller.GetRotor(ch);
			GD.Print($"  ch{ch}: {(rotor != null ? rotor.Name.ToString() : "(null)"),-32} " +
				$"delta={Mathf.RadToDeg(moved),10:F1}deg expect={(expected > 0 ? "+CCW" : "-CW ")} " +
				$"others_max={Mathf.RadToDeg(othersMax):F4}deg  {(ok ? "OK" : "NG")}");
		}

		private void Finish(int n)
		{
			finished = true;
			GD.Print($"SELFTEST: {okCount}/{n} OK");
			GetTree().Quit(okCount == n ? 0 : 1);
		}

		private void SaveScreenshot()
		{
			var vp = GetViewport();
			if (vp == null) return;
			var img = vp.GetTexture().GetImage();
			if (img == null) return;
			var err = img.SavePng(screenshotPath);
			GD.Print($"screenshot: {screenshotPath} ({err})");
		}
	}
}
