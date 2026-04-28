using Godot;

namespace hakoniwa.objects.core
{
    public partial class SimpleBaggageGrabber : Node, IBaggageGrabber
    {
        public Magnet magnet;

        public void Grab(bool forceOn)
        {
            if (forceOn)
            {
                magnet.TurnOnForce();
            }
            else
            {
                magnet.TurnOn();
            }
        }

        public bool IsGrabbed()
        {
            return magnet.IsConntactOn();
        }

        public void Release()
        {
            magnet.TurnOff();
        }
    }
}
