using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// One action's state for one player, updated every tick: value, phase, the events of the last tick,
/// and timings.
/// </summary>
public sealed class ActionState
{
    internal ActionState(GameAction action)
    {
        Action = action;
        Value = ActionValue.Zero(action?.ValueKind ?? ActionValueKind.Digital);
    }

    /// <summary>The action this state belongs to.</summary>
    public GameAction Action { get; }

    /// <summary>The value after all modifiers, whatever the phase.</summary>
    public ActionValue Value { get; private set; }

    /// <summary>The phase this tick.</summary>
    public TriggerPhase Phase { get; private set; }

    /// <summary>The phase the tick before.</summary>
    public TriggerPhase PreviousPhase { get; private set; }

    /// <summary>The events raised by the last tick.</summary>
    public ActionEvents Events { get; private set; }

    /// <summary>
    /// Seconds since the action started (left idle), including the tick it started on. Kept on the
    /// tick it completes or cancels; zero while idle afterwards.
    /// </summary>
    public double ElapsedSeconds { get; private set; }

    /// <summary>Seconds spent triggered since the action started. Same lifetime as <see cref="ElapsedSeconds"/>.</summary>
    public double TriggeredSeconds { get; private set; }

    /// <summary>The player tick on which this state was last evaluated.</summary>
    public ulong LastTick { get; internal set; }

    /// <summary>True when the phase is <see cref="TriggerPhase.Triggered"/>.</summary>
    public bool IsTriggered => Phase == TriggerPhase.Triggered;

    /// <summary>True when the phase is not idle or something was raised this tick (so it still needs ticking).</summary>
    internal bool IsActive => Phase != TriggerPhase.Idle || Events != ActionEvents.None;

    // Evaluation bookkeeping (owned by InputPlayer).
    internal ulong ResolvedTick;
    internal bool Resolving;
    internal ModifierMemory[] ModifierMemories = System.Array.Empty<ModifierMemory>();
    internal TriggerMemory[] TriggerMemories = System.Array.Empty<TriggerMemory>();

    /// <summary>
    /// Moves to the next phase and works out the events of the transition:
    /// Idle→Ongoing: Started+Ongoing · Idle→Triggered: Started+Triggered · Ongoing→Ongoing: Ongoing ·
    /// Ongoing→Triggered: Triggered · Triggered→Triggered: Triggered · Triggered→Ongoing: Ongoing ·
    /// Ongoing→Idle: Canceled · Triggered→Idle: Completed · Idle→Idle: nothing.
    /// </summary>
    internal void Advance(TriggerPhase next, ActionValue value, double delta)
    {
        var previous = Phase;
        PreviousPhase = previous;
        Phase = next;
        Value = value;

        var events = ActionEvents.None;
        if (next == TriggerPhase.Idle)
        {
            if (previous == TriggerPhase.Triggered) events = ActionEvents.Completed;
            else if (previous == TriggerPhase.Ongoing) events = ActionEvents.Canceled;
            else
            {
                ElapsedSeconds = 0;
                TriggeredSeconds = 0;
            }
        }
        else
        {
            if (previous == TriggerPhase.Idle)
            {
                events |= ActionEvents.Started;
                ElapsedSeconds = 0;
                TriggeredSeconds = 0;
            }

            ElapsedSeconds += delta;
            if (next == TriggerPhase.Triggered)
            {
                events |= ActionEvents.Triggered;
                TriggeredSeconds += delta;
            }
            else
            {
                events |= ActionEvents.Ongoing;
            }
        }

        Events = events;
    }

    /// <summary>Back to idle without raising anything (used when a player is reset).</summary>
    internal void Reset()
    {
        Phase = TriggerPhase.Idle;
        PreviousPhase = TriggerPhase.Idle;
        Events = ActionEvents.None;
        ElapsedSeconds = 0;
        TriggeredSeconds = 0;
        Value = ActionValue.Zero(Action?.ValueKind ?? ActionValueKind.Digital);
        foreach (var m in ModifierMemories) m?.Reset();
        foreach (var m in TriggerMemories) m?.Reset();
    }

    public override string ToString() => $"{Action}: {Phase} {Value} [{Events}] {ElapsedSeconds:0.###}s/{TriggeredSeconds:0.###}s";
}

/// <summary>Passed to action callbacks: one event of one action.</summary>
public readonly struct ActionEventArgs
{
    public ActionEventArgs(InputPlayer player, GameAction action, ActionEvents @event, ActionState state)
    {
        Player = player;
        Action = action;
        Event = @event;
        Value = state.Value;
        Phase = state.Phase;
        ElapsedSeconds = state.ElapsedSeconds;
        TriggeredSeconds = state.TriggeredSeconds;
    }

    /// <summary>The player the event belongs to.</summary>
    public InputPlayer Player { get; }

    /// <summary>The action.</summary>
    public GameAction Action { get; }

    /// <summary>Exactly one event flag.</summary>
    public ActionEvents Event { get; }

    /// <summary>The action's value this tick.</summary>
    public ActionValue Value { get; }

    /// <summary>The action's phase this tick.</summary>
    public TriggerPhase Phase { get; }

    /// <summary>See <see cref="ActionState.ElapsedSeconds"/>.</summary>
    public double ElapsedSeconds { get; }

    /// <summary>See <see cref="ActionState.TriggeredSeconds"/>.</summary>
    public double TriggeredSeconds { get; }
}
