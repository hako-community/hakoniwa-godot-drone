using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.hako_msgs;
using Godot;

namespace hakoniwa.ar.bridge.sharesim
{
    public interface IShareSimPhysics
    {
        void Initialize(Node target);
        void StartPhysics();
        void StopPhysics();
        void UpdatePosition(ShareObjectOwner owner);
    }
}
