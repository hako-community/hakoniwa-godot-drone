using System;
using hakoniwa.objects.core;
using hakoniwa.pdu.interfaces;
using hakoniwa.sim;
using Godot;

namespace hakoniwa.drone.sim
{
    public partial class BaggageGrabber : Node
    {
        [Export]
        public string pdu_name_cmd_magnet = "hako_cmd_magnet_holder";
        [Export]
        public string pdu_name_status_magnet = "hako_status_magnet_holder";
        private string robotName;
        private Magnet magnet;

        public void DoInitialize(string robot_name, IHakoPdu hakoPdu)
        {
            this.robotName = robot_name;
            var ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_cmd_magnet);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_cmd_magnet}");
            }
            ret = hakoPdu.DeclarePduForWrite(robotName, pdu_name_status_magnet);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_status_magnet}");
            }
            magnet = NodeUtil.FindNodeByInterface<Magnet>(this);
            if(magnet == null)
            {
                throw new ArgumentException($"Can not find Magnet on: {robotName}");
            }
        }

        public void DoControl(IPduManager pduManager)
        {
            IPdu pdu_cmd_magnet = pduManager.ReadPdu(robotName, pdu_name_cmd_magnet);
            if (pdu_cmd_magnet != null) {
                var cmd_magnet = new hakoniwa.pdu.msgs.hako_msgs.HakoCmdMagnetHolder(pdu_cmd_magnet);
                if (cmd_magnet.header.request)
                {
                    GD.Print("magnet cmd is received");
                    if (cmd_magnet.magnet_on)
                    {
                        this.Grab(true);
                    }
                    else
                    {
                        this.Release();
                    }
                }
            }
            var controller = GameController.Instance;
            if (controller != null && controller.GetRadioControlOn())
            {
                if (controller.GetGrabBaggageOn())
                {
                    this.Grab(true);
                }
                else
                {
                    this.Release();
                }
            }

            INamedPdu pdu_status_magnet = pduManager.CreateNamedPdu(robotName, pdu_name_status_magnet);
            if (pdu_name_status_magnet == null) {
                throw new ArgumentException($"Can not create pdu for write: {robotName} {pdu_name_status_magnet}");
            }
            var status_magnet = new hakoniwa.pdu.msgs.hako_msgs.HakoStatusMagnetHolder(pdu_status_magnet);
            status_magnet.magnet_on = magnet.IsMagnetOn();
            status_magnet.contact_on = magnet.IsConntactOn();
            pduManager.WriteNamedPdu(pdu_status_magnet);
            pduManager.FlushNamedPdu(pdu_status_magnet);
        }

        private void Grab(bool forceOn)
        {
            if (forceOn)
            {
                magnet.TurnOnForce();
            }
            else
            {
                magnet.TurnOn();
            }
        }

        private void Release()
        {
            magnet.TurnOff();
        }
    }
}