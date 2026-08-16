using System;
using System.Collections.Generic;
using hakoniwa.objects.core;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;
using hakoniwa.pdu.msgs.hako_mavlink_msgs;
using hakoniwa.pdu.msgs.hako_msgs;
using hakoniwa.sim;
using hakoniwa.sim.core;
using Godot;

namespace hakoniwa.drone.sim
{
    public partial class DroneAvatar : Node3D, IHakoObject, IDroneBatteryStatus, IMovableObject
    {
        IHakoPdu hakoPdu;
        [Export]
        public string robotName = "Drone";

        [ExportGroup("PDU Names")]
        [Export]
        public string pdu_name_propeller = "motor";
        [Export]
        public string pdu_name_pos = "pos";
        [Export]
        public string pdu_name_touch_sensor = "baggage_sensor";
        [Export]
        public string pdu_name_collision = "impulse";
        [Export]
        public string pdu_name_battery = "battery";
        [Export]
        public string pdu_name_disturbance = "disturb";
        [Export]
        public string pdu_name_status = "status";
        
        [ExportGroup("Settings")]
        public bool useBattery = true;
        [Export]
        public Node3D body;
        [Export]
        public RigidBody3D rd;
        [Export]
        public bool useTouchSensor;
        
        private TouchSensor touchSensor;

        [Export]
        public Node droneCollisionNode; // インスペクターでノードを選択
        private DroneCollision drone_collision;

        private HakoBatteryStatus battery_status;
        private CameraController cameraController;
        private BaggageGrabber baggageGrabber;
        private GameController gameController;
        private DroneConfig droneConfig;
        private List<ILiDAR3DController> lidars;
        private List<IRadar3DController> radars;
        private RadarPointCloudVisualizer radarVisualizer;
        private LiDARPointCloudVisualizer lidarVisualizer;
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

        private DronePropeller drone_propeller;

        private IPduManager cachedPduManager = null;
        private int last_internal_state = -1;
        private float last_propeller_rotation = -1f;
        private double debug_timer = 0;
        public void EventInitialize()
        {
            GD.Print("Event Initialize");
            if (body == null)
            {
                throw new Exception("Body is not assigned");
            }
            if (rd == null)
            {
                throw new Exception("Can not find rigidbody on " + this.Name);
            }
            if (rd != null)
             {
                 rd.Freeze = true;
                 rd.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
                 GD.Print("DroneAvatar: RigidBody3D set to Freeze/Kinematic");
             }
             else
             {
                 GD.PrintErr("DroneAvatar Error: RigidBody3D not found! Position updates will not work.");
             }

            // Recursive searches
            drone_propeller = NodeUtil.FindNodeByInterface<DronePropeller>(this);
             if (drone_propeller != null)
             {
                 GD.Print("DroneAvatar: DronePropeller found.");
             }
             else
             {
                 GD.PrintErr("DroneAvatar Error: DronePropeller component not found! Propeller controls will not work.");
             }
             if (drone_propeller == null)
             {
                 GD.PrintErr("DroneAvatar Error: DronePropeller component not found! Propeller controls will not work.");
             }
            if (drone_propeller == null)
            {
                GD.PrintErr("DroneAvatar Error: DronePropeller component not found! LED state will not change.");
            }
            drone_collision = droneCollisionNode as DroneCollision;

            touchSensor = NodeUtil.FindNodeByInterface<TouchSensor>(this);

            gameController = NodeUtil.FindNodeByInterface<GameController>(this);

            cameraController = NodeUtil.FindNodeByInterface<CameraController>(this);

            baggageGrabber = NodeUtil.FindNodeByInterface<BaggageGrabber>(this);

            droneConfig = NodeUtil.FindNodeByInterface<DroneConfig>(this);

            lidars = FindComponents<ILiDAR3DController>();

            // Pattern A toggle: when set, hakoniwa-mujoco-sensor produces the point
            // clouds and Godot only reads/visualizes them (no self ray cast).
            bool externalSensing = OS.GetEnvironment("HAKO_EXTERNAL_SENSING") == "1";

            // R3/R4: Godot Pattern-B radar. Use scene-provided radars if present,
            // otherwise create one programmatically (avoids editing the .tscn).
            radars = FindComponents<IRadar3DController>();
            // Pattern B (Godot's own ray-cast radar) is deprecated: sensing belongs to
            // hakoniwa-mujoco-sensor. Auto-creating it made every scene draw hundreds of
            // white 0.12m spheres plus a 30m translucent cone ("white bubble behind the
            // drone" / "screen goes blue"). Opt-in only now: HAKO_LEGACY_RADAR=1.
            bool legacyRadar = OS.GetEnvironment("HAKO_LEGACY_RADAR") == "1";
            if (legacyRadar && (radars == null || radars.Count == 0))
            {
                var radarNode = new Default3DRadarController {
                    Name = "RadarAuto",
                    HorizontalFOV = 90f, VerticalFOV = 50f, Range = 30f,
                    PointsPerSecond = 3000, UpdateRateHz = 10,
                    ExternalSensing = externalSensing
                };
                AddChild(radarNode);
                radars = new List<IRadar3DController> { radarNode };
                radarVisualizer = new RadarPointCloudVisualizer {
                    Name = "RadarVizAuto",
                    HorizontalFOV = 90f, VerticalFOV = 50f, Range = 30f
                };
                AddChild(radarVisualizer);
                // Pattern B: read the scan directly from the in-process controller.
                // Pattern A: leave source null so the visualizer reads radar_points via PDU.
                if (!externalSensing) radarVisualizer.SetSource(radarNode);
            }

            wind = NodeUtil.FindNodeByInterface<Wind>(this);

             if (touchSensor != null)
             {
                 GD.Print("TouchSensor found.");
             }
             else
             {
                 GD.Print("TouchSensor not found. Touch sensing will be disabled.");
             }

             if (drone_collision != null)
             {
                 GD.Print("collision is attached.");
             }
             else
             {
                 GD.Print("DroneCollision not found. Collision detection will be disabled.");
             }
            hakoPdu = HakoAsset.GetHakoPdu();
            
            /*
             * Position
             */
            var ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_pos);
            if (!ret) throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_pos}");
            
