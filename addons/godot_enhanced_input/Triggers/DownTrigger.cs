using Godot;

namespace GodotEnhancedInput;

/// <summary>Triggered on every tick the value is actuated.</summary>
[Tool]
[GlobalClass]
public partial class DownTrigger : InputTrigger
{
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
        => actuated ? TriggerPhase.Triggered : TriggerPhase.Idle;
}
