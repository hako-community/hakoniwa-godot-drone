using Godot;
using System;
using System.Collections.Generic;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.msgs.sensor_msgs;
using hakoniwa.sim.core;

namespace hakoniwa.objects.core.sensors
{
    // Sensor visualization rig (§18 Stage V) -- used ONLY by Scenes/sensor_viz.tscn.
    //
    // Design intent (user-confirmed): what should be drawn is
    //   (1) the sensor's DETECTION RANGE in front of the drone, and
    //   (2) the objects that enter it.
    // The point cloud is evidence, not the主役. So the range is a clean wireframe
    // (never a translucent solid) and its FOV/Range come from the A-2 manifest --
    // never hard-coded -- so what is drawn always matches what the sensor actually is.
    //
    // Why this class implements IRadar3DController:
    //   DroneAvatar.FindComponents<IRadar3DController>() scans the whole scene tree
    //   and only auto-creates its own Default3DRadarController + RadarPointCloudVisualizer
    //   when none is found. By being found here we (a) suppress that legacy Pattern-B
    //   pair, (b) get radar_points declared for READ by DroneAvatar, and (c) get
    //   DoInitialize/DoControl driven for free -- all WITHOUT modifying any existing
    //   script or drone_1.tscn.
    //
    // lidar_points is declared for READ by this rig itself, because sensor_viz.tscn
    // carries no ILiDAR3DController (its LiDAR node is left as inert decoration).
    // User-confirmed (2026-07-22): LiDAR と Radar を同時に出さない。片方ずつだけ見る。
    public enum SensorVizMode { RadarOnly, LidarOnly, None }

    // Scene camera / top-down orthographic / oblique chase. The overhead views exist
    // so the DETECTION RANGE can be judged as a whole, which the close third-person
    // scene camera cannot show.
    public enum SensorVizCam { Scene, Top, Oblique }

    public partial class SensorVizRig : Node3D, IRadar3DController
    {
        // --- inspector ---------------------------------------------------------
        [Export] public string LidarPduName = "lidar_points";
        [Export] public string RadarPduName = "radar_points";
        [Export] public float LidarPointSize = 0.03f;
        [Export] public float RadarPointSize = 0.07f;
        [Export] public float TopCamHeight = 30.0f;
        [Export] public int MaxPoints = 200000;
        [Export] public float DopplerFullScale = 1.2f;   // m/s mapped to full red/blue (indoor speeds are ~0.5 m/s)
        [Export] public bool ShowHud = true;
        // Only the primary rig owns the shared scene camera, the HUD and the
        // camera-cycle keys. Secondary rigs (extra drones in a 2-drone view) still
        // draw their own cone / points / clusters but must not build cameras or a
        // second HUD. Set false on cloned avatars.
        [Export] public bool IsPrimary = true;
        // V-4 detection: points are grouped into objects by a voxel grid + union-find.
        [Export] public float ClusterCellM = 0.40f;
        [Export] public int MinPointsRadar = 3;
        [Export] public int MinPointsLidar = 12;
        [Export] public int MaxDetections = 8;
        [Export] public float WarnDistanceM = 3.0f;   // inside this -> red / WARNING
        // Floating 3D text next to every detection ("2.61 m +3 deg / 120 pts +1.4 m/s").
        // Useful when a single drone is scanning a room, but with two drones the tags
        // overlap each other and the aircraft, so the avoidance scene turns them off
        // and reads the same numbers from the HUD instead. Boxes/points are unaffected.
        [Export] public bool ShowDetectionLabels = true;
        // The origin-01 glb ships its own motor/propeller visuals under a "Dynamics"
        // node. They are static (the spinning propellers are separate instances placed
        // by the scene), so leaving them visible draws a second, oversized set of
        // blades. Hidden here so the scene file stays free of import-path guesswork.
        [Export] public string HideModelNodeName = "Dynamics";

        // IRadar3DController: always true here -- this rig never senses, it only draws.
        public bool ExternalSensing { get; set; } = true;

        // --- sensor spec (from the A-2 manifest; defaults are only a fallback) ---
        private sealed class SensorSpec
        {
            public bool Found;
            public float RangeM = 20.0f;
            public float HFovDeg = 60.0f;
            public float VFovDeg = 20.0f;
            // Explicit angular window (deg, azimuth positive left / elevation positive
            // up). NaN means the manifest did not give one, so the symmetric FOV above
            // is used -- mirroring math::WindowOf on the sensor side. A sensor may look
            // somewhere other than straight ahead (a rear sector, a downward slice), and
            // what is drawn has to be where it actually looks.
            public float AzStartDeg = float.NaN;
            public float AzEndDeg = float.NaN;
            public float ElStartDeg = float.NaN;
            public float ElEndDeg = float.NaN;
            public Vector3 MountRos = Vector3.Zero;   // ROS body frame: x fwd, y left, z up
            public float MountYawDeg;

            // Distance-dependent detection model (the RCS stand-in), mirroring
            // math::DetectionProbability on the sensor side:
            //     P(R) = 1                      for R <= ref
            //     P(R) = (ref / R) ^ falloff    for R >  ref
            // ref <= 0 means the model is off and every in-range hit is reported.
            // Drawing only the geometric `range` while this is active is a lie: at
            // ref=6/range=20 the drawn edge detects barely 9% of the time.
            public float DetectionRefM;               // 0 = model disabled
            public float DetectionFalloffExp = 2.0f;
            // The RCS the reference range is quoted against, and the RCS of the
            // target the isosurface is drawn FOR. Rmax scales as sigma^(1/4), so a
            // shinier target pushes every isosurface outward. Drawing only the
            // reference target would repeat #1's mistake one level down: the
            // picture would be right for a 1 m^2 target and wrong for the aircraft
            // actually being tracked.
            public float ReferenceRcsM2 = 1.0f;
            public float TargetRcsM2 = 1.0f;

            public bool HasDetectionModel => DetectionRefM > 0.0f
                                          && DetectionFalloffExp > 0.0f
                                          && DetectionRefM < RangeM;

            /// Reference range after scaling for the target being drawn.
            public float EffectiveRefM => (ReferenceRcsM2 > 0.0f && TargetRcsM2 > 0.0f)
                ? DetectionRefM * Mathf.Pow(TargetRcsM2 / ReferenceRcsM2, 0.25f)
                : DetectionRefM;

            /// Range at which detection probability falls to `p` (0 &lt; p &lt;= 1),
            /// for the target this spec is drawn for.
            /// Inverting P above: R = ref_eff * p^(-1/falloff).
            public float RangeAtProbability(float p)
                => EffectiveRefM * Mathf.Pow(p, -1.0f / DetectionFalloffExp);

