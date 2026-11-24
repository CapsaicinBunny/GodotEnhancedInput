using Godot;

namespace GodotEnhancedInput;

/// <summary>Triggered on every tick the value is actuated.</summary>
[Tool]
public partial class DownTrigger : InputTrigger
{
    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
        => actuated ? TriggerPhase.Triggered : TriggerPhase.Idle;
}
