using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Repeats like a held key in a text field: triggered on the press (if <see cref="TriggerOnPress"/>),
/// again after <see cref="InitialDelaySeconds"/>, then every <see cref="IntervalSeconds"/> while held;
/// ongoing in between. Without the press trigger it is a long press that then repeats. An optional
/// <see cref="MaxRepeats"/> stops it (idle until released).
/// </summary>
[Tool]
[GlobalClass]
public partial class RepeatTrigger : InputTrigger
{
    [Export] public bool TriggerOnPress { get; set; } = true;

    /// <summary>Time from the press to the first repeat.</summary>
    [Export(PropertyHint.Range, "0,5,0.01,suffix:s")] public float InitialDelaySeconds { get; set; } = 0.4f;

    /// <summary>Time between later repeats.</summary>
    [Export(PropertyHint.Range, "0.01,5,0.01,suffix:s")] public float IntervalSeconds { get; set; } = 0.08f;

    /// <summary>Most repeats after the press (0: no limit).</summary>
    [Export(PropertyHint.Range, "0,1000,1,or_greater")] public int MaxRepeats { get; set; }

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;   // Timer: time to the next repeat · Count: repeats so far
        if (!actuated)
        {
            m.Timer = 0;
            m.Count = 0;
            return TriggerPhase.Idle;
        }

        if (!wasActuated)
        {
            m.Timer = Scaled(context, InitialDelaySeconds, HoldTimeScaleSetting);
            m.Count = 0;
            return TriggerOnPress ? TriggerPhase.Triggered : TriggerPhase.Ongoing;
        }

        if (MaxRepeats > 0 && m.Count >= MaxRepeats) return TriggerPhase.Idle;
        m.Timer -= context.Delta;
        if (m.Timer <= TimeEpsilon)
        {
            m.Timer += Scaled(context, IntervalSeconds, RepeatTimeScaleSetting);
            if (m.Timer < 0) m.Timer = 0;
            m.Count++;
            return TriggerPhase.Triggered;
        }
        return TriggerPhase.Ongoing;
    }
}