            public float Range50 => RangeAtProbability(0.5f);

            public float Az0 => float.IsNaN(AzStartDeg) ? -0.5f * HFovDeg : AzStartDeg;
            public float Az1 => float.IsNaN(AzEndDeg) ? 0.5f * HFovDeg : AzEndDeg;
            public float El0 => float.IsNaN(ElStartDeg) ? -0.5f * VFovDeg : ElStartDeg;
            public float El1 => float.IsNaN(ElEndDeg) ? 0.5f * VFovDeg : ElEndDeg;
            public float AzSpanDeg => Az1 - Az0;
            public float ElSpanDeg => El1 - El0;
            public bool FullCircle => Mathf.Abs(AzSpanDeg) >= 359.5f;
            public bool Asymmetric => !float.IsNaN(AzStartDeg) || !float.IsNaN(ElStartDeg);

            public string Label => Asymmetric
                ? $"az {Az0:F0}..{Az1:F0}, el {El0:F0}..{El1:F0} deg"
                : $"fov {HFovDeg:F0}x{VFovDeg:F0} deg";

            // Spelled out on the HUD because the geometric range alone is misleading
            // once the falloff is on: P(range) is what the edge is actually worth.
            public string DetectionLabel => HasDetectionModel
                ? $"P1.0 {EffectiveRefM:F1} m, P0.5 {Range50:F1} m, "
                  + $"P{ProbabilityAtRange(RangeM):F2} @ {RangeM:F1} m"
                  + (Mathf.Abs(TargetRcsM2 - ReferenceRcsM2) > 1e-6f
                        ? $"  (rcs {TargetRcsM2:G3}/{ReferenceRcsM2:G3} m2)" : "")
                : "no falloff (geometric)";

            /// Detection probability at range `r` -- the same curve the sensor uses.
            public float ProbabilityAtRange(float r)
            {
                float refEff = EffectiveRefM;
                if (!HasDetectionModel || r <= refEff) return 1.0f;
                return Mathf.Clamp(Mathf.Pow(refEff / r, DetectionFalloffExp), 0.0f, 1.0f);
            }
        }

        // Draw the detection envelope for a target of this RCS (m^2) instead of the
        // manifest's reference target. 0 = use the reference.
        private static float TargetRcsOverride
        {
            get
            {
                string s = OS.GetEnvironment("HAKO_VIZ_TARGET_RCS");
                return (!string.IsNullOrEmpty(s) && float.TryParse(s, out float v) && v > 0.0f)
                    ? v : 0.0f;
            }
        }

        /// Monostatic radar equation, mirroring math::RadarEquationRange on the
        /// sensor side. Returns 0 for an incomplete budget.
        private static float RadarEquationRange(float txPowerW, float gainDbi, float wavelengthM,
                                                float rcsM2, float minSignalW)
        {
            if (txPowerW <= 0.0f || wavelengthM <= 0.0f || rcsM2 <= 0.0f || minSignalW <= 0.0f)
                return 0.0f;
            double g = Mathf.Pow(10.0f, gainDbi / 10.0f);
            double fourPiCubed = Mathf.Pow(4.0f * Mathf.Pi, 3.0f);
            double num = txPowerW * g * g * (double)wavelengthM * wavelengthM * rcsM2;
            return (float)Mathf.Pow((float)(num / (fourPiCubed * minSignalW)), 0.25f);
        }

        private readonly SensorSpec lidarSpec = new SensorSpec { RangeM = 20.0f, HFovDeg = 360.0f, VFovDeg = 30.0f };
        private readonly SensorSpec radarSpec = new SensorSpec { RangeM = 20.0f, HFovDeg = 60.0f, VFovDeg = 20.0f };

        // --- runtime state -----------------------------------------------------
        private string robotName;
        private SensorVizMode mode = SensorVizMode.RadarOnly;
        private SensorVizCam camMode = SensorVizCam.Scene;
        private Camera3D sceneCam, topCam, obliqueCam;
        // One aircraft may carry several radars (a forward sector plus a rear one),
        // each with its own window and its own PDU channel, so the rig keeps a
        // view per radar instead of a single set of fields.
        private sealed class RadarView
        {
            public SensorSpec Spec;
            public string PduName;
            public Node3D Root;
            public MultiMesh Points;
            public DetLayer Det;
            public int Count;
            public float NearestM = -1f;
            public float NearestAzDeg;
        }
        private readonly List<RadarView> radars = new List<RadarView>();
        private readonly List<SensorSpec> radarSpecs = new List<SensorSpec>();

        private Node3D lidarRoot;
        private MultiMesh lidarPoints;
        private Label hud;
        private int lidarCount, radarCount;
        private float radarNearestM = -1f, radarNearestAzDeg;
        // Detections from every radar, for the HUD.
        private readonly List<Detection> allDets = new List<Detection>();
        private readonly List<string> radarPduNames = new List<string>();
        // Filled by UpdatePoints for the radar it was just called on.
        private float radarNearestScratch = -1f, radarNearestAzScratch;
        // Warnings found by the most recent UpdateDetections call.
        private int frameWarnCount;
        private const int PointStep = 16;   // x,y,z + intensity/velocity (4 x float32)

        // --- V-4 detection state ------------------------------------------------
        private sealed class DetLayer
        {
            public ImmediateMesh Mesh;
            public readonly List<Label3D> Labels = new List<Label3D>();
        }
        private sealed class Detection
        {
            public Vector3 Min, Max, Centroid;   // ROS sensor frame
            public int Count;
            public float MinRange, AzDeg, VelMps;
        }
        private DetLayer lidarDet;
        private readonly List<Vector3> pts = new List<Vector3>();   // ROS frame, current frame
        private readonly List<float> vals = new List<float>();
        private readonly List<Detection> dets = new List<Detection>();
        private int warnCount;

        // ======================================================================
        // IRadar3DController (driven by DroneAvatar)
        // ======================================================================
        public bool SetParams(Radar3DParams p) { return true; }   // spec comes from the manifest

        public Radar3DParams GetParams()
        {
            return new Radar3DParams
            {
                Enabled = true,
                Range = radarSpec.RangeM,
                HorizontalFOV = radarSpec.HFovDeg,
                VerticalFOV = radarSpec.VFovDeg,
                PointsPerSecond = 0,
                UpdateRateHz = 0,
                NoiseSeed = 0,
                DrawDebugPoints = false
            };
        }

