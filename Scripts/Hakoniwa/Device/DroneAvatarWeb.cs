using hakoniwa.drone;
using hakoniwa.drone.sim;
using hakoniwa.objects.core;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.core;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;
using hakoniwa.pdu.msgs.hako_mavlink_msgs;
using hakoniwa.pdu.msgs.hako_msgs;
using hakoniwa.sim;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Vector3 = Godot.Vector3;

public partial class DroneAvatarWeb : Node3D, IHakoniwaWebObject, IDroneBatteryStatus
{
    [Export]
    public string robotName = "Drone";
    public string pdu_name_propeller = "motor";
    public string pdu_name_pos = "pos";
    public string pdu_name_collision = "impulse";
    public string pdu_name_disturbance = "disturb";
    public string pdu_name_status = "status";
    
    private DroneCollision drone_collision;
    [Export]
    public Node3D body;
    private DronePropeller drone_propeller;
    private DroneControl drone_control;
    
    [Export]
    public bool useBattery = true;
    public string pdu_name_battery = "battery";
    private HakoBatteryStatus battery_status;
    
    [Export]
    public DroneCameraController camera_controller;
    private DroneConfig droneConfig;
    private List<ILiDAR3DController> lidars = null;
    private Wind wind;
    
    [Export]
    public double sea_level_atm = 1.0;
    [Export]
    public double sea_level_temperature = 15.0;
    
    [Export]
    public DroneLedController[] leds = new DroneLedController[0];
    [Export]
    public FlightModeLedController[] flight_mode_leds = new FlightModeLedController[0];
    [Export]
    public PropellerWindController[] propeller_winds = new PropellerWindController[0];

    public override void _Ready()
    {
        GD.Print("DroneAvatarWeb is ready.*************");
        if (body == null)
        {
            GD.PrintErr("Body is not assigned for DroneAvatarWeb");
            body = this;
        }
        
        drone_propeller = NodeUtil.FindNodeByInterface<DronePropeller>(this);
        if (drone_propeller == null)
        {
            GD.PrintErr("Can not found drone propeller");
        }
        else 
        {
            GD.Print("max rotation : " + drone_propeller.maxRotationSpeed);
        }

        drone_control = NodeUtil.FindNodeByInterface<DroneControl>(this);
        if (drone_control == null)
        {
            GD.Print("not found DroneControl");
        }
        
        droneConfig = NodeUtil.FindNodeByInterface<DroneConfig>(this);
        if (droneConfig != null)
        {
            droneConfig.LoadDroneConfig(robotName);
        } else
        {
            GD.Print("not found DroneConfig");
        }
        
        drone_collision = NodeUtil.FindNodeByInterface<DroneCollision>(this);
        if (drone_collision != null)
        {
            GD.Print("collision is attached.");
        } else
        {
            GD.Print("not found DroneCollision");
        }
    }

    private float[] prev_controls = new float[4];
    private IPduManager pduManager;

