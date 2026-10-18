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
    public bool IsReady => isReady;

    // ★ 2026-10-18: 箱庭との繋がりの状態。DroneAvatar はこれを見て、オンライン（箱庭経由）かオフライン（hakodrone）かを決める。
    public enum ConnectionState { Waiting, Connecting, Ready, Failed, Cancelled }
    public ConnectionState Connection { get; private set; } = ConnectionState.Waiting;

    // オフラインで動き始めた場面が呼ぶ。プラントを待つのをやめる（あとから来た箱庭には繋がない）。
    public void CancelWaitForPlant()
    {
        if (Connection == ConnectionState.Waiting) Connection = ConnectionState.Cancelled;
    }
    
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

    // Hako objects added at runtime (e.g. a cloned avatar for a 2-drone view).
    // Registered before _Ready via RegisterExtraHakoObject and initialized
    // alongside the exported hakoObjects.
    private readonly List<Node> extraHakoObjects = new List<Node>();

    // Add a hako object (a Node carrying an IHakoObject) at runtime. Must be
    // called before this asset's _Ready (e.g. from a sibling's _EnterTree, which
    // always runs before any _Ready in the same tree).
    public void RegisterExtraHakoObject(Node obj)
    {
        if (obj != null) extraHakoObjects.Add(obj);
    }

    private bool HakoAssetIsValid(List<IHakoObject> hakoObectList)
    {
        if (hakoObjects == null && extraHakoObjects.Count == 0) return false;
        var all = new List<Node>();
        if (hakoObjects != null) all.AddRange(hakoObjects);
        all.AddRange(extraHakoObjects);
        foreach (var obj in all)
        {
            IHakoObject ihako = NodeUtil.FindNodeByInterface<IHakoObject>(obj);
            if (ihako == null)
            {
                throw new ArgumentException("Can not find IHakoObject on " + obj.Name);
            }
            hakoObectList.Add(ihako);
        }
        return hakoObectList.Count > 0;
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
            Connection = ConnectionState.Failed;
            return;
        }

        // Optional override of the channel-definition json (e.g. a 2-drone def
        // that adds Drone1). Lets a launcher point at a different pdudef without
        // editing the scene. Unset -> keep the scene's customJsonFilePath.
        string cfgOverride = OS.GetEnvironment("HAKO_PDU_CONFIG");
        if (!string.IsNullOrEmpty(cfgOverride))
        {
            customJsonFilePath = cfgOverride;
            GD.Print($"[HakoAsset] HAKO_PDU_CONFIG override -> {customJsonFilePath}");
        }

        // ★ 2026-10-18: 箱庭（プラント）が居ないまま shakoc を呼ぶと落ちる（HakoAssetImpl.HakoniwaMasterExists）。
        //   プラントが立つまで待って、立ったら自動で繋ぐ。オフラインで動く場面（DroneAvatar.enableOfflineMode）は
        //   待つのをやめさせる（CancelWaitForPlant）。
        if (!HakoAssetImpl.HakoniwaMasterExists())
        {
            GD.Print("[HakoAsset] 箱庭（プラント）を待っています。プラントを起動すると自動で繋がります");
            while (!HakoAssetImpl.HakoniwaMasterExists(false))
            {
                await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
                if (!IsInsideTree()) return;
                if (Connection == ConnectionState.Cancelled)
                {
                    GD.Print("[HakoAsset] オフラインで動くので、箱庭は待ちません");
                    return;
                }
            }
            // マスタが共有メモリを作り終えるのを少し待つ
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            if (!IsInsideTree()) return;
            if (Connection == ConnectionState.Cancelled) return;
            GD.Print("[HakoAsset] 箱庭が起動したので繋ぎます");
        }
        Connection = ConnectionState.Connecting;

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
                Connection = ConnectionState.Ready;
            }
            else
            {
                // ★ 2026-10-18: 登録できないまま EventInitialize を呼ぶと、PDU を宣言できずに例外になる（DroneAvatar）。
                //   前のプラントの共有メモリが残っている（強制終了など）か、箱庭が START 済みで新しい登録を受け付けないとき。
                //   オフラインに切り替えられる場面はローカル（hakodrone）で動く。
                GD.PrintErr("Can not register on Hakoniwa: " + assetName +
                            "（箱庭で動かすなら、プラントを起動し直してから、この場面も開き直してください）");
                Connection = ConnectionState.Failed;
                return;
            }
            foreach (var ihako in hakoObectList)
            {
                ihako.EventInitialize();
            }
        }
        else
        {
            GD.PrintErr("Can not Initialize Hakoniwa: " + assetName);
            Connection = ConnectionState.Failed;
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

    // ★ 2026-10-18: 箱庭に登録できるまで（プラント待ちの間も）は shakoc を呼ばない。
    //   GUI（SimStart・SimTime）は毎フレーム呼ぶので、hakoCommand が無い間に NullReferenceException になっていた。
    //   Terminated を返すと SimStart は START ボタンを押せなくする。
    public long GetWorldTime() => isReady ? hakoCommand.GetWorldTime() : 0;

    public HakoSimState GetState()
    {
        if (!isReady) return HakoSimState.Terminated;
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

    public bool SimulationStart() => isReady && hakoCommand.SimulationStart();
    public bool SimulationStop() => isReady && hakoCommand.SimulationStop();
    public bool SimulationReset() => isReady && hakoCommand.SimulationReset();
}