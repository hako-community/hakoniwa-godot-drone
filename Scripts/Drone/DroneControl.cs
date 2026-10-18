using hakoniwa.drone.sim;
using hakoniwa.objects.core;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.interfaces;
using Godot;
using System;

namespace hakoniwa.drone
{
	public enum DroneControlInputType
	{
		PS4,
		Xbox,
		Xr
	}
	public interface IDroneControlOp
	{
		public void DoInitialize(string robotName);
		public int PutRadioControlButton(int index, int value);
		public int PutFlightModeChangeButton(int index, int value);
		public int PutHorizontal(int index, double value);
		public int PutForward(int index, double value);
		public int PutHeading(int index, double value);
		public int PutVertical(int index, double value);
		public bool GetMagnetRequest(out bool magnet_on);
		public void PutMagnetStatus(bool magnet_on, bool contact_on);
		public void DoFlush();
	}

	public partial class DroneControl : Node
	{
		[Export]
		public string robotName = "Drone";
		[Export]
		public DroneControlInputType input_type;

		/*
		 * Horizontal: full stick = target GROUND SPEED in m/s.
		 *
		 * param-api-mixer.txt has ANGLE_CONTROL_ENABLE 0, so PutHorizontal/PutForward feed the
		 * horizontal SPEED loop, not an attitude. This was 15.0, and the airframe cannot do
		 * 15 m/s: PID_POS_MAX_ROLL/PITCH cap the tilt at 15 deg, which tops out around 4.5 m/s
		 * (measured maximum across every logged flight: 4.93 m/s). The speed loop was therefore
		 * saturated for any stick deflection past roughly a third, which caused BOTH reported
		 * symptoms:
		 *
		 *   - no proportional feel: past ~1/3 stick every input produced the same 15 deg tilt;
		 *   - "forward and roll get mixed" (2026-07-26 pilot report): with PID_POS_VX/VY_Kp = 20
		 *     an error of only 0.75 m/s already saturates an axis, so the cross axis saturates
		 *     too, and the attitude lands on the CORNER of the +/-15 deg clip square -- a 45 deg
		 *     diagonal, whatever direction was commanded.
		 *
		 * That corner effect is measured, not inferred. Across every run17/16/15/13/11/10/9
		 * sample (command >= 5, speed >= 1.5 m/s), comparing the commanded direction with the
		 * body-frame velocity the simulator reports:
		 *
		 *     both |roll| and |pitch| >= 14.4 deg : n=170  direction error median +41.4 deg
		 *     exactly one axis saturated          : n=759  direction error median   0.0 deg
		 *
		 * and 166 of those 170 were commanded within 20 deg of a pure axis. Single-axis bench
		 * tests (run9/run10) never saturated both axes, which is why they looked clean and the
		 * fault only showed up in continuous flying.
		 *
		 * 3.0 m/s is inside the achievable range, so the loop leaves saturation and the tilt
		 * settles at whatever angle sustains the commanded speed.
		 */
		[Export]
		public double stick_strength = 3.0;

		/*
		 * Vertical keeps the old 15.0. PutVertical feeds the altitude speed loop, and the
		 * `throttle` sensitivity already scales it into range: 15 * 0.18 = 2.7 m/s against
		 * PID_ALT_MAX_SPD 5.5. run17 confirms that loop is NOT saturated -- commanded 0.4 / 0.9 /
		 * 1.9 / 2.5 m/s produced measured climb rates of 0.40 / 0.93 / 1.91 / 2.46 m/s.
		 * Sharing one constant with the horizontal axis would have cut the climb rate to 0.54 m/s.
		 */
		[Export]
		public double stick_vertical_strength = 15.0;

		/*
		 * Yaw. The unit has no counterpart in param-api-mixer.txt (roll/pitch's 15.0 matches
		 * PID_POS_MAX_ROLL/PITCH); this number is empirical. run17 measures the response at
		 * ~87 deg/s per unit of command, so the tuning file's `yaw` sensitivity sets the
		 * full-stick rate: 0.014 * 100 * 87 = ~120 deg/s, which is the training figure.
		 * Retune it there (no rebuild), not here.
		 */
		[Export]
		public double stick_yaw_strength = 100.0;
		private IDroneInput controller_input;
		public bool magnet_on = false;
		[Export]
		public Node grabberObject;
		private IBaggageGrabber grabber;
		[Export]
		public bool forceGrab = false;
		private IDroneControlOp droneControlOp = null;
		[Export]
		public bool isPlayer = true;

