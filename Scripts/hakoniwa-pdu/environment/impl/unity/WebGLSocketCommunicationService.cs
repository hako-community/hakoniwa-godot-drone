#if UNITY_WEBGL
using System.Threading;
using System.Threading.Tasks;
using NativeWebSocket;
using hakoniwa.environment.interfaces;
using System;
using hakoniwa.environment.impl;
using Godot;

public class WebGLSocketCommunicationService : ICommunicationService, IDisposable
{
    private readonly string serverUri;
    private WebSocket webSocket;
    private ICommunicationBuffer buffer;
    private bool isServiceEnabled = false;


    public WebSocket GetWebSocket()
    {
        return webSocket;
    }
    public WebGLSocketCommunicationService(string serverUri)
    {
        this.serverUri = serverUri;
    }

    public bool IsServiceEnabled()
    {
        return isServiceEnabled;
    }

    async public Task<bool> SendData(string robotName, int channelId, byte[] pdu_data)
    {
        if (!isServiceEnabled || webSocket.State != WebSocketState.Open)
        {
            GD.Print($"send error: isServiceEnabled={isServiceEnabled} webSocket.State ={webSocket.State }");
            return false;
        }

        IDataPacket packet = new DataPacket()
        {
            RobotName = robotName,
            ChannelId = channelId,
            BodyData = pdu_data
        };

        var data = packet.Encode();

        try
        {
            await webSocket.Send(data);
            return true;
        }
        catch (WebSocketException ex)
        {
            GD.Print($"Failed to send data: {ex.Message}");
            return false;
        }
    }

    async public Task<bool> StartService(ICommunicationBuffer comm_buffer, string uri = null)
    {
        if (isServiceEnabled)
        {
            return false;
        }
        if (uri != null) {
            this.serverUri = uri;
        }
        buffer = comm_buffer;
        webSocket = new WebSocket(serverUri);

        webSocket.OnOpen += () =>
        {
            GD.Print("Connection open!");
        };

        webSocket.OnError += (e) =>
        {
            GD.Print("Error! " + e);
        };

        webSocket.OnClose += (e) =>
        {
            GD.Print("Connection closed!");
        };

        webSocket.OnMessage += (bytes) =>
        {
            //GD.Print("OnMessage event triggered!");

            try
            {
                // 4バイトのヘッダーを読み取り、メッセージ全体の長さを取得
                if (bytes.Length < 4)
                {
                    GD.PushWarning("Header is incomplete.");
                    return;
                }

                int totalLength = BitConverter.ToInt32(bytes, 0);
                //GD.Print("Total Length: " + totalLength);

                // データの長さが一致する場合のみ処理を続ける
                if (bytes.Length == 4 + totalLength)
                {
                    //DumpDataBuffer(completeData);

                    IDataPacket packet = DataPacket.Decode(bytes);
                    buffer.PutPacket(packet);
                    //GD.Print("Data received and processed.");
                }
                else
                {
                    GD.PushWarning("Received data length mismatch.");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Receive error: {ex.Message}");
            }
        };


        try
        {
            GD.Print("Start Connect");
            await webSocket.Connect();
        }
        catch (WebSocketException ex)
        {
            GD.Print($"WebSocket connection error: {ex.Message}");
            isServiceEnabled = false;
            return false;
        }
        catch (Exception ex)
        {
            GD.Print($"Unexpected connection error: {ex.Message}");
            isServiceEnabled = false;
            return false;
        }

        isServiceEnabled = true;
        return true;
    }


    private void DumpDataBuffer(byte[] dataBuffer)
    {
        GD.Print("Data Buffer Dump:");
        for (int i = 0; i < dataBuffer.Length; i++)
        {
            GD.Print($"Byte {i}: {dataBuffer[i]:X2}");
        }
    }

    public async Task<bool> StopService()
    {
        GD.Print("Stop Service");
        if (!isServiceEnabled)
        {
            return false;
        }

        isServiceEnabled = false;

        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            webSocket.Close();
        }

        return true;
    }
    public void Dispose()
    {
        DisposeAsync().Wait();
    }

    private async Task DisposeAsync()
    {
        await StopService();
        webSocket = null;
    }
    public string GetServerUri()
    {
        return serverUri;
    }
}
#endif
