using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Ongoing while the value is actuated; on release, triggered for one tick if it was held for at least
/// <see cref="HoldSeconds"/>, otherwise idle (the action is canceled).
/// </summary>
[Tool]
[GlobalClass]
public partial class HoldReleaseTrigger : InputTrigger
{
    /// <summary>The minimum time the value must be held before releasing counts.</summary>
    [Export(PropertyHint.Range, "0,10,0.01,or_greater,suffix:s")] public float HoldSeconds { get; set; } = 0.5f;

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var memory = context.Memory;
        if (actuated)
        {
            memory.Timer += context.Delta;
            return TriggerPhase.Ongoing;
        }

        bool fire = wasActuated && memory.Timer + TimeEpsilon >= Scaled(context, HoldSeconds, HoldTimeScaleSetting);
        memory.Timer = 0;
        return fire ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
