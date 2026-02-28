using System;
using System.Collections.Generic;
using hakoniwa.objects.core.sensors;
using hakoniwa.sim;
using hakoniwa.sim.core;
using Godot;

namespace hakoniwa.drone.sim
{
    public partial class HakoMonitorCameraManager : Node, IHakoObject
    {
        IHakoPdu hakoPdu;
        private MonitorCameraManager cameraManager;
        private List<HakoCameraController> cameraControllers;
        public void EventInitialize()
        {
            hakoPdu = HakoAsset.GetHakoPdu();
            cameraManager = MonitorCameraManager.Instance;
            if (cameraManager == null)
            {
                throw new Exception("MonitorCameraManager is not assigned");
            }
            cameraControllers = new List<HakoCameraController>();
            List<string> cameraNames = cameraManager.GetCameraNames();
            foreach (var cameraName in cameraNames)
            {
                HakoCameraController entry = new HakoCameraController();
                entry.DoInitialize(cameraName, hakoPdu);
                cameraControllers.Add(entry);
            }
        }
        public void EventTick()
        {
            var pduManager = hakoPdu.GetPduManager();
            if (pduManager == null)
            {
                return;
            }
            foreach (var entry in cameraControllers)
            {
                entry.DoControl(pduManager);
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
    }
}
