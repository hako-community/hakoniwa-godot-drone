#if !NO_USE_UNITY
using System.Collections;
using System.Collections.Generic;
using Godot;

namespace hakoniwa.pdu
{
    public class Frame
    {
        public static Godot.Vector3 toUnity(msgs.geometry_msgs.Vector3 src)
        {
            return new Godot.Vector3(
                (float)src.x,
                (float)src.y,
                (float)src.z
                );
        }
        public static void toPdu(Godot.Vector3 src, msgs.geometry_msgs.Vector3 dst)
        {
            dst.x = (double)src.x;
            dst.y = (double)src.y;
            dst.z = (double)src.z;
        }
        //pdu(ros) frame to unity frame
        public static Godot.Vector3 toUnityPosFromPdu(Godot.Vector3 src)
        {
            return new Godot.Vector3(
                -src.y,
                src.z,
                src.x
                );
        }
        public static Godot.Vector3 toUnityPosFromPdu(msgs.geometry_msgs.Vector3 src)
        {
            return toUnityPosFromPdu(toUnity(src));
        }
        public static Godot.Vector3 toUnityAngleFromPdu(Godot.Vector3 src)
        {
            return new Godot.Vector3(
                src.y,
                -src.z,
                -src.x
                );
        }
        public static Godot.Vector3 toUnityAngleFromPdu(msgs.geometry_msgs.Vector3 src)
        {
            return toUnityAngleFromPdu(toUnity(src));
        }
        //unity frame to pdu(ros) frame
        public static void toPduPosFromUnity(Godot.Vector3 src, msgs.geometry_msgs.Vector3 dst)
        {
            dst.x = (double)src.z;
            dst.y = (double)-src.x;
            dst.z = (double)src.y;
        }
        public static void toPduAngleFromUnity(Godot.Vector3 src, msgs.geometry_msgs.Vector3 dst)
        {
            dst.x = (double)-src.z;
            dst.y = (double)src.x;
            dst.z = (double)-src.y;
        }
    }
}
#endif
