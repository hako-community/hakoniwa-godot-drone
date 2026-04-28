using Godot;
using System;

namespace hakoniwa.objects.core
{
    public partial class HakoDroneXrInputManager : Node, IDroneInput
    {
        public static HakoDroneXrInputManager Instance { get; private set; }

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }

            Instance = this;
        }

        public Vector2 GetLeftStickInput()
        {
            // Assuming "left_hand_stick" action is defined in Input Map for XR
            // Or use direct axis if not using Input Map
            float x = Input.GetActionStrength("xr_left_stick_x_positive") - Input.GetActionStrength("xr_left_stick_x_negative");
            float y = Input.GetActionStrength("xr_left_stick_y_positive") - Input.GetActionStrength("xr_left_stick_y_negative");
            
            // Alternatively, if using standard OpenXR names:
            // This depends on the project's Input Map setup.
            // For now, providing a generic implementation using Input.GetVector if possible
            return Input.GetVector("xr_left_stick_left", "xr_left_stick_right", "xr_left_stick_up", "xr_left_stick_down");
        }

        public Vector2 GetRightStickInput()
        {
            return Input.GetVector("xr_right_stick_left", "xr_right_stick_right", "xr_right_stick_up", "xr_right_stick_down");
        }

        public bool IsAButtonPressed() => Input.IsActionJustPressed("xr_a_button");
        public bool IsAButtonReleased() => Input.IsActionJustReleased("xr_a_button");

        public bool IsBButtonPressed() => Input.IsActionJustPressed("xr_b_button");
        public bool IsBButtonReleased() => Input.IsActionJustReleased("xr_b_button");

        public bool IsXButtonPressed() => Input.IsActionJustPressed("xr_x_button");
        public bool IsXButtonReleased() => Input.IsActionJustReleased("xr_x_button");

        public bool IsYButtonPressed() => Input.IsActionJustPressed("xr_y_button");
        public bool IsYButtonReleased() => Input.IsActionJustReleased("xr_y_button");

        public bool IsUpButtonPressed() => Input.IsActionJustPressed("xr_left_trigger");
        public bool IsUpButtonReleased() => Input.IsActionJustReleased("xr_left_trigger");

        public bool IsDownButtonPressed() => Input.IsActionJustPressed("xr_right_trigger");
        public bool IsDownButtonReleased() => Input.IsActionJustReleased("xr_right_trigger");

        public void DoVibration(bool isRightHand, float frequency, float amplitude, float durationSec)
        {
            // Godot XR vibration is typically handled via XRController3D.TriggerHapticPulse
            // For a global manager, we might need to find the controller nodes.
            // This is a placeholder as it depends on the scene structure.
            GD.Print($"XR Vibration requested: RightHand={isRightHand}, Amp={amplitude}, Dur={durationSec}");
        }
    }
}