using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.sensor_msgs;
using hakoniwa.pdu.godot;
using hakoniwa.sim;
using System;
using System.Collections.Generic;
using Godot;

namespace hakoniwa.objects.core.sensors
{
    // R3: Godot Pattern-B Radar (Strategy-C second backend).
    //
    // Mirrors Default3DLiDARController but scans a random cone of rays (CARLA
    // SendLineTraces port) and outputs each detection's relative (Doppler)
    // velocity in the PointCloud2 "intensity" field. The PDU is the same
    // sensor_msgs/PointCloud2 used by the mujoco-sensor radar (Pattern A), so
    // RadarPointCloudVisualizer / Python clients consume either path.
    //
    // ExternalSensing=true -> sensing is done by hakoniwa-mujoco-sensor; Godot
    // only visualizes and must not ray cast / publish (same switch as LiDAR).
    public struct Radar3DParams
    {
        public bool Enabled;
        public float Range;          // m
        public float HorizontalFOV;  // deg (full)
        public float VerticalFOV;    // deg (full)
        public int PointsPerSecond;
        public int UpdateRateHz;
        public int NoiseSeed;
        public bool DrawDebugPoints;
    }

    public interface IRadar3DController
    {
        public bool SetParams(Radar3DParams param);
        public Radar3DParams GetParams();
        // Pattern A (true): hakoniwa-mujoco-sensor produces radar_points; Godot
        // only reads/visualizes and must not ray cast or publish itself.
        public bool ExternalSensing { get; set; }
        public void DoInitialize(string robot_name, IPduManager pduManager);
        public void DoControl(IPduManager pduManager);
    }

    public partial class Default3DRadarController : Node3D, IRadar3DController
    {
        [Export] public bool Enabled = true;
        [Export] public float Range = 30f;
        [Export] public float HorizontalFOV = 30f;
        [Export] public float VerticalFOV = 10f;
        [Export] public int PointsPerSecond = 1500;
        [Export] public int UpdateRateHz = 10;
        [Export] public int NoiseSeed = 1;
        [Export] public bool DrawDebugPoints = true;

        // Pattern A: mujoco-sensor publishes radar_points; Godot only visualizes.
        [Export] public bool ExternalSensing { get; set; } = false;

        // max detections per update (PointsPerSecond/UpdateRateHz) bounded by channel
        public const int max_data_array_size = 176656; // 16 bytes * 11041 pts
        private const int point_step = 16;

        private string robotName;
        private RandomNumberGenerator rng;
        private byte[] data;
        // exposed so an in-process visualizer can read the latest scan directly
        // (the PduManager declared-reader path does not surface our own writes).
        public int LastDetections { get; private set; } = 0;
        public byte[] LastScanData => data;
        private int update_cycle = 1;
        private PointField[] pointFields;

        // self + target velocity tracking (finite difference) for Doppler
        private Vector3 prevSelfPos;
        private bool haveSelf = false;
        private readonly Dictionary<ulong, Vector3> prevTargetPos = new Dictionary<ulong, Vector3>();

        public const string pdu_name_radar_points = "radar_points";
        public const string pdu_name_radar_pos = "radar_pos";

        private struct FieldDef { public string name; public uint offset; public byte datatype; public uint count; }
        private readonly FieldDef[] fields = {
            new FieldDef{ name="x", offset=0, datatype=7, count=1 },
            new FieldDef{ name="y", offset=4, datatype=7, count=1 },
            new FieldDef{ name="z", offset=8, datatype=7, count=1 },
            new FieldDef{ name="intensity", offset=12, datatype=7, count=1 }, // = relative (Doppler) velocity
        };

        public bool SetParams(Radar3DParams p)
        {
            this.Enabled = p.Enabled;
            this.Range = p.Range;
            this.HorizontalFOV = p.HorizontalFOV;
            this.VerticalFOV = p.VerticalFOV;
            this.PointsPerSecond = p.PointsPerSecond;
            this.UpdateRateHz = Math.Max(1, p.UpdateRateHz);
            this.NoiseSeed = p.NoiseSeed;
            this.DrawDebugPoints = p.DrawDebugPoints;
            this.update_cycle = ComputeUpdateCycle();
            return true;
        }

