using System.Collections.Generic;
using hakoniwa.objects.core.sensors;
//using TMPro;
using Godot;
using Godot.Collections;

namespace hakoniwa.objects.ui
{
    public partial class MonitorCameraUIArrays : Node
    {
        private MonitorCameraManager cameraManager;
        public List<TextureRect> cameraDisplays;

        private void Initialize()
        {
            cameraManager = MonitorCameraManager.Instance;
        }

        public override void _Ready()
        {
            Initialize();
        }

        public override void _Process(double delta)
        {
            if (cameraManager == null) return;
            int i = 0;
            foreach (var camera_name in cameraManager.GetCameraNames())
            {
                if (i >= cameraDisplays.Count) break;
                var texture = cameraManager.GetCameraRenderTexture(camera_name);
                if (texture != null && cameraDisplays[i] != null)
                {
                    cameraDisplays[i].Texture = texture as Texture2D;
                }
                i++;
            }
        }

    }
}