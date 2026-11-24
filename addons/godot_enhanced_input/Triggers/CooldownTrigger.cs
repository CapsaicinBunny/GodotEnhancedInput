using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Blocker: after the action has fired, it cannot fire again for <see cref="CooldownSeconds"/> — a dash,
/// an ability, a shot with a fire rate. The cooldown belongs to the action (all its bindings share it) and
/// runs from the moment it last became triggered; an activation already under way is never cut short.
/// Add it next to the triggers that decide when the action fires (Pressed, Hold…); on its own, the action
/// fires whenever its value is non-zero and the cooldown allows.
/// </summary>
[Tool]
public partial class CooldownTrigger : InputTrigger
{
    /// <summary>How long the action cannot fire again after it fired.</summary>
    [Export(PropertyHint.Range, "0,30,0.01,or_greater,suffix:s")] public float CooldownSeconds { get; set; } = 0.5f;

    /// <inheritdoc/>
    public override TriggerRole Role => TriggerRole.Blocker;

    /// <inheritdoc/>
    public override bool ReadsOtherActions => true;   // reads its own action's state, kept current step by step

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        if (context.Player == null || context.Action == null) return TriggerPhase.Idle;
        var state = context.Player.GetState(context.Action);
        if (state.IsTriggered) return TriggerPhase.Idle;   // under way: let it finish
        double since = context.Player.InputTime - state.LastTriggeredTime;
        return since < CooldownSeconds - TimeEpsilon ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }

    /// <summary>Seconds left before the action can fire again for a player (0 when ready).</summary>
    public double Remaining(InputPlayer player, EnhancedInputAction action)
    {
        if (player == null || action == null) return 0;
        var state = player.GetState(action);
        return System.Math.Max(0.0, CooldownSeconds - (player.InputTime - state.LastTriggeredTime));
    }
}
