using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// Describes a family of devices — a wheel, a set of pedals, a flight stick — so they work well out of
/// the box: which devices it matches (by name or GUID), what they are, friendly names for numbered
/// buttons and axes (used in prompts), per-axis calibration and inversion applied to every event from a
/// matching device before anything else sees it. Add profiles to <see cref="EnhancedInput.DeviceProfiles"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class DeviceProfile : Resource
{
    /// <summary>The profile matches a joypad whose name contains any of these (case-insensitive).</summary>
    [Export] public string[] NameContains { get; set; } = System.Array.Empty<string>();

    /// <summary>The profile matches a joypad with any of these GUIDs.</summary>
    [Export] public string[] Guids { get; set; } = System.Array.Empty<string>();

    /// <summary>What the device is.</summary>
    [Export] public DeviceRole Role { get; set; } = DeviceRole.Gamepad;

    /// <summary>A name for menus.</summary>
    [Export] public string DisplayName { get; set; } = string.Empty;

    /// <summary>Names for numbered buttons ("12" → "Paddle Shift Up").</summary>
    [Export] public Dictionary<int, string> ButtonNames { get; set; } = new();

    /// <summary>Names for numbered axes ("0" → "Steering").</summary>
    [Export] public Dictionary<int, string> AxisNames { get; set; } = new();

    /// <summary>Per axis: (min, centre, max) of the raw reading, mapped to −1…0…1 (see <see cref="CalibrationModifier"/>).</summary>
    [Export] public Dictionary<int, Vector3> AxisCalibration { get; set; } = new();

    /// <summary>Axes whose direction is flipped.</summary>
    [Export] public int[] InvertedAxes { get; set; } = System.Array.Empty<int>();

    /// <summary>Whether the profile describes a joypad with this name and GUID.</summary>
    public bool Matches(string name, string guid)
    {
        if (!string.IsNullOrEmpty(guid))
            foreach (var g in Guids)
                if (string.Equals(g, guid, System.StringComparison.OrdinalIgnoreCase)) return true;
        var lower = (name ?? string.Empty).ToLowerInvariant();
        foreach (var part in NameContains)
            if (!string.IsNullOrEmpty(part) && lower.Contains(part.ToLowerInvariant())) return true;
        return false;
    }

    /// <summary>Applies this profile's calibration and inversion to one axis reading.</summary>
    public float AdjustAxis(int axis, float value)
    {
        if (AxisCalibration.TryGetValue(axis, out var c)) value = CalibrationModifier.Map(value, c.X, c.Y, c.Z);
        if (System.Array.IndexOf(InvertedAxes, axis) >= 0) value = -value;
        return value;
    }

    /// <summary>The friendly name of a numbered button, or null.</summary>
    public string ButtonName(int index) => ButtonNames.TryGetValue(index, out var n) && !string.IsNullOrEmpty(n) ? n : null;

    /// <summary>The friendly name of a numbered axis, or null.</summary>
    public string AxisName(int index) => AxisNames.TryGetValue(index, out var n) && !string.IsNullOrEmpty(n) ? n : null;
}
