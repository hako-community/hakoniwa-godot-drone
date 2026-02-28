using System.Threading.Tasks;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;
using Godot;

namespace hakoniwa.ar.bridge
{
    public partial class HakoniwaDevicePlayer : Node3D, IHakoniwaArObject
    {
        [Export]
        public string robotName = "Player1";
        [Export]
        public string pdu_name = "head";
        [Export]
        public Node3D body;

        public override void _Ready()
        {
            if (body == null)
            {
                throw new System.Exception("Body is not assigned");
            }
        }

        public async Task DeclarePduAsync(string type_name, string robot_name)
        {
            var pdu_manager = ARBridge.Instance.Get();
            if (pdu_manager == null)
            {
                throw new System.Exception("Can not get Pdu Manager");
            }
            //this.robotName = robot_name;
            var ret = await pdu_manager.DeclarePduForWrite(robotName, pdu_name);
            GD.Print($"declare for write pdu_name: {robotName}/{pdu_name} ret = {ret}");
        }

        public override async void _PhysicsProcess(double delta)
        {
            var pdu_manager = ARBridge.Instance.Get();
            if (pdu_manager == null)
            {
                return;
            }
            INamedPdu npdu = pdu_manager.CreateNamedPdu(robotName, pdu_name);
            if (npdu == null)
            {
                throw new System.Exception($"Can not find npud: {robotName} / {pdu_name}");
            }
            Twist pdu = new Twist(npdu.Pdu);
            
            // In Godot, Position and Rotation (Euler) are used.
            // Note: Rotation in Godot is in radians.
            SetPosition(pdu, body.Position, body.Rotation);
            
            pdu_manager.WriteNamedPdu(npdu);
            var ret = await pdu_manager.FlushNamedPdu(npdu);
        }

        private void SetPosition(Twist pos, Godot.Vector3 unity_pos, Godot.Vector3 unity_rot)
        {
            pos.linear.x = unity_pos.Z;
            pos.linear.y = -unity_pos.X;
            pos.linear.z = unity_pos.Y;

            // Godot unity_rot is already in radians.
            pos.angular.x = -unity_rot.Z;
            pos.angular.y = unity_rot.X;
            pos.angular.z = -unity_rot.Y;
        }
    }
}