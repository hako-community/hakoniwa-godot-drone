using Godot;

namespace hakoniwa.objects.core
{
    // Restyles the origin-01 airframe at runtime, without touching the .glb.
    //
    // Two jobs:
    //   1. Hide the model's built-in motor/blade group ("Dynamics"). The spinning
    //      propellers are separate instances, so leaving it visible draws a second,
    //      oversized set of blades.
    //   2. Recolor the near-white surfaces (propellers, frame highlights) to black.
    //      Textured surfaces are left alone so the livery survives.
    //
    // Attach as a child of the drone avatar; it restyles its parent's whole subtree.
    public partial class OriginModelStyle : Node3D
    {
        [Export] public bool Enabled = true;
        [Export] public string HideNodeName = "Dynamics";
        // The sensor visualization subtree is NOT airframe: its range wireframes and
        // point clouds are deliberately colored and must never be repainted.
        [Export] public string SkipNodeName = "SensorViz";
        [Export] public Color TargetColor = new Color(0.06f, 0.06f, 0.06f);
        // surfaces brighter than this (perceived luminance) get repainted
        [Export] public float WhiteThreshold = 0.62f;
        [Export] public bool Verbose = false;

        private StandardMaterial3D blackMat;

        public override void _Ready()
        {
            if (!Enabled) return;
            blackMat = new StandardMaterial3D { AlbedoColor = TargetColor, Metallic = 0.1f, Roughness = 0.6f };
            Node root = GetParent() ?? this;
            int hidden = 0, painted = 0;
            Walk(root, ref hidden, ref painted);
            GD.Print($"[OriginModelStyle] hidden={hidden} repainted={painted} -> {TargetColor.ToHtml(false)}");
        }

        private void Walk(Node node, ref int hidden, ref int painted)
        {
            if (node.Name == SkipNodeName) return;
            if (node.Name == HideNodeName && node is Node3D n3)
            {
                n3.Visible = false;
                hidden++;
                return;   // no need to descend into a hidden branch
            }
            if (node is MeshInstance3D mi && mi.Mesh != null)
            {
                for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                {
                    if (!IsNearWhite(mi.GetActiveMaterial(i))) continue;
                    mi.SetSurfaceOverrideMaterial(i, blackMat);
                    painted++;
                    if (Verbose) GD.Print($"[OriginModelStyle] repaint {mi.GetPath()} surface {i}");
                }
            }
            foreach (Node c in node.GetChildren()) Walk(c, ref hidden, ref painted);
        }

        private bool IsNearWhite(Material m)
        {
            if (m is not StandardMaterial3D sm) return false;
            if (sm.AlbedoTexture != null) return false;    // keep textured surfaces as-is
            if (sm.VertexColorUseAsAlbedo) return false;   // vertex-colored = visualization, not airframe
            Color c = sm.AlbedoColor;
            float lum = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
            return lum >= WhiteThreshold;
        }
    }
}
