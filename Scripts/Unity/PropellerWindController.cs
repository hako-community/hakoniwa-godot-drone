using Godot;

namespace hakoniwa.objects.core
{
    public partial class PropellerWindController : Node3D
    {
        [Export]
        public GpuParticles3D windParticle;

        [ExportGroup("Wind Parameters")]
        [Export]
        public float speedFactor = 2.0f;
        [Export]
        public float lifetimeBase = 0.5f;
        [Export]
        public float lifetimeReductionFactor = 0.05f;
        [Export]
        public float minLifetime = 0.1f;
        [Export]
        public float maxLifetime = 1.0f;

        [ExportGroup("Wind Position Offset")]
        [Export]
        public float offsetDistance = 0.1f;

        [ExportGroup("Wind Threshold")]
        [Export]
        public float windThreshold = 0.05f; // この値以下で停止

        private Vector3 baseLocalPosition;
        private Vector3 windVelocity = Vector3.Zero;

        private Node3D parentTransform;

        public override void _Ready()
        {
            parentTransform = GetParent<Node3D>();
            baseLocalPosition = Position;
        }

        public void SetWindVelocityFromRos(Vector3 rosVelocity)
        {
            // ROS ENU (x, y, z) -> Unity (x, z, -y)
            // Godot is Y-up. ROS is Z-up. 
            // In DronePlayer.cs, we used: unity.Z = ros.x, unity.X = -ros.y, unity.Y = ros.z
            // Here we should probably stay consistent with that mapping if possible,
            // but the original code used: windVelocity = new Vector3(rosVelocity.x, rosVelocity.z, -rosVelocity.y)
            // Let's stick to the original logic's mapping but with Godot uppercase.
            windVelocity = new Vector3(rosVelocity.X, rosVelocity.Z, -rosVelocity.Y);
        }

        public override void _Process(double delta)
        {
            if (windParticle == null) return;

            float windStrength = windVelocity.Length();

            if (windStrength < windThreshold)
            {
                StopWind();
                return;
            }

            UpdateWindPositionAndDirection();
            UpdateParticleSettings(windStrength);
            PlayWind();
        }

        private void UpdateWindPositionAndDirection()
        {
            // Unity's TransformPoint(localPosition) -> Godot's ToGlobal(localPosition)
            Vector3 baseWorldPosition = parentTransform != null
                ? parentTransform.ToGlobal(baseLocalPosition)
                : GlobalPosition;

            if (windVelocity.IsZeroApprox()) return;

            Vector3 worldDirection = windVelocity.Normalized();

            // Godot's LookAt: 1st param is target position, 2nd is up vector.
            // LookAt makes the node face the target position.
            // If we want it to face worldDirection, we target (GlobalPosition + worldDirection).
            if (!worldDirection.IsZeroApprox())
            {
                LookAt(GlobalPosition + worldDirection, Vector3.Up);
            }
            
            GlobalPosition = baseWorldPosition - worldDirection * offsetDistance;
        }

        private void UpdateParticleSettings(float windStrength)
        {
            // Godot's GPUParticles3D has different properties than Unity's ParticleSystem.MainModule
            // Speed can be controlled via SpeedScale or process material properties.
            windParticle.SpeedScale = windStrength * speedFactor;
            
            // Lifetime is a property of GPUParticles3D
            windParticle.Lifetime = Mathf.Clamp(lifetimeBase - windStrength * lifetimeReductionFactor, minLifetime, maxLifetime);
        }

        private void StopWind()
        {
            if (windParticle.Emitting)
            {
                windParticle.Emitting = false;
            }

            // 位置はベース位置に戻す
            Vector3 baseWorldPosition = parentTransform != null
                ? parentTransform.ToGlobal(baseLocalPosition)
                : GlobalPosition;
            GlobalPosition = baseWorldPosition;
        }

        private void PlayWind()
        {
            if (!windParticle.Emitting)
            {
                windParticle.Emitting = true;
            }
        }
    }
}