            /*
             * Propeller
             */
            if (drone_propeller != null)
            {
                ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_propeller);
                if (!ret) throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_propeller}");
            }
            /*
             * Battery
             */
            if (useBattery)
            {
                ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_battery);
                if (!ret) throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_battery}");
            }
            /*
             * TouchSensor
             */
            if (useTouchSensor)
            {
                ret = hakoPdu.DeclarePduForWrite(robotName, pdu_name_touch_sensor);
                if (!ret) throw new ArgumentException($"Can not declare pdu for write: {robotName} {pdu_name_touch_sensor}");
            }
            /*
             * Collision
             */
            if (drone_collision != null)
            {
                GD.Print("DroneCollision component found. Declaring collision PDU.");
                ret = hakoPdu.DeclarePduForWrite(robotName, pdu_name_collision);
                if (!ret) throw new ArgumentException($"Can not declare pdu for write: {robotName} {pdu_name_collision}");
            } else
            {
                GD.Print("DroneCollision component not found. Collision PDU will not be declared.");
            }
            
            if (gameController != null) gameController.DoInitialize(robotName, hakoPdu);
            if (cameraController != null) cameraController.DoInitialize(robotName, hakoPdu);
            if (baggageGrabber != null) baggageGrabber.DoInitialize(robotName, hakoPdu);
            if (droneConfig != null) droneConfig.LoadDroneConfig(robotName);
            
            if (lidars != null && lidars.Count > 0)
            {
                if (droneConfig != null)
                {
                    GD.Print("SetLidarPosition : "+ lidars.Count + " lidars found.");
                    droneConfig.SetLidarPosition(robotName);
                }
                foreach(var lidar in lidars)
                {
                    if (externalSensing) lidar.ExternalSensing = true;
                    if (lidar.ExternalSensing)
                    {
                        // Pattern A: hakoniwa-mujoco-sensor publishes lidar_points.
                        // Declare for READ so HakoCommunicationService.EventTick pulls
                        // it from SHM, then visualize it from the PDU.
                        ret = hakoPdu.DeclarePduForRead(robotName, Default3DLiDARController.pdu_name_lidar_point_cloud);
                        if (!ret) throw new ArgumentException($"Can not declare pdu for read: {robotName} {Default3DLiDARController.pdu_name_lidar_point_cloud}");
                        if (lidarVisualizer == null)
                        {
                            lidarVisualizer = new LiDARPointCloudVisualizer { Name = "LiDARVizAuto" };
                            // Parent under the sensor node so sensor-local points
                            // render at the drone's LiDAR mount pose.
                            (lidar as Node3D)?.AddChild(lidarVisualizer);
                            lidarVisualizer.DoInitialize(robotName, hakoPdu.GetPduManager());
                        }
                        GD.Print("LiDAR Pattern A: reading lidar_points from mujoco-sensor.");
                    }
                    lidar.DoInitialize(robotName, hakoPdu.GetPduManager());
                }
            }

            if (radars != null && radars.Count > 0)
            {
                foreach(var radar in radars)
                {
                    if (externalSensing) radar.ExternalSensing = true;
                    if (radar.ExternalSensing)
                    {
                        // Pattern A: mujoco-sensor publishes radar_points; declare for READ.
                        ret = hakoPdu.DeclarePduForRead(robotName, Default3DRadarController.pdu_name_radar_points);
                        if (!ret) throw new ArgumentException($"Can not declare pdu for read: {robotName} {Default3DRadarController.pdu_name_radar_points}");
                    }
                    // Pattern B keeps the pdudef-driven CreateNamedPdu/WriteNamedPdu path.
                    radar.DoInitialize(robotName, hakoPdu.GetPduManager());
                }
                if (radarVisualizer != null) radarVisualizer.DoInitialize(robotName, hakoPdu.GetPduManager());
                GD.Print("Radar(R3/R4) initialized: " + radars.Count + " radar(s)");
            }

            // Optional Pattern-A alignment aid: reconstruct the sensed room (env.xml
            // source OBB) in Godot's world so the point cloud can be checked against
            // the walls it was measured from. No-op unless HAKO_ENV_OBB is set.
            hakoniwa.env.EnvRoomBuilder.BuildIfRequested(GetParent());

            if (wind != null)
            {
                ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_disturbance);
                if (!ret) throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_disturbance}");
            }
            
            ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_status);
            if (!ret) throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_status}");

            // LEDs Init
            GD.Print($"DroneAvatar: Initializing {leds.Length} LEDs.");
            foreach (var led in leds) led.SetMode(DroneLedController.DroneMode.DISARM);
            foreach (var led in flight_mode_leds) led.SetMode(FlightModeLedController.FlightMode.GPS);
            foreach (var w in propeller_winds) w.SetWindVelocityFromRos(Godot.Vector3.Zero);
        }

        private List<T> FindComponents<T>() where T : class
        {
            // Search only THIS avatar's own subtree. Searching from the scene root
            // used to be fine with a single avatar, but a 2-drone view has one
            // sensor rig per avatar -- a whole-tree search made every avatar grab
            // every rig (cross-wiring + double init). Sensors are mounted on the
            // drone, so the avatar subtree is the correct scope.
            List<T> results = new List<T>();
            NodeUtil._FindComponentsRecursive(this, results);
            return results;
        }

        public void EventReset() { }
        public void EventStart() { }
        public void EventStop() { }

        private bool has_printed_pdu_names = false;
        public void EventTick()
        {
            debug_timer += GetProcessDeltaTime();
            if (this.cachedPduManager == null)
            {
                this.cachedPduManager = hakoPdu.GetPduManager();
                if (this.cachedPduManager == null) return;
            }
            var pduManager = this.cachedPduManager;

            if (!has_printed_pdu_names)
            {
                has_printed_pdu_names = true;
                GD.Print($"--- Registered PDU Channel Check for {robotName} ---");
                string[] checkNames = { pdu_name_pos, pdu_name_propeller, pdu_name_status, "status", "Drone_status" };
                foreach (var n in checkNames) {
                    try {
                        int cid = pduManager.GetChannelId(robotName, n);
                        GD.Print($"PDU Name '{n}': Channel ID = {cid}");
                    } catch (Exception) {
                        GD.Print($"PDU Name '{n}': Not Found or Error");
                    }
                }
                GD.Print($"---------------------------------------");
            }

//            /*
            if (debug_timer > 1.0)
            {
                IPdu pdu_s = pduManager.ReadPdu(robotName, pdu_name_status);
                int istate = -1;
                int fmode = -1;
                if (pdu_s != null) {
                    var ds = new DroneStatus(pdu_s);
                    istate = ds.internal_state;
                    fmode = ds.flight_mode;
                }
                IPdu pdu_p = pduManager.ReadPdu(robotName, pdu_name_pos);
                string rawPos = "N/A";
                if (pdu_p != null) {
                    var t = new Twist(pdu_p);
                    rawPos = $"({t.linear.x:F2},{t.linear.y:F2},{t.linear.z:F2})";
                }
                bool status_ok = pdu_s != null;
                long wtime = HakoAsset.Instance.GetWorldTime();
                GD.Print($"[Heartbeat] Time:{wtime} Robot:{robotName} Status:{status_ok} Rot:{last_propeller_rotation:F2} State:{istate} PDU_Pos:{rawPos} Godot_Pos:{this.GlobalPosition}");
                debug_timer = 0;
            }
//            */

            /*
             * Position
             */
            IPdu pdu_pos = pduManager.ReadPdu(robotName, pdu_name_pos);
            if (pdu_pos != null)
            {
                Twist pos = new Twist(pdu_pos);
                UpdatePosition(pos);
            }

            float propellerRotation = 0;
            if (drone_propeller != null)
            {
                IPdu pdu_propeller = pduManager.ReadPdu(robotName, pdu_name_propeller);
                if (pdu_propeller != null)
                {
                    HakoHilActuatorControls propeller = new HakoHilActuatorControls(pdu_propeller);
                    // ロータ本数は機体（シーン）依存。4 発固定にしない
                    int n = Math.Min(drone_propeller.RotorCount, propeller.controls.Length);
                    float[] controls = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        controls[i] = (float)propeller.controls[i];
                    }
                    drone_propeller.Rotate(controls);
                    propellerRotation = (n > 0) ? controls[0] : 0;
                }
            }

            if (useBattery)
            {
                IPdu pdu_battery = pduManager.ReadPdu(robotName, pdu_name_battery);
                if (pdu_battery != null) battery_status = new HakoBatteryStatus(pdu_battery);
            }

            if (touchSensor != null)
            {
                INamedPdu pdu_touch_sensor = pduManager.CreateNamedPdu(robotName, pdu_name_touch_sensor);
                var is_touched = new hakoniwa.pdu.msgs.std_msgs.Bool(pdu_touch_sensor);
                is_touched.data = touchSensor.IsTouched();
                pduManager.WriteNamedPdu(pdu_touch_sensor);
                pduManager.FlushNamedPdu(pdu_touch_sensor);
            }

            if (drone_collision != null)
            {
                var col = drone_collision.GetImpulseCollision();
                if (col.collision)
                {
                    GD.Print($"[DroneAvatar] !!! COLLISION !!! Sending impulse to {robotName}");
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

            if (gameController != null) gameController.DoControl(pduManager);
            if (cameraController != null) cameraController.DoControl(pduManager);
            if (baggageGrabber != null) baggageGrabber.DoControl(pduManager);
            if (lidars != null) foreach(var lidar in lidars) lidar.DoControl(pduManager);
            if (lidarVisualizer != null) lidarVisualizer.DoControl(pduManager);
            if (radars != null) foreach(var radar in radars) radar.DoControl(pduManager);
            if (radarVisualizer != null) radarVisualizer.DoControl(pduManager);

            if (wind != null)
            {
                IPdu pdu_disturb = pduManager.ReadPdu(robotName, pdu_name_disturbance);
                if (pdu_disturb != null)
                {
                    Disturbance disturb = new Disturbance(pdu_disturb);
                    wind.wind_direction = new Godot.Vector3(-(float)disturb.d_wind.value.y, (float)disturb.d_wind.value.z, (float)disturb.d_wind.value.x);
                    sea_level_temperature = disturb.d_temp.value;
                    sea_level_atm = disturb.d_atm.sea_level_atm;
                }
            }

            IPdu pdu_status = pduManager.ReadPdu(robotName, pdu_name_status);
            if (pdu_status != null)
            {
                DroneStatus drone_status = new DroneStatus(pdu_status);
                if (drone_status.internal_state != last_internal_state || Math.Abs(propellerRotation - last_propeller_rotation) > 0.1f)
                {
//                     GD.Print($"[DroneAvatar Log] Propeller:{propellerRotation:F2} State:{drone_status.internal_state}");
                     last_internal_state = drone_status.internal_state;
                     last_propeller_rotation = propellerRotation;
                }
                // LEDs Update based on status
                if (leds.Length > 0)
                {
                    if (propellerRotation > 0.01f)
                    {
                        foreach (var led in leds)
                        {
                            switch (drone_status.internal_state)
                            {
                                case 0: led.SetMode(DroneLedController.DroneMode.TAKEOFF); break;
                                case 1: led.SetMode(DroneLedController.DroneMode.HOVER); break;
                                case 2: led.SetMode(DroneLedController.DroneMode.LANDING); break;
                                default:
                                    // GD.Print($"DroneAvatar: Unknown internal_state: {drone_status.internal_state}");
                                    led.SetMode(DroneLedController.DroneMode.DISARM);
                                    break;
                            }
                        }
                    }
                    else foreach (var led in leds) led.SetMode(DroneLedController.DroneMode.DISARM);
                }
                
                if (flight_mode_leds.Length > 0)
                {
                    foreach (var led in flight_mode_leds) led.SetMode(drone_status.flight_mode == 0 ? FlightModeLedController.FlightMode.ATTI : FlightModeLedController.FlightMode.GPS);
                }

                if (propeller_winds.Length > 0)
                {
                    Godot.Vector3 w = new Godot.Vector3((float)drone_status.propeller_wind.x, (float)drone_status.propeller_wind.y, (float)drone_status.propeller_wind.z);
                    foreach (var p_wind in propeller_winds) p_wind.SetWindVelocityFromRos(w);
                }
            }
        }

        [Export]
        public bool enableLerp = false;
        private void UpdatePosition(Twist pos)
        {
            // 1. 位置は元の通り (Godotの-Z前方に合わせる)
            Godot.Vector3 unity_pos = new Godot.Vector3(-(float)pos.linear.y, (float)pos.linear.z, -(float)pos.linear.x);

            // 2. 回転の各軸を個別に作成 (Unityの符号反転論理を適用)
            // Pitch (angular.y) を反転させて「正 = 機首下げ」にする
            // Yaw (angular.z) と Roll (angular.x) もUnityの Euler(-yaw, -roll) に合わせる
            var qRoll  = Godot.Quaternion.FromEuler(new Godot.Vector3(0, 0, -(float)pos.angular.x));
            var qPitch = Godot.Quaternion.FromEuler(new Godot.Vector3(-(float)pos.angular.y, 0, 0)); 
            var qYaw   = Godot.Quaternion.FromEuler(new Godot.Vector3(0, (float)pos.angular.z, 0));

            // 3. Unityと同じ ZXY 順序 (Roll -> Pitch -> Yaw) で合成
            Godot.Quaternion targetRotation = qYaw * qPitch * qRoll;

            if (enableLerp)
            {
                float step = 8.0f * (float)GetProcessDeltaTime();
                this.GlobalPosition = this.GlobalPosition.Lerp(unity_pos, step);
                this.GlobalBasis = new Basis(new Godot.Quaternion(this.GlobalBasis).Slerp(targetRotation, step));
            }
            else
            {
                this.GlobalPosition = unity_pos;
                this.GlobalBasis = new Basis(targetRotation);
            }
        }

        public double get_full_voltage() => battery_status?.full_voltage ?? 0;
        public double get_curr_voltage() => battery_status?.curr_voltage ?? 0;
        public uint get_status() => battery_status?.status ?? 0;
        public uint get_cycles() => battery_status?.cycles ?? 0;
        public double get_temperature() => battery_status?.curr_temp ?? 0;

        Godot.Vector3 IMovableObject.GetPosition() => body?.GlobalPosition ?? GlobalPosition;
        Godot.Vector3 IMovableObject.GetEulerDeg() => body?.GlobalRotationDegrees ?? GlobalRotationDegrees;

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
    }
}
