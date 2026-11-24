using Godot;

namespace GodotEnhancedInput;

/// <summary>Which crossings <see cref="ThresholdTrigger"/> reports.</summary>
public enum CrossingDirection
{
    /// <summary>Going above the threshold.</summary>
    Rising,
    /// <summary>Going below it.</summary>
    Falling,
    /// <summary>Either way.</summary>
    Both,
}

/// <summary>
/// Triggered for one tick when the value's magnitude crosses <see cref="Threshold"/> — e.g. "the
/// throttle passed 80 %" or "the trigger was let go below 20 %". A <see cref="Band"/> around the
/// threshold keeps a noisy value from crossing back and forth.
/// </summary>
[Tool]
[GlobalClass]
public partial class ThresholdTrigger : InputTrigger
{
    [Export(PropertyHint.Range, "0,1,0.01,or_greater")] public float Threshold { get; set; } = 0.5f;

    /// <summary>Width of the dead band around the threshold.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float Band { get; set; } = 0.05f;

    [Export] public CrossingDirection Direction { get; set; } = CrossingDirection.Rising;

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;   // Flag: currently above
        float magnitude = context.Value.Length();
        bool crossedUp = !m.Flag && magnitude >= Threshold + Band * 0.5f;
        bool crossedDown = m.Flag && magnitude <= Threshold - Band * 0.5f;
        if (crossedUp) m.Flag = true;
        if (crossedDown) m.Flag = false;

        bool report = Direction switch
        {
            CrossingDirection.Rising => crossedUp,
            CrossingDirection.Falling => crossedDown,
            _ => crossedUp || crossedDown,
        };
        return report ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
