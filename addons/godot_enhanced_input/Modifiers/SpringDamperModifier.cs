using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Follows the input like a mass on a spring: the output is pulled towards the input with a natural
/// <see cref="Frequency"/> and slowed by <see cref="DampingRatio"/> — 1 settles as fast as possible without
/// overshooting, below 1 overshoots and wobbles, above 1 creeps in. Unlike plain smoothing it keeps
/// momentum, which feels right for steering, a camera that leans into turns, or a cursor with weight.
/// Frame-rate independent. All four components follow their own spring.
/// </summary>
[Tool]
public partial class SpringDamperModifier : InputModifier
{
    /// <summary>The spring's natural frequency in hertz: higher follows faster.</summary>
    [Export(PropertyHint.Range, "0.1,30,0.1,suffix:Hz")] public float Frequency { get; set; } = 4f;

    /// <summary>1 = critically damped (no overshoot), less overshoots, more is sluggish.</summary>
    [Export(PropertyHint.Range, "0,4,0.01")] public float DampingRatio { get; set; } = 1f;

    private const double MaxStep = 1.0 / 240.0;

    /// <inheritdoc/>
    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        var m = context.Memory;   // Previous4: position · Samples4[0]: velocity
        m.Samples4 ??= new Vector4[1];
        if (!m.HasPrevious || context.Delta <= 0)
        {
            if (!m.HasPrevious)
            {
                m.Previous4 = value;   // start where the input is, not at zero
                m.Samples4[0] = Vector4.Zero;
                m.HasPrevious = true;
            }
            return m.Previous4;
        }

        float omega = Mathf.Tau * Frequency;
        var x = m.Previous4;
        var v = m.Samples4[0];
        double remaining = context.Delta;
        while (remaining > 1e-9)
        {
            float dt = (float)System.Math.Min(remaining, MaxStep);
            var a = (value - x) * (omega * omega) - v * (2f * DampingRatio * omega);
            v += a * dt;      // semi-implicit Euler: stable at these step sizes
            x += v * dt;
            remaining -= dt;
        }
        m.Previous4 = x;
        m.Samples4[0] = v;
        return x;
    }
}
