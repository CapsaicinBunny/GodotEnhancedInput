using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Triggered on the release that completes <see cref="Taps"/> quick taps (2 for a double tap). Each press
/// must be released within <see cref="TapSeconds"/>, and the next press must start within
/// <see cref="GapSeconds"/>. Ongoing while a sequence is under way; a press held too long or a gap too
/// long starts over (canceling the action).
/// </summary>
[Tool]
[GlobalClass]
public partial class MultiTapTrigger : InputTrigger
{
    /// <summary>How many taps are needed.</summary>
    [Export(PropertyHint.Range, "2,8,1")] public int Taps { get; set; } = 2;

    /// <summary>The longest press that counts as a tap.</summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,suffix:s")] public float TapSeconds { get; set; } = 0.2f;

    /// <summary>The longest pause allowed between taps.</summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,suffix:s")] public float GapSeconds { get; set; } = 0.3f;

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        double tapSeconds = Scaled(context, TapSeconds, TapTimeScaleSetting), gapSeconds = Scaled(context, GapSeconds, TapTimeScaleSetting);
        var m = context.Memory;   // Count: taps done · Timer: time since last release · Timer2: current press length
        if (actuated)
        {
            if (!wasActuated)
            {
                if (m.Count > 0 && m.Timer > gapSeconds + TimeEpsilon) m.Count = 0;
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
