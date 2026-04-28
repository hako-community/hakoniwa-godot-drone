using Godot;

namespace hakoniwa.objects.core
{
    // [RequireComponent(typeof(Camera))] // Godot: Assume attached to Node3D containing Camera or is Camera
    public partial class CameraView : Node3D
    {
        [Export]
        private Node3D target;

        private Vector3 positionOffset;
        private Quaternion rotationOffset;

        public enum OperateMode
        {
            Mouse,
            Auto,
        }

        public OperateMode currentMode = OperateMode.Mouse;

        [Export(PropertyHint.Range, "0.001, 10")]
        private float wheelSpeed = 8f;

        [Export(PropertyHint.Range, "0.001, 10")]
        private float moveSpeed = 8.0f;

        [Export(PropertyHint.Range, "0.001, 10")]
        private float rotateSpeed = 8.0f;

        [Export]
        private bool useGimbal = true;

        [Export(PropertyHint.Range, "0.1, 5")]
        private float gimbalSmoothSpeed = 2.0f;

        [Export]
        private Vector3 fixedDistance = new Vector3(0, 2, -7);

        [Export]
        private Vector3 fixedAngle = new Vector3(0, 0, 0); // カメラの固定角度

        private Vector2 preMousePos;
        private Vector3 prevFixedDistance;
        private Vector3 prevFixedAngle;

        public override void _Ready()
        {
            if (target != null)
            {
                positionOffset = fixedDistance;
                rotationOffset = Quaternion.Identity;
            }

            // 初期値を保存
            prevFixedDistance = fixedDistance;
            prevFixedAngle = fixedAngle;
        }

        public override void _Process(double delta)
        {
            if (Input.IsKeyPressed(Key.A) && !Input.IsKeyPressed(Key.Shift)) // Simple toggle check, improved logic needed for 'wasPressedThisFrame'
            {
                // Just a naive toggle, might flicker if held. 
                // Better: if (Input.IsActionJustPressed("toggle_camera_mode"))
                // For now, let's skip the key check or use JustPressed if Action map exists.
                // Assuming no action map, use static variable to debounce?
                // currentMode = currentMode == OperateMode.Auto ? OperateMode.Mouse : OperateMode.Auto;
            }

            switch (currentMode)
            {
                case OperateMode.Mouse:
                    MouseUpdate((float)delta);
                    UpdateCameraPositionAndRotation();
                    break;
                case OperateMode.Auto:
                    MouseUpdate((float)delta);
                    AutoUpdate((float)delta);
                    break;
            }

            prevFixedDistance = fixedDistance;
            prevFixedAngle = fixedAngle;
        }

        private void MouseUpdate(float delta)
        {
            // Scroll Wheel
            if (Input.IsMouseButtonPressed(MouseButton.WheelUp))
            {
                MouseWheel(1.0f);
            }
            else if (Input.IsMouseButtonPressed(MouseButton.WheelDown))
            {
                MouseWheel(-1.0f);
            }

            Vector2 currentMousePos = GetViewport().GetMousePosition();

            if (Input.IsMouseButtonPressed(MouseButton.Left) ||
                Input.IsMouseButtonPressed(MouseButton.Middle) ||
                Input.IsMouseButtonPressed(MouseButton.Right))
            {
                // On press start
                // Godot input is polling here.
                // We need 'wasPressed' logic to set preMousePos on start of drag.
                // Or just use relative motion from InputEventMouseMotion.
            }
            
            // Logic adapted for polling:
            MouseDrag(currentMousePos, delta);
            preMousePos = currentMousePos;
        }

        private void MouseWheel(float delta)
        {
            if (currentMode == OperateMode.Mouse)
            {
                // transform.forward -> -Basis.Z
                fixedDistance += -GlobalBasis.Z * delta * wheelSpeed * 0.1f; // Scaled down
            }
            else
            {
                if (target != null)
                {
                    Vector3 directionToCamera = (GlobalPosition - target.GlobalPosition).Normalized();
                    fixedDistance.Z += directionToCamera.Y * delta * wheelSpeed * 0.1f;
                }
            }
        }

