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

    /// <summary>
    /// The action's value: the combined value of the bindings that fire this tick (bindings without
    /// triggers, and bindings whose triggers are triggered), after the action's modifiers. Bindings
    /// held back by their triggers (a hold not reached, a chord missing, a threshold not met) do not
    /// add to it.
    /// </summary>
    public ActionValue Value { get; private set; }

    /// <summary>
    /// The combined value of every evaluated binding (after binding modifiers, before the action's
    /// modifiers), whether or not its triggers fire — e.g. how far an analog trigger is pulled before it
    /// reaches the threshold, or the progress of a charge. Suppressed bindings are not included.
    /// </summary>
    public ActionValue RawValue { get; private set; }

    /// <summary>
    /// The untouched input: the evaluated bindings' source values combined before any modifier, trigger
    /// or action processing (shaped to the action's kind). Read it for raw sensor or stick values next to
    /// the processed <see cref="Value"/>.
    /// </summary>
    public ActionValue InputValue { get; internal set; }

    /// <summary>The phase this tick.</summary>
    public TriggerPhase Phase { get; private set; }

    /// <summary>The phase the tick before.</summary>
    public TriggerPhase PreviousPhase { get; private set; }

    /// <summary>
    /// The events raised during the last tick. When several changes arrived in one frame (a press and its
    /// release), the tick evaluates them in order and this holds all their events (Started, Triggered,
    /// Completed…); callbacks receive them one by one.
    /// </summary>
    public ActionEvents Events { get; private set; }

    /// <summary>The events of the latest evaluation step only.</summary>
    public ActionEvents StepEvents { get; private set; }

    /// <summary>
    /// Why the action last went idle: <see cref="InputEndReason.Released"/> for a genuine release, or the
    /// reason it was cut off (device lost, focus lost, context removed…). Cut-off actions raise
    /// <see cref="ActionEvents.Canceled"/>, never <see cref="ActionEvents.Completed"/>, and release-based
    /// triggers do not fire for them.
    /// </summary>
    public InputEndReason EndReason { get; private set; }

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
    internal bool IsActive => Phase != TriggerPhase.Idle || StepEvents != ActionEvents.None;

    // Evaluation bookkeeping (owned by InputPlayer).
    internal ulong ResolvedTick;
    internal ulong StepId;
    internal bool HadBindings;
    internal bool ToggleOn;
    internal bool TogglePressed;
    internal Vector4 ToggleValue;
    internal bool Resolving;
    internal ModifierMemory[] ModifierMemories = System.Array.Empty<ModifierMemory>();
    internal TriggerMemory[] TriggerMemories = System.Array.Empty<TriggerMemory>();

    /// <summary>
    /// Moves to the next phase and works out the events of the transition:
    /// Idle→Ongoing: Started+Ongoing · Idle→Triggered: Started+Triggered · Ongoing→Ongoing: Ongoing ·
    /// Ongoing→Triggered: Triggered · Triggered→Triggered: Triggered · Triggered→Ongoing: Ongoing ·
    /// Ongoing→Idle: Canceled · Triggered→Idle: Completed · Idle→Idle: nothing.
    /// </summary>
    // The action as a signal argument, made once.
    internal Variant SignalArg;
    internal bool SignalArgReady;

    internal void Advance(TriggerPhase next, ActionValue value, double delta) => Advance(next, value, delta, value);

    internal void Advance(TriggerPhase next, ActionValue value, double delta, ActionValue rawValue) => Advance(next, value, delta, rawValue, 0, null);

    internal void Advance(TriggerPhase next, ActionValue value, double delta, ActionValue rawValue, ulong tick, InputEndReason? cut)
    {
        var previous = Phase;
        PreviousPhase = previous;
        Phase = next;
        Value = value;
        RawValue = rawValue;

        var events = ActionEvents.None;
        if (next == TriggerPhase.Idle)
        {
            if (previous != TriggerPhase.Idle) EndReason = cut ?? InputEndReason.Released;
            if (previous == TriggerPhase.Triggered) events = cut.HasValue ? ActionEvents.Canceled : ActionEvents.Completed;
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

        StepEvents = events;
        if (tick == 0 || LastTick != tick) Events = events;
        else Events |= events;
    }

    /// <summary>Back to idle without raising anything (used when a player is reset).</summary>
    internal void Reset()
    {
        Phase = TriggerPhase.Idle;
        PreviousPhase = TriggerPhase.Idle;
        Events = ActionEvents.None;
        StepEvents = ActionEvents.None;
        ElapsedSeconds = 0;
        TriggeredSeconds = 0;
        Value = ActionValue.Zero(Action?.ValueKind ?? ActionValueKind.Digital);
        RawValue = Value;
        ResetMemories();
    }

    /// <summary>Forgets the action-level modifier and trigger memories (when the action loses all its input).</summary>
    internal void ResetMemories()
    {
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
