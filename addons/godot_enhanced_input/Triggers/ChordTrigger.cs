using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Implicit: triggered only while <see cref="ChordAction"/> is triggered this tick. Combined with the
/// binding's own value, "Shift + Space" becomes a binding on Space with a chord on a Shift action.
/// </summary>
[Tool]
public partial class ChordTrigger : InputTrigger
{
    /// <inheritdoc/>
    public override bool ReadsOtherActions => true;

    /// <summary>The action that must be triggered at the same time.</summary>
    [Export] public EnhancedInputAction ChordAction { get; set; }

    /// <inheritdoc/>
    public override TriggerRole Role => TriggerRole.Implicit;

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        if (ChordAction == null || context.Player == null) return TriggerPhase.Idle;
        return context.Player.GetPhaseThisTick(ChordAction) == TriggerPhase.Triggered ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
