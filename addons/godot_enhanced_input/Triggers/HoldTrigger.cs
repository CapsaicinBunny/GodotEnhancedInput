using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Ongoing while the value has been actuated for less than <see cref="HoldSeconds"/>, then triggered —
/// on every tick after that, or only once with <see cref="OneShot"/> (then idle until released).
/// Releasing early returns to idle, which cancels the action.
/// </summary>
[Tool]
[GlobalClass]
public partial class HoldTrigger : InputTrigger
{
    /// <summary>How long the value must stay actuated.</summary>
    [Export(PropertyHint.Range, "0,10,0.01,or_greater,suffix:s")] public float HoldSeconds { get; set; } = 0.5f;

    /// <summary>Trigger only on the first tick the hold time is reached.</summary>
    [Export] public bool OneShot { get; set; }

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var memory = context.Memory;
        if (!actuated)
        {
            memory.Timer = 0;
            memory.Flag = false;
            return TriggerPhase.Idle;
        }

        memory.Timer += context.Delta;
        if (memory.Timer + TimeEpsilon < Scaled(context, HoldSeconds, HoldTimeScaleSetting)) return TriggerPhase.Ongoing;

        if (OneShot && memory.Flag) return TriggerPhase.Idle;
        memory.Flag = true;
        return TriggerPhase.Triggered;
    }
}
