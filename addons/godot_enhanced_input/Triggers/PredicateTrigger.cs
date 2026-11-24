using System;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// A trigger whose logic is a C# function, for one-off rules that do not deserve a class:
/// <c>new PredicateTrigger { Evaluator = ctx => ctx.Value.X &gt; 0.5f &amp;&amp; IsGrounded ? TriggerPhase.Triggered : TriggerPhase.Idle }</c>.
/// The function receives the context (value, delta, player, memory). Set in code; a resource saved
/// with no function stays idle.
/// </summary>
[Tool]
public partial class PredicateTrigger : InputTrigger
{
    /// <inheritdoc/>
    public override bool ReadsOtherActions => true;

    /// <summary>How this trigger combines with others.</summary>
    [Export] public TriggerRole TriggerRole { get; set; } = TriggerRole.Explicit;

    /// <summary>The rule. Null: idle.</summary>
    public Func<TriggerContext, TriggerPhase> Evaluator { get; set; }

    /// <inheritdoc/>
    public override TriggerRole Role => TriggerRole;

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
        => Evaluator?.Invoke(context) ?? TriggerPhase.Idle;
}
