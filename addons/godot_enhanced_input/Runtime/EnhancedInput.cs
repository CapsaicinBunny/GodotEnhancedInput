using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The autoload that owns the <see cref="InputPlayer"/>s: it receives Godot input events, hands each to
/// the players that own its device, and ticks the players. It never marks events as handled (except a
/// press captured for rebinding), so the GUI and other nodes keep receiving all input. Player 0
/// always exists.
/// </summary>
public partial class EnhancedInput : Node
{
    /// <summary>The active instance (normally the autoload).</summary>
    public static EnhancedInput Instance { get; private set; }

    /// <summary>Player 0 of the active instance, or null when there is none.</summary>
    public static InputPlayer DefaultPlayer => Instance?.GetPlayer(0);

    /// <summary>Raised when a player is added.</summary>
    [Signal] public delegate void PlayerAddedEventHandler(InputPlayer player);

    /// <summary>Raised when a player is removed.</summary>
    [Signal] public delegate void PlayerRemovedEventHandler(InputPlayer player);

    /// <summary>Raised when a joypad is assigned to a player (by code or by joining).</summary>
    [Signal] public delegate void JoypadAssignedEventHandler(InputPlayer player, int device);

    /// <summary>Raised when a button press on an unowned joypad assigns it to a player (see <see cref="JoinMode"/>).</summary>
    [Signal] public delegate void PlayerJoinedEventHandler(InputPlayer player, int device);

    /// <summary>Raised when a joypad is taken from a player (by code, or because another pad took its device index).</summary>
    [Signal] public delegate void JoypadUnassignedEventHandler(InputPlayer player, int device);

    /// <summary>Raised when a joypad connects or disconnects. Its owner, if any, keeps it for when it returns.</summary>
    [Signal] public delegate void JoypadConnectionChangedEventHandler(int device, bool connected);

    /// <summary>When players are ticked.</summary>
    [Export]
    public TickMode TickMode
    {
        get => _tickMode;
        set
        {
            _tickMode = value;
            // Real-time ticking places each change at the moment it arrived; manual ticks are not wall-clock time.
            foreach (var player in _players) player.UseEventTimes = value != TickMode.Manual;
        }
    }

    private TickMode _tickMode = TickMode.Auto;

    private EnhancedInputConfig _configuration;

    /// <summary>
    /// The game's input configuration, loaded into every player (also players added later): control
    /// schemes, contexts by id, startup contexts, default settings. Optional.
    /// </summary>
    [Export]
    public EnhancedInputConfig Configuration
    {
        get => _configuration;
        set
        {
            _configuration = value;
            foreach (var player in _players) player.LoadConfiguration(value);
        }
    }

    /// <summary>
    /// Release keys and buttons that Godot's <c>Input</c> no longer reports as held — when game code
    /// marked their release as handled before it reached this node, they would otherwise stay held.
    /// Only ever releases; checked once per tick for held channels only. On by default.
    /// </summary>
    [Export] public bool ReleaseStuckInput { get; set; } = true;

    /// <summary>How many stuck keys or buttons <see cref="ReleaseStuckInput"/> has released (diagnostics).</summary>
    public int StuckInputReleased { get; private set; }

    /// <summary>How often the device registry is checked against Godot's list of connected pads (seconds).</summary>
    [Export] public double DeviceCheckSeconds { get; set; } = 2.0;

    private bool _tickedThisFrame;
    private ulong _lastTickUsec;

    /// <summary>How event intake shares input with the GUI.</summary>
    [Export] public GuiPolicy GuiPolicy { get; set; } = GuiPolicy.RespectGui;

    /// <summary>How unowned joypads become a player's.</summary>
    [Export] public JoinMode JoinMode { get; set; } = JoinMode.Manual;

    /// <summary>Most players joining may create.</summary>
    [Export(PropertyHint.Range, "1,16,1")] public int MaxPlayers { get; set; } = 4;

    /// <summary>When on, the press that joins a joypad does not also reach the player's actions.</summary>
    [Export] public bool ConsumeJoinPress { get; set; } = true;

    private readonly List<InputPlayer> _players = new();

    // Split screen: which player each pressed pointer (mouse = -1, touch = its index) belongs to.
    private readonly Dictionary<int, InputPlayer> _pointerCapture = new();
    private const int NoPointer = int.MinValue;

