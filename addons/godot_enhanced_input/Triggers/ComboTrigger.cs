using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// Fires when a sequence of other actions is performed in order, each within its time limit.
/// Ongoing while partway through; triggered on the tick the last step completes. The combo starts
/// over when a step's time runs out, when one of its step actions completes out of order, or when
/// a <see cref="CancelActions"/> action triggers. The binding's own value is not used, so the binding
/// can have any source (or none).
/// <para>For fighting-game input, <see cref="ToleranceSeconds"/> lets steps that come within that time of
/// each other arrive in either order (the button a moment before the last direction, two directions of a
/// rolled motion), and <see cref="IgnoreOutOfOrder"/> keeps a stray step action from breaking the combo.</para>
/// </summary>
[Tool]
public partial class ComboTrigger : InputTrigger
{
    /// <inheritdoc/>
    public override bool ReadsOtherActions => true;

    /// <summary>The steps, in order.</summary>
    [Export] public Array<ComboStep> Steps { get; set; } = new();

    /// <summary>Actions that break the combo when they trigger.</summary>
    [Export] public Array<EnhancedInputAction> CancelActions { get; set; } = new();

    /// <summary>
    /// Steps completed up to this long before their turn still count, so near-simultaneous steps may come in
    /// either order. 0: strict order.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.5,0.005,suffix:s")] public float ToleranceSeconds { get; set; }

    /// <summary>When on, a step action completing out of turn is ignored instead of restarting the combo.</summary>
    [Export] public bool IgnoreOutOfOrder { get; set; }

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var player = context.Player;
        var memory = context.Memory; // Count = index of the next step; Timer = time since last step; Custom = ages of step completions.
        if (player == null || Steps.Count == 0) return TriggerPhase.Idle;
        var ages = Ages(memory, context.Delta, player);

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
            // Later steps that already came within the tolerance count now.
            while (ToleranceSeconds > 0f && memory.Count < Steps.Count && ages[memory.Count] <= ToleranceSeconds + TimeEpsilon)
            {
                ages[memory.Count] = double.PositiveInfinity;
                memory.Count++;
            }
            if (memory.Count >= Steps.Count)
            {
                memory.Count = 0;
                ResetAges(ages);
                return TriggerPhase.Triggered;
            }
            return TriggerPhase.Ongoing;
        }

        // Another step's action completing out of order breaks the combo; it may start a new one.
        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (step == null || step.Action == expected?.Action || !Completed(player, step)) continue;
            if (IgnoreOutOfOrder || (ToleranceSeconds > 0f && i > memory.Count)) continue;   // remembered, or ignored
            memory.Count = 0;
            memory.Timer = 0;
            if (Completed(player, Steps[0])) memory.Count = 1;
            break;
        }

        return memory.Count > 0 ? TriggerPhase.Ongoing : TriggerPhase.Idle;
    }

    // How long ago each step's action last completed (infinity: not recently).
    private double[] Ages(TriggerMemory memory, double delta, InputPlayer player)
    {
        if (memory.Custom is not double[] ages || ages.Length != Steps.Count)
        {
            ages = new double[Steps.Count];
            ResetAges(ages);
            memory.Custom = ages;
        }
        for (int i = 0; i < ages.Length; i++)
            ages[i] = Completed(player, Steps[i]) ? 0.0 : ages[i] + delta;
        return ages;
    }

    private static void ResetAges(double[] ages)
    {
        for (int i = 0; i < ages.Length; i++) ages[i] = double.PositiveInfinity;
    }

    private static bool Completed(InputPlayer player, ComboStep step)
        => step?.Action != null && (player.GetEventsThisTick(step.Action) & step.CompletesOn) != 0;
}
