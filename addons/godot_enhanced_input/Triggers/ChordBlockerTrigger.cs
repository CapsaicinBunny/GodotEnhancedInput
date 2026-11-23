using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Blocker: keeps its set idle while <see cref="BlockingAction"/> is triggered this tick. Put it on a
/// plain action (Space → Jump) so it stays quiet while a chorded variant (Shift + Space → Dash) fires.
/// </summary>
[Tool]
[GlobalClass]
public partial class ChordBlockerTrigger : InputTrigger
{
    /// <summary>The action whose triggering blocks this set.</summary>
    [Export] public GameAction BlockingAction { get; set; }

    public override TriggerRole Role => TriggerRole.Blocker;

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        if (BlockingAction == null || context.Player == null) return TriggerPhase.Idle;
        return context.Player.GetPhaseThisTick(BlockingAction) == TriggerPhase.Triggered ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
