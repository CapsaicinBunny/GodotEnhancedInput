using Godot;

namespace GodotEnhancedInput;

/// <summary>Triggered only on the tick the value becomes actuated; idle while it stays held.</summary>
[Tool]
[GlobalClass]
public partial class PressedTrigger : InputTrigger
{
    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
        => actuated && !wasActuated ? TriggerPhase.Triggered : TriggerPhase.Idle;
}
