using Godot;
using System;
using System.ComponentModel;

public partial class CameraGimbal : Camera3D
{
    // インスペクターからドローンノードを選択できるようにする
    [Export] public Node3D DroneNode;
    [Export] public Node3D target;

    // ドローンの中心からカメラをどれだけ離すか（例：真下へ0.5m）
    [Export] public Vector3 Offset = new Vector3(0, -0.5f, 0);

    [Export] public float CameraRotate = 30f; // カメラの下向きの角度（例：30度）

    public override void _Process(double delta)
    {
        Node3D followTarget = DroneNode ?? target;
        if (followTarget == null) return;

        // 1. 位置の同期（ドローンの位置 + オフセット）
        GlobalPosition = followTarget.GlobalPosition + Offset;

        // 2. 回転の同期（ヨーのみ抽出）
        Vector3 droneRotation = followTarget.GlobalRotation;

        // X(ピッチ)とZ(ロール)は0に固定し、Y(ヨー)だけをコピーする
        GlobalRotation = new Vector3(0, droneRotation.Y, 0);

        // 3. (オプション) カメラ自体の傾き
        RotateObjectLocal(Vector3.Right, Mathf.DegToRad(-CameraRotate));
    }
}