        public void DoInitialize(string robot_name, IPduManager pduManager)
        {
            robotName = robot_name;

            LoadManifest();
            DeclareLidarForRead();
            ParseInitialMode();

            lidarRoot = BuildSensorRoot("LidarViz", lidarSpec);
            BuildRadarViews();
            lidarPoints = AddPointCloud(lidarRoot, LidarPointSize, false);
            AddLidarRangeRings(lidarRoot, lidarSpec);
            lidarDet = BuildDetLayer(lidarRoot);
            BuildCameras();
            HideModelBuiltinDynamics();
            if (ShowHud) BuildHud();

            ApplyMode();
            ApplyCamera();
            GD.Print($"[SensorViz] initialized robot={robotName} mode={mode} " +
                     $"lidar(range={lidarSpec.RangeM}m vfov={lidarSpec.VFovDeg}deg found={lidarSpec.Found}) " +
                     $"radar x{radars.Count}(" + string.Join(", ", radars.ConvertAll(
                         v => $"{v.PduName}: range={v.Spec.RangeM}m {v.Spec.Label} [{v.Spec.DetectionLabel}]")) + ")");
        }

        public void DoControl(IPduManager pduManager)
        {
            if (mode == SensorVizMode.LidarOnly)
            {
                lidarCount = UpdatePoints(pduManager, LidarPduName, lidarPoints, false);
                UpdateDetections(lidarDet, MinPointsLidar);
            }
            else if (mode == SensorVizMode.RadarOnly)
            {
                radarCount = 0;
                radarNearestM = -1f;
                allDets.Clear();
                warnCount = 0;
                foreach (RadarView v in radars)
                {
                    v.Count = UpdatePoints(pduManager, v.PduName, v.Points, true);
                    v.NearestM = radarNearestScratch;
                    v.NearestAzDeg = radarNearestAzScratch;
                    UpdateDetections(v.Det, MinPointsRadar);
                    radarCount += v.Count;
                    allDets.AddRange(dets);
                    warnCount += frameWarnCount;
                    if (v.NearestM > 0f && (radarNearestM < 0f || v.NearestM < radarNearestM))
                    {
                        radarNearestM = v.NearestM;
                        radarNearestAzDeg = v.NearestAzDeg;
                    }
                }
                allDets.Sort((a, b) => a.MinRange.CompareTo(b.MinRange));
            }
            UpdateCameras();
            UpdateHud();
        }

        private void HideModelBuiltinDynamics()
        {
            if (string.IsNullOrEmpty(HideModelNodeName)) return;
            Node root = GetParent() ?? this;
            var found = new List<Node>();
            CollectByName(root, HideModelNodeName, found);
            foreach (Node n in found)
            {
                if (n is Node3D n3) { n3.Visible = false; GD.Print($"[SensorViz] hid built-in node: {n3.GetPath()}"); }
            }
        }

        private static void CollectByName(Node node, string name, List<Node> outList)
        {
            if (node.Name == name) outList.Add(node);
            foreach (Node c in node.GetChildren()) CollectByName(c, name, outList);
        }

        // ======================================================================
        // mode switching (V-0)
        // ======================================================================
        private void ParseInitialMode()
        {
            switch (OS.GetEnvironment("HAKO_VIZ_MODE").ToLower())
            {
                case "lidar": mode = SensorVizMode.LidarOnly; break;
                case "none":  mode = SensorVizMode.None; break;
                case "radar":
                default:      mode = SensorVizMode.RadarOnly; break;
            }
            string z = OS.GetEnvironment("HAKO_VIZ_CAM_ZOOM");
            if (!string.IsNullOrEmpty(z) && float.TryParse(z, out float zv) && zv > 0.01f)
            {
                camZoom = Mathf.Clamp(zv, 0.05f, 4.0f);
            }
            switch (OS.GetEnvironment("HAKO_VIZ_CAM").ToLower())
            {
                case "top":     camMode = SensorVizCam.Top; break;
                case "oblique": camMode = SensorVizCam.Oblique; break;
                default:        camMode = SensorVizCam.Scene; break;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is not InputEventKey k || !k.Pressed || k.Echo) return;
            switch (k.Keycode)
            {
                case Key.L: case Key.Key1: SetMode(SensorVizMode.LidarOnly); return;
                case Key.R: case Key.Key2: SetMode(SensorVizMode.RadarOnly); return;
                case Key.N: case Key.Key0: SetMode(SensorVizMode.None); return;
                case Key.C:
                    camMode = (SensorVizCam)(((int)camMode + 1) % 3);
                    ApplyCamera();
                    GD.Print($"[SensorViz] camera -> {camMode}");
                    return;
                // zoom: the sensor range and the room size need not match, so the
                // overhead views must be zoomable to judge either one.
                case Key.Equal: case Key.Plus: case Key.KpAdd:
                    camZoom = Mathf.Max(0.05f, camZoom * 0.8f); return;
                case Key.Minus: case Key.KpSubtract:
                    camZoom = Mathf.Min(4.0f, camZoom / 0.8f); return;
                default: return;
            }
        }

        private void SetMode(SensorVizMode next)
        {
            if (next == mode) return;
            mode = next;
            ApplyMode();
            GD.Print($"[SensorViz] mode -> {mode}");
        }

        // Only one sensor is ever shown at a time (user-confirmed).
        private void ApplyMode()
        {
            bool lidarOn = mode == SensorVizMode.LidarOnly;
            bool radarOn = mode == SensorVizMode.RadarOnly;
            if (lidarRoot != null) lidarRoot.Visible = lidarOn;
            foreach (RadarView v in radars) if (v.Root != null) v.Root.Visible = radarOn;
            if (!lidarOn) { lidarCount = 0; if (lidarPoints != null) lidarPoints.InstanceCount = 0; }
            if (!radarOn)
            {
                radarCount = 0; radarNearestM = -1f; allDets.Clear();
                foreach (RadarView v in radars) if (v.Points != null) v.Points.InstanceCount = 0;
            }
        }

        // ======================================================================
        // cameras: the detection range must be judgeable as a whole
        // ======================================================================
        private void BuildCameras()
        {
            if (!IsPrimary) return;   // secondary rigs never touch the shared camera
            sceneCam = GetViewport()?.GetCamera3D();

            float r = Mathf.Max(radarSpec.RangeM, lidarSpec.RangeM);
            // Top-down is ORTHOGONAL and sized to the detection range: the fan/rings
            // read like a radar PPI display and are free of perspective distortion.
            topCam = new Camera3D
            {
                Name = "VizCamTop",
                TopLevel = true,
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size = r * 2.2f,
                Near = 0.05f,
                Far = TopCamHeight * 2f
            };
            AddChild(topCam);

            obliqueCam = new Camera3D { Name = "VizCamOblique", TopLevel = true, Fov = 60f, Far = 500f };
            AddChild(obliqueCam);
        }

