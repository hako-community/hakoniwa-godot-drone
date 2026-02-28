using hakoniwa.drone.service;
using hakoniwa.objects.core;
using hakoniwa.pdu.msgs.geometry_msgs;
using hakoniwa.pdu.msgs.hako_msgs;
using System;
using System.Reflection;
using Godot;

namespace hakoniwa.drone
{
    public partial class DronePlayer : Node, IDroneBatteryStatus, ISimTime, IMovableObject
    {
        [Export]
        public Node3D body;
        [Export]
        public int debuff_duration_msec = 100;
        private DroneCollision my_collision;
        private DroneControl drone_control;
        private DronePropeller drone_propeller;
        [Export]
        public bool enable_debuff = false;

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
        
        [Export]
        public DroneLedController[] leds = new DroneLedController[0];
        [Export]
        public FlightModeLedController[] flight_mode_leds = new FlightModeLedController[0];
        [Export]
        public PropellerWindController[] propeller_winds = new PropellerWindController[0];

        private void SetPosition(Twist pos, Godot.Vector3 unity_pos, Godot.Vector3 unity_rot)
        {
            pos.linear.x = unity_pos.Z;
            pos.linear.y = -unity_pos.X;
            pos.linear.z = unity_pos.Y;

            // unity_rot in Godot is in radians if coming from Rotation.
            // If it's RotationDegrees, we need to convert.
            pos.angular.x = -unity_rot.Z;
            pos.angular.y = unity_rot.X;
            pos.angular.z = -unity_rot.Y;
        }

        public override void _Ready()
        {
//            my_collision = FindComponent<DroneCollision>(this);
            my_collision = FindNodeByInterface<DroneCollision>(this);
            if (my_collision != null)
            {
                my_collision.SetIndex(0);
            }
//            drone_control = FindComponent<DroneControl>(this);
            drone_control = FindNodeByInterface<DroneControl>(this);
            if (drone_control == null)
            {
                throw new Exception("Can not found drone control");
            }
//            drone_propeller = FindComponent<DronePropeller>(this);
            drone_propeller = FindNodeByInterface<DronePropeller>(this);
            if (drone_propeller == null)
            {
                GD.Print("Can not found drone propeller");
            }

            string droneConfigText = LoadTextFromResources("res://config/drone/drone_config_0.json"); // Assuming .json extension
            if (string.IsNullOrEmpty(droneConfigText))
            {
                // Try without extension or with .txt
                droneConfigText = LoadTextFromResources("res://config/drone/drone_config_0");
            }

//            string filename = "param-api-mixer";
            string filename = "param-api-mixer.txt";
            string controllerConfigText = LoadTextFromResources("res://config/controller/" + filename);

            if (string.IsNullOrEmpty(droneConfigText))
            {
                GD.PrintErr("Failed to load droneConfigText from res://config/drone/rc/drone_config_0");
                // For development, we might not want to throw if we're just testing the build
            }

            if (string.IsNullOrEmpty(controllerConfigText))
            {
                GD.PrintErr("Failed to load controllerConfigText from res://config/controller/" + filename);
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
                GD.PrintErr("Can not Initialize DroneService RC with InitSingle: debug_logpath= " + debug_logpath);
            }

            /*
             * Leds
             */
            if (leds != null)
            {
                foreach (var led in leds)
                {
                    led.SetMode(DroneLedController.DroneMode.DISARM);
                }
            }
            if (flight_mode_leds != null)
            {
                foreach (var led in flight_mode_leds)
                {
                    led.SetMode(FlightModeLedController.FlightMode.GPS);
                }
            }
            /*
             * Propeller Winds
             */
            if (propeller_winds != null)
            {
                foreach (var wind in propeller_winds)
                {
                    wind.SetWindVelocityFromRos(Godot.Vector3.Zero);
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
            if (drone_control != null)
            {
                drone_control.HandleInput();
            }
        }

        public override void _PhysicsProcess(double delta)
        {
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
                Godot.Vector3 unity_pos = new Godot.Vector3();
                unity_pos.Z = (float)x;
                unity_pos.X = -(float)y;
                unity_pos.Y = (float)z;
                body.Position = unity_pos;
            }
            
            double roll, pitch, yaw;
            ret = DroneServiceRC.GetAttitude(0, out roll, out pitch, out yaw);
            if (ret == 0)
            {
                // In Godot, rotation is in radians.
                // Standard mapping for Godot (Y-up, Right-handed)
                body.Quaternion = Godot.Quaternion.FromEuler(new Godot.Vector3((float)pitch, (float)yaw, (float)roll));
            }

            float propellerRotation = 0;
            if (drone_propeller != null)
            {
                double c1, c2, c3, c4, c5, c6, c7, c8;
                ret = DroneServiceRC.GetControls(0, out c1, out c2, out c3, out c4, out c5, out c6, out c7, out c8);
                if (ret == 0)
                {
                    drone_propeller.Rotate((float)c1, (float)c2, (float)c3, (float)c4);
                }
                propellerRotation = (float)c1;
            }
            RunBatteryStatus();
            
            /*
             * Leds
             */
            int internal_state;
            int flight_mode;
            DroneServiceRC.GetInternalState(0, out internal_state);
            DroneServiceRC.GetFlightMode(0, out flight_mode);
            
            if (leds != null && leds.Length > 0)
            {
                if (propellerRotation > 0)
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
                Godot.Vector3 rosWind = Godot.Vector3.Zero;
                DroneServiceRC.GetPropellerWind(0, out rosWind);
                foreach (var wind in propeller_winds)
                {
                    wind.SetWindVelocityFromRos(rosWind);
                }
            }
        }
        
        private hakoniwa.drone.service.DroneServiceRC.BatteryStatus battery_status;
        private void RunBatteryStatus()
        {
            var ret = DroneServiceRC.TryGetBatteryStatus(0, out battery_status);
            if (!ret)
            {
                //GD.PushWarning("Can not read battery status");
            }
        }

        public override void _ExitTree()
        {
            int ret = DroneServiceRC.Stop();
            GD.Print("Stop: ret = " + ret);
        }

        public double get_full_voltage() => battery_status.FullVoltage;
        public double get_curr_voltage() => battery_status.CurrentVoltage;
        public uint get_status() => battery_status.Status;
        public uint get_cycles() => battery_status.ChargeCycles;
        public double get_temperature() => battery_status.CurrentTemperature;

        public long GetWorldTime() => (long)DroneServiceRC.GetTimeUsec(0);

        public Godot.Vector3 GetPosition() => body != null ? body.Position : Godot.Vector3.Zero;
        public Godot.Vector3 GetEulerDeg() => body != null ? body.RotationDegrees : Godot. Vector3.Zero;

        public double get_atmospheric_pressure() => 1.0;
    }
}