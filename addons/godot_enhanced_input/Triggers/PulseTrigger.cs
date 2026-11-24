using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// While the value is actuated, triggers in pulses: on the first tick (if <see cref="TriggerOnStart"/>),
/// then once every <see cref="IntervalSeconds"/>, ongoing in between. With a <see cref="MaxPulses"/> limit,
/// it goes idle after the last pulse until the value is released.
/// </summary>
[Tool]
[GlobalClass]
public partial class PulseTrigger : InputTrigger
{
    /// <summary>Time between pulses.</summary>
    [Export(PropertyHint.Range, "0.01,10,0.01,or_greater,suffix:s")] public float IntervalSeconds { get; set; } = 0.5f;

    /// <summary>Pulse on the tick the value becomes actuated.</summary>
    [Export] public bool TriggerOnStart { get; set; } = true;

    /// <summary>Most pulses per press; 0 for no limit.</summary>
    [Export(PropertyHint.Range, "0,100,1,or_greater")] public int MaxPulses { get; set; }

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var memory = context.Memory;
        if (!actuated)
        {
            memory.Timer = 0;
            memory.Count = 0;
            return TriggerPhase.Idle;
        }

        if (!wasActuated)
        {
            memory.Timer = 0;
            memory.Count = 0;
            if (TriggerOnStart)
            {
                memory.Count = 1;
                return TriggerPhase.Triggered;
            }
            return TriggerPhase.Ongoing;
        }

        if (MaxPulses > 0 && memory.Count >= MaxPulses) return TriggerPhase.Idle;

        memory.Timer += context.Delta;
        double interval = Scaled(context, IntervalSeconds, RepeatTimeScaleSetting);
        if (memory.Timer + TimeEpsilon >= interval)
        {
            memory.Timer -= interval;
            if (memory.Timer < 0) memory.Timer = 0;
            memory.Count++;
            return TriggerPhase.Triggered;
        }
        return TriggerPhase.Ongoing;
    }
}
