using System.Collections.Generic;
using hakoniwa.objects.core.sensors;
using Godot;
using System;

namespace hakoniwa.objects.ui
{
    public partial class MonitorCameraUI : Node
    {
        private MonitorCameraManager cameraManager;

        [Export]
        public TextureRect cameraDisplay;
        [Export]
        public OptionButton cameraDropdown;
        [Export]
        public Button reloadButton;

        private void Initialize()
        {
            cameraManager = MonitorCameraManager.Instance;
            UpdateCameraList();

            // Connect signals in Godot way
            cameraDropdown.ItemSelected += OnCameraSelected;
            reloadButton.Pressed += () => {
                cameraManager.ReloadCameras();
                UpdateCameraList();
            };
        }

        public override void _Ready()
        {
            Initialize();
        }

        void UpdateCameraList()
        {
            cameraDropdown.Clear();
            List<string> cameraNames = cameraManager.GetCameraNames();
            foreach (var name in cameraNames)
            {
                cameraDropdown.AddItem(name);
            }

            if (cameraNames.Count > 0)
            {
                cameraDropdown.Selected = 0;
                OnCameraSelected(0);
            }
        }

        void OnCameraSelected(long index)
        {
            string selectedCamera = cameraDropdown.GetItemText((int)index);
            // Assuming MonitorCameraManager is fixed to return a Godot-compatible texture if it's not already.
            // Based on previous analysis, if RenderTexture is a compatibility class, it might need casting or conversion.
            // For now, mapping it to Godot's TextureRect.Texture.
            var texture = cameraManager.GetCameraRenderTexture(selectedCamera);
            if (texture != null && cameraDisplay != null)
            {
                // Note: If GetCameraRenderTexture returns a custom type, this might need further adjustment.
                // Assuming it returns something that can be assigned to TextureRect.Texture (like ViewportTexture or ImageTexture).
                cameraDisplay.Texture = texture as Texture2D;
            }
        }
    }
}