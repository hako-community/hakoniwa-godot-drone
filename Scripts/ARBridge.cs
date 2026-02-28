using System.Collections.Generic;
using System.Threading.Tasks;
using hakoniwa.ar.bridge;
using hakoniwa.environment.impl;
using hakoniwa.environment.interfaces;
using hakoniwa.objects.core;
using hakoniwa.pdu;
using hakoniwa.pdu.core;
using hakoniwa.pdu.interfaces;
using Godot;

public partial class ARBridge : Node, IHakoniwaArBridgePlayer, IHakoPduInstance
{
    public static ARBridge Instance { get; private set; }

    public override void _EnterTree()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private IHakoniwaArBridge bridge;
    [Export]
    public Node3D base_object;
    private IDroneInput drone_input;
    private Vector3 base_pos;
    private Vector3 base_rot;

    [Export]
    public Node player_obj;
    [Export]
    public Node[] avatar_objs;
    private IHakoniwaArObject ar_player;
    private List<IHakoniwaArObject> ar_avatars;
    private IPduManager mgr = null;
    private IEnvironmentService service;

    [Export]
    public bool xr = false;
    [Export]
    public float moveSpeed = 0.1f;
    [Export]
    public float rotationSpeed = 1.0f;

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

    public void setPositioningSpeed(float rotation, float move)
    {
        moveSpeed = move;
        rotationSpeed = rotation;
    }

    public void GetBasePosition(out HakoVector3 position, out HakoVector3 rotation)
    {
        position = new HakoVector3(
            base_pos.X,
            base_pos.Y,
            base_pos.Z
            );
        rotation = new HakoVector3(
            base_rot.X,
            base_rot.Y,
            base_rot.Z
            );
    }

    public Task<bool> StartService(string serverUri)
    {
        service = EnvironmentServiceFactory.Create("websocket_dotnet", "godot", ".");
        mgr = new PduManager(service, ".");
        GD.Print("Start Service!! " + serverUri);
        var ret = mgr.StartService(serverUri);
        GD.Print("Start Service!! " + serverUri + " ret: " + ret);
        return ret;
    }

    public bool StopService()
    {
        if (mgr != null)
        {
            mgr.StopService();
            mgr = null;
        }
        return true;
    }

    public void UpdatePosition(HakoVector3 position, HakoVector3 rotation)
    {
        Vector2 left_value;
        Vector2 right_value;
        left_value = -drone_input.GetLeftStickInput();
        right_value = -drone_input.GetRightStickInput();

        float deltaTime = (float)GetPhysicsProcessDeltaTime();

        // 現在の回転角度（度数法からラジアンに変換）
        float angleY = Mathf.DegToRad(base_rot.Y);

        // 前後移動
        Vector3 forward = new Vector3(Mathf.Sin(angleY), 0, Mathf.Cos(angleY));
        Vector3 right = new Vector3(Mathf.Cos(angleY), 0, -Mathf.Sin(angleY));

        // 移動計算（回転を考慮）
        Vector3 moveDelta = forward * right_value.Y * moveSpeed * deltaTime
                          + right * right_value.X * moveSpeed * deltaTime
                          + Vector3.Up * left_value.Y * moveSpeed * deltaTime;

        // 浮動小数点誤差による不要なドリフトを防ぐ
        if (!Mathf.IsZeroApprox(moveDelta.Length()))
        {
            base_pos += moveDelta;
        }

        // 回転計算（速度を考慮）
        base_rot.Y = Mathf.PosMod(base_rot.Y + left_value.X * rotationSpeed * deltaTime, 360f);

        // 更新
        if (base_object != null)
        {
            base_object.Position = base_pos;
            base_object.RotationDegrees = base_rot;
        }
    }

    public override void _Ready()
    {
        if (player_obj != null)
        {
            // Recursive search or GetComponent logic
            ar_player = FindIHakoniwaArObject(player_obj);
            if (ar_player == null)
            {
                throw new System.Exception("Can not find Player ar obj");
            }
        }
        
        ar_avatars = new List<IHakoniwaArObject>();
        if (avatar_objs != null)
        {
            foreach (var entry in avatar_objs)
            {
                var e = FindIHakoniwaArObject(entry);
                if (e == null)
                {
                    throw new System.Exception("Can not find Avatar ar obj");
                }
                ar_avatars.Add(e);
            }
        }

        if (xr)
        {
            drone_input = hakoniwa.objects.core.HakoDroneXrInputManager.Instance;
        }
        else
        {
            drone_input = hakoniwa.objects.core.HakoDroneInputManager.Instance;
        }
        base_pos = new Vector3();
        base_rot = new Vector3();
        bridge = HakoniwaArBridgeDevice.Instance;
        bridge.Register(this);
        bridge.Start();
    }

    private IHakoniwaArObject FindIHakoniwaArObject(Node node)
    {
        if (node is IHakoniwaArObject obj) return obj;
        foreach (Node child in node.GetChildren())
        {
            var found = FindIHakoniwaArObject(child);
            if (found != null) return found;
        }
        return null;
    }

    public override void _Process(double delta)
    {
        if (drone_input == null) return;
        
        bool x_button_pressed = drone_input.IsXButtonPressed();
        bool y_button_pressed = drone_input.IsYButtonPressed();
        
        if (bridge.GetState() == BridgeState.POSITIONING && x_button_pressed)
        {
            GD.Print("x_button_pressed (X/Square): " + x_button_pressed);
            bridge.DevicePlayStartEvent();
        }
        else if (bridge.GetState() == BridgeState.PLAYING && y_button_pressed)
        {
            GD.Print("y_button_pressed (Y/Triangle): " + y_button_pressed);
            bridge.DeviceResetEvent();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        bridge.Run();
    }

    public override void _ExitTree()
    {
        if (bridge != null)
        {
            bridge.Stop();
        }
    }

    public void SetBasePosition(HakoVector3 position, HakoVector3 rotation)
    {
        base_pos.X = position.X;
        base_pos.Y = position.Y;
        base_pos.Z = position.Z;
        base_rot.X = rotation.X;
        base_rot.Y = rotation.Y;
        base_rot.Z = rotation.Z;
    }

    public async Task InitializeAsync(PlayerData player, List<AvatarData> avatars)
    {
        if (ar_player != null)
        {
            await ar_player.DeclarePduAsync(null, null);
        }
        foreach (var avatar in ar_avatars)
        {
            await avatar.DeclarePduAsync(null, null);
        }
    }
}
