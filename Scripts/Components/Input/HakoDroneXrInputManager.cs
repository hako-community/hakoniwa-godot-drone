using Godot;
using System;
using System.Collections.Generic;

namespace hakoniwa.objects.core
{
    public partial class HakoDroneXrInputManager : Node, IDroneInput
    {
        public static HakoDroneXrInputManager Instance { get; private set; }

        private static RichTextLabel debugLabelInstance = null;
        private static readonly List<string> logQueue = new List<string>();
        private const int MAX_LOG_LINES = 100;

        /*
         * File sink for the VR debug panel.
         * Everything shown on the panel is mirrored to a text file so the session can be
         * reviewed off-device. On Android the file is written to the app-private external
         * directory, which `adb pull` can read without root and without any permission:
         *   adb pull /sdcard/Android/data/com.hakoniwa.dronesimulator/files/  (one file per session)
         * On desktop it lands next to the other user data (OS.GetUserDataDir()).
         */
        private const string AndroidPackageName = "com.hakoniwa.dronesimulator"; // export_presets.cfg: package/unique_name
        private const string LogFilePrefix = "xr_debug_panel";
        private const int MaxLogFilesKept = 10;
        private static readonly object logFileLock = new object();
        private static System.IO.StreamWriter logFileWriter = null;
        private static bool logFileInitialized = false;

        // One file per app session, named after the session start.
        //
        // It used to be a single fixed name opened with append:false, which meant simply
        // relaunching the app destroyed the previous flight's log. On 2026-07-26 a flight that
        // reproduced both the roll-direction and the stuck-airframe symptoms could not be analysed
        // for exactly that reason. Uninstalling the app also removes this directory, so the log
        // must be pulled before a reinstall either way.
        private static string SessionLogFileName()
        {
            var t = Time.GetDatetimeDictFromSystem();
            int year = t.ContainsKey("year") ? (int)t["year"] : 0;
            int month = t.ContainsKey("month") ? (int)t["month"] : 0;
            int day = t.ContainsKey("day") ? (int)t["day"] : 0;
            int hour = t.ContainsKey("hour") ? (int)t["hour"] : 0;
            int minute = t.ContainsKey("minute") ? (int)t["minute"] : 0;
            int second = t.ContainsKey("second") ? (int)t["second"] : 0;
            return $"{LogFilePrefix}_{year:D4}{month:D2}{day:D2}_{hour:D2}{minute:D2}{second:D2}.log";
        }

        // Keeps the newest MaxLogFilesKept session logs. Only files this class created (the
        // prefix and the .log extension) in its own log directory are ever considered.
        private static void PruneOldLogs(string dir)
        {
            try
            {
                var files = new List<System.IO.FileInfo>();
                foreach (string p in System.IO.Directory.GetFiles(dir, LogFilePrefix + "*.log"))
                {
                    files.Add(new System.IO.FileInfo(p));
                }
                files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = MaxLogFilesKept; i < files.Count; i++)
                {
                    files[i].Delete();
                }
            }
            catch (Exception e)
            {
                GD.Print($"HakoDroneXrInputManager: log prune skipped ({e.Message}).");
            }
        }

        private static string ResolveLogDir()
        {
            if (OS.GetName() == "Android")
            {
                string externalDir = $"/sdcard/Android/data/{AndroidPackageName}/files";
                try
                {
                    System.IO.Directory.CreateDirectory(externalDir);
                    return externalDir;
                }
                catch (Exception e)
                {
                    GD.Print($"HakoDroneXrInputManager: external log dir unavailable ({e.Message}), falling back to user data dir.");
                }
            }
            return OS.GetUserDataDir();
        }

        private static string ResolveLogFilePath()
        {
            string dir = ResolveLogDir();
            PruneOldLogs(dir);
            return System.IO.Path.Combine(dir, SessionLogFileName());
        }

        /*
         * High-rate flight diagnostics -- [Response], [HandleInput], [XRInput] and the gamepad axis
         * dump -- are OFF by default. They run at 5 Hz and produced 1.3 MB per 10 minutes during the
         * 2026-07-26 lockup investigation, which is the right trade while chasing a bug and pure
         * overhead once it is fixed. The low-rate event lines (ARM, gamepad connect/disconnect,
         * control-source toggles, tuning) stay on always: they are what makes a log worth reading at
         * all, and they cost nothing.
         *
         * Turning them back on must NOT need a rebuild -- the export + install loop costs ~10 minutes
         * and is exactly what makes an investigation expensive. So this reads a marker file next to
         * the logs; create it and relaunch the app:
         *
         *   adb shell touch /sdcard/Android/data/com.hakoniwa.dronesimulator/files/DIAG_ON
         *   adb shell rm    /sdcard/Android/data/com.hakoniwa.dronesimulator/files/DIAG_ON
         *
         * On desktop, `--verbose-diag` on the command line does the same. The answer is resolved once
         * per session, so the marker is read at startup and cannot change mid-flight.
         */
        private const string DiagMarkerFileName = "DIAG_ON";
        private const string DiagCmdlineFlag = "--verbose-diag";
        private static bool diagResolved = false;
        private static bool diagEnabled = false;

        public static bool VerboseDiagnostics
        {
            get
            {
                if (diagResolved) return diagEnabled;
                diagResolved = true;
                try
                {
                    string marker = System.IO.Path.Combine(ResolveLogDir(), DiagMarkerFileName);
                    diagEnabled = System.IO.File.Exists(marker);
                    if (!diagEnabled)
                    {
                        foreach (string arg in OS.GetCmdlineArgs())
                        {
                            if (arg == DiagCmdlineFlag) { diagEnabled = true; break; }
                        }
                    }
                }
                catch (Exception e)
                {
                    // Never let the switch itself break the frame; default to quiet.
                    diagEnabled = false;
                    GD.Print($"HakoDroneXrInputManager: diagnostics marker check failed ({e.Message}), staying quiet.");
                }
                GD.Print($"HakoDroneXrInputManager: verbose flight diagnostics = {diagEnabled}"
                    + (diagEnabled ? "" : $" (touch {DiagMarkerFileName} next to the logs to enable)"));
                return diagEnabled;
            }
        }

