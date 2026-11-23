using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// An on-screen stick. One finger that starts inside the control moves the knob; the control writes
/// a 2D value (length up to 1, up is −Y like a real stick) to its virtual control, and zero on
/// release. With <see cref="Floating"/>, the stick centres where the finger lands.
/// </summary>
[Tool]
[GlobalClass]
public partial class VirtualJoystick : TouchControl
{
    private int _pointer = int.MinValue;
    private Vector2 _center;

    /// <summary>How far the knob travels, in pixels, for a full value.</summary>
    [Export(PropertyHint.Range, "8,512,1,suffix:px")] public float Radius { get; set; } = 64f;

    /// <summary>Centre the stick where the finger touches down instead of in the middle of the control.</summary>
    [Export] public bool Floating { get; set; }

    [Export] public Color BaseColor { get; set; } = new(1f, 1f, 1f, 0.18f);
    [Export] public Color KnobColor { get; set; } = new(1f, 1f, 1f, 0.55f);

    /// <summary>The value last written.</summary>
    public Vector2 Value { get; private set; }

    public VirtualJoystick()
    {
        ControlName = "touch_move";
        CustomMinimumSize = new Vector2(160, 160);
    }

    protected override bool OnPointerDown(int pointer, Vector2 localPosition)
    {
        if (_pointer != int.MinValue) return false;
        _pointer = pointer;
        _center = Floating ? localPosition : Size / 2f;
        Move(localPosition);
        return true;
    }

    protected override void OnPointerMove(int pointer, Vector2 localPosition, Vector2 relative)
    {
        if (pointer == _pointer) Move(localPosition);
    }

    protected override void OnPointerUp(int pointer)
    {
        if (pointer != _pointer) return;
        _pointer = int.MinValue;
        Write(Vector2.Zero);
    }

    private void Move(Vector2 localPosition)
    {
        var offset = (localPosition - _center).LimitLength(Radius);
        Write(Radius > 0f ? offset / Radius : Vector2.Zero);
    }

    private void Write(Vector2 value)
    {
        Value = value;
        Player?.SetVirtualControl(ControlName, value);
    }

    public override void _Draw()
    {
        var center = _pointer != int.MinValue ? _center : Size / 2f;
        DrawCircle(center, Radius, BaseColor);
        DrawCircle(center + Value * Radius, Radius * 0.45f, KnobColor);
    }
}