        private float camZoom = 1.0f;

        private void UpdateCameras()
        {
            if (topCam == null) return;
            float rr = Mathf.Max(radarSpec.RangeM, lidarSpec.RangeM);
            topCam.Size = rr * 2.2f * camZoom;
            Vector3 p = GlobalPosition;
            Vector3 fwd = -GlobalTransform.Basis.Z;                 // drone heading (Godot forward)
            Vector3 fwdFlat = new Vector3(fwd.X, 0, fwd.Z);
            if (fwdFlat.LengthSquared() < 1e-6f) fwdFlat = Vector3.Forward;
            fwdFlat = fwdFlat.Normalized();

            // heading-up so the fan always points to the top of the screen
            topCam.GlobalPosition = p + new Vector3(0, TopCamHeight, 0);
            topCam.LookAt(p, fwdFlat);

            float r = rr * camZoom;
            obliqueCam.GlobalPosition = p - fwdFlat * (r * 0.9f) + new Vector3(0, r * 0.75f, 0);
            obliqueCam.LookAt(p + fwdFlat * (r * 0.35f), Vector3.Up);
        }

        private void ApplyCamera()
        {
            switch (camMode)
            {
                case SensorVizCam.Top: topCam?.MakeCurrent(); break;
                case SensorVizCam.Oblique: obliqueCam?.MakeCurrent(); break;
                default: sceneCam?.MakeCurrent(); break;
            }
        }

        // ======================================================================
        // manifest (V-2: single source of truth for FOV / Range / mount)
        // ======================================================================
        private void LoadManifest()
        {
            // Aircraft may carry different sensor fits (a forward radar on one, a
            // rear sector on the other), so the secondary rig looks for its own
            // manifest first and falls back to the shared one.
            string path = IsPrimary ? "" : OS.GetEnvironment("HAKO_SENSOR_MANIFEST2");
            if (string.IsNullOrEmpty(path))
            {
                path = OS.GetEnvironment("HAKO_SENSOR_MANIFEST");
            }
            if (string.IsNullOrEmpty(path))
            {
                GD.Print("[SensorViz] HAKO_SENSOR_MANIFEST unset -> using fallback specs");
                return;
            }
            if (!Godot.FileAccess.FileExists(path))
            {
                GD.PrintErr($"[SensorViz] manifest not found: {path}");
                return;
            }
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            var json = new Json();
            if (json.Parse(f.GetAsText()) != Error.Ok)
            {
                GD.PrintErr($"[SensorViz] manifest parse error: {json.GetErrorMessage()}");
                return;
            }
            var root = json.Data.AsGodotDictionary();
            if (!root.ContainsKey("components")) return;

            foreach (var e in root["components"].AsGodotArray())
            {
                var comp = e.AsGodotDictionary();
                string type = comp.ContainsKey("type") ? comp["type"].AsString() : "";
                var prm = comp.ContainsKey("params") ? comp["params"].AsGodotDictionary() : new Godot.Collections.Dictionary();

                if (type == "lidar_3d")
                {
                    lidarSpec.Found = true;
                    lidarSpec.RangeM = GetF(prm, "max_distance", lidarSpec.RangeM);
                    float lo = GetF(prm, "vertical_fov_lower_deg", -15f);
                    float hi = GetF(prm, "vertical_fov_upper_deg", 15f);
                    lidarSpec.VFovDeg = hi - lo;
                    float hs = GetF(prm, "horizontal_fov_start_deg", -180f);
                    float he = GetF(prm, "horizontal_fov_end_deg", 180f);
                    lidarSpec.HFovDeg = he - hs;
                    ReadMount(comp, lidarSpec);
                }
                else if (type == "radar")
                {
                    // Every radar in the manifest gets its own spec and PDU name.
                    // The first one also fills radarSpec, which the cameras and the
                    // IRadar3DController params still read.
                    var spec = new SensorSpec { Found = true };
                    spec.RangeM = GetF(prm, "range", radarSpec.RangeM);
                    spec.HFovDeg = GetF(prm, "horizontal_fov_deg", radarSpec.HFovDeg);
                    spec.VFovDeg = GetF(prm, "vertical_fov_deg", radarSpec.VFovDeg);
                    // Optional asymmetric window; absent keys stay NaN and the
                    // symmetric FOV above is used instead.
                    spec.AzStartDeg = GetF(prm, "azimuth_start_deg", float.NaN);
                    spec.AzEndDeg = GetF(prm, "azimuth_end_deg", float.NaN);
                    spec.ElStartDeg = GetF(prm, "elevation_start_deg", float.NaN);
                    spec.ElEndDeg = GetF(prm, "elevation_end_deg", float.NaN);
                    // Distance-dependent detection model. Absent (or 0) leaves it off,
                    // which is how the non-RCS manifests behave.
                    spec.DetectionRefM = GetF(prm, "detection_reference_range", 0.0f);
                    spec.DetectionFalloffExp = GetF(prm, "detection_falloff_exp", 2.0f);
                    spec.ReferenceRcsM2 = GetF(prm, "reference_rcs_m2", 1.0f);
                    // The manifest may state sensitivity as a radar-equation link
                    // budget instead of a distance; derive the same Rmax the runtime
                    // derives, so the drawing never disagrees with the sensor.
                    {
                        float derived = RadarEquationRange(
                            GetF(prm, "tx_power_w", 0.0f),
                            GetF(prm, "antenna_gain_dbi", 0.0f),
                            GetF(prm, "wavelength_m", 0.0f),
                            spec.ReferenceRcsM2,
                            GetF(prm, "min_detectable_signal_w", 0.0f));
                        if (derived > 0.0f) spec.DetectionRefM = derived;
                    }
                    // Which target the envelope is drawn for. Defaults to the
                    // reference, and HAKO_VIZ_TARGET_RCS overrides it so an operator
                    // can ask "how far would I see THIS aircraft?".
                    spec.TargetRcsM2 = TargetRcsOverride > 0.0f
                        ? TargetRcsOverride : spec.ReferenceRcsM2;
                    ReadMount(comp, spec);
                    // The PDU name is what ties the drawn window to the channel the
                    // sensor actually publishes on.
                    string pdu = comp.ContainsKey("pdu_name") ? comp["pdu_name"].AsString() : "";
                    if (string.IsNullOrEmpty(pdu) || pdu == "radar_scan") pdu = RadarPduName;
                    radarSpecs.Add(spec);
                    radarPduNames.Add(pdu);
                    if (radarSpecs.Count == 1)
                    {
                        radarSpec.Found = true;
                        radarSpec.RangeM = spec.RangeM;
                        radarSpec.HFovDeg = spec.HFovDeg;
                        radarSpec.VFovDeg = spec.VFovDeg;
                        radarSpec.AzStartDeg = spec.AzStartDeg;
                        radarSpec.AzEndDeg = spec.AzEndDeg;
                        radarSpec.ElStartDeg = spec.ElStartDeg;
                        radarSpec.ElEndDeg = spec.ElEndDeg;
                        radarSpec.DetectionRefM = spec.DetectionRefM;
                        radarSpec.DetectionFalloffExp = spec.DetectionFalloffExp;
                        radarSpec.ReferenceRcsM2 = spec.ReferenceRcsM2;
                        radarSpec.TargetRcsM2 = spec.TargetRcsM2;
                    }
                }
            }
        }

