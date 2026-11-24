using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Triggered on the release that completes <see cref="Taps"/> quick taps (2 for a double tap). Each press
/// must be released within <see cref="TapSeconds"/>, and the next press must start within
/// <see cref="GapSeconds"/>. Ongoing while a sequence is under way; a press held too long or a gap too
/// long starts over (canceling the action). With <see cref="MaxDistance"/>, a press too far from the first
/// one (in pointer pixels) starts a new sequence — a double-click must land on the same spot.
/// </summary>
[Tool]
public partial class MultiTapTrigger : InputTrigger
{
    /// <summary>How many taps are needed.</summary>
    [Export(PropertyHint.Range, "2,8,1")] public int Taps { get; set; } = 2;

    /// <summary>The longest press that counts as a tap.</summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,suffix:s")] public float TapSeconds { get; set; } = 0.2f;

    /// <summary>The longest pause allowed between taps.</summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,suffix:s")] public float GapSeconds { get; set; } = 0.3f;

    /// <summary>
    /// How far (in pixels) the pointer may be from where the first tap was pressed; 0 = anywhere. Uses the
    /// mouse position, which touches also move unless touch-to-mouse emulation is off.
    /// </summary>
    [Export(PropertyHint.Range, "0,200,1,or_greater,suffix:px")] public float MaxDistance { get; set; }

    private static Vector2 Pointer(in TriggerContext context)
    {
        var p = context.Player?.Raw.GetCurrent(SourceKey.MousePosition) ?? Vector4.Zero;
        return new Vector2(p.X, p.Y);
    }

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        double tapSeconds = Scaled(context, TapSeconds, TapTimeScaleSetting), gapSeconds = Scaled(context, GapSeconds, TapTimeScaleSetting);
        var m = context.Memory;   // Count: taps done · Timer: time since last release · Timer2: current press length
        if (actuated)
        {
            if (!wasActuated)
            {
                if (m.Count > 0 && m.Timer > gapSeconds + TimeEpsilon) m.Count = 0;
                if (MaxDistance > 0f)
                {
                    var at = Pointer(context);
                    if (m.Count > 0 && at.DistanceTo(new Vector2(m.PreviousValue.X, m.PreviousValue.Y)) > MaxDistance) m.Count = 0;
                    if (m.Count == 0) m.PreviousValue = new Vector3(at.X, at.Y, 0f);   // where the sequence started
                }
                m.Timer2 = 0;
                m.Flag = true;   // pressing a candidate tap
            }
            if (!m.Flag) return TriggerPhase.Idle;   // held too long: wait for release
            m.Timer2 += context.Delta;
            if (m.Timer2 > tapSeconds + TimeEpsilon)
            {
                m.Count = 0;
                m.Flag = false;
                return TriggerPhase.Idle;
            }
            return TriggerPhase.Ongoing;
        }

        if (wasActuated && m.Flag)
        {
            m.Flag = false;
            m.Count++;
            m.Timer = 0;
            if (m.Count >= Taps)
            {
                m.Count = 0;
                return TriggerPhase.Triggered;
            }
            return TriggerPhase.Ongoing;
        }

        if (m.Count > 0)
        {
            m.Timer += context.Delta;
            if (m.Timer > gapSeconds + TimeEpsilon)
            {
                m.Count = 0;
                return TriggerPhase.Idle;
            }
            return TriggerPhase.Ongoing;
        }
        return TriggerPhase.Idle;
    }
}
