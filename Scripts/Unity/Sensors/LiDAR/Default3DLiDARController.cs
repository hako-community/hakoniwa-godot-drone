using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.sensor_msgs;
using hakoniwa.pdu.unity;
using hakoniwa.sim;
using System;
using Godot;

namespace hakoniwa.objects.core.sensors
{
    public struct PointCloudFieldType
    {
        public string name;
        public uint offset;
        public byte datatype; 
        public uint count; 
        public PointCloudFieldType(string n, uint off, byte type, uint c)
        {
            this.name = n;
            this.offset = off;
            this.datatype = type;
            this.count = c;
        }
    }
    public struct LiDAR3DParams
    {
        public bool Enabled;
        public int NumberOfChannels;
        public int RotationsPerSecond;
        public int PointsPerSecond;
        public float MaxDistance;
        public float VerticalFOVUpper;
        public float VerticalFOVLower;
        public float HorizontalFOVStart;
        public float HorizontalFOVEnd;
        public bool DrawDebugPoints;
    }
    public interface ILiDAR3DController
    {
        public bool SetParams(LiDAR3DParams param);
        public LiDAR3DParams GetParams();
        public void DoInitialize(string robot_name, IPduManager pduManager);
        public void DoControl(IPduManager pduManager);
    }

    public partial class Default3DLiDARController : Node3D, ILiDAR3DController
    {
        [Export]
        public bool Enabled = true;
        [Export]
        public int NumberOfChannels = 16;
        [Export]
        public int RotationsPerSecond = 10;
        [Export]
        public int PointsPerSecond = 10000;
        [Export]
        public float MaxDistance = 10;
        [Export]
        public float VerticalFOVUpper = -15f;
        [Export]
        public float VerticalFOVLower = -25f;
        [Export]
        public float HorizontalFOVStart = -20f;
        [Export]
        public float HorizontalFOVEnd = 20f;
        [Export]
        public bool DrawDebugPoints = true;

        public const int MaxHeight = 61;
        public const int MaxWidth = 181;

        public float deg_interval_h = 1f;
        public float deg_interval_v = 1f;

        public int height = 61;
        public int width = 181;
        public int update_cycle = 1;

        public int PointsPerRotation;
        public int HorizontalPointsPerRotation;
        public float HorizontalRanges;
        public float VerticalRanges;
        public float SecondsPerRotation;

        public bool SetParams(LiDAR3DParams param)
        {
            PointsPerRotation = param.PointsPerSecond / param.RotationsPerSecond;
            HorizontalPointsPerRotation = PointsPerRotation / param.NumberOfChannels;
            HorizontalRanges = param.HorizontalFOVEnd - param.HorizontalFOVStart;
            VerticalRanges = param.VerticalFOVUpper - param.VerticalFOVLower;
            SecondsPerRotation = 1.0f / (float)param.RotationsPerSecond;

            if (param.NumberOfChannels > MaxHeight)
            {
                GD.PrintErr("NumberOfChannels is invalid: " + param.NumberOfChannels);
                return false;
            }
            if (HorizontalPointsPerRotation > MaxWidth)
            {
                GD.PrintErr("PointsPerRotation / NumberOfChannels is invalid: " + HorizontalPointsPerRotation);
                return false;
            }

            this.height = param.NumberOfChannels;
            this.width = HorizontalPointsPerRotation;
            this.deg_interval_h = HorizontalRanges / HorizontalPointsPerRotation;
            this.deg_interval_v = VerticalRanges / param.NumberOfChannels;
            this.update_cycle = Mathf.RoundToInt(SecondsPerRotation / (float)GetPhysicsProcessDeltaTime());

            this.Enabled = param.Enabled;
            this.NumberOfChannels = param.NumberOfChannels;
            this.RotationsPerSecond = param.RotationsPerSecond;
            this.PointsPerSecond = param.PointsPerSecond;
            this.MaxDistance = param.MaxDistance;
            this.VerticalFOVLower = param.VerticalFOVLower;
            this.VerticalFOVUpper = param.VerticalFOVUpper;
            this.HorizontalFOVStart = param.HorizontalFOVStart;
            this.HorizontalFOVEnd = param.HorizontalFOVEnd;
            this.DrawDebugPoints = param.DrawDebugPoints;
            return true;
        }