        public Radar3DParams GetParams()
        {
            return new Radar3DParams
            {
                Enabled = Enabled, Range = Range, HorizontalFOV = HorizontalFOV, VerticalFOV = VerticalFOV,
                PointsPerSecond = PointsPerSecond, UpdateRateHz = UpdateRateHz, NoiseSeed = NoiseSeed,
                DrawDebugPoints = DrawDebugPoints
            };
        }

        public void DoInitialize(string robot_name, IPduManager pduManager)
        {
            GD.Print("Initialize Default3DRadarController for " + robot_name);
            this.robotName = robot_name;
            if (this.ExternalSensing)
            {
                // Pattern A: hakoniwa-mujoco-sensor produces radar_points. Godot
                // neither scans nor publishes here (the PDU is declared for READ
                // by DroneAvatar and rendered by RadarPointCloudVisualizer).
                GD.Print("Default3DRadarController: ExternalSensing=true -> skip self scan/publish.");
                return;
            }
            this.rng = new RandomNumberGenerator();
            this.rng.Seed = (ulong)this.NoiseSeed;
            this.data = new byte[max_data_array_size];
            this.update_cycle = ComputeUpdateCycle();

            INamedPdu pdu = pduManager.CreateNamedPdu(robotName, pdu_name_radar_points);
            if (pdu == null) throw new ArgumentException($"ERROR: can not find pdu({robotName}/{pdu_name_radar_points})");
            var pc = new PointCloud2(pdu);
            pointFields = new PointField[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                PointField field = new PointField(pduManager.CreatePduByType("fields", "sensor_msgs", "PointField"));
                field.name = fields[i].name;
                field.offset = fields[i].offset;
                field.datatype = fields[i].datatype;
                field.count = fields[i].count;
                pointFields[i] = field;
            }
            pc.fields = pointFields;
            pduManager.WriteNamedPdu(pdu);
            pduManager.FlushNamedPdu(pdu);
        }

        // Returns number of detections written into `data`.
        private int ScanCone(float dt)
        {
            int numPoints = Math.Max(1, (int)(PointsPerSecond * dt));
            float maxRx = Mathf.Tan(Mathf.DegToRad(HorizontalFOV * 0.5f)) * Range;
            float maxRy = Mathf.Tan(Mathf.DegToRad(VerticalFOV * 0.5f)) * Range;

            Vector3 selfPos = GlobalPosition;
            Vector3 selfVel = Vector3.Zero;
            if (haveSelf && dt > 1e-6f) selfVel = (selfPos - prevSelfPos) / dt;

            var spaceState = GetWorld3D().DirectSpaceState;
            Basis basisInv = GlobalTransform.Basis.Inverse();
            int dataIndex = 0;
            int detections = 0;

            for (int i = 0; i < numPoints; i++)
            {
                float radius = rng.Randf();                  // U(0,1)
                float angle = rng.RandfRange(0f, Mathf.Tau);
                Vector3 localEnd = new Vector3(maxRx * radius * Mathf.Cos(angle),
                                               maxRy * radius * Mathf.Sin(angle),
                                               -Range); // Godot forward = -Z
                Vector3 worldEnd = GlobalTransform * localEnd;
                var query = PhysicsRayQueryParameters3D.Create(selfPos, worldEnd);
                var hit = spaceState.IntersectRay(query);
                if (hit.Count == 0) continue;

                Vector3 hitPos = (Vector3)hit["position"];
                Vector3 dir = (hitPos - selfPos);
                float depth = dir.Length();
                if (depth < 1e-4f) continue;
                Vector3 dirN = dir / depth;

                // target velocity (finite difference per collider; RigidBody3D exact)
                Vector3 targetVel = Vector3.Zero;
                if (hit.ContainsKey("collider") && hit["collider"].Obj is GodotObject collObj)
                {
                    if (collObj is RigidBody3D rb)
                    {
                        targetVel = rb.LinearVelocity;
                    }
                    else if (collObj is Node3D n3 && hit.ContainsKey("rid"))
                    {
                        ulong id = n3.GetInstanceId();
                        Vector3 cur = n3.GlobalPosition;
                        if (prevTargetPos.TryGetValue(id, out Vector3 prev) && dt > 1e-6f)
                            targetVel = (cur - prev) / dt;
                        prevTargetPos[id] = cur;
                    }
                }
                float relVel = (targetVel - selfVel).Dot(dirN); // +: receding

                // local-frame hit point (sensor frame) -> ROS pack like LiDAR
                Vector3 lp = basisInv * dir;
                Buffer.BlockCopy(BitConverter.GetBytes(lp.Z), 0, data, dataIndex + 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(-lp.X), 0, data, dataIndex + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(lp.Y), 0, data, dataIndex + 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(relVel), 0, data, dataIndex + 12, 4);
                dataIndex += point_step;
                detections++;
                if (dataIndex + point_step > data.Length) break;
            }

            prevSelfPos = selfPos;
            haveSelf = true;
            return detections;
        }

