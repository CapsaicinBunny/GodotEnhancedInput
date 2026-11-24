using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Triggered for one tick when a stick (or any vector) goes from near rest to nearly full travel within
/// <see cref="MaxSeconds"/> — a flick. Slow pushes do not count. Read the direction from the value on
/// the triggering tick. It re-arms once the value returns to rest.
/// </summary>
[Tool]
[GlobalClass]
public partial class FlickTrigger : InputTrigger
{
    /// <summary>Below this magnitude the value is at rest.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float RestRadius { get; set; } = 0.2f;

    /// <summary>The magnitude a flick must reach.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float FlickRadius { get; set; } = 0.9f;

    /// <summary>The longest time from leaving rest to reaching <see cref="FlickRadius"/>.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01,suffix:s")] public float MaxSeconds { get; set; } = 0.12f;

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;   // Flag: armed (left rest, not yet fired) · Count: 1 once fired until rest · Timer
        float magnitude = context.Value.Length();
        if (magnitude <= RestRadius)
        {
            m.Flag = false;
            m.Count = 0;
            m.Timer = 0;
            return TriggerPhase.Idle;
        }
        if (m.Count > 0) return TriggerPhase.Idle;

        if (!m.Flag)
        {
            m.Flag = true;
            m.Timer = 0;
        }
        else
        {
            m.Timer += context.Delta;
        }

        if (magnitude >= FlickRadius)
        {
            m.Count = 1;
            return m.Timer <= MaxSeconds + TimeEpsilon ? TriggerPhase.Triggered : TriggerPhase.Idle;
        }
        return m.Timer <= MaxSeconds + TimeEpsilon ? TriggerPhase.Ongoing : TriggerPhase.Idle;
    }
}
