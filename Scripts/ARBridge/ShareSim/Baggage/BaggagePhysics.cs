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
            baggage = NodeUtil.FindNodeByInterface<Baggage>(target);
            if (baggage == null)
            {
                throw new System.Exception("Can not find baggage on " + target.Name);
            }
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