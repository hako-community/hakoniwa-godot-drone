using Godot;
using System.Collections.Generic;

namespace hakoniwa.objects.core
{
    public partial class Magnet : Node3D
    {
        [Export]
        public bool on; // MagnetのOn/Off状態
        [Export]
        public bool forceOn = false;
        private Color originalColor = Colors.Gray; // 元の色を保存
        
        [Export]
        public MeshInstance3D magnetRenderer; // Replaced GeometryInstance3D with MeshInstance3D
        
        [Export]
        public float detectionRange = 0.5f; 
        private Baggage currentBaggage; 

        public bool IsMagnetOn() => on;
        public bool IsConntactOn() => currentBaggage != null;

        public override void _Ready()
        {
            on = false; 
            if (magnetRenderer == null)
            {
                // Try to find a MeshInstance3D on this node or children
//                magnetRenderer = FindComponent<MeshInstance3D>(this);
                magnetRenderer = FindNodeByInterface<MeshInstance3D>(this);
            }

            if (magnetRenderer == null)
            {
                GD.PrintErr("Can not found MeshInstance3D on " + this.Name);
            }
            else
            {
                // Get the material currently being displayed
                var mat = magnetRenderer.GetActiveMaterial(0) as StandardMaterial3D;
                if (mat != null)
                {
                    originalColor = mat.AlbedoColor;
                }
                else
                {
                    // Create a unique material if not present
                    var newMat = new StandardMaterial3D();
                    newMat.AlbedoColor = originalColor;
                    magnetRenderer.MaterialOverride = newMat;
                }
            }
        }

        private T FindComponent<T>(Node node) where T : class
        {
            if (node is T found) return found;
            foreach (Node child in node.GetChildren())
            {
                var result = FindComponent<T>(child);
                if (result != null) return result;
            }
            return null;
        }


        public T FindNodeByInterface<T>(Node root) where T : class
        {
            if (root is T found) return found;

            foreach (Node child in root.GetChildren())
            {
                var result = FindNodeByInterface<T>(child);
                if (result != null) return result;
            }
            return null;
        }
        
        public override void _PhysicsProcess(double delta)
        {
            if (on && currentBaggage == null)
            {
                FindAndGrabNearestBaggage();
            }
            else if (!on && currentBaggage != null)
            {
                ReleaseBaggage();
            }
            UpdateColor();
        }

        public void TurnOn()
        {
            on = true;
            forceOn = false;
        }
        public void TurnOnForce()
        {
            on = true;
            forceOn = true;
        }
        public bool TurnOn(Baggage baggage)
        {
            if (currentBaggage != null)
            {
                return false;
            }
            currentBaggage = baggage;
            currentBaggage.Grab(this);
            on = true;
            return true;
        }

        public void TurnOff()
        {
            on = false;
            forceOn = false;
        }

        public bool IsConntact()
        {
            return on && (currentBaggage != null);
        }

        public Baggage FindNearestBaggage()
        {
            Baggage nearestBaggage = null;
            float nearestDistance = detectionRange;

            // Find baggages in the scene tree
            List<Baggage> baggages = FindAllBaggages(GetTree().Root);

            foreach (Baggage baggage in baggages)
            {
                bool isFree = baggage.IsFree();
                if (forceOn)
                {
                    isFree = true;
                }
                
                // Position check
                if (isFree && baggage.GlobalPosition.Y < this.GlobalPosition.Y)
                {
                    float distance = GlobalPosition.DistanceTo(baggage.GlobalPosition);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestBaggage = baggage;
                    }
                }
            }

            return nearestBaggage;
        }

        private List<Baggage> FindAllBaggages(Node node)
        {
            List<Baggage> results = new List<Baggage>();
            if (node is Baggage b) results.Add(b);
            foreach (Node child in node.GetChildren())
            {
                results.AddRange(FindAllBaggages(child));
            }
            return results;
        }

        private void FindAndGrabNearestBaggage()
        {
            Baggage nearestBaggage = FindNearestBaggage();
            if (nearestBaggage != null)
            {
                currentBaggage = nearestBaggage;
                currentBaggage.Grab(this);
            }
        }

        private void ReleaseBaggage()
        {
            if (currentBaggage != null)
            {
                currentBaggage.Release();
                currentBaggage = null;
            }
        }

        private void UpdateColor()
        {
            if (magnetRenderer != null)
            {
                // Prefer MaterialOverride for runtime changes
                var mat = magnetRenderer.MaterialOverride as StandardMaterial3D;
                if (mat == null)
                {
                    // Fallback to active material if override is not set
                    var activeMat = magnetRenderer.GetActiveMaterial(0);
                    if (activeMat != null)
                    {
                        // Duplicate to avoid affecting other objects using the same material
                        mat = (StandardMaterial3D)activeMat.Duplicate();
                        magnetRenderer.MaterialOverride = mat;
                    }
                }

                if (mat != null)
                {
                    mat.AlbedoColor = on ? Colors.Red : originalColor;
                }
            }
        }
    }
}
