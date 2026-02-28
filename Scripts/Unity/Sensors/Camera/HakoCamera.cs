using Godot;
using System;

namespace hakoniwa.objects.core.sensors
{
    public partial class HakoCamera : Node3D
    {
        private Camera3D _camera;
        private SubViewport _viewport;
        private IMovableObject targetObject;
        private Vector3 localPositionOffset;
        private Vector3 localRotationOffset;
        private string encode_type;

        public void ConfigureCamera(string cameraId, string cameraType, string _encode_type, string coordinate_type, string target, Vector3 position, Vector3 rotation, float fov, int width, int height)
        {
            // 1. Get or Create Camera3D
//            _camera = FindComponent<Camera3D>(this);
            _camera = FindNodeByInterface<Camera3D>(this);
            if (_camera == null)
            {
                _camera = new Camera3D();
                AddChild(_camera);
            }

            // 2. Create SubViewport (equivalent to Unity's RenderTexture)
            if (_viewport == null)
            {
                _viewport = new SubViewport();
                AddChild(_viewport);
            }
            _viewport.Size = new Vector2I(width, height);
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

            // 3. Move camera into the viewport
            if (_camera.GetParent() != _viewport)
            {
                if (_camera.GetParent() != null)
                {
                    _camera.GetParent().RemoveChild(_camera);
                }
                _viewport.AddChild(_camera);
            }

            this.Name = cameraId;

            // Apply FOV
            _camera.Fov = fov;

            if (coordinate_type == "local")
            {
                // Recursive search for target node
                var obj = GetTree().Root.FindChild(target, true, false);
                if (obj == null)
                {
                    GD.PrintErr("Can not find Node: " + target);
                    return;
                }
                targetObject = FindComponent<IMovableObject>(obj);
                if (targetObject == null)
                {
                    GD.PrintErr("Can not find IMovableObject: " + target);
                    return;
                }
                localPositionOffset = position;
                localRotationOffset = rotation;
            }
            else // global
            {
                // Apply global pos/rot to the camera holder (this Node3D)
                GlobalPosition = position;
                RotationDegrees = rotation;
            }
            
            this.encode_type = _encode_type;
            GD.Print($"Configured Camera: {cameraId} - Type: {cameraType}, FOV: {fov}, Position: {position}, Rotation: {rotation}, Resolution: {width}x{height}");
        }

        public override void _Process(double delta)
        {
            if (targetObject != null)
            {
                // Sync position/rotation with target
                Vector3 targetPos = targetObject.GetPosition();
                Vector3 targetRotDeg = targetObject.GetEulerDeg();
                
                // Convert degrees to radians for Basis/Quaternion
                Vector3 targetRotRad = targetRotDeg * (float)(Math.PI / 180.0);
                Basis targetBasis = new Basis(Quaternion.FromEuler(targetRotRad));
                
                GlobalPosition = targetPos + targetBasis * localPositionOffset;
                GlobalRotationDegrees = targetRotDeg + localRotationOffset;
            }
        }

        public Texture2D GetRenderTexture()
        {
            return _viewport?.GetTexture();
        }

        public float GetFov()
        {
            return _camera != null ? _camera.Fov : 0;
        }

        public string GetEncodeType()
        {
            return encode_type;
        }

        public byte[] GetImage(string encode_type)
        {
            if (_viewport == null) return null;

            // Extract image from viewport
            Image img = _viewport.GetTexture().GetImage();
            
            // Viewport textures in Godot are often flipped relative to what we expect
            // img.FlipY();

            if (encode_type.ToLower() == "png")
            {
                return img.SavePngToBuffer();
            }
            else
            {
                return img.SaveJpgToBuffer();
            }
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
    }
}