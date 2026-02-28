using Godot;

namespace hakoniwa.objects.core
{
    public partial class DronePitch : Control
    {
        [Export]
        private float deg_5 = 32; // ピッチ角が5度増えた場合のY座標の変化量
        [Export]
        public Control pitchpicture; // Replaced RectTransform with Control
        [Export]
        public Node3D drone; // Replaced Node with Node3D for rotation access

        public override void _Process(double delta)
        {
            if (pitchpicture == null || drone == null) return;

            // ドローンのピッチ角を取得
            float dronePitch = GetDronePitch();
            
            // ピッチ角を-180度から180度の範囲に正規化
            if (dronePitch > 180f)
            {
                dronePitch -= 360f;
            }
            
            // ピッチ角に応じたY座標の変化量を計算
            float yChange = (dronePitch / 5) * deg_5;
            
            // Godot Control nodes use Position for anchored movement relative to anchors.
            pitchpicture.Position = new Vector2(pitchpicture.Position.X, yChange);
        }

        float GetDronePitch()
        {
            // ドローンのピッチ角度（X軸の回転角度）を取得
            return drone.RotationDegrees.X;
        }
    }
}