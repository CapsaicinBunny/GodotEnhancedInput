using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Ongoing while the value is actuated; triggered for one tick on release if it was released within
/// <see cref="TapSeconds"/>. Held longer, it goes idle (canceling the action) until released.
/// </summary>
[Tool]
[GlobalClass]
public partial class TapTrigger : InputTrigger
{
    /// <summary>The longest press that still counts as a tap.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,or_greater,suffix:s")] public float TapSeconds { get; set; } = 0.2f;

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var memory = context.Memory;
        if (actuated)
        {
            memory.Timer += context.Delta;
            return memory.Timer <= TapSeconds + TimeEpsilon ? TriggerPhase.Ongoing : TriggerPhase.Idle;
        }

        bool fire = wasActuated && memory.Timer <= TapSeconds + TimeEpsilon;
        memory.Timer = 0;
        return fire ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
