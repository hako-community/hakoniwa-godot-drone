using System;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.interfaces;
using hakoniwa.sim;
using Godot;

namespace hakoniwa.drone.sim
{
    // Wrapper for the Godot radar (R3), mirroring LiDAR3DController:
    // declares the radar PDUs for write and delegates to IRadar3DController.
    public partial class Radar3DController : Node
    {
        private IRadar3DController controller;

        private IRadar3DController GetController()
        {
            if (controller != null) return controller;
            controller = NodeUtil.FindNodeByInterface<IRadar3DController>(this);
            if (controller == null) throw new Exception("Can not find IRadar3DController");
            return controller;
        }

        public void DoInitialize(string robotName, IHakoPdu hakoPdu)
        {
            if (!hakoPdu.DeclarePduForWrite(robotName, Default3DRadarController.pdu_name_radar_pos))
                throw new ArgumentException($"Can not declare pdu for write: {robotName} {Default3DRadarController.pdu_name_radar_pos}");
            if (!hakoPdu.DeclarePduForWrite(robotName, Default3DRadarController.pdu_name_radar_points))
                throw new ArgumentException($"Can not declare pdu for write: {robotName} {Default3DRadarController.pdu_name_radar_points}");
            var pduManager = hakoPdu.GetPduManager();
            if (pduManager == null) throw new ArgumentException("ERROR: can not find pduManager");
            this.GetController().DoInitialize(robotName, pduManager);
        }

        public void DoControl(IPduManager pduManager)
        {
            this.GetController().DoControl(pduManager);
        }
    }
}
