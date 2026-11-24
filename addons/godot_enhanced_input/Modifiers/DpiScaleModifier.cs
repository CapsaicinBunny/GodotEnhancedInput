using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Makes pointer motion independent of screen density: multiplies by <see cref="ReferenceDpi"/> divided
/// by the screen DPI, so the same hand movement turns the camera equally on a 1080p and a 4K screen.
/// <see cref="InputPlayer.ScreenDpiOverride"/> wins when set.
/// </summary>
[Tool]
[GlobalClass]
public partial class DpiScaleModifier : InputModifier
{
    /// <summary>The screen density the sensitivity was tuned on.</summary>
    [Export(PropertyHint.Range, "24,600,1")] public float ReferenceDpi { get; set; } = 96f;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        float dpi = context.Player?.ScreenDpiOverride ?? DisplayServer.ScreenGetDpi();
        return dpi > 0 ? value * (ReferenceDpi / dpi) : value;
    }
}
