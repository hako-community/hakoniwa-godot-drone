using Godot;
using System;

namespace hakoniwa.objects.core
{
    public partial class DroneLedController : MeshInstance3D
    {
        public enum DroneMode
        {
            DISARM,
            TAKEOFF,
            HOVER,
            LANDING
        }

        [ExportGroup("LED Settings")]
        [Export]
        private MeshInstance3D ledRenderer;

        [ExportGroup("Mode Materials")]
        [Export]
        public Material disarmMaterial;
        [Export]
        public Material takeoffMaterial;
        [Export]
        public Material hoverMaterial;
        [Export]
        public Material landingMaterial;

        [ExportGroup("Blink Settings")]
        [Export]
        public float takeoffBlinkInterval = 0.5f;
        [Export]
        public float landingBlinkInterval = 0.5f;

        private DroneMode currentMode = DroneMode.DISARM;
        private float blinkTimer = 0f;
        private bool ledOn = false;

        public override void _Ready()
        {
            UpdateLedImmediate();
        }

        public void SetMode(DroneMode mode)
        {
            if (currentMode != mode)
            {
                // GD.Print($"DroneLedController ({this.Name}): Changing mode from {currentMode} to {mode}");
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
                case DroneMode.DISARM:
                    SetLedOn(false, disarmMaterial);
                    break;

                case DroneMode.HOVER:
                    SetLedOn(true, hoverMaterial);
                    break;

                case DroneMode.TAKEOFF:
                    UpdateBlink((float)delta, takeoffBlinkInterval, takeoffMaterial);
                    break;

                case DroneMode.LANDING:
                    UpdateBlink((float)delta, landingBlinkInterval, landingMaterial);
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
                case DroneMode.DISARM:
                    SetLedOn(false, disarmMaterial);
                    break;

                case DroneMode.HOVER:
                    SetLedOn(true, hoverMaterial);
                    break;

                case DroneMode.TAKEOFF:
                case DroneMode.LANDING:
                    SetLedOn(false, takeoffMaterial); // 初期は OFF
                    break;
            }
        }

        private void SetLedOn(bool on, Material modeMaterial)
        {
            if (ledRenderer == null) return;

            if (on)
            {
                if (ledRenderer.MaterialOverride != modeMaterial)
                {
                    ledRenderer.MaterialOverride = modeMaterial;
                }
            }
            else
            {
                if (ledRenderer.MaterialOverride != disarmMaterial)
                {
                    ledRenderer.MaterialOverride = disarmMaterial; // OFF時は DISARM 用マテリアル
                }
            }
        }
    }
}