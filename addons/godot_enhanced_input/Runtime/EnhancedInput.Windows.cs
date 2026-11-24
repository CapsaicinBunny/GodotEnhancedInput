using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Input from more windows than the main one.</summary>
public partial class EnhancedInput
{
    private readonly List<Window> _windows = new();
    private bool _focusCheckQueued;

    /// <summary>The extra windows whose input reaches the players (see <see cref="AttachWindow"/>).</summary>
    public IReadOnlyList<Window> AttachedWindows => _windows;

    /// <summary>
    /// Lets a window's input reach the players: a native window's events never pass the main window, so
    /// without this they are not seen. A small relay node is added to the window; it hands the window's
    /// events over in the same GUI-aware way as the main window. With <paramref name="players"/>, only those
    /// players get the window's events (a second screen for player 2, a tool window). Focus moving between
    /// the game's own windows no longer counts as losing focus.
    /// </summary>
    public void AttachWindow(Window window, params InputPlayer[] players)
    {
        if (window == null || _windows.Contains(window)) return;
        _windows.Add(window);
        var relay = new WindowInputRelay { System = this, Players = players ?? global::System.Array.Empty<InputPlayer>(), Name = "EnhancedInputRelay" };
        window.AddChild(relay, forceReadableName: true, InternalMode.Front);
        window.TreeExiting += () => _windows.Remove(window);
    }

    /// <summary>Stops a window's input from reaching the players.</summary>
    public void DetachWindow(Window window)
    {
        if (window == null || !_windows.Remove(window) || !IsInstanceValid(window)) return;
        foreach (var child in window.GetChildren(includeInternal: true))
            if (child is WindowInputRelay relay) relay.QueueFree();
    }

    internal void QueueFocusCheck()
    {
        if (_focusCheckQueued) return;
        _focusCheckQueued = true;
        CallDeferred(MethodName.CheckFocus);
    }

    private void CheckFocus()
    {
        _focusCheckQueued = false;
        if (IsInsideTree() && GetWindow() is { } main && main.HasFocus()) return;
        foreach (var window in _windows)
            if (IsInstanceValid(window) && window.HasFocus()) return;
        if (StopHapticsWhenInactive)
            foreach (var player in _players) player.ReleaseAllHapticOutputs();
        if (ResetInputOnFocusLoss) ResetAllInput(InputEndReason.FocusLost);
    }
}

/// <summary>Hands one window's input to <see cref="EnhancedInput"/> (added by <see cref="EnhancedInput.AttachWindow"/>).</summary>
public partial class WindowInputRelay : Node
{
    /// <summary>The system the events go to.</summary>
    public EnhancedInput System { get; set; }

    /// <summary>The players the window belongs to (empty: any).</summary>
    public IReadOnlyCollection<InputPlayer> Players { get; set; } = global::System.Array.Empty<InputPlayer>();

    /// <summary>Creates a relay.</summary>
    public WindowInputRelay() => ProcessMode = ProcessModeEnum.Always;

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (System == null || !IsInstanceValid(System)) return;
        bool captured = System.FeedEvent(@event, System.GuiPolicy == GuiPolicy.ReadEverything ? IntakeStage.Direct : IntakeStage.Early, Players);
        if (captured) GetViewport()?.SetInputAsHandled();
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (System != null && IsInstanceValid(System) && System.GuiPolicy == GuiPolicy.RespectGui)
            System.FeedEvent(@event, IntakeStage.Unhandled, Players);
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && System != null && IsInstanceValid(System)) System.QueueFocusCheck();
    }
}
