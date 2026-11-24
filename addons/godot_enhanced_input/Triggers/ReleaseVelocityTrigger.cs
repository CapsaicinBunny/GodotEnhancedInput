using Godot;

namespace GodotEnhancedInput;

/// <summary>What a <see cref="ReleaseVelocityTrigger"/> measures.</summary>
public enum VelocityMeasure
{
    /// <summary>The value is a position (a stick, a pointer or touch position): speed is how fast it changes.</summary>
    Position,
    /// <summary>The value is already movement per tick (mouse motion, a touch drag): speed is its size per second.</summary>
    Motion,
}

/// <summary>
/// Triggered for one tick on release if the value was moving fast just before — throwing, flinging,
/// flick-to-scroll, a swipe that must be quick. The speed is the fastest seen during the last
/// <see cref="WindowSeconds"/> before the release (so slowing down at the very end does not spoil a throw);
/// a slow release cancels. Ongoing while actuated.
/// </summary>
[Tool]
public partial class ReleaseVelocityTrigger : InputTrigger
{
    /// <summary>The slowest release that counts, in value units per second (stick: 1 = full travel in a second; pointer: pixels per second).</summary>
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float MinSpeed { get; set; } = 4f;

    /// <summary>How far back before the release the speed is looked for.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01,suffix:s")] public float WindowSeconds { get; set; } = 0.1f;

    /// <summary>Whether the value is a position or per-tick movement.</summary>
    [Export] public VelocityMeasure Measure { get; set; } = VelocityMeasure.Position;

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;   // PreviousValue: last value · Timer2: best speed · Timer: age of best
        if (actuated)
        {
            if (!wasActuated)
            {
                m.Timer2 = 0;
                m.Timer = 0;
            }
            else if (context.Delta > 0)
            {
                double speed = Measure == VelocityMeasure.Motion
                    ? context.Value.Length() / context.Delta
                    : (context.Value - m.PreviousValue).Length() / context.Delta;
                m.Timer += context.Delta;
                if (speed >= m.Timer2 || m.Timer > WindowSeconds + TimeEpsilon)
                {
                    m.Timer2 = speed;
                    m.Timer = 0;
                }
            }
            m.PreviousValue = context.Value;
            return TriggerPhase.Ongoing;
        }

        if (!wasActuated) return TriggerPhase.Idle;
        bool fast = m.Timer2 >= MinSpeed;
        m.Timer2 = 0;
        m.Timer = 0;
        return fast ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
