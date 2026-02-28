using System;
using System.Collections.Generic;
using hakoniwa.pdu.interfaces;
using hakoniwa.sim.core.impl;
using Godot;

namespace hakoniwa.sim.core;

public partial class HakoAsset: Node, IHakoPdu, IHakoControl
{
    public static HakoAsset Instance { get; private set; }

    [Export]
    private string assetName = "GodotAsset";
//    private string assetName = "UnityƒAsset";
    [Export]
    private string pduConfigPath = ".";
    [Export]
    private string customJsonFilePath = "./custom.json";

    [Export]
    private Node[] hakoObjects;

    public SimulationState _state;
    public long _worldTime;
    public SimulationState State => _state;
    public long WorldTime => _worldTime;

    private bool isReady = false;
    
    private IHakoAsset hakoAsset;
    private IHakoCommand hakoCommand;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public static IHakoPdu GetHakoPdu()
    {
        if (Instance == null)
        {
            GD.PrintErr("HakoAsset Instance is null");
        }
        return Instance;
    }
    public static IHakoControl GetHakoControl()
    {
        if (Instance == null)
        {
            GD.PrintErr("HakoAsset Instance is null");
        }
        return Instance;
    }

    private bool HakoAssetIsValid(List<IHakoObject> hakoObectList)
    {
        if (hakoObjects == null) return false;
        foreach (var obj in hakoObjects)
        {
//            IHakoObject ihako = FindComponent<IHakoObject>(obj);
            IHakoObject ihako = FindNodeByInterface<IHakoObject>(obj);
            if (ihako == null)
            {
                throw new ArgumentException("Can not find IHakoObject on " + obj.Name);
            }
            hakoObectList.Add(ihako);
        }
        return hakoObectList.Count > 0;
    }

    private T FindComponent<T>(Node root) where T : class
    {
        if (root is T t) return t;
        foreach (Node child in root.GetChildren())
        {
            var res = FindComponent<T>(child);
            if (res != null) return res;
        }
        return null;
    }


    public T FindNodeByInterface<T>(Node root) where T : class
    {
        if (root is T found) return found;

        foreach (Node child in root.GetChildren())
        {
            var result = FindNodeByInterface<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    public override async void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        var hakoObectList = new List<IHakoObject>();
        if (!HakoAssetIsValid(hakoObectList))
        {
            GD.PrintErr("Invalid HakoAssets");
            return;
        }

        long delta_time = 1000; // 1ms
        hakoAsset = new HakoAssetImpl(assetName, delta_time, pduConfigPath, customJsonFilePath);
        hakoCommand = (IHakoCommand)hakoAsset;
        
        if (hakoAsset.Initialize(hakoObectList))
        {
            GD.Print("OK: Initialize Hakoniwa");
            bool ret = await hakoAsset.RegisterOnHakoniwa();
            if (ret)
            {
                GD.Print("OK: Register on Hakoniwa: " + assetName);
                isReady = true;
            }
            else
            {
                GD.PrintErr("Can not register on Hakoniwa: " + assetName);
            }
            foreach (var ihako in hakoObectList)
            {
                ihako.EventInitialize();
            }
        }
        else
        {
            GD.PrintErr("Can not Initialize Hakoniwa: " + assetName);
        }
    }

    private double accumulatedDelta = 0;
    public override void _PhysicsProcess(double delta)
    {
        if (isReady == false) return;

        double step = 0.001; // 1ms
        accumulatedDelta += delta;

        while (accumulatedDelta >= step)
        {
            _state = hakoCommand.GetState();
            _worldTime = hakoCommand.GetWorldTime();

            if (hakoAsset.Execute())
            {
                // Processed one simulation step (1ms)
            }
            accumulatedDelta -= step;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            Shutdown();
        }
    }

    private async void Shutdown()
    {
        GD.Print("Shutdown");
        if (hakoAsset != null && isReady)
        {
            bool ret = await hakoAsset.UnRegisterOnHakoniwa();
            isReady = false;
            GD.Print($"OK: Unregister from Hakoniwa: {assetName} ret: {ret}");
        }
    }

    public IPduManager GetPduManager()
    {
        return hakoAsset.GetPduManager();
    }

    public bool DeclarePduForWrite(string robotName, string pduName)
    {
        var srv = hakoAsset.GetHakoCommunicationService();
        if (srv == null) return false;
        int channel_id = GetPduManager().GetChannelId(robotName, pduName);
        int pdu_size = GetPduManager().GetPduSize(robotName, pduName);
        return srv.DeclarePduForWrite(robotName, pduName, channel_id, pdu_size);
    }

    public bool DeclarePduForRead(string robotName, string pduName)
    {
        var srv = hakoAsset.GetHakoCommunicationService();
        if (srv == null) return false;
        int channel_id = GetPduManager().GetChannelId(robotName, pduName);
        int pdu_size = GetPduManager().GetPduSize(robotName, pduName);
        return srv.DeclarePduForRead(robotName, pduName, channel_id, pdu_size);
    }

    public long GetWorldTime() => hakoCommand.GetWorldTime();

    public HakoSimState GetState()
    {
        SimulationState state = hakoCommand.GetState();
        switch (state)
        {
            case SimulationState.Stopped: return HakoSimState.Stopped;
            case SimulationState.Runnable: return HakoSimState.Runnable;
            case SimulationState.Running: return HakoSimState.Running;
            case SimulationState.Stopping: return HakoSimState.Stopping;
            case SimulationState.Terminated: return HakoSimState.Terminated;
            default: return HakoSimState.Terminated;
        }
    }

    public bool SimulationStart() => hakoCommand.SimulationStart();
    public bool SimulationStop() => hakoCommand.SimulationStop();
    public bool SimulationReset() => hakoCommand.SimulationReset();
}