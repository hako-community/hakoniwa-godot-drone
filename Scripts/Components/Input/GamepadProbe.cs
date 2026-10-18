using Godot;
using System;
using System.Text;

namespace hakoniwa.objects.core
{
    /// <summary>
    /// Phase 0 spike probe for "Quest 3 + Bluetooth gamepad" support.
    ///
    /// This exists to answer the open risks in devai/GAMEPAD_SUPPORT_PLAN.md section 3 on the
    /// actual headset, BEFORE any of the selection UI is built:
    ///
    ///   R1 does Godot on Quest see a Bluetooth pad at all?   -> joypad count + name + guid
    ///   R2 is the axis/button mapping correct?               -> IsJoyKnown + live axis/button dump
    ///   R3 which buttons does Meta OS swallow?               -> per-button edge log (a button that
    ///                                                           never logs is captured by the system)
    ///   R4 does input arrive during an immersive session?    -> values keep updating in the course scene
    ///   R5 does sleep/disconnect/reconnect recover?          -> JoyConnectionChanged events
    ///
    /// Output goes through HakoDroneXrInputManager.Log, so it shows on the VR debug panel AND is
    /// mirrored to the pullable log file:
    ///   adb pull /sdcard/Android/data/com.hakoniwa.dronesimulator/files/ .  (one file per session)
    ///
    /// TEMPORARY. Remove (or fold into the permanent diagnostics) at Phase 5-4.
    /// </summary>
    public partial class GamepadProbe : Node
    {
        public static GamepadProbe Instance { get; private set; }

        /// <summary>
        /// When true, DroneControl reads the gamepad instead of the XR controllers.
        /// Phase 0 only -- Phase 1 replaces this with DroneParam + DroneInputResolver.
        ///
        /// DroneControl is created in code by DroneAvatar.StartOfflineMode(), so an [Export] flag
        /// on it would never be reachable from the editor. The toggle is a gamepad button combo
        /// instead, which doubles as a test: if the combo cannot toggle it, the pad is not
        /// delivering input and the probe log already says so.
        /// </summary>
        public static bool ForceGamepadInput { get; private set; } = false;

        // RB held + DpadUp pressed.
        //
        // The first on-device run used LB+RB and never fired: LeftShoulder produced no events at
        // all while RightShoulder did, so the toggle was unreachable and the drone stayed on the
        // XR controllers for the whole session. Both buttons used here are confirmed to arrive on
        // the Quest (see devai/GAMEPAD_SUPPORT_PLAN.md section 4 Phase 0 results).
        private static readonly JoyButton ToggleHold = JoyButton.RightShoulder;
        private static readonly JoyButton ToggleTrigger = JoyButton.DpadUp;
        private bool toggleTriggerWasPressed = false;
        private bool autoEnabledOnce = false;

        // Every SDL button/axis, so a mis-mapped pad is visible rather than silently wrong.
        private static readonly JoyButton[] ProbedButtons =
        {
            JoyButton.A, JoyButton.B, JoyButton.X, JoyButton.Y,
            JoyButton.Back, JoyButton.Guide, JoyButton.Start,
            JoyButton.LeftStick, JoyButton.RightStick,
            JoyButton.LeftShoulder, JoyButton.RightShoulder,
            JoyButton.DpadUp, JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.DpadRight,
            JoyButton.Misc1,
            JoyButton.Paddle1, JoyButton.Paddle2, JoyButton.Paddle3, JoyButton.Paddle4,
            JoyButton.Touchpad,
        };

        private static readonly JoyAxis[] ProbedAxes =
        {
            JoyAxis.LeftX, JoyAxis.LeftY, JoyAxis.RightX, JoyAxis.RightY,
            JoyAxis.TriggerLeft, JoyAxis.TriggerRight,
        };

        private readonly bool[] buttonWasPressed = new bool[ProbedButtons.Length];

        private double summaryTimer = 0;
        private int lastJoypadCount = -1;

        /// <summary>
        /// Device id of the first *real* gamepad, or -1 if none is attached.
        ///
        /// This exists because "is any joypad connected" is useless on a Quest: the headset
        /// exposes each Touch controller as an Android GAMEPAD/JOYSTICK device
        /// (Classes: KEYBOARD | GAMEPAD | JOYSTICK | INPUT_DEVICE_CLASS_VR_PERIPHERAL), so
        /// Input.GetConnectedJoypads() is never empty even with no gamepad in the room.
        /// Worse, a Touch controller can occupy index 0, so pads[0] is not the gamepad either.
        /// </summary>
        public static int GetActiveGamepadDevice()
        {
            foreach (int dev in Input.GetConnectedJoypads())
            {
                if (IsRealGamepad(dev)) return dev;
            }
            return -1;
        }

        public static bool IsRealGamepad(int dev)
        {
            // Touch controllers arrive with no SDL mapping and a synthesised name such as
            // "Device 0x353E3AA8718B1D83". A real pad ("Xbox Series X Controller") is known to SDL.
            if (!Input.IsJoyKnown(dev)) return false;
            string name = Input.GetJoyName(dev);
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("Device 0x", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }
            Instance = this;

            Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;

            HakoDroneXrInputManager.Log("[Gamepad] probe started (Phase 0). LB+RB toggles gamepad control.");
            LogDeviceInventory("startup");
        }

        public override void _ExitTree()
        {
            if (Instance == this)
            {
                Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
                Instance = null;
            }
        }