    private static int PointerId(InputEvent @event) => @event switch
    {
        InputEventMouse => -1,
        InputEventScreenTouch touch => touch.Index,
        InputEventScreenDrag drag => drag.Index,
        _ => NoPointer,
    };

    private static bool IsPointerPress(InputEvent @event)
        => @event is InputEventMouseButton { Pressed: true } or InputEventScreenTouch { Pressed: true };

    // The interaction ends when the last mouse button is let go, or the finger lifts (or is canceled).
    private static bool IsPointerRelease(InputEvent @event) => @event switch
    {
        InputEventMouseButton { Pressed: false } button => button.ButtonMask == 0,
        InputEventScreenTouch { Pressed: false } => true,
        _ => false,
    };
    private readonly List<InputPlayer> _targets = new();
    private readonly Dictionary<int, string> _joypadGuids = new();
    private int _profileCounter;
    private bool _joySignalConnected;
    private InputEvent _swallowedJoinPress;

    /// <summary>Creates the system node (normally the autoload).</summary>
    public EnhancedInput()
    {
        Name = "EnhancedInput";
        // Keep receiving events and ticking while the tree is paused; actions decide for themselves.
        ProcessMode = ProcessModeEnum.Always;
        // Tick before any gameplay node reads input, whatever priorities the game uses.
        ProcessPriority = int.MinValue;
        ProcessPhysicsPriority = int.MinValue;
    }

    // ------------------------------------------------------------------
    //  Players
    // ------------------------------------------------------------------

    /// <summary>The players, by index.</summary>
    public IReadOnlyList<InputPlayer> Players
    {
        get
        {
            EnsureDefaultPlayer();
            return _players;
        }
    }

    /// <summary>The player with the given index, or null.</summary>
    public InputPlayer GetPlayer(int index = 0)
    {
        EnsureDefaultPlayer();
        return index >= 0 && index < _players.Count ? _players[index] : null;
    }

    /// <summary>
    /// Adds a player and returns it. New players listen to no keyboard, mouse, touch or
    /// <see cref="InputEventAction"/> until told to, and receive unowned joypads until one is assigned.
    /// </summary>
    public InputPlayer AddPlayer()
    {
        EnsureDefaultPlayer();
        return CreatePlayer();
    }

    /// <summary>Removes a player other than player 0, releasing its joypads. The rest are re-indexed.</summary>
    public bool RemovePlayer(InputPlayer player)
    {
        if (player != null)
            foreach (var pair in new List<KeyValuePair<int, InputPlayer>>(_pointerCapture))
                if (pair.Value == player) _pointerCapture.Remove(pair.Key);
        if (player != null) InputMapBridge.Forget(player);
        if (player == null || _players.IndexOf(player) <= 0) return false;
        player.ReleaseAllHapticOutputs();   // nothing the player started keeps running on its devices
        _players.Remove(player);
        foreach (var d in _devices)
            if (d.Owner == player) d.Owner = null;
        for (int i = 0; i < _players.Count; i++)
        {
            _players[i].PlayerIndex = i;
            _players[i].Name = $"Player{i}";
        }
        if (player.GetParent() == this) RemoveChild(player);
        EmitSignal(SignalName.PlayerRemoved, player);
        player.QueueFree();
        return true;
    }

    private void EnsureDefaultPlayer()
    {
        if (_players.Count == 0) CreatePlayer();
    }

    private InputPlayer CreatePlayer()
    {
        int index = _players.Count;
        var player = new InputPlayer { Name = $"Player{index}", PlayerIndex = index, ProfileId = NextProfileId(), UseEventTimes = TickMode != TickMode.Manual };
        if (index > 0)
        {
            // Added players only hear the devices they are given.
            player.AcceptKeyboardMouse = false;
            player.AcceptTouch = false;
            player.AcceptActionEvents = false;
            player.ReceivesUnownedJoypads = false;
            player.PollInputMap = false;   // the InputMap is global state: one player reads it
        }
        _players.Add(player);
        AddChild(player);
        if (_configuration != null) player.LoadConfiguration(_configuration);
        EmitSignal(SignalName.PlayerAdded, player);
        return player;
    }

    // ------------------------------------------------------------------
    //  Joypads
    // ------------------------------------------------------------------

