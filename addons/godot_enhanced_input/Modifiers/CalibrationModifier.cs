using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Corrects an axis that does not read −1…1 around a true centre — worn sticks, wheels, pedals, flight
/// throttles. Per component, values between <see cref="Min"/> and <see cref="Center"/> map to −1…0 and
/// between <see cref="Center"/> and <see cref="Max"/> to 0…1 (clamped). Measure the three points once
/// (e.g. in a calibration screen) and store them, or let a <see cref="DeviceProfile"/> provide them.
/// </summary>
[Tool]
public partial class CalibrationModifier : InputModifier
{
    /// <summary>Measured reading at full negative travel, per component.</summary>
    [Export] public Vector3 Min { get; set; } = new(-1f, -1f, -1f);
    /// <summary>Measured reading at rest, per component.</summary>
    [Export] public Vector3 Center { get; set; } = Vector3.Zero;
    /// <summary>Measured reading at full positive travel, per component.</summary>
    [Export] public Vector3 Max { get; set; } = new(1f, 1f, 1f);

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 v, in ModifierContext context)
        => new(Map(v.X, Min.X, Center.X, Max.X), Map(v.Y, Min.Y, Center.Y, Max.Y), Map(v.Z, Min.Z, Center.Z, Max.Z));

    /// <summary>Maps one value with a min/centre/max calibration.</summary>
    public static float Map(float v, float min, float center, float max)
    {
        if (v >= center) return max > center ? Mathf.Clamp((v - center) / (max - center), 0f, 1f) : 0f;
        return center > min ? -Mathf.Clamp((center - v) / (center - min), 0f, 1f) : 0f;
    }
}
