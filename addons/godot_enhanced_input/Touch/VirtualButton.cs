using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// An on-screen button. Writes 1 to its virtual control while any finger that started on it is down,
/// and 0 when the last one lifts. Triggers on the bound action decide what a press means.
/// </summary>
[Tool]
[GlobalClass]
public partial class VirtualButton : TouchControl
{
    /// <summary>Label drawn on the button.</summary>
    [Export] public string Text { get; set; } = string.Empty;

    [Export] public Color IdleColor { get; set; } = new(1f, 1f, 1f, 0.2f);
    [Export] public Color PressedColor { get; set; } = new(1f, 1f, 1f, 0.55f);

    /// <summary>Whether a finger is on the button.</summary>
    public bool IsPressed => Pointers.Count > 0;

    public VirtualButton()
    {
        ControlName = "touch_button";
        CustomMinimumSize = new Vector2(72, 72);
    }

    protected override bool OnPointerDown(int pointer, Vector2 localPosition)
    {
        Player?.SetVirtualControl(ControlName, 1f);
        return true;
    }

    protected override void OnPointerUp(int pointer)
    {
        if (Pointers.Count == 0) Player?.SetVirtualControl(ControlName, 0f);
    }

    public override void _Draw()
    {
        float radius = Mathf.Min(Size.X, Size.Y) / 2f;
        DrawCircle(Size / 2f, radius, IsPressed ? PressedColor : IdleColor);
        if (!string.IsNullOrEmpty(Text))
        {
            var font = GetThemeDefaultFont();
            int fontSize = GetThemeDefaultFontSize();
            var textSize = font.GetStringSize(Text, HorizontalAlignment.Left, -1, fontSize);
            DrawString(font, Size / 2f - new Vector2(textSize.X / 2f, -textSize.Y / 4f), Text, HorizontalAlignment.Left, -1, fontSize);
        }
    }
}
