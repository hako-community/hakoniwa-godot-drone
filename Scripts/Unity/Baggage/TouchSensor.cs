using Godot;

namespace hakoniwa.objects.core
{
    public partial class TouchSensor : Node
    {
        public bool isTouched = false;
        public Node3D holder;
        public bool IsTouched()
        {
            return isTouched;
        }
        void Start()
        {
            //nothing to do
        }
        void OnTriggerEnter(CollisionObject3D t)
        {
            this.isTouched = true;
            var parent = t.GetParent();
            if (parent != null) {
                GD.Print("ENTER:" + parent.Name);
//                Baggage baggage = FindComponent<Baggage>(parent);
                Baggage baggage = FindNodeByInterface<Baggage>(parent);
                if (baggage != null)
                {
                    baggage.Grab(holder);
                }
                GD.Print("Baggage: " + baggage);
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

        void OnTriggerStay(CollisionObject3D t)
        {
            this.isTouched = true;
            //GD.Print("STAY:" + t.gameObject.name);
        }

        private void OnTriggerExit(CollisionObject3D t)
        {
            this.isTouched = false;
            GD.Print("EXIT:" + t.Name);
        }

    }
}