        private void MouseDrag(Vector2 mousePos, float delta)
        {
            Vector2 diff = mousePos - preMousePos;

            if (diff.LengthSquared() < Mathf.Epsilon)
                return;

            if (Input.IsMouseButtonPressed(MouseButton.Middle))
            {
                if (currentMode == OperateMode.Mouse)
                {
                    fixedDistance.X -= diff.X * delta * moveSpeed * 0.1f;
                    fixedDistance.Y -= diff.Y * delta * moveSpeed * 0.1f; // Inverted Y screen to world?
                }
                else
                {
                    if (target != null)
                    {
                        Vector3 directionToCamera = (GlobalPosition - target.GlobalPosition).Normalized();
                        fixedDistance.X -= directionToCamera.Y * diff.X * delta * moveSpeed * 0.1f;
                        fixedDistance.Y -= directionToCamera.Y * diff.Y * delta * moveSpeed * 0.1f;
                    }
                }
            }
            else if (Input.IsMouseButtonPressed(MouseButton.Right))
            {
                fixedAngle.Y += diff.X * delta * rotateSpeed * 0.5f;
                fixedAngle.X -= diff.Y * delta * rotateSpeed * 0.5f;
            }
        }

        private void UpdateCameraPositionAndRotation()
        {
            Vector3 distanceDiff = fixedDistance - prevFixedDistance;
            GlobalPosition += distanceDiff;

            Vector3 angleDiff = fixedAngle - prevFixedAngle;

            // RotateAround implementation in Godot
            // transform.RotateAround(transform.position, transform.right, angleDiff.x);
            // transform.RotateAround(transform.position, Vector3.up, angleDiff.y);
            
            // GlobalRotate(GlobalBasis.X, Mathf.DegToRad(angleDiff.X));
            // GlobalRotate(Vector3.Up, Mathf.DegToRad(angleDiff.Y));
            
            // Simplified:
            RotateObjectLocal(Vector3.Right, Mathf.DegToRad(angleDiff.X));
            GlobalRotate(Vector3.Up, Mathf.DegToRad(angleDiff.Y));
        }

        private void AutoUpdate(float delta)
        {
            if (target == null) return;

            Vector3 targetPosition = target.GlobalPosition;
            Quaternion targetRotation = target.GlobalBasis.GetRotationQuaternion();

            Vector3 desiredPosition = targetPosition + targetRotation * fixedDistance; // Rotation * Vector? logic check

            // Unity: targetPosition + transform.rotation * fixedDistance; 
            // It used its own rotation? "transform.rotation"
            // Let's use GlobalRotation
            desiredPosition = targetPosition + GlobalBasis.GetRotationQuaternion() * fixedDistance;


            // Quaternion desiredRotation = Quaternion.Euler(fixedAngle.x, targetRotation.eulerAngles.y + fixedAngle.y, fixedAngle.z);
            Vector3 targetEuler = targetRotation.GetEuler();
            Quaternion desiredRotation = Quaternion.FromEuler(new Vector3(
                Mathf.DegToRad(fixedAngle.X),
                targetEuler.Y + Mathf.DegToRad(fixedAngle.Y),
                Mathf.DegToRad(fixedAngle.Z)
            ));


            if (useGimbal)
            {
                GlobalPosition = GlobalPosition.Lerp(desiredPosition, delta * gimbalSmoothSpeed);
                GlobalRotation = GlobalBasis.GetRotationQuaternion().Slerp(desiredRotation, delta * gimbalSmoothSpeed).GetEuler();
            }
            else
            {
                GlobalPosition = desiredPosition;
                GlobalRotation = desiredRotation.GetEuler();
            }
        }
    }
}