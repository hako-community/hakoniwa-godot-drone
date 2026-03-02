using Godot;
using hakoniwa.drone.service;

namespace hakoniwa.drone
{
    public partial class BoundaryManager : Node
    {
        [ExportGroup("Scene References")]
        [Export]
        public Node3D drone;                // ドローン本体
        [Export]
        public Node3D[] boundaryPlanes;     // 壁や床のノード

        [ExportGroup("Plane Settings")]
        [Export]
        public Vector3 localNormalAxis = Vector3.Up;

        [ExportGroup("API Settings")]
        [Export]
        public bool pushToApi = true;
        [Export]
        public int droneIndex = 0;

        public override void _PhysicsProcess(double delta)
        {
            if (drone == null || boundaryPlanes == null || boundaryPlanes.Length == 0) return;

            Vector3 dronePos = drone.GlobalPosition;

            float minDist = float.MaxValue;
            Vector3 bestNormal = Vector3.Zero;
            Vector3 bestPlanePt = Vector3.Zero;
            bool found = false;

            foreach (var plane in boundaryPlanes)
            {
                if (plane == null) continue;

                Vector3 centerW = GetPlaneCenterWorld(plane);
                Vector3 normalW = GetPlaneNormalWorld(plane, localNormalAxis);

                Vector3 toDrone = dronePos - centerW;
                if (toDrone.Dot(normalW) < 0f) normalW = -normalW;

                float tSigned = toDrone.Dot(normalW);
                Vector3 hitPt = dronePos - tSigned * normalW;
                float distAbs = Mathf.Abs(tSigned);

                Vector3 tangentW, bitangentW;
                Basis basis = plane.GlobalTransform.Basis;
                if (localNormalAxis.IsEqualApprox(Vector3.Up))
                {
                    tangentW = basis.X;
                    bitangentW = basis.Z;
                }
                else
                {
                    tangentW = basis.X;
                    bitangentW = basis.Y;
                }

                Vector2 sizeW = GetPlaneSizeWorld(plane);
                float halfW = sizeW.X * 0.5f;
                float halfH = sizeW.Y * 0.5f;

                Vector3 d = hitPt - centerW;
                float xProj = d.Dot(tangentW);
                float yProj = d.Dot(bitangentW);

                if (Mathf.Abs(xProj) <= halfW && Mathf.Abs(yProj) <= halfH)
                {
                    if (distAbs < minDist)
                    {
                        minDist = distAbs;
                        bestNormal = normalW;
                        bestPlanePt = centerW;
                        found = true;
                    }
                }
            }

            if (found && pushToApi)
            {
                Vector3 rosPoint = UnityToRos(bestPlanePt);
                Vector3 rosNormal = UnityToRos(bestNormal).Normalized();
                DroneServiceRC.PutDisturbanceBoundary(droneIndex, rosPoint, rosNormal);
            }
        }

        Vector3 GetPlaneCenterWorld(Node3D plane)
        {
            var mi = NodeUtil.FindNodeByInterface<MeshInstance3D>(plane);
            if (mi != null && mi.Mesh != null)
            {
                return plane.ToGlobal(mi.Mesh.GetAabb().GetCenter());
            }
            return plane.GlobalPosition;
        }

        Vector2 GetPlaneSizeWorld(Node3D plane)
        {
            var mi = NodeUtil.FindNodeByInterface<MeshInstance3D>(plane);
            if (mi == null || mi.Mesh == null) return Vector2.Zero;

            Aabb aabb = mi.Mesh.GetAabb();
            Vector3 size = aabb.Size;
            Vector3 scale = plane.GlobalTransform.Basis.Scale;
            
            float width = Mathf.Abs(size.X * scale.X);
            float height = Mathf.Abs(size.Z * scale.Z); 
            return new Vector2(width, height);
        }

        Vector3 GetPlaneNormalWorld(Node3D plane, Vector3 localNormal)
        {
            return (plane.GlobalTransform.Basis * localNormal).Normalized();
        }

        static Vector3 UnityToRos(Vector3 v) => new Vector3(v.Z, -v.X, v.Y);
    }
}