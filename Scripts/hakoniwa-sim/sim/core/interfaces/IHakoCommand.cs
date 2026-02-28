using System;
using System.Collections.Generic;

namespace hakoniwa.sim.core
{
    public enum SimulationState
    {
        Stopped = 0,
        Runnable = 1,
        Running = 2,
        Stopping = 3,
        Terminated = 99,
    }
    public interface IHakoCommand
    {
        long GetWorldTime();
        SimulationState GetState();
        bool SimulationStart();
        bool SimulationStop();
        bool SimulationReset();
    }
}
