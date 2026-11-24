using System.Collections.Generic;
using System.Text;
using Godot;

namespace GodotEnhancedInput.Demo;

/// <summary>
/// A small top-down playground for Godot Enhanced Input. Everything visible is built here in code;
/// the input itself is data: the two contexts (gameplay.tres, menu.tres) and the actions they bind.
/// The panel on the left lists player 0's actions with phase, value and timings; the log shows events.
/// More joypads join as more players by pressing a button; on-screen controls appear on touchscreens
/// (or with F2).
/// </summary>
public partial class Demo : Node2D
{
    /// <summary>On-foot controls. Defaults to the demo's gameplay.tres.</summary>
    [Export] public EnhancedInputMappingContext Gameplay { get; set; }

    /// <summary>Pause menu. Defaults to the demo's menu.tres.</summary>
    [Export] public EnhancedInputMappingContext Menu { get; set; }

    /// <summary>Load player 0's rebinding profile on start and save it after every change.</summary>
    [Export] public bool PersistBindings { get; set; } = true;

    /// <summary>Where the demo keeps its rebinding profile.</summary>
    [Export] public string ProfilePath { get; set; } = "user://godot_enhanced_input_demo/bindings.json";

    private const float Speed = 260f;
    private const float SprintFactor = 1.8f;

    private static readonly Color[] PlayerColors = { new("4ade80"), new("60a5fa"), new("f472b6"), new("facc15") };

    private sealed class Avatar
    {
        public InputPlayer Player;
        public Node2D Root;
        public Polygon2D Body;
        public ColorRect ChargeBar;
        public Color Color;
        public Vector2 Facing = Vector2.Right;
        public Vector2 DashVelocity;
        public System.Action<ActionEventArgs> Handler;
    }

    private EnhancedInput _system;
    private bool _ownSystem;
    private JoinMode _previousJoinMode;
    private InputPlayer _player;
    private readonly List<Avatar> _avatars = new();
    private readonly System.Collections.Generic.Dictionary<string, EnhancedInputAction> _actions = new();
    private readonly List<string> _log = new();
    private readonly List<(Node2D Node, Vector2 Velocity, double Life)> _shots = new();

    private Label _panel;
    private Label _logLabel;
    private Label _help;
    private PanelContainer _menu;
    private GridContainer _slotGrid;
    private Control _touchControls;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Gameplay ??= GD.Load<EnhancedInputMappingContext>($"{DemoContent.Folder}/contexts/gameplay.tres");
        Menu ??= GD.Load<EnhancedInputMappingContext>($"{DemoContent.Folder}/contexts/menu.tres");
        CollectActions(Gameplay);
        CollectActions(Menu);

        // Use the autoload when the plugin is enabled; otherwise run a private instance.
        _system = EnhancedInput.Instance;
        if (_system == null)
        {
            _system = new EnhancedInput();
            _ownSystem = true;
            AddChild(_system);
        }

        // Another joypad pressing a button joins as a new player.
        _previousJoinMode = _system.JoinMode;
        _system.JoinMode = JoinMode.OnButtonPress;
        _system.PlayerJoined += OnPlayerJoined;

        _player = _system.GetPlayer(0);
        BuildUi();
        AddAvatar(_player);

        // Rebinding: list gameplay slots in the menu, keep the profile on disk.
        _player.RegisterRebindContext(Gameplay);
        if (PersistBindings) _player.LoadProfile(ProfilePath);
        _player.BindingsChanged += OnBindingsChanged;
        _player.RebindCaptureCanceled += OnCaptureCanceled;

        // Prompts follow the device player 0 last used, and its rebinds.
        _player.InputDeviceChanged += OnInputDeviceChanged;
        UpdateHelp();

