using System.Collections.Generic;
using System.Text;
using Godot;

namespace GodotEnhancedInput.Demo;

/// <summary>
/// A small top-down playground for Godot Enhanced Input. Everything visible is built here in code;
/// the input itself is data: the two contexts (gameplay.tres, menu.tres) and the actions they bind.
/// The panel on the left lists every action's phase, value and timings; the log shows events.
/// </summary>
public partial class Demo : Node2D
{
    /// <summary>On-foot controls. Defaults to the demo's gameplay.tres.</summary>
    [Export] public BindingContext Gameplay { get; set; }

    /// <summary>Pause menu. Defaults to the demo's menu.tres.</summary>
    [Export] public BindingContext Menu { get; set; }

    private const float Speed = 260f;
    private const float SprintFactor = 1.8f;

    private InputPlayer _player;
    private EnhancedInput _ownSystem;
    private readonly System.Collections.Generic.Dictionary<string, GameAction> _actions = new();
    private readonly List<string> _log = new();
    private readonly List<(Node2D Node, Vector2 Velocity, double Life)> _shots = new();

    private Node2D _avatar;
    private Polygon2D _body;
    private ColorRect _chargeBar;
    private Label _panel;
    private Label _logLabel;
    private Label _menuLabel;
    private Vector2 _facing = Vector2.Right;
    private Vector2 _dashVelocity;

    public override void _Ready()
    {
        Gameplay ??= GD.Load<BindingContext>($"{DemoContent.Folder}/contexts/gameplay.tres");
        Menu ??= GD.Load<BindingContext>($"{DemoContent.Folder}/contexts/menu.tres");
        CollectActions(Gameplay);
        CollectActions(Menu);

        // Use the autoload when the plugin is enabled; otherwise run a private instance.
        var system = EnhancedInput.Instance;
        if (system == null)
        {
            _ownSystem = system = new EnhancedInput();
            AddChild(_ownSystem);
        }
        _player = system.GetPlayer(0);
        _player.AddContext(Gameplay, priority: 0);
        _player.ActionEventRaised += OnActionEvent;

        BuildScene();
    }

    public override void _ExitTree()
    {
        if (_player == null || !IsInstanceValid(_player)) return;
        _player.ActionEventRaised -= OnActionEvent;
        _player.RemoveContext(Gameplay);
        _player.RemoveContext(Menu);
    }

    private void CollectActions(BindingContext context)
    {
        if (context == null) return;
        foreach (var binding in context.Bindings)
            if (binding?.Action != null) _actions.TryAdd(binding.Action.Name, binding.Action);
    }

    private GameAction A(string name) => _actions.TryGetValue(name, out var action) ? action : null;

    // ------------------------------------------------------------------
    //  Reacting to actions
    // ------------------------------------------------------------------

    private void OnActionEvent(ActionEventArgs e)
    {
        if (e.Event != ActionEvents.Ongoing)
            AddLog($"{e.Action.GetDisplayName()}: {e.Event}" + (e.Event == ActionEvents.Completed || e.Event == ActionEvents.Canceled ? $" after {e.ElapsedSeconds:0.00}s" : ""));

        if (e.Event != ActionEvents.Triggered) return;
        switch (e.Action.Name)
        {
            case "jump": Bounce(1.35f); break;
            case "dash": _dashVelocity = _facing * 900f; break;
            case "charge": Bounce(1.8f); break;
            case "inspect": Flash(new Color("60a5fa")); break;
            case "pick_up": Flash(new Color("facc15")); break;
            case "fire": Shoot(); break;
            case "spin": Spin(); break;
            case "pause": OpenMenu(); break;
            case "resume": CloseMenu(); break;
        }
    }

    private void OpenMenu()
    {
        // The Escape press that opened the menu is still held: the menu ignores it until released.
        _player.AddContext(Menu, priority: 100, new ContextOptions { IgnoreHeldSourcesUntilReleased = true });
        _menuLabel.Visible = true;
    }

    private void CloseMenu()
    {
        _player.RemoveContext(Menu);
        _menuLabel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_player == null) return;

