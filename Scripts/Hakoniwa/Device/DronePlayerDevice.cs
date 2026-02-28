using hakoniwa.ar.bridge;
using hakoniwa.ar.bridge.sharesim;
using hakoniwa.drone;
using hakoniwa.drone.service;
using hakoniwa.objects.core;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.core;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;
using hakoniwa.pdu.msgs.hako_mavlink_msgs;
using hakoniwa.pdu.msgs.hako_msgs;
using System;
using System.Threading.Tasks;
using Godot;
using Vector3 = Godot.Vector3;
using Quaternion = Godot.Quaternion;

public partial class DronePlayerDevice : Node, IHakoniwaArObject, IDroneDisturbableObject
{
    [Export]
    public Node3D body;
    [Export]
    public int debuff_duration_msec = 100;
    private DroneCollision my_collision;
    private DroneControl drone_control;
    private DronePropeller drone_propeller;
    private IHakoniwaArBridge ibridge;
    private BaggageGrabber baggage_grabber;
    private ShareSimClient sharesim_client;
    [Export]
    public bool enable_debuff = false;
    [Export]
    public bool useWebServer = true;

    [Export]
    public bool enable_data_logger = false;
    [Export]
    public string debug_logpath = "";

    [Export]
    public string robotName = "Drone";
    [Export]
    public string pdu_name_propeller = "motor";
    [Export]
    public string pdu_name_pos = "pos";
    private ICameraController camera_controller;
    
    [Export]
    public DroneLedController[] leds = new DroneLedController[0];
    [Export]
    public FlightModeLedController[] flight_mode_leds = new FlightModeLedController[0];
    [Export]
    public PropellerWindController[] propeller_winds = new PropellerWindController[0];

    private void SetPosition(Twist pos, Vector3 unity_pos, Vector3 unity_rot)
    {
        pos.linear.x = unity_pos.Z;
        pos.linear.y = -unity_pos.X;
        pos.linear.z = unity_pos.Y;

        // Godot unity_rot is in radians (from body.Rotation)
        pos.angular.x = -unity_rot.Z;
        pos.angular.y = unity_rot.X;
        pos.angular.z = -unity_rot.Y;
    }


    private async void FlushPduPos(Vector3 unity_pos)
    {
        var pdu_manager = ARBridge.Instance.Get();
        if (pdu_manager == null || body == null)
        {
            return;
        }
        /*
         * Position
         */
        INamedPdu npdu_pos = pdu_manager.CreateNamedPdu(robotName, pdu_name_pos);
        if (npdu_pos == null || npdu_pos.Pdu == null)
        {
            throw new Exception($"Can not find npdu: {robotName} {pdu_name_pos}");
        }
        Twist pos = new Twist(npdu_pos.Pdu);
        SetPosition(pos, body.Position, body.Rotation);
        pdu_manager.WriteNamedPdu(npdu_pos);
        var ret = await pdu_manager.FlushNamedPdu(npdu_pos);
    }
    private async void FlushPduPropeller(float c1, float c2, float c3, float c4)
    {
        var pdu_manager = ARBridge.Instance.Get();
        if (pdu_manager == null)
        {
            return;
        }

        /*
         * Propeller
         */
        INamedPdu npdu = pdu_manager.CreateNamedPdu(robotName, pdu_name_propeller);
        if (npdu == null || npdu.Pdu == null)
        {
            throw new Exception($"Can not find npdu: {robotName} {pdu_name_propeller}");
        }

        HakoHilActuatorControls actuator = new HakoHilActuatorControls(npdu.Pdu);
        float[] controls = new float[16];
        controls[0] = c1;
        controls[1] = c2;
        controls[2] = c3;
        controls[3] = c4;
        actuator.controls = controls;

        pdu_manager.WriteNamedPdu(npdu);
        var ret = await pdu_manager.FlushNamedPdu(npdu);
    }
    public async Task DeclarePduAsync(string type_name, string robot_name)
    {
        var pdu_manager = ARBridge.Instance.Get();
        if (pdu_manager == null)
        {
            throw new Exception("Can not get Pdu Manager");
        }
        var ret = await pdu_manager.DeclarePduForWrite(robotName, pdu_name_pos);
        GD.Print("declare pdu pos: " + ret);
        ret = await pdu_manager.DeclarePduForWrite(robotName, pdu_name_propeller);
        GD.Print("declare pdu propeller: " + ret);
    }

