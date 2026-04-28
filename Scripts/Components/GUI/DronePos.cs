using System.Collections.Generic;
using Godot;

namespace hakoniwa.objects.core
{
    public partial class DronePos : Node
    {
        [Export]
        public Node3D drone; // ドローンのNode -> Node3D
        [Export]
        public Label altitudeText; // 高度表示用のText -> Label
        [Export]
        public Label positionText; // 位置表示用のText -> Label
        [Export]
        public Label velocityText; // 速度表示用のText -> Label
        [Export]
        public Label distanceText; // 走行距離表示用 のText -> Label

        private float altitudeScale = 1f; // 高度表示のスケール
        private float positionScale = 1f; // 位置表示のスケール

        private Vector3 previousPosition; // 前フレームのドローンの位置
        private float totalDistance; // 合計走行距離
        private Queue<Vector3> velocityQueue = new Queue<Vector3>(); // 速度のキュー
        private int velocityQueueSize = 100; // 速度の平均化に使用するサンプル数

        public override void _Ready()
        {
            if (drone != null)
                previousPosition = drone.GlobalPosition; // 初期位置を保存
            
            totalDistance = 0f; // 走行距離を初期化
        }

        public override void _Process(double delta)
        {
            if (drone == null) return;

            // ドローンの高度と位置を取得
            float altitude = GetDroneAltitude();
            Vector3 position = GetDronePosition();
            Vector3 velocity = GetSmoothedVelocity(GetDroneVelocity((float)delta));

            // 走行距離を計算
            float distance = position.DistanceTo(previousPosition);
            totalDistance += distance;

            // 高度に応じたY座標の変化量を計算
            float altitudeYChange = altitude * altitudeScale;

            // 位置に応じたX, Z座標の変化量を計算
            float positionXChange = position.X * positionScale;
            float positionZChange = position.Z * positionScale;

            // 高度と位置をテキストに更新
            if (altitudeText != null)
                altitudeText.Text = "Altitude(m): " + altitude.ToString("F1");
            if (positionText != null)
                positionText.Text = "Position(m): (" + position.X.ToString("F1") + ", " + position.Z.ToString("F1") + ")";

            // 速度をテキストに更新
            // ベクトルの大きさを計算
            float speedMagnitude = Mathf.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);

            // メートル毎秒(m/s)を時速(km/h)に変換
            float speedKmh = speedMagnitude * 3.6f;

            // 速度をテキストに更新
            if (velocityText != null)
                velocityText.Text = "Speed (km/h): " + speedKmh.ToString("F1");

            // 走行距離をテキストに更新
            if (distanceText != null)
                distanceText.Text = "Distance(m): " + totalDistance.ToString("F1");

            // 現在の位置を保存
            previousPosition = position;
        }

        float GetDroneAltitude()
        {
            // ドローンの高度を取得する処理をここに記述します
            return drone.GlobalPosition.Y;
        }

        Vector3 GetDronePosition()
        {
            // ドローンの位置を取得する処理をここに記述します
            return drone.GlobalPosition;
        }

        Vector3 GetDroneVelocity(float delta)
        {
            // ドローンの速度を計算する
            Vector3 currentPosition = drone.GlobalPosition;
            Vector3 velocity = (currentPosition - previousPosition) / Mathf.Max(delta, 0.0001f);
            return velocity;
        }
        Vector3 GetSmoothedVelocity(Vector3 newVelocity)
        {
            // 新しい速度をキューに追加
            velocityQueue.Enqueue(newVelocity);

            // キューのサイズを超えたら古い速度を削除
            if (velocityQueue.Count > velocityQueueSize)
            {
                velocityQueue.Dequeue();
            }

            // 平均速度を計算
            Vector3 smoothedVelocity = Vector3.Zero;
            foreach (Vector3 v in velocityQueue)
            {
                smoothedVelocity += v;
            }
            if (velocityQueue.Count > 0)
                smoothedVelocity /= velocityQueue.Count;

            return smoothedVelocity;
        }
    }
}