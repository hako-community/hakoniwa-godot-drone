using Godot;
using System;

namespace hakoniwa.objects.core
{
    public partial class FlightModeLedController : MeshInstance3D
    {
        public enum FlightMode
        {
            ATTI,
            GPS
        }

        [ExportGroup("LED Settings")]
        [Export]
        private MeshInstance3D ledRenderer;

        [ExportGroup("Mode Materials")]
        [Export]
        public Material attiMaterial;
        [Export]
        public Material gpsMaterial;
        [Export]
        public Material offMaterial;

        [ExportGroup("Blink Settings")]
        [Export]
        public float attiBlinkInterval = 0.5f;
        [Export]
        public float gpsBlinkInterval = 0.5f;

        private FlightMode currentMode = FlightMode.GPS;
        private float blinkTimer = 0f;
        private bool ledOn = false;

        public override void _Ready()
        {
            UpdateLedImmediate();
        }

        public void SetMode(FlightMode mode)
        {
            if (currentMode != mode)
            {
                currentMode = mode;
                blinkTimer = 0f;
                ledOn = false;
                UpdateLedImmediate();
            }
        }

        public override void _Process(double delta)
        {
            switch (currentMode)
            {
                case FlightMode.ATTI:
                    UpdateBlink((float)delta, attiBlinkInterval, attiMaterial);
                    break;

                case FlightMode.GPS:
                    UpdateBlink((float)delta, gpsBlinkInterval, gpsMaterial);
                    break;
            }
        }

        private void UpdateBlink(float delta, float interval, Material modeMaterial)
        {
            blinkTimer += delta;
            if (blinkTimer >= interval)
            {
                blinkTimer = 0f;
                ledOn = !ledOn;
                SetLedOn(ledOn, modeMaterial);
            }
        }

        private void UpdateLedImmediate()
        {
            switch (currentMode)
            {
                case FlightMode.ATTI:
                    SetLedOn(false, attiMaterial);
                    break;
                case FlightMode.GPS:
                    SetLedOn(false, gpsMaterial);
                    break;
            }
        }

        private void SetLedOn(bool on, Material modeMaterial)
        {
            if (ledRenderer == null) return;

            if (on)
            {
                ledRenderer.MaterialOverride = modeMaterial;
            }
            else
            {
                ledRenderer.MaterialOverride = offMaterial;  // 消灯
            }
        }
    }
}