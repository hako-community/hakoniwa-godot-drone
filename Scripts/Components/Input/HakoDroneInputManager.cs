using Godot;
using System;

namespace hakoniwa.objects.core
{
    public partial class HakoDroneInputManager : Node, IDroneInput
    {
        public static HakoDroneInputManager Instance { get; private set; }

        private GamepadTuning tuning = new GamepadTuning();
        public GamepadTuning Tuning => tuning;

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
        private ButtonState _menuButton = new ButtonState();

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }

            Instance = this;
            int deviceId = 0;
            string joyName = Input.GetJoyName(deviceId);
            tuning = GamepadTuning.Load(joyName);
            GD.Print($"HakoDroneInputManager: loaded tuning => {tuning}");
        }

        public override void _Process(double delta)
        {
            int deviceId = 0;

            _sButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.A) || Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.Enter));       // South (A/Cross)
            _eButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.B) || Input.IsKeyPressed(Key.Shift));       // East (B/Circle)
            _nButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.Y) || Input.IsKeyPressed(Key.C)); // North
            _wButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.X) || Input.IsKeyPressed(Key.X)); // West

            _upButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.DpadUp) || Input.IsKeyPressed(Key.Pageup));
            _downButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.DpadDown) || Input.IsKeyPressed(Key.Pagedown));

            _menuButton.Update(Input.IsJoyButtonPressed(deviceId, JoyButton.Start) || 
                               Input.IsJoyButtonPressed(deviceId, JoyButton.Back) || 
                               Input.IsKeyPressed(Key.Escape) || 
                               Input.IsKeyPressed(Key.M) || 
                               Input.IsKeyPressed(Key.Backspace));
        }

        public Vector2 GetLeftStickInput()
        {
            int deviceId = 0;
            float x = Input.GetJoyAxis(deviceId, JoyAxis.LeftX);
            float y = Input.GetJoyAxis(deviceId, JoyAxis.LeftY);

            // Keyboard fallback
            if (Math.Abs(x) < 0.1f)
            {
                if (Input.IsKeyPressed(Key.A)) x = -1.0f;
                else if (Input.IsKeyPressed(Key.D)) x = 1.0f;
            }
            if (Math.Abs(y) < 0.1f)
            {
                if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) y = -1.0f;
                else if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) y = 1.0f;
            }

            return new Vector2(
                tuning.ApplyYaw(x),
                tuning.Apply(y, tuning.ThrottleSensitivity) * -1.0f
            );
        }

        public Vector2 GetRightStickInput()
        {
            int deviceId = 0;
            float x = Input.GetJoyAxis(deviceId, JoyAxis.RightX);
            float y = Input.GetJoyAxis(deviceId, JoyAxis.RightY);

            // Keyboard fallback
            if (Math.Abs(x) < 0.1f)
            {
                if (Input.IsKeyPressed(Key.Left)) x = -1.0f;
                else if (Input.IsKeyPressed(Key.Right)) x = 1.0f;
            }
            if (Math.Abs(y) < 0.1f)
            {
                if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.S))
                {
                    if (Input.IsKeyPressed(Key.Up)) y = -1.0f;
                    else if (Input.IsKeyPressed(Key.Down)) y = 1.0f;
                }
            }

            return new Vector2(
                tuning.Apply(x, tuning.RollSensitivity),
                tuning.Apply(y, tuning.PitchSensitivity) * -1.0f
            );
        }

        public bool IsAButtonPressed() => _sButton.IsJustPressed;
        public bool IsAButtonReleased() => _sButton.IsJustReleased;

        public bool IsBButtonPressed() => _eButton.IsJustPressed;
        public bool IsBButtonReleased() => _eButton.IsJustReleased;

        public bool IsXButtonPressed() => _wButton.IsJustPressed;
        public bool IsXButtonReleased() => _wButton.IsJustReleased;

        public bool IsYButtonPressed() => _nButton.IsJustPressed;
        public bool IsYButtonReleased() => _nButton.IsJustReleased;

        public bool IsUpButtonPressed() => _upButton.IsJustPressed;
        public bool IsUpButtonReleased() => _upButton.IsJustReleased;

        public bool IsDownButtonPressed() => _downButton.IsJustReleased;
        public bool IsDownButtonReleased() => _downButton.IsJustReleased;

        public bool IsMenuButtonPressed() => _menuButton.IsJustPressed;

        public void DoVibration(bool isRightHand, float frequency, float amplitude, float durationSec)
        {
            int deviceId = 0;
            float weakMagnitude = isRightHand ? 0.0f : amplitude;
            float strongMagnitude = isRightHand ? amplitude : 0.0f;
            
            Input.StartJoyVibration(deviceId, weakMagnitude, strongMagnitude, durationSec);
        }
    }
}