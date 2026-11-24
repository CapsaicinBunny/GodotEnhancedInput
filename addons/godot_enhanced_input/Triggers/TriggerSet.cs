using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Combines the phases of a set of triggers (a binding's or an action's) into one phase:
/// <list type="bullet">
/// <item>A triggered <see cref="TriggerRole.Blocker"/> makes the set <see cref="TriggerPhase.Idle"/>.</item>
/// <item>The explicit side is satisfied when any explicit trigger is triggered, or — when the set has no
/// explicit triggers — when the value is non-zero.</item>
/// <item>The set is <see cref="TriggerPhase.Triggered"/> when the explicit side is satisfied and every
/// <see cref="TriggerRole.Implicit"/> trigger is triggered.</item>
/// <item>Otherwise it is <see cref="TriggerPhase.Ongoing"/> when the explicit side shows progress
/// (an explicit trigger ongoing or triggered, or a non-zero value when there are no explicit triggers)
/// or an implicit trigger is ongoing; else <see cref="TriggerPhase.Idle"/>.</item>
/// </list>
/// An empty set is triggered while the value is non-zero.
/// </summary>
public static class TriggerSet
{
    /// <summary>Evaluates a set for a three-component value.</summary>
    public static TriggerPhase Evaluate(IReadOnlyList<InputTrigger> triggers, IReadOnlyList<TriggerMemory> memories, Vector3 value, ActionValueKind kind, double delta, InputPlayer player)
        => Evaluate(triggers, memories, new Vector4(value.X, value.Y, value.Z, 0f), kind, delta, player);

    /// <summary>Evaluates every trigger (none are skipped, so each keeps its memory current) and combines them.</summary>
    public static TriggerPhase Evaluate(
        IReadOnlyList<InputTrigger> triggers,
        IReadOnlyList<TriggerMemory> memories,
        Vector4 value,
        ActionValueKind kind,
        double delta,
        InputPlayer player)
        => Evaluate(triggers, memories, value, kind, delta, player, null);

    /// <summary>Evaluates a set for a known action (triggers can then look at the action's state).</summary>
    public static TriggerPhase Evaluate(
        IReadOnlyList<InputTrigger> triggers,
        IReadOnlyList<TriggerMemory> memories,
        Vector4 value,
        ActionValueKind kind,
        double delta,
        InputPlayer player,
        EnhancedInputAction action)
    {
        bool nonZero = !ActionValue.IsZeroOf(kind, value);
        int count = triggers?.Count ?? 0;

        bool hasExplicit = false, explicitTriggered = false, explicitProgress = false;
        bool allImplicit = true, implicitOngoing = false, blocked = false;
        bool any = false;

        for (int i = 0; i < count; i++)
        {
            var trigger = triggers[i];
            if (trigger == null) continue;
            any = true;

            var phase = trigger.Evaluate(new TriggerContext(value, kind, delta, player, memories[i], action));
            switch (trigger.Role)
            {
                case TriggerRole.Explicit:
                    hasExplicit = true;
                    if (phase == TriggerPhase.Triggered) explicitTriggered = true;
                    if (phase != TriggerPhase.Idle) explicitProgress = true;
                    break;
                case TriggerRole.Implicit:
                    if (phase != TriggerPhase.Triggered) allImplicit = false;
                    if (phase == TriggerPhase.Ongoing) implicitOngoing = true;
                    break;
                case TriggerRole.Blocker:
                    if (phase == TriggerPhase.Triggered) blocked = true;
                    break;
            }
        }

        if (!any) return nonZero ? TriggerPhase.Triggered : TriggerPhase.Idle;
        if (blocked) return TriggerPhase.Idle;

        bool explicitSatisfied = hasExplicit ? explicitTriggered : nonZero;
        if (hasExplicit == false) explicitProgress = nonZero;

        if (explicitSatisfied && allImplicit) return TriggerPhase.Triggered;
        if (explicitProgress || implicitOngoing) return TriggerPhase.Ongoing;
        return TriggerPhase.Idle;
    }
}
