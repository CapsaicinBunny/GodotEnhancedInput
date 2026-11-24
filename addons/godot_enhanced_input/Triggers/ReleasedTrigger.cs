using Godot;

namespace GodotEnhancedInput;

/// <summary>Ongoing while the value is actuated; triggered on the tick it stops being actuated.</summary>
[Tool]
public partial class ReleasedTrigger : InputTrigger
{
    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        if (actuated) return TriggerPhase.Ongoing;
        return wasActuated ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
