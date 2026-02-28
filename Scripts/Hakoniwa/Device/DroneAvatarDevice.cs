using System;
using System.Threading.Tasks;
using hakoniwa.ar.bridge;
using hakoniwa.drone;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;
using hakoniwa.pdu.msgs.hako_mavlink_msgs;
using Godot;

public partial class DroneAvatarDevice : Node, IHakoniwaArObject
{
    [Export]
    public string robotName = "Drone";
    public string pdu_name_propeller = "motor";
    public string pdu_name_pos = "pos";
    [Export]
    public Node3D body;
    private DronePropeller drone_propeller;

    public override void _Ready()
    {
        if (body == null)
        {
            throw new Exception("Body is not assigned");
        }
//        drone_propeller = FindComponent<DronePropeller>(body);
        drone_propeller = FindNodeByInterface<DronePropeller>(body);
        if (drone_propeller == null)
        {
            throw new Exception("Can not found drone propeller");
        }
        GD.Print("max rotation : " + drone_propeller.maxRotationSpeed);
    }
    
    private float[] prev_controls = new float[4];

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

    public override void _PhysicsProcess(double delta)
    {
        var pduManager = ARBridge.Instance.Get();
        if (pduManager == null) return;

        /*
         * Position
         */
        IPdu pdu_pos = pduManager.ReadPdu(robotName, pdu_name_pos);
        if (pdu_pos != null)
        {
            Twist pos = new Twist(pdu_pos);
            UpdatePosition(pos);
        }

        /*
         * Propeller
         */
        IPdu pdu_propeller = pduManager.ReadPdu(robotName, pdu_name_propeller);
        if (pdu_propeller == null)
        {
            drone_propeller.Rotate(prev_controls[0], prev_controls[1], prev_controls[2], prev_controls[3]);
        }
        else
        {
            HakoHilActuatorControls propeller = new HakoHilActuatorControls(pdu_propeller);
            for (int i = 0; i < 4; i++)
            {
                prev_controls[i] = propeller.controls[i];
            }
            drone_propeller.Rotate((float)propeller.controls[0], (float)propeller.controls[1], (float)propeller.controls[2], (float)propeller.controls[3]);
        }
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
 
        // Use radians directly for FromEuler
        body.Quaternion = Godot.Quaternion.FromEuler(new Godot.Vector3(pitch, -yaw, -roll));
    }

    public async Task DeclarePduAsync(string type_name, string robot_name)
    {
        var pdu_manager = ARBridge.Instance.Get();
        if (pdu_manager == null)
        {
            throw new Exception("Can not get Pdu Manager");
        }
        var ret = await pdu_manager.DeclarePduForRead(robotName, pdu_name_pos);
        GD.Print("declare pdu pos: " + ret);
        ret = await pdu_manager.DeclarePduForRead(robotName, pdu_name_propeller);
        GD.Print("declare pdu propeller: " + ret);
    }
}