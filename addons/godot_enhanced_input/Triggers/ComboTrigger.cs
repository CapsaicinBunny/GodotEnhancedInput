using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// Fires when a sequence of other actions is performed in order, each within its time limit.
/// Ongoing while partway through; triggered on the tick the last step completes. The combo starts
/// over when a step's time runs out, when one of its step actions completes out of order, or when
/// a <see cref="CancelActions"/> action triggers. The binding's own value is not used, so the binding
/// can have any source (or none).
/// </summary>
[Tool]
[GlobalClass]
public partial class ComboTrigger : InputTrigger
{
    public override bool ReadsOtherActions => true;

    /// <summary>The steps, in order.</summary>
    [Export] public Array<ComboStep> Steps { get; set; } = new();

    /// <summary>Actions that break the combo when they trigger.</summary>
    [Export] public Array<GameAction> CancelActions { get; set; } = new();

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var player = context.Player;
        var memory = context.Memory; // Count = index of the next step; Timer = time since last step.
        if (player == null || Steps.Count == 0) return TriggerPhase.Idle;

        foreach (var cancel in CancelActions)
        {
            if (cancel != null && (player.GetEventsThisTick(cancel) & ActionEvents.Triggered) != 0)
            {
                memory.Count = 0;
                memory.Timer = 0;
                return TriggerPhase.Idle;
            }
        }

        if (memory.Count > 0)
        {
            memory.Timer += context.Delta;
            var limit = Steps[memory.Count].TimeLimit;
            if (limit > 0f && memory.Timer > limit + TimeEpsilon)
            {
                memory.Count = 0;
                memory.Timer = 0;
            }
        }

        var expected = Steps[memory.Count];
        if (Completed(player, expected))
        {
            memory.Count++;
            memory.Timer = 0;
            if (memory.Count >= Steps.Count)
            {
                memory.Count = 0;
                return TriggerPhase.Triggered;
            }
            return TriggerPhase.Ongoing;
        }

        // Another step's action completing out of order breaks the combo; it may start a new one.
        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (step == null || step.Action == expected?.Action || !Completed(player, step)) continue;
            memory.Count = 0;
            memory.Timer = 0;
            if (Completed(player, Steps[0])) memory.Count = 1;
            break;
        }

        return memory.Count > 0 ? TriggerPhase.Ongoing : TriggerPhase.Idle;
    }

    private static bool Completed(InputPlayer player, ComboStep step)
        => step?.Action != null && (player.GetEventsThisTick(step.Action) & step.CompletesOn) != 0;
}
