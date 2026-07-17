using Godot;

namespace hakoniwa.env
{
    // Optional Pattern-A visualization helper.
    //
    // hakoniwa-mujoco-sensor senses the world described by env.xml (generated
    // from an OBB json). Godot normally displays its own scene, so the sensed
    // point cloud has no matching visible geometry to align with. When the
    // environment variable HAKO_ENV_OBB points at that OBB json, this builder
    // reconstructs the same room (floor / walls / pillars) in Godot's world
    // frame so the LiDAR/Radar point cloud can be visually checked against the
    // walls it was measured from.
    //
    // No-op when HAKO_ENV_OBB is unset -> the default godot-drone is unaffected
    // and has no dependency on mujoco-sensor or any env file.
    //
    // OBB json is ENU (x=East, y=North, z=Up). Godot is (x=East, y=Up, z=North),
    // matching env.tscn generated alongside env.xml from the same source.
    public static class EnvRoomBuilder
    {
        public static void BuildIfRequested(Node worldParent)
        {
            string path = OS.GetEnvironment("HAKO_ENV_OBB");
            if (string.IsNullOrEmpty(path)) return;
            if (!Godot.FileAccess.FileExists(path))
            {
                GD.PrintErr($"[EnvRoom] HAKO_ENV_OBB file not found: {path}");
                return;
            }
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            var json = new Json();
            if (json.Parse(f.GetAsText()) != Error.Ok)
            {
                GD.PrintErr($"[EnvRoom] JSON parse error: {json.GetErrorMessage()}");
                return;
            }
            var root = json.Data.AsGodotDictionary();
            if (!root.ContainsKey("results"))
            {
                GD.PrintErr("[EnvRoom] no 'results' array in OBB json");
                return;
            }

            var envRoot = new Node3D { Name = "EnvRoom" };

            int n = 0;
            foreach (var entry in root["results"].AsGodotArray())
            {
                var b = entry.AsGodotDictionary();
                var center = b["center"].AsGodotArray();      // [East, North]
                var half = b["half_size"].AsGodotArray();     // [hx, hy]
                double cx = center[0].AsDouble(), cy = center[1].AsDouble();
                double hx = half[0].AsDouble(), hy = half[1].AsDouble();
                double zmin = b.ContainsKey("zmin") ? b["zmin"].AsDouble() : 0.0;
                double zmax = b.ContainsKey("zmax") ? b["zmax"].AsDouble() : 1.0;
                double yaw = b.ContainsKey("yaw_rad") ? b["yaw_rad"].AsDouble() : 0.0;

                // ENU (East, North, Up) -> Godot (x=East, y=Up, z=North)
                var size = new Vector3((float)(2.0 * hx), (float)(zmax - zmin), (float)(2.0 * hy));
                var pos = new Vector3((float)cx, (float)((zmin + zmax) * 0.5), (float)cy);

                var color = new Color(0.82f, 0.82f, 0.86f, 1f);
                if (b.ContainsKey("rgba"))
                {
                    var c = b["rgba"].AsGodotArray();
                    color = new Color((float)c[0].AsDouble(), (float)c[1].AsDouble(),
                                      (float)c[2].AsDouble(), (float)c[3].AsDouble());
                }

                var mesh = new BoxMesh { Size = size };
                mesh.Material = new StandardMaterial3D { AlbedoColor = color };
                var mi = new MeshInstance3D
                {
                    Mesh = mesh,
                    Name = b.ContainsKey("id") ? b["id"].AsString() : $"box{n}",
                    Position = pos
                };
                mi.RotateY((float)-yaw); // ENU yaw (CCW about Up) -> Godot about +Y
                envRoot.AddChild(mi);
                n++;
            }
            // EventInitialize runs while the parent is still setting up children,
            // so defer the attach to avoid "parent busy" errors.
            worldParent.CallDeferred(Node.MethodName.AddChild, envRoot);
            GD.Print($"[EnvRoom] built {n} boxes from {path}");
        }
    }
}