    private string NextProfileId()
    {
        // Stable for the player's lifetime: re-indexing after a removal does not move anyone's profile.
        string id;
        do id = $"player_{_profileCounter++}";
        while (_players.Exists(p => p.ProfileId == id));
        return id;
    }

    /// <summary>When on (default), losing window or application focus releases all held input of every player.</summary>
    [Export] public bool ResetInputOnFocusLoss { get; set; } = true;

    /// <summary>Releases all held input of every player (their actions finish on the next tick).</summary>
    public void ResetAllInput() => ResetAllInput(InputEndReason.Cleared);

    /// <summary>Releases all held input of every player as cut off for a reason (release triggers do not fire).</summary>
    public void ResetAllInput(InputEndReason reason)
    {
        foreach (var player in _players) player.ClearRawInput(reason);
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && _windows.Count > 0)
        {
            // Focus may only be moving to another of the game's windows: decide once it has moved.
            QueueFocusCheck();
            return;
        }
        bool focusOut = what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut;
        bool suspended = what == NotificationApplicationPaused;
        if ((focusOut || suspended) && StopHapticsWhenInactive)
            foreach (var player in _players) player.ReleaseAllHapticOutputs();
        if (!ResetInputOnFocusLoss) return;
        if (focusOut) ResetAllInput(InputEndReason.FocusLost);
        else if (suspended) ResetAllInput(InputEndReason.ApplicationSuspended);
    }

    /// <summary>
    /// When on (default), losing focus or being suspended stops every player's vibration, trigger
    /// resistance and force feedback, so nothing keeps buzzing or pushing while the game is in the
    /// background.
    /// </summary>
    [Export] public bool StopHapticsWhenInactive { get; set; } = true;

    // ------------------------------------------------------------------
    //  Events and ticking
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        Instance = this;
        if (!Engine.IsEditorHint()) EnhancedInputSettings.ApplyTo(this);
        EnsureDefaultPlayer();
        if (!_joySignalConnected)
        {
            Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
            _joySignalConnected = true;
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_joySignalConnected)
        {
            Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
            _joySignalConnected = false;
        }
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Offers an event to the players it belongs to: joypad events to the joypad's owner (or, for an
    /// unowned joypad, to players with no joypads listed — or to a player it joins); other events to
    /// players that accept that kind of device. Returns true when a player's running rebind capture
    /// took it.
    /// </summary>
    public bool FeedEvent(InputEvent @event, IntakeStage stage = IntakeStage.Direct) => FeedEvent(@event, stage, null);

    /// <summary>
    /// Offers an event like <see cref="FeedEvent(InputEvent, IntakeStage)"/>, but only to the given players
    /// (none or null: any) — events of a window that belongs to some players (see <see cref="AttachWindow"/>).
    /// </summary>
    public bool FeedEvent(InputEvent @event, IntakeStage stage, IReadOnlyCollection<InputPlayer> onlyPlayers)
    {
        if (@event == null) return false;
        EnsureDefaultPlayer();
        @event = ApplyDeviceProfile(@event);

        if (ReferenceEquals(@event, _swallowedJoinPress))
        {
            if (stage != IntakeStage.Early) _swallowedJoinPress = null;
            return false;
        }

        _targets.Clear();
        if (@event is InputEventJoypadButton or InputEventJoypadMotion)
        {
            int device = @event.Device;
            if (IsJoypadReconnectPending(device)) return false;   // whose pad it is has not been decided yet
            var owner = GetJoypadOwner(device);
            if (owner == null && JoinMode == JoinMode.OnButtonPress && stage != IntakeStage.Unhandled
                && @event is InputEventJoypadButton { Pressed: true })
            {
                owner = Join(device);
                if (owner != null && ConsumeJoinPress)
                {
                    // Early stage: the same event comes back from _UnhandledInput; drop it there too.
                    _swallowedJoinPress = stage == IntakeStage.Early ? @event : null;
                    return false;
                }
            }

            if (owner != null) _targets.Add(owner);
            else
                foreach (var player in _players)
                    if (player.JoypadDevices.Count == 0 && player.ReceivesUnownedJoypads) _targets.Add(player);
        }
        else
        {
            foreach (var player in _players)
                if (player.Accepts(@event)) _targets.Add(player);
        }

        bool captured = false;
        int pointer = PointerId(@event);
        _pointerCapture.TryGetValue(pointer, out var holder);
        if (holder != null && !_players.Contains(holder)) { _pointerCapture.Remove(pointer); holder = null; }
        InputPlayer pressedFor = null;
        foreach (var player in _targets)
        {
            if (onlyPlayers != null && onlyPlayers.Count > 0 && !System.Linq.Enumerable.Contains(onlyPlayers, player)) continue;
            InputEvent local = @event;
            if (pointer != NoPointer && player.HasPointerRegion)
            {
                // Split screen: a pressed pointer stays with the player it was pressed for until it is
                // released (wherever it moves); otherwise it goes to the player whose region it is in.
                if (holder != null)
                {
                    if (holder != player) continue;
                    local = player.ToRegion(@event);
                }
                else if (!player.TryLocalizePointer(@event, out local)) continue;
                else if (IsPointerPress(@event)) pressedFor = player;
            }
            if (player.FeedEvent(local, stage)) captured = true;
        }
        if (pressedFor != null) _pointerCapture[pointer] = pressedFor;
        if (IsPointerRelease(@event)) _pointerCapture.Remove(pointer);
        return captured;
    }

    /// <summary>Every player's <see cref="InputPlayer.DescribeState"/>, plus the system's settings.</summary>
    public string DescribeState(bool onlyActive = false)
    {
        EnsureDefaultPlayer();
        var text = new System.Text.StringBuilder();
        text.AppendLine($"EnhancedInput  tick: {TickMode}  gui: {GuiPolicy}  join: {JoinMode} (max {MaxPlayers})  players: {_players.Count}");
        foreach (var player in _players) text.Append(player.DescribeState(onlyActive));
        return text.ToString();
    }

    /// <summary>Ticks every player.</summary>
    public void Tick(double delta)
    {
        EnsureDefaultPlayer();
        PollBackends(delta);
        bool live = IsInsideTree() && !Engine.IsEditorHint();
        if (live)
        {
            _joypadCheckTimer += delta;
            if (_joypadCheckTimer >= DeviceCheckSeconds) { _joypadCheckTimer = 0; VerifyConnectedDevices(); }
        }
        live &= ReleaseStuckInput;
        for (int i = 0; i < _players.Count; i++)
        {
            var player = _players[i];
            if (live && !player.IsReplaying) StuckInputReleased += player.Raw.ReleaseStuck();
            player.Tick(delta);
        }
    }

    private void AutoTick(double fallbackDelta)
    {
        if (_tickedThisFrame) return;
        _tickedThisFrame = true;
        ulong now = Time.GetTicksUsec();
        double delta = _lastTickUsec == 0 || now <= _lastTickUsec
            ? fallbackDelta
            : System.Math.Min(1.0, (now - _lastTickUsec) / 1_000_000.0) * Engine.TimeScale;
        _lastTickUsec = now;
        Tick(delta);
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        bool captured = FeedEvent(@event, GuiPolicy == GuiPolicy.ReadEverything ? IntakeStage.Direct : IntakeStage.Early);

        // The one exception to "never mark events handled": a press captured for rebinding must not
        // also click the focused button or reach gameplay.
        if (captured && IsInsideTree()) GetViewport()?.SetInputAsHandled();
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (GuiPolicy == GuiPolicy.RespectGui) FeedEvent(@event, IntakeStage.Unhandled);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (TickMode == TickMode.Process) Tick(delta);
        else if (TickMode == TickMode.Auto)
        {
            AutoTick(delta);
            _tickedThisFrame = false;   // this node processes first; the next frame ticks again
        }
        PublishFrames(InputLoop.Process);
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (TickMode == TickMode.Physics) Tick(delta);
        else if (TickMode == TickMode.Auto) AutoTick(delta);
        PublishFrames(InputLoop.Physics);
    }

    /// <summary>
    /// Starts a new frame of a loop for every player's view of it (<see cref="InputPlayer.PhysicsFrame"/>,
    /// <see cref="InputPlayer.ProcessFrame"/>). The autoload calls it at the start of each physics step and
    /// each rendered frame, after any tick in that loop, whatever the <see cref="TickMode"/>.
    /// </summary>
    public void PublishFrames(InputLoop loop)
    {
        foreach (var player in _players) player.PublishFrameIfUsed(loop);
    }
}
