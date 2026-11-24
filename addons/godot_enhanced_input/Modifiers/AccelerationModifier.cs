using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Pointer acceleration for movement values: faster movement is amplified more. The gain is
/// <c>1 + Acceleration · speed^Exponent</c>, capped at <see cref="MaxGain"/>, where speed is the value's
/// length per second. Slow, precise movement stays close to 1:1.
/// </summary>
[Tool]
public partial class AccelerationModifier : InputModifier
{
    /// <summary>How much faster movement is amplified.</summary>
    [Export(PropertyHint.Range, "0,1,0.0001,or_greater")] public float Acceleration { get; set; } = 0.001f;
    /// <summary>How the gain grows with speed (1: linearly).</summary>
    [Export(PropertyHint.Range, "0.1,3,0.01")] public float Exponent { get; set; } = 1f;
    /// <summary>The largest gain applied.</summary>
    [Export(PropertyHint.Range, "1,20,0.1,or_greater")] public float MaxGain { get; set; } = 4f;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        if (context.Delta <= 0) return value;
        float speed = value.Length() / (float)context.Delta;
        float gain = Mathf.Min(1f + Acceleration * Mathf.Pow(speed, Exponent), MaxGain);
        return value * gain;
    }
}
