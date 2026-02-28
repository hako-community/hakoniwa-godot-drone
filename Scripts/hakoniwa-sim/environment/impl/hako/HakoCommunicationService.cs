using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using hakoniwa.environment.interfaces;
using hakoniwa.sim.core.impl;

namespace hakoniwa.environment.impl.hako
{
    internal class PduInformation
    {
        public string robotName;
        public string pduName;
        public int channelId;
        public int pduSize;
        public IntPtr buffer;
        public byte[] managedBuffer;
        public PduInformation(string robo, string name, int channel_id, int pdu_size)
        {
            robotName = robo;
            pduName = name;
            pduSize = pdu_size;
//            GD.Print("pduSize: " + pdu_size);
            channelId = channel_id;
            managedBuffer = new byte[pdu_size];
            buffer = Marshal.AllocHGlobal(pdu_size);
            if (buffer == IntPtr.Zero)
            {
                throw new Exception($"Failed to allocate unmanaged memory: robotName='{robotName}', channelId={channelId}, pduSize={pdu_size}");
            }
        }
    }

    public class HakoCommunicationService : IHakoCommunicationService
    {
        private ICommunicationBuffer commBuffer;
        private bool isServiceEnabled = false;
        private string serverURL;
        private static Dictionary<(string roboName, int channelId), PduInformation> channel_writers = new Dictionary<(string, int), PduInformation>();
        private static Dictionary<(string roboName, int channelId), PduInformation> channel_readers = new Dictionary<(string, int), PduInformation>();
        private static Dictionary<(string roboName, string pduName), PduInformation> writers = new Dictionary<(string, string), PduInformation>();
        private static Dictionary<(string roboName, string pduName), PduInformation> readers = new Dictionary<(string, string), PduInformation>();

        public HakoCommunicationService(string assetName)
        {
            this.serverURL = assetName;
        }

        public bool DeclarePduForWrite(string robotName, string pduName, int channelId, int pduSize)
        {
            PduInformation pdu_info;
            var key = (robotName, pduName);
            if (!writers.TryGetValue(key, out pdu_info))
            {
                bool ret = HakoCppWrapper.asset_create_pdu_lchannel(robotName, channelId, (uint)pduSize);
                if (ret == false)
                {
                    throw new ArgumentException($"Can not create pdu channel!! robotName: {robotName} channel_id: {channelId}");
                }
//                GD.Print($"DeclarePduForWrite: robotName={robotName}, pduName={pduName}, channelId={channelId}, pduSize={pduSize}");
                writers[(robotName, pduName)] = new PduInformation(robotName, pduName, channelId, pduSize);
                channel_writers[(robotName, channelId)] = writers[(robotName, pduName)];
            }
            return true;
        }

        public bool DeclarePduForRead(string robotName, string pduName, int channelId, int pduSize)
        {
            PduInformation pdu_info;
            var key = (robotName, pduName);
            if (!readers.TryGetValue(key, out pdu_info))
            {
                bool ret = HakoCppWrapper.asset_create_pdu_lchannel(robotName, channelId, (uint)pduSize);
                if (ret == false)
                {
                    throw new ArgumentException($"Can not create pdu channel!! robotName: {robotName} channel_id: {channelId}");
                }
//                GD.Print($"DeclarePduForRead: robotName={robotName}, pduName={pduName}, channelId={channelId}, pduSize={pduSize}");
                readers[(robotName, pduName)] = new PduInformation(robotName, pduName, channelId, pduSize);
                channel_readers[(robotName, channelId)] = readers[(robotName, pduName)];
            }
            return true;
        }

        public string GetServerUri()
        {
            return serverURL;
        }

        public bool IsServiceEnabled()
        {
            return isServiceEnabled;
        }

        public Task<bool> SendData(string robotName, int channelId, byte[] pdu_data)
        {
            if (!isServiceEnabled)
            {
                return Task.FromResult<bool>(false);
            }
            var key = (robotName, channelId);
            PduInformation pdu_info;
            if (!channel_writers.TryGetValue(key, out pdu_info))
            {
                throw new Exception($"can not find pdu: robotName='{robotName}', channelId={channelId}, pduSize={pdu_data.Length}");
            }
            Marshal.Copy(pdu_data, 0, pdu_info.buffer, pdu_data.Length);
            bool ret = HakoCppWrapper.asset_write_pdu(serverURL, robotName, channelId, pdu_info.buffer, (uint)pdu_data.Length);
            return Task.FromResult<bool>(ret);
        }

        public Task<bool> StartService(ICommunicationBuffer comm_buffer, string uri = null)
        {
            if (comm_buffer == null)
            {
                throw new ArgumentNullException(nameof(comm_buffer), "Communication buffer cannot be null.");
            }
            commBuffer = comm_buffer;
            isServiceEnabled = true;
            return Task.FromResult(true);
        }
        public void EventTick()
        {
            if (!isServiceEnabled)
            {
                return; // サービスが有効でない場合は処理しない
            }

            foreach (var reader in channel_readers.Values)
            {
                try
                {
                    // PDUデータを共有メモリから読み込む
                    bool ret = HakoCppWrapper.asset_read_pdu(
                        serverURL,
                        reader.robotName,
                        reader.channelId,
                        reader.buffer,
                        (uint)reader.pduSize
                    );

                    if (!ret)
                    {
                        throw new ArgumentException($"Failed to read PDU data: robotName='{reader.robotName}', channelId={reader.channelId}");
                    }
                    Marshal.Copy(reader.buffer, reader.managedBuffer, 0, reader.pduSize);
                    commBuffer.PutPacket(reader.robotName, reader.channelId, reader.managedBuffer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"EventTick error: {ex.Message}");
                }
            }
        }

        public Task<bool> StopService()
        {
            try
            {
                // channel_writers のメモリ解放
                foreach (var pduInfo in channel_writers.Values)
                {
                    if (pduInfo.buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pduInfo.buffer);
                        pduInfo.buffer = IntPtr.Zero; // 解放後にポインタを無効化
                    }
                }
                channel_writers.Clear();

                // channel_readers のメモリ解放
                foreach (var pduInfo in channel_readers.Values)
                {
                    if (pduInfo.buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pduInfo.buffer);
                        pduInfo.buffer = IntPtr.Zero;
                    }
                }
                channel_readers.Clear();

                // writers のメモリ解放
                foreach (var pduInfo in writers.Values)
                {
                    if (pduInfo.buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pduInfo.buffer);
                        pduInfo.buffer = IntPtr.Zero;
                    }
                }
                writers.Clear();

                // readers のメモリ解放
                foreach (var pduInfo in readers.Values)
                {
                    if (pduInfo.buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pduInfo.buffer);
                        pduInfo.buffer = IntPtr.Zero;
                    }
                }
                readers.Clear();

                // サービス状態を無効化
                isServiceEnabled = false;

                Console.WriteLine("StopService: メモリ解放完了、サービス停止");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StopServiceエラー: {ex.Message}");
                return Task.FromResult(false);
            }
        }

    }
}
