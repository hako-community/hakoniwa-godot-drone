using hakoniwa.ar.bridge.sharesim;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;
using hakoniwa.pdu.msgs.hako_msgs;
using Godot;

namespace hakoniwa.ar.bridge.sharesim
{
    public partial class DefaultShareSimAvatar : Node, IShareSimAvatar
    {
        private Node3D body;
        private RigidBody3D rd;

        public void Initialize(Node target)
        {
            this.body = target as Node3D;
            if (this.body != null)
            {
                this.rd = FindRigidBody3D(this.body);
            }
        }

        private RigidBody3D FindRigidBody3D(Node node)
        {
            if (node is RigidBody3D rb) return rb;
            foreach (Node child in node.GetChildren())
            {
                var found = FindRigidBody3D(child);
                if (found != null) return found;
            }
            return null;
        }

        public void StartAvatarProc()
        {
            if (this.rd != null)
            {
                // In Godot, Freeze = true is equivalent to isKinematic = true for disabling physics response
                this.rd.Freeze = true;
            }
        }

        public void StopAvatarProc()
        {
            if (this.rd != null)
            {
                this.rd.Freeze = false;
            }
        }

        public void UpdatePosition(ShareObjectOwner owner)
        {
            UpdatePosition(owner.pos);
        }

        private void UpdatePosition(Twist pos)
        {
            if (body == null) return;

            Godot.Vector3 unity_pos = new Godot.Vector3();
            unity_pos.Z = (float)pos.linear.x;
            unity_pos.X = -(float)pos.linear.y;
            unity_pos.Y = (float)pos.linear.z;
            
            body.Position = unity_pos;

            float roll = (float)pos.angular.x;
            float pitch = (float)pos.angular.y;
            float yaw = (float)pos.angular.z;
            
            // Godot's Quaternion.FromEuler takes a Vector3 of radians.
            // Original mapping was Euler(pitch, -yaw, -roll)
            body.Quaternion = Godot.Quaternion.FromEuler(new Godot.Vector3(pitch, -yaw, -roll));
        }
    }
}