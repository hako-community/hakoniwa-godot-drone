using Godot;
using System;
using System.ComponentModel;

public partial class CameraGimbal : Camera3D
{
    // インスペクターからドローンノードを選択できるようにする
    [Export] public Node3D DroneNode;

    // ドローンの中心からカメラをどれだけ離すか（例：真下へ0.5m）
    [Export] public Vector3 Offset = new Vector3(0, -0.5f, 0);

    [Export] public float CameraRotate= 30f; // カメラの下向きの角度（例：30度）

    public override void _PhysicsProcess(double delta)
    {
        if (DroneNode == null) return;

        // 1. 位置の同期（ドローンの位置 + オフセット）
        // GlobalPositionを使うことで、親子関係に関わらず正しい世界座標に配置されます
        GlobalPosition = DroneNode.GlobalPosition + Offset;

        // 2. 回転の同期（ヨーのみ抽出）
        // ドローンの現在の回転を取得
        Vector3 droneRotation = DroneNode.GlobalRotation;

        // X(ピッチ)とZ(ロール)は0に固定し、Y(ヨー)だけをコピーする
        // これでドローンが傾いてもカメラは常に水平を保ち、かつ旋回には追従します
        GlobalRotation = new Vector3(0, droneRotation.Y, 0);

        // 3. (オプション) もしカメラ自体を少し下向きに傾けたい場合
        // ここで追加の回転を与えます（例：30度だけ下を向く）
        RotateObjectLocal(Vector3.Right, Mathf.DegToRad(-CameraRotate));
    }
}