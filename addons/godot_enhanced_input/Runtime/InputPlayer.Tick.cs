using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>True while the scene tree is paused (only when this player is inside a tree).</summary>
    public bool IsTreePaused => IsInsideTree() && GetTree().Paused;

    /// <summary>
    /// Evaluates the input received since the last tick, then the frame:
    /// <list type="number">
    /// <item>polls motion sensors (live) and closes or plays the recorded frame;</item>
    /// <item>for every queued input event, in arrival order: lets time pass up to the moment it arrived for
    /// the actions it touches (with the input as it was), then applies it and evaluates those actions
    /// again; their phase changes are raised at once, in order;</item>
    /// <item>evaluates every action at the end of the frame, with the frame's per-frame amounts (mouse
    /// motion, scrolling, drags, gestures, injections). Held actions raise their steady events here, at
    /// most once per frame.</item>
    /// </list>
    /// Each action's time advances by exactly the frame's delta over the tick, split at the moments its
    /// input changed. Changes are placed in time by when they reached the player (see
    /// <see cref="UseEventTimes"/>); Godot does not give events their own timestamps.
    /// </summary>
    public void Tick(double delta)
    {
        if (_ticking || _dispatching)
        {
            // Called from a trigger, modifier or event callback: a nested tick would advance the state
            // half-way through this one. Ignored; the next regular tick sees everything.
            if (!_warnedReentrantTick)
            {
                _warnedReentrantTick = true;
                GD.PushWarning($"InputPlayer {PlayerIndex}: Tick was called from inside a tick and was ignored.");
            }
            ReentrantTicksIgnored++;
            return;
        }

        ulong now = Raw.Clock();
        if (_lastFrameUsec == 0 || now < _lastFrameUsec) _lastFrameUsec = now;
        if (_replay == null) PollSensorsForTick();   // before the recorded frame closes, so replays see them in the same frame
        delta = BeginTickRecording(delta, now);
        _tick++;
        double frameDelta = Math.Max(0.0, delta);
        UpdateCapture(frameDelta);
        Raw.SetFrameWindow(_lastFrameUsec, now, UseEventTimes || _replay != null);
        _frameStartSeconds = _lastFrameUsec / 1_000_000.0;
        _frameSeconds = (now - _lastFrameUsec) / 1_000_000.0;
        _frameDelta = frameDelta;
        _lastFrameUsec = now;
        _frameTime = 0;

        _ticking = true;
        try
        {
            PrepareTick();

            // Input events, one at a time.
            int steps = 0;
            while (steps < MaxStepsPerTick && Raw.NextEventStep(_stepKeys, out double fraction))
            {
                steps++;
                double at = Math.Max(_frameTime, fraction * frameDelta);
                CollectAffected();
                UpdateIgnoredAll();

                // Time passes up to the event under the input as it was.
                if (at > _frameTime)
                {
                    _frameTime = at;
                    _step++;
                    Raw.ObserveBefore();
                    for (int i = 0; i < _affected.Count; i++)
                    {
                        var state = GetOrCreateState(_affected[i]);
                        if (state.EvalTick != _tick || state.EvalTime < at) Resolve(_affected[i]);
                    }
                    Raw.ObserveStep();
                    Dispatch(final: false);
                    ReplanIfChanged();
                }

                // The event happens.
                _step++;
                _eventPhase = true;
                for (int i = 0; i < _affected.Count; i++) Resolve(_affected[i]);
                _eventPhase = false;
                Dispatch(final: false);
                Raw.EndStep();
                ReplanIfChanged();
            }

            // The frame: every action, with the frame's per-frame amounts.
            _frameTime = frameDelta;
            _step++;
            _finalStep = true;
            Raw.BeginFinalStep();
            UpdateIgnoredAll();
            for (int i = 0; i < _tickActions.Count; i++) Resolve(_tickActions[i]);
            Dispatch(final: true);
            Raw.EndStep();
            _finalStep = false;
            LastTickSteps = steps + 1;
        }
        finally
        {
            _ticking = false;
        }
        _delta = frameDelta;

        EndTick();
        EndTickRecording();
        UpdateHaptics(frameDelta);
    }

    /// <summary>
    /// Place each change at the moment it reached this player within the frame, so time passes between
    /// them (a press and release within one slow frame last as long as they really did, as far as
    /// arrival times tell). Off: every change counts as arriving at the start of the frame. On by default
    /// for players ticked by <see cref="EnhancedInput"/> in real time; off for manual ticking, where the
    /// deltas passed in are not wall-clock time. Godot delivers most events once per frame; enable
    /// <c>input_devices/buffering/agile_event_flushing</c> for finer arrival times.
    /// </summary>
    public bool UseEventTimes { get; set; }

    /// <summary>The clock arrival times are taken from, in microseconds (Godot's monotonic clock by default; tests can set their own).</summary>
    public Func<ulong> Clock
    {
        get => Raw.Clock;
        set => Raw.Clock = value ?? Time.GetTicksUsec;
    }

    /// <summary>Most input events one tick evaluates one by one (more wait for the next tick).</summary>
    public const int MaxStepsPerTick = 128;

    /// <summary>The evaluation steps the last tick ran: one per input event evaluated, plus the frame itself.</summary>
    public int LastTickSteps { get; private set; }

    private ulong _lastFrameUsec;
    private double _frameStartSeconds, _frameSeconds, _frameDelta;
    private double _frameTime;   // seconds into the tick of the step being evaluated
    private bool _finalStep;
    private bool _eventPhase;   // evaluating an input event with its changes applied
    private readonly List<SourceKey> _stepKeys = new();
    private readonly List<GameAction> _affected = new();
    private readonly HashSet<GameAction> _affectedSet = new();
    private readonly List<GameAction> _stepResolved = new();
    private readonly Dictionary<SourceKey, List<GameAction>> _actionsByKey = new();
    private readonly List<GameAction> _alwaysEvaluate = new();

    /// <summary>The actions an input event touches: those reading its channels, plus those whose triggers read other actions.</summary>
    private void CollectAffected()
    {
        _affected.Clear();
        _affectedSet.Clear();
        for (int i = 0; i < _stepKeys.Count; i++)
        {
            var key = _stepKeys[i];
            AddAffected(key);
            if (key.Device != SourceKey.AnyDevice) AddAffected(key with { Device = SourceKey.AnyDevice });
        }
        for (int i = 0; i < _alwaysEvaluate.Count; i++)
            if (_affectedSet.Add(_alwaysEvaluate[i])) _affected.Add(_alwaysEvaluate[i]);
    }

    private void AddAffected(in SourceKey key)
    {
        if (!_actionsByKey.TryGetValue(key, out var actions)) return;
        for (int i = 0; i < actions.Count; i++)
            if (_affectedSet.Add(actions[i])) _affected.Add(actions[i]);
    }

    /// <summary>A callback changed contexts, the mode or bindings mid-tick: the rest of the tick uses the new plan.</summary>
    private void ReplanIfChanged()
    {
        if (PlanIsStale()) PrepareTick();
    }

    private void PollSensorsForTick()
    {
        if (PlanIsStale()) CompilePlan();
        _polledSensors.Clear();
        for (int p = 0; p < _plan.Count; p++)
            if (_plan[p].PollsSensors) PollSensors(_plan[p].Source);
    }

    /// <summary>
    /// Forces the active bindings to be compiled again on the next tick. Needed only after changing a
    /// binding, source, modifier list or trigger list in place while its context is active (adding or
    /// removing bindings, contexts, rebinding and mode changes are noticed on their own).
    /// </summary>
    public void RefreshBindings() => _planDirty = true;

    /// <summary>How many times the active bindings were compiled (for profiling and tests).</summary>
    public int PlanCompileCount { get; private set; }

    /// <summary>How many calls to <see cref="Tick"/> were ignored because they came from inside a tick.</summary>
    public int ReentrantTicksIgnored { get; private set; }

    private bool _warnedReentrantTick;

    private bool PlanIsStale()
    {
        if (_planDirty) return true;
        foreach (var entry in _ordered)
        {
            var context = entry.Context;
            var bindings = context.Bindings;
            if (entry.PlanVersion != context.Version || !ReferenceEquals(entry.PlanArray, bindings) || entry.PlanCount != bindings.Count)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reads every active binding once — its action, effective source, the channels it reads, and its
    /// modifier and trigger lists — in context order. Reused until something it depends on changes.
    /// </summary>
    private void CompilePlan()
    {
        _planDirty = false;
        PlanCompileCount++;
        OrderEntries();
        _plan.Clear();
        foreach (var list in _bindingsByAction.Values) list.Clear();

        foreach (var entry in _ordered)
        {
            var context = entry.Context;
            var bindings = context.Bindings;
            entry.PlanVersion = context.Version;
            entry.PlanArray = bindings;
            int count = bindings.Count;
            entry.PlanCount = count;

            for (int i = 0; i < count; i++)
            {
                var binding = bindings[i];
                var action = binding?.Action;
                if (action == null) continue;
                if (!IsBindingActive(binding)) continue;   // not in the active mode's groups

                var source = GetEffectiveSource(binding);
                _keyScratch.Clear();
                source?.CollectKeys(_keyScratch);
                var compiled = new CompiledBinding
                {
                    Entry = entry,
                    Index = i,
                    Binding = binding,
                    Action = action,
                    Source = source,
                    Keys = _keyScratch.ToArray(),
                    Modifiers = ToArray(binding.Modifiers),
                    Triggers = ToArray(binding.Triggers),
                    PollsSensors = source != null && UsesSensors(source),
                };
                compiled.HasTriggers = HasTriggers(compiled.Triggers);
                _plan.Add(compiled);

                if (!_bindingsByAction.TryGetValue(action, out var list))
                    _bindingsByAction[action] = list = new List<CompiledBinding>();
                list.Add(compiled);
            }
        }

        // Which actions each channel drives, and which actions look at other actions.
        _actionsByKey.Clear();
        _alwaysEvaluate.Clear();
        foreach (var compiled in _plan)
        {
            for (int k = 0; k < compiled.Keys.Length; k++)
            {
                if (!_actionsByKey.TryGetValue(compiled.Keys[k], out var actions))
                    _actionsByKey[compiled.Keys[k]] = actions = new List<GameAction>(2);
                if (!actions.Contains(compiled.Action)) actions.Add(compiled.Action);
            }
            if (ReadsOtherActions(compiled.Triggers) || ReadsOtherActions(compiled.Action.Triggers))
                if (!_alwaysEvaluate.Contains(compiled.Action)) _alwaysEvaluate.Add(compiled.Action);
        }

        // Drop lists of actions no longer bound (keeps the dictionary from growing forever).
        _keyActions.Clear();
        foreach (var pair in _bindingsByAction)
            if (pair.Value.Count == 0) _keyActions.Add(pair.Key);
        foreach (var action in _keyActions) _bindingsByAction.Remove(action);
    }

    private readonly List<GameAction> _keyActions = new();

    private static T[] ToArray<[MustBeVariant] T>(Godot.Collections.Array<T> array) where T : GodotObject
    {
        if (array == null || array.Count == 0) return System.Array.Empty<T>();
        var result = new T[array.Count];
        for (int i = 0; i < result.Length; i++) result[i] = array[i];
        return result;
    }

    private static bool ReadsOtherActions(IList<InputTrigger> triggers)
    {
        if (triggers == null) return false;
        for (int i = 0; i < triggers.Count; i++)
            if (triggers[i] != null && triggers[i].ReadsOtherActions) return true;
        return false;
    }

    private static bool UsesSensors(InputSource source)
    {
        if (source.Kind == SourceKind.Motion) return true;
        if (source.Kind != SourceKind.Composite || source.Parts == null) return false;
        foreach (var part in source.Parts)
            if (part != null && UsesSensors(part)) return true;
        return false;
    }

    private void PrepareTick()
    {
        if (PlanIsStale()) CompilePlan();

        _tickActions.Clear();
        _tickActionSet.Clear();
        _consumedBy.Clear();
        _blockRank = int.MaxValue;

        foreach (var entry in _ordered)
            if (entry.Context.BlockLowerPriority && _blockRank == int.MaxValue) _blockRank = entry.Rank;

        for (int p = 0; p < _plan.Count; p++)
        {
            var compiled = _plan[p];
            var action = compiled.Action;
            AddTickAction(action);

            // Consumption depends only on which contexts are active, never on this tick's results, so
            // actions can be evaluated in any order. Channels keep their device or finger; a source
            // reading "any device" consumes the channel on every device.
            if (action.ConsumeInput)
            {
                var keys = compiled.Keys;
                for (int k = 0; k < keys.Length; k++) _consumedBy.TryAdd(Raw.PhysicalOf(keys[k]), compiled.Entry.Rank);
            }
        }

        foreach (var pair in _injections)
            if (pair.Value.Continuous || pair.Value.HasPeak) AddTickAction(pair.Key);

        // Actions that are still active but no longer bound must be ticked so they finish.
        foreach (var pair in _states)
            if (pair.Value.IsActive) AddTickAction(pair.Key);
    }

    private void UpdateIgnoredAll()
    {
        foreach (var entry in _ordered) UpdateIgnored(entry);
    }

    private void AddTickAction(GameAction action)
    {
        if (_tickActionSet.Add(action)) _tickActions.Add(action);
    }

    private void UpdateIgnored(ContextEntry entry)
    {
        if (entry.Ignored.Count == 0) return;
        _keyScratch.Clear();
        foreach (var key in entry.Ignored)
            if (ReadRaw(key, null) == Vector4.Zero) _keyScratch.Add(key);
        foreach (var key in _keyScratch) entry.Ignored.Remove(key);
    }

    // ------------------------------------------------------------------
    //  Dispatch
    // ------------------------------------------------------------------

    private static readonly ActionEvents[] EventOrder =
    {
        ActionEvents.Started, ActionEvents.Ongoing, ActionEvents.Triggered, ActionEvents.Completed, ActionEvents.Canceled,
    };

    /// <summary>Where an event of this action came from: binding, exact channel and device.</summary>
    private InputProvenance Provenance(ActionState state)
    {
        bool pass = state.PassBinding != null && !_finalStep && state.Action?.Evaluation == ActionEvaluation.PassThrough;
        var binding = pass ? state.PassBinding : state.SourceBinding;
        var keys = pass ? state.PassKeys : state.SourceKeys;
        SourceKey? source = keys != null && Raw.TryStrongestChannel(keys, out var exact) ? exact : null;
        var device = source.HasValue && GetParent() is EnhancedInput system ? system.GetDeviceForChannel(source.Value) : null;
        return new InputProvenance(binding, source, device, pass ? state.PassValue : null);
    }

    /// <summary>The player's clock, in seconds, at the step being evaluated.</summary>
    private double StepClock => _frameStartSeconds + (_frameDelta > 0 ? _frameTime / _frameDelta * _frameSeconds : 0.0);

    /// <summary>
    /// Raises the events of the actions evaluated in this step, in evaluation order. Phase changes (started,
    /// triggered from ongoing or idle, completed, canceled…) are raised at once; steady events of a held
    /// action (triggered again, still ongoing) only at the end of the frame, at most once per frame, so an
    /// unrelated input never makes a held action fire twice in one frame.
    /// </summary>
    private void Dispatch(bool final)
    {
        _dispatching = true;
        try
        {
            for (int i = 0; i < _stepResolved.Count; i++)
            {
                var action = _stepResolved[i];
                if (!_states.TryGetValue(action, out var state) || state.StepId != _step || state.StepEvents == ActionEvents.None) continue;
                if (state.FrameDispatchTick != _tick)
                {
                    state.FrameDispatchTick = _tick;
                    state.FrameDispatched = ActionEvents.None;
                }
                bool change = state.PreviousPhase != state.Phase
                    || (state.StepEvents & (ActionEvents.Started | ActionEvents.Completed | ActionEvents.Canceled)) != 0;
                var policy = action.Evaluation;

                // Pass-through: every input event on one of its bindings is reported on its own.
                if (policy == ActionEvaluation.PassThrough && !final && state.PassBinding != null && !change
                    && (state.Phase == TriggerPhase.Triggered || state.PassValue.IsNonZero))
                {
                    state.FrameDispatched |= ActionEvents.Triggered;
                    Raise(action, state, ActionEvents.Triggered);
                    continue;
                }

                foreach (var flag in EventOrder)
                {
                    if ((state.StepEvents & flag) == 0) continue;
                    if (!change)
                    {
                        // A steady event of a held action: at most once per frame, at the frame's end, and
                        // only as the action's policy allows.
                        if (!final || (state.FrameDispatched & flag) != 0) continue;
                        if (policy == ActionEvaluation.EventDriven) continue;
                        if (policy == ActionEvaluation.PassThrough && (state.FrameDispatched & ActionEvents.Triggered) != 0) continue;
                        if (policy == ActionEvaluation.OnChange && state.Value == state.LastDispatchedValue) continue;
                    }
                    state.FrameDispatched |= flag;
                    if (flag == ActionEvents.Triggered) state.LastDispatchedValue = state.Value;
                    Raise(action, state, flag);
                }
            }
        }
        finally
        {
            _stepResolved.Clear();
            _dispatching = false;
            PurgeInactiveSubscriptions();
        }
    }

    private void Raise(GameAction action, ActionState state, ActionEvents flag)
    {
        var args = new ActionEventArgs(this, action, flag, state, Provenance(state), StepClock);

        if (_subscriptions.TryGetValue(action, out var list))
        {
            int count = list.Count;
            for (int i = 0; i < count && i < list.Count; i++)
            {
                var subscription = list[i];
                if (!subscription.IsActive || (subscription.Events & flag) == 0) continue;
                try { subscription.Callback(args); }
                catch (Exception e) { GD.PushError($"[GodotEnhancedInput] {action} {flag} callback threw: {e}"); }
            }
        }

        if (ActionEventRaised != null)
        {
            try { ActionEventRaised(args); }
            catch (Exception e) { GD.PushError($"[GodotEnhancedInput] ActionEventRaised handler threw: {e}"); }
        }

        var signal = flag switch
        {
            ActionEvents.Started => SignalName.ActionStarted,
            ActionEvents.Ongoing => SignalName.ActionOngoing,
            ActionEvents.Triggered => SignalName.ActionTriggered,
            ActionEvents.Completed => SignalName.ActionCompleted,
            _ => SignalName.ActionCanceled,
        };
        if (!EmitActionSignals) return;
        // The argument list and the action's variant are reused, so emitting does not allocate.
        if (!state.SignalArgReady)
        {
            state.SignalArg = Variant.From(action);
            state.SignalArgReady = true;
        }
        _signalArgs[0] = state.SignalArg;
        EmitSignal(signal, _signalArgs);
        _signalArgs[0] = default;
    }

    private readonly Variant[] _signalArgs = new Variant[1];

    /// <summary>
    /// Emit the action signals (<c>ActionStarted</c>, <c>ActionTriggered</c>…) as well as the C# callbacks.
    /// Turn off when only <see cref="Bind"/> and <see cref="ActionEventRaised"/> are used, to save the
    /// cost of a Godot signal per event.
    /// </summary>
    [Export] public bool EmitActionSignals { get; set; } = true;

    private void PurgeInactiveSubscriptions()
    {
        foreach (var list in _subscriptions.Values)
            list.RemoveAll(static s => !s.IsActive);
    }

    private void EndTick()
    {
        Raw.EndTick();
        foreach (var injection in _injections.Values) injection.HasPeak = false;
    }
}
