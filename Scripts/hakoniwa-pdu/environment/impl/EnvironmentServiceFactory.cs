using System;
using hakoniwa.environment.impl.local;
#if !NO_USE_UNITY
using hakoniwa.environment.impl.unity;
using Godot;
#endif
using hakoniwa.environment.interfaces;
#if !UNITY_WEBGL
using Newtonsoft.Json;
#endif
using System.IO;
using System.Text.RegularExpressions;

namespace hakoniwa.environment.impl
{
    [System.Serializable]
    public class UdpConfig
    {
#if UNITY_WEBGL
        public int LocalPort;
        public int RemotePort;
        public string RemoteIPAddress;
#else
        public int LocalPort { get; set; }
        public int RemotePort { get; set; }
        public string RemoteIPAddress { get; set; }
#endif
    }

    [System.Serializable]
    public class WebSocketConfig
    {
#if UNITY_WEBGL
        public string ServerURI;
#else
        public string ServerURI { get; set; }
#endif
    }

    [System.Serializable]
    public class CommServiceConfig
    {
#if UNITY_WEBGL
        public UdpConfig Udp = new UdpConfig();
        public WebSocketConfig WebSocket = new WebSocketConfig();
#else
        public UdpConfig Udp { get; set; }
        public WebSocketConfig WebSocket { get; set; }
#endif
    }
    public class ConfigParser
    {
        private readonly string jsonText;

        public ConfigParser(string jsonText)
        {
            this.jsonText = jsonText;
        }

        private string GetStringValue(string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*\"([^\"]+)\"";
            var match = Regex.Match(jsonText, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        private int GetIntValue(string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(\\d+)";
            var match = Regex.Match(jsonText, pattern);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public CommServiceConfig ParseCommServiceConfig()
        {
            var config = new CommServiceConfig
            {
                Udp = new UdpConfig
                {
                    LocalPort = GetIntValue("localPort"),
                    RemotePort = GetIntValue("remotePort"),
                    RemoteIPAddress = GetStringValue("remoteIPAddress")
                },
                WebSocket = new WebSocketConfig
                {
                    ServerURI = GetStringValue("serverURI")
                }
            };

            return config;
        }
    }

    public class EnvironmentServiceFactory
    {
        public static IEnvironmentService Create(string service_type, string file_type="local", string path=".")
        {
            return new EnvironmentService(service_type, file_type, path);
        }
    }

    public class EnvironmentService : IEnvironmentService
    {
        private IFileLoader file_loader;
        private ICommunicationService comm_service;

        public void SetCommunication(ICommunicationService comm)
        {
            this.comm_service = comm;
        }

        private CommServiceConfig LoadCommServiceConfig(string path)
        {
            string relative_path = path + "/" + "comm_service_config";
            string normalizedPath = relative_path.Replace('/', Path.DirectorySeparatorChar);
            string param = file_loader.LoadText(normalizedPath, ".json");

            try
            {
#if UNITY_WEBGL
                GD.Print("readed json: " + param);
                ConfigParser parser = new ConfigParser(param);
                CommServiceConfig config = parser.ParseCommServiceConfig();
                // WebGL用のJsonUtilityを使ってデシリアライズ
                //CommServiceConfig config = JsonUtility.FromJson<CommServiceConfig>(param);
                GD.Print("config.WebSocket.ServerURI: " + config.WebSocket.ServerURI);
#else
                // それ以外はNewtonsoft.Jsonを使用
                CommServiceConfig config = JsonConvert.DeserializeObject<CommServiceConfig>(param);
#endif
                return config;
            }
            catch (Exception e)
            {
                Console.WriteLine($"JSONのパース中にエラーが発生しました: {e.Message}");
                return null;
            }
        }

        public EnvironmentService(string service_type, string file_type, string path)
        {
            if (file_type == "local")
            {
                file_loader = new LocalFileLoader();
            }
#if !NO_USE_UNITY
            else if (file_type == "unity")
            {
                file_loader = new ResourcesFileLoader();
            }
#endif
            if (service_type == "dummy")
            {
                comm_service = new DummyCommunicationService();
            }
            else if (service_type == "udp")
            {
                var config = LoadCommServiceConfig(path);
                comm_service = new UDPCommunicationService(config.Udp.LocalPort, config.Udp.RemoteIPAddress, config.Udp.RemotePort);
            }
            else if (service_type == "websocket_dotnet")
            {
                var config = LoadCommServiceConfig(path);
                string serverUri = config.WebSocket.ServerURI;
                comm_service = new WebSocketCommunicationService(serverUri);
            }
#if UNITY_WEBGL
            else if (service_type == "websocket_unity")
            {
                var config = LoadCommServiceConfig(path);
                string serverUri = config.WebSocket.ServerURI;
                comm_service = new WebGLSocketCommunicationService(serverUri);
            }
#endif
        }

        public ICommunicationService GetCommunication()
        {
            return comm_service;
        }

        public IFileLoader GetFileLoader()
        {
            return file_loader;
        }
    }
}
