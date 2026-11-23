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
    [Export] public TickMode TickMode { get; set; } = TickMode.Process;

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
        foreach (var other in _players)
            if (other != player) other.JoypadDevices.Remove(device);
        if (!player.JoypadDevices.Contains(device)) player.JoypadDevices.Add(device);
        if (string.IsNullOrEmpty(guid)) _joypadGuids.Remove(device);
        else _joypadGuids[device] = guid;
        // Whatever the joypad was doing belongs to its previous route.
        foreach (var other in _players) other.Raw.ClearDevice(device);
        EmitSignal(SignalName.JoypadAssigned, player, device);
    }

    /// <summary>Takes a joypad from whichever player owns it. Returns false if nobody did.</summary>
    public bool UnassignJoypad(int device)
    {
        bool removed = false;
        _joypadGuids.Remove(device);
        foreach (var player in _players)
            if (player.JoypadDevices.Remove(device))
            {
                removed = true;
                player.Raw.ClearDevice(device);
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
    public void NotifyJoypadConnection(int device, bool connected, string guid = null)
    {
        if (!connected)
        {
            foreach (var player in _players) player.Raw.ClearDevice(device);
        }
        else if (!string.IsNullOrEmpty(guid) && _joypadGuids.TryGetValue(device, out var known) && known != guid)
        {
            UnassignJoypad(device);
        }
        EmitSignal(SignalName.JoypadConnectionChanged, device, connected);
    }

    /// <summary>When on (default), losing window or application focus releases all held input of every player.</summary>
    [Export] public bool ResetInputOnFocusLoss { get; set; } = true;

    /// <summary>Releases all held input of every player (their actions finish on the next tick).</summary>
    public void ResetAllInput()
    {
        foreach (var player in _players) player.ClearRawInput();
    }

    public override void _Notification(int what)
    {
        if (!ResetInputOnFocusLoss) return;
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut || what == NotificationApplicationPaused)
            ResetAllInput();
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

        if (ReferenceEquals(@event, _swallowedJoinPress))
        {
            if (stage != IntakeStage.Early) _swallowedJoinPress = null;
            return false;
        }

        _targets.Clear();
        if (@event is InputEventJoypadButton or InputEventJoypadMotion)
        {
            int device = @event.Device;
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
        for (int i = 0; i < _players.Count; i++) _players[i].Tick(delta);
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
    }

    public override void _PhysicsProcess(double delta)
    {
        if (TickMode == TickMode.Physics) Tick(delta);
    }
}
