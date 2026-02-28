#if !UNITY_WEBGL
using Newtonsoft.Json;
#else
using Godot;
#endif
using System.Collections.Generic;
using hakoniwa.environment.interfaces;

namespace hakoniwa.pdu.core
{
    public class PduChannelConfig
    {
#if UNITY_WEBGL
        public List<RobotConfig> robots = new List<RobotConfig>();
#else
        [Newtonsoft.Json.JsonConstructor]
        public PduChannelConfig(List<RobotConfig> robots)
        {
            this.robots = robots ?? new List<RobotConfig>();
        }
        
        public List<RobotConfig> robots { get; set; } = new List<RobotConfig>();
#endif

        public string GetPduType(string robotName, string pduName)
        {
            foreach (var robot in robots)
            {
                if (robot.name == robotName)
                {
                    foreach (var reader in robot.shm_pdu_readers)
                    {
                        if (reader.org_name == pduName)
                        {
                            return reader.type;
                        }
                    }
                    foreach (var writer in robot.shm_pdu_writers)
                    {
                        if (writer.org_name == pduName)
                        {
                            return writer.type;
                        }
                    }
                }
            }
            return null;
        }

        public string GetPduName(string robotName, int channelId)
        {
            foreach (var robot in robots)
            {
                if (robot.name == robotName)
                {
                    foreach (var reader in robot.shm_pdu_readers)
                    {
                        if (reader.channel_id == channelId)
                        {
                            return reader.org_name;
                        }
                    }
                    foreach (var writer in robot.shm_pdu_writers)
                    {
                        if (writer.channel_id == channelId)
                        {
                            return writer.org_name;
                        }
                    }
                }
            }
            return null;
        }

        public int GetChannelId(string robotName, string pduName)
        {
            foreach (var robot in robots)
            {
                if (robot.name == robotName)
                {
                    foreach (var reader in robot.shm_pdu_readers)
                    {
                        if (reader.org_name == pduName)
                        {
                            return reader.channel_id;
                        }
                    }
                    foreach (var writer in robot.shm_pdu_writers)
                    {
                        if (writer.org_name == pduName)
                        {
                            return writer.channel_id;
                        }
                    }
                }
            }
            return -1;
        }
        public int GetPduSize(string robotName, string pduName)
        {
            foreach (var robot in robots)
            {
                if (robot.name == robotName)
                {
                    foreach (var reader in robot.shm_pdu_readers)
                    {
                        if (reader.org_name == pduName)
                        {
                            return reader.pdu_size;
                        }
                    }
                    foreach (var writer in robot.shm_pdu_writers)
                    {
                        if (writer.org_name == pduName)
                        {
                            return writer.pdu_size;
                        }
                    }
                }
            }
            return -1;
        }        
    }
    [System.Serializable]
    public class RobotConfig
    {
        public string name;
        public List<PduChannel> rpc_pdu_readers = new List<PduChannel>();
        public List<PduChannel> rpc_pdu_writers = new List<PduChannel>();
        public List<PduChannel> shm_pdu_readers = new List<PduChannel>();
        public List<PduChannel> shm_pdu_writers = new List<PduChannel>();
    }

    [System.Serializable]
    public class PduChannel
    {
        public string type;
        public string org_name;
        public string name;
        public string class_name;
        public string conv_class_name;
        public int channel_id;
        public int pdu_size;
        public int write_cycle;
        public string method_type;
    }

    public class PduChannelLoader
    {
        private IFileLoader fileLoader;

        public PduChannelLoader(IFileLoader loader)
        {
            fileLoader = loader;
        }

        public PduChannelConfig Load(string filePath, string extension)
        {
            string jsonContent = fileLoader.LoadText(filePath, extension);

#if UNITY_WEBGL
            // WebGL用のJsonUtilityでデシリアライズ
            PduChannelConfig config = JsonUtility.FromJson<PduChannelConfig>(jsonContent);
#else
            // 現行コード：Newtonsoft.Jsonを使用
            PduChannelConfig config = Newtonsoft.Json.JsonConvert.DeserializeObject<PduChannelConfig>(jsonContent);
#endif
            return config;
        }
    }
}
