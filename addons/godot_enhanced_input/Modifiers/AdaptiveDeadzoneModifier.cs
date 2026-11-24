using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// A radial deadzone that adapts to the stick: it learns where a drifting stick rests and how much it
/// jitters there, and centres the deadzone on that rest point with a radius just above the jitter — so a
/// worn pad stops creeping without every player having to raise a fixed deadzone. While the stick is near
/// its rest (within <see cref="LearnRadius"/>), the rest point and the jitter are followed slowly; pushed
/// further, nothing is learned. The radius stays between <see cref="MinDeadzone"/> and
/// <see cref="MaxDeadzone"/>; outside it the value is rescaled to 0…1 from the learned centre.
/// Works on the first two components (a stick); the rest pass through.
/// </summary>
[Tool]
public partial class AdaptiveDeadzoneModifier : InputModifier
{
    /// <summary>The smallest deadzone radius.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.005")] public float MinDeadzone { get; set; } = 0.05f;

    /// <summary>The largest deadzone radius, however noisy the stick.</summary>
    [Export(PropertyHint.Range, "0,0.9,0.005")] public float MaxDeadzone { get; set; } = 0.3f;

    /// <summary>Readings within this distance of the centre are taken as "at rest" and learned from.</summary>
    [Export(PropertyHint.Range, "0.01,0.9,0.01")] public float LearnRadius { get; set; } = 0.3f;

    /// <summary>How fast the rest point and jitter are followed, per second.</summary>
    [Export(PropertyHint.Range, "0.01,10,0.01,suffix:/s")] public float LearnRate { get; set; } = 0.5f;

    /// <summary>The deadzone radius is the learned jitter times this.</summary>
    [Export(PropertyHint.Range, "1,5,0.05")] public float Margin { get; set; } = 1.5f;

    /// <summary>The rest point learned so far for a memory (for debugging and calibration screens).</summary>
    public static Vector2 LearnedCenter(ModifierMemory memory) => memory == null ? Vector2.Zero : new Vector2(memory.Previous4.X, memory.Previous4.Y);

    /// <summary>The deadzone radius in use for a memory.</summary>
    public float CurrentDeadzone(ModifierMemory memory)
        => Mathf.Clamp((memory?.Previous4.Z ?? 0f) * Margin, MinDeadzone, Mathf.Max(MinDeadzone, MaxDeadzone));

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var m = context.Memory;   // Previous4: (centre x, centre y, jitter, unused)
        var v = new Vector2(value.X, value.Y);
        var center = new Vector2(m.Previous4.X, m.Previous4.Y);
        float jitter = m.Previous4.Z;

        if (v.Length() <= LearnRadius && context.Delta > 0)
        {
            float k = 1f - Mathf.Exp(-LearnRate * (float)context.Delta);
            center += (v - center) * k;
            float spread = (v - center).Length();
            // Jitter rises quickly to a new spread and falls slowly.
            jitter += (spread - jitter) * (spread > jitter ? Mathf.Min(1f, k * 8f) : k);
            m.Previous4 = new Vector4(center.X, center.Y, jitter, 0f);
        }

        float deadzone = CurrentDeadzone(m);
        var offset = v - center;
        float length = offset.Length();
        if (length <= deadzone) return new Vector3(0f, 0f, value.Z);
        float scaled = Mathf.Min(1f, (length - deadzone) / Mathf.Max(1e-4f, 1f - deadzone));
        var result = offset / length * scaled;
        return new Vector3(result.X, result.Y, value.Z);
    }
}
