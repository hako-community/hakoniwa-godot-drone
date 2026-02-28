using System;
using hakoniwa.environment.interfaces;

namespace hakoniwa.environment.interfaces
{
    public interface IHakoPduService
    {
        bool DeclarePduForWrite(string robotName, string pduName, int channelId, int pduSize);
        bool DeclarePduForRead(string robotName, string pduName, int channelId, int pduSize);
    }

    public interface IHakoCommunicationService : ICommunicationService, IHakoPduService
    {
        void EventTick();
    }
}