        public LiDAR3DParams GetParams()
        {
            return new LiDAR3DParams
            {
                Enabled = this.Enabled,
                NumberOfChannels = this.NumberOfChannels,
                RotationsPerSecond = this.RotationsPerSecond,
                PointsPerSecond = this.PointsPerSecond,
                MaxDistance = this.MaxDistance,
                VerticalFOVUpper = this.VerticalFOVUpper,
                VerticalFOVLower = this.VerticalFOVLower,
                HorizontalFOVStart = this.HorizontalFOVStart,
                HorizontalFOVEnd = this.HorizontalFOVEnd,
                DrawDebugPoints = this.DrawDebugPoints
            };
        }

        private Node3D sensor;
        private string robotName;

        readonly public int max_data_array_size = 176656;
        private int point_step = 16;
        private int row_step = 0;
        private bool is_bigendian = false;
        private PointCloudFieldType[] fields =
        {
            new PointCloudFieldType("x", 0, 7, 1),
            new PointCloudFieldType("y", 4, 7, 1),
            new PointCloudFieldType("z", 8, 7, 1),
            new PointCloudFieldType("intensity", 12, 7, 1),
        };
        private byte[] data;

        public float view_cycle_h = 2;
        public float view_cycle_v = 2;

        private float GetSensorValue(float degreeYaw, float degreePitch, bool debug)
        {
            // Godot standard forward is -Z. Unity is +Z.
            // Using Basis to calculate rotation.
            Vector3 forward = -GlobalTransform.Basis.Z;
            Vector3 up = GlobalTransform.Basis.Y;
            Vector3 right = GlobalTransform.Basis.X;

            // Godot: Quaternion from axis and angle (radians)
            Quaternion yawRotation = new Quaternion(up, Mathf.DegToRad(degreeYaw));
            Quaternion pitchRotation = new Quaternion(yawRotation * right, Mathf.DegToRad(degreePitch));

            Quaternion finalRotation = yawRotation * pitchRotation;
            Vector3 finalDirection = finalRotation * forward;

            var spaceState = GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(GlobalPosition, GlobalPosition + finalDirection * MaxDistance);
            // Optionally exclude self
            // query.Exclude = new Godot.Collections.Array<Rid> { ((CollisionObject3D)GetParent()).GetRid() };

            var result = spaceState.IntersectRay(query);

            if (result.Count > 0)
            {
                float distance = GlobalPosition.DistanceTo((Vector3)result["position"]);
                if (debug)
                {
                    // Debug drawing in Godot requires a helper or custom logic
                }
                return distance;
            }
            else
            {
                return MaxDistance;
            }
        }

        private void ScanEnvironment()
        {
            int dataIndex = 0;
            float fixedIntensity = 1.0f;

            int i_v = 0;
            for (float pitch = VerticalFOVLower; pitch <= VerticalFOVUpper; pitch += deg_interval_v)
            {
                bool debug_v = ((i_v % view_cycle_v) == 0);
                i_v++;
                int i_h = 0;
                for (float yaw = HorizontalFOVStart; yaw <= HorizontalFOVEnd; yaw += deg_interval_h)
                {
                    bool debug_h = ((i_h % view_cycle_h) == 0);
                    i_h++;
                    
                    float distance = GetSensorValue(yaw, pitch, (DrawDebugPoints && debug_h && debug_v));
                    Vector3 point = CalculatePoint(distance, yaw, pitch);

                    // Note: Coordinate conversion ROS/Unity/Godot might need adjustment
                    Buffer.BlockCopy(BitConverter.GetBytes(point.Z), 0, data, dataIndex, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(-point.X), 0, data, dataIndex + 4, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(point.Y), 0, data, dataIndex + 8, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(fixedIntensity), 0, data, dataIndex + 12, 4);

                    dataIndex += point_step;
                    if (dataIndex + point_step > data.Length) break;
                }
            }
        }

        private Vector3 CalculatePoint(float distance, float degreeYaw, float degreePitch)
        {
            // Godot: -Z is forward
            Quaternion rotation = Quaternion.FromEuler(new Vector3(Mathf.DegToRad(degreePitch), Mathf.DegToRad(degreeYaw), 0));
            Vector3 forwardInLocal = rotation * Vector3.Forward; 
            return forwardInLocal * distance;
        }

