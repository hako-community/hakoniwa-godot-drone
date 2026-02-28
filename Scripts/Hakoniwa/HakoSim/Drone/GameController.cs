using System;
using hakoniwa.pdu.interfaces;
using hakoniwa.sim;
using Godot;

namespace hakoniwa.drone.sim;

public partial class GameController : Node
{
	public static GameController Instance { get; private set; }

	public override void _EnterTree()
	{
		if (Instance == null)
		{
			Instance = this;
		}
	}

	private bool is_radio_control = false;

	[Export]
	public string pdu_name_cmd_game = "hako_cmd_game";
	private string robotName;
	private bool[] button_array = new bool[16];

	public bool GetGrabBaggageOn()
	{
		return button_array[DroneControlPdu.game_ops_grab_baggage_button_index];
	}
	public bool GetCameraShotOn()
	{
		return button_array[DroneControlPdu.game_ops_camera_button_index];
	}
	public bool GetCameraMoveUp()
	{
		return button_array[DroneControlPdu.game_ops_camera_move_up_index];
	}
	public bool GetCameraMoveDown()
	{
		return button_array[DroneControlPdu.game_ops_camera_move_down_index];
	}
	public bool GetRadioControlOn()
	{
		return is_radio_control;
	}

	public void DoInitialize(string robot_name, IHakoPdu hakoPdu)
	{
		GD.Print("GameController Initialize");
		this.robotName = robot_name;
		var ret = hakoPdu.DeclarePduForRead(robot_name, pdu_name_cmd_game);
		if (ret == false)
		{
			throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_cmd_game}");
		}
		button_array = new bool[16];
	}
	public void DoControl(IPduManager pduManager)
	{
		IPdu pdu_cmd_game_ctrl = pduManager.ReadPdu(robotName, pdu_name_cmd_game);
		if (pdu_cmd_game_ctrl != null)
		{
			var cmd_game_ctrl = new hakoniwa.pdu.msgs.hako_msgs.GameControllerOperation(pdu_cmd_game_ctrl);
			for (int i = 0; i < cmd_game_ctrl.button.Length; i++)
			{
				button_array[i] = cmd_game_ctrl.button[i];
			}
			if (button_array[DroneControlPdu.game_ops_arm_button_index])
			{
				is_radio_control = true;
			}
		}
	}
}
