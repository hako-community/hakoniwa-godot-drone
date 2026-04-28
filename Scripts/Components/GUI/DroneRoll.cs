using Godot;

namespace hakoniwa.objects.core
{
    public partial class DroneRoll : Control
    {
        [Export]
        public Control rollpicture;
        [Export]
        public Node3D drone;

        public override void _Process(double delta)
        {
            if (rollpicture == null || drone == null) return;

            // Get drone's Z rotation (Roll)
            float droneRoll = GetDroneRoll();

            // Godot Control nodes use a single float for RotationDegrees (around the Z axis in 2D)
            rollpicture.RotationDegrees = -droneRoll;
        }

        float GetDroneRoll()
        {
            // Returns the drone's Z-axis rotation in degrees
            return drone.RotationDegrees.Z;
        }
    }
}