		public IDroneInput GetDroneInput()
		{
			return controller_input;
		}

		public bool IsMagnetOn()
		{
			return magnet_on;
		}

		public override void _Ready()
		{
			if (isPlayer)
			{
				droneControlOp = new DroneControlRc();
			}
			else
			{
				droneControlOp = NodeUtil.FindNodeByInterface<IDroneControlOp>(this);
			}
			if (droneControlOp != null)
			{
				droneControlOp.DoInitialize(robotName);
			}

			if (grabberObject != null)
			{
				grabber = NodeUtil.FindNodeByInterface<IBaggageGrabber>(grabberObject);
				GD.Print("grabber: " + grabber);
			}
			else
			{
				GD.Print("Grabber is not found.");
			}

			controller_input = ResolveControllerInput();
			GD.Print($"[DroneControl] _Ready: input_type={input_type}, controller_input={controller_input?.GetType().Name ?? "null"}");
		}
            
		public float move_step = 1.0f; 
		private float camera_move_button_time_duration = 0f;
		public float camera_move_button_threshold_speedup = 1.0f;
		public bool is_pressed_up = false;
		public bool is_pressed_down = false;

		public void HandleCameraControl(ICameraController camera_controller, IPduManager pduManager)
		{
			if (controller_input == null) return;
			{
				/*
				 * Camera Image Rc request
				 */
				if (controller_input.IsYButtonPressed() && pduManager != null)
				{
					GD.Print("SHOT!!");
					camera_controller.Scan();
					camera_controller.WriteCameraDataPdu(pduManager);
				}
				if (controller_input.IsUpButtonPressed())
				{
					is_pressed_up = true;
				}
				else if (controller_input.IsUpButtonReleased())
				{
					is_pressed_up = false;
				}
				
				float fixedDeltaTime = (float)GetPhysicsProcessDeltaTime();

				if (is_pressed_up)
				{
					camera_move_button_time_duration += fixedDeltaTime;
					if (camera_move_button_time_duration > camera_move_button_threshold_speedup)
					{
						camera_controller.RotateCamera(-move_step * 3f);
					}
					else
					{
						camera_controller.RotateCamera(-move_step);
					}

				}
				if (controller_input.IsDownButtonPressed())
				{
					is_pressed_down = true;
				}
				else if (controller_input.IsDownButtonReleased())
				{
					is_pressed_down = false;
				}


				if (is_pressed_down)
				{
					camera_move_button_time_duration += fixedDeltaTime;
					if (camera_move_button_time_duration > camera_move_button_threshold_speedup)
					{
						camera_controller.RotateCamera(move_step * 3f);
					}
					else
					{
						camera_controller.RotateCamera(move_step);
					}
				}

				if (!is_pressed_down && !is_pressed_up)
				{
					camera_move_button_time_duration = 0f;
				}
			}
		}
		// Picks the input implementation for the current frame.
		//
		// PHASE 0 (spike): GamepadProbe.ForceGamepadInput -- toggled on-device with LB+RB -- wins
		// over input_type so the gamepad can be flown without a rebuild. It falls back to the
		// XR controllers the moment the pad disappears, so a Bluetooth dropout mid-flight does
		// not leave the drone uncontrollable.
		// Phase 1 replaces this whole method with DroneInputResolver + DroneParam.
		private IDroneInput ResolveControllerInput()
		{
			// NOTE: "any joypad connected" is not a usable test on a Quest -- the Touch controllers
			// themselves enumerate as Android gamepads. GetActiveGamepadDevice() filters them out.
			if (GamepadProbe.ForceGamepadInput && GamepadProbe.GetActiveGamepadDevice() >= 0)
			{
				return HakoDroneInputManager.Instance;
			}

			if (input_type == DroneControlInputType.PS4 || input_type == DroneControlInputType.Xbox)
			{
				return HakoDroneInputManager.Instance;
			}
			return HakoDroneXrInputManager.Instance;
		}

