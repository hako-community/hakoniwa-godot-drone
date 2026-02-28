using Godot;

namespace hakoniwa.objects.core
{
    public interface IBaggageGrabber
    {
        public void Grab(bool forceOn);
        public void Release();
        public bool IsGrabbed();
    }
}
