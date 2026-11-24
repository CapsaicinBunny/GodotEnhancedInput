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
/// <see cref="WindowSeconds"/> before the release (so slowing down at the very end does not spoil a throw,
/// but a fast move followed by a pause does not count); a slow release cancels. Ongoing while actuated.
/// Movement is kept as timestamped samples, so evaluation steps in which no time passes still add their
/// movement to the next sample.
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

    private const int Capacity = 32;

    // Timestamped speed samples: each covers [Time - Span, Time].
    private sealed class Samples
    {
        public readonly double[] Time = new double[Capacity];
        public readonly double[] Speed = new double[Capacity];
        public int Count, Head;
        public double Clock;           // seconds since the press
        public double PendingSpan;     // time since the last sample
        public Vector3 PendingMotion;  // motion values seen since the last sample
        public Vector3 LastPosition;

        public void Clear()
        {
            Count = Head = 0;
            Clock = PendingSpan = 0;
            PendingMotion = Vector3.Zero;
        }

        public void Add(double time, double speed)
        {
            Time[Head] = time;
            Speed[Head] = speed;
            Head = (Head + 1) % Capacity;
            if (Count < Capacity) Count++;
        }

        public double FastestSince(double since)
        {
            double best = 0;
            for (int i = 0; i < Count; i++)
            {
                int k = (Head - 1 - i + Capacity) % Capacity;
                if (Time[k] < since) break;
                if (Speed[k] > best) best = Speed[k];
            }
            return best;
        }
    }

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        if (context.Memory.Custom is not Samples s) context.Memory.Custom = s = new Samples();

        if (actuated && !wasActuated)
        {
            s.Clear();
            s.LastPosition = context.Value;
            return TriggerPhase.Ongoing;
        }
        if (!actuated && !wasActuated) return TriggerPhase.Idle;

        // Time passes for every evaluation, the release included, so old movement expires.
        s.Clock += context.Delta;
        s.PendingSpan += context.Delta;
        if (actuated)
        {
            if (Measure == VelocityMeasure.Motion) s.PendingMotion += context.Value;
            if (s.PendingSpan > 0)
            {
                double distance = Measure == VelocityMeasure.Motion
                    ? s.PendingMotion.Length()
                    : (context.Value - s.LastPosition).Length();
                s.Add(s.Clock, distance / s.PendingSpan);
                s.PendingSpan = 0;
                s.PendingMotion = Vector3.Zero;
                s.LastPosition = context.Value;
            }
            // No time passed: the movement waits for the next sample instead of being lost.
            return TriggerPhase.Ongoing;
        }

        bool fast = s.FastestSince(s.Clock - WindowSeconds - TimeEpsilon) >= MinSpeed;
        s.Clear();
        return fast ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
