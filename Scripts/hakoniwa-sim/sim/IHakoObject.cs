using System.Collections;
using System.Collections.Generic;

namespace hakoniwa.sim
{
    public interface IHakoObject
    {
        void EventInitialize();
        void EventStart();
        void EventStop();
        void EventReset();
        void EventTick();
    }
}