        private static void ReadMount(Godot.Collections.Dictionary comp, SensorSpec spec)
        {
            if (!comp.ContainsKey("mount")) return;
            var m = comp["mount"].AsGodotDictionary();
            spec.MountRos = new Vector3(GetF(m, "x", 0f), GetF(m, "y", 0f), GetF(m, "z", 0f));
            spec.MountYawDeg = GetF(m, "yaw_deg", 0f);
        }

        private static float GetF(Godot.Collections.Dictionary d, string key, float fallback)
        {
            return d.ContainsKey(key) ? (float)d[key].AsDouble() : fallback;
        }

        private void DeclareLidarForRead()
        {
            var hakoPdu = HakoAsset.GetHakoPdu();
            if (hakoPdu == null) { GD.PrintErr("[SensorViz] IHakoPdu is null"); return; }
            if (!hakoPdu.DeclarePduForRead(robotName, LidarPduName))
            {
                GD.PrintErr($"[SensorViz] can not declare pdu for read: {robotName}/{LidarPduName}");
            }
            // DroneAvatar declares the primary radar channel on our behalf (that is
            // what implementing IRadar3DController buys). Any FURTHER radar the
            // manifest asks for has to be declared here, or its channel is never
            // surfaced to the PduManager and reads come back empty.
            foreach (string pdu in radarPduNames)
            {
                if (pdu == RadarPduName) continue;
                if (!hakoPdu.DeclarePduForRead(robotName, pdu))
                {
                    GD.PrintErr($"[SensorViz] can not declare pdu for read: {robotName}/{pdu}");
                }
                else
                {
                    GD.Print($"[SensorViz] declared extra radar channel for read: {robotName}/{pdu}");
                }
            }
        }

        // ======================================================================
        // scene construction
        // ======================================================================
        // ROS sensor frame (x fwd, y left, z up) -> Godot (x right, y up, z back).
        private static Vector3 RosToGodot(Vector3 ros) => new Vector3(-ros.Y, ros.Z, -ros.X);

        // One view per radar in the manifest. With no manifest (or none carrying a
        // radar) a single fallback view keeps the previous single-radar behaviour.
        private void BuildRadarViews()
        {
            if (radarSpecs.Count == 0)
            {
                radarSpecs.Add(radarSpec);
                radarPduNames.Add(RadarPduName);
            }
            for (int i = 0; i < radarSpecs.Count; i++)
            {
                var v = new RadarView { Spec = radarSpecs[i], PduName = radarPduNames[i] };
                v.Root = BuildSensorRoot($"RadarViz{i}", v.Spec);
                v.Points = AddPointCloud(v.Root, RadarPointSize, true);
                AddRadarFovWireframe(v.Root, v.Spec);
                v.Det = BuildDetLayer(v.Root);
                radars.Add(v);
            }
        }

        private Node3D BuildSensorRoot(string name, SensorSpec spec)
        {
            var n = new Node3D { Name = name, Position = RosToGodot(spec.MountRos) };
            n.RotateY(-Mathf.DegToRad(spec.MountYawDeg));
            AddChild(n);
            return n;
        }

        private MultiMesh AddPointCloud(Node3D parent, float size, bool perPointColor)
        {
            var mesh = new BoxMesh { Size = new Vector3(size, size, size) };
            var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            if (perPointColor) mat.VertexColorUseAsAlbedo = true;
            else mat.AlbedoColor = new Color(0.2f, 0.9f, 0.3f);   // LiDAR green
            mesh.Material = mat;

            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = perPointColor,
                Mesh = mesh,
                InstanceCount = 0
            };
            parent.AddChild(new MultiMeshInstance3D { Multimesh = mm });
            return mm;
        }

        private static StandardMaterial3D LineMaterial()
        {
            return new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
        }

        // direction of (azimuth, elevation) in the ROS sensor frame, as a Godot vector
        private static Vector3 Dir(float azRad, float elRad, float r)
        {
            var ros = new Vector3(
                r * Mathf.Cos(elRad) * Mathf.Cos(azRad),
                r * Mathf.Cos(elRad) * Mathf.Sin(azRad),
                r * Mathf.Sin(elRad));
            return RosToGodot(ros);
        }

        // V-1: LiDAR is (nearly) omnidirectional -> show range as horizontal rings.
        private void AddLidarRangeRings(Node3D parent, SensorSpec spec)
        {
            var im = new ImmediateMesh();
            var mat = LineMaterial();
            var edge = new Color(0.2f, 0.9f, 0.3f, 0.85f);       // max-range circle
            var ring = new Color(0.2f, 0.9f, 0.3f, 0.25f);       // intermediate rings / cone
            im.SurfaceBegin(Mesh.PrimitiveType.Lines, mat);

            float half = Mathf.DegToRad(spec.VFovDeg * 0.5f);
            float R = spec.RangeM;
            Ring(im, edge, R, 0f, -Mathf.Pi, Mathf.Pi, 96);              // horizon at max range
            foreach (float el in new[] { -half, half })
                Ring(im, ring, R, el, -Mathf.Pi, Mathf.Pi, 64);          // vertical FOV extent
            for (int i = 1; i <= 3; i++) Ring(im, ring, R * i / 4f, 0f, -Mathf.Pi, Mathf.Pi, 64);
            // radial ticks every 45 deg so bearing is readable
            for (int i = 0; i < 8; i++)
            {
                float az = i * Mathf.Pi / 4f;
                im.SurfaceSetColor(ring); im.SurfaceAddVertex(Vector3.Zero);
                im.SurfaceSetColor(ring); im.SurfaceAddVertex(Dir(az, 0f, R));
            }
            im.SurfaceEnd();
            parent.AddChild(new MeshInstance3D { Mesh = im });
        }

