using hakoniwa.pdu.core;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.unity;
using hakoniwa.sim;
using System;
using System.IO;
using Godot;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace hakoniwa.objects.core.sensors
{
    public interface ICameraController
    {
        public void Initialize();
        public void UpdateCameraAngle();
        public void DelclarePdu(string robotName, IPduManager pduManager);
        public void RotateCamera(float step);
        public void WriteCameraInfo(int move_current_id, IPduManager pduManager);
        public void WriteCameraDataPdu(IPduManager pduManager);
        public void Scan();
        public void SetCameraAngle(float angle);
        public void UpdateCameraImageTexture();
        public void CameraImageRequest(IPduManager pduManager);
        public void CameraMoveRequest(IPduManager pduManager);
    }

    public partial class DefaultCameraController : Node3D, ICameraController
    {
        private string robotName;
        [Export]
        public string sensor_name = "hakoniwa_camera";
        [Export]
        public string pdu_name_cmd_camera = "hako_cmd_camera";
        [Export]
        public string pdu_name_camera_data = "hako_camera_data";
        [Export]
        public string pdu_name_cmd_camera_move = "hako_cmd_camera_move";
        [Export]
        public string pdu_name_camera_info = "hako_cmd_camera_info";

        [Export]
        public float camera_move_up_deg = -15.0f;
        [Export]
        public float camera_move_down_deg = 90.0f;
        
        private SubViewport _viewport;
        private Image _lastScanImage;
        
        [Export]
        public int width = 640;
        [Export]
        public int height = 480;
        
        private byte[] compressed_bytes;
        private Camera3D my_camera;
        [Export]
        public TextureRect displayImage; 
        private float manual_rotation_deg = 0;

        [Export]
        public int current_id = -1;
        [Export]
        public int request_id = 0;
        [Export]
        public int encode_type = 0;

        [Export]
        public int move_current_id = -1;
        [Export]
        public int move_request_id = 0;
        [Export]
        public float move_step = 1.0f;
        [Export]
        public float camera_move_button_threshold_speedup = 1.0f;

        [Export]
        public MeshInstance3D targetRenderer;

        [Export]
        public Vector3 cameraOffset = new Vector3(0, 0.2f, 1.0f);

        public async void DelclarePdu(string robotName, IPduManager pduManager)
        {
            this.robotName = robotName;
            if (pduManager != null)
            {
                var ret = await pduManager.DeclarePduForRead(robotName, pdu_name_cmd_camera);
                if (ret == false)
                {
                    throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_cmd_camera}");
                }
                ret = await pduManager.DeclarePduForRead(robotName, pdu_name_cmd_camera_move);
                if (ret == false)
                {
                    throw new ArgumentException($"Can not declare pdu for read: {robotName} {pdu_name_cmd_camera_move}");
                }
                ret = await pduManager.DeclarePduForWrite(robotName, pdu_name_camera_data);
                if (ret == false)
                {
                    throw new ArgumentException($"Can not declare pdu for write: {robotName} {pdu_name_camera_data}");
                }
                ret = await pduManager.DeclarePduForWrite(robotName, pdu_name_camera_info);
                if (ret == false)
                {
                    throw new ArgumentException($"Can not declare pdu for write: {robotName} {pdu_name_camera_info}");
                }
            }
        }

        public void Initialize()
        {
            if (this.my_camera == null)
            {
                this.my_camera = NodeUtil.FindNodeByInterface<Camera3D>(this);
            }
            
            if (my_camera == null)
            {
                my_camera = new Camera3D();
                CallDeferred(Node.MethodName.AddChild, my_camera);
            }
            my_camera.Current = true;

            // Create SubViewport for off-screen rendering if not found
            if (_viewport == null)
            {
                _viewport = NodeUtil.FindNodeByInterface<SubViewport>(this);
            }

            if (_viewport == null)
            {
                _viewport = new SubViewport();
                CallDeferred(Node.MethodName.AddChild, _viewport);
            }
            _viewport.Size = new Vector2I(width, height);
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

            // Move camera into viewport if needed
            if (my_camera.GetParent() != _viewport)
            {
                CallDeferred(nameof(SetupCameraHierarchy));
            }

            if (targetRenderer != null)
            {
                var mat = new StandardMaterial3D();
                mat.AlbedoTexture = _viewport.GetTexture();
                targetRenderer.MaterialOverride = mat;
            }
        }

        // ヘルパーメソッドを追加
        private void SetupCameraHierarchy()
        {
            if (my_camera.GetParent() != null)
            {
                my_camera.GetParent().RemoveChild(my_camera);
            }
            _viewport.AddChild(my_camera);
            my_camera.Current = true;

            if (targetRenderer != null)
            {
                var mat = new StandardMaterial3D();
                mat.AlbedoTexture = _viewport.GetTexture();
                targetRenderer.MaterialOverride = mat;
            }
        }

        public void RotateCamera(float step)
        {
            float newPitch = manual_rotation_deg + step;
            if (newPitch > 180) newPitch -= 360;
            newPitch = Mathf.Clamp(newPitch, this.camera_move_up_deg, this.camera_move_down_deg);
            manual_rotation_deg = newPitch;
        }

        public void Scan()
        {
            if (_viewport == null) return;

            // Force update and get image
            _lastScanImage = _viewport.GetTexture().GetImage();
            
            // Encode texture
            if (encode_type == 0)
            {
                compressed_bytes = _lastScanImage.SavePngToBuffer();
            }
            else
            {
                compressed_bytes = _lastScanImage.SaveJpgToBuffer();
            }
        }

        public void SetCameraAngle(float angle)
        {
            float newPitch = angle;
            if (newPitch > 180) newPitch -= 360;
            newPitch = Mathf.Clamp(newPitch, this.camera_move_up_deg, this.camera_move_down_deg);
            manual_rotation_deg = newPitch;
        }

        public void UpdateCameraAngle()
        {
            if (my_camera == null) return;
            
            // Sync position to this node
            my_camera.GlobalPosition = this.GlobalPosition + this.GlobalBasis * cameraOffset;
            
            // Extract only the Yaw (Y-axis rotation) to stay level
            Vector3 rot = this.GlobalRotation;
            my_camera.GlobalRotation = new Vector3(0, rot.Y, 0);
            
            // Apply manual camera tilt (pitch)
            my_camera.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(manual_rotation_deg));
        }

        public void WriteCameraDataPdu(IPduManager pduManager)
        {
            INamedPdu pdu = pduManager.CreateNamedPdu(robotName, pdu_name_camera_data);
            if (pdu == null)
            {
                throw new ArgumentException($"Can not create pdu for write: {robotName} {pdu_name_camera_data}");
            }
            var camera_data = new hakoniwa.pdu.msgs.hako_msgs.HakoCameraData(pdu);
            camera_data.request_id = current_id;
            TimeStamp.Set(camera_data.image.header);
            camera_data.image.header.frame_id = this.sensor_name;
            camera_data.image.format = (encode_type == 0) ? "png" : "jpeg";
            camera_data.image.data = compressed_bytes;

            pduManager.WriteNamedPdu(pdu);
            pduManager.FlushNamedPdu(pdu);
        }

        public void WriteCameraInfo(int move_current_id, IPduManager pduManager)
        {
            INamedPdu pdu = pduManager.CreateNamedPdu(robotName, pdu_name_camera_info);
            if (pdu == null)
            {
                throw new ArgumentException($"Can not create pdu for write: {robotName} {pdu_name_camera_info}");
            }
            var camera_info = new hakoniwa.pdu.msgs.hako_msgs.HakoCameraInfo(pdu);

            camera_info.request_id = move_current_id;
            camera_info.angle.x = 0;
            camera_info.angle.y = this.manual_rotation_deg;
            camera_info.angle.z = 0;

            pduManager.WriteNamedPdu(pdu);
            pduManager.FlushNamedPdu(pdu);
        }

        public void UpdateCameraImageTexture()
        {
            if (displayImage != null && _viewport != null)
            {
                displayImage.Texture = _viewport.GetTexture();
            }
        }

        public void CameraImageRequest(IPduManager pduManager)
        {
            IPdu pdu_cmd_camera = pduManager.ReadPdu(robotName, pdu_name_cmd_camera);
            if (pdu_cmd_camera != null)
            {
                var cmd_camera = new hakoniwa.pdu.msgs.hako_msgs.HakoCmdCamera(pdu_cmd_camera);
                if (cmd_camera.header.request)
                {
                    request_id = cmd_camera.request_id;
                    encode_type = cmd_camera.encode_type;

                    if (current_id != request_id)
                    {
                        current_id = request_id;
                        this.Scan();
                        this.WriteCameraDataPdu(pduManager);
                    }
                }
            }
        }

        public void CameraMoveRequest(IPduManager pduManager)
        {
            IPdu pdu_camera_move = pduManager.ReadPdu(robotName, pdu_name_cmd_camera_move);
            if (pdu_camera_move == null) return;
            
            var camera_move = new hakoniwa.pdu.msgs.hako_msgs.HakoCmdCameraMove(pdu_camera_move);
            if (camera_move.header.request)
            {
                move_request_id = camera_move.request_id;
                if (move_current_id != move_request_id)
                {
                    move_current_id = move_request_id;
                    var target_degree = (float)camera_move.angle.y;
                    this.SetCameraAngle(-target_degree);
                    this.WriteCameraInfo(move_current_id, pduManager);
                }
            }
        }
    }
}