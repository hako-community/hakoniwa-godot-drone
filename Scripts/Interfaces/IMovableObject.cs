

using Godot;

namespace hakoniwa.objects.core
{
    public interface IMovableObject
    {
        public Vector3 GetPosition();
        public Vector3 GetEulerDeg();
    }
}