        // V-1: Radar detection volume as a WIREFRAME (never a translucent solid),
        // drawn over the sensor's ACTUAL angular window -- which since the sampler
        // change may be a full 360 deg ring or an off-boresight sector, not just a
        // symmetric forward cone.
        private void AddRadarFovWireframe(Node3D parent, SensorSpec spec)
        {
            var im = new ImmediateMesh();
            var mat = LineMaterial();
            // The horizontal fan is the primary read (especially from the top camera);
            // the 3D envelope stays faint so it never competes with the detections.
            var fan = new Color(0.25f, 0.85f, 1.0f, 0.95f);
            var ring = new Color(0.25f, 0.85f, 1.0f, 0.30f);
            var envelope = new Color(0.25f, 0.85f, 1.0f, 0.22f);
            var axis = new Color(0.25f, 0.85f, 1.0f, 0.45f);
            im.SurfaceBegin(Mesh.PrimitiveType.Lines, mat);

            float az0 = Mathf.DegToRad(spec.Az0), az1 = Mathf.DegToRad(spec.Az1);
            float el0 = Mathf.DegToRad(spec.El0), el1 = Mathf.DegToRad(spec.El1);
            float azMid = 0.5f * (az0 + az1), elMid = 0.5f * (el0 + el1);
            float R = spec.RangeM;
            // Enough segments that a 360 deg ring still looks round.
            int seg = Mathf.Clamp((int)(Mathf.Abs(spec.AzSpanDeg) / 3f), 24, 180);

            // --- horizontal fan at the window's mid elevation ---
            if (!spec.FullCircle)
            {
                // The straight edges only mean something for a sector; on a full
                // circle they would be two coincident spokes.
                foreach (float az in new[] { az0, az1 })
                {
                    im.SurfaceSetColor(fan); im.SurfaceAddVertex(Vector3.Zero);
                    im.SurfaceSetColor(fan); im.SurfaceAddVertex(Dir(az, elMid, R));
                }
            }
            im.SurfaceSetColor(axis); im.SurfaceAddVertex(Vector3.Zero);
            im.SurfaceSetColor(axis); im.SurfaceAddVertex(Dir(azMid, elMid, R));   // boresight
            Ring(im, fan, R, elMid, az0, az1, seg);
            for (int i = 1; i <= 3; i++) Ring(im, ring, R * i / 4f, elMid, az0, az1, Mathf.Max(16, seg / 2));

            // --- detection-probability isosurfaces (only when the RCS model is on) ---
            // Without these the cyan edge reads as "the radar sees this far", which is
            // false once the falloff is active: at ref=6/range=20 the edge detects
            // about 9% of the time. Green = certain, amber = coin flip.
            if (spec.HasDetectionModel)
            {
                float rCertain = spec.EffectiveRefM;      // P = 1.0 out to here
                float r50 = spec.Range50;                 // P = 0.5
                var certain = new Color(0.30f, 0.95f, 0.45f, 0.80f);
                var half = new Color(1.00f, 0.72f, 0.20f, 0.90f);

                Ring(im, certain, rCertain, elMid, az0, az1, seg);
                Ring(im, half, r50, elMid, az0, az1, seg);
                // Faint 3D shells so the isosurfaces read from an oblique camera too,
                // not just from directly above.
                foreach (float el in new[] { el0, el1 })
                {
                    Ring(im, certain * new Color(1, 1, 1, 0.35f), rCertain, el, az0, az1, Mathf.Max(12, seg / 3));
                    Ring(im, half * new Color(1, 1, 1, 0.35f), r50, el, az0, az1, Mathf.Max(12, seg / 3));
                }
                foreach (float az in new[] { az0, az1 })
                {
                    VArc(im, certain * new Color(1, 1, 1, 0.35f), rCertain, az, el0, el1, 10);
                    VArc(im, half * new Color(1, 1, 1, 0.35f), r50, az, el0, el1, 10);
                }
            }

            // --- 3D envelope (faint): corner edges + far-face outline ---
            foreach (float az in new[] { az0, az1 })
                foreach (float el in new[] { el0, el1 })
                {
                    im.SurfaceSetColor(envelope); im.SurfaceAddVertex(Vector3.Zero);
                    im.SurfaceSetColor(envelope); im.SurfaceAddVertex(Dir(az, el, R));
                }
            foreach (float el in new[] { el0, el1 }) Ring(im, envelope, R, el, az0, az1, Mathf.Max(12, seg / 3));
            foreach (float az in new[] { az0, az1 }) VArc(im, envelope, R, az, el0, el1, 12);

            im.SurfaceEnd();
            parent.AddChild(new MeshInstance3D { Mesh = im });
        }