        // R5: a Bluetooth pad that sleeps and wakes shows up here, and the device id may change.
        private void OnJoyConnectionChanged(long device, bool connected)
        {
            string name = connected ? Input.GetJoyName((int)device) : "-";
            HakoDroneXrInputManager.Log(
                $"[Gamepad] CONNECTION CHANGED dev={device} connected={connected} name=\"{name}\"");
            LogDeviceInventory("connection-changed");
        }

        // Runs in the physics loop, matching DroneControl.HandleInput() so that what the probe
        // reports is what the drone would have consumed in the same frame.
        public override void _PhysicsProcess(double delta)
        {
            var pads = Input.GetConnectedJoypads();

            if (pads.Count != lastJoypadCount)
            {
                lastJoypadCount = pads.Count;
                LogDeviceInventory("count-changed");
            }

            int dev = GetActiveGamepadDevice();
            if (dev < 0)
            {
                summaryTimer = 0;
                if (ForceGamepadInput)
                {
                    // The pad went away (battery, sleep, out of range). Fall back so the drone
                    // does not become uncontrollable mid-flight.
                    ForceGamepadInput = false;
                    autoEnabledOnce = false;
                    HakoDroneXrInputManager.Log(
                        "[Gamepad] *** ForceGamepadInput = False *** (no real gamepad -> back on XR controllers)");
                }
                return;
            }

            // Desired end behaviour is "gamepad connected -> gamepad flies it", so opt in
            // automatically the first time a real pad shows up. RB+DpadUp still flips it back
            // for an A/B comparison against the Touch controllers without a rebuild.
            if (!autoEnabledOnce)
            {
                autoEnabledOnce = true;
                ForceGamepadInput = true;
                HakoDroneXrInputManager.Log(
                    $"[Gamepad] *** ForceGamepadInput = True *** (auto: real gamepad dev={dev} \"{Input.GetJoyName(dev)}\")");
            }

            HandleToggleCombo(dev);
            LogButtonEdges(dev);

            summaryTimer += delta;
            if (summaryTimer >= 1.0)
            {
                summaryTimer = 0;
                LogAxes(dev);
            }
        }

        private void HandleToggleCombo(int dev)
        {
            bool hold = Input.IsJoyButtonPressed(dev, ToggleHold);
            bool trigger = Input.IsJoyButtonPressed(dev, ToggleTrigger);

            if (hold && trigger && !toggleTriggerWasPressed)
            {
                ForceGamepadInput = !ForceGamepadInput;
                HakoDroneXrInputManager.Log(
                    $"[Gamepad] *** ForceGamepadInput = {ForceGamepadInput} *** " +
                    (ForceGamepadInput ? "(drone now reads the GAMEPAD)" : "(drone back on XR controllers)"));
            }
            toggleTriggerWasPressed = trigger;
        }

        // R3: a button that never produces a line here is being swallowed before Godot sees it.
        private void LogButtonEdges(int dev)
        {
            for (int i = 0; i < ProbedButtons.Length; i++)
            {
                bool pressed = Input.IsJoyButtonPressed(dev, ProbedButtons[i]);
                if (pressed != buttonWasPressed[i])
                {
                    buttonWasPressed[i] = pressed;
                    HakoDroneXrInputManager.Log(
                        $"[Gamepad] button {ProbedButtons[i]} ({(int)ProbedButtons[i]}) -> {(pressed ? "DOWN" : "UP")}");
                }
            }
        }

        private void LogAxes(int dev)
        {
            var sb = new StringBuilder("[Gamepad] axes");
            foreach (var axis in ProbedAxes)
            {
                sb.Append($" {axis}={Input.GetJoyAxis(dev, axis):F2}");
            }
            sb.Append($" | forceGamepad={ForceGamepadInput}");
            // 1 Hz axis dump: a diagnostic, not an event. Button edges and connect/disconnect below
            // stay on the always-on sink.
            HakoDroneXrInputManager.LogDiag(sb.ToString());
        }

        // R1/R2: identity of every attached pad. IsJoyKnown tells whether SDL has a mapping for it;
        // if that is false the axis/button numbering is not to be trusted.
        private void LogDeviceInventory(string reason)
        {
            var pads = Input.GetConnectedJoypads();
            HakoDroneXrInputManager.Log($"[Gamepad] inventory ({reason}): joypads={pads.Count}");

            foreach (int dev in pads)
            {
                string vendor = "?";
                string product = "?";
                string rawName = "?";
                try
                {
                    var info = Input.GetJoyInfo(dev);
                    if (info.ContainsKey("vendor_id")) vendor = info["vendor_id"].ToString();
                    if (info.ContainsKey("product_id")) product = info["product_id"].ToString();
                    if (info.ContainsKey("raw_name")) rawName = info["raw_name"].ToString();
                }
                catch (Exception e)
                {
                    rawName = $"(GetJoyInfo failed: {e.Message})";
                }

                HakoDroneXrInputManager.Log(
                    $"[Gamepad]   dev={dev} known={Input.IsJoyKnown(dev)} name=\"{Input.GetJoyName(dev)}\" " +
                    $"raw=\"{rawName}\" vendor={vendor} product={product}");
                HakoDroneXrInputManager.Log(
                    $"[Gamepad]   dev={dev} guid={Input.GetJoyGuid(dev)}");
            }
        }
    }
}