        // F3 shows the full state of every player.
        AddChild(new InputDebugOverlay { System = _system });
    }

    private void OnInputDeviceChanged(InputDeviceKind kind, int joypad) => UpdateHelp();

    private void UpdateHelp()
    {
        if (_help == null) return;
        string P(string action) => _player.GetPromptText(A(action), separator: " or ") is { Length: > 0 } text ? text : "—";
        _help.Text =
            $"Move: {P("move")} · Sprint: {P("sprint")} · Jump: {P("jump")} · Dash: {P("dash")} · Spin: Jump, then Dash\n" +
            $"Charge: hold {P("charge")} · Inspect: tap {P("inspect")} · Pick up: hold {P("pick_up")} · Fire: {P("fire")} · " +
            $"Pause & rebind: {P("pause")} · F2: touch controls · F3: debug overlay";
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_system == null || !IsInstanceValid(_system)) return;
        _system.PlayerJoined -= OnPlayerJoined;
        _system.JoinMode = _previousJoinMode;

        _player.BindingsChanged -= OnBindingsChanged;
        _player.RebindCaptureCanceled -= OnCaptureCanceled;
        _player.InputDeviceChanged -= OnInputDeviceChanged;
        _player.CancelCapture();
        _player.UnregisterRebindContext(Gameplay);
        _player.RemoveContext(Menu);

        foreach (var avatar in _avatars)
        {
            if (!IsInstanceValid(avatar.Player)) continue;
            avatar.Player.ActionEventRaised -= avatar.Handler;
            avatar.Player.RemoveContext(Gameplay);
            if (avatar.Player != _player && !_ownSystem) _system.RemovePlayer(avatar.Player);
        }
        _avatars.Clear();
    }

    private void CollectActions(EnhancedInputMappingContext context)
    {
        if (context == null) return;
        foreach (var binding in context.Bindings)
            if (binding?.Action != null) _actions.TryAdd(binding.Action.Name, binding.Action);
    }

    private EnhancedInputAction A(string name) => _actions.TryGetValue(name, out var action) ? action : null;

    // ------------------------------------------------------------------
    //  Players
    // ------------------------------------------------------------------

    private void OnPlayerJoined(InputPlayer player, int device)
    {
        AddLog($"Joypad {device} joined as player {player.PlayerIndex + 1}");
        foreach (var avatar in _avatars)
            if (avatar.Player == player) return;   // player 0 picking up its first joypad
        AddAvatar(player);
    }

    private void AddAvatar(InputPlayer player)
    {
        var size = GetViewportRect().Size;
        var avatar = new Avatar
        {
            Player = player,
            Color = PlayerColors[_avatars.Count % PlayerColors.Length],
            Root = new Node2D { Position = size / 2f + new Vector2(60f * _avatars.Count, 0f) },
        };
        avatar.Body = new Polygon2D
        {
            Polygon = new[] { new Vector2(22, 0), new Vector2(-14, -14), new Vector2(-8, 0), new Vector2(-14, 14) },
            Color = avatar.Color,
        };
        avatar.ChargeBar = new ColorRect { Color = new Color("f97316"), Position = new Vector2(-30, 26), Size = Vector2.Zero };
        avatar.Root.AddChild(avatar.Body);
        avatar.Root.AddChild(avatar.ChargeBar);
        AddChild(avatar.Root);

        avatar.Handler = e => OnActionEvent(avatar, e);
        player.ActionEventRaised += avatar.Handler;
        player.AddContext(Gameplay, priority: 0);
        _avatars.Add(avatar);
    }

    // ------------------------------------------------------------------
    //  Reacting to actions
    // ------------------------------------------------------------------

    private void OnActionEvent(Avatar avatar, ActionEventArgs e)
    {
        if (e.Event != ActionEvents.Ongoing)
            AddLog($"P{avatar.Player.PlayerIndex + 1} {e.Action.GetDisplayName()}: {e.Event}"
                   + (e.Event is ActionEvents.Completed or ActionEvents.Canceled ? $" after {e.ElapsedSeconds:0.00}s" : ""));

        if (e.Event != ActionEvents.Triggered) return;
        switch (e.Action.Name)
        {
            case "jump": Bounce(avatar, 1.35f); break;
            case "dash": avatar.DashVelocity = avatar.Facing * 900f; break;
            case "charge": Bounce(avatar, 1.8f); break;
            case "inspect": Flash(avatar, new Color("60a5fa")); break;
            case "pick_up": Flash(avatar, new Color("facc15")); break;
            case "fire": Shoot(avatar); break;
            case "spin": Spin(avatar); break;
            case "toggle_touch": _touchControls.Visible = !_touchControls.Visible; break;
            // The pause menu belongs to player 0.
            case "pause" when avatar.Player == _player: OpenMenu(); break;
            case "resume" when avatar.Player == _player: CloseMenu(); break;
        }
    }

    private void OpenMenu()
    {
        // The Escape press that opened the menu is still held: the menu ignores it until released.
        _player.AddContext(Menu, priority: 100, new ContextOptions { IgnoreHeldSourcesUntilReleased = true });
        RefreshMenu();
        _menu.Visible = true;
    }

    private void CloseMenu()
    {
        _player.CancelCapture();
        _player.RemoveContext(Menu);
        _menu.Visible = false;
    }

    // ------------------------------------------------------------------
    //  Rebinding menu
    // ------------------------------------------------------------------

    private void OnBindingsChanged()
    {
        if (PersistBindings) _player.SaveProfile(ProfilePath);
        RefreshMenu();
        UpdateHelp();
    }

    private void OnCaptureCanceled(string id) => RefreshMenu();

    private void RefreshMenu()
    {
        if (_slotGrid == null) return;
        foreach (var child in _slotGrid.GetChildren()) { _slotGrid.RemoveChild(child); child.QueueFree(); }

        foreach (var slot in _player.GetRebindSlots())
        {
            _slotGrid.AddChild(new Label { Text = $"{slot.DisplayName}  ({slot.Category})" });
            var id = slot.Id;
            var button = new Button
            {
                Text = _player.CaptureId == id ? "press an input…  (Esc cancels)" : (slot.CurrentSource?.Describe() ?? "—") + (slot.IsOverridden ? "  *" : ""),
                CustomMinimumSize = new Vector2(240, 0),
            };
            button.Pressed += () => StartRebind(id);
            _slotGrid.AddChild(button);
        }
    }

    private void StartRebind(string id)
    {
        // Swap: if the new input is already used by another slot, that slot takes this one's old input.
        _player.StartCapture(id, new CaptureOptions { ConflictPolicy = RebindConflictPolicy.Swap });
        RefreshMenu();
    }

    // ------------------------------------------------------------------
    //  Frame update
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_player == null) return;
        var bounds = GetViewportRect().Size;

        foreach (var avatar in _avatars)
        {
            var input = avatar.Player;
            // Polling works as well as events: read the value every frame.
            var move = input.GetValue(A("move")).AsVector2();
            if (move != Vector2.Zero) avatar.Facing = move.Normalized();
            float speed = Speed * (input.IsTriggered(A("sprint")) ? SprintFactor : 1f);
            avatar.Root.Position = (avatar.Root.Position + (move * speed + avatar.DashVelocity) * (float)delta).Clamp(Vector2.Zero, bounds);
            avatar.DashVelocity = avatar.DashVelocity.MoveToward(Vector2.Zero, 3000f * (float)delta);
            avatar.Body.Rotation = Mathf.LerpAngle(avatar.Body.Rotation, avatar.Facing.Angle(), 0.3f);

            var charge = input.GetState(A("charge"));
            avatar.ChargeBar.Size = new Vector2(charge.Phase == TriggerPhase.Ongoing ? (float)(charge.ElapsedSeconds / 0.6) * 60f : 0f, 6f);
        }

        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var (node, velocity, life) = _shots[i];
            node.Position += velocity * (float)delta;
            life -= delta;
            if (life <= 0) { node.QueueFree(); _shots.RemoveAt(i); }
            else _shots[i] = (node, velocity, life);
        }

        _panel.Text = DescribeActions();
    }

    private string DescribeActions()
    {
        var text = new StringBuilder();
        text.AppendLine($"Players: {_avatars.Count}   (another joypad: press a button to join)");
        text.AppendLine("Player 1 contexts: " + string.Join(", ", _player.GetContexts().ConvertAll(c => c.Name)));
        text.AppendLine();
        foreach (var action in _actions.Values)
        {
            var state = _player.GetState(action);
            text.Append($"{action.GetDisplayName(),-12} {state.Phase,-9} {state.Value,-16}");
            if (state.Phase != TriggerPhase.Idle) text.Append($" {state.ElapsedSeconds:0.00}s");
            text.AppendLine();
        }
        return text.ToString();
    }

    private void AddLog(string line)
    {
        _log.Add(line);
        if (_log.Count > 12) _log.RemoveAt(0);
        if (_logLabel != null) _logLabel.Text = string.Join("\n", _log);
    }

    // ------------------------------------------------------------------
    //  Visuals
    // ------------------------------------------------------------------

    private void BuildUi()
    {
        var size = GetViewportRect().Size;
        var ui = new CanvasLayer();
        AddChild(ui);

        _panel = new Label { Position = new Vector2(12, 12) };
        _panel.AddThemeFontSizeOverride("font_size", 13);
        ui.AddChild(_panel);

        _logLabel = new Label { Position = new Vector2(12, size.Y - 230) };
        _logLabel.AddThemeFontSizeOverride("font_size", 13);
        ui.AddChild(_logLabel);

        _help = new Label { Position = new Vector2(12, size.Y - 46) };
        _help.AddThemeFontSizeOverride("font_size", 13);
        ui.AddChild(_help);

        // On-screen controls for player 0: shown on touchscreens, or with F2.
        _touchControls = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = DisplayServer.IsTouchscreenAvailable() };
        _touchControls.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_touchControls);
        _touchControls.AddChild(new VirtualJoystick
        {
            ControlName = "touch_move", Floating = true, Radius = 60,
            Position = new Vector2(20, size.Y * 0.45f), Size = new Vector2(size.X * 0.4f, size.Y * 0.5f),
        });
        AddTouchButton("touch_jump", "Jump", new Vector2(size.X - 110, size.Y - 190));
        AddTouchButton("touch_dash", "Dash", new Vector2(size.X - 200, size.Y - 130));
        AddTouchButton("touch_fire", "Fire", new Vector2(size.X - 110, size.Y - 290));

        _menu = new PanelContainer { Visible = false, Position = new Vector2(size.X / 2f - 260f, 60f) };
        ui.AddChild(_menu);
        var column = new VBoxContainer();
        _menu.AddChild(column);
        column.AddChild(new Label { Text = "PAUSED — the menu context blocks gameplay. Esc resumes." });
        column.AddChild(new Label { Text = "Click a binding, then press a key, mouse button or pad input. * = changed." });
        _slotGrid = new GridContainer { Columns = 2 };
        column.AddChild(_slotGrid);
        var reset = new Button { Text = "Reset all to defaults" };
        reset.Pressed += () => _player.ResetAllRebinds();
        column.AddChild(reset);
    }

    private void AddTouchButton(string controlName, string text, Vector2 position)
        => _touchControls.AddChild(new VirtualButton { ControlName = controlName, Text = text, Position = position, Size = new Vector2(84, 84) });

    private void Bounce(Avatar avatar, float scale)
    {
        var tween = CreateTween();
        tween.TweenProperty(avatar.Body, "scale", Vector2.One * scale, 0.08);
        tween.TweenProperty(avatar.Body, "scale", Vector2.One, 0.15);
    }

    private void Flash(Avatar avatar, Color color)
    {
        var tween = CreateTween();
        tween.TweenProperty(avatar.Body, "color", color, 0.05);
        tween.TweenProperty(avatar.Body, "color", avatar.Color, 0.3);
    }

    private void Spin(Avatar avatar)
    {
        var tween = CreateTween();
        tween.TweenProperty(avatar.Root, "rotation", avatar.Root.Rotation + Mathf.Tau, 0.35);
    }

    private void Shoot(Avatar avatar)
    {
        var shot = new Polygon2D
        {
            Polygon = new[] { new Vector2(-4, -2), new Vector2(4, -2), new Vector2(4, 2), new Vector2(-4, 2) },
            Color = new Color("fde68a"),
            Position = avatar.Root.Position + avatar.Facing * 24f,
            Rotation = avatar.Facing.Angle(),
        };
        AddChild(shot);
        _shots.Add((shot, avatar.Facing * 700f, 1.0));
    }
}
