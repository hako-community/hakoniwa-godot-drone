using hakoniwa.environment.impl;
using hakoniwa.environment.interfaces;
using hakoniwa.pdu;
using hakoniwa.pdu.core;
using hakoniwa.pdu.interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

public interface IHakoniwaWebObject
{
    Task DeclarePduAsync();
}

public partial class WebServerBridge : Node, IHakoPduInstance
{
    [System.Serializable]
    private class ServerUriConfig
    {
        public string uri = "";
    }
    public static WebServerBridge Instance { get; private set; }
    
    [Export]
    public Node[] hako_objects;
    private IEnvironmentService service;
    [Export]
    private string serverUri = "ws://localhost:8765";
    [Export]
    public string serverUriConfigPath = "./server-uri.json";
    [Export]
    private string pduConfigPath = ".";
    [Export]
    private string customJsonFilePath = "./custom.json";
    [Export]
    private string filesystem_type = "godot";

    public override void _EnterTree()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private IPduManager mgr = null;

    public IPduManager Get()
    {
        if (mgr == null)
        {
            return null;
        }
        if (mgr.IsServiceEnabled() == false)
        {
            GD.Print("SERVER IS NOT ENABLED");
            return null;
        }
        return mgr;
    }

    public override async void _Ready()
    {
        service = EnvironmentServiceFactory.Create("websocket_dotnet", filesystem_type, ".");
        if (service == null)
        {
            throw new System.Exception("Can not create service...");
        }
        
        if (System.IO.File.Exists(serverUriConfigPath))
        {
            try
            {
                string json = System.IO.File.ReadAllText(serverUriConfigPath);
                // Simple JSON parsing if Newtonsoft.Json is not used, 
                // but project has Newtonsoft.Json reference in .csproj.
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerUriConfig>(json);
                if (config != null && !string.IsNullOrEmpty(config.uri))
                {
                    serverUri = config.uri;
                    GD.Print("Loaded server URI from config: " + serverUri);
                }
            }
            catch (System.Exception e)
            {
                GD.PushWarning("Failed to read server-uri.json: " + e.Message);
            }
        }

        mgr = new PduManager(service, pduConfigPath, customJsonFilePath);
        GD.Print("Start Service!! " + serverUri);
        var result = await mgr.StartService(serverUri);
        GD.Print("Start Service!! " + serverUri + " ret: " + result);
        
        if (hako_objects != null)
        {
            foreach (var entry in hako_objects)
            {
                IHakoniwaWebObject obj = FindIHakoniwaWebObject(entry);
                if (obj == null)
                {
                    throw new System.Exception("Can not find IHakoniwaWebObject on " + entry.Name);
                }
                await obj.DeclarePduAsync();
            }
        }
    }

    private IHakoniwaWebObject FindIHakoniwaWebObject(Node node)
    {
        if (node is IHakoniwaWebObject obj) return obj;
        foreach (Node child in node.GetChildren())
        {
            var found = FindIHakoniwaWebObject(child);
            if (found != null) return found;
        }
        return null;
    }
}
