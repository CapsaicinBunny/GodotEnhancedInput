using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The opposite of a deadzone: lifts small non-zero values up to <see cref="Minimum"/> and scales the
/// rest into <c>Minimum…1</c>, to cancel a deadzone that a game, driver or accessibility device applies
/// later. Radial keeps the direction; axial works per component.
/// </summary>
[Tool]
public partial class AntiDeadzoneModifier : InputModifier
{
    /// <summary>Per component (axial) or on the vector's length (radial).</summary>
    [Export] public DeadzoneShape Shape { get; set; } = DeadzoneShape.Radial;
    /// <summary>The smallest non-zero output.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Minimum { get; set; } = 0.2f;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        if (Shape == DeadzoneShape.Axial) return new Vector3(Lift(value.X), Lift(value.Y), Lift(value.Z));
        float length = value.Length();
        return length == 0f ? Vector3.Zero : value / length * Lift(length);
    }

    private float Lift(float v) => v == 0f ? 0f : Mathf.Sign(v) * (Minimum + (1f - Minimum) * Mathf.Min(Mathf.Abs(v), 1f));
}
