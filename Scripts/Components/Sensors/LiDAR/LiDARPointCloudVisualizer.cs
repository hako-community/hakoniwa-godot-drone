using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.sensor_msgs;
using System;
using Godot;

namespace hakoniwa.objects.core.sensors
{
    // M5 visualizer: subscribes to a lidar_points (sensor_msgs/PointCloud2) PDU
    // and renders it as a MultiMeshInstance3D point cloud.
    //
    // In Pattern A (env.xml present, Default3DLiDARController.ExternalSensing =
    // true) the points are produced by hakoniwa-mujoco-sensor; in Pattern B they
    // are produced by Godot's own Default3DLiDARController. The PDU is identical
    // (x,y,z,intensity, organized height x width), so this visualizer works for
    // both paths -- it only consumes lidar_points.
    public partial class LiDARPointCloudVisualizer : Node3D
    {
        [Export] public bool Enabled = true;
        [Export] public string PduName = "lidar_points";
        [Export] public float PointSize = 0.03f;
        [Export] public int MaxPoints = 200000;
        // Point coordinate frame of lidar_points (REP-103 sensor-local: x fwd,
        // y left, z up). Rendered relative to this node's transform.
        [Export] public Color PointColor = new Color(0.2f, 0.9f, 0.3f);

        private string robotName;
        private MultiMeshInstance3D mmi;
        private MultiMesh multiMesh;
        private const int PointStep = 16; // 4 x float32

        public void DoInitialize(string robot_name, IPduManager pduManager)
        {
            this.robotName = robot_name;

            var mesh = new BoxMesh { Size = new Vector3(PointSize, PointSize, PointSize) };
            var mat = new StandardMaterial3D
            {
                AlbedoColor = PointColor,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            mesh.Material = mat;

            multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = 0
            };
            mmi = new MultiMeshInstance3D { Multimesh = multiMesh };
            AddChild(mmi);
            GD.Print("LiDARPointCloudVisualizer initialized for " + robot_name + " pdu=" + PduName);
        }

        public void DoControl(IPduManager pduManager)
        {
            if (!Enabled || multiMesh == null) return;

            // Read the SHM-populated buffer (CreateNamedPdu would return a fresh
            // zero buffer and never surface the mujoco-sensor's write).
            IPdu pdu = pduManager.ReadPdu(robotName, PduName);
            if (pdu == null) return;
            var pc = new PointCloud2(pdu);
            byte[] data = pc.data;
            if (data == null || data.Length < PointStep) return;

            int count = Math.Min(data.Length / PointStep, MaxPoints);
            multiMesh.InstanceCount = count;
            int drawn = 0;
            for (int i = 0; i < count; i++)
            {
                int off = i * PointStep;
                float x = BitConverter.ToSingle(data, off + 0);
                float y = BitConverter.ToSingle(data, off + 4);
                float z = BitConverter.ToSingle(data, off + 8);
                float intensity = BitConverter.ToSingle(data, off + 12);
                if (intensity <= 0.0f) continue; // skip max-range / no-hit points

                // PointCloud2 REP-103 (x fwd, y left, z up) -> Godot local
                // (Godot: x right, y up, z back; forward = -z).
                var p = new Vector3(-y, z, -x);
                multiMesh.SetInstanceTransform(drawn, new Transform3D(Basis.Identity, p));
                drawn++;
            }
            multiMesh.InstanceCount = drawn;
        }
    }
}
