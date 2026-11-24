using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// For movement values (mouse motion, touch drag, deltas): adds up how far the value has moved since it
/// started moving and is triggered once the total reaches <see cref="Distance"/> — "drag at least 20
/// pixels before it counts". Ongoing before that. The total starts over after
/// <see cref="ResetSeconds"/> without movement. With <see cref="OneShot"/> it triggers once per movement.
/// </summary>
[Tool]
[GlobalClass]
public partial class DistanceTrigger : InputTrigger
{
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")] public float Distance { get; set; } = 20f;
    [Export(PropertyHint.Range, "0,2,0.01,suffix:s")] public float ResetSeconds { get; set; } = 0.15f;
    [Export] public bool OneShot { get; set; }

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;   // Timer2: distance so far · Timer: time without movement · Flag: fired
        float step = context.Value4.Length();
        if (step == 0f)
        {
            m.Timer += context.Delta;
            if (m.Timer > ResetSeconds + TimeEpsilon)
            {
                m.Timer2 = 0;
                m.Flag = false;
                return TriggerPhase.Idle;
            }
            return m.Timer2 > 0 && !m.Flag ? TriggerPhase.Ongoing : TriggerPhase.Idle;
        }

        m.Timer = 0;
        m.Timer2 += step;
        if (m.Timer2 + TimeEpsilon < Distance) return TriggerPhase.Ongoing;
        if (OneShot && m.Flag) return TriggerPhase.Idle;
        m.Flag = true;
        return TriggerPhase.Triggered;
    }
}
