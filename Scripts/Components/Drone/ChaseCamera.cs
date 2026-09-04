using Godot;

/// <summary>
/// 機体を追いかけるカメラ。
///
/// ★★★★ なぜ要るか（2026-09-04・mujoco-drone R-2b）:
///   drone3_hako.tscn のカメラは**原点に固定**されていた。ホバリングは写るが、
///   **巡航して 350 m 離れると画角の外に出る**ため、巡航のスクリーンショットが 1 枚も撮れなかった
///   （PDU も描画も正しく動いていて、写らないのはカメラだけ、という状態だった）。
///
/// ★★★ 位置だけ追い、姿勢は追わない（既定）。
///   機体の下にぶら下げる／回転まで真似ると、機体が傾くたびに絵が揺れて見づらい。
///   ★ ヨーだけ追いたいときは FollowYaw を true にする（旋回を追う撮影向け）。
/// </summary>
public partial class ChaseCamera : Camera3D
{
    /// <summary>追う相手。★ drone3_hako.tscn では DroneAvatar が付いた Drone3 ノード
    /// （DroneAvatar は自分自身の GlobalPosition を動かすので、model ではなく親を指すこと）。</summary>
    [Export] public Node3D Target;

    /// <summary>相手からの相対位置。★ 機首は -Z なので、+Z が「後ろ」。</summary>
    [Export] public Vector3 Offset = new Vector3(7.0f, 6.5f, 8.5f);

    /// <summary>ヨーに合わせてオフセットも回すか（既定 false ＝ 世界座標のまま）。</summary>
    [Export] public bool FollowYaw = false;

    /// <summary>追従の速さ [1/s]。0 以下で即時追従。★ 速い巡航では 6 くらいが見やすい。</summary>
    [Export] public float Smooth = 6.0f;

    /// <summary>false にすると何もしない（＝ 従来どおりの固定カメラに戻る）。</summary>
    [Export] public bool Enabled = true;

    public override void _Ready()
    {
        if (Target == null)
        {
            GD.PrintErr("[ChaseCamera] Target が未設定。固定カメラとして振る舞う");
            Enabled = false;
        }
    }

    public override void _Process(double delta)
    {
        if (!Enabled || Target == null) return;

        Vector3 offset = FollowYaw
            ? new Basis(Vector3.Up, Target.GlobalRotation.Y) * Offset
            : Offset;
        Vector3 want = Target.GlobalPosition + offset;

        if (Smooth > 0.0f)
        {
            float k = Mathf.Min(1.0f, (float)delta * Smooth);
            GlobalPosition = GlobalPosition.Lerp(want, k);
        }
        else
        {
            GlobalPosition = want;
        }

        // ★ 相手と重なっていると LookAt が例外を投げる。距離があるときだけ向ける。
        if (GlobalPosition.DistanceSquaredTo(Target.GlobalPosition) > 0.01f)
        {
            LookAt(Target.GlobalPosition, Vector3.Up);
        }
    }
}
