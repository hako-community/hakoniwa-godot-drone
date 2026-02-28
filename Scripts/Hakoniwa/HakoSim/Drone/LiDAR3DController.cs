using System;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.sensor_msgs;
using hakoniwa.pdu.unity;
using hakoniwa.sim;
using Godot;

namespace hakoniwa.drone.sim
{
    public partial class LiDAR3DController : Node
    {
        private ILiDAR3DController controller;
        private ILiDAR3DController GetController()
        {
            if (controller != null)
            {
                return controller;
            }
//            controller = FindComponent<ILiDAR3DController>(this);
            controller = FindNodeByInterface<ILiDAR3DController>(this);
            if (controller == null)
            {
                throw new Exception("Can not find ILiDAR3DController");
            }
            return controller;
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

        public void DoInitialize(string robotName, IHakoPdu hakoPdu)
        {
            var ret = hakoPdu.DeclarePduForWrite(robotName, Default3DLiDARController.pdu_name_lidar_pos);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for write: {robotName} {Default3DLiDARController.pdu_name_lidar_pos}");
            }
            ret = hakoPdu.DeclarePduForWrite(robotName, Default3DLiDARController.pdu_name_lidar_point_cloud);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for write: {robotName} {Default3DLiDARController.pdu_name_lidar_point_cloud}");
            }
            var pduManager = hakoPdu.GetPduManager();
            if (pduManager == null)
            {
                throw new ArgumentException("ERROR: can not find pduManager");
            }
            this.GetController().DoInitialize(robotName, pduManager);
        }

        public void DoControl(IPduManager pduManager)
        {
            this.GetController().DoControl(pduManager);
        }

    }
}