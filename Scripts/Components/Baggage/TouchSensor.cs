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
                Baggage baggage = NodeUtil.FindNodeByInterface<Baggage>(parent);
                if (baggage != null)
                {
                    baggage.Grab(holder);
                }
                GD.Print("Baggage: " + baggage);
            }

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
