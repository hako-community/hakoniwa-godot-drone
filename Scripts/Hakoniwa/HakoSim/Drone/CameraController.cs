using System;
using hakoniwa.drone.sim;
using hakoniwa.objects.core.sensors;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.unity;
using hakoniwa.sim;
using Godot;
using Godot.Collections;

namespace hakoniwa.drone.sim
{
    public partial class CameraController : Node
    {
        private string pdu_name_cmd_camera = "hako_cmd_camera";
        private string pdu_name_camera_data = "hako_camera_data";
        private string pdu_name_cmd_camera_move = "hako_cmd_camera_move";
        private string pdu_name_camera_info = "hako_cmd_camera_info";
        private ICameraController controller;

        [Export]
        public Node controllerNode;

        [Export]
        public float move_step = 1.0f;  
        private float camera_move_button_time_duration = 0f;
        [Export]
        public float camera_move_button_threshold_speedup = 1.0f;

        private string robotName;

        public override void _Ready()
        {
//            this.controller = FindComponent<ICameraController>(GetParent());
            this.controller = controllerNode as ICameraController;
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

        public void DoInitialize(string robot_name, IHakoPdu hakoPdu)
        {
#if false
            this.robotName = robot_name;
            if (controller == null)
            {
                throw new Exception("Can not find ICameraController");
            }
            this.controller.DelclarePdu(robotName, null);
            var ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_cmd_camera);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_cmd_camera}");
            }
            ret = hakoPdu.DeclarePduForRead(robotName, pdu_name_cmd_camera_move);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_cmd_camera_move}");
            }
            ret = hakoPdu.DeclarePduForWrite(robotName, pdu_name_camera_data);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for write: {robotName} {pdu_name_camera_data}");
            }
            ret = hakoPdu.DeclarePduForWrite(robotName, pdu_name_camera_info);
            if (ret == false)
            {
                throw new ArgumentException($"Can not declare pdu for write: {robotName} {pdu_name_camera_info}");
            }
#else
        GD.Print("CameraController DoInitialize: robotName=" + robot_name);
            this.robotName = robot_name;

            // --- コントローラーの解決ロジック (既存) ---
            if (this.controller == null)
            {
                if (controllerNode != null)
                    this.controller = controllerNode as ICameraController;
                
                if (this.controller == null)
                    this.controller = FindComponent<ICameraController>(GetParent());
            }

            if (this.controller == null)
            {
                GD.PrintErr($"CameraController: Controller not found at {GetPath()}");
                throw new Exception("Can not find ICameraController");
            }

            // --- 復活させるべき PDU 宣言処理 ---
            // controller 自身の内部的な PDU 宣言
            this.controller.DelclarePdu(robotName, null);

            // hakoPdu を使ったシステムへの PDU 登録 (ここが抜けていました)
            if (hakoPdu != null) 
            {
                // 読み込み用 PDU
                hakoPdu.DeclarePduForRead(robotName, pdu_name_cmd_camera);
                hakoPdu.DeclarePduForRead(robotName, pdu_name_cmd_camera_move);
                
                // 書き込み用 PDU
                hakoPdu.DeclarePduForWrite(robotName, pdu_name_camera_data);
                hakoPdu.DeclarePduForWrite(robotName, pdu_name_camera_info);
            }
#endif
        }

        public void DoControl(IPduManager pduManager)
        {
            if (this.controller == null) return;

            /*
             * Camera Image Request
             */
            this.controller.CameraImageRequest(pduManager);
            /*
             * Camera Move Request
             */
            this.controller.CameraMoveRequest(pduManager);

            var game_controller = GameController.Instance;
            // Fixed the Unity-style boolean check to Godot C# style (game_controller != null)
            if (game_controller != null && game_controller.GetRadioControlOn())
            {
                /*
                 * Camera Image Rc request
                 */
                if (game_controller.GetCameraShotOn())
                {
                    GD.Print("SHOT!!");
                    this.controller.Scan();
                    this.controller.WriteCameraDataPdu(pduManager);
                }

                if (game_controller.GetCameraMoveUp())
                {
                    camera_move_button_time_duration += (float)GetPhysicsProcessDeltaTime();
                    if (camera_move_button_time_duration > camera_move_button_threshold_speedup)
                    {
                        this.controller.RotateCamera(-move_step * 3f);
                    }
                    else
                    {
                        this.controller.RotateCamera(-move_step);
                    }

                }

                if (game_controller.GetCameraMoveDown())
                {
                    camera_move_button_time_duration += (float)GetPhysicsProcessDeltaTime();
                    if (camera_move_button_time_duration > camera_move_button_threshold_speedup)
                    {
                        this.controller.RotateCamera(move_step * 3f);
                    }
                    else
                    {
                        this.controller.RotateCamera(move_step);
                    }
                }
                if (!game_controller.GetCameraMoveDown() && !game_controller.GetCameraMoveUp())
                {
                    camera_move_button_time_duration = 0f;
                }
            }

            this.controller.UpdateCameraImageTexture();
            this.controller.UpdateCameraAngle();
        }
       
    }
}