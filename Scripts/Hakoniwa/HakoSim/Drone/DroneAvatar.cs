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

        // ★ 2026-10-18: 箱庭（プラント）が居なければ、Godot の中の hakodrone（内蔵 FC ＋ MuJoCo）で飛ばす
        //   （hakoniwa-godot-drone-courses のオフライン経路を移したもの）。機体は courses_drone（Origin-01 相当の 4 発）
        //   なので、Origin-01 1 機の場面だけで有効にする。無効の場面は箱庭が立つまで待って繋ぐ（HakoAsset）。
        [Export]
        public bool enableOfflineMode = false;
        private bool isOfflineMode = false;
        private bool isInitialized = false;
        private DroneControl localDroneControl = null;
        private double offlineTimer = 0;
        private float offline_last_propeller_rotation = 0f;
        private double offline_c1, offline_c2, offline_c3, offline_c4, offline_c5, offline_c6, offline_c7, offline_c8;
        private int offline_internal_state = 0;
        private int offline_flight_mode = 0;
        private hakoniwa.drone.service.DroneServiceRC.BatteryStatus offline_battery_status;
        private ICameraController offlineCameraController;
        private Godot.Vector3 lastPosition = Godot.Vector3.Zero;

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
        private float current_propeller_rotation = -1f;
        private int current_internal_state = -1;
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
                    // （8 発機 drone3 のように 4 発でない機体がある）。
                    int n = Math.Min(drone_propeller.RotorCount, propeller.controls.Length);
                    float[] controls = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        controls[i] = (float)propeller.controls[i];
                    }
                    drone_propeller.Rotate(controls);
                    propellerRotation = (n > 0) ? controls[0] : 0;
                    current_propeller_rotation = propellerRotation;
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
                current_internal_state = drone_status.internal_state;
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
        public bool enableLerp = true;
        [Export]
        public float lerpSpeed = 25.0f;

        private Godot.Vector3 target_unity_pos = Godot.Vector3.Zero;
        private Godot.Quaternion target_unity_rot = Godot.Quaternion.Identity;
        private bool has_target_pos = false;
        private Godot.Vector3 initialScale = Godot.Vector3.One;

        public override void _Ready()
        {
            if (this.Scale != Godot.Vector3.Zero)
            {
                initialScale = this.Scale;
            }
            // ★ 2026-10-18: 箱庭（プラント）を待つ間は EventInitialize が呼ばれないので、ここでも固定しておく。
            //   固定しないと RigidBody3D だけが重力で落ち、繋がったあと当たり判定が機体からずれる。
            if (rd != null)
            {
                rd.Freeze = true;
                rd.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            }
        }

        private void UpdatePosition(Twist pos)
        {
            // 1. 位置は元の通り (Godotの-Z前方に合わせる)
            target_unity_pos = new Godot.Vector3(-(float)pos.linear.y, (float)pos.linear.z, -(float)pos.linear.x);

            // 2. 回転の各軸を個別に作成 (Unityの符号反転論理を適用)
            var qRoll  = Godot.Quaternion.FromEuler(new Godot.Vector3(0, 0, -(float)pos.angular.x));
            var qPitch = Godot.Quaternion.FromEuler(new Godot.Vector3(-(float)pos.angular.y, 0, 0)); 
            var qYaw   = Godot.Quaternion.FromEuler(new Godot.Vector3(0, (float)pos.angular.z, 0));

            // 3. Unityと同じ ZXY 順序 (Roll -> Pitch -> Yaw) で合成
            target_unity_rot = qYaw * qPitch * qRoll;

            if (!has_target_pos)
            {
                this.GlobalPosition = target_unity_pos;
                this.GlobalBasis = new Basis(target_unity_rot).Scaled(initialScale);
                has_target_pos = true;
            }
            else if (!enableLerp)
            {
                this.GlobalPosition = target_unity_pos;
                this.GlobalBasis = new Basis(target_unity_rot).Scaled(initialScale);
            }
        }

        public override void _Process(double delta)
        {
            if (has_target_pos && enableLerp)
            {
                float step = Mathf.Clamp((float)delta * lerpSpeed, 0f, 1f);
                this.GlobalPosition = this.GlobalPosition.Lerp(target_unity_pos, step);
                var currentRot = this.GlobalBasis.GetRotationQuaternion();
                var slerpedRot = currentRot.Slerp(target_unity_rot, step);
                this.GlobalBasis = new Basis(slerpedRot).Scaled(initialScale);
            }
        }

        public double get_full_voltage() => isOfflineMode ? offline_battery_status.FullVoltage : (battery_status?.full_voltage ?? 0);
        public double get_curr_voltage() => isOfflineMode ? offline_battery_status.CurrentVoltage : (battery_status?.curr_voltage ?? 0);
        public uint get_status() => isOfflineMode ? offline_battery_status.Status : (battery_status?.status ?? 0);
        public uint get_cycles() => isOfflineMode ? offline_battery_status.ChargeCycles : (battery_status?.cycles ?? 0);
        public double get_temperature() => isOfflineMode ? offline_battery_status.CurrentTemperature : (battery_status?.curr_temp ?? 0);

        public override void _PhysicsProcess(double delta)
        {
            if (!isInitialized)
            {
                offlineTimer += delta;
                if (offlineTimer < 0.5) return;

                // 箱庭が居るかは HakoAsset が決める（mmap-0xff.bin の有無と登録の結果）。繋ぎにいっている間は待つ。
                var asset = HakoAsset.Instance;
                var state = asset?.Connection ?? HakoAsset.ConnectionState.Failed;
                if (state == HakoAsset.ConnectionState.Connecting) return;
                isInitialized = true;
                if (state == HakoAsset.ConnectionState.Ready) return;   // オンライン（箱庭経由）
                if (enableOfflineMode && StartOfflineMode())
                {
                    asset?.CancelWaitForPlant();   // オフラインで動くので、あとから来た箱庭には繋がない
                }
            }

            if (isOfflineMode)
            {
                UpdateOfflineSimulation();
            }
        }

        private void ResolveComponentsForOffline()
        {
            drone_propeller = NodeUtil.FindNodeByInterface<DronePropeller>(this);
            drone_collision = droneCollisionNode as DroneCollision;
            touchSensor = NodeUtil.FindNodeByInterface<TouchSensor>(this);
            gameController = GameController.Instance ?? NodeUtil.FindNodeByInterface<GameController>(this);
            cameraController = NodeUtil.FindNodeByInterface<CameraController>(this);
            // カメラの上下は ICameraController そのものに渡す（CameraController は中の制御を外に出さない）
            offlineCameraController = NodeUtil.FindNodeByInterface<ICameraController>(this);
            baggageGrabber = NodeUtil.FindNodeByInterface<BaggageGrabber>(this);
            droneConfig = NodeUtil.FindNodeByInterface<DroneConfig>(this);
            lidars = FindComponents<ILiDAR3DController>();
            wind = NodeUtil.FindNodeByInterface<Wind>(this);
            if (drone_collision != null)
            {
                drone_collision.SetIndex(0);
            }
            if (drone_propeller == null)
            {
                GD.PrintErr("DroneAvatar Error: DronePropeller not found for offline mode!");
            }
        }

        // hakodrone を開けたら true。開けなければ（DLL や機体の定義が無いなど）オフラインにしない。
        private bool StartOfflineMode()
        {
            GD.Print("DroneAvatar: 箱庭が居ないので、hakodrone（res://SimModels/courses_drone）でローカルに動かします");
            int ret;
            try
            {
                ret = hakoniwa.drone.service.DroneServiceRC.Open();
            }
            catch (Exception e)
            {
                GD.PrintErr($"DroneAvatar: hakodrone を開けない（{e.Message}）。tools/fetch_native.ps1 で取得したか確かめること");
                return false;
            }
            if (ret != 0)
            {
                GD.PrintErr($"DroneAvatar: hakodrone を開けない（Open ret = {ret}）");
                return false;
            }
            hakoniwa.drone.service.DroneServiceRC.Start();
            isOfflineMode = true;

            ResolveComponentsForOffline();

            localDroneControl = NodeUtil.FindNodeByInterface<DroneControl>(this);
            if (localDroneControl == null)
            {
                var droneControlNode = new DroneControl();
                droneControlNode.Name = "DroneControl";
                droneControlNode.robotName = this.robotName;
                var xrInterface = XRServer.FindInterface("OpenXR");
                bool isXrActive = (xrInterface != null && xrInterface.IsInitialized()) || OS.GetName() == "Android" || HakoDroneXrInputManager.Instance != null;
                droneControlNode.input_type = isXrActive ? DroneControlInputType.Xr : DroneControlInputType.PS4;
                droneControlNode.isPlayer = true;
                AddChild(droneControlNode);
                localDroneControl = droneControlNode;
            }

            if (rd != null)
            {
                rd.Freeze = true;
            }
            return true;
        }

        private void UpdateOfflineSimulation()
        {
            if (localDroneControl != null)
            {
                localDroneControl.HandleInput();
                // カメラの上下（PDU は無いので null。Y の撮影は動かない）。UpdateCameraAngle がカメラを機体に付けて動かす
                if (offlineCameraController != null)
                {
                    localDroneControl.HandleCameraControl(offlineCameraController, null);
                    offlineCameraController.UpdateCameraAngle();
                }
            }

            // オフラインでも当たり判定を効かせる（オンラインは impulse PDU でプラントが同じ式を解く）
            if (drone_collision != null)
            {
                var col = drone_collision.GetImpulseCollision();
                if (col.collision)
                {
                    hakoniwa.drone.service.DroneServiceRC.ApplyContact(0, col);
                }
            }

            for (int i = 0; i < 20; i++)
            {
                hakoniwa.drone.service.DroneServiceRC.Run();
            }

            double x, y, z;
            int ret = hakoniwa.drone.service.DroneServiceRC.GetPosition(0, out x, out y, out z);
            if (ret == 0)
            {
                Godot.Vector3 unity_pos = new Godot.Vector3(-(float)y, (float)z, -(float)x);
                double roll, pitch, yaw;
                ret = hakoniwa.drone.service.DroneServiceRC.GetAttitude(0, out roll, out pitch, out yaw);
                if (ret == 0)
                {
                    var qRoll  = Godot.Quaternion.FromEuler(new Godot.Vector3(0, 0, -(float)roll));
                    var qPitch = Godot.Quaternion.FromEuler(new Godot.Vector3(-(float)pitch, 0, 0));
                    var qYaw   = Godot.Quaternion.FromEuler(new Godot.Vector3(0, (float)yaw, 0));
                    Godot.Quaternion targetRotation = qYaw * qPitch * qRoll;

                    this.GlobalPosition = unity_pos;
                    this.GlobalBasis = new Basis(targetRotation).Scaled(initialScale);

                    // 当たり判定の体を機体に合わせる（物理のスレッドにも直接入れる）
                    if (rd != null && GodotObject.IsInstanceValid(rd))
                    {
                        var targetTransform = new Transform3D(new Basis(targetRotation), this.GlobalPosition);
                        rd.GlobalTransform = targetTransform;
                        PhysicsServer3D.BodySetState(rd.GetRid(), PhysicsServer3D.BodyState.Transform, targetTransform);
                        rd.Sleeping = false;
                        float delta = (float)GetProcessDeltaTime();
                        if (delta > 0.0001f)
                        {
                            Godot.Vector3 velocity = (this.GlobalPosition - lastPosition) / delta;
                            rd.LinearVelocity = velocity;
                            PhysicsServer3D.BodySetState(rd.GetRid(), PhysicsServer3D.BodyState.LinearVelocity, velocity);
                        }
                        lastPosition = this.GlobalPosition;
                    }
                }
            }

            if (drone_propeller != null)
            {
                ret = hakoniwa.drone.service.DroneServiceRC.GetControls(
                    0,
                    out offline_c1, out offline_c2, out offline_c3, out offline_c4,
                    out offline_c5, out offline_c6, out offline_c7, out offline_c8);
                if (ret == 0)
                {
                    drone_propeller.Rotate((float)offline_c1, (float)offline_c2, (float)offline_c3, (float)offline_c4);
                    offline_last_propeller_rotation = (float)offline_c1;
                }
            }

            if (useBattery)
            {
                hakoniwa.drone.service.DroneServiceRC.TryGetBatteryStatus(0, out offline_battery_status);
            }
            hakoniwa.drone.service.DroneServiceRC.GetInternalState(0, out offline_internal_state);
            hakoniwa.drone.service.DroneServiceRC.GetFlightMode(0, out offline_flight_mode);

            if (leds.Length > 0)
            {
                foreach (var led in leds)
                {
                    if (offline_last_propeller_rotation <= 0.01f) { led.SetMode(DroneLedController.DroneMode.DISARM); continue; }
                    switch (offline_internal_state)
                    {
                        case 0: led.SetMode(DroneLedController.DroneMode.TAKEOFF); break;
                        case 1: led.SetMode(DroneLedController.DroneMode.HOVER); break;
                        case 2: led.SetMode(DroneLedController.DroneMode.LANDING); break;
                        default: led.SetMode(DroneLedController.DroneMode.DISARM); break;
                    }
                }
            }
            if (flight_mode_leds.Length > 0)
            {
                foreach (var led in flight_mode_leds) led.SetMode(offline_flight_mode == 0 ? FlightModeLedController.FlightMode.ATTI : FlightModeLedController.FlightMode.GPS);
            }
        }

        public override void _ExitTree()
        {
            if (isOfflineMode)
            {
                hakoniwa.drone.service.DroneServiceRC.Stop();
            }
        }

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

        public bool IsArmed()
        {
            float rot = GetPropellerRotation();
            // デモモードや未初期化状態（PDUデータがまだ来ていない）
            if (rot < 0f) {
                return true;
            }
            return rot > 0.01f;
        }

        public float GetPropellerRotation() => isOfflineMode ? offline_last_propeller_rotation : current_propeller_rotation;
        public int GetInternalState() => isOfflineMode ? offline_internal_state : current_internal_state;
    }
}
