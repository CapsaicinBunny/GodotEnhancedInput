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
    [Export] public DeadzoneShape Shape { get; set; } = DeadzoneShape.Radial;
    [Export(PropertyHint.Range, "0,1,0.01")] public float Lower { get; set; } = 0.2f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float Upper { get; set; } = 1f;

    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        if (Shape == DeadzoneShape.Axial)
            return new Vector3(Remap(value.X), Remap(value.Y), Remap(value.Z));

        float length = value.Length();
        if (length <= 0f) return Vector3.Zero;
        return value / length * Remap(length);
    }

    private float Remap(float v)
    {
        float magnitude = Mathf.Abs(v);
        if (magnitude == 0f || magnitude < Lower) return 0f;
        float range = Upper - Lower;
        float scaled = range <= 0f ? 1f : Mathf.Clamp((magnitude - Lower) / range, 0f, 1f);
        return Mathf.Sign(v) * scaled;
    }
}
