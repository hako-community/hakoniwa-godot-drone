using System.Collections.Generic;
using System.IO;
using hakoniwa.objects.core.sensors;
using Newtonsoft.Json;
using Godot;

namespace hakoniwa.drone
{
    public partial class DroneConfig : Node
    {
        private string audioPath;
        [System.Serializable]
        public class DroneConfigData
        {
            public Dictionary<string, DroneDetails> drones;
        }

        [System.Serializable]
        public class DroneDetails
        {
            public string audio_rotor_path;
            public Dictionary<string, DroneLidarDetails> LiDARs;
        }
        [System.Serializable]
        public class DroneLidarDetails
        {
            public bool Enabled;
            public int NumberOfChannels;
            public int RotationsPerSecond;
            public int PointsPerSecond;
            public float VerticalFOVUpper;
            public float VerticalFOVLower;
            public float HorizontalFOVStart;
            public float HorizontalFOVEnd;
            public bool DrawDebugPoints;
            public float MaxDistance;
            public float X;
            public float Y;
            public float Z;
            public float Roll;
            public float Pitch;
            public float Yaw;
        }
        private DroneConfigData loadedData = null;
        [Export]
//        public string drone_config_path = "./drone_config.json";
        public string drone_config_path = "drone_config.json";

        private List<T> FindComponents<T>() where T : class
        {
            List<T> results = new List<T>();
            var root = GetTree().Root;
            NodeUtil._FindComponentsRecursive(root, results);
            return results;
        }

        public void LoadDroneConfig(string droneName)
        {
//            string filePath = drone_config_path;
            string filePath = ProjectSettings.GlobalizePath(drone_config_path);
            GD.Print("Looking for config file at: " + filePath);

            if (File.Exists(filePath))
            {
                string dataAsJson = File.ReadAllText(filePath);
                loadedData = JsonConvert.DeserializeObject<DroneConfigData>(dataAsJson);

                if (loadedData != null && loadedData.drones != null)
                {
                    if (loadedData.drones.ContainsKey(droneName))
                    {
                        audioPath = loadedData.drones[droneName].audio_rotor_path;
                        GD.Print("Audio Path for " + droneName + ": " + audioPath);
                    }
                    else
                    {
                        GD.PrintErr("Drone configuration for " + droneName + " not found.");
                    }
                }
                else
                {
                    GD.PrintErr("Drone configurations are missing or corrupt. Check JSON structure.");
                }
            }
            else
            {
                GD.PrintErr("Cannot find drone_config.json file at: " + filePath);
            }
        }
        private bool GetParam(string droneName, string name, out LiDAR3DParams param)
        {
            param = new LiDAR3DParams();
            if (loadedData == null)
            {
                GD.PrintErr("Drone configuration data not loaded. Cannot get parameters.");
                return false;
            }
            if (loadedData.drones.ContainsKey(droneName))
            {
                if (loadedData.drones[droneName].LiDARs.ContainsKey(name))
                {
                    GD.Print("found param: " + name);
                    param.Enabled = loadedData.drones[droneName].LiDARs[name].Enabled;
                    param.NumberOfChannels = loadedData.drones[droneName].LiDARs[name].NumberOfChannels;
                    param.RotationsPerSecond = loadedData.drones[droneName].LiDARs[name].RotationsPerSecond;
                    param.PointsPerSecond = loadedData.drones[droneName].LiDARs[name].PointsPerSecond;
                    param.MaxDistance = loadedData.drones[droneName].LiDARs[name].MaxDistance;
                    param.VerticalFOVUpper = loadedData.drones[droneName].LiDARs[name].VerticalFOVUpper;
                    param.VerticalFOVLower = loadedData.drones[droneName].LiDARs[name].VerticalFOVLower;
                    param.HorizontalFOVStart = loadedData.drones[droneName].LiDARs[name].HorizontalFOVStart;
                    param.HorizontalFOVEnd = loadedData.drones[droneName].LiDARs[name].HorizontalFOVEnd;
                    param.DrawDebugPoints = loadedData.drones[droneName].LiDARs[name].DrawDebugPoints;
                    return true;
                } else
                {
                    GD.PrintErr("LiDAR configuration for " + name + " not found in drone " + droneName);
                }
                return false;
            }
            else
            {
                GD.PrintErr("Drone configuration for " + droneName + " not found.");
                return false;
            }
        }
        public void SetLidarPosition(string droneName)
        {
            var lidars = FindComponents<ILiDAR3DController>();
            var lidarCount = lidars.Count;
            GD.Print("Found " + lidarCount + " LiDAR components in drone: " + droneName);
            var counter = 0;
            foreach (var ilidar in lidars)
            {
                var lidarNode = ilidar as Node;
                if (lidarNode == null) {
                    GD.PrintErr("LiDAR component is not a Node. Skipping.");
                    continue;
                }

                Node parent = lidarNode.GetParent();
                if (parent == null) {
                    GD.PrintErr("LiDAR node has no parent. Skipping.");
                    continue;
                }

                GD.Print("Found Lidar: " + parent.Name);
                LiDAR3DParams param;
                if (this.GetParam(droneName, parent.Name, out param))
                {
                    counter++;
                    ilidar.SetParams(param);
                    //pos
                    var lidarData = loadedData.drones[droneName].LiDARs[parent.Name];
                    float x = lidarData.X;
                    float y = lidarData.Y;
                    float z = lidarData.Z;
                    
                    Node grandParent = parent.GetParent();
                    float y_off = 0;
                    if (grandParent is Node3D gp3d)
                    {
                        y_off = gp3d.GlobalPosition.Y;
                    }
                    
                    Vector3 v = new Vector3(x, y, z);
                    Vector3 v_unity = ConvertRos2Unity(v);
                    
                    if (parent is Node3D p3d)
                    {
                        p3d.Position = v_unity;
                    }
                    GD.Print("v: " + v_unity);
                    //angle
                    float roll = lidarData.Roll;
                    float pitch = lidarData.Pitch;
                    float yaw = lidarData.Yaw;
                    Vector3 euler_angle = new Vector3(roll, pitch, yaw);
                    Vector3 euler_angle_unity = -ConvertRos2Unity(euler_angle);
                    GD.Print("euler_angle: " + euler_angle_unity);
                    
                    if (parent is Node3D p3d_rot)
                    {
                        p3d_rot.RotationDegrees = euler_angle_unity;
                    }
                } else
                {
                    GD.PrintErr("Failed to get LiDAR parameters for " + parent.Name + " in drone " + droneName);
                }
            }
            GD.Print("LiDAR configuration applied to " + counter + " LiDAR components.");
        }
        private Vector3 ConvertRos2Unity(Vector3 ros_data)
        {
            return new Vector3(
                -ros_data.Y, // Godot.X
                ros_data.Z,  // Godot.Y
                ros_data.X   // Godot.Z
                );
        }

    }
}