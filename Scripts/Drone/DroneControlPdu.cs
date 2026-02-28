using hakoniwa.drone.sim;
using hakoniwa.objects.core;
using hakoniwa.pdu.core;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.hako_msgs;
using hakoniwa.sim;
using System;
using System.Threading.Tasks;
using Godot;

namespace hakoniwa.drone
{
    public partial class DroneControlPdu : Node, IDroneControlOp, IHakoniwaWebObject
    {
        public const int game_ops_arm_button_index = 0;
        public const int game_ops_grab_baggage_button_index = 1;
        public const int game_ops_camera_button_index = 2;
        public const int game_ops_flight_mode_change_index = 3;
        public const int game_ops_camera_move_up_index = 11;
        public const int game_ops_camera_move_down_index = 12;

        public const int game_ops_stick_turn_lr = 0;
        public const int game_ops_stick_up_down = 1;
        public const int game_ops_stick_move_lr = 2;
        public const int game_ops_stick_move_fb = 3;

        private string robotName = "Drone";
        private string pdu_name = "hako_cmd_game";
        private IPduManager pdu_manager;
        private bool[] button;
        private double[] axis;

        //magnet
        public bool useMagnet = true;
        public string pdu_name_cmd_magnet = "hako_cmd_magnet_holder";
        public string pdu_name_status_magnet = "hako_status_magnet_holder";
        private bool status_magnet_magnet_on = false;
        private bool status_magnet_contact_on = false;

        void Awake()
        {
            axis = new double[6];
            button = new bool[15];

        }

        public async Task DeclarePduAsync()
        {
            pdu_manager = WebServerBridge.Instance.Get();
            if (pdu_manager == null)
            {
                throw new Exception("Can not get Pdu Manager");
            }
            var ret = await pdu_manager.DeclarePduForWrite(robotName, pdu_name);
            GD.Print("declare pdu hako_cmd_game: " + ret);

            /*
             * Magnet
             */
            if (useMagnet)
            {

                ret = await pdu_manager.DeclarePduForRead(robotName, pdu_name_cmd_magnet);
                if (ret == false)
                {
                    throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_cmd_magnet}");
                }
                ret = await pdu_manager.DeclarePduForWrite(robotName, pdu_name_status_magnet);
                if (ret == false)
                {
                    throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_status_magnet}");
                }
            }
        }

        public void DoFlush()
        {
            if (pdu_manager == null)
            {
                return;
            }
            var pdu_cmd_game_ctrl = pdu_manager.CreateNamedPdu(robotName, pdu_name);
            if (pdu_cmd_game_ctrl == null)
            {
                return;
            }
            var cmd_game_ctrl = new hakoniwa.pdu.msgs.hako_msgs.GameControllerOperation(pdu_cmd_game_ctrl);
            cmd_game_ctrl.button = button;
            cmd_game_ctrl.axis = axis;

            //GD.Print("Abutton: button: " + button[game_ops_arm_button_index] + " pdu: " + cmd_game_ctrl.button[game_ops_arm_button_index]);
            pdu_manager.WriteNamedPdu(pdu_cmd_game_ctrl);
            pdu_manager.FlushNamedPdu(pdu_cmd_game_ctrl);


            /*
             * Magnet
             */
            if (useMagnet)
            {
                INamedPdu pdu_status_magnet = pdu_manager.CreateNamedPdu(robotName, pdu_name_status_magnet);
                if (pdu_name_status_magnet == null)
                {
                    throw new ArgumentException($"Can not create pdu for write: {robotName} {pdu_name_status_magnet}");
                }
                var status_magnet = new hakoniwa.pdu.msgs.hako_msgs.HakoStatusMagnetHolder(pdu_status_magnet);
                status_magnet.magnet_on = status_magnet_magnet_on;
                status_magnet.contact_on = status_magnet_contact_on;
                pdu_manager.WriteNamedPdu(pdu_status_magnet);
                pdu_manager.FlushNamedPdu(pdu_status_magnet);
            }

        }

        public void DoInitialize(string robotName)
        {
            this.robotName = robotName;
        }

        public int PutForward(int index, double value)
        {
            axis[game_ops_stick_move_fb] = value;
            return 0;
        }

        public int PutHeading(int index, double value)
        {
            axis[game_ops_stick_turn_lr] = value;
            return 0;
        }

        public int PutHorizontal(int index, double value)
        {

            axis[game_ops_stick_move_lr] = value;
            return 0;
        }
        public int PutVertical(int index, double value)
        {
            axis[game_ops_stick_up_down] = value;
            return 0;
        }

        public int PutRadioControlButton(int index, int value)
        {
            GD.Print("PutRadioControlButton: value = " + value);
            button[game_ops_arm_button_index] = (value != 0);
            return 0;
        }

        public bool GetMagnetRequest(out bool magnet_on)
        {
            magnet_on = false;
            if (pdu_manager == null)
            {
                return false;
            }
            IPdu pdu_cmd_magnet = pdu_manager.ReadPdu(robotName, pdu_name_cmd_magnet);
            if (pdu_cmd_magnet != null)
            {
                var cmd_magnet = new hakoniwa.pdu.msgs.hako_msgs.HakoCmdMagnetHolder(pdu_cmd_magnet);
                if (cmd_magnet.header.request)
                {
                    magnet_on = cmd_magnet.magnet_on;
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public void PutMagnetStatus(bool magnet_on, bool contact_on)
        {
            status_magnet_magnet_on = magnet_on;
            status_magnet_contact_on = contact_on;
        }

        public int PutFlightModeChangeButton(int index, int value)
        {
            GD.Print("PutFlightModeChangeButton: value = " + value);
            button[game_ops_flight_mode_change_index] = (value != 0);
            return 0;
        }
    }

}
