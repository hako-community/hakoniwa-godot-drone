using System;

namespace hakoniwa.pdu.unity
{
    public class TimeStamp
    {
        public static long GetUnixTime()
        {
            var baseDt = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var unixtime = (DateTimeOffset.Now - baseDt).Ticks / 10;//usec
            return unixtime;
        }
        public static void Set(hakoniwa.pdu.msgs.std_msgs.Header header)
        {
            long t = GetUnixTime();
            int t_sec = (int)((long)(t / 1000000));
            uint t_nsec = (uint)((long)(t % 1000000)) * 1000;
            header.stamp.sec = t_sec;
            header.stamp.nanosec = t_nsec;
        }
    }
}
