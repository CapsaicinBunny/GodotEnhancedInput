using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Base for on-screen controls. Follows fingers that start inside the control (and, optionally, the
/// left mouse button for testing on desktop), writes to a named virtual control of a player, and
/// marks the events it uses as handled so they do not also act as gameplay touches or clicks. Bind
/// the control's name with an <see cref="InputSource"/> of kind <see cref="SourceKind.Virtual"/>.
/// </summary>
[Tool]
[GlobalClass]
public abstract partial class TouchControl : Control
{
    /// <summary>Pointer id used for the mouse.</summary>
    public const int MousePointer = -100;

    /// <summary>The player this control writes to (when <see cref="TargetPlayer"/> is not set).</summary>
    [Export(PropertyHint.Range, "0,15,1")] public int PlayerIndex { get; set; }

    /// <summary>The virtual control name this control writes, e.g. "touch_move".</summary>
    [Export] public string ControlName { get; set; } = string.Empty;

    /// <summary>Also react to the left mouse button (real mouse only, not touch-emulated clicks).</summary>
    [Export] public bool AcceptMouse { get; set; } = true;

    /// <summary>When the control is shown.</summary>
    [Export] public TouchControlVisibility Visibility { get; set; } = TouchControlVisibility.Always;

    /// <summary>The player to write to; overrides <see cref="PlayerIndex"/> (useful in tests and split screen).</summary>
    public InputPlayer TargetPlayer { get; set; }

    /// <summary>Pointers (finger indices, or <see cref="MousePointer"/>) this control is following.</summary>
    protected readonly HashSet<int> Pointers = new();

    /// <summary>The player written to, or null.</summary>
    protected InputPlayer Player => TargetPlayer != null && IsInstanceValid(TargetPlayer) ? TargetPlayer : EnhancedInput.Instance?.GetPlayer(PlayerIndex);

    /// <summary>Keep the control inside the screen's safe area (notches, rounded corners, system bars). On by default.</summary>
    [Export] public bool KeepInSafeArea { get; set; } = true;

    /// <summary>While true, the control does not act; a <see cref="TouchLayoutRoot"/> in edit mode moves it instead.</summary>
    public bool Editing { get; set; }

    /// <summary>Overrides the safe area, in viewport pixels (tests, custom layouts).</summary>
    public Rect2? SafeAreaOverride { get; set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Input is read in _Input, not through the GUI, so several fingers work at once.
        MouseFilter = MouseFilterEnum.Ignore;
        if (!Engine.IsEditorHint() && Visibility == TouchControlVisibility.TouchscreenOnly && !DisplayServer.IsTouchscreenAvailable())
            Hide();
        if (!Engine.IsEditorHint() && KeepInSafeArea) CallDeferred(MethodName.FitToSafeArea);
    }

    /// <summary>The safe area in viewport pixels (the whole viewport when the platform reports none).</summary>
    public Rect2 GetSafeArea()
    {
        if (SafeAreaOverride.HasValue) return SafeAreaOverride.Value;
        var viewport = GetViewportRect();
        if (Engine.IsEditorHint() || !IsInsideTree()) return viewport;
        var safe = DisplayServer.GetDisplaySafeArea();
        var screen = DisplayServer.ScreenGetSize();
        if (safe.Size.X <= 0 || safe.Size.Y <= 0 || screen.X <= 0 || screen.Y <= 0) return viewport;
        // Screen pixels → viewport pixels (stretch modes scale the viewport).
        var scale = viewport.Size / new Vector2(screen.X, screen.Y);
        return new Rect2(new Vector2(safe.Position.X, safe.Position.Y) * scale, new Vector2(safe.Size.X, safe.Size.Y) * scale).Intersection(viewport);
    }

    /// <summary>Moves (and if needed shrinks) the control so it lies inside the safe area.</summary>
    public void FitToSafeArea()
    {
        var safe = GetSafeArea();
        if (safe.Size.X <= 0 || safe.Size.Y <= 0) return;
        var rect = GetGlobalRect();
        var size = new Vector2(Mathf.Min(rect.Size.X, safe.Size.X), Mathf.Min(rect.Size.Y, safe.Size.Y));
        var position = new Vector2(
            Mathf.Clamp(rect.Position.X, safe.Position.X, safe.End.X - size.X),
            Mathf.Clamp(rect.Position.Y, safe.Position.Y, safe.End.Y - size.Y));
        if (size != rect.Size) Size = size;
        if (position != rect.Position) GlobalPosition = position;
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;
        if (HandlePointerEvent(@event)) GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        // Hidden, removed, or the app lost focus / was suspended: fingers may never send their release.
        if (what == NotificationVisibilityChanged && !IsVisibleInTree() || what == NotificationExitTree)
            ReleaseAll();
        else if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut || what == NotificationApplicationPaused)
        {
            // Fingers lost to focus loss or suspension were not lifted: cut the control off, do not release it.
            bool held = Pointers.Count > 0;
            ReleaseAll();
            if (held && !string.IsNullOrEmpty(ControlName))
                Player?.CancelVirtualControl(ControlName, what == NotificationApplicationPaused ? InputEndReason.ApplicationSuspended : InputEndReason.FocusLost);
        }
    }

    /// <summary>
    /// Processes a touch or mouse event (positions in viewport pixels). Returns true when the control
    /// used it. Called from <c>_Input</c>; public so tests and custom routing can drive it.
    /// </summary>
    public bool HandlePointerEvent(InputEvent @event)
    {
        if (!IsVisibleInTree() || Editing) return false;

        switch (@event)
        {
            case InputEventScreenTouch touch:
                return Pointer(touch.Index, touch.Position, touch.Pressed && !touch.Canceled, isPress: true, Vector2.Zero);
            case InputEventScreenDrag drag:
                return Pointer(drag.Index, drag.Position, true, isPress: false, drag.Relative);
            case InputEventMouseButton mouse when AcceptMouse && mouse.ButtonIndex == MouseButton.Left && mouse.Device != InputEvent.DeviceIdEmulation:
                return Pointer(MousePointer, mouse.Position, mouse.Pressed, isPress: true, Vector2.Zero);
            case InputEventMouseMotion motion when AcceptMouse && motion.Device != InputEvent.DeviceIdEmulation:
                return Pointer(MousePointer, motion.Position, true, isPress: false, motion.Relative);
        }
        return false;
    }

    private bool Pointer(int id, Vector2 viewportPosition, bool down, bool isPress, Vector2 relative)
    {
        var local = GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        bool following = Pointers.Contains(id);

        if (isPress && down)
        {
            if (following || !new Rect2(Vector2.Zero, Size).HasPoint(local)) return following;
            if (!OnPointerDown(id, local)) return false;
            Pointers.Add(id);
            QueueRedraw();
            return true;
        }

        if (!following) return false;

        if (isPress)
        {
            Pointers.Remove(id);
            OnPointerUp(id);
        }
        else
        {
            OnPointerMove(id, local, relative);
        }
        QueueRedraw();
        return true;
    }

    /// <summary>Stops following every pointer (as if they were all released).</summary>
    public void ReleaseAll()
    {
        if (Pointers.Count == 0) return;
        foreach (var id in new List<int>(Pointers))
        {
            Pointers.Remove(id);
            OnPointerUp(id);
        }
        QueueRedraw();
    }

    /// <summary>A pointer touched down inside the control. Return false to ignore it.</summary>
    protected abstract bool OnPointerDown(int pointer, Vector2 localPosition);

    /// <summary>A followed pointer moved.</summary>
    protected virtual void OnPointerMove(int pointer, Vector2 localPosition, Vector2 relative) { }

    /// <summary>A followed pointer was released.</summary>
    protected abstract void OnPointerUp(int pointer);
}
