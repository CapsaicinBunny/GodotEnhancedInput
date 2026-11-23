using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// A debug overlay: shows <see cref="InputPlayer.DescribeState"/> for one player or all of them,
/// refreshed every frame, with the most recent action events underneath. Toggle it with
/// <see cref="ToggleKey"/> (F3 by default); the key is not consumed. Drop it into any scene.
/// </summary>
[GlobalClass]
public partial class InputDebugOverlay : CanvasLayer
{
    /// <summary>Which player to show; −1 for all.</summary>
    [Export(PropertyHint.Range, "-1,15,1")] public int PlayerIndex { get; set; } = -1;

    /// <summary>Physical key that shows and hides the overlay. <see cref="Key.None"/> disables it.</summary>
    [Export] public Key ToggleKey { get; set; } = Key.F3;

    /// <summary>Whether the overlay starts visible.</summary>
    [Export] public bool StartVisible { get; set; }

    /// <summary>List only actions that are not idle.</summary>
    [Export] public bool OnlyActiveActions { get; set; }

    /// <summary>How many recent events to keep.</summary>
    [Export(PropertyHint.Range, "0,100,1")] public int HistoryLength { get; set; } = 12;

    [Export] public int FontSize { get; set; } = 12;

    /// <summary>The system to show; defaults to <see cref="EnhancedInput.Instance"/>.</summary>
    public EnhancedInput System { get; set; }

    /// <summary>The text shown (also when hidden, after <see cref="Refresh"/>).</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>Recent events, oldest first.</summary>
    public IReadOnlyList<string> History => _history;

    private readonly List<string> _history = new();
    private readonly HashSet<InputPlayer> _watched = new();
    private readonly Dictionary<InputPlayer, Action<ActionEventArgs>> _handlers = new();
    private PanelContainer _panel;
    private Label _label;
    private EnhancedInput _connectedSystem;

    public InputDebugOverlay()
    {
        Layer = 100;
    }

    public override void _Ready()
    {
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(8, 8) };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.7f), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 });
        _label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        var mono = new SystemFont { FontNames = new[] { "monospace", "Consolas", "Menlo", "DejaVu Sans Mono" } };
        _label.AddThemeFontOverride("font", mono);
        _panel.AddChild(_label);
        AddChild(_panel);
        Visible = StartVisible;
    }

    public override void _ExitTree()
    {
        foreach (var pair in _handlers)
            if (IsInstanceValid(pair.Key)) pair.Key.ActionEventRaised -= pair.Value;
        _handlers.Clear();
        _watched.Clear();
        if (_connectedSystem != null && IsInstanceValid(_connectedSystem)) _connectedSystem.PlayerAdded -= Watch;
        _connectedSystem = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (ToggleKey != Key.None && @event is InputEventKey { Pressed: true, Echo: false } key
            && (key.PhysicalKeycode == ToggleKey || key.PhysicalKeycode == Key.None && key.Keycode == ToggleKey))
            Visible = !Visible;
    }

    public override void _Process(double delta)
    {
        if (Visible) Refresh();
    }

    /// <summary>Rebuilds <see cref="Text"/> (and the label) from the current state.</summary>
    public void Refresh()
    {
        var system = System ?? EnhancedInput.Instance;
        if (system == null || !IsInstanceValid(system))
        {
            Text = "No EnhancedInput instance.";
        }
        else
        {
            ConnectTo(system);
            var text = new StringBuilder();
            if (PlayerIndex < 0) text.Append(system.DescribeState(OnlyActiveActions));
            else text.Append(system.GetPlayer(PlayerIndex)?.DescribeState(OnlyActiveActions) ?? $"No player {PlayerIndex}.\n");

            if (HistoryLength > 0 && _history.Count > 0)
            {
                text.AppendLine("recent events:");
                foreach (var line in _history) text.Append("    ").AppendLine(line);
            }
            Text = text.ToString();
        }
        if (_label != null) _label.Text = Text;
    }

    private void ConnectTo(EnhancedInput system)
    {
        if (_connectedSystem != system)
        {
            if (_connectedSystem != null && IsInstanceValid(_connectedSystem)) _connectedSystem.PlayerAdded -= Watch;
            _connectedSystem = system;
            system.PlayerAdded += Watch;
        }
        foreach (var player in system.Players) Watch(player);
    }

    private void Watch(InputPlayer player)
    {
        if (player == null || !_watched.Add(player)) return;
        Action<ActionEventArgs> handler = e => Record(e);
        _handlers[player] = handler;
        player.ActionEventRaised += handler;
    }

    private void Record(ActionEventArgs e)
    {
        if (e.Event == ActionEvents.Ongoing || HistoryLength <= 0) return;
        if (PlayerIndex >= 0 && e.Player.PlayerIndex != PlayerIndex) return;
        _history.Add($"P{e.Player.PlayerIndex} t{e.Player.TickCount} {e.Action?.Name} {e.Event} {e.Value}");
        while (_history.Count > HistoryLength) _history.RemoveAt(0);
    }
}
