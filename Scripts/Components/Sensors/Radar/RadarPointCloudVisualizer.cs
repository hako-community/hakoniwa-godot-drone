using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.sensor_msgs;
using System;
using Godot;

namespace hakoniwa.objects.core.sensors
{
    // R4: radar visualizer. Subscribes to radar_points (sensor_msgs/PointCloud2,
    // x,y,z + intensity=relative/Doppler velocity) and renders the detections as
    // a MultiMeshInstance3D, colored by velocity (red=approaching, blue=receding,
    // grey=static), plus a translucent FOV cone. Works for both the Godot radar
    // (Pattern B) and the mujoco-sensor radar (Pattern A) -- it only consumes the
    // radar_points PDU.
    public partial class RadarPointCloudVisualizer : Node3D
    {
        [Export] public bool Enabled = true;
        [Export] public string PduName = "radar_points";
        [Export] public float PointSize = 0.12f;
        [Export] public int MaxPoints = 50000;
        [Export] public float Range = 30f;
        [Export] public float HorizontalFOV = 30f;
        [Export] public float VerticalFOV = 10f;
        [Export] public float VelColorScale = 3.0f; // m/s mapped to full red/blue
        // OFF by default (2026-07-22): this cone is a SOLID translucent cylinder of
        // BottomRadius=max(rx,ry) and Height=Range with CullMode=Disabled, so at the
        // auto-created 90x50deg/30m settings it is a 30m x 30m blob that swallows the
        // camera -- the "screen goes blue / white bubble behind the drone" symptom.
        // A proper wireframe range display lives in SensorVizRig (see design doc 18.2).
        [Export] public bool ShowFovCone = false;

        private string robotName;
        private MultiMesh multiMesh;
        private const int PointStep = 16;
        // in-process source (the radar controller). When set, the visualizer reads
        // the latest scan directly instead of via the PduManager (whose declared
        // reader does not surface the same asset's own writes).
        private Default3DRadarController source;
        public void SetSource(Default3DRadarController c) { source = c; }

        public void DoInitialize(string robot_name, IPduManager pduManager)
        {
            this.robotName = robot_name;

            var mesh = new SphereMesh { Radius = PointSize, Height = PointSize * 2f, RadialSegments = 6, Rings = 3 };
            var mat = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            mesh.Material = mat;

            multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = 0
            };
            AddChild(new MultiMeshInstance3D { Multimesh = multiMesh });

            if (ShowFovCone) AddFovCone();
            GD.Print("RadarPointCloudVisualizer initialized for " + robot_name + " pdu=" + PduName);
        }

        private void AddFovCone()
        {
            // approximate cone with elliptical base sized by H/V FOV at Range
            float rx = Mathf.Tan(Mathf.DegToRad(HorizontalFOV * 0.5f)) * Range;
            float ry = Mathf.Tan(Mathf.DegToRad(VerticalFOV * 0.5f)) * Range;
            var cone = new CylinderMesh
            {
                TopRadius = 0.001f,
                BottomRadius = Mathf.Max(rx, ry),
                Height = Range,
                RadialSegments = 16
            };
            var cm = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.2f, 0.6f, 1f, 0.12f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            cone.Material = cm;
            // cylinder axis is +Y; rotate so it points to Godot forward (-Z), apex at sensor
            var mi = new MeshInstance3D { Mesh = cone };
            mi.Scale = new Vector3(rx <= 0 ? 1 : 1, 1, ry <= 0 ? 1 : (ry / Mathf.Max(rx, ry)));
            mi.RotateX(Mathf.DegToRad(-90));        // +Y -> -Z
            mi.Position = new Vector3(0, 0, -Range * 0.5f);
            AddChild(mi);
        }

        private Color VelColor(float v)
        {
            // v>0 receding (blue), v<0 approaching (red), ~0 grey
            float t = Mathf.Clamp(v / VelColorScale, -1f, 1f);
            if (t < 0) return new Color(1f, 1f + t, 1f + t);       // toward red
            return new Color(1f - t, 1f - t, 1f);                  // toward blue
        }

        public void DoControl(IPduManager pduManager)
        {
            if (!Enabled || multiMesh == null) return;

            byte[] data;
            int count;
            if (source != null)
            {
                data = source.LastScanData;
                if (data == null) return;
                count = Math.Min(source.LastDetections, MaxPoints);
            }
            else
            {
                // Read the SHM-populated buffer (CreateNamedPdu would return a
                // fresh zero buffer and never surface the mujoco-sensor's write).
                IPdu pdu = pduManager.ReadPdu(robotName, PduName);
                if (pdu == null) return;
                var pc = new PointCloud2(pdu);
                data = pc.data;
                if (data == null || data.Length < PointStep) return;
                count = Math.Min(data.Length / PointStep, MaxPoints);
                int width = (int)pc.width;
                if (width > 0) count = Math.Min(count, width);
            }
            if (count <= 0) { multiMesh.InstanceCount = 0; return; }
            multiMesh.InstanceCount = count;
            int drawn = 0;
            for (int i = 0; i < count; i++)
            {
                int off = i * PointStep;
                float x = BitConverter.ToSingle(data, off + 0);
                float y = BitConverter.ToSingle(data, off + 4);
                float z = BitConverter.ToSingle(data, off + 8);
                float vel = BitConverter.ToSingle(data, off + 12);
                if (x == 0f && y == 0f && z == 0f) continue;

                // PointCloud2 REP-103 (x fwd, y left, z up) -> Godot local (fwd=-z)
                var p = new Vector3(-y, z, -x);
                multiMesh.SetInstanceTransform(drawn, new Transform3D(Basis.Identity, p));
                multiMesh.SetInstanceColor(drawn, VelColor(vel));
                drawn++;
            }
            multiMesh.InstanceCount = drawn;
        }
    }
}
