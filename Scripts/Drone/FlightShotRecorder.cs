using System;
using System.Text;
using Godot;

namespace hakoniwa.drone
{
	/// <summary>
	/// ★★★★ 飛んでいるところを **数字つきで** 記録する（V8・2026-09-10）。
	///
	/// リフト＆クルーズの見どころは「**揚力ロータ 4 発が 0 のまま、プッシャだけが回る**」ところだが、
	/// 絵だけでは「本当に 0 なのか、ゆっくり回っているのか」が分からない。
	/// そこで HUD に **5 本の指令値**と経過時間を出し、同じ内容の PNG を一定間隔で保存する。
	///
	/// ★★★★ **スクリーンショットは手で撮らない。**
	///   手打ちの記録は翌日には言い伝えになる（O-10 の「初検知 20.7 m」は消えて再現しなかった）ので、
	///   撮るところまで `tools/deltaquad_godot.bash` に載せる。
	///
	/// ★ **既定 Off**: 環境変数 `HAKO_SHOT_DIR` が空なら PNG は 1 枚も書かない
	///   （HUD の表示だけは常に行う。飛行には 1 ビットも影響しない）。
	///
	///   HAKO_SHOT_DIR   … 保存先ディレクトリ（絶対パス）。空なら保存しない
	///   HAKO_SHOT_EVERY … 保存間隔（秒・既定 10）
	///   HAKO_SHOT_MAX   … 最大枚数（既定 60）
	/// </summary>
	public partial class FlightShotRecorder : Node
	{
		[Export] public NodePath propellersPath;
		[Export] public NodePath statusLabelPath;
		/// <summary>この値未満の指令は「止まっている」とみなす（HUD の STOPPED 表示だけに使う）。</summary>
		[Export] public float stoppedThreshold = 0.01f;
		/// <summary>揚力ロータの本数。これ以降の指令はプッシャとして表示する。</summary>
		[Export] public int liftRotorCount = 4;
		/// <summary>
		/// ★★★★ 揚力ロータが回っているときの表示（2026-09-12）。
		///   既定は DeltaQuad（リフト＆クルーズ）の文言のまま ＝ **既存シーンは 1 文字も変わらない**。
		///   ★ マルチロータのシーンでは「HOVER / TRANSITION」は嘘に近いので、
		///     シーン側で "ROTORS RUNNING" などに差し替える。**HUD も土俵の一部である。**
		/// </summary>
		[Export] public string runningLabel = "HOVER / TRANSITION";

		private DronePropeller propellers;
		private Label status;
		private string outDir = "";
		private double every = 10.0;
		private int maxShots = 60;
		private double elapsed = 0.0;
		private double nextShot = 0.0;
		private int shots = 0;

		public override void _Ready()
		{
			propellers = GetNodeOrNull<DronePropeller>(propellersPath);
			status = GetNodeOrNull<Label>(statusLabelPath);

			outDir = OS.GetEnvironment("HAKO_SHOT_DIR") ?? "";
			var e = OS.GetEnvironment("HAKO_SHOT_EVERY");
			if (!string.IsNullOrEmpty(e) && double.TryParse(e, out var ev) && ev > 0.0) every = ev;
			var m = OS.GetEnvironment("HAKO_SHOT_MAX");
			if (!string.IsNullOrEmpty(m) && int.TryParse(m, out var mv) && mv > 0) maxShots = mv;

			if (outDir.Length > 0)
			{
				DirAccess.MakeDirRecursiveAbsolute(outDir);
				GD.Print($"[shot] dir={outDir} every={every}s max={maxShots}");
			}
			nextShot = every;
		}

		public override void _Process(double delta)
		{
			elapsed += delta;
			string line = BuildLine();
			if (status != null) status.Text = line;

			if (outDir.Length == 0 || shots >= maxShots || elapsed < nextShot) return;
			nextShot += every;
			CallDeferred(nameof(Grab));
		}

		private string BuildLine()
		{
			var sb = new StringBuilder();
			sb.Append($"t={elapsed,6:F1}s  ");
			if (propellers == null) return sb.Append("(no propellers node)").ToString();

			float[] c = propellers.GetCurrentControls();
			bool liftStopped = true;
			for (int i = 0; i < liftRotorCount && i < c.Length; i++)
			{
				if (Mathf.Abs(c[i]) >= stoppedThreshold) liftStopped = false;
			}
			sb.Append("lift[");
			for (int i = 0; i < liftRotorCount && i < c.Length; i++)
			{
				sb.Append(i > 0 ? " " : "").Append($"{c[i]:F2}");
			}
			// ★ 揚力ロータしか無い機体（ふつうのマルチロータ）では pusher[] を出さない。
			if (c.Length > liftRotorCount)
			{
				sb.Append("]  pusher[");
				for (int i = liftRotorCount; i < c.Length; i++)
				{
					sb.Append(i > liftRotorCount ? " " : "").Append($"{c[i]:F2}");
				}
			}
			sb.Append("]  ");

			// ★ 「巡航」かどうかは FC ではなくロータの指令から読む（表示だけの判定）。
			// ★★★★ **全部 0 を「巡航」と呼ばないこと。** プラントに繋がっていないだけの状態が
			//   「揚力ロータ停止 ＝ 巡航」に見えてしまう（地面に載ったままの run を「巡航」と
			//   数えていたのと同じ形）。プッシャが回っていて初めて巡航と表示する。
			bool pusherRunning = false;
			for (int i = liftRotorCount; i < c.Length; i++)
			{
				if (Mathf.Abs(c[i]) >= stoppedThreshold) pusherRunning = true;
			}
			if (!liftStopped) sb.Append(runningLabel);
			else if (pusherRunning) sb.Append("CRUISE - lift rotors STOPPED, wing + pusher only");
			else sb.Append("IDLE - no command (plant not driving)");
			return sb.ToString();
		}

		private async void Grab()
		{
			await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
			var img = GetViewport().GetTexture().GetImage();
			string path = $"{outDir}/shot_{shots:D3}_t{elapsed:F0}.png";
			var err = img.SavePng(path);
			if (err != Error.Ok) GD.PrintErr($"[shot] save failed {path}: {err}");
			shots++;
		}
	}
}
