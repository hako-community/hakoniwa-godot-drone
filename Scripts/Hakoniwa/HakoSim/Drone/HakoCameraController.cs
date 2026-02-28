using System;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.unity;
using hakoniwa.sim;
using hakoniwa.sim.core;
using Godot;

namespace hakoniwa.drone.sim
{
    public class HakoCameraController
    {
        private string robotName;
        public string pdu_name_cmd = "cmd";
        public string pdu_name_data = "data";
        private MonitorCameraManager cameraManager;
        /*
         * Camera Image
         */
        private int current_id = -1;
        private int request_id = 0;

        public void DoInitialize(string robot_name, IHakoPdu hakoPdu)
        {
            GD.Print("HakoCameraController: DoInitialize()");
            this.robotName = robot_name;
            var ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_cmd);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_cmd}");
            }
            ret = hakoPdu.DeclarePduForWrite(robotName, pdu_name_data);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for write: {robotName} {pdu_name_data}");
            }
            cameraManager = MonitorCameraManager.Instance;
        }
        private void WriteCameraDataPdu(IPduManager pduManager)
        {
            INamedPdu pdu = pduManager.CreateNamedPdu(robotName, pdu_name_data);
            if (pdu == null)
            {
                throw new ArgumentException($"Can not create pdu for write: {robotName} {pdu_name_data}");
            }
            var camera_data = new hakoniwa.pdu.msgs.hako_msgs.MonitorCameraData(pdu);
            camera_data.request_id = current_id;
            TimeStamp.Set(camera_data.image.header);
            camera_data.image.header.frame_id = robotName;
            string encode_type = cameraManager.GetEncodeType(robotName);
            if (encode_type == "png")
            {
                camera_data.image.format = "png";
            }
            else
            {
                camera_data.image.format = "jpeg";
            }
            byte[] compressed_bytes = cameraManager.GetImage(robotName, encode_type);
            camera_data.image_data_length = compressed_bytes.Length;
            camera_data.image.data = compressed_bytes;

            pduManager.WriteNamedPdu(pdu);
            pduManager.FlushNamedPdu(pdu);
            //GD.Print("Write Camera Data done!!: size of compressed_bytes = " + compressed_bytes.Length);
        }
        public void DoControl(IPduManager pduManager)
        {
            /*
             * Camera Image Request
             */
            IPdu pdu_cmd_camera = pduManager.ReadPdu(robotName, pdu_name_cmd);
            if (pdu_cmd_camera != null)
            {
                var cmd_camera = new hakoniwa.pdu.msgs.hako_msgs.MonitorCameraCmd(pdu_cmd_camera);
                if (cmd_camera.header.request)
                {
                    request_id = cmd_camera.request_id;
                    //GD.Print("Camera shot request: request_id = " + request_id);
                    if (current_id != request_id)
                    {
                        current_id = request_id;
                        this.WriteCameraDataPdu(pduManager);
                    }
                    else
                    {
                        //GD.Print("request id is invalid");
                    }
                }
            }
        }
    }

}
