using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Newtonsoft.Json;

namespace hakoniwa.objects.core.sensors
{
    [Serializable]
    public class CameraConfig
    {
        public string pdu_path;
        public List<MonitorCamera> monitor_cameras;
    }

    [Serializable]
    public class MonitorCamera
    {
        public PduInfo pdu_info;
        public UiPosition ui_position;
        public CoordinateSystem coordinate_system;
        public Fov fov;
        public Resolution resolution;
        public string camera_type;
        public string encode_type;
        public string trigger_type;
    }

    [Serializable]
    public class PduInfo
    {
        public string robot_name;
    }

    [Serializable]
    public class CoordinateSystem
    {
        public string type;
        public string target;
        public Position position;
        public Rotation orientation;
    }

    [Serializable]
    public class Position
    {
        public float x, y, z;
    }

    [Serializable]
    public class Rotation
    {
        public float roll, pitch, yaw;
    }

    [Serializable]
    public class Fov
    {
        public float horizontal, vertical;
    }

    [Serializable]
    public class Resolution
    {
        public int width, height;
    }
    
    [Serializable]
    public class UiPosition
    {
        public int x, y;
    }

    public class CameraConfigLoader
    {
        public static CameraConfig LoadConfig(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
            {
                GD.PrintErr($"Config file not found: {jsonFilePath}");
                return null;
            }

            try
            {
                string jsonText = File.ReadAllText(jsonFilePath);
                // Godot: Using Newtonsoft.Json instead of Unity's JsonUtility
                var cameraConfig = JsonConvert.DeserializeObject<CameraConfig>(jsonText);

                if (cameraConfig != null && cameraConfig.monitor_cameras != null)
                {
                    GD.Print($"Loaded {cameraConfig.monitor_cameras.Count} cameras from JSON");
                }
                return cameraConfig;
            }
            catch (Exception e)
            {
                GD.PrintErr($"Failed to deserialize camera config: {e.Message}");
                return null;
            }
        }

        public static void DebugPrint(CameraConfig cameraConfig)
        {
            if (cameraConfig == null) return;

            GD.Print($"PDU Path: {cameraConfig.pdu_path}");
            GD.Print($"Number of Cameras: {cameraConfig.monitor_cameras.Count}");

            foreach (var camera in cameraConfig.monitor_cameras)
            {
                GD.Print($"Camera: {camera.pdu_info.robot_name}");
                GD.Print($" - UI Position: ({camera.ui_position.x}, {camera.ui_position.y})");
                GD.Print($" - Type: {camera.camera_type}");
                GD.Print($" - Target: {camera.coordinate_system.target}");
                GD.Print($" - Position: ({camera.coordinate_system.position.x}, {camera.coordinate_system.position.y}, {camera.coordinate_system.position.z})");
                GD.Print($" - Rotation: (Roll: {camera.coordinate_system.orientation.roll}, Pitch: {camera.coordinate_system.orientation.pitch}, Yaw: {camera.coordinate_system.orientation.yaw})");
                GD.Print($" - FOV: Horizontal {camera.fov.horizontal}, Vertical {camera.fov.vertical}");
                GD.Print($" - Resolution: {camera.resolution.width}x{camera.resolution.height}");
                GD.Print($" - Trigger Type: {camera.trigger_type}");
            }
        }
    }
}