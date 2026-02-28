using System.Threading.Tasks;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;
using Godot;

namespace hakoniwa.ar.bridge
{
    public partial class HakoniwaDeviceAvatar : Node3D, IHakoniwaArObject
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
            // Note: ARBridge class seems to be missing in the current Godot project.
            // This will still cause a compile error until ARBridge is implemented or provided.
            var pdu_manager = ARBridge.Instance.Get();
            if (pdu_manager == null)
            {
                throw new System.Exception("Can not get Pdu Manager");
            }
            //this.robotName = robot_name;
            var ret = await pdu_manager.DeclarePduForRead(robotName, pdu_name);
            GD.Print($"declare for read pdu_name: {robotName}/{pdu_name} ret = {ret}");
        }

        public override void _PhysicsProcess(double delta)
        {
            var pdu_manager = ARBridge.Instance.Get();
            if (pdu_manager == null)
            {
                return;
            }

            /*
             * Position
             */
            IPdu pdu_pos = pdu_manager.ReadPdu(robotName, pdu_name);
            if (pdu_pos != null)
            {
                Twist pos = new Twist(pdu_pos);
                UpdatePosition(pos);
            }
        }

        private void UpdatePosition(Twist pos)
        {
            Godot.Vector3 unity_pos = new Godot.Vector3();
            unity_pos.Z = (float)pos.linear.x;
            unity_pos.X = -(float)pos.linear.y;
            unity_pos.Y = (float)pos.linear.z;
            
            if (body != null)
            {
                body.Position = unity_pos;

                float roll = (float)pos.angular.x;
                float pitch = (float)pos.angular.y;
                float yaw = (float)pos.angular.z;

                // Unity Euler(pitch, -yaw, -roll)
                // In Godot, we use Quaternion.FromEuler(Vector3) which takes radians.
                body.Quaternion = Godot.Quaternion.FromEuler(new Godot.Vector3(pitch, -yaw, -roll));
            }
        }
    }
}