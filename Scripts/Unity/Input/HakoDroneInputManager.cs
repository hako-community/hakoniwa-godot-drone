using Godot;
using System;

namespace hakoniwa.objects.core
{
    public partial class HakoDroneInputManager : Node, IDroneInput
    {
        public static HakoDroneInputManager Instance { get; private set; }

        // Button state tracking
        private class ButtonState
        {
            public bool IsPressed;
            public bool IsJustPressed;
            public bool IsJustReleased;

            public void Update(bool currentPressed)
            {
                IsJustPressed = currentPressed && !IsPressed;
                IsJustReleased = !currentPressed && IsPressed;
                IsPressed = currentPressed;
            }
        }

        private ButtonState _sButton = new ButtonState(); // A
        private ButtonState _eButton = new ButtonState(); // B
        private ButtonState _nButton = new ButtonState(); // X
        private ButtonState _wButton = new ButtonState(); // Y
        private ButtonState _upButton = new ButtonState();
        private ButtonState _downButton = new ButtonState();

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }

            Instance = this;
            // DontDestroyOnLoad equivalent is not strictly needed if this is an Autoload or part of a persistent scene,
            // but effectively for this manager we keep it simple. 
            // If it needs to persist across scene changes in Godot, it should be an Autoload.
        }

        public override void _Process(double delta)
        {
            // Update button states every frame
            // Assuming Device ID 0 for the primary gamepad
            int deviceId = 0;

            _sButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.A));       // South (A/Cross)
            _eButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.B));       // East (B/Circle)
            _nButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.X));       // West (X/Square) -> Wait, Unity's N(orth) is Y/Triangle. 
                                                                                    // Unity InputSystem Gamepad:
                                                                                    // North = Y (Triangle)
                                                                                    // West = X (Square)
                                                                                    // South = A (Cross)
                                                                                    // East = B (Circle)
            
            // Godot JoyButton:
            // A = 0 (Sony Cross, Xbox A) -> South
            // B = 1 (Sony Circle, Xbox B) -> East
            // X = 2 (Sony Square, Xbox X) -> West
            // Y = 3 (Sony Triangle, Xbox Y) -> North

            // Mapping correction based on standard layouts:
            // Unity Sbutton (South) -> Godot A
            // Unity Ebutton (East)  -> Godot B
            // Unity Nbutton (North) -> Godot Y
            // Unity Wbutton (West)  -> Godot X

            // Re-updating with correct mapping:
             _nButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.Y)); // North
             _wButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.X)); // West

            _upButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.DpadUp));
            _downButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.DpadDown));
        }

        public Vector2 GetLeftStickInput()
        {
            int deviceId = 0;
            float x = Input.GetJoyAxis(deviceId, JoyAxis.LeftX);
            float y = Input.GetJoyAxis(deviceId, JoyAxis.LeftY);
            // Unity's Input System Vector2: Up is (0, 1).
            // Godot's GetJoyAxis: Up is -1.
            // So we negate Y to match Unity's convention.
            return new Vector2(x, -y);
        }

        public Vector2 GetRightStickInput()
        {
            int deviceId = 0;
            float x = Input.GetJoyAxis(deviceId, JoyAxis.RightX);
            float y = Input.GetJoyAxis(deviceId, JoyAxis.RightY);
            return new Vector2(x, -y);
        }

        public bool IsAButtonPressed() => _sButton.IsJustPressed;
        public bool IsAButtonReleased() => _sButton.IsJustReleased;

        public bool IsBButtonPressed() => _eButton.IsJustPressed;
        public bool IsBButtonReleased() => _eButton.IsJustReleased;

        public bool IsXButtonPressed() => _nButton.IsJustPressed;
        public bool IsXButtonReleased() => _nButton.IsJustReleased;

        public bool IsYButtonPressed() => _wButton.IsJustPressed;
        public bool IsYButtonReleased() => _wButton.IsJustReleased;

        public bool IsUpButtonPressed() => _upButton.IsJustPressed;
        public bool IsUpButtonReleased() => _upButton.IsJustReleased;

        public bool IsDownButtonPressed() => _downButton.IsJustPressed;
        public bool IsDownButtonReleased() => _downButton.IsJustReleased;

        public void DoVibration(bool isRightHand, float frequency, float amplitude, float durationSec)
        {
            int deviceId = 0;
            float weakMagnitude = isRightHand ? 0.0f : amplitude;
            float strongMagnitude = isRightHand ? amplitude : 0.0f;
            
            // Godot handles duration automatically
            Input.StartJoyVibration(deviceId, weakMagnitude, strongMagnitude, durationSec);
        }
    }
}