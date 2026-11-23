using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The autoload that owns the <see cref="InputPlayer"/>s: it receives Godot input events, hands each to
/// the players whose device filter accepts it, and ticks the players. It never marks events as
/// handled, so the GUI and other nodes keep receiving all input. Player 0 always exists.
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

    /// <summary>When players are ticked.</summary>
    [Export] public TickMode TickMode { get; set; } = TickMode.Process;

    /// <summary>How event intake shares input with the GUI.</summary>
    [Export] public GuiPolicy GuiPolicy { get; set; } = GuiPolicy.RespectGui;

    private readonly List<InputPlayer> _players = new();
    private bool _joySignalConnected;

    public EnhancedInput()
    {
        Name = "EnhancedInput";
        // Keep receiving events and ticking while the tree is paused; actions decide for themselves.
        ProcessMode = ProcessModeEnum.Always;
    }

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

    /// <summary>Adds a player and returns it.</summary>
    public InputPlayer AddPlayer()
    {
        EnsureDefaultPlayer();
        return CreatePlayer();
    }

    /// <summary>Removes a player other than player 0. The remaining players are re-indexed.</summary>
    public bool RemovePlayer(InputPlayer player)
    {
        if (player == null || _players.IndexOf(player) <= 0) return false;
        _players.Remove(player);
        for (int i = 0; i < _players.Count; i++) _players[i].PlayerIndex = i;
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
        var player = new InputPlayer { Name = $"Player{_players.Count}", PlayerIndex = _players.Count };
        _players.Add(player);
        AddChild(player);
        EmitSignal(SignalName.PlayerAdded, player);
        return player;
    }

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

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (connected) return;
        foreach (var player in _players) player.Raw.ClearDevice((int)device);
    }

    /// <summary>
    /// Offers an event to every player that accepts its device. Returns true when a player's running
    /// rebind capture took it.
    /// </summary>
    public bool FeedEvent(InputEvent @event, IntakeStage stage = IntakeStage.Direct)
    {
        if (@event == null) return false;
        EnsureDefaultPlayer();
        bool captured = false;
        foreach (var player in _players)
            if (player.Accepts(@event) && player.FeedEvent(@event, stage)) captured = true;
        return captured;
    }

    /// <summary>Ticks every player.</summary>
    public void Tick(double delta)
    {
        EnsureDefaultPlayer();
        foreach (var player in _players) player.Tick(delta);
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
