using System.Collections.Generic;
using Godot;
using Vector3 = Godot.Vector3;

namespace hakoniwa.drone
{
    public interface IDroneDisturbableObject
    {
        void ApplyDisturbance(float temperature, Godot.Vector3 windVector);
        void ResetDisturbance();
    }

    public partial class DisturbanceArea : Area3D
    {
        [Export]
        public float temperature = 20.0f;
        [Export]
        public Vector3 windVector = new Vector3(1f, 0f, 0f);

        public override void _Ready()
        {
            BodyEntered += OnBodyEntered;
            BodyExited += OnBodyExited;
        }

        private void OnBodyEntered(Node3D body)
        {
            var target = NodeUtil.FindNodeByInterface<IDroneDisturbableObject>(body);
            if (target != null)
            {
                target.ApplyDisturbance(temperature, windVector);
            }
        }

        private void OnBodyExited(Node3D body)
        {
            var target = NodeUtil.FindNodeByInterface<IDroneDisturbableObject>(body);
            if (target != null)
            {
                target.ResetDisturbance();
            }
        }
    }
}