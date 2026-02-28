using System.Collections.Generic;
using Godot;

namespace hakoniwa.objects.core
{
    public partial class DroneMapUI : Node
    {
        [Export]
        public Node3D drone;          // ドローンオブジェクト
        [Export]
        public Control map;         // マップのUI（親オブジェクト） -> Control
        [Export]
        public Control droneIcon;   // ドローン位置を示すUIオブジェクト -> Control
        [Export]
        public Control droneRollIcon; // ロール角度を示すUIオブジェクト -> Control
        [Export]
        public Control dronePitchIcon; // ピッチ角度を示すUIオブジェクト -> Control
        [Export]
        public Label scaleText;            // スケール表示用のテキストUI -> Label
        
        [Export]
        public float initialWorldSize = 500.0f;  // 初期設定の飛行可能範囲（ワールド空間の大きさ）
        [Export]
        public float map_adjust_scale = 0.8f;    // マップサイズ調整スケール
        [Export]
        public float pitch_adjust_scale = 2.0f;  // ピッチ角のスケール
        
        private float map_scale = 1.0f;          // 現在のマップスケール（スケール値）
        private float currentWorldSize;          // 現在のスケール範囲

        private Vector2 initialPitchIconPosition; // ピッチUIの初期位置を記録
        private Vector3 initialDronePosition;     // ドローンの初期位置を記録

        public override void _Ready()
        {
            if (drone == null || map == null || droneIcon == null)
            {
                GD.PrintErr("DroneMapUI: Some references are missing.");
                return;
            }

            // 初期値として設定されたワールドサイズを現在のワールドサイズに設定
            currentWorldSize = initialWorldSize;

            // 初期のスケールを表示
            UpdateScaleText();

            // ピッチアイコンの初期位置を記録
            if (dronePitchIcon != null)
                initialPitchIconPosition = dronePitchIcon.Position;

            // ドローンの初期位置を記録
            initialDronePosition = drone.GlobalPosition;
        }

        public override void _Process(double delta)
        {
            if (drone == null) return;
            UpdateScale();
            UpdatePosition();
            UpdateRotation();
        }

        private void UpdateScale()
        {
            // ドローンの現在の位置と初期位置の距離を計算
            Vector3 realDronePos = drone.GlobalPosition;
            float distanceFromInitial = initialDronePosition.DistanceTo(realDronePos);

            // ドローンが現在のスケール範囲を超えている場合
            if (distanceFromInitial > currentWorldSize)
            {
                // 現在のワールドサイズを拡大
                currentWorldSize *= 2.0f;
                map_scale *= 2.0f; // スケールが2倍になるので、表示倍率も更新

                // スケールテキストの更新
                UpdateScaleText();
            }

            // ドローンがスケール範囲内に戻ってきた場合（ヒステリシスを考慮して距離が十分小さくなったら）
            if (distanceFromInitial < currentWorldSize * 0.4f && currentWorldSize > initialWorldSize)
            {
                // 現在のワールドサイズを元に戻す
                currentWorldSize /= 2.0f;
                map_scale /= 2.0f; // スケールが小さくなるので、表示倍率も更新

                // スケールテキストの更新
                UpdateScaleText();
            }
        }

        private void UpdatePosition()
        {
            if (map == null || droneIcon == null) return;

            // マップの半径を取得
            float mapRadius = map_adjust_scale * map.Size.X / 2.0f; // 正方形のマップを前提にして幅の半分を半径とする

            // ドローンの現在の位置と初期位置の距離を計算
            Vector3 realDronePos = drone.GlobalPosition;
            float distanceFromInitial = initialDronePosition.DistanceTo(realDronePos);

            // 現在のワールドサイズに応じてスケールを決定
            float scaleFactor = mapRadius / currentWorldSize; // ワールド空間からマップ空間へのスケール
            Vector2 direction = new Vector2(realDronePos.X - initialDronePosition.X, realDronePos.Z - initialDronePosition.Z).Normalized();
            Vector2 mapLocalPos = direction * distanceFromInitial * scaleFactor;

            // 円形の範囲内に収めるためのクランプ処理
            if (mapLocalPos.Length() > mapRadius)
            {
                mapLocalPos = mapLocalPos.Normalized() * mapRadius;
            }

            // ドローンアイコンの位置をマップ内の相対的な位置に設定
            // Note: If droneIcon is child of map, Position is local.
            // If map center is (0,0) logic works. If map center is (Width/2, Height/2), we need offset.
            // Assuming map anchor is centered or Position logic is relative to center.
            // For now, direct port:
            droneIcon.Position = mapLocalPos + map.Size / 2.0f; // Centering offset if origin is top-left
        }

        private void UpdateRotation()
        {
            if (droneIcon == null || droneRollIcon == null || dronePitchIcon == null) return;

            // ドローンの回転を取得してマップ内に反映
            Vector3 realDroneAngle = drone.GlobalRotationDegrees;

            // Yaw (方向) を反映
            // Godot Control Rotation is float (degrees)
            droneIcon.RotationDegrees = -realDroneAngle.Y; // ドローンの向きを反映 (Y軸の回転をZに反映)

            // Roll (ロール角) を反映
            droneRollIcon.RotationDegrees = realDroneAngle.Z;

            // ピッチアイコンの高さを設定
            UpdatePitchIcon(realDroneAngle);
        }

        private void UpdatePitchIcon(Vector3 realDroneAngle)
        {
            // ピッチ角の設定
            float pitchAngle = NormalizeAngle(realDroneAngle.X); // 角度を -180 から 180 の範囲に正規化

            // ±40度の制限を適用
            pitchAngle = pitch_adjust_scale * Mathf.Clamp(pitchAngle, -40.0f, 40.0f);
            
            // ピッチ角を高さに反映 (初期位置からピッチに応じた高さを設定)
            Vector2 newPitchPosition = initialPitchIconPosition;
            // Unity Y is Up. Godot Y is Down.
            // If we want pitch UP to move icon UP visually on screen:
            // Unity: newPos.y += pitchAngle (moves up)
            // Godot: newPos.Y -= pitchAngle (moves up)
            // Let's invert the sign for Godot.
            newPitchPosition.Y -= pitchAngle; 
            
            dronePitchIcon.Position = newPitchPosition;
        }

        // スケールテキストを更新するメソッド
        private void UpdateScaleText()
        {
            if (scaleText != null)
            {
                int scaleInt = Mathf.RoundToInt(map_scale);
                scaleText.Text = $"1/{scaleInt}";
            }
        }

        // 角度を -180 から 180 に正規化するメソッド
        private float NormalizeAngle(float angle)
        {
            while (angle > 180) angle -= 360;
            while (angle < -180) angle += 360;
            return angle;
        }
    }
}