    public override void _PhysicsProcess(double delta)
    {
        if (pduManager == null)
        {
            pduManager = WebServerBridge.Instance.Get();
            if (pduManager == null) return;
        }

        /*
         * Position
         */
        IPdu pdu_pos = pduManager.ReadPdu(robotName, pdu_name_pos);
        if (pdu_pos != null)
        {
            Twist pos = new Twist(pdu_pos);
            UpdatePosition(pos);

            if (lidars == null)
            {
                var local_lidars_list = FindComponents<ILiDAR3DController>();
                if (local_lidars_list.Count > 0)
                {
                    if (droneConfig != null)
                    {
                        droneConfig.SetLidarPosition(robotName);
                    }
                    foreach (var lidar in local_lidars_list)
                    {
                        lidar.DoInitialize(robotName, pduManager);
                    }
                    lidars = local_lidars_list;
                }
            }
        }

        /*
         * Propeller
         */
        float propellerRotation = 0;
        IPdu pdu_propeller = pduManager.ReadPdu(robotName, pdu_name_propeller);
        if (pdu_propeller == null)
        {
            if (drone_propeller != null)
                drone_propeller.Rotate(prev_controls[0], prev_controls[1], prev_controls[2], prev_controls[3]);
        }
        else
        {
            HakoHilActuatorControls propeller = new HakoHilActuatorControls(pdu_propeller);
            for (int i = 0; i < 4; i++)
            {
                prev_controls[i] = (float)propeller.controls[i];
            }
            if (drone_propeller != null)
                drone_propeller.Rotate(prev_controls[0], prev_controls[1], prev_controls[2], prev_controls[3]);
            propellerRotation = prev_controls[0];
        }
        /*
         * Battery
         */
        if (useBattery)
        {
            IPdu pdu_battery = pduManager.ReadPdu(robotName, pdu_name_battery);
            if (pdu_battery != null)
            {
                battery_status = new HakoBatteryStatus(pdu_battery);
            }
        }
        /*
         * Camera
         */
        if (camera_controller != null)
        {
            camera_controller.DoControl(pduManager);
        }
        /*
         * LiDAR
         */
        if (lidars != null)
        {
            foreach (var lidar in lidars)
            {
                lidar.DoControl(pduManager);
            }
        }
        /*
         * Collision
         */
        if (drone_collision != null)
        {
            var col = drone_collision.GetImpulseCollision();
            if (col.collision)
            {
                INamedPdu pdu_col = pduManager.CreateNamedPdu(robotName, pdu_name_collision);
                var impulseCollision = new ImpulseCollision(pdu_col);
                impulseCollision.collision = true;
                impulseCollision.is_target_static = col.isTargetStatic;
                impulseCollision.restitution_coefficient = col.restitutionCoefficient;
                impulseCollision.self_contact_vector.x = col.selfContactVector.X;
                impulseCollision.self_contact_vector.y = col.selfContactVector.Y;
                impulseCollision.self_contact_vector.z = col.selfContactVector.Z;
                impulseCollision.normal.x = col.normal.X;
                impulseCollision.normal.y = col.normal.Y;
                impulseCollision.normal.z = col.normal.Z;
                impulseCollision.target_contact_vector.x = col.targetContactVector.X;
                impulseCollision.target_contact_vector.y = col.targetContactVector.Y;
                impulseCollision.target_contact_vector.z = col.targetContactVector.Z;
                impulseCollision.target_velocity.x = col.targetVelocity.X;
                impulseCollision.target_velocity.y = col.targetVelocity.Y;
                impulseCollision.target_velocity.z = col.targetVelocity.Z;
                impulseCollision.target_angular_velocity.x = col.targetAngularVelocity.X;
                impulseCollision.target_angular_velocity.y = col.targetAngularVelocity.Y;
                impulseCollision.target_angular_velocity.z = col.targetAngularVelocity.Z;
                impulseCollision.target_euler.x = col.targetEuler.X;
                impulseCollision.target_euler.y = col.targetEuler.Y;
                impulseCollision.target_euler.z = col.targetEuler.Z;
                impulseCollision.target_inertia.x = col.targetInertia.X;
                impulseCollision.target_inertia.y = col.targetInertia.Y;
                impulseCollision.target_inertia.z = col.targetInertia.Z;
                impulseCollision.target_mass = col.targetMass;
                pduManager.WriteNamedPdu(pdu_col);
                pduManager.FlushNamedPdu(pdu_col);
            }
        }
        /*
         * Disturbance
         */
        if (wind != null)
        {
            IPdu pdu_disturb = pduManager.ReadPdu(robotName, pdu_name_disturbance);
            if (pdu_disturb != null)
            {
                Disturbance disturb = new Disturbance(pdu_disturb);
                wind.wind_direction = new Vector3(-(float)disturb.d_wind.value.y, (float)disturb.d_wind.value.z, (float)disturb.d_wind.value.x);
                sea_level_temperature = disturb.d_temp.value;
                sea_level_atm = disturb.d_atm.sea_level_atm;
            }
        }
        /*
         * Drone Status
         */
        IPdu pdu_status = pduManager.ReadPdu(robotName, pdu_name_status);
        if (pdu_status != null)
        {
            DroneStatus drone_status = new DroneStatus(pdu_status);
            if (leds != null && leds.Length > 0)
            {
                if (propellerRotation > 0)
                {
                    foreach (var led in leds)
                    {
                        switch (drone_status.internal_state)
                        {
                            case 0: led.SetMode(DroneLedController.DroneMode.TAKEOFF); break;
                            case 1: led.SetMode(DroneLedController.DroneMode.HOVER); break;
                            case 2: led.SetMode(DroneLedController.DroneMode.LANDING); break;
                        }
                    }
                }
                else foreach (var led in leds) led.SetMode(DroneLedController.DroneMode.DISARM);
            }
            if (flight_mode_leds != null && flight_mode_leds.Length > 0)
            {
                foreach (var led in flight_mode_leds) led.SetMode(drone_status.flight_mode == 0 ? FlightModeLedController.FlightMode.ATTI : FlightModeLedController.FlightMode.GPS);
            }
            if (propeller_winds != null && propeller_winds.Length > 0)
            {
                Vector3 w = new Vector3((float)drone_status.propeller_wind.x, (float)drone_status.propeller_wind.y, (float)drone_status.propeller_wind.z);
                foreach (var p_wind in propeller_winds) p_wind.SetWindVelocityFromRos(w);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (drone_control != null)
        {
            drone_control.HandleInput();
            if (pduManager != null && camera_controller != null)
            {
                drone_control.HandleCameraControl(camera_controller.GetCameraController(), pduManager);
            }
        }
    }

    private void UpdatePosition(Twist pos)
    {
        Vector3 unity_pos = new Vector3(-(float)pos.linear.y, (float)pos.linear.z, (float)pos.linear.x);
        if (body != null) body.GlobalPosition = unity_pos;

        float roll = (float)pos.angular.x;
        float pitch = (float)pos.angular.y;
        float yaw = (float)pos.angular.z;

        if (body != null)
            body.Quaternion = Godot.Quaternion.FromEuler(new Vector3(pitch, -yaw, -roll));
    }

    public async Task DeclarePduAsync()
    {
        var p_manager = WebServerBridge.Instance.Get();
        if (p_manager == null) throw new Exception("Can not get Pdu Manager");
        
        await p_manager.DeclarePduForRead(robotName, pdu_name_pos);
        await p_manager.DeclarePduForRead(robotName, pdu_name_propeller);
        if (drone_collision != null) await p_manager.DeclarePduForWrite(robotName, pdu_name_collision);
        if (useBattery) await p_manager.DeclarePduForRead(robotName, pdu_name_battery);

        foreach (Node child in GetChildren())
        {
            var subObjects = FindComponents<IHakoniwaWebObject>();
            foreach (var obj in subObjects)
            {
                if (obj != (IHakoniwaWebObject)this) await obj.DeclarePduAsync();
            }
        }
        
        if (camera_controller != null) camera_controller.GetCameraController().DelclarePdu(robotName, p_manager);
        
        var local_lidars_test = NodeUtil.FindNodeByInterface<ILiDAR3DController>(this);
        if (local_lidars_test != null)
        {
            await p_manager.DeclarePduForWrite(robotName, "lidar_pos");
            await p_manager.DeclarePduForWrite(robotName, "lidar_point_cloud");
        }
        
        var wind = NodeUtil.FindNodeByInterface<Wind>(this);
        if (wind != null) await p_manager.DeclarePduForRead(robotName, pdu_name_disturbance);
        
        await p_manager.DeclarePduForRead(robotName, pdu_name_status);

        foreach (var led in leds) led.SetMode(DroneLedController.DroneMode.DISARM);
        foreach (var led in flight_mode_leds) led.SetMode(FlightModeLedController.FlightMode.GPS);
        foreach (var p_wind in propeller_winds) p_wind.SetWindVelocityFromRos(Vector3.Zero);
    }

    public double get_full_voltage() => battery_status?.full_voltage ?? 0;
    public double get_curr_voltage() => battery_status?.curr_voltage ?? 0;
    public uint get_status() => battery_status?.status ?? 0;
    public uint get_cycles() => battery_status?.cycles ?? 0;
    public double get_temperature() => battery_status?.curr_temp ?? 0;

    [Export]
    public double Altitude = 121.321;

    public double get_atmospheric_pressure()
    {
        return AtmosphericPressure.PascalToAtm(
            AtmosphericPressure.ConvertAltToBaro(
                Altitude + GlobalPosition.Y,
                sea_level_atm,
                sea_level_temperature));
    }

    private List<T> FindComponents<T>() where T : class
    {
        List<T> results = new List<T>();
        var root = GetTree().Root;
        NodeUtil._FindComponentsRecursive(root, results);
        return results;
    }

}
