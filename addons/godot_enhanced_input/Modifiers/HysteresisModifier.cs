using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Holds the output still until the input moves more than <see cref="Band"/> away from it, removing
/// jitter from noisy sensors and worn sticks without the lag of smoothing. Zero always passes through.
/// </summary>
[Tool]
public partial class HysteresisModifier : InputModifier
{
    /// <summary>How far the input must move before the output follows.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.001")] public float Band { get; set; } = 0.02f;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var m = context.Memory;
        if (!m.HasPrevious || value == Vector3.Zero || (value - m.Previous).Length() > Band)
        {
            m.Previous = value;
            m.HasPrevious = true;
        }
        return m.Previous;
    }
}