        // Same sink as Log(), but only when the high-rate diagnostics are switched on.
        // Callers that build an expensive line should test VerboseDiagnostics themselves so the
        // string interpolation and the native service reads behind it are skipped too.
        public static void LogDiag(string message)
        {
            if (!VerboseDiagnostics) return;
            Log(message);
        }

        private static void WriteLogFile(string line)
        {
            lock (logFileLock)
            {
                if (!logFileInitialized)
                {
                    logFileInitialized = true; // only attempt to open once per session
                    try
                    {
                        string path = ResolveLogFilePath();
                        logFileWriter = new System.IO.StreamWriter(path, append: false) { AutoFlush = true };
                        GD.Print($"HakoDroneXrInputManager: debug panel log file = {path}");
                    }
                    catch (Exception e)
                    {
                        logFileWriter = null;
                        GD.PrintErr($"HakoDroneXrInputManager: could not open debug panel log file: {e.Message}");
                    }
                }

                if (logFileWriter == null) return;
                try
                {
                    logFileWriter.WriteLine(line);
                }
                catch (Exception)
                {
                    // A failing sink must never take the panel (or the frame) down.
                    logFileWriter = null;
                }
            }
        }

        public static void Log(string message)
        {
            GD.Print(message);

            var time = Time.GetTimeDictFromSystem();
            int hour = time.ContainsKey("hour") ? (int)time["hour"] : 0;
            int minute = time.ContainsKey("minute") ? (int)time["minute"] : 0;
            int second = time.ContainsKey("second") ? (int)time["second"] : 0;
            string formatted = $"[{hour:D2}:{minute:D2}:{second:D2}] {message}";

            // The file additionally carries a monotonic millisecond tick, the panel does not.
            // The diagnostics run at 5 Hz, so several lines share a second and a seconds-only
            // stamp cannot pair a [HandleInput] with the [Response] it caused.
            //
            // GetTicksMsec, not GetUnixTimeFromSystem: the first attempt derived the sub-second
            // part from the latter and every one of the 3805 lines in run8 came out as ".647",
            // so on this platform it carries no usable sub-second precision. The tick is also
            // monotonic, which is what interval measurements actually want.
            WriteLogFile($"[{hour:D2}:{minute:D2}:{second:D2} t={Time.GetTicksMsec()}] {message}");

            logQueue.Add(formatted);
            if (logQueue.Count > MAX_LOG_LINES)
            {
                logQueue.RemoveAt(0);
            }

            if (debugLabelInstance != null && GodotObject.IsInstanceValid(debugLabelInstance))
            {
                debugLabelInstance.Text = string.Join("\n", logQueue);
                int lineCount = debugLabelInstance.GetLineCount();
                if (lineCount > 0)
                {
                    debugLabelInstance.ScrollToLine(lineCount - 1);
                }
            }
        }

        public void LogMessage(string message)
        {
            Log(message);
        }

        private XRController3D leftController;
        private XRController3D rightController;
        private double resolveTimer = 0;
        private double panelCheckTimer = 0.5;

        /*
         * Stick conditioning (deadzone, curve, per-axis sensitivity) for the Touch controllers.
         *
         * These used to be hard-coded fields at deadzone 0.15 / exponent 1.5 / every sensitivity
         * 1.0, i.e. yaw ran 8x and throttle 5.5x hotter than the values the gamepad path was tuned
         * to (yaw 0.12, throttle 0.18, exponent 2.5) even though both feed the SAME downstream
         * stick_yaw_strength (100) and stick_strength (15). That is why the pad felt fine and the
         * Touch controllers did not.
         *
         * Now read from the "xr" section of config/controller/gamepad_tuning.json, which the
         * external override on the headset can replace without an export cycle.
         */
        private GamepadTuning tuning = new GamepadTuning();
        public GamepadTuning Tuning => tuning;

        // Button state tracking with cooldown debounce to prevent chattering
        private class ButtonState
        {
            public bool IsPressed;
            public bool IsJustPressed;
            public bool IsJustReleased;
            private double cooldownTimer = 0;
            private const double CooldownTime = 0.2; // 200ms cooldown

            public void Update(bool currentPressed, double delta)
            {
                if (cooldownTimer > 0)
                {
                    cooldownTimer -= delta;
                }

                bool prevPressed = IsPressed;
                IsPressed = currentPressed;

                if (currentPressed && !prevPressed)
                {
                    if (cooldownTimer <= 0)
                    {
                        IsJustPressed = true;
                        cooldownTimer = CooldownTime;
                    }
                    else
                    {
                        IsJustPressed = false;
                    }
                    IsJustReleased = false;
                }
                else if (!currentPressed && prevPressed)
                {
                    IsJustPressed = false;
                    IsJustReleased = true;
                }
                else
                {
                    IsJustPressed = false;
                    IsJustReleased = false;
                }
            }
        }

        private class ControllerButtonState
        {
            public ButtonState Ax = new ButtonState();
            public ButtonState By = new ButtonState();
            public ButtonState TriggerClick = new ButtonState();
            // openxr_action_map.tres binds "menu_button" to .../left/input/menu/click and
            // .../right/input/system/click. Only the left one is usable on a Quest: the right-hand
            // Oculus button is owned by Horizon OS and never reaches the app.
            public ButtonState Menu = new ButtonState();

            public void Update(XRController3D controller, double delta)
            {
                if (controller != null && GodotObject.IsInstanceValid(controller))
                {
                    Ax.Update(controller.IsButtonPressed("ax_button"), delta);
                    By.Update(controller.IsButtonPressed("by_button"), delta);
                    TriggerClick.Update(controller.IsButtonPressed("trigger_click"), delta);
                    Menu.Update(controller.IsButtonPressed("menu_button"), delta);
                }
                else
                {
                    Ax.Update(false, delta);
                    By.Update(false, delta);
                    TriggerClick.Update(false, delta);
                    Menu.Update(false, delta);
                }
            }
        }