        // Polling works as well as events: read the value every frame.
        var move = _player.GetValue(A("move")).AsVector2();
        if (move != Vector2.Zero) _facing = move.Normalized();
        float speed = Speed * (_player.IsTriggered(A("sprint")) ? SprintFactor : 1f);
        _avatar.Position += (move * speed + _dashVelocity) * (float)delta;
        _dashVelocity = _dashVelocity.MoveToward(Vector2.Zero, 3000f * (float)delta);
        _avatar.Position = _avatar.Position.Clamp(Vector2.Zero, GetViewportRect().Size);
        _body.Rotation = Mathf.LerpAngle(_body.Rotation, _facing.Angle(), 0.3f);

        var charge = _player.GetState(A("charge"));
        _chargeBar.Size = new Vector2(charge.Phase == TriggerPhase.Ongoing ? (float)(charge.ElapsedSeconds / 0.6) * 60f : 0f, 6f);

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
        text.AppendLine("Contexts: " + string.Join(", ", _player.GetContexts().ConvertAll(c => c.Name)));
        text.AppendLine();
        foreach (var action in _actions.Values)
        {
            var state = _player.GetState(action);
            text.Append($"{action.GetDisplayName(),-8} {state.Phase,-9} {state.Value,-16}");
            if (state.Phase != TriggerPhase.Idle) text.Append($" {state.ElapsedSeconds:0.00}s");
            text.AppendLine();
        }
        return text.ToString();
    }

    private void AddLog(string line)
    {
        _log.Add(line);
        if (_log.Count > 12) _log.RemoveAt(0);
        _logLabel.Text = string.Join("\n", _log);
    }

    // ------------------------------------------------------------------
    //  Visuals
    // ------------------------------------------------------------------

    private void BuildScene()
    {
        var size = GetViewportRect().Size;

        _avatar = new Node2D { Position = size / 2f };
        AddChild(_avatar);
        _body = new Polygon2D
        {
            Polygon = new[] { new Vector2(22, 0), new Vector2(-14, -14), new Vector2(-8, 0), new Vector2(-14, 14) },
            Color = new Color("4ade80"),
        };
        _avatar.AddChild(_body);
        _chargeBar = new ColorRect { Color = new Color("f97316"), Position = new Vector2(-30, 26), Size = Vector2.Zero };
        _avatar.AddChild(_chargeBar);

        var ui = new CanvasLayer();
        AddChild(ui);
        _panel = new Label { Position = new Vector2(12, 12) };
        _panel.AddThemeFontSizeOverride("font_size", 13);
        ui.AddChild(_panel);

        _logLabel = new Label { Position = new Vector2(12, size.Y - 230) };
        _logLabel.AddThemeFontSizeOverride("font_size", 13);
        ui.AddChild(_logLabel);

        var help = new Label
        {
            Text = "WASD / stick: move · Shift: sprint · Space: jump · Shift+Space: dash · Jump→Dash: spin\n" +
                   "Hold E: charge · Tap F: inspect · Hold F: pick up · Mouse/RT: fire · Esc/Start: pause",
            Position = new Vector2(12, size.Y - 46),
        };
        help.AddThemeFontSizeOverride("font_size", 13);
        ui.AddChild(help);

        _menuLabel = new Label
        {
            Text = "PAUSED — the menu context blocks gameplay. Esc to resume.",
            Position = new Vector2(size.X / 2f - 200f, size.Y / 2f - 80f),
            Visible = false,
        };
        ui.AddChild(_menuLabel);
    }

    private void Bounce(float scale)
    {
        var tween = CreateTween();
        tween.TweenProperty(_body, "scale", Vector2.One * scale, 0.08);
        tween.TweenProperty(_body, "scale", Vector2.One, 0.15);
    }

    private void Flash(Color color)
    {
        var tween = CreateTween();
        tween.TweenProperty(_body, "color", color, 0.05);
        tween.TweenProperty(_body, "color", new Color("4ade80"), 0.3);
    }

    private void Spin()
    {
        var tween = CreateTween();
        tween.TweenProperty(_avatar, "rotation", _avatar.Rotation + Mathf.Tau, 0.35);
    }

    private void Shoot()
    {
        var shot = new Polygon2D
        {
            Polygon = new[] { new Vector2(-4, -2), new Vector2(4, -2), new Vector2(4, 2), new Vector2(-4, 2) },
            Color = new Color("fde68a"),
            Position = _avatar.Position + _facing * 24f,
            Rotation = _facing.Angle(),
        };
        AddChild(shot);
        _shots.Add((shot, _facing * 700f, 1.0));
    }
}
