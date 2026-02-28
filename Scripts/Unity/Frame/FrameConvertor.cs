using Godot;

namespace hakoniwa.objects.core.frame
{
    public class FrameConvertor
    {
        public static Vector3 PosRos2Unity(Vector3 rosVec)
        {
            return new Vector3(
                -rosVec.Y,
                rosVec.Z,
                rosVec.X);
        }
        public static Vector3 EulerRosRad2UnityDeg(Vector3 rosVec)
        {
            return new Vector3(
                Mathf.RadToDeg(rosVec.Y),
                Mathf.RadToDeg(-rosVec.Z),
                Mathf.RadToDeg(-rosVec.X));
        }
        public static Vector3 EulerRosDeg2UnityDeg(Vector3 rosVec)
        {
            return new Vector3(
                rosVec.Y,
                -rosVec.Z,
                -rosVec.X);
        }
        public static Vector3 PosUnity2Ros(Vector3 unityVec)
        {
            return new Vector3(
                unityVec.Z,   
                -unityVec.X,  
                unityVec.Y    
            );
        }
        public static Vector3 EulerUnityDeg2RosRad(Vector3 unityVec)
        {
            return new Vector3(
                Mathf.DegToRad(-unityVec.Z),
                Mathf.DegToRad(unityVec.X), 
                Mathf.DegToRad(-unityVec.Y)
            );
        }

    }
}