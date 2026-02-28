using System;
using System.Runtime.InteropServices;
//using Godot;
using Godot;

namespace hakoniwa.sim.core.impl
{
    public static class HakoConductor
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const string DllName = "conductor"; // Windows用DLL名
#else
        private const string DllName = "libconductor"; // Ubuntu/Mac用DLL名
#endif

        /*
         * Start the Conductor
         */
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hako_conductor_start(ulong delta_usec, ulong max_delay_usec);

        public static bool Start(ulong deltaUsec, ulong maxDelayUsec)
        {
            try
            {
                int ret = hako_conductor_start(deltaUsec, maxDelayUsec);
                if (ret != 0) //true
                {
                    return true;//success
                }
                else //false
                {
                    return false;
                }
            }
            catch (DllNotFoundException e)
            {
                GD.PrintErr($"DllNotFoundException: {e.Message}");
                return false;
            }
            catch (EntryPointNotFoundException e)
            {
                GD.PrintErr($"EntryPointNotFoundException: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                GD.PrintErr($"Exception: {e.Message}");
                return false;
            }
        }

        /*
         * Stop the Conductor
         */
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void hako_conductor_stop();

        public static void Stop()
        {
            try
            {
                hako_conductor_stop();
            }
            catch (DllNotFoundException e)
            {
                GD.PrintErr($"DllNotFoundException: {e.Message}");
            }
            catch (EntryPointNotFoundException e)
            {
                GD.PrintErr($"EntryPointNotFoundException: {e.Message}");
            }
            catch (Exception e)
            {
                GD.PrintErr($"Exception: {e.Message}");
            }
        }
    }
}
