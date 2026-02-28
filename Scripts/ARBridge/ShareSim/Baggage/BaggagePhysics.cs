using hakoniwa.ar.bridge.sharesim;
using hakoniwa.objects.core;
using hakoniwa.pdu.msgs.hako_msgs;
using Godot;

namespace hakoniwa.ar.bridge.sharesim
{
    public partial class BaggagePhysics : Node3D, IShareSimPhysics
    {
        private Baggage baggage;

        public void Initialize(Node target)
        {
//            baggage = FindComponent<Baggage>(target);
            baggage = FindNodeByInterface<Baggage>(target);
            if (baggage == null)
            {
                throw new System.Exception("Can not find baggage on " + target.Name);
            }
        }

        private T FindComponent<T>(Node node) where T : class
        {
            if (node is T found) return found;
            foreach (Node child in node.GetChildren())
            {
                var result = FindComponent<T>(child);
                if (result != null) return result;
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

        public void StartPhysics()
        {
            //nothing to do
        }

        public void StopPhysics()
        {
            //nothing to do
        }

        public void UpdatePosition(ShareObjectOwner owner)
        {
            // Godot: Replaced transform.position with GlobalPosition 
            // and transform.eulerAngles with GlobalRotationDegrees
            DefaultShareSimPhysics.SetPosition(owner.pos, GlobalPosition, GlobalRotationDegrees);
        }

    }
}