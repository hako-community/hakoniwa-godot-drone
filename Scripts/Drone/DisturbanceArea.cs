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
//            var target = FindComponentInParent<IDroneDisturbableObject>(body);
            var target = FindNodeByInterface<IDroneDisturbableObject>(body);
            if (target != null)
            {
                target.ApplyDisturbance(temperature, windVector);
            }
        }

        private void OnBodyExited(Node3D body)
        {
//            var target = FindComponentInParent<IDroneDisturbableObject>(body);
            var target = FindNodeByInterface<IDroneDisturbableObject>(body);
            if (target != null)
            {
                target.ResetDisturbance();
            }
        }

        private T FindComponentInParent<T>(Node node) where T : class
        {
            Node current = node;
            while (current != null)
            {
                if (current is T found) return found;
                current = current.GetParent();
            }
            return null;
        }
        public T FindNodeByInterface<T>(Node root) where T : class
        {
            if (root is T found) return found;

            foreach (Node child in root.GetChildren())
            {
                var result = FindNodeByInterface<T>(child);
                if (result != null) return result;
            }
            return null;
        }



    }
}