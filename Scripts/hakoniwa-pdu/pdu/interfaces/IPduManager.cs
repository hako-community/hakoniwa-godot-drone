using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace hakoniwa.pdu.interfaces
{
    public interface IPduManager
    {
        Task<bool> StartService(string server_uri = null);
        bool StopService();
        bool IsServiceEnabled();

        public Task<bool> DeclarePduForRead(string robotName, string pduName);
        public Task<bool> DeclarePduForWrite(string robotName, string pduName);

        IPdu CreatePdu(string robotName, string pduName);
        IPdu CreatePduByType(string pduName, string packageName, string typeName);
        string WritePdu(string robotName, IPdu pdu);
        Task<bool> FlushPdu(string key);
        Task<bool> FlushPdu(string robotName, string pduName);

        IPdu ReadPdu(string robotName, string pduName);
        int GetChannelId(string robotName, string pduName);
        int GetPduSize(string robotName, string pduName);

        INamedPdu CreateNamedPdu(string robotName, string pduName);
        string WriteNamedPdu(INamedPdu npdu);
        Task<bool> FlushNamedPdu(INamedPdu npdu);

    }
}