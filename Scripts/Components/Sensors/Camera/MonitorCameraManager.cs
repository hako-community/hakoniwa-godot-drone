using System;
using System.Collections.Generic;
using System.IO;
using hakoniwa.objects.core.frame;
using Godot;

namespace hakoniwa.objects.core.sensors
{
    public partial class MonitorCameraManager : Node
    {
        public static MonitorCameraManager Instance { get; private set; }

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }

            Instance = this;
            hakoCameras = new Dictionary<string, HakoCamera>();
            SetCameras();
        }

        [Export]
        public string monitorCameraConfigPath = "./monitor_camera_config.json";
        [Export]
        public PackedScene cameraPrefab; // Godot: Use PackedScene for prefabs
        private Dictionary<string, HakoCamera> hakoCameras;

        public string saveDirPath = ".";

        public void GetAndSaveCameraImages(string image_name)
        {
            foreach (var camera_name in hakoCameras.Keys)
            {
                var camera_data = hakoCameras[camera_name].GetImage("png");
                string filePath = Path.Combine(saveDirPath, $"{image_name}_{camera_name}.png");
                try
                {
                    File.WriteAllBytes(filePath, camera_data);
                    GD.Print($"Image saved: {filePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"Failed to save image: {e.Message}");
                }
            }
        }

        public List<string> GetCameraNames()
        {
            return new List<string>(hakoCameras.Keys);
        }

        public Texture2D GetCameraRenderTexture(string cameraName)
        {
            if (hakoCameras.TryGetValue(cameraName, out var camera))
            {
                return camera.GetRenderTexture();
            }
            return null;
        }

        public string GetEncodeType(string cameraName)
        {
            if (hakoCameras.TryGetValue(cameraName, out var camera))
            {
                return camera.GetEncodeType();
            }
            return null;
        }

        public byte[] GetImage(string cameraName, string encode_type)
        {
            if (hakoCameras.TryGetValue(cameraName, out var camera))
            {
                return camera.GetImage(encode_type);
            }
            return null;
        }

        public (Vector3 position, Vector3 rotation, float fov) GetCameraInfo(string cameraName)
        {
            if (hakoCameras.TryGetValue(cameraName, out var camera))
            {
                return (camera.GlobalPosition, camera.GlobalRotationDegrees, camera.GetFov());
            }
            return (Vector3.Zero, Vector3.Zero, 0);
        }

        public void ReloadCameras()
        {
            RemoveCameras();
            SetCameras();
        }

        private void RemoveCameras()
        {
            foreach (var hakoCamera in hakoCameras.Values)
            {
                if (hakoCamera != null)
                {
                    hakoCamera.QueueFree();
                }
            }
            hakoCameras.Clear();
        }

        private void SetCameras()
        {
            var cameraConfig = CameraConfigLoader.LoadConfig(monitorCameraConfigPath);

            if (cameraConfig == null)
            {
                GD.PrintErr("Failed to load camera configuration.");
                return;
            }
            if (cameraPrefab == null)
            {
                GD.PrintErr("Camera prefab is not set.");
                return;
            }

            foreach (var camData in cameraConfig.monitor_cameras)
            {
                // Godot: Instantiate PackedScene
                Node3D newCamera = cameraPrefab.Instantiate<Node3D>();
                HakoCamera hakoCamera = NodeUtil.FindNodeByInterface<HakoCamera>(newCamera);
                if (hakoCamera == null)
                {
                    GD.PrintErr($"Failed to get HakoCamera component for {camData.pdu_info.robot_name}");
                    newCamera.QueueFree();
                    continue;
                }

                AddChild(newCamera);
                newCamera.Name = camData.pdu_info.robot_name;

                Vector3 position = new Vector3(camData.coordinate_system.position.x, camData.coordinate_system.position.y, camData.coordinate_system.position.z);
                Vector3 rotation = new Vector3(camData.coordinate_system.orientation.roll, camData.coordinate_system.orientation.pitch, camData.coordinate_system.orientation.yaw);
                
                // Note: Assuming FrameConvertor is already ported or handles Godot Vector3
                position = FrameConvertor.PosRos2Unity(position);
                rotation = FrameConvertor.EulerRosDeg2UnityDeg(rotation);

                hakoCamera.ConfigureCamera(
                    camData.pdu_info.robot_name,
                    camData.camera_type,
                    camData.encode_type,
                    camData.coordinate_system.type,
                    camData.coordinate_system.target,
                    position,
                    rotation,
                    camData.fov.horizontal,
                    camData.resolution.width,
                    camData.resolution.height
                );

                hakoCameras[camData.pdu_info.robot_name] = hakoCamera;
            }
        }
    }
}
