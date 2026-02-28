using hakoniwa.ar.bridge.sharesim;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;
using hakoniwa.pdu.msgs.hako_msgs;
using Godot;

namespace hakoniwa.ar.bridge.sharesim
{
    public partial class DefaultShareSimPhysics : Node, IShareSimPhysics
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

        public void StartPhysics()
        {
            this.rd.Freeze = false;
        }

        public void StopPhysics()
        {
            this.rd.Freeze = true;
        }

        public void UpdatePosition(ShareObjectOwner owner)
        {
            SetPosition(owner.pos, body.Position, body.Rotation);
        }
        public static void SetPosition(Twist pos, Godot.Vector3 unity_pos, Godot.Vector3 unity_rot)
        {
            pos.linear.x = unity_pos.Z;
            pos.linear.y = -unity_pos.X;
            pos.linear.z = unity_pos.Y;

//            pos.angular.x = -Mathf.Deg2Rad * unity_rot.Z;
//            pos.angular.y = Mathf.Deg2Rad * unity_rot.X;
//            pos.angular.z = -Mathf.Deg2Rad * unity_rot.Y;

            pos.angular.x = -unity_rot.Z;
            pos.angular.y = unity_rot.X;
            pos.angular.z = -unity_rot.Y;
        }

    }
}