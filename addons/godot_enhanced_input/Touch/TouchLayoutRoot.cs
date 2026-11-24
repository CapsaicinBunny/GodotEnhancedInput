using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Holds on-screen controls and lets players arrange them. <see cref="Apply"/> places every child
/// <see cref="TouchControl"/> named in the <see cref="Layout"/>; <see cref="Capture"/> records the current
/// arrangement. In <see cref="EditMode"/>, controls stop acting and can be dragged with a finger or the
/// mouse; <see cref="Capture"/> then stores where they ended up. Save the layout per player with
/// <see cref="TouchLayout.SaveJson"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class TouchLayoutRoot : Control
{
    private bool _editMode;
    private TouchControl _dragging;
    private int _dragPointer = int.MinValue;

    /// <summary>The layout applied on ready and by <see cref="Apply"/>.</summary>
    [Export] public TouchLayout Layout { get; set; }

    /// <summary>While on, controls can be moved instead of used.</summary>
    [Export]
    public bool EditMode
    {
        get => _editMode;
        set
        {
            _editMode = value;
            foreach (var control in Controls()) control.Editing = value;
            if (!value) { _dragging = null; _dragPointer = int.MinValue; }
        }
    }

    public TouchLayoutRoot()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        if (Layout != null) Apply();
    }

    /// <summary>The child controls arranged by this root.</summary>
    public List<TouchControl> Controls()
    {
        var list = new List<TouchControl>();
        foreach (var child in GetChildren())
            if (child is TouchControl control) list.Add(control);
        return list;
    }

    private Vector2 AreaSize => Size.X > 0 && Size.Y > 0 ? Size : GetViewportRect().Size;

    /// <summary>Places the controls named in the layout, scales them and sets their opacity.</summary>
    public void Apply()
    {
        if (Layout == null) return;
        var area = AreaSize;
        foreach (var control in Controls())
        {
            if (Layout.Rects.TryGetValue(control.Name, out var rect))
            {
                var size = rect.Size * area * Layout.Scale;
                var center = (rect.Position + rect.Size * 0.5f) * area;
                control.Position = center - size * 0.5f;
                control.Size = size;
            }
            control.Modulate = new Color(control.Modulate, Layout.Opacity);
            if (control.KeepInSafeArea && control.IsInsideTree()) control.FitToSafeArea();
        }
    }

    /// <summary>Stores the current position and size of every control in the layout (creating one if needed).</summary>
    public TouchLayout Capture()
    {
        Layout ??= new TouchLayout();
        var area = AreaSize;
        if (area.X <= 0 || area.Y <= 0) return Layout;
        float scale = Layout.Scale <= 0 ? 1f : Layout.Scale;
        foreach (var control in Controls())
        {
            var size = control.Size / scale;
            var center = control.Position + control.Size * 0.5f;
            var unscaledPosition = center - size * 0.5f;
            Layout.Rects[control.Name] = new Rect2(unscaledPosition / area, size / area);
        }
        return Layout;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_editMode || Engine.IsEditorHint()) return;
        if (HandleEditEvent(@event)) GetViewport().SetInputAsHandled();
    }

    /// <summary>In edit mode, drags controls (public so tests can drive it).</summary>
    public bool HandleEditEvent(InputEvent @event)
    {
        if (!_editMode) return false;
        switch (@event)
        {
            case InputEventScreenTouch { Pressed: true } touch: return BeginDrag(touch.Index, touch.Position);
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse: return BeginDrag(TouchControl.MousePointer, mouse.Position);
            case InputEventScreenDrag drag when drag.Index == _dragPointer: return MoveDrag(drag.Relative);
            case InputEventMouseMotion motion when _dragPointer == TouchControl.MousePointer: return MoveDrag(motion.Relative);
            case InputEventScreenTouch { Pressed: false } up when up.Index == _dragPointer: return EndDrag();
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } when _dragPointer == TouchControl.MousePointer: return EndDrag();
        }
        return false;
    }

    private bool BeginDrag(int pointer, Vector2 position)
    {
        if (_dragging != null) return false;
        var controls = Controls();
        for (int i = controls.Count - 1; i >= 0; i--)
        {
            if (!controls[i].GetGlobalRect().HasPoint(position)) continue;
            _dragging = controls[i];
            _dragPointer = pointer;
            return true;
        }
        return false;
    }

    private bool MoveDrag(Vector2 relative)
    {
        if (_dragging == null) return false;
        _dragging.Position += relative;
        return true;
    }

    private bool EndDrag()
    {
        if (_dragging == null) return false;
        if (_dragging.KeepInSafeArea) _dragging.FitToSafeArea();
        _dragging = null;
        _dragPointer = int.MinValue;
        return true;
    }
}
