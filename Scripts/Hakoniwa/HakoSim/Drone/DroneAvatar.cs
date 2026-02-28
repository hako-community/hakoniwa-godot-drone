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
//            drone_propeller = FindComponent<DronePropeller>(this);
            drone_propeller = FindNodeByInterface<DronePropeller>(this);
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
//            drone_collision = FindComponent<DroneCollision>(this);
//            drone_collision = FindNodeByInterface<DroneCollision>(this);
            drone_collision = droneCollisionNode as DroneCollision;

//            touchSensor = FindComponent<TouchSensor>(this);
            touchSensor = FindNodeByInterface<TouchSensor>(this);

//            gameController = FindComponent<GameController>(this);
            gameController = FindNodeByInterface<GameController>(this);

//            cameraController = FindComponent<CameraController>(this);
            cameraController = FindNodeByInterface<CameraController>(this);

//            baggageGrabber = FindComponent<BaggageGrabber>(this);
            baggageGrabber = FindNodeByInterface<BaggageGrabber>(this);

//            droneConfig = FindComponent<DroneConfig>(this);
            droneConfig = FindNodeByInterface<DroneConfig>(this);

            lidars = FindComponents<ILiDAR3DController>();

//            wind = FindComponent<Wind>(this);
            wind = FindNodeByInterface<Wind>(this);

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
                foreach(var lidar in lidars) lidar.DoInitialize(robotName, hakoPdu.GetPduManager());
            }
            
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

#if false
        private List<T> FindComponents<T>(Node node) where T : class
        {
            List<T> results = new List<T>();
            if (node is T found) results.Add(found);
            foreach (Node child in node.GetChildren())
            {
                results.AddRange(FindComponents<T>(child));
            }
            return results;
        }
#else
        private List<T> FindComponents<T>() where T : class
        {
            List<T> results = new List<T>();
            var root = GetTree().Root;
            _FindComponentsRecursive(root, results);
            return results;
        }

        private void _FindComponentsRecursive<T>(Node node, List<T> results) where T : class
        {
            if (node == null) return;

            // 1. 自分自身が型 T にキャストできるかチェック
            if (node is T found)
            {
                results.Add(found);
            }

            // 2. 子ノードに対して再帰的に処理
            foreach (Node child in node.GetChildren())
            {
                _FindComponentsRecursive(child, results);
            }
        }
#endif
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
                    drone_propeller.Rotate((float)propeller.controls[0], (float)propeller.controls[1], (float)propeller.controls[2], (float)propeller.controls[3]);
                    propellerRotation = (float)propeller.controls[0];
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
