using System.Collections.Generic;
using Godot;

public partial class TrajectoryDrawer : Node3D
{
    [Export]
    public Node3D droneTransform; 
    [Export]
    public float minDistance = 0.1f; 

    private MeshInstance3D meshInstance;
    private ImmediateMesh immediateMesh;
    private StandardMaterial3D material;
    private List<Vector3> positions = new List<Vector3>();

    public override void _Ready()
    {
        // LineRenderer の代わりに MeshInstance3D と ImmediateMesh を作成
        meshInstance = new MeshInstance3D();
        immediateMesh = new ImmediateMesh();
        meshInstance.Mesh = immediateMesh;
        
        // 線の色とマテリアルの設定
        material = new StandardMaterial3D();
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        material.AlbedoColor = Colors.Green;
        meshInstance.MaterialOverride = material;
        
        AddChild(meshInstance);
        
        if (droneTransform == null)
        {
            GD.PrintErr("Drone Transform not set!");
        }
    }

    public override void _Process(double delta)
    {
        if (droneTransform == null) return;

        // グローバル座標を取得
        Vector3 currentPos = droneTransform.GlobalPosition;

        // 一定距離移動したら点を追加
        if (positions.Count == 0 || currentPos.DistanceTo(positions[positions.Count - 1]) > minDistance)
        {
            positions.Add(currentPos);
            UpdateMesh();
        }
    }

    private void UpdateMesh()
    {
        immediateMesh.ClearSurfaces();
        if (positions.Count < 2) return;

        // 線の描画開始
        immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int i = 0; i < positions.Count - 1; i++)
        {
            // 各点を現在のノードのローカル座標に変換して追加
            immediateMesh.SurfaceAddVertex(ToLocal(positions[i]));
            immediateMesh.SurfaceAddVertex(ToLocal(positions[i+1]));
        }
        immediateMesh.SurfaceEnd();
    }
}