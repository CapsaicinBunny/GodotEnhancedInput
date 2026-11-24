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
    [Export] public TickMode TickMode { get; set; } = TickMode.Auto;

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
    private readonly List<InputPlayer> _targets = new();
    private readonly Dictionary<int, string> _joypadGuids = new();
    private int _profileCounter;
    private bool _joySignalConnected;
    private InputEvent _swallowedJoinPress;

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
        if (player == null || _players.IndexOf(player) <= 0) return false;
        _players.Remove(player);
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
        var player = new InputPlayer { Name = $"Player{index}", PlayerIndex = index, ProfileId = NextProfileId() };
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
        EmitSignal(SignalName.PlayerAdded, player);
        return player;
    }

    // ------------------------------------------------------------------
    //  Joypads
    // ------------------------------------------------------------------

    /// <summary>A connected joypad's identity, or null (never asks Godot about a device that is not connected).</summary>
    private static string GuidOf(int device)
        => device >= 0 && Input.GetConnectedJoypads().Contains(device) ? Input.GetJoyGuid(device) : null;

    private string NextProfileId()
    {
        // Stable for the player's lifetime: re-indexing after a removal does not move anyone's profile.
        string id;
        do id = $"player_{_profileCounter++}";
        while (_players.Exists(p => p.ProfileId == id));
        return id;
    }

    /// <summary>Gives a joypad to a player, taking it from any other player. The joypad's identity is remembered.</summary>
    public void AssignJoypad(InputPlayer player, int device) => AssignJoypad(player, device, GuidOf(device));

    /// <summary>
    /// Gives a joypad to a player and remembers its identity (<paramref name="guid"/>, as from
    /// <c>Input.GetJoyGuid</c>), so a different pad that later receives the same device index does not
    /// inherit the assignment.
    /// </summary>
    public void AssignJoypad(InputPlayer player, int device, string guid)
    {
        if (player == null || device < 0) return;
        if (_ambiguous.TryGetValue(device, out var doubt))
        {
            // Assigning a doubtful pad to one of its candidates settles whose it is.
            if (doubt.Entries.Exists(x => x.Owner == player)) ResolveJoypadReconnect(device, player);
            _ambiguous.Remove(device);
        }
        var record = GetJoypadDevice(device);
        if (record != null && !record.IsConnected) record.LastOwner = player;   // a missing pad: it comes back to this player
        foreach (var other in _players)
            if (other != player) other.JoypadDevices.Remove(device);
        if (!player.JoypadDevices.Contains(device)) player.JoypadDevices.Add(device);
        if (string.IsNullOrEmpty(guid)) _joypadGuids.Remove(device);
        else _joypadGuids[device] = guid;
        // Whatever the joypad was doing belongs to its previous route: cut it off there.
        foreach (var other in _players) other.Raw.ClearDevice(device, InputEndReason.DeviceReassigned);
        EmitSignal(SignalName.JoypadAssigned, player, device);
    }

    /// <summary>Takes a joypad from whichever player owns it. Returns false if nobody did.</summary>
    public bool UnassignJoypad(int device)
    {
        bool removed = false;
        _joypadGuids.Remove(device);
        _ambiguous.Remove(device);
        var record = GetJoypadDevice(device);
        if (record != null && !record.IsConnected) record.LastOwner = null;   // a missing pad no longer returns to anyone
        foreach (var player in _players)
            if (player.JoypadDevices.Remove(device))
            {
                removed = true;
                player.Raw.ClearDevice(device, InputEndReason.DeviceReassigned);
                EmitSignal(SignalName.JoypadUnassigned, player, device);
            }
        return removed;
    }

    /// <summary>The player that owns a joypad, or null.</summary>
    public InputPlayer GetJoypadOwner(int device)
    {
        foreach (var player in _players)
            if (player.JoypadDevices.Contains(device)) return player;
        return null;
    }

    private InputPlayer Join(int device)
    {
        InputPlayer target = null;
        foreach (var player in _players)
            if (player.JoypadDevices.Count == 0) { target = player; break; }
        if (target == null && _players.Count < MaxPlayers) target = CreatePlayer();
        if (target == null) return null;

        AssignJoypad(target, device);
        EmitSignal(SignalName.PlayerJoined, target, device);
        return target;
    }

    private void OnJoyConnectionChanged(long device, bool connected)
        => NotifyJoypadConnection((int)device, connected, connected ? GuidOf((int)device) : null);

    /// <summary>
    /// Handles a joypad connecting or disconnecting (called from Godot's signal; public for tests and
    /// custom backends). A disconnected pad keeps its owner. When a pad connects under a device index
    /// whose remembered identity differs (<paramref name="guid"/>), the old assignment is dropped instead
    /// of being inherited by the new pad.
    /// </summary>
    public void NotifyJoypadConnection(int device, bool connected, string guid = null) => NotifyJoypadConnection(device, connected, guid, null, null);

    /// <summary>
    /// Handles a joypad connecting or disconnecting, with its serial number and name when known (tests and
    /// backends can pass them). A pad that comes back — matched by serial, else identity — keeps its
    /// owner even under a different device index; a player whose current device disconnects falls back
    /// to another one it can use, and <see cref="PlayerDeviceLost"/> tells the game when none is left.
    /// </summary>
    public void NotifyJoypadConnection(int device, bool connected, string guid, string serial, string name)
    {
        _connectedJoypadsValid = false;
        EnsureBuiltinDevices();
        if (!connected)
        {
            foreach (var player in _players) player.Raw.ClearDevice(device);
            var gone = DisconnectJoypadRecord(device);
            bool wasPending = _ambiguous.Remove(device, out var pending) && pending.Pending == gone && gone != null;
            if (gone != null && !wasPending)
                foreach (var player in _players)
                {
                    bool owned = player.JoypadDevices.Contains(device);
                    bool current = player.CurrentDevice == gone;
                    if (!owned && !current) continue;
                    player.OnDeviceLost(gone);
                    if (owned && !player.HasOtherJoypad(device)) EmitSignal(SignalName.PlayerDeviceLost, player, gone.Id);
                }
            if (wasPending) _devices.Remove(gone);   // it never became anyone's
        }
        else
        {
            var reading = ReadJoypad(device, guid, serial, name);
            bool refresh = ConnectedJoypadAt(device) is { } same && same.ModelKey == reading.Model;
            var info = ConnectJoypadRecord(device, reading, out bool returning, out bool bySerial);
            if (!refresh)
            {
                var owner = returning ? AliveOwner(info) : null;
                int oldIndex = info.PreviousIndex >= 0 ? info.PreviousIndex : device;
                info.LastOwner = null;
                bool ambiguous = !bySerial && (_owned.Count >= 2 || (ReconnectWithoutSerial == ReconnectPolicy.Ask && _owned.Count >= 1));
                var candidates = ambiguous ? new List<(InputDeviceInfo, InputPlayer, int)>(_owned) : null;

                // Nobody keeps a claim on this index for some other pad that is still missing, and a pad
                // seen for the first time never inherits an assignment meant for a different one.
                foreach (var player in _players.ToArray())
                {
                    if (player == owner || !player.JoypadDevices.Contains(device)) continue;
                    bool stale = IsStaleClaim(player, device, info)
                        || (!returning && !HasJoypadRecordAt(device, info) && !string.IsNullOrEmpty(reading.Guid)
                            && _joypadGuids.TryGetValue(device, out var known) && known != reading.Guid);
                    if (!stale) continue;
                    player.JoypadDevices.Remove(device);
                    _joypadGuids.Remove(device);
                    EmitSignal(SignalName.JoypadUnassigned, player, device);
                }

                if (owner != null)
                {
                    GiveJoypadIndex(owner, oldIndex, device);
                    EmitSignal(SignalName.PlayerDeviceRegained, owner, info.Id);
                }
                if (ambiguous)
                {
                    var a = new Ambiguity { Pending = returning ? null : info };
                    var list = new Godot.Collections.Array<InputPlayer>();
                    foreach (var c in candidates)
                    {
                        a.Entries.Add(c);
                        list.Add(c.Item2);
                    }
                    _ambiguous[device] = a;
                    EmitSignal(SignalName.JoypadReconnectAmbiguous, device, list, owner);
                }
                else _ambiguous.Remove(device);
            }
        }
        EmitSignal(SignalName.JoypadConnectionChanged, device, connected);
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

    public override void _Notification(int what)
    {
        if (!ResetInputOnFocusLoss) return;
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut) ResetAllInput(InputEndReason.FocusLost);
        else if (what == NotificationApplicationPaused) ResetAllInput(InputEndReason.ApplicationSuspended);
    }

    // ------------------------------------------------------------------
    //  Events and ticking
    // ------------------------------------------------------------------

    public override void _EnterTree()
    {
        Instance = this;
        EnsureDefaultPlayer();
        if (!_joySignalConnected)
        {
            Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
            _joySignalConnected = true;
        }
    }

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
    public bool FeedEvent(InputEvent @event, IntakeStage stage = IntakeStage.Direct)
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
        foreach (var player in _targets)
            if (player.FeedEvent(@event, stage)) captured = true;
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

    public override void _Input(InputEvent @event)
    {
        bool captured = FeedEvent(@event, GuiPolicy == GuiPolicy.ReadEverything ? IntakeStage.Direct : IntakeStage.Early);

        // The one exception to "never mark events handled": a press captured for rebinding must not
        // also click the focused button or reach gameplay.
        if (captured && IsInsideTree()) GetViewport()?.SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GuiPolicy == GuiPolicy.RespectGui) FeedEvent(@event, IntakeStage.Unhandled);
    }

    public override void _Process(double delta)
    {
        if (TickMode == TickMode.Process) Tick(delta);
        else if (TickMode == TickMode.Auto)
        {
            AutoTick(delta);
            _tickedThisFrame = false;   // this node processes first; the next frame ticks again
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (TickMode == TickMode.Physics) Tick(delta);
        else if (TickMode == TickMode.Auto) AutoTick(delta);
    }
}