        // Physics dt is 0 during _Ready/DoInitialize; guard against div-by-zero
        // (RoundToInt(Inf) throws OverflowException) and fall back to 60 Hz.
        private int ComputeUpdateCycle()
        {
            float pdt = (float)GetPhysicsProcessDeltaTime();
            if (pdt <= 0f || float.IsNaN(pdt) || float.IsInfinity(pdt)) pdt = 1f / 60f;
            return Mathf.Max(1, Mathf.RoundToInt((1.0f / Math.Max(1, this.UpdateRateHz)) / pdt));
        }

        private int count = 0;
        public void DoControl(IPduManager pduManager)
        {
            if (!Enabled) return;
            if (ExternalSensing) return; // Pattern A: mujoco-sensor publishes radar_points
            if (string.IsNullOrEmpty(robotName)) return; // not initialized yet

            // lazy init guards (in case DoInitialize was skipped/aborted)
            if (rng == null) { rng = new RandomNumberGenerator(); rng.Seed = (ulong)NoiseSeed; }
            if (data == null) data = new byte[max_data_array_size];
            if (update_cycle < 1) update_cycle = ComputeUpdateCycle();

            count++;
            if (count < update_cycle) return;
            float pdt = (float)GetPhysicsProcessDeltaTime();
            if (pdt <= 0f) pdt = 1f / 60f;
            float dt = count * pdt;
            count = 0;

            int detections = ScanCone(dt);
            this.LastDetections = detections; // exposed for in-process visualizer (avoids PduManager read)

            INamedPdu pdu = pduManager.CreateNamedPdu(robotName, pdu_name_radar_points);
            if (pdu != null)
            {
                var pc = new PointCloud2(pdu);
                TimeStamp.Set(pc.header);
                pc.header.frame_id = "front_radar_frame";
                pc.height = 1;
                pc.width = (uint)detections;
                pc.is_bigendian = false;
                pc.fields = pointFields;
                pc.point_step = (uint)point_step;
                pc.row_step = (uint)(point_step * detections);
                pc.data = this.data;
                pc.is_dense = true;
                pduManager.WriteNamedPdu(pdu);
                pduManager.FlushNamedPdu(pdu);
            }

            INamedPdu pdu_pos = pduManager.CreateNamedPdu(robotName, pdu_name_radar_pos);
            if (pdu_pos != null)
            {
                var radar_pos = new hakoniwa.pdu.msgs.geometry_msgs.Twist(pdu_pos);
                radar_pos.linear.x = (double)GlobalPosition.Z;
                radar_pos.linear.y = -(double)GlobalPosition.X;
                radar_pos.linear.z = (double)GlobalPosition.Y;
                var euler = GlobalRotation;
                radar_pos.angular.x = -(double)euler.Z;
                radar_pos.angular.y = (double)euler.X;
                radar_pos.angular.z = -(double)euler.Y;
                pduManager.WriteNamedPdu(pdu_pos);
                pduManager.FlushNamedPdu(pdu_pos);
            }
        }
    }
}