        private void UpdateLidarPdu(hakoniwa.pdu.msgs.sensor_msgs.PointCloud2 point_cloud2)
        {
            TimeStamp.Set(point_cloud2.header);
            point_cloud2.header.frame_id = "front_lidar_frame";
            point_cloud2.height = (uint)this.height;
            point_cloud2.width = (uint)this.width;
            point_cloud2.is_bigendian = this.is_bigendian;
            point_cloud2.fields = pointFields;
            point_cloud2.point_step = (uint)this.point_step;
            point_cloud2.row_step = (uint)this.row_step;
            point_cloud2.data = this.data;
            point_cloud2.is_dense = true;
        }

        public const string pdu_name_lidar_point_cloud = "lidar_points";
        public const string pdu_name_lidar_pos = "lidar_pos";
        private PointField[] pointFields;

        public void DoInitialize(string robot_name, IPduManager pduManager)
        {
            GD.Print("Initialize Default3DLiDARController for " + robot_name);
            this.robotName = robot_name;
            this.sensor = this;
            this.width = Mathf.CeilToInt((HorizontalFOVEnd - HorizontalFOVStart) / deg_interval_h) + 1;
            this.height = Mathf.CeilToInt((VerticalFOVUpper - VerticalFOVLower) / deg_interval_v) + 1;
            this.row_step = this.width * this.point_step;

            if ((this.row_step * this.height) > this.max_data_array_size)
            {
                GD.PrintErr("ERROR: overflow data size: " + (this.row_step * this.height) + " max: " + this.max_data_array_size);
            }
            this.data = new byte[Math.Min(this.row_step * this.height, max_data_array_size)];

            INamedPdu pdu = pduManager.CreateNamedPdu(robotName, pdu_name_lidar_point_cloud);
            if (pdu == null) throw new ArgumentException($"ERROR: can not find pdu({robotName}/{pdu_name_lidar_point_cloud})");
            
            var point_cloud2 = new hakoniwa.pdu.msgs.sensor_msgs.PointCloud2(pdu);
            pointFields = new PointField[this.fields.Length];
            for (int i = 0; i < this.fields.Length; i++)
            {
                PointField field = new PointField(pduManager.CreatePduByType("fields", "sensor_msgs", "PointField"));
                field.name = this.fields[i].name;
                field.offset = (uint)this.fields[i].offset;
                field.datatype = (byte)this.fields[i].datatype;
                field.count = (uint)this.fields[i].count;
                pointFields[i] = field;
            }
            point_cloud2.fields = pointFields;
            pduManager.WriteNamedPdu(pdu);
            pduManager.FlushNamedPdu(pdu);
        }

        private int count = 0;
        public void DoControl(IPduManager pduManager)
        {
            if (!this.Enabled) return;
            
            this.count++;
            if (this.count < this.update_cycle) return;
            this.count = 0;
            
            this.ScanEnvironment();

            INamedPdu pdu_point_cloud2 = pduManager.CreateNamedPdu(robotName, pdu_name_lidar_point_cloud);
            if (pdu_point_cloud2 != null)
            {
                var point_cloud2 = new PointCloud2(pdu_point_cloud2);
                this.UpdateLidarPdu(point_cloud2);
                pduManager.WriteNamedPdu(pdu_point_cloud2);
                pduManager.FlushNamedPdu(pdu_point_cloud2);
            }

            INamedPdu pdu_lidar_pos = pduManager.CreateNamedPdu(robotName, pdu_name_lidar_pos);
            if (pdu_lidar_pos != null)
            {
                var lidar_pos = new hakoniwa.pdu.msgs.geometry_msgs.Twist(pdu_lidar_pos);
                this.UpdatePosPdu(lidar_pos);
                pduManager.WriteNamedPdu(pdu_lidar_pos);
                pduManager.FlushNamedPdu(pdu_lidar_pos);
            }
        }

        private void UpdatePosPdu(hakoniwa.pdu.msgs.geometry_msgs.Twist lidar_pos)
        {
            // Godot GlobalPosition to ROS Frame
            lidar_pos.linear.x = (double)GlobalPosition.Z;
            lidar_pos.linear.y = -(double)GlobalPosition.X;
            lidar_pos.linear.z = (double)GlobalPosition.Y;

            var euler = GlobalRotation; // Radians
            lidar_pos.angular.x = -(double)euler.Z;
            lidar_pos.angular.y = (double)euler.X;
            lidar_pos.angular.z = -(double)euler.Y;
        }
    }
}