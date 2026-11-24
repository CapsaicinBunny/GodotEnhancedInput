using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Scales by a sensitivity the player chose, stored as a setting in their profile
/// (<see cref="InputProfile.SetSetting"/>), times <see cref="Scale"/>. <see cref="InvertYSetting"/> and
/// <see cref="InvertXSetting"/> name yes/no settings that flip an axis. Use one setting per group —
/// "mouse_sensitivity", "stick_sensitivity" — so keyboard/mouse and joypad are tuned separately.
/// </summary>
[Tool]
[GlobalClass]
public partial class SensitivityModifier : InputModifier
{
    /// <summary>Setting holding the multiplier (1 when not set).</summary>
    [Export] public string Setting { get; set; } = "sensitivity";

    /// <summary>Fixed multiplier applied as well.</summary>
    [Export] public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>Optional yes/no setting that flips Y (e.g. "invert_look_y").</summary>
    [Export] public string InvertYSetting { get; set; } = string.Empty;

    /// <summary>Optional yes/no setting that flips X.</summary>
    [Export] public string InvertXSetting { get; set; } = string.Empty;

    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var profile = context.Player?.Profile;
        float multiplier = (float)(profile?.GetSetting(Setting, 1.0) ?? 1.0);
        var result = value * Scale * multiplier;
        if (profile != null && !string.IsNullOrEmpty(InvertYSetting) && profile.GetFlag(InvertYSetting)) result.Y = -result.Y;
        if (profile != null && !string.IsNullOrEmpty(InvertXSetting) && profile.GetFlag(InvertXSetting)) result.X = -result.X;
        return result;
    }
}
