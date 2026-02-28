using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using hakoniwa.environment.interfaces;

namespace hakoniwa.environment.impl.local
{
    public class UDPCommunicationService : ICommunicationService
    {
        private readonly int localPort;
        private readonly string remoteAddress;
        private readonly int remotePort;
        private UdpClient udpClient;
        private CancellationTokenSource cancellationTokenSource;
        private Task receiveTask;
        private ICommunicationBuffer buffer;
        private bool isServiceEnabled = false;
        public string GetServerUri()
        {
            return null;
        }

        public UDPCommunicationService(int localPort, string remoteAddress, int remotePort)
        {
            this.localPort = localPort;
            this.remoteAddress = remoteAddress;
            this.remotePort = remotePort;
        }
        public Task<bool> StartService(ICommunicationBuffer comm_buffer, string uri = null)
        {
            if (isServiceEnabled)
            {
                return Task.FromResult(false); ;
            }
            buffer = comm_buffer;
            udpClient = new UdpClient(localPort);
            cancellationTokenSource = new CancellationTokenSource();
            receiveTask = Task.Run(() => ReceiveDataLoop(cancellationTokenSource.Token));
            isServiceEnabled = true;
            return Task.FromResult(true); ;
        }

        public async Task<bool> StopService()
        {
            if (!isServiceEnabled)
            {
                return false;
            }

            try
            {
                // キャンセルを発行して受信タスクを終了させる
                cancellationTokenSource?.Cancel();

                // ソケットを閉じてReceiveAsyncを強制終了させる
                udpClient?.Client.Close();

                // 受信タスクが終了するのを待つ
                if (receiveTask != null)
                {
                    await receiveTask;
                }
            }
            catch (AggregateException e)
            {
                Console.WriteLine($"Exception in StopService: {e.Message}");
            }
            finally
            {
                udpClient?.Dispose();
                cancellationTokenSource?.Dispose();
                isServiceEnabled = false;
            }

            return true;
        }

        public async Task<bool> SendData(string robotName, int channelId, byte[] pdu_data)
        {
            if (!isServiceEnabled)
            {
                return false;
            }

            try
            {
                IDataPacket packet = new DataPacket()
                {
                    RobotName = robotName,
                    ChannelId = channelId,
                    BodyData = pdu_data
                };
                var endPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
                var data = packet.Encode();
                await udpClient.SendAsync(data, data.Length, endPoint);
                return true;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Failed to send data: {ex.Message}");
                return false;
            }
        }

        private async Task ReceiveDataLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await udpClient.ReceiveAsync();
                    var receivedData = result.Buffer;
                    // 受信データの処理
                    //Console.WriteLine($"Received Data: {BitConverter.ToString(receivedData)}");

                    IDataPacket packet = DataPacket.Decode(receivedData);
                    buffer.PutPacket(packet);

                }
                catch (SocketException e)
                {
                    Console.WriteLine($"Socket Exception: {e.Message}");
                }
            }
        }

        public bool IsServiceEnabled()
        {
            return isServiceEnabled;
        }
    }
}