        private static void Ring(ImmediateMesh im, Color c, float r, float el, float az0, float az1, int seg)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = Mathf.Lerp(az0, az1, (float)i / seg);
                float a1 = Mathf.Lerp(az0, az1, (float)(i + 1) / seg);
                im.SurfaceSetColor(c); im.SurfaceAddVertex(Dir(a0, el, r));
                im.SurfaceSetColor(c); im.SurfaceAddVertex(Dir(a1, el, r));
            }
        }

        private static void VArc(ImmediateMesh im, Color c, float r, float az, float el0, float el1, int seg)
        {
            for (int i = 0; i < seg; i++)
            {
                float e0 = Mathf.Lerp(el0, el1, (float)i / seg);
                float e1 = Mathf.Lerp(el0, el1, (float)(i + 1) / seg);
                im.SurfaceSetColor(c); im.SurfaceAddVertex(Dir(az, e0, r));
                im.SurfaceSetColor(c); im.SurfaceAddVertex(Dir(az, e1, r));
            }
        }

        // ======================================================================
        // point cloud update
        // ======================================================================
        private int UpdatePoints(IPduManager pduManager, string pduName, MultiMesh mm, bool isRadar)
        {
            if (mm == null) return 0;
            // Clear FIRST. With several radars these buffers are reused per sensor,
            // so an early return would otherwise leave the previous radar's points
            // in place and the next detection pass would re-report them.
            pts.Clear();
            vals.Clear();
            radarNearestScratch = -1f;
            radarNearestAzScratch = 0f;
            // ReadPdu (not CreateNamedPdu): only the SHM-populated buffer carries the
            // mujoco-sensor's write.
            IPdu pdu = pduManager.ReadPdu(robotName, pduName);
            if (pdu == null) return 0;
            var pc = new PointCloud2(pdu);
            byte[] data = pc.data;
            if (data == null || data.Length < PointStep) { mm.InstanceCount = 0; return 0; }

            int count = Math.Min(data.Length / PointStep, MaxPoints);
            int width = (int)pc.width;
            int height = (int)pc.height;
            int declared = width * Math.Max(1, height);
            if (declared > 0) count = Math.Min(count, declared);
            if (count <= 0) { mm.InstanceCount = 0; return 0; }

            mm.InstanceCount = count;
            int drawn = 0;
            float nearest = float.MaxValue;
            float nearestAz = 0f;

            for (int i = 0; i < count; i++)
            {
                int off = i * PointStep;
                float x = BitConverter.ToSingle(data, off + 0);   // ROS: forward
                float y = BitConverter.ToSingle(data, off + 4);   // ROS: left
                float z = BitConverter.ToSingle(data, off + 8);   // ROS: up
                float w = BitConverter.ToSingle(data, off + 12);  // LiDAR: intensity / Radar: Doppler
                if (x == 0f && y == 0f && z == 0f) continue;

                mm.SetInstanceTransform(drawn, new Transform3D(Basis.Identity, RosToGodot(new Vector3(x, y, z))));
                pts.Add(new Vector3(x, y, z));
                vals.Add(isRadar ? w : 0f);
                if (isRadar)
                {
                    mm.SetInstanceColor(drawn, DopplerColor(w));
                    float d = Mathf.Sqrt(x * x + y * y + z * z);
                    if (d < nearest) { nearest = d; nearestAz = Mathf.RadToDeg(Mathf.Atan2(y, x)); }
                }
                drawn++;
            }
            mm.InstanceCount = drawn;

            if (isRadar)
            {
                radarNearestScratch = (drawn > 0 && nearest < float.MaxValue) ? nearest : -1f;
                radarNearestAzScratch = nearestAz;
            }
            return drawn;
        }

        // red = approaching, blue = receding, amber = static.
        // Amber (not grey) so静止した壁も白い背景の上で確実に見える。
        private Color DopplerColor(float v)
        {
            float t = Mathf.Clamp(v / DopplerFullScale, -1f, 1f);
            var stat = new Color(0.98f, 0.72f, 0.15f);
            if (t < -0.02f) return stat.Lerp(new Color(1f, 0.15f, 0.1f), -t);
            if (t > 0.02f) return stat.Lerp(new Color(0.2f, 0.45f, 1f), t);
            return stat;
        }

        // ======================================================================
        // V-4 detection: point cloud -> objects that entered the detection range
        // ======================================================================
        // Voxel grid + union-find: O(n) so the same code serves both the 150-point
        // radar frame and the 5000-point LiDAR frame.
        private DetLayer BuildDetLayer(Node3D parent)
        {
            var layer = new DetLayer { Mesh = new ImmediateMesh() };
            parent.AddChild(new MeshInstance3D { Mesh = layer.Mesh });
            // No labels requested -> build none. DrawDetections iterates the (empty)
            // list, so nothing else needs to know.
            if (!ShowDetectionLabels) return layer;
            for (int i = 0; i < MaxDetections; i++)
            {
                var lb = new Label3D
                {
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    FontSize = 40,
                    PixelSize = 0.008f,
                    OutlineSize = 14,
                    Visible = false
                };
                parent.AddChild(lb);
                layer.Labels.Add(lb);
            }
            return layer;
        }

        private readonly Dictionary<long, int> cellIdx = new Dictionary<long, int>();
        private readonly List<int> uf = new List<int>();
        private readonly List<int> cCount = new List<int>();
        private readonly List<Vector3> cSum = new List<Vector3>();
        private readonly List<Vector3> cMin = new List<Vector3>();
        private readonly List<Vector3> cMax = new List<Vector3>();
        private readonly List<float> cVel = new List<float>();
        private readonly List<int> cKeyX = new List<int>();
        private readonly List<int> cKeyY = new List<int>();
        private readonly List<int> cKeyZ = new List<int>();

        private int Find(int a) { while (uf[a] != a) { uf[a] = uf[uf[a]]; a = uf[a]; } return a; }
        private void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) uf[b] = a; }
        private static long CellKey(int x, int y, int z) =>
            ((long)(x & 0xFFFFF) << 40) | ((long)(y & 0xFFFFF) << 20) | (long)(z & 0xFFFFF);

        private void UpdateDetections(DetLayer layer, int minPoints)
        {
            dets.Clear();
            warnCount = 0;
            frameWarnCount = 0;
            if (layer == null) return;
            layer.Mesh.ClearSurfaces();

            cellIdx.Clear(); uf.Clear(); cCount.Clear(); cSum.Clear();
            cMin.Clear(); cMax.Clear(); cVel.Clear(); cKeyX.Clear(); cKeyY.Clear(); cKeyZ.Clear();

            float cs = Mathf.Max(0.05f, ClusterCellM);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i];
                int gx = Mathf.FloorToInt(p.X / cs), gy = Mathf.FloorToInt(p.Y / cs), gz = Mathf.FloorToInt(p.Z / cs);
                long k = CellKey(gx, gy, gz);
                if (!cellIdx.TryGetValue(k, out int ci))
                {
                    ci = uf.Count;
                    cellIdx[k] = ci; uf.Add(ci);
                    cCount.Add(0); cSum.Add(Vector3.Zero); cVel.Add(0f);
                    cMin.Add(new Vector3(float.MaxValue, float.MaxValue, float.MaxValue));
                    cMax.Add(new Vector3(float.MinValue, float.MinValue, float.MinValue));
                    cKeyX.Add(gx); cKeyY.Add(gy); cKeyZ.Add(gz);
                }
                cCount[ci]++; cSum[ci] += p; cVel[ci] += vals[i];
                cMin[ci] = cMin[ci].Min(p); cMax[ci] = cMax[ci].Max(p);
            }

            // merge touching cells (26-neighbourhood)
            for (int ci = 0; ci < uf.Count; ci++)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;
                            if (cellIdx.TryGetValue(CellKey(cKeyX[ci] + dx, cKeyY[ci] + dy, cKeyZ[ci] + dz), out int nb))
                                Union(ci, nb);
                        }

            var byRoot = new Dictionary<int, Detection>();
            for (int ci = 0; ci < uf.Count; ci++)
            {
                int r = Find(ci);
                if (!byRoot.TryGetValue(r, out Detection d))
                {
                    d = new Detection
                    {
                        Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                        Max = new Vector3(float.MinValue, float.MinValue, float.MinValue)
                    };
                    byRoot[r] = d;
                }
                d.Count += cCount[ci];
                d.Centroid += cSum[ci];
                d.VelMps += cVel[ci];
                d.Min = d.Min.Min(cMin[ci]);
                d.Max = d.Max.Max(cMax[ci]);
            }

            foreach (var d in byRoot.Values)
            {
                if (d.Count < minPoints) continue;
                d.Centroid /= d.Count;
                d.VelMps /= d.Count;
                d.MinRange = Mathf.Sqrt(d.Min.X * d.Min.X);   // placeholder, replaced below
                d.MinRange = NearestRangeOf(d);
                d.AzDeg = Mathf.RadToDeg(Mathf.Atan2(d.Centroid.Y, d.Centroid.X));
                dets.Add(d);
            }
            dets.Sort((a, b) => a.MinRange.CompareTo(b.MinRange));
            if (dets.Count > MaxDetections) dets.RemoveRange(MaxDetections, dets.Count - MaxDetections);

            DrawDetections(layer);
        }

        // closest approach of the cluster's bounding box to the sensor origin
        private static float NearestRangeOf(Detection d)
        {
            float cx = Mathf.Clamp(0f, d.Min.X, d.Max.X);
            float cy = Mathf.Clamp(0f, d.Min.Y, d.Max.Y);
            float cz = Mathf.Clamp(0f, d.Min.Z, d.Max.Z);
            return new Vector3(cx, cy, cz).Length();
        }

        private void DrawDetections(DetLayer layer)
        {
            for (int i = 0; i < layer.Labels.Count; i++) layer.Labels[i].Visible = false;
            if (dets.Count == 0) return;

            var mat = LineMaterial();
            layer.Mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, mat);
            for (int i = 0; i < dets.Count; i++)
            {
                Detection d = dets[i];
                bool warn = d.MinRange <= WarnDistanceM;
                if (warn) { warnCount++; frameWarnCount++; }
                Color c = warn ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(0.35f, 1f, 0.95f, 0.9f);
                Box(layer.Mesh, c, d.Min, d.Max);

                if (i >= layer.Labels.Count) continue;   // labels disabled
                Label3D lb = layer.Labels[i];
                lb.Visible = true;
                lb.Position = RosToGodot(new Vector3(d.Centroid.X, d.Centroid.Y, d.Max.Z)) + new Vector3(0, 0.18f, 0);
                lb.Modulate = c;
                lb.Text = $"{d.MinRange:F2} m  {d.AzDeg:+0;-0;0} deg\n{d.Count} pts  {d.VelMps:+0.0;-0.0;0.0} m/s";
            }
            layer.Mesh.SurfaceEnd();
        }

        private static void Box(ImmediateMesh im, Color c, Vector3 rosMin, Vector3 rosMax)
        {
            // 8 corners of the ROS-frame AABB, drawn in Godot space
            var v = new Vector3[8];
            int n = 0;
            foreach (float x in new[] { rosMin.X, rosMax.X })
                foreach (float y in new[] { rosMin.Y, rosMax.Y })
                    foreach (float z in new[] { rosMin.Z, rosMax.Z })
                        v[n++] = RosToGodot(new Vector3(x, y, z));
            // indices follow the x,y,z nesting above
            int[,] e = { {0,1},{2,3},{4,5},{6,7}, {0,2},{1,3},{4,6},{5,7}, {0,4},{1,5},{2,6},{3,7} };
            for (int i = 0; i < e.GetLength(0); i++)
            {
                im.SurfaceSetColor(c); im.SurfaceAddVertex(v[e[i, 0]]);
                im.SurfaceSetColor(c); im.SurfaceAddVertex(v[e[i, 1]]);
            }
        }

        // ======================================================================
        // HUD
        // ======================================================================
        private void BuildHud()
        {
            if (!IsPrimary) return;   // one HUD only (owned by the primary rig)
            var layer = new CanvasLayer { Name = "SensorVizHud" };
            AddChild(layer);
            // The HUD normally sits just under Godot's own toolbar. Recorded demos
            // burn a caption across the top of the frame, so HAKO_VIZ_HUD_Y moves it
            // clear of that instead of having the two overlap.
            float hudY = 54f;
            string hy = OS.GetEnvironment("HAKO_VIZ_HUD_Y");
            if (!string.IsNullOrEmpty(hy) && float.TryParse(hy, out float hyv)) hudY = hyv;
            hud = new Label
            {
                Name = "Text",
                Position = new Vector2(14, hudY),
                Modulate = new Color(1, 1, 1)
            };
            hud.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            hud.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            hud.AddThemeConstantOverride("outline_size", 6);
            layer.AddChild(hud);
        }

        private void UpdateHud()
        {
            if (hud == null) return;
            string near = radarNearestM > 0f
                ? $"nearest {radarNearestM:F2} m @ {radarNearestAzDeg:+0;-0;0} deg"
                : "nearest --";
            string body = mode switch
            {
                SensorVizMode.LidarOnly =>
                    $"LiDAR : {lidarCount,6} pts   range {lidarSpec.RangeM:F1} m, vfov {lidarSpec.VFovDeg:F0} deg",
                SensorVizMode.RadarOnly =>
                    $"Radar x{radars.Count} : {radarCount,6} pts   range {radarSpec.RangeM:F1} m\n" +
                    string.Join("", radars.ConvertAll(v =>
                        $"        [{v.PduName}] {v.Spec.Label}  {v.Count} pts\n" +
                        $"          detect: {v.Spec.DetectionLabel}\n")) +
                    $"        {near}",
                _ => "(sensor view off)"
            };
            if (mode != SensorVizMode.None)
            {
                var shown = mode == SensorVizMode.RadarOnly ? allDets : dets;
                body += $"\nDETECTED: {shown.Count} object(s)";
                if (warnCount > 0) body += $"   ***  WARNING: {warnCount} within {WarnDistanceM:F1} m  ***";
                for (int i = 0; i < shown.Count && i < 3; i++)
                {
                    var d = shown[i];
                    body += $"\n  #{i + 1}  {d.MinRange,5:F2} m  {d.AzDeg,4:+0;-0;0} deg  {d.Count,5} pts  {d.VelMps,5:+0.0;-0.0;0.0} m/s";
                }
            }
            hud.Text =
                $"SENSOR: {mode}   [L]iDAR / [R]adar / [N]one     CAM: {camMode} x{camZoom:F2}   [C]ycle  [+/-]zoom\n" + body;
        }
    }
}
