using Godot;

namespace hakoniwa.objects.core
{
    public partial class DroneCompass : Control
    {
        [Export]
        public Control arrow;  // Replaced RectTransform with Control
        [Export]
        public Node3D drone;   // Replaced Node with Node3D for rotation access

        public override void _Process(double delta)
        {
            if (arrow == null || drone == null) return;

            // Get drone's Y rotation in degrees
            float droneDirection = GetDroneDirection();

            // In Godot Control nodes, rotation is in radians by default, 
            // but we can set RotationDegrees.
            // Note: 2D UI rotation in Godot: positive is clockwise.
            arrow.RotationDegrees = -droneDirection;
        }

        float GetDroneDirection()
        {
            // Returns the drone's Y-axis rotation in degrees
            return drone.RotationDegrees.Y;
        }
    }
}