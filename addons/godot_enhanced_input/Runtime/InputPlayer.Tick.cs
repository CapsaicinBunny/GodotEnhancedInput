using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

public partial class InputPlayer : IChannelMask
{
    /// <summary>True while the scene tree is paused (only when this player is inside a tree).</summary>
    public bool IsTreePaused => IsInsideTree() && GetTree().Paused;

    /// <summary>
    /// Evaluates every action once:
    /// <list type="number">
    /// <item>orders the active contexts and works out which sources each one consumes or blocks;</item>
    /// <item>for each action (lazily, so triggers can ask for other actions): reads its bindings' sources,
    /// applies binding modifiers and triggers, combines the bindings that fire, applies the action's
    /// modifiers and triggers, and moves the phase state machine;</item>
    /// <item>raises callbacks and signals for the events of this tick;</item>
    /// <item>moves raw input on by one queued change and clears per-tick data (mouse motion, one-tick injections).</item>
    /// </list>
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

        delta = BeginTickRecording(delta);
        _tick++;
        double frameDelta = Math.Max(0.0, delta);
        _delta = frameDelta;
        UpdateCapture(_delta);

        // One step per run of queued changes (see RawInputState): every change received since the last
        // tick is evaluated now, in arrival order. Earlier steps are instants; the last one is "now" and
        // advances time by the frame's delta.
        for (int step = 0; ; step++)
        {
            _step++;
            LastTickSteps = step + 1;
            bool last = Raw.BeginStep(forceLast: step + 1 >= MaxStepsPerTick);
            _delta = last ? frameDelta : 0;
            _ticking = true;
            try
            {
                PrepareTick(last);
                for (int i = 0; i < _tickActions.Count; i++) Resolve(_tickActions[i]);
            }
            finally
            {
                _ticking = false;
            }
            Dispatch();
            Raw.EndStep();
            if (last) break;
        }
        _delta = frameDelta;

        EndTick();
        EndTickRecording();
        UpdateHaptics(frameDelta);
    }

    /// <summary>Most evaluation steps one tick runs (changes beyond that wait for the next tick).</summary>
    public const int MaxStepsPerTick = 32;

    /// <summary>The evaluation steps the last tick ran (1 unless several changes of one channel arrived in one frame).</summary>
    public int LastTickSteps { get; private set; }

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

    private static bool UsesSensors(InputSource source)
    {
        if (source.Kind == SourceKind.Motion) return true;
        if (source.Kind != SourceKind.Composite || source.Parts == null) return false;
        foreach (var part in source.Parts)
            if (part != null && UsesSensors(part)) return true;
        return false;
    }

    private void PrepareTick(bool firstStep = true)
    {
        if (PlanIsStale()) CompilePlan();

        _tickActions.Clear();
        _tickActionSet.Clear();
        _consumedBy.Clear();
        _polledSensors.Clear();
        _blockRank = int.MaxValue;

        foreach (var entry in _ordered)
        {
            UpdateIgnored(entry);
            if (entry.Context.BlockLowerPriority && _blockRank == int.MaxValue) _blockRank = entry.Rank;
        }

        for (int p = 0; p < _plan.Count; p++)
        {
            var compiled = _plan[p];
            var action = compiled.Action;
            AddTickAction(action);
            if (firstStep && compiled.PollsSensors) PollSensors(compiled.Source);

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

    /// <summary>
    /// Evaluates one action for this tick.
    /// <list type="bullet">
    /// <item>A binding is <b>suppressed</b> when its context is blocked, the tree is paused (and the action
    /// does not run while paused), or every channel it reads is consumed by a higher context or ignored
    /// until released. A suppressed binding is not evaluated at all: its modifier and trigger memories are
    /// reset and it neither contributes a value nor a phase — so blocking never looks like a release.</item>
    /// <item>A binding <b>fires</b> when it has no triggers, or when its triggers are triggered. Only firing
    /// bindings contribute to the action's value; bindings whose triggers are idle or ongoing do not.</item>
    /// <item>With no action triggers, the phase is the strongest binding phase (a trigger-less binding counts
    /// as triggered while its value is non-zero). With action triggers, the action's triggers decide on the
    /// combined value, so release, tap and hold-release triggers work at action level; the action shows
    /// ongoing while a binding is partway (e.g. a chord is missing).</item>
    /// <item>When nothing is left to evaluate (all bindings suppressed, none bound, no injection), the action
    /// goes idle and its own memories are reset.</item>
    /// </list>
    /// </summary>
    private void Resolve(GameAction action)
    {
        var state = GetOrCreateState(action);
        if (state.ResolvedTick == _step || state.Resolving) return;

        AddTickAction(action);
        state.Resolving = true;
        try
        {
            var kind = action.ValueKind;
            bool silenced = IsTreePaused && !action.TriggerWhenPaused;
            var mode = action.Accumulation;
            var firing = Vector4.Zero;
            var raw = Vector4.Zero;
            var input = Vector4.Zero;
            var bindingPhase = TriggerPhase.Idle;
            bool triggerlessNonZero = false;
            bool anyEvaluated = false;
            InputEndReason? cut = null;
            ValueNature? nature = null;

            if (_bindingsByAction.TryGetValue(action, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var compiled = list[i];
                    var entry = compiled.Entry;
                    var memory = entry.GetMemory(compiled, out bool replaced);
                    var source = compiled.Source;
                    if (replaced && state.Phase != TriggerPhase.Idle) cut ??= InputEndReason.BindingChanged;
                    if (Raw.AnyCanceled && AnyCanceled(compiled.Keys, out var reason))
                    {
                        // Cut off, not released: forget trigger and modifier history so nothing fires on it.
                        memory.Reset();
                        cut ??= reason;
                    }

                    if (silenced || entry.Rank > _blockRank || IsFullySuppressed(entry, source, compiled.Keys))
                    {
                        memory.Reset();
                        continue;
                    }
                    anyEvaluated = true;
                    var bindingNature = source?.Nature ?? ValueNature.State;
                    nature = nature == null || nature == bindingNature ? bindingNature : ValueNature.State;

                    _readEntry = entry;
                    var value = source?.Read(this) ?? Vector4.Zero;
                    _readEntry = null;

                    value = ActionValue.Shape(kind, value);
                    input = Accumulate(kind, input, value, mode);
                    value = ApplyModifiers(compiled.Modifiers, memory.Modifiers, value, kind, source?.Nature ?? ValueNature.State);
                    raw = Accumulate(kind, raw, value, mode);

                    if (compiled.HasTriggers)
                    {
                        var phase = TriggerSet.Evaluate(compiled.Triggers, memory.Triggers, value, kind, _delta, this);
                        if (phase > bindingPhase) bindingPhase = phase;
                        if (phase == TriggerPhase.Triggered) firing = Accumulate(kind, firing, value, mode);
                    }
                    else
                    {
                        firing = Accumulate(kind, firing, value, mode);
                        if (!ActionValue.IsZeroOf(kind, value)) triggerlessNonZero = true;
                    }
                }
            }

            if (!silenced && _injections.TryGetValue(action, out var injection) && injection.TryRead(out var injected))
            {
                anyEvaluated = true;
                injected = ActionValue.Shape(kind, injected);
                firing = Accumulate(kind, firing, injected, mode);
                raw = Accumulate(kind, raw, injected, mode);
                input = Accumulate(kind, input, injected, mode);
                if (!ActionValue.IsZeroOf(kind, injected)) triggerlessNonZero = true;
            }

            EnsureArray(ref state.ModifierMemories, action.Modifiers?.Count ?? 0);
            EnsureArray(ref state.TriggerMemories, action.Triggers?.Count ?? 0);

            if (!anyEvaluated)
            {
                state.ResetMemories();
                state.ToggleOn = false;
                state.TogglePressed = false;
                state.InputValue = ActionValue.Zero(kind);
                // Nothing left to evaluate is never a release: the input was taken away.
                InputEndReason? why = cut ?? (silenced ? InputEndReason.Paused
                    : list != null && list.Count > 0 ? InputEndReason.Blocked
                    : state.HadBindings ? _planChangeReason
                    : null);   // only injected input drove it: its end is a genuine release
                state.HadBindings = false;
                state.Advance(TriggerPhase.Idle, ActionValue.Zero(kind), _delta, ActionValue.Zero(kind), _tick, why);
            }
            else
            {
                state.HadBindings = list != null && list.Count > 0;
                if (cut.HasValue && ActionValue.IsZeroOf(kind, firing)) state.ResetMemories();
                // Mixed natures count as held state; all-delta or all-absolute keep theirs.
                var value = Trim(kind, ApplyModifiers(action.Modifiers, state.ModifierMemories, firing, kind, nature ?? ValueNature.State));
                TriggerPhase finalPhase;
                if (HasTriggers(action.Triggers))
                {
                    finalPhase = TriggerSet.Evaluate(action.Triggers, state.TriggerMemories, value, kind, _delta, this);
                    if (finalPhase == TriggerPhase.Idle && bindingPhase == TriggerPhase.Ongoing) finalPhase = TriggerPhase.Ongoing;
                }
                else
                {
                    finalPhase = bindingPhase;
                    if (triggerlessNonZero) finalPhase = TriggerPhase.Triggered;
                }
                if (IsHoldToToggle(action))
                {
                    // Accessibility: each press flips the action instead of having to hold it.
                    bool pressed = finalPhase == TriggerPhase.Triggered;
                    if (pressed && !state.TogglePressed)
                    {
                        state.ToggleOn = !state.ToggleOn;
                        if (state.ToggleOn) state.ToggleValue = !ActionValue.IsZeroOf(kind, value) ? value : ActionValue.Shape(kind, new Vector4(1f, 0f, 0f, 0f));
                    }
                    state.TogglePressed = pressed;
                    finalPhase = state.ToggleOn ? TriggerPhase.Triggered : TriggerPhase.Idle;
                    value = state.ToggleOn ? state.ToggleValue : Vector4.Zero;
                }
                state.InputValue = new ActionValue(kind, Trim(kind, input));
                state.Advance(finalPhase, new ActionValue(kind, value), _delta, new ActionValue(kind, Trim(kind, raw)), _tick, cut);
            }

            state.LastTick = _tick;
            state.ResolvedTick = _step;
            state.StepId = _step;
        }
        finally
        {
            state.Resolving = false;
        }
    }

    private static bool HasTriggers(IList<InputTrigger> triggers)
    {
        if (triggers == null) return false;
        for (int i = 0; i < triggers.Count; i++)
            if (triggers[i] != null) return true;
        return false;
    }

    private bool AnyCanceled(SourceKey[] keys, out InputEndReason reason)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (Raw.WasCanceled(Raw.PhysicalOf(key), out reason) || Raw.WasCanceled(key, out reason)) return true;
            if (key.Device == SourceKey.AnyDevice && key.IsPerDevice)
                foreach (var d in Raw.DevicesWithChannel(key.Kind))
                    if (Raw.WasCanceled(key with { Device = d }, out reason)) return true;
        }
        reason = InputEndReason.Released;
        return false;
    }

    /// <summary>Whether every channel a source reads is consumed above this context or ignored in it.</summary>
    private bool IsFullySuppressed(ContextEntry entry, InputSource source, SourceKey[] keys)
    {
        if (source == null || source.Kind == SourceKind.None || keys.Length == 0) return false;
        if (_consumedBy.Count == 0 && entry.Ignored.Count == 0) return false;
        for (int i = 0; i < keys.Length; i++)
            if (!ChannelHidden(entry, Raw.PhysicalOf(keys[i]))) return false;
        return true;
    }

    /// <summary>
    /// Whether a channel is hidden from a context: consumed by a higher-priority context or ignored
    /// until released. A consumption on "any device" hides the channel on every device; a consumption
    /// on one device (or finger) hides only that one — so for an any-device channel only an any-device
    /// consumption hides it entirely.
    /// </summary>
    private bool ChannelHidden(ContextEntry entry, in SourceKey key)
    {
        if (_consumedBy.TryGetValue(key, out var rank) && rank < entry.Rank) return true;
        var wildcard = key with { Device = SourceKey.AnyDevice };
        if (key.Device != SourceKey.AnyDevice && _consumedBy.TryGetValue(wildcard, out rank) && rank < entry.Rank) return true;
        if (entry.Ignored.Count > 0 && (entry.Ignored.Contains(key) || entry.Ignored.Contains(wildcard))) return true;
        return false;
    }

    private Vector4 ApplyModifiers(IList<InputModifier> modifiers, ModifierMemory[] memories, Vector4 value, ActionValueKind kind, ValueNature nature)
    {
        if (modifiers == null) return value;
        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (modifier == null) continue;
            value = modifier.Run(value, new ModifierContext(kind, _delta, this, memories[i], nature));
        }
        return value;
    }

    /// <summary>
    /// Combines the values of several bindings. Vectors: the strongest per component, or the sum.
    /// Rotations (quaternions): the largest rotation, or all rotations composed in order.
    /// </summary>
    internal static Vector4 Accumulate(ActionValueKind kind, Vector4 accumulated, Vector4 value, ValueAccumulation mode)
    {
        if (kind == ActionValueKind.Quaternion)
        {
            if (ActionValue.IsZeroOf(kind, value)) return accumulated;
            if (ActionValue.IsZeroOf(kind, accumulated)) return value;
            if (mode == ValueAccumulation.Sum)
                return ActionValue.FromQuaternion(ActionValue.ToQuaternion(accumulated) * ActionValue.ToQuaternion(value));
            return ActionValue.MagnitudeOf(kind, value) > ActionValue.MagnitudeOf(kind, accumulated) ? value : accumulated;
        }
        if (mode == ValueAccumulation.Sum) return accumulated + value;
        return new Vector4(Strongest(accumulated.X, value.X), Strongest(accumulated.Y, value.Y), Strongest(accumulated.Z, value.Z), Strongest(accumulated.W, value.W));
    }

    private static float Strongest(float a, float b) => Mathf.Abs(b) > Mathf.Abs(a) ? b : a;

    private static Vector4 Trim(ActionValueKind kind, Vector4 value) => kind switch
    {
        ActionValueKind.Digital or ActionValueKind.Axis1D => new Vector4(value.X, 0f, 0f, 0f),
        ActionValueKind.Axis2D => new Vector4(value.X, value.Y, 0f, 0f),
        ActionValueKind.Axis3D or ActionValueKind.Euler => new Vector4(value.X, value.Y, value.Z, 0f),
        ActionValueKind.Quaternion => ActionValue.IsZeroOf(kind, value) ? Vector4.Zero : ActionValue.FromQuaternion(ActionValue.ToQuaternion(value)),
        _ => value,
    };

    // ------------------------------------------------------------------
    //  Reading raw input with consumption and ignore-until-release
    // ------------------------------------------------------------------

    bool IChannelMask.IsMasked(in SourceKey exactKey)
        => _readEntry != null && ChannelHidden(_readEntry, Raw.PhysicalOf(exactKey));

    Vector4 ISourceReader.Read(in SourceKey key) => ReadRaw(key, _readEntry != null ? this : null);

    Vector2 ISourceReader.ReadStick(JoyAxis x, JoyAxis y, int device) => Raw.ReadStick(x, y, device, _readEntry != null ? this : null);

    private Vector4 ReadRaw(in SourceKey key, IChannelMask mask)
    {
        var value = Raw.Read(key, mask);
        if (key.Kind == RawKind.InputMapAction && PollInputMap && _replay == null && !string.IsNullOrEmpty(key.Name) && (mask == null || !mask.IsMasked(key)))
        {
            if (!_inputMapNames.TryGetValue(key.Name, out var name))
                _inputMapNames[key.Name] = name = new StringName(key.Name);
            if (InputMap.HasAction(name))
            {
                float polled = Input.GetActionRawStrength(name);
                if (polled > value.X) value = new Vector4(polled, 0f, 0f, 0f);
            }
        }
        return value;
    }

    // ------------------------------------------------------------------
    //  Dispatch
    // ------------------------------------------------------------------

    private static readonly ActionEvents[] EventOrder =
    {
        ActionEvents.Started, ActionEvents.Ongoing, ActionEvents.Triggered, ActionEvents.Completed, ActionEvents.Canceled,
    };

    private void Dispatch()
    {
        _dispatching = true;
        try
        {
            for (int i = 0; i < _tickActions.Count; i++)
            {
                var action = _tickActions[i];
                if (!_states.TryGetValue(action, out var state) || state.StepId != _step || state.StepEvents == ActionEvents.None) continue;

                foreach (var flag in EventOrder)
                {
                    if ((state.StepEvents & flag) == 0) continue;
                    Raise(action, state, flag);
                }
            }
        }
        finally
        {
            _dispatching = false;
            PurgeInactiveSubscriptions();
        }
    }

    private void Raise(GameAction action, ActionState state, ActionEvents flag)
    {
        var args = new ActionEventArgs(this, action, flag, state);

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
