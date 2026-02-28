using System;
using System.Threading.Tasks;

namespace hakoniwa.environment.interfaces
{
    public interface ICommunicationBuffer
    {
        void PutPacket(IDataPacket packet);
        void PutPacket(string robotName, int channelId, byte[] pdu_data);
        string GetKey(string robotName, string pduName);
        void Key2RobotPdu(string key, out string robotName, out string pduName);
    }
    public interface ICommunicationService
    {
        Task<bool> StartService(ICommunicationBuffer comm_buffer, string uri = null);
        Task<bool> StopService();
        bool IsServiceEnabled();
        Task<bool> SendData(string robotName, int channelId, byte[] pdu_data);

        string GetServerUri();
    }
}
