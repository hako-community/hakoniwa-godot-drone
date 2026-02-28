using hakoniwa.drone.sim;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.interfaces;
using System;
using Godot;

namespace hakoniwa.drone
{
    public partial class DroneCameraController : Node
    {
        private ICameraController controller;

        private void Awake()
        {
//            this.controller = this.GetComponentInChildren<ICameraController>();
//            this.controller = FindComponent<ICameraController>(this);
            this.controller = FindNodeByInterface<ICameraController>(this);
            if (this.controller == null)
            {
                throw new Exception("Can not find ICameraController");
            }
            controller.Initialize();
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


        void LateUpdate()
        {
            this.controller.UpdateCameraAngle();
        }

        public void DoControl(IPduManager pduManager)
        {
            /*
             * Camera Image Request
             */
            this.controller.CameraImageRequest(pduManager);
            /*
             * Camera Move Request
             */
            this.controller.CameraMoveRequest(pduManager);

            this.controller.UpdateCameraImageTexture();
        }
        public ICameraController GetCameraController()
        {
            return this.controller;
        }
    }

}
