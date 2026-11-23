using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// An invisible area — say the right half of the screen — that turns a drag starting inside it into
/// per-tick movement on its virtual control (pixels × <see cref="Sensitivity"/>), for camera look.
/// Optionally writes 1 to <see cref="PressedControlName"/> while touched.
/// </summary>
[Tool]
[GlobalClass]
public partial class TouchZone : TouchControl
{
    private int _pointer = int.MinValue;

    /// <summary>Multiplier from pixels dragged to the value added.</summary>
    [Export] public float Sensitivity { get; set; } = 1f;

    /// <summary>Optional virtual control set to 1 while the zone is touched.</summary>
    [Export] public string PressedControlName { get; set; } = string.Empty;

    /// <summary>Draw the zone's outline in game (for layout work).</summary>
    [Export] public bool ShowOutline { get; set; }

    public TouchZone()
    {
        ControlName = "touch_look";
    }

    protected override bool OnPointerDown(int pointer, Vector2 localPosition)
    {
        if (_pointer != int.MinValue) return false;
        _pointer = pointer;
        if (!string.IsNullOrEmpty(PressedControlName)) Player?.SetVirtualControl(PressedControlName, 1f);
        return true;
    }

    protected override void OnPointerMove(int pointer, Vector2 localPosition, Vector2 relative)
    {
        if (pointer == _pointer) Player?.AddToVirtualControl(ControlName, relative * Sensitivity);
    }

    protected override void OnPointerUp(int pointer)
    {
        if (pointer != _pointer) return;
        _pointer = int.MinValue;
        if (!string.IsNullOrEmpty(PressedControlName)) Player?.SetVirtualControl(PressedControlName, 0f);
    }

    public override void _Draw()
    {
        if (ShowOutline || Engine.IsEditorHint()) DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 1f, 1f, 0.25f), filled: false, width: 2f);
    }
}
