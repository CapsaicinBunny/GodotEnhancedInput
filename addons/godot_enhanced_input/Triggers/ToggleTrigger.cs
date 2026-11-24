using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Each press flips the action on or off: triggered while on (toggle crouch, toggle aim). Starts off,
/// and starts off again when the binding is suppressed (a menu opens) or the player is reset.
/// </summary>
[Tool]
public partial class ToggleTrigger : InputTrigger
{
    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var memory = context.Memory;
        if (actuated && !wasActuated) memory.Flag = !memory.Flag;
        return memory.Flag ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