    public override void _Ready()
    {
        if (useWebServer)
        {
            sharesim_client = ShareSimClient.Instance;
            ibridge = HakoniwaArBridgeDevice.Instance;
        }
//        my_collision = FindComponent<DroneCollision>(this);
        my_collision = FindNodeByInterface<DroneCollision>(this);
        if (my_collision == null) {
            throw new Exception("Can not found collision");
        }
//        drone_control = FindComponent<DroneControl>(this);
        drone_control = FindNodeByInterface<DroneControl>(this);
        if (drone_control == null)
        {
            throw new Exception("Can not found drone control");
        }
//        drone_propeller = FindComponent<DronePropeller>(this);
        drone_propeller = FindNodeByInterface<DronePropeller>(this);
        if (drone_propeller == null)
        {
            throw new Exception("Can not found drone propeller");
        }
        my_collision.SetIndex(0);
//        baggage_grabber = FindComponent<BaggageGrabber>(this);
        baggage_grabber = FindNodeByInterface<BaggageGrabber>(this);
    
        if (baggage_grabber == null)
        {
            GD.PushWarning("Can not found BaggageGrabber");
        }

        string droneConfigText = LoadTextFromResources("res://config/drone/rc/drone_config_0");
        string controllerConfigText = LoadTextFromResources("res://config/controller/param-api-mixer");

        if (string.IsNullOrEmpty(droneConfigText))
        {
            GD.PrintErr("Failed to load droneConfigText from res://config/drone/rc/drone_config_0");
        }

        if (string.IsNullOrEmpty(controllerConfigText))
        {
            GD.PrintErr("Failed to load controllerConfigText from res://config/controller/param-api-mixer");
        }
        
        int ret = -1;
        string logpath = string.IsNullOrEmpty(debug_logpath) ? null : debug_logpath;
        ret = DroneServiceRC.InitSingle(droneConfigText ?? "", controllerConfigText ?? "", enable_data_logger, logpath);
        
        if (enable_debuff)
        {
            DroneServiceRC.SetDebuffOnCollision(0, debuff_duration_msec);
        }
        GD.Print("InitSingle: ret = " + ret);

        if (ret != 0)
        {
            GD.PrintErr("Can not Initialize DroneService RC with InitSingle");
        }
        /*
         * Camera
         */
//        camera_controller = FindComponent<ICameraController>(this);
        camera_controller = FindNodeByInterface<ICameraController>(this);
        if (camera_controller != null)
        {
            GD.Print("Camera is enabled");
            camera_controller.Initialize();
        }

        /*
         * Leds
         */
        if (leds != null && leds.Length > 0)
        {
            foreach (var led in leds)
            {
                led.SetMode(DroneLedController.DroneMode.DISARM);
            }
        }
        if (flight_mode_leds != null && flight_mode_leds.Length > 0)
        {
            foreach (var led in flight_mode_leds)
            {
                led.SetMode(FlightModeLedController.FlightMode.GPS);
            }
        }
        /*
         * Propeller Winds
         */
        if (propeller_winds != null && propeller_winds.Length > 0)
        {
            foreach (var wind in propeller_winds)
            {
                wind.SetWindVelocityFromRos(Vector3.Zero);
            }
        }
        
        // DroneServiceRC.Startの呼び出し
        ret = DroneServiceRC.Start();
        GD.Print("Start: ret = " + ret);

        if (ret != 0)
        {
            GD.PrintErr("Can not Start DroneService RC");
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

    private string LoadTextFromResources(string resourcePath)
    {
        if (!FileAccess.FileExists(resourcePath)) return null;
        using var file = FileAccess.Open(resourcePath, FileAccess.ModeFlags.Read);
        return file?.GetAsText();
    }

    public override void _Process(double delta)
    {
        if (ibridge != null && ibridge.GetState() == BridgeState.POSITIONING)
        {
            return;
        }
        if (drone_control != null)
        {
            drone_control.HandleInput();
            if (camera_controller != null)
            {
                drone_control.HandleCameraControl(camera_controller, null);
            }
        }
    }

    private bool isGrabProcessing = false;
    private bool isReleaseProcessing = false;
    private bool isGrabbed = false;
    private async Task GrabControlAsync()
    {
        var pdu_manager = ARBridge.Instance.Get();
        if (pdu_manager == null || baggage_grabber == null || sharesim_client == null)
        {
            return;
        }

        if (drone_control.IsMagnetOn())
        {
            if (!isGrabbed)
            {
                if (!isGrabProcessing) 
                {
                    isGrabProcessing = true;
                    var result = await baggage_grabber.RequestGrab(sharesim_client.GetOwnerId(), pdu_manager);

                    if (result == BaggageGrabber.GrabResult.Success)
                    {
                        isGrabbed = true; 
                    }
                    isGrabProcessing = false; 
                }
            }
        }
        else
        {
            if (isGrabbed)
            {
                if (!isReleaseProcessing) 
                {
                    isReleaseProcessing = true;
                    var result = await baggage_grabber.RequestRelease(sharesim_client.GetOwnerId(), pdu_manager);
                    isReleaseProcessing = false; 

                    if (result == BaggageGrabber.ReleaseResult.Success)
                    {
                        isGrabbed = false; 
                    }
                }
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        DroneServiceRC.PutDisturbance(0, temperature, (float)rosWind.X, (float)rosWind.Y, (float)rosWind.Z);
        // 現在位置を記録
        for (int i = 0; i < 20; i++)
        {
            DroneServiceRC.Run();
        }

        if (body == null) return;

        double x, y, z;
        int ret = DroneServiceRC.GetPosition(0, out x, out y, out z);
        if (ret == 0)
        {
            Vector3 unity_pos = new Vector3();
            unity_pos.Z = (float)x;
            unity_pos.X = -(float)y;
            unity_pos.Y = (float)z;
            body.Position = unity_pos;
            if (useWebServer)
            {
                FlushPduPos(unity_pos);
            }
        }
        
        double roll, pitch, yaw;
        ret = DroneServiceRC.GetAttitude(0, out roll, out pitch, out yaw);
        if (ret == 0)
        {
            // Use radians directly for FromEuler
            body.Quaternion = Quaternion.FromEuler(new Vector3((float)pitch, -(float)yaw, -(float)roll));
        }

        double c1, c2, c3, c4, c5, c6, c7, c8;
        ret = DroneServiceRC.GetControls(0, out c1, out c2, out c3, out c4, out c5, out c6, out c7, out c8);
        if (ret == 0)
        {
            drone_propeller.Rotate((float)c1, (float)c2, (float)c3, (float)c4);
            if (useWebServer)
            {
                FlushPduPropeller((float)c1, (float)c2, (float)c3, (float)c4);
            }
        }
        
        if (baggage_grabber != null)
        {
            // Note: FixedUpdate was async in original, Godot _PhysicsProcess is not easily async
            // but we can call an async task. 
            _ = GrabControlAsync();
        }
        
        /*
         * Leds
         */
        int internal_state;
        int flight_mode;
        DroneServiceRC.GetInternalState(0, out internal_state);
        DroneServiceRC.GetFlightMode(0, out flight_mode);
        if (leds != null && leds.Length > 0)
        {
            if (c1 > 0)
            {
                foreach (var led in leds)
                {
                    switch (internal_state)
                    {
                        case 0: led.SetMode(DroneLedController.DroneMode.TAKEOFF); break;
                        case 1: led.SetMode(DroneLedController.DroneMode.HOVER); break;
                        case 2: led.SetMode(DroneLedController.DroneMode.LANDING); break;
                    }
                }
            }
            else
            {
                foreach (var led in leds)
                {
                    led.SetMode(DroneLedController.DroneMode.DISARM);
                }
            }
        }
        
        if (flight_mode_leds != null && flight_mode_leds.Length > 0)
        {
            foreach (var led in flight_mode_leds)
            {
                if (flight_mode == 0) led.SetMode(FlightModeLedController.FlightMode.ATTI);
                else led.SetMode(FlightModeLedController.FlightMode.GPS);
            }
        }
        
        /*
         * Propeller wind
         */
        if (propeller_winds != null && propeller_winds.Length > 0)
        {
            Vector3 rosWindOut = Vector3.Zero;
            DroneServiceRC.GetPropellerWind(0, out rosWindOut);
            foreach (var wind in propeller_winds)
            {
                wind.SetWindVelocityFromRos(rosWindOut);
            }
        }
        
        if (camera_controller != null)
        {
            camera_controller.UpdateCameraAngle();
        }
    }

    public override void _ExitTree()
    {
        int ret = DroneServiceRC.Stop();
        GD.Print("Stop: ret = " + ret);
    }

    private Godot.Vector3 UnityToRos(Godot.Vector3 unityVec)
    {
        return new Godot.Vector3(
            unityVec.X,
            -unityVec.Z,
            unityVec.Y
        );
    }

    public void ApplyDisturbance(float temp, Godot.Vector3 windVector)
    {
        GD.Print("ApplyDisturbance: " + windVector);
        rosWind = UnityToRos(windVector);
        temperature = temp;
    }

    public void ResetDisturbance()
    {
        GD.Print("ResetDisturbance: ");
        rosWind = Vector3.Zero;
        temperature = 20;
    }
    
    private Godot.Vector3 rosWind;
    private double temperature;
}