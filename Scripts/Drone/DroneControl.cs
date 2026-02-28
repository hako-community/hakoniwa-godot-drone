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
		[Export]
		public double stick_strength = 15.0;
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
//				droneControlOp = FindComponent<IDroneControlOp>(this);
				droneControlOp = FindNodeByInterface<IDroneControlOp>(this);
			}
			if (droneControlOp != null)
			{
				droneControlOp.DoInitialize(robotName);
			}

			if (grabberObject != null)
			{
//				grabber = FindComponent<IBaggageGrabber>(grabberObject);
				grabber = FindNodeByInterface<IBaggageGrabber>(grabberObject);
				GD.Print("grabber: " + grabber);
			}
			else
			{
				GD.Print("Grabber is not found.");
			}

			if (input_type == DroneControlInputType.PS4)
			{
				controller_input = HakoDroneInputManager.Instance;
			}
			else if (input_type == DroneControlInputType.Xbox)
			{
				//TODO
			}
			else
			{
				controller_input = HakoDroneXrInputManager.Instance;
			}
		}

		private T FindComponent<T>(Node node) where T : class
		{
			if (node is T found) return found;
			foreach (Node child in node.GetChildren())
			{
				var result = FindComponent<T>(child);
				if (result != null) return result;
			}
			return null;
		}

        public T FindNodeByInterface<T>(Node root) where T : class
        {
            if (root is T found) return found;

            foreach (Node child in root.GetChildren())
            {
                var result = FindNodeByInterface<T>(child);
                if (result != null) return result;
            }
            return null;
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
		private bool is_armed = false;
		public void HandleInput()
		{
			if (controller_input == null || droneControlOp == null) return;

			Vector2 leftStick = controller_input.GetLeftStickInput();
			Vector2 rightStick = controller_input.GetRightStickInput();
			float horizontal = rightStick.X;
			float forward = rightStick.Y;
			float yaw = leftStick.X;
			float pitch = leftStick.Y;

			bool mag_on = false;
			bool mag_req = droneControlOp.GetMagnetRequest(out mag_on);
			if (controller_input.IsAButtonPressed())
			{
				is_armed = !is_armed;
				droneControlOp.PutRadioControlButton(0, is_armed ? 1 : 0);
			}

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
			droneControlOp.PutHorizontal(0, horizontal * stick_strength);
			droneControlOp.PutForward(0, -forward * stick_strength);
			droneControlOp.PutHeading(0, -yaw * stick_yaw_strength);
			droneControlOp.PutVertical(0, pitch * stick_strength);

			droneControlOp.DoFlush();
		}

	}
}
