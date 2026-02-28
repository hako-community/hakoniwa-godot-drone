using Godot;
using System;

namespace hakoniwa.objects.core
{
    public partial class Wind : Node3D
    {
        [Export]
        public Node3D stick;
        [Export]
        public Node3D drone;
        [Export]
        public Node3D wind_state;
        [Export]
        public Node3D wind_spinner;
        [Export]
        public float maxSpinSpeed = 360f;
        
        public Vector3 wind_direction;

        [Export]
        public float baseRotateSpeed = 2.0f;
        [Export]
        public float maxRotateSpeed = 10.0f;
        [Export]
        public float maxWindStrength = 2.0f;

        public override void _Process(double delta)
        {
            if (stick == null || drone == null || wind_spinner == null)
                return;

            // Godot: Replaced magnitude with Length()
            float windStrength = wind_direction.Length();
            float speedRatio = Mathf.Clamp(windStrength / maxWindStrength, 0.0f, 1.0f);
            float currentRotateSpeed = Mathf.Lerp(baseRotateSpeed, maxRotateSpeed, speedRatio);

            if (windStrength > 0.0001f)
            {
                // Convert world wind direction to drone local coordinates
                // Unity's InverseTransformDirection(wind_direction.normalized)
                // Godot: ToLocal(GlobalPosition + direction) - ToLocal(GlobalPosition)
                Vector3 normalizedWind = wind_direction.Normalized();
                Vector3 localWindDir = drone.ToLocal(drone.GlobalPosition + normalizedWind);
                
                // Project onto horizontal plane (ignore local Y)
                localWindDir.Y = 0f;
                
                // Replaced sqrMagnitude with LengthSquared()
                if (localWindDir.LengthSquared() > 0.0001f)
                {
                    localWindDir = localWindDir.Normalized();
                    
                    // Godot: LookAt for local rotation. 
                    // Note: stick.LookAt(stick.Position + localWindDir, Vector3.Up) would set Global basis.
                    // To handle local rotation correctly:
                    
                    // Target angle in radians
                    float targetYaw = Mathf.Atan2(localWindDir.X, localWindDir.Z);
                    
                    // Current local rotation
                    Vector3 currentEuler = stick.Rotation;
                    
                    // Interpolate Yaw
                    float nextYaw = (float)Mathf.LerpAngle(currentEuler.Y, targetYaw, delta * currentRotateSpeed);
                    
                    // Update stick rotation
                    stick.Rotation = new Vector3(currentEuler.X, nextYaw, currentEuler.Z);

                    // Spinner rotation
                    float spinSpeed = maxSpinSpeed * speedRatio;
                    // RotateY takes radians
                    wind_spinner.RotateY(Mathf.DegToRad(spinSpeed * (float)delta));
                }
            }
        }
    }
}