		private bool is_armed = false;
		private bool _prevArmKey = false;   // ★ キーボードの Space（アーム）の前回の状態
		private bool _hasSpokenArmGuide = false;
		[Export] public bool IsArmedProp { get => is_armed; set => is_armed = value; }
		public bool IsArmed() { return is_armed; }
		private double _diagTimer = 0;
		// 5 Hz. Keep in sync with DroneAvatar.ResponseLogPeriod: the two lines are read as pairs,
		// and at the original 1 Hz a whole stick sweep could happen between samples, which made
		// the measured command -> response gain meaningless.
		private const double DiagLogPeriod = 0.2;
		// false so that the first frame with a working input path logs one confirming line.
		private bool _wasWired = false;
		private int _armEventCount = 0;
		public void HandleInput()
		{
			// Re-resolve every frame: the Phase 0 toggle and Bluetooth connect/disconnect can both
			// change the answer at any time.
			controller_input = ResolveControllerInput();

			// --- DIAGNOSTIC (throttled ~1 Hz, routed to VR debug panel via Log) ---
			// Confirms whether HandleInput is actually running and correctly wired on-device.
			// The full line is emitted after the Put* calls below so that the *sent* values
			// (not just the raw sticks) are visible -- that is what makes an inverted or
			// mis-scaled axis identifiable from a single log line.
			_diagTimer += GetPhysicsProcessDeltaTime();
			bool logDiag = _diagTimer >= DiagLogPeriod;
			if (logDiag)
			{
				_diagTimer = 0;
			}
			// The per-sample line is a diagnostic, so it follows the verbose switch
			// (HakoDroneXrInputManager.VerboseDiagnostics). The "not wired" line below does NOT --
			// it reports a broken input path, which must show up in every log -- but it is emitted
			// on the transition instead of at 5 Hz, which is both quieter and easier to read.
			logDiag = logDiag && hakoniwa.objects.core.HakoDroneXrInputManager.VerboseDiagnostics;

			bool wired = controller_input != null && droneControlOp != null;
			if (wired != _wasWired)
			{
				_wasWired = wired;
				string ciName = controller_input?.GetType().Name ?? "null";
				string opName = (droneControlOp == null) ? "null" : "ok";
				hakoniwa.objects.core.HakoDroneXrInputManager.Log(wired
					? $"[HandleInput] type={input_type} ci={ciName} op={opName} (input path wired)"
					: $"[HandleInput] type={input_type} ci={ciName} op={opName} (input path NOT wired)");
			}
			if (!wired)
			{
				return;
			}

			Vector2 leftStick = controller_input.GetLeftStickInput();
			Vector2 rightStick = controller_input.GetRightStickInput();
			float horizontal = rightStick.X;
			float forward = rightStick.Y;
			float yaw = leftStick.X;
			float pitch = leftStick.Y;

			bool mag_on = false;
			bool mag_req = droneControlOp.GetMagnetRequest(out mag_on);
			// ★ 2026-10-04: ゲームパッドの A（PS 系は ×）でのアーム／ディスアームはやめた（ユーザ決定「アームはスティックの倒し込みのみでそろえる」）。
			//   ゲームパッドのアームは両スティックを内側下へ倒して 1 秒（CSC・hakodrone の RcPilot）＝ オンライン（swarm_app）と同じ。
			//   ★ ただし**キーボードの Space は残す**（キーボードだけでは倒し込みができず、飛べなくなるため。ユーザ指示）。
			bool spaceNow = Godot.Input.IsKeyPressed(Godot.Key.Space);
			if (spaceNow && !_prevArmKey)
			{
				is_armed = !is_armed;
				_armEventCount++;
				hakoniwa.objects.core.HakoDroneXrInputManager.Log($"[DroneControl] Space pressed. Virtual Arm State: {is_armed} (event #{_armEventCount})");
				droneControlOp.PutRadioControlButton(0, 1);
			}
			else if (!spaceNow && _prevArmKey)
			{
				droneControlOp.PutRadioControlButton(0, 0);
			}
			_prevArmKey = spaceNow;

			if (controller_input.IsXButtonPressed())
			{
				droneControlOp.PutFlightModeChangeButton(0, 1);
			}
			else if (controller_input.IsXButtonReleased())
			{
				droneControlOp.PutFlightModeChangeButton(0, 0);
			}
			if (controller_input.IsBButtonReleased())
			{
				GD.Print("Bbutton released");
				magnet_on = !magnet_on;
				if (grabber != null)
				{
					if (magnet_on)
					{
						grabber.Grab(forceGrab);
					}
					else
					{
						grabber.Release();
					}
				}

			}
			else if (mag_req)
			{
				if (mag_on)
				{
					GD.Print("Request: mag_on");
					if (grabber != null) grabber.Grab(forceGrab);
				}
				else
				{
					GD.Print("Request: mag_off");
					if (grabber != null) grabber.Release();
				}
				magnet_on = mag_on;
			}
			if (grabber  != null)
			{
				droneControlOp.PutMagnetStatus(magnet_on, grabber.IsGrabbed());
			}
			// Stick convention (both IDroneInput implementations): up = +Y, right = +X.
			// HakoDroneInputManager negates Godot's joy axis on Y only (X already reads right = +);
			// HakoDroneXrInputManager gets it natively from OpenXR.
			//
			// The native RC API is in RC/aviation axes, which are the exact negation of the FLU
			// axes its state getters report (GetPosition/GetAttitude use x forward, y left, z up):
			//
			//     Put*(positive)  ==  backward / right / yaw right / descend
			//
			// So the two axes whose stick direction agrees with that (right, and yaw right) need
			// NO negation, and the two that oppose it (stick up = forward, stick up = climb) do.
			//
			// This was measured in run9, not assumed -- see devai/ROLL_INVERSION_AND_STUCK_DRONE.md
			// section 9. Every earlier attempt tried to make all four axes look alike:
			//   2026-07-25  PutVertical was not negated -> stick-up descended.  Negating it: correct.
			//   2026-07-26  PutHorizontal was negated to match -> stick-right flew to the airframe's
			//               LEFT at every heading (run9: Horiz=-15 -> vRight=-3.5 at yaw=2, 178 and
			//               -167 alike). Reverted here.
			//   2026-07-26  PutHeading had the same unneeded negation -> stick-left yawed right
			//               (run9 18:55:32, LS.X=-0.12 held 5 s, heading ran 59 deg -> -74 deg).
			//
			// Note that the roll reversal the pilot sees when the nose is turned 180 deg is NOT a
			// bug: the command is body-referenced, exactly like a real aircraft. run9 confirms the
			// frame directly -- the sign of vRight follows the command sign at every heading.
			double outHorizontal = horizontal * stick_strength;
			double outForward = -forward * stick_strength;
			double outHeading = yaw * stick_yaw_strength;
			double outVertical = -pitch * stick_vertical_strength;

			droneControlOp.PutHorizontal(0, outHorizontal);
			droneControlOp.PutForward(0, outForward);
			droneControlOp.PutHeading(0, outHeading);
			droneControlOp.PutVertical(0, outVertical);

			droneControlOp.DoFlush();

			if (logDiag)
			{
				string ci = controller_input.GetType().Name;
				// F3, not F2: the per-axis sensitivity multipliers scale the conditioned stick well
				// below 1.0 (yaw is at 0.12), so two decimals left only ~12 distinguishable steps
				// across the whole axis and made the stick position unrecoverable from the log.
				hakoniwa.objects.core.HakoDroneXrInputManager.LogDiag(
					$"[HandleInput] type={input_type} ci={ci} armed={is_armed} armEvents={_armEventCount} " +
					$"LS=({leftStick.X:F3},{leftStick.Y:F3}) RS=({rightStick.X:F3},{rightStick.Y:F3}) " +
					$"-> Horiz={outHorizontal:F2} Fwd={outForward:F2} Yaw={outHeading:F2} Vert={outVertical:F2}");
			}
		}

	}
}
