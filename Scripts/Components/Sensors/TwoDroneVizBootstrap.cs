using System.Collections.Generic;
using Godot;
using hakoniwa.sim.core;
using hakoniwa.drone.sim;

namespace hakoniwa.objects.core.sensors
{
    // 2-drone visualization bootstrap (§18 Stage B display).
    //
    // On HAKO_TWO_DRONE=1 this clones the primary avatar into a second,
    // display-only drone (default robot "Drone1") so a single Godot asset shows
    // both drones and their radar. The clone is a FULL avatar (Node.Duplicate),
    // but its SensorVizRig runs as secondary (IsPrimary=false): it draws its own
    // cone / points / clusters and never touches the shared camera or HUD.
    //
    // Placement: put this node in the scene BEFORE the Hakoniwa (HakoAsset) node.
    // _Ready runs bottom-up in sibling (tree) order, so this node's _Ready fires
    // before Hakoniwa's -> the clone is registered before HakoAsset._Ready
    // initializes the hako objects. By _Ready every _EnterTree has already run,
    // so HakoAsset.Instance is set and add_child is allowed (the tree is no
    // longer "busy setting up children", which forbids add_child in _EnterTree).
    public partial class TwoDroneVizBootstrap : Node
    {
        [Export] public NodePath PrimaryAvatarPath;      // e.g. ../DRAvatar2
        [Export] public string SecondRobotName = "Drone1";

        public override void _Ready()
        {
            if (OS.GetEnvironment("HAKO_TWO_DRONE") != "1")
            {
                return;   // single-drone default: no-op
            }

            var primary = PrimaryAvatarPath != null ? GetNodeOrNull<DroneAvatar>(PrimaryAvatarPath) : null;
            if (primary == null)
            {
                GD.PrintErr($"[TwoDrone] primary avatar not found at '{PrimaryAvatarPath}'");
                return;
            }
            var asset = HakoAsset.Instance;
            if (asset == null)
            {
                GD.PrintErr("[TwoDrone] HakoAsset.Instance is null -- place this node AFTER Hakoniwa");
                return;
            }

            var clone = (DroneAvatar)primary.Duplicate();
            clone.Name = "DRAvatar2_1";
            clone.robotName = SecondRobotName;

            // Secondary rig(s): draw only. No camera / HUD / input ownership.
            foreach (var rig in FindAll<SensorVizRig>(clone))
            {
                rig.IsPrimary = false;
            }

            // Add under THIS node, not the scene root: during _Ready propagation
            // the root is "busy setting up children" and refuses add_child, but
            // this bootstrap node is not blocked. The clone is a Node3D that sets
            // its own world transform from its pos PDU, so a plain-Node parent is
            // fine (identical to DRAvatar2 sitting under the identity root).
            AddChild(clone);
            asset.RegisterExtraHakoObject(clone);
            GD.Print($"[TwoDrone] cloned '{primary.Name}' -> '{clone.Name}' robot={clone.robotName}");
        }

        private static List<T> FindAll<T>(Node root) where T : class
        {
            var outList = new List<T>();
            Walk(root, outList);
            return outList;
        }

        private static void Walk<T>(Node node, List<T> outList) where T : class
        {
            if (node is T t) outList.Add(t);
            foreach (Node c in node.GetChildren()) Walk(c, outList);
        }
    }
}