        private readonly ControllerButtonState leftButtons = new ControllerButtonState();
        private readonly ControllerButtonState rightButtons = new ControllerButtonState();

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }

            Instance = this;

            // Loaded once per session, like the gamepad path: an autoload outlives every scene
            // change, and re-reading the file per frame would be pointless I/O.
            tuning = GamepadTuning.LoadSection("xr");
            Log($"[XRInput] tuning: {tuning}");

            ResolveControllers();
        }

        private void ResolveControllers()
        {
            // Find XROrigin3D in the active scene tree
            var xrOrigin = FindNodeByTypeRecursive<XROrigin3D>(GetTree().Root);
            if (xrOrigin != null)
            {
                GD.Print("HakoDroneXrInputManager: XROrigin3D found. Resolving controllers...");
                
                // Left controller
                leftController = FindXRControllerRecursive(xrOrigin, "left_hand");
                if (leftController == null)
                {
                    GD.Print("HakoDroneXrInputManager: Left controller not found under XROrigin3D. Creating LeftHand controller dynamically...");
                    var leftNode = new XRController3D();
                    leftNode.Name = "LeftHand";
                    leftNode.Tracker = "left_hand";
                    xrOrigin.AddChild(leftNode);
                    leftController = leftNode;
                }
                
                // Right controller
                rightController = FindXRControllerRecursive(xrOrigin, "right_hand");
                if (rightController == null)
                {
                    GD.Print("HakoDroneXrInputManager: Right controller not found under XROrigin3D. Creating RightHand controller dynamically...");
                    var rightNode = new XRController3D();
                    rightNode.Name = "RightHand";
                    rightNode.Tracker = "right_hand";
                    xrOrigin.AddChild(rightNode);
                    rightController = rightNode;
                }
                
                GD.Print($"HakoDroneXrInputManager: Left controller resolved = {(leftController != null)} (Tracker: {leftController?.Tracker}), Right = {(rightController != null)} (Tracker: {rightController?.Tracker})");

                if (debugLabelInstance == null)
                {
                    var camera = FindNodeByTypeRecursive<XRCamera3D>(xrOrigin);
                    if (camera != null)
                    {
                        SetupVrDebugPanel(camera);
                    }
                }
            }
        }

        public override void _Process(double delta)
        {
            if (debugLabelInstance != null && !GodotObject.IsInstanceValid(debugLabelInstance))
            {
                debugLabelInstance = null;
            }

            // If the references are invalid (due to scene changes), set them to null
            if (leftController != null && !GodotObject.IsInstanceValid(leftController))
            {
                leftController = null;
            }
            if (rightController != null && !GodotObject.IsInstanceValid(rightController))
            {
                rightController = null;
            }

            // Periodically attempt to resolve controllers if they are null
            if (leftController == null || rightController == null)
            {
                resolveTimer += delta;
                if (resolveTimer >= 1.0)
                {
                    resolveTimer = 0;
                    ResolveControllers();
                }
            }
            else
            {
                resolveTimer = 0;
            }

            // NOTE: ButtonState.Update() now runs in _PhysicsProcess so that edge flags
            // (IsJustPressed/Released) are generated and consumed in the same 60 Hz loop as
            // DroneControl.HandleInput(). The diagnostic below reads *held* state live from the
            // controllers, so it does not depend on Update() ordering.
            panelCheckTimer -= delta;
            bool logPanelState = panelCheckTimer <= 0;
            if (logPanelState)
            {
                panelCheckTimer = 1.0;
            }
            if (logPanelState && VerboseDiagnostics)
            {
                string lState = (leftController != null && GodotObject.IsInstanceValid(leftController)) ? "OK" : "NULL";
                string rState = (rightController != null && GodotObject.IsInstanceValid(rightController)) ? "OK" : "NULL";
                Vector2 ls = GetLeftStickInput();
                Vector2 rs = GetRightStickInput();
                // IMPORTANT: read the *held* state (IsButtonPressed) live from the controller.
                // IsJustPressed is a 1-frame edge and can never be caught by a 1 Hz sampler,
                // which is why every button previously showed False even while pressed.
                bool lValid = leftController != null && GodotObject.IsInstanceValid(leftController);
                bool rValid = rightController != null && GodotObject.IsInstanceValid(rightController);
                bool lx = lValid && leftController.IsButtonPressed("ax_button");   // physical X
                bool ly = lValid && leftController.IsButtonPressed("by_button");   // physical Y
                bool lt = lValid && leftController.IsButtonPressed("trigger_click");
                bool ra = rValid && rightController.IsButtonPressed("ax_button");  // physical A
                bool rb = rValid && rightController.IsButtonPressed("by_button");  // physical B
                bool rt = rValid && rightController.IsButtonPressed("trigger_click");
                LogDiag($"[XRInput] L:{lState} R:{rState} LS:({ls.X:F2},{ls.Y:F2}) RS:({rs.X:F2},{rs.Y:F2}) | held X:{lx} Y:{ly} LT:{lt} A:{ra} B:{rb} RT:{rt}");
            }

            // NOTE: Y-button recenter and trigger->menu transition are edge-driven inputs and now
            // run in _PhysicsProcess (below), together with the button Update, so they share the
            // same 60 Hz loop as DroneControl. Only the render-side HUD setup remains in _Process.

            // Dynamically setup the VR HUD panels in front of the camera
            string currentScenePath = GetTree().CurrentScene?.SceneFilePath;
            if (currentScenePath != null && currentScenePath.Contains("exam_course_"))
            {
                var activeScene = GetTree().CurrentScene;
                if (activeScene == null || !activeScene.IsNodeReady())
                {
                    return; // Wait until the current scene is fully loaded and initialized
                }

                var xrOrigin = FindNodeByTypeRecursive<XROrigin3D>(GetTree().Root);
                if (xrOrigin != null)
                {
                    var camera = FindNodeByTypeRecursive<XRCamera3D>(xrOrigin);
                    if (camera != null)
                    {
                        // Left-Bottom Main HUD (Drone telemetry: map, battery, altitude, etc.)
                        var hud = GetTree().Root.FindChild("VrSimpleHudSprite", true, false);
                        if (hud == null || !GodotObject.IsInstanceValid(hud))
                        {
                            SetupVrSimpleHud(camera);
                        }


                        // Top Sim Info HUD (Simulation clock, system status, elapsed time)
                        var infoHud = GetTree().Root.FindChild("VrSimInfoHudSprite", true, false);
                        if (infoHud == null || !GodotObject.IsInstanceValid(infoHud))
                        {
                            SetupVrSimInfoHud(camera);
                        }

                        // VR Camera Panels (Front, Downward, Side FPV viewports) projected on the right side
                        var cameraPanelsHud = camera.GetNodeOrNull("VrCameraPanelsHudSprite");
                        if (cameraPanelsHud == null || !GodotObject.IsInstanceValid(cameraPanelsHud))
                        {
                            SetupVrCameraPanelsHud(camera);
                        }

                        // VR Debug Log Panel projected on the left side (Default: OFF, toggleable)
                        var debugPanel = camera.GetNodeOrNull("VrDebugPanelSprite");
                        if (debugPanel == null || !GodotObject.IsInstanceValid(debugPanel))
                        {
                            SetupVrDebugPanel(camera);
                        }



                        // One-shot readback a few seconds later: is the camera panel black
                        // because the render target is black, or because the sprite is not
                        // showing it? See ProbeCameraPanels().
                        ProbeCameraPanels();
                    }
                }
            }
        }

        // Button edges (IsJustPressed/Released) are generated here in the physics loop so they are
        // consumed in the SAME 60 Hz frame as DroneControl.HandleInput() (which runs from
        // DroneAvatar._PhysicsProcess). Previously ButtonState.Update() ran in _Process (render rate,
        // 72-120 Hz on Quest) while the drone read the edges from _PhysicsProcess (60 Hz); the two
        // loops did not line up, so button edges were missed or read twice. That is exactly why
        // stick input (continuous, Update-independent) worked but button-driven actions
        // (ARM / flight-mode / magnet) did not. Autoloads process before scene nodes, so this
        // Update() runs before HandleInput() within the same physics frame.
        public override void _PhysicsProcess(double delta)
        {
            // Drop references that became invalid due to a scene change
            if (leftController != null && !GodotObject.IsInstanceValid(leftController)) leftController = null;
            if (rightController != null && !GodotObject.IsInstanceValid(rightController)) rightController = null;

            // Authoritative, single per-frame button update (source of truth for edge flags)
            leftButtons.Update(leftController, delta);
            rightButtons.Update(rightController, delta);

            // VR view recentering (left Y button, or the gamepad Y button)
            if (IsYButtonPressed() || GamepadPressed(gp => gp.IsYButtonPressed()))
            {
                XRServer.CenterOnHmd(XRServer.RotationMode.ResetButKeepTilt, true);
                Log("HakoDroneXrInputManager: VR View recentered to current head direction.");
            }

            // VR Debug Log Panel toggle (left X button, gamepad X button, or Keyboard D key)
            if (IsXButtonPressed() || GamepadPressed(gp => gp.IsXButtonPressed()) || Input.IsKeyPressed(Key.D))
            {
                var debugSprite = GetTree().Root.FindChild("VrDebugPanelSprite", true, false) as Sprite3D;
                if (debugSprite != null && GodotObject.IsInstanceValid(debugSprite))
                {
                    debugSprite.Visible = !debugSprite.Visible;
                    Log($"HakoDroneXrInputManager: VR Debug Panel toggled => Visible={debugSprite.Visible}");
                }
            }


            // Return to menu on the dedicated menu button while in a course scene.
            //
            // This used to be the triggers (Up/Down), which are also HandleCameraControl's tilt
            // binding -- the camera control could therefore never be switched on. Both devices have
            // a real menu button, so the actions no longer share one.
            string currentScenePath = GetTree().CurrentScene?.SceneFilePath;
            if (currentScenePath != null && currentScenePath.Contains("exam_course_"))
            {
                if (IsMenuButtonPressed() || GamepadPressed(gp => gp.IsMenuButtonPressed()))
                {
                    var bootScript = GD.Load<GDScript>("res://Scripts/app_bootstrapper.gd");
                    string targetScene = bootScript != null ? bootScript.Call("get_target_menu_scene").AsString() : "res://Scenes/top_menu_game_xr.tscn";

                    Log($"HakoDroneXrInputManager: menu button pressed in course scene. Transitioning to {targetScene}...");
                    var sceneChanger = GetNodeOrNull("/root/SceneChanger");
                    if (sceneChanger != null)
                    {
                        sceneChanger.Call("change_scene", targetScene);
                    }
                    else
                    {
                        GetTree().ChangeSceneToFile(targetScene);
                    }
                }
            }
        }

        // Recenter and return-to-menu must stay reachable from whatever the user is actually
        // holding: the headset is still on even when the drone is being flown with a gamepad, and
        // the pad has no other way to trigger either action. The gamepad autoload is registered
        // before this one, so its edge flags are already up to date for this physics frame.
        private static bool GamepadPressed(Func<IDroneInput, bool> read)
        {
            var gamepad = HakoDroneInputManager.Instance;
            if (gamepad == null) return false;
            if (GamepadProbe.GetActiveGamepadDevice() < 0) return false;
            return read(gamepad);
        }

        private void SetupVrSimpleHud(XRCamera3D camera)
        {
            var activeScene = GetTree().CurrentScene;
            if (activeScene == null) return;

            // Find the original InfoPanel in the scene
            var infoPanel = activeScene.GetNodeOrNull("GUI/CanvasLayer/InfoPanel");
            if (infoPanel == null)
            {
                GD.Print("HakoDroneXrInputManager: GUI/CanvasLayer/InfoPanel not found in current scene.");
                return;
            }

            GD.Print("HakoDroneXrInputManager: Setting up VR HUD from InfoPanel...");

            // 1. Create SubViewport for HUD rendering
            var viewport = new SubViewport();
            viewport.Size = new Vector2I(440, 240); // Height matched to map (~240px), width compacted to 440px
            viewport.TransparentBg = true;
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            camera.AddChild(viewport); // Must be in scene tree

            // 2. Create Control container with dark translucent background
            var control = new Control();
            control.CustomMinimumSize = new Vector2(440, 240);
            viewport.AddChild(control);

            var panel = new Panel();
            panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var styleBox = new StyleBoxFlat();
            styleBox.BgColor = new Color(0, 0, 0, 0.5f); // 50% opacity black
            styleBox.CornerRadiusTopLeft = 15;
            styleBox.CornerRadiusTopRight = 15;
            styleBox.CornerRadiusBottomLeft = 15;
            styleBox.CornerRadiusBottomRight = 15;
            panel.AddThemeStyleboxOverride("panel", styleBox);
            control.AddChild(panel);

            // 3. Move all 2D UI children of InfoPanel into this SubViewport Control container
            var children = new List<Node>();
            foreach (Node child in infoPanel.GetChildren())
            {
                children.Add(child);
            }

            foreach (var child in children)
            {
                infoPanel.RemoveChild(child);
                control.AddChild(child);

                // Reposition controls horizontally: Map -> Battery Bar (slimmer) -> Stacked Statuses
                if (child is Control c)
                {
                    c.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                    
                    if (c.Name == "DroneMap")
                    {
                        // 1. Leftmost (map height ~213px)
                        c.OffsetLeft = 20;
                        c.OffsetTop = 0;
                        c.Size = new Vector2(213, 213);
                    }
                    else if (c.Name == "TextureProgressBar_battery")
                    {
                        // 2. Battery progress bar (positioned next to Map, Y=15, scaled to 18% width, 30% height to be slim)
                        c.OffsetLeft = 240;
                        c.OffsetTop = 30;
                        c.Scale = new Vector2(0.18f, 0.3f);
                        c.Size = new Vector2(195, 195);
                    }
                    else if (c.Name == "VBoxContainer")
                    {
                        // 3. Alt/Speed status values (positioned on the right side, top - shifted left to X=300)
                        c.OffsetLeft = 300;
                        c.OffsetTop = 20;
                        c.CustomMinimumSize = new Vector2(130, 0);
                    }
                    else if (c.Name == "HBoxContainer")
                    {
                        // 4. Battery detailed values (positioned on the right side, bottom - aligned with Alt/Speed box at X=300)
                        c.OffsetLeft = 300;
                        c.OffsetTop = 20;
                        c.CustomMinimumSize = new Vector2(130, 0);
                    }
                }
            }

            // 4. Create Sprite3D to project the HUD viewport in VR
            var sprite = new Sprite3D();
            sprite.Name = "VrSimpleHudSprite";
            sprite.Texture = viewport.GetTexture();
            sprite.PixelSize = 0.0009f; // Reduced 3D physical size to 3/4 (from 0.0012f)
            sprite.Shaded = false;
            sprite.DoubleSided = true;
            sprite.Layers = 2; // Layer 2: Dedicated layer for VR HUD panel
            
            // Shifted even lower (Y=-0.58f) to clear central flight field of view
//            sprite.Position = new Vector3(-0.4f, -0.58f, -1.2f);
            sprite.Position = new Vector3(-0.4f, -0.58f, -0.5f);
            // Rotation: tilted up 10 deg, tilted right 18 deg to face player
            sprite.RotationDegrees = new Vector3(-10, 18, 0);

            camera.AddChild(sprite);

            // Hide the original 2D CanvasLayer since it's now projected in VR
            var canvasLayer = activeScene.GetNodeOrNull<CanvasLayer>("GUI/CanvasLayer");
            if (canvasLayer != null)
            {
                canvasLayer.Visible = false;
            }

            GD.Print("HakoDroneXrInputManager: VR HUD successfully created!");
        }

        private void SetupVrSimInfoHud(XRCamera3D camera)
        {
            var activeScene = GetTree().CurrentScene;

            if (activeScene == null) return;

            var originalControl = activeScene.GetNodeOrNull("GUI/CanvasLayer/Control");
            if (originalControl == null)
            {
                GD.Print("HakoDroneXrInputManager: GUI/CanvasLayer/Control not found in current scene.");
                return;
            }

            GD.Print("HakoDroneXrInputManager: Setting up VR Simulation Info HUD...");

            // 1. Create SubViewport for HUD rendering
            var viewport = new SubViewport();
            viewport.Size = new Vector2I(600, 200); // 3x larger viewport for 2 large labels
            viewport.TransparentBg = true;
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            camera.AddChild(viewport);

            // 2. Create Control container with dark translucent background
            var control = new Control();
            control.CustomMinimumSize = new Vector2(600, 200);
            viewport.AddChild(control);

            var panel = new Panel();
            panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var styleBox = new StyleBoxFlat();
            styleBox.BgColor = new Color(0, 0, 0, 0.5f); // 50% opacity black
            styleBox.CornerRadiusTopLeft = 30;
            styleBox.CornerRadiusTopRight = 30;
            styleBox.CornerRadiusBottomLeft = 30;
            styleBox.CornerRadiusBottomRight = 30;
            panel.AddThemeStyleboxOverride("panel", styleBox);
            control.AddChild(panel);

            // 3. Move Label nodes from GUI/CanvasLayer/Control into this SubViewport Control container
            var controlLabels = new List<Node>();
            foreach (Node child in originalControl.GetChildren())
            {
                if (child is Label)
                {
                    controlLabels.Add(child);
                }
            }

            foreach (var label in controlLabels)
            {
                originalControl.RemoveChild(label);
                control.AddChild(label);

                if (label is Label l)
                {
                    l.SetAnchorsPreset(Control.LayoutPreset.TopLeft);

                    if (l.Name == "Label_SimTimeH")
                    {
                        l.Visible = false;
                    }
                    else if (l.Name == "Label_time")
                    {
                        l.OffsetLeft = 40;
                        l.OffsetTop = 25;
                        l.AddThemeFontSizeOverride("font_size", 48); // 3x font size
                    }
                    else if (l.Name == "Label_status")
                    {
                        l.Visible = false;
                    }
                    else if (l.Name == "Label_altitude_2")
                    {
                        l.OffsetLeft = 40;
                        l.OffsetTop = 105;
                        l.AddThemeFontSizeOverride("font_size", 48); // 3x font size
                    }
                }
            }

            // 4. Create Sprite3D to project the HUD viewport in VR (Top-Center position)
            var sprite = new Sprite3D();
            sprite.Name = "VrSimInfoHudSprite";
            sprite.Texture = viewport.GetTexture();
            sprite.PixelSize = 0.0009f; // Match 3/4 scale
            sprite.Shaded = false;
            sprite.DoubleSided = true;
            sprite.Layers = 2; // Layer 2: Dedicated layer for VR HUD panels
            
            // Position: 0.0 meters (centered), 0.5 meters up, 1.2 meters in front
            sprite.Position = new Vector3(0.0f, 0.5f, -1.2f);
            // Rotation: tilted down 12 deg to face player
            sprite.RotationDegrees = new Vector3(12, 0, 0);

            camera.AddChild(sprite);

            GD.Print("HakoDroneXrInputManager: VR Sim Info HUD successfully created!");
        }

        private void SetupVrDebugPanel(XRCamera3D camera)
        {
            GD.Print("HakoDroneXrInputManager: Setting up VR Debug Log Panel...");
            
            // Clean up old instance if exists
            debugLabelInstance = null;

            // 1. Create SubViewport for Debug rendering
            var viewport = new SubViewport();
            viewport.Size = new Vector2I(400, 500); // 400x500 resolution
            viewport.TransparentBg = true;
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            camera.AddChild(viewport);

            // 2. Create Control container with dark background
            var control = new Control();
            control.CustomMinimumSize = new Vector2(400, 500);
            viewport.AddChild(control);

            var panel = new Panel();
            panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var styleBox = new StyleBoxFlat();
            styleBox.BgColor = new Color(0, 0, 0, 0.7f); // 70% opacity black
            styleBox.CornerRadiusTopLeft = 10;
            styleBox.CornerRadiusTopRight = 10;
            styleBox.CornerRadiusBottomLeft = 10;
            styleBox.CornerRadiusBottomRight = 10;
            styleBox.BorderWidthLeft = 2;
            styleBox.BorderWidthRight = 2;
            styleBox.BorderWidthTop = 2;
            styleBox.BorderWidthBottom = 2;
            styleBox.BorderColor = new Color(1f, 0.4f, 0.4f, 0.8f); // Reddish border for debug
            panel.AddThemeStyleboxOverride("panel", styleBox);
            control.AddChild(panel);

            // 3. Create RichTextLabel inside panel
            var richText = new RichTextLabel();
            richText.Name = "DebugLogLabel";
            richText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            richText.OffsetLeft = 15;
            richText.OffsetTop = 15;
            richText.OffsetRight = -15;
            richText.OffsetBottom = -15;
            richText.ScrollActive = true;
            richText.ScrollFollowing = true;
            richText.AddThemeFontSizeOverride("normal_font_size", 16); // Increased font size for readability
            richText.Text = string.Join("\n", logQueue);
            control.AddChild(richText);

            debugLabelInstance = richText;

            // 4. Create Sprite3D to project the HUD viewport in VR (Far Left position)
            var sprite = new Sprite3D();
            sprite.Name = "VrDebugPanelSprite";
            sprite.Texture = viewport.GetTexture();
            sprite.PixelSize = 0.0009f; // Match scale
            sprite.Shaded = false;
            sprite.DoubleSided = true;
            sprite.Layers = 2; // Layer 2: Dedicated layer for VR HUD panels
            
            // Shifted left to align its right edge with the left edge of simple HUD (-0.598f)
            // Width = 400 * 0.0009 = 0.36m, so position X = -0.598 - 0.18 = -0.778f
//            sprite.Position = new Vector3(-0.778f, -0.2f, -1.2f);
            sprite.Position = new Vector3(-0.378f, -0.2f, -1.2f);
            // Rotation adjusted to tilted right 30 deg to face player from further left
            sprite.RotationDegrees = new Vector3(-10, 30, 0);
            sprite.Visible = false; // Default OFF as requested

            camera.AddChild(sprite);


            GD.Print("HakoDroneXrInputManager: VR Debug Log Panel successfully created!");
        }

        private void SetupVrCameraPanelsHud(XRCamera3D camera)
        {
            var activeScene = GetTree().CurrentScene;
            if (activeScene == null) return;

            // Find the original VBoxContainer containing the camera viewports
            // Log(), not GD.Print(): the pilot reported "there is no camera view at all" on
            // 2026-07-26 and the pulled session log could not answer whether this ran, because
            // GD.Print only reaches logcat. These three lines are one-shot per scene.
            var vBoxContainer = activeScene.GetNodeOrNull("GUI/CanvasLayer/VBoxContainer");
            if (vBoxContainer == null)
            {
                Log("[CameraHud] GUI/CanvasLayer/VBoxContainer not found in current scene.");
                return;
            }

            // Each visible child is a SubViewportContainer holding one SubViewport with one
            // gimballed Camera3D. Their names and their order differ per course
            // (exam_course_3_xr has _topCam first, and the SubViewport is called "SubViewport2" in
            // some scenes), so both are found by type, never by name.
            //
            // The panels used to be shown by reparenting this whole VBoxContainer into a second,
            // code-created 640x960 SubViewport and putting THAT texture on one Sprite3D -- a
            // SubViewport nested inside a SubViewport. On 2026-07-26 (run17) the pilot reported
            // both panels black while the log showed the sprite being created correctly. The
            // nesting reproduces fine on desktop Vulkan, so it is not provably the cause, but it
            // is the one thing this HUD did that the two working panels (debug log, sim info) did
            // not, and it is unnecessary: a SubViewport with UpdateMode.Always keeps rendering to
            // its own target even when its container and the whole CanvasLayer are hidden
            // (verified on 4.7.1). So take each inner texture directly -- no nesting, no
            // reparenting, and the gimbal scripts keep the node paths they resolved at _ready.
            // The FIRST panel is the drone's own first-person viewport, not the VBox's "_front".
            //
            // run18's probe settled why _front was black: its Camera3D_front is ORTHOGONAL
            // (projection = 1, size = 2.0 in every course scene) and follow_full_transform pins it
            // to the drone's camera mount, 0.23 m above the floor. A 2 m orthographic box centred
            // there has two thirds of itself below the ground plane, so the panel showed the
            // underside of the floor -- mean rgba (0.009, 0.006, 0.001). The viewport was rendering
            // perfectly; there was simply nothing to see.
            //
            // DRAvatar2/DroneCamera/CameraController/SubViewportContainer/SubViewport is the real
            // thing: perspective, already rendering (it is the FPV window of the flat build), and
            // it is the camera HandleCameraControl's trigger tilt actually moves --
            // DefaultCameraController.SetupCameraHierarchy() reparents my_camera into it and
            // UpdateCameraAngle() drives it. _front only ever mirrored that camera's transform.
            var fpvViewport = FindSubViewportUnder(
                activeScene.GetNodeOrNull("DRAvatar2/DroneCamera/CameraController/SubViewportContainer"));

            int index = 0;
            if (fpvViewport != null)
            {
                var fpvContainer = fpvViewport.GetParent() as SubViewportContainer;
                if (fpvContainer != null)
                {
                    fpvContainer.RemoveChild(fpvViewport);
                    activeScene.AddChild(fpvViewport);
                    fpvContainer.Visible = false;
                }
                AddCameraPanel(camera, fpvViewport, "DroneCamera(FPV)", ref index);
            }
            else
            {
                Log("[CameraHud] drone FPV SubViewport not found; falling back to the VBox panels.");
            }

            foreach (Node child in vBoxContainer.GetChildren())
            {
                var container = child as SubViewportContainer;
                if (container == null || !container.Visible) continue;

                // Skip the orthographic stand-in once the real FPV view is in hand: it shows the
                // same camera pose, badly, and would just be a second black panel.
                if (fpvViewport != null && container.Name.ToString().Contains("front"))
                {
                    // Visible = false, not zero alpha: this one is NOT shown anywhere, so freezing
                    // its viewport is exactly what we want -- one less render target per frame.
                    container.Visible = false;
                    Log($"[CameraHud] {container.Name} skipped (orthographic; replaced by the FPV viewport).");
                    continue;
                }

                SubViewport panelViewport = FindSubViewportUnder(container);
                if (panelViewport == null)
                {
                    Log($"[CameraHud] {container.Name}: no SubViewport child, skipped.");
                    continue;
                }

                // Reparent SubViewport out of SubViewportContainer so it renders independently
                // without relying on SubViewportContainer's tree visibility, avoiding the
                // "Attempted to use the same texture in framebuffer attachment and a uniform" GPU error.
                if (panelViewport.GetParent() == container)
                {
                    container.RemoveChild(panelViewport);
                    activeScene.AddChild(panelViewport);
                }

                AddCameraPanel(camera, panelViewport, container.Name.ToString(), ref index);

                // Now that the SubViewport is reparented to activeScene, we can safely hide the SubViewportContainer.
                container.Visible = false;
            }

            if (index == 0)
            {
                Log($"[CameraHud] no panel source found "
                    + $"(VBox children={vBoxContainer.GetChildCount()}).");
                return;
            }

            cameraPanelProbeDueMsec = Time.GetTicksMsec() + CameraPanelProbeDelayMsec;
            Log($"[CameraHud] {index} panel(s) created; render-target probe every "
                + $"{CameraPanelProbePeriodMsec} ms.");
        }

        private static SubViewport FindSubViewportUnder(Node parent)
        {
            if (parent == null) return null;
            foreach (Node child in parent.GetChildren())
            {
                if (child is SubViewport sv) return sv;
            }
            return null;
        }

        private static void ExcludeVrHudLayerFromCamera(SubViewport viewport)
        {
            if (viewport == null) return;
            var camera = viewport.GetCamera3D();
            if (camera != null)
            {
                // Exclude Layer 2 (VR HUD panels layer) from the camera's cull mask (bitmask 2)
                // so the camera never renders VR HUD panels, preventing GPU texture feedback loops.
                camera.CullMask &= ~2U;
            }
        }

        private void AddCameraPanel(XRCamera3D camera, SubViewport panelViewport, string label, ref int index)
        {
            // The scenes already set 4 (Always); make it explicit so a scene edit cannot
            // silently leave a panel frozen on its first frame.
            panelViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

            // Prevent camera from rendering VR HUD panels (eliminates texture feedback loop)
            ExcludeVrHudLayerFromCamera(panelViewport);

            {
                var sprite = new Sprite3D();
                // The first sprite keeps the historic name: _Process() uses it to decide whether
                // this scene has been set up yet.
                sprite.Name = index == 0 ? "VrCameraPanelsHudSprite" : $"VrCameraPanelsHudSprite{index}";
                sprite.Texture = panelViewport.GetTexture();
                sprite.PixelSize = 0.0005f; // 640x480 -> ~32cm x 24cm in VR space
                sprite.Shaded = false;
                sprite.DoubleSided = true;
                sprite.Layers = 2; // Layer 2: Dedicated layer for VR HUD panels

                // Clearer, slightly more opaque modulate (85% opacity) for high visibility
                sprite.Modulate = new Color(1f, 1f, 1f, 0.85f);


                // Camera Viewport positions:
                // index 0 (FPV view): Lowered further to bottom right position (Y = -0.52f)
                // index 1 (TopCam view): Positioned next to the VR HUD panel
                if (index == 0)
                {
//                    sprite.Position = new Vector3(0.45f, -0.52f, -1.2f);
                    sprite.Position = new Vector3(0.45f, -0.52f, -0.5f);
                }
                else
                {
//                    sprite.Position = new Vector3(0.45f, -0.08f, -1.2f);
                    sprite.Position = new Vector3(0.45f, -0.08f, -0.5f);
                }

                sprite.RotationDegrees = new Vector3(-8, -28, 0);


                camera.AddChild(sprite);

                Log($"[CameraHud] {sprite.Name} <- {label}/{panelViewport.Name} "
                    + $"size={panelViewport.Size} at {sprite.Position}");

                cameraPanelProbes.Add(panelViewport);
                index++;
            }
        }

        /*
         * "The panel is there but it is black" cannot be told apart from "the panel is not there"
         * by any amount of setup logging -- run16 and run17 both logged a correctly created sprite
         * while the pilot saw nothing usable. This reads the render target back once, a few
         * seconds after setup, and prints its mean colour:
         *
         *   mean alpha ~0 or mean rgb ~0  -> the viewport itself renders nothing (camera / world /
         *                                    update-mode problem, the sprite is innocent)
         *   plausible colours              -> the target has the course in it and the problem is
         *                                    the sprite (placement, material, XR compositing)
         *
         * It REPEATS, at CameraPanelProbePeriodMsec, because the second half of the 2026-07-27
         * report -- "the original camera shows something, but the picture stops once the drone
         * moves" -- is about change over time and a single sample taken while the drone is still
         * on the ground cannot speak to it. Each line carries the camera's world position, so the
         * log answers "did the panel camera follow the airframe" directly.
         *
         * GetImage() is a GPU->CPU readback and stalls the frame, so the period is measured in
         * tens of seconds and the whole thing only runs with the diagnostics switched on.
         */
        private const ulong CameraPanelProbeDelayMsec = 4000;
        private const ulong CameraPanelProbePeriodMsec = 15000;
        private readonly List<SubViewport> cameraPanelProbes = new List<SubViewport>();
        private ulong cameraPanelProbeDueMsec = 0;

        private void ProbeCameraPanels()
        {
            if (cameraPanelProbeDueMsec == 0 || Time.GetTicksMsec() < cameraPanelProbeDueMsec) return;

            if (!VerboseDiagnostics)
            {
                cameraPanelProbeDueMsec = 0;
                cameraPanelProbes.Clear();
                return;
            }
            cameraPanelProbeDueMsec = Time.GetTicksMsec() + CameraPanelProbePeriodMsec;

            foreach (var viewport in cameraPanelProbes)
            {
                if (viewport == null || !GodotObject.IsInstanceValid(viewport)) continue;
                try
                {
                    var texture = viewport.GetTexture();
                    var image = texture?.GetImage();
                    if (image == null || image.IsEmpty())
                    {
                        Log($"[CameraHud] probe {viewport.Name}: no image.");
                        continue;
                    }

                    // Coarse grid: 32x32 samples is enough to tell "black" from "a course".
                    double r = 0, g = 0, b = 0, a = 0;
                    int n = 0;
                    int stepX = Math.Max(1, image.GetWidth() / 32);
                    int stepY = Math.Max(1, image.GetHeight() / 32);
                    for (int y = 0; y < image.GetHeight(); y += stepY)
                    {
                        for (int x = 0; x < image.GetWidth(); x += stepX)
                        {
                            var c = image.GetPixel(x, y);
                            r += c.R; g += c.G; b += c.B; a += c.A;
                            n++;
                        }
                    }
                    if (n == 0) continue;

                    var cam = viewport.GetCamera3D();
                    // -Z is forward in Godot: the direction tells a downward gimbal from a camera
                    // that has quietly ended up pointing at the sky (which reads as black under AR
                    // passthrough, and would look exactly like "the panel died").
                    string camInfo = cam == null
                        ? "camera=NONE"
                        : $"camera={cam.Name} current={cam.Current} pos={cam.GlobalPosition} "
                          + $"fwd={-cam.GlobalBasis.Z} proj={cam.Projection}";
                    Log($"[CameraHud] probe {viewport.Name}: mean rgba="
                        + $"({r / n:F3},{g / n:F3},{b / n:F3},{a / n:F3}) {camInfo}");
                }
                catch (Exception e)
                {
                    Log($"[CameraHud] probe {viewport.Name} failed: {e.Message}");
                }
            }
        }

        private T FindNodeByTypeRecursive<T>(Node node) where T : Node
        {
            if (node is T result)
            {
                return result;
            }

            foreach (Node child in node.GetChildren())
            {
                var found = FindNodeByTypeRecursive<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private XRController3D FindXRControllerRecursive(Node node, string trackerName)
        {
            if (node is XRController3D controller)
            {
                string trackerStr = controller.Tracker.ToString();
                if (trackerStr == trackerName)
                {
                    return controller;
                }
            }

            foreach (Node child in node.GetChildren())
            {
                var found = FindXRControllerRecursive(child, trackerName);
                if (found != null) return found;
            }
            return null;
        }

        public Vector2 GetLeftStickInput()
        {
            if (leftController != null && GodotObject.IsInstanceValid(leftController))
            {
                // In OpenXR, the analog thumbstick is commonly named "primary"
                Vector2 stick = leftController.GetVector2("primary");

                // Yaw: X (stick right is +1 in OpenXR). Throttle: Y (stick up is +1).
                // Yaw goes through ApplyYaw: this stick's X leaks ~0.34 while Y is held up, so the
                // yaw axis needs its own deadzone. See GamepadTuning.YawDeadzone.
                return new Vector2(
                    tuning.ApplyYaw(stick.X),
                    tuning.Apply(stick.Y, tuning.ThrottleSensitivity));
            }
            return Vector2.Zero;
        }

        public Vector2 GetRightStickInput()
        {
            if (rightController != null && GodotObject.IsInstanceValid(rightController))
            {
                Vector2 stick = rightController.GetVector2("primary");
                return new Vector2(
                    tuning.Apply(stick.X, tuning.RollSensitivity),
                    tuning.Apply(stick.Y, tuning.PitchSensitivity));
            }
            return Vector2.Zero;
        }

        // Real-world right hand maps to rightController (rightButtons)
        // ARM toggle is bound to the RIGHT A button ONLY. Previously this OR-ed in right B / right
        // trigger / left X / left Y, so unrelated actions (recenter, menu return, flight-mode change)
        // silently toggled ARM and left the drone in an unpredictable armed/disarmed state.
        public bool IsAButtonPressed() => rightButtons.Ax.IsJustPressed;
        public bool IsAButtonReleased() => rightButtons.Ax.IsJustReleased;

        public bool IsBButtonPressed() => rightButtons.By.IsJustPressed;
        public bool IsBButtonReleased() => rightButtons.By.IsJustReleased;

        // Real-world left hand maps to leftController (leftButtons)
        public bool IsXButtonPressed() => leftButtons.Ax.IsJustPressed;
        public bool IsXButtonReleased() => leftButtons.Ax.IsJustReleased;

        public bool IsYButtonPressed() => leftButtons.By.IsJustPressed;
        public bool IsYButtonReleased() => leftButtons.By.IsJustReleased;

        // Triggers
        // Left trigger = camera up, right trigger = camera down (DroneControl.HandleCameraControl).
        // These are no longer the menu-return binding -- see IsMenuButtonPressed.
        public bool IsUpButtonPressed() => leftButtons.TriggerClick.IsJustPressed; // Left trigger
        public bool IsUpButtonReleased() => leftButtons.TriggerClick.IsJustReleased;

        public bool IsDownButtonPressed() => rightButtons.TriggerClick.IsJustPressed; // Right trigger
        public bool IsDownButtonReleased() => rightButtons.TriggerClick.IsJustReleased;

        // Left controller only in practice; the right binding is the system button and never fires.
        public bool IsMenuButtonPressed() => leftButtons.Menu.IsJustPressed || rightButtons.Menu.IsJustPressed;

        public void DoVibration(bool isRightHand, float frequency, float amplitude, float durationSec)
        {
            var controller = isRightHand ? rightController : leftController;
            if (controller != null && GodotObject.IsInstanceValid(controller))
            {
                controller.TriggerHapticPulse("haptic", frequency, amplitude, durationSec, 0f);
            }
        }
    }
}