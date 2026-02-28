using Godot;
using System.Collections;
using hakoniwa.sim;
using System;
using hakoniwa.sim.core;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.geometry_msgs;

namespace hakoniwa.ar.bridge
{
    public partial class HakoniwaAvatar : Node, IHakoObject
    {
        IHakoPdu hakoPdu;
        [Export]
        public string robotName = "Player1";
        [Export]
        public string pdu_name = "head";
        [Export]
        public Node3D body;

        public void EventInitialize()
        {
            GD.Print("Event Initialize");
            if (body == null)
            {
                throw new Exception("Body is not assigned");
            }
            hakoPdu = HakoAsset.GetHakoPdu();
            /*
             * Position
             */
            var ret = hakoPdu.DeclarePduForRead(robotName, pdu_name);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name}");
            }
        }

        public void EventReset()
        {
            //nothing to do
        }

        public void EventStart()
        {
            //nothing to do
        }

        public void EventStop()
        {
            //nothing to do
        }

        public void EventTick()
        {
            var pduManager = hakoPdu.GetPduManager();
            if (pduManager == null)
            {
                return;
            }

            /*
             * Position
             */
            IPdu pdu_pos = pduManager.ReadPdu(robotName, pdu_name);
            if (pdu_pos == null)
            {
               // GD.Print("Can not get pdu of pos");
            }
            else
            {
                Twist pos = new Twist(pdu_pos);
                //GD.Print($"Twist ({pos.linear.x} {pos.linear.y} {pos.linear.z})");
                UpdatePosition(pos);
            }
        }

        private void UpdatePosition(Twist pos)
        {
            Godot.Vector3 unity_pos = new Godot.Vector3();
            unity_pos.Z = (float)pos.linear.x;
            unity_pos.X = -(float)pos.linear.y;
            unity_pos.Y = (float)pos.linear.z;
            
            if (body != null)
            {
                body.Position = unity_pos;

                // Godot's Quaternion.FromEuler takes a Vector3 of radians.
                // The original code was converting to degrees and then using a hypothetical Euler method.
                // We'll use radians directly.
                float roll = (float)pos.angular.x;
                float pitch = (float)pos.angular.y;
                float yaw = (float)pos.angular.z;

                // Original mapping: Euler(pitchDegrees, -yawDegrees, -rollDegrees)
                body.Quaternion = Godot.Quaternion.FromEuler(new Godot.Vector3(pitch, -yaw, -roll));
            }
        }

    }
}