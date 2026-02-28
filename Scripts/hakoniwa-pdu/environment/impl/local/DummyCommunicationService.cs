using System;
using System.Threading.Tasks;
using hakoniwa.environment.interfaces;

namespace hakoniwa.environment.impl.local
{
    public class DummyCommunicationService: ICommunicationService
    {
        private bool isServiceEnabled = false;
        public DummyCommunicationService()
        {
        }
        public string GetServerUri()
        {
            return null;
        }

        public bool IsServiceEnabled()
        {
            return isServiceEnabled;
        }

        public Task<bool> SendData(string robotName, int channelId, byte[] pdu_data)
        {
            if (!isServiceEnabled)
            {
                return Task.FromResult(false);  // 非同期に false を返す
            }
            
            // 本来の送信処理を行う場合は、ここで非同期処理を実行し、結果を返す必要があります。
            // ここでは簡単に true を返す
            return Task.FromResult(true);  // 非同期に true を返す
        }


        public Task<bool> StartService(ICommunicationBuffer comm_buffer, string uri = null)
        {
            if (isServiceEnabled)
            {
                return Task.FromResult(false);
            }
            isServiceEnabled = true;
            return Task.FromResult(true); ;
        }

        public Task<bool> StopService()
        {
            if (isServiceEnabled)
            {
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }
}
