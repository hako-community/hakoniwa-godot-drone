using System;
using System.Collections.Generic;
using hakoniwa.environment.interfaces;

namespace hakoniwa.pdu.core
{
    public class PduBuffer: ICommunicationBuffer
    {
        private readonly Dictionary<string, byte[]> pduBuffer;
        private readonly object lockObj = new object();
        private PduChannelConfig channel_config;

        private const char Separator = '\u001F';

        public string GetKey(string robotName, string pduName)
        {
            return robotName + Separator + pduName;
        }

        public void Key2RobotPdu(string key, out string robotName, out string pduName)
        {
            string[] tokens = key.Split(Separator);
            if (tokens.Length != 2)
            {
                throw new ArgumentException($"Invalid key format: {key}");
            }
            robotName = tokens[0];
            pduName = tokens[1];
        }


        public PduBuffer(PduChannelConfig pdu_channel_config)
        {
            pduBuffer = new Dictionary<string, byte[]>();
            channel_config = pdu_channel_config;
        }

        public void SetBuffer(string key, byte[] data)
        {
            lock (lockObj)
            {
                pduBuffer[key] = data;
            }
        }

        public byte[] GetBuffer(string key)
        {
            lock (lockObj)
            {
                if (pduBuffer.TryGetValue(key, out var data))
                {
                    pduBuffer.Remove(key); // キーとデータを削除
                    return data;
                }
                return null;
            }
        }

        public bool ContainsBuffer(string key)
        {
            lock (lockObj)
            {
                return pduBuffer.ContainsKey(key);
            }
        }

        public void Clear()
        {
            lock (lockObj)
            {
                pduBuffer.Clear();
            }
        }

        public void PutPacket(IDataPacket packet)
        {
            string pduName = this.channel_config.GetPduName(packet.GetRobotName(), packet.GetChannelId());
            if (pduName != null)
            {
                //string key = packet.GetRobotName() + "_" + pduName;
                string key = GetKey(packet.GetRobotName(), pduName);
                //Console.WriteLine($"put packet: {key}");
                this.SetBuffer(key, packet.GetPduData());
            }
        }
        public void PutPacket(string robotName, int channelId, byte[] pdu_data)
        {
            string pduName = this.channel_config.GetPduName(robotName, channelId);
            if (pduName != null)
            {
                //string key = robotName + "_" + pduName;
                string key = GetKey(robotName, pduName);
                //Console.WriteLine($"put packet: {key}");
                this.SetBuffer(key, pdu_data);
            }
        }
    }
}
