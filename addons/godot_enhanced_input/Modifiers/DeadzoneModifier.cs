using Godot;

namespace GodotEnhancedInput;

/// <summary>How <see cref="DeadzoneModifier"/> measures the value.</summary>
public enum DeadzoneShape
{
    /// <summary>Each component on its own.</summary>
    Axial,
    /// <summary>The length of the vector, keeping its direction.</summary>
    Radial,
}

/// <summary>
/// Ignores small values and rescales the rest. Below <see cref="Lower"/> the value is zero; between
/// <see cref="Lower"/> and <see cref="Upper"/> it is stretched to 0…1; above <see cref="Upper"/> it is 1.
/// <see cref="DeadzoneShape.Axial"/> does this per component (keeping each sign);
/// <see cref="DeadzoneShape.Radial"/> does it on the vector's length (keeping its direction).
/// </summary>
[Tool]
[GlobalClass]
public partial class DeadzoneModifier : InputModifier
{
    /// <summary>Per component (axial) or on the vector's length (radial, keeps the direction).</summary>
    [Export] public DeadzoneShape Shape { get; set; } = DeadzoneShape.Radial;
    /// <summary>Below this the output is zero (for rotations: a fraction of half a turn).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Lower { get; set; } = 0.2f;
    /// <summary>Above this the output is full.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Upper { get; set; } = 1f;

    /// <summary>Optional player setting that replaces <see cref="Lower"/> (e.g. "stick_deadzone"), so players can tune it.</summary>
    [Export] public string LowerSetting { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        float lower = !string.IsNullOrEmpty(LowerSetting) && context.Player != null ? (float)context.Player.GetSetting(LowerSetting, Lower) : Lower;
        if (Shape == DeadzoneShape.Axial)
            return new Vector3(Remap(value.X, lower), Remap(value.Y, lower), Remap(value.Z, lower));

        float length = value.Length();
        if (length <= 0f) return Vector3.Zero;
        return value / length * Remap(length, lower);
    }

    /// <inheritdoc/>
    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        float lower = !string.IsNullOrEmpty(LowerSetting) && context.Player != null ? (float)context.Player.GetSetting(LowerSetting, Lower) : Lower;
        if (context.Kind == ActionValueKind.Quaternion)
        {
            // Small rotations are ignored and the rest rescaled, on the rotation angle.
            float angle = ActionValue.MagnitudeOf(context.Kind, value);
            if (angle <= 0f) return Vector4.Zero;
            float remapped = Remap(angle, lower);
            return remapped == 0f ? Vector4.Zero : ActionValue.ScaleRotation(value, remapped / angle);
        }
        if (Shape == DeadzoneShape.Axial)
            return new Vector4(Remap(value.X, lower), Remap(value.Y, lower), Remap(value.Z, lower), Remap(value.W, lower));
        float length = value.Length();
        if (length <= 0f) return Vector4.Zero;
        return value / length * Remap(length, lower);
    }

    private float Remap(float v, float lower)
    {
        float magnitude = Mathf.Abs(v);
        if (magnitude == 0f || magnitude < lower) return 0f;
        float range = Upper - lower;
        float scaled = range <= 0f ? 1f : Mathf.Clamp((magnitude - lower) / range, 0f, 1f);
        return Mathf.Sign(v) * scaled;
    }
}
