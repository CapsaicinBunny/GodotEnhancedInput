using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// A radial deadzone that adapts to the stick: it learns where a drifting stick rests and how much it
/// jitters there, and centres the deadzone on that rest point with a radius just above the jitter — so a
/// worn pad stops creeping without every player having to raise a fixed deadzone. It learns only while the
/// stick rests: near the centre (within <see cref="LearnRadius"/>) and still (moving less than
/// <see cref="StillThreshold"/> per tick) for <see cref="RestSeconds"/>, so a stick held a little off-centre on
/// purpose while aiming or creeping is not mistaken for drift once it moves; the learned centre never goes
/// further than <see cref="MaxCenterOffset"/>, and <see cref="FreezeLearning"/> stops learning (after a
/// calibration screen, say). The radius stays between <see cref="MinDeadzone"/> and <see cref="MaxDeadzone"/>;
/// outside it the value is rescaled to 0…1 from the learned centre. Works on the first two components (a
/// stick); the rest pass through.
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

    /// <summary>How long the stick must rest (near the centre and still) before anything is learned.</summary>
    [Export(PropertyHint.Range, "0,5,0.05,suffix:s")] public float RestSeconds { get; set; } = 0.5f;

    /// <summary>Movement per tick below which the stick counts as still.</summary>
    [Export(PropertyHint.Range, "0.001,0.2,0.001")] public float StillThreshold { get; set; } = 0.02f;

    /// <summary>The furthest the learned centre may move from (0, 0).</summary>
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float MaxCenterOffset { get; set; } = 0.2f;

    /// <summary>Stop learning: the centre and jitter learned so far are kept.</summary>
    [Export] public bool FreezeLearning { get; set; }

    /// <summary>Forgets what was learned for a memory (a "recalibrate" button).</summary>
    public static void Reset(ModifierMemory memory) => memory?.Reset();

    /// <summary>The rest point learned so far for a memory (for debugging and calibration screens).</summary>
    public static Vector2 LearnedCenter(ModifierMemory memory) => memory == null ? Vector2.Zero : new Vector2(memory.Previous4.X, memory.Previous4.Y);

    /// <summary>The deadzone radius in use for a memory.</summary>
    public float CurrentDeadzone(ModifierMemory memory)
        => Mathf.Clamp((memory?.Previous4.Z ?? 0f) * Margin, MinDeadzone, Mathf.Max(MinDeadzone, MaxDeadzone));

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var m = context.Memory;   // Previous4: (centre x, centre y, jitter, rest time) · Previous: last reading
        var v = new Vector2(value.X, value.Y);
        var center = new Vector2(m.Previous4.X, m.Previous4.Y);
        float jitter = m.Previous4.Z;
        float rest = m.Previous4.W;

        if (context.Delta > 0)
        {
            var last = new Vector2(m.Previous.X, m.Previous.Y);
            bool resting = v.Length() <= LearnRadius && (!m.HasPrevious || (v - last).Length() <= StillThreshold);
            rest = resting ? rest + (float)context.Delta : 0f;
            m.Previous = new Vector3(v.X, v.Y, 0f);
            m.HasPrevious = true;

            if (!FreezeLearning && rest >= RestSeconds)
            {
                float k = 1f - Mathf.Exp(-LearnRate * (float)context.Delta);
                center += (v - center) * k;
                if (center.Length() > MaxCenterOffset) center = center.Normalized() * MaxCenterOffset;
                float spread = (v - center).Length();
                // Jitter rises quickly to a new spread and falls slowly.
                jitter += (spread - jitter) * (spread > jitter ? Mathf.Min(1f, k * 8f) : k);
            }
            m.Previous4 = new Vector4(center.X, center.Y, jitter, rest);
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
