using Godot;
using Hakoniwa.Drone.Service; // 作成したHakoServiceの名前空間

public partial class DroneController : Node3D
{
	private const int DRONE_INDEX = 0; // ドローンのインデックス
	public bool enable_data_logger = false;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		string droneConfigText = LoadTextFromFile("config/drone/drone_config_0.json");
		string filename = "param-api-mixer.txt";
		string controllerConfigText = LoadTextFromFile("config/controller/" + filename);

		GD.Print("Initializing Hakoniwa Drone Service...");
		// DroneServiceRC.Init() に相当する処理
		// Godotではプロジェクトの実行ファイルからの相対パスになります。
		// "res://" はGodotのプロジェクトルートを指します。
		// ここでは仮にカレントディレクトリを指定します。
//		int result = HakoService.Init("config/drone_config_0");
		int result = HakoService.InitSingle(droneConfigText, controllerConfigText, enable_data_logger, null);
		if (result == 0)
		{
			GD.Print("HakoService Initialized Successfully.");
			HakoService.Start();
			GD.Print("HakoService Started.");
		}
		else
		{
			GD.PushError("Failed to initialize HakoService.");
			GD.Print("Failed to initialize HakoService.");
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		// DroneServiceRC.Run() に相当
		HakoService.Run();

		// GetPosition/Attitudeで位置や姿勢を取得して、Godotのノードに反映
		double x, y, z;
		if (HakoService.GetPosition(DRONE_INDEX, out x, out y, out z) == 0)
		{
			// 座標系がROS ENU (右手系) と同じであれば、そのまま代入できる可能性が高い
			this.GlobalPosition = new Vector3((float)x, (float)y, (float)z);
		}

		double roll, pitch, yaw;
		if (HakoService.GetAttitude(DRONE_INDEX, out roll, out pitch, out yaw) == 0)
		{
			 // Godotのオイラー角もYXZ順（内因性）なので、yaw, pitch, rollの順で適用
			this.GlobalRotation = new Vector3((float)pitch, (float)yaw, (float)roll);
		}

//		GD.Print($"Drone Position: {this.GlobalPosition}, Rotation: {this.GlobalRotation}");
	}

	public override void _Notification(int what)
	{
		// アプリケーション終了時にHakoServiceを停止する
		if (what == NotificationWMCloseRequest)
		{
			GD.Print("Stopping HakoService...");
			HakoService.Stop();
		}
	}

	private string LoadTextFromFile(string filePath)
	{
		// 指定したパスのファイルを読み込みモードで開く
		using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);

		// ファイルが開けなかった（存在しない等）場合の処理
		if (file == null)
		{
			GD.PrintErr($"ファイルが見つかりません: {filePath} (エラー: {FileAccess.GetOpenError()})");
			return null;
		}

		// 全内容を文字列として取得
		return file.GetAsText();
	}
}
