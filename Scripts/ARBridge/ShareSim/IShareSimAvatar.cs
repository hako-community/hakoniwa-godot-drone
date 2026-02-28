using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.hako_msgs;
using Godot;

namespace hakoniwa.ar.bridge.sharesim
{
    public interface IShareSimAvatar
    {
        void Initialize(Node target);
        void StartAvatarProc();
        void StopAvatarProc();
        void UpdatePosition(ShareObjectOwner owner);
    }
}
