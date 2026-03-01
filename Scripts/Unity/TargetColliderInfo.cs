using Godot;
using System.Collections.Generic;

namespace hakoniwa.objects.core
{
    public partial class TargetColliderInfo : Node
    {
        private static Dictionary<CollisionObject3D, TargetColliderInfo> colliderInfoMap = new Dictionary<CollisionObject3D, TargetColliderInfo>();
        
        public static TargetColliderInfo GetInfo(CollisionObject3D collider)
        {
            if (collider != null && colliderInfoMap.TryGetValue(collider, out TargetColliderInfo info))
            {
                return info;
            }
            GD.PushWarning($"Collider {collider?.Name} に対応するTargetColliderInfoが見つかりません。");
            return null;
        }

        private static void PutInfo(CollisionObject3D collider, TargetColliderInfo obj)
        {
            colliderInfoMap[collider] = obj;
        }

        [Export]
        public bool IsStatic = false; // 静的オブジェクトかどうか

        [Export]
        public Vector3 Position = Vector3.Zero;
        [Export]
        public Quaternion Rotation = Quaternion.Identity;
        [Export]
        public Vector3 Velocity = Vector3.Zero;
        [Export]
        public Vector3 AngularVelocity = Vector3.Zero;
        [Export]
        public Vector3 Euler = Vector3.Zero;

        [Export]
        public Vector3 Inertia = new Vector3(1.0f, 1.0f, 1.0f); // 慣性テンソル
        [Export]
        public double Mass = 1.0; // 質量
        [Export]
        public double RestitutionCoefficient = 0.5; // 反発係数

        [Export]
        public RigidBody3D rb = null;
        [Export]
        public CollisionObject3D collider_obj = null;

        private Vector3 lastValidVelocity = Vector3.Zero;
        private Vector3 previousPosition;
        private double velocity_lastUpdateTime = 0;

        private Vector3 lastValidAngularVelocity = Vector3.Zero;
        private Quaternion previousRotation;
        private double rotation_lastUpdateTime = 0;

        private double currentTime = 0;

//        public string GetName()
//        {
//            return this.Name;
//        }

        public Vector3 GetNormal(Vector3 contactPoint, Vector3 contactedTartgetCenterPoint)
        {
            // Godot simplified normal calculation logic
            // In Godot, the normal is often provided by the collision signal.
            // But if we need to calculate it like the original:
            if (collider_obj != null)
            {
                // Simple sphere normal for now as Box normal calculation is complex without Unity's bounds
                Vector3 normal = (contactedTartgetCenterPoint - this.Position).Normalized();
                return normal;
            }
            return Vector3.Up;
        }

        public override void _Ready()
        {
            if (rb == null)
            {
                rb = NodeUtil.FindNodeByInterface<RigidBody3D>(GetParent());
            }
            if (collider_obj == null)
            {
                collider_obj = NodeUtil.FindNodeByInterface<CollisionObject3D>(GetParent());
            }

            if (collider_obj == null)
            {
                GD.PrintErr($"{Name}: CollisionObject3Dが設定されていません。");
                return;
            }

            if (rb != null)
            {
                previousPosition = rb.GlobalPosition;
                previousRotation = rb.GlobalBasis.GetRotationQuaternion();
            }
            else
            {
                IsStatic = true;
            }
            
            PutInfo(collider_obj, this);
            currentTime = 0;
        }

        public override void _ExitTree()
        {
            if (collider_obj != null)
            {
                colliderInfoMap.Remove(collider_obj);
                collider_obj = null;
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            UpdatePosition();
            UpdateVelocity();
            UpdateAngularVelocity();
            currentTime += delta;
        }

        private void UpdatePosition()
        {
            if (rb != null)
            {
                Position = rb.GlobalPosition;
                Rotation = rb.GlobalBasis.GetRotationQuaternion();
                Euler = rb.RotationDegrees;
            }
        }

        private void UpdateVelocity()
        {
            this.Velocity = GetVelocity();
        }

        private void UpdateAngularVelocity()
        {
            this.AngularVelocity = GetAngularVelocity();
        }

        private Vector3 GetVelocity()
        {
            if (rb != null)
            {
                if (rb.Freeze == false)
                {
                    return rb.LinearVelocity;
                }
                else
                {
                    Vector3 currentPosition = rb.GlobalPosition;
                    if (currentPosition.IsEqualApprox(previousPosition))
                    {
                        return lastValidVelocity;
                    }

                    double deltaTime = currentTime - velocity_lastUpdateTime;
                    velocity_lastUpdateTime = currentTime;

                    Vector3 velocity = (currentPosition - previousPosition) / (float)(deltaTime > 0 ? deltaTime : GetPhysicsProcessDeltaTime());
                    
                    previousPosition = currentPosition;
                    if (velocity.Length() < 0.0001f)
                    {
                        return lastValidVelocity;
                    }
                    
                    lastValidVelocity = velocity;
                    return velocity;
                }
            }
            return Vector3.Zero;
        }

        private Vector3 GetAngularVelocity()
        {
            if (rb != null)
            {
                if (rb.Freeze == false)
                {
                    return rb.AngularVelocity;
                }
                else
                {
                    Quaternion currentRotation = rb.GlobalBasis.GetRotationQuaternion();
                    if (currentRotation.IsEqualApprox(previousRotation))
                    {
                        return lastValidAngularVelocity;
                    }

                    double deltaTime = currentTime - rotation_lastUpdateTime;
                    rotation_lastUpdateTime = currentTime;

                    Quaternion deltaRotation = currentRotation * previousRotation.Inverse();
                    previousRotation = currentRotation;

                    float angle = deltaRotation.GetAngle();
                    Vector3 axis = deltaRotation.GetAxis();
                    Vector3 angularVelocity = (axis * angle) / (float)(deltaTime > 0 ? deltaTime : GetPhysicsProcessDeltaTime());

                    if (angularVelocity.Length() < 0.0001f)
                    {
                        return lastValidAngularVelocity;
                    }

                    lastValidAngularVelocity = angularVelocity;
                    return angularVelocity;
                }
            }
            return Vector3.Zero;
        }
    }
}