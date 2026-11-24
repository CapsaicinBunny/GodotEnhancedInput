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
        _delta = Math.Max(0.0, delta);
        UpdateCapture(_delta);
        _ticking = true;
        try
        {
            PrepareTick();
            for (int i = 0; i < _tickActions.Count; i++) Resolve(_tickActions[i]);
        }
        finally
        {
            _ticking = false;
        }

        Dispatch();
        EndTick();
        EndTickRecording();
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

    private void PrepareTick()
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
            if (compiled.PollsSensors) PollSensors(compiled.Source);

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
            if (ReadRaw(key, null) == Vector3.Zero) _keyScratch.Add(key);
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
        if (state.ResolvedTick == _tick || state.Resolving) return;

        AddTickAction(action);
        state.Resolving = true;
        try
        {
            var kind = action.ValueKind;
            bool silenced = IsTreePaused && !action.TriggerWhenPaused;
            var firing = Vector3.Zero;
            var raw = Vector3.Zero;
            var bindingPhase = TriggerPhase.Idle;
            bool triggerlessNonZero = false;
            bool anyEvaluated = false;

            if (_bindingsByAction.TryGetValue(action, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var compiled = list[i];
                    var entry = compiled.Entry;
                    var memory = entry.GetMemory(compiled);
                    var source = compiled.Source;

                    if (silenced || entry.Rank > _blockRank || IsFullySuppressed(entry, source, compiled.Keys))
                    {
                        memory.Reset();
                        continue;
                    }
                    anyEvaluated = true;

                    _readEntry = entry;
                    var value = source?.Read(this) ?? Vector3.Zero;
                    _readEntry = null;

                    value = ActionValue.Shape(kind, value);
                    value = ApplyModifiers(compiled.Modifiers, memory.Modifiers, value, kind, source?.Nature ?? ValueNature.State);
                    raw = Accumulate(raw, value, action.Accumulation);

                    if (compiled.HasTriggers)
                    {
                        var phase = TriggerSet.Evaluate(compiled.Triggers, memory.Triggers, value, kind, _delta, this);
                        if (phase > bindingPhase) bindingPhase = phase;
                        if (phase == TriggerPhase.Triggered) firing = Accumulate(firing, value, action.Accumulation);
                    }
                    else
                    {
                        firing = Accumulate(firing, value, action.Accumulation);
                        if (value != Vector3.Zero) triggerlessNonZero = true;
                    }
                }
            }

            if (!silenced && _injections.TryGetValue(action, out var injection) && injection.TryRead(out var injected))
            {
                anyEvaluated = true;
                injected = ActionValue.Shape(kind, injected);
                firing = Accumulate(firing, injected, action.Accumulation);
                raw = Accumulate(raw, injected, action.Accumulation);
                if (injected != Vector3.Zero) triggerlessNonZero = true;
            }

            EnsureArray(ref state.ModifierMemories, action.Modifiers?.Count ?? 0);
            EnsureArray(ref state.TriggerMemories, action.Triggers?.Count ?? 0);

            if (!anyEvaluated)
            {
                state.ResetMemories();
                state.ToggleOn = false;
                state.TogglePressed = false;
                state.Advance(TriggerPhase.Idle, ActionValue.Zero(kind), _delta, ActionValue.Zero(kind));
            }
            else
            {
                var value = Trim(kind, ApplyModifiers(action.Modifiers, state.ModifierMemories, firing, kind, ValueNature.State));
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
                        if (state.ToggleOn) state.ToggleValue = value != Vector3.Zero ? value : ActionValue.Shape(kind, Vector3.Right);
                    }
                    state.TogglePressed = pressed;
                    finalPhase = state.ToggleOn ? TriggerPhase.Triggered : TriggerPhase.Idle;
                    value = state.ToggleOn ? state.ToggleValue : Vector3.Zero;
                }
                state.Advance(finalPhase, new ActionValue(kind, value), _delta, new ActionValue(kind, Trim(kind, raw)));
            }

            state.LastTick = _tick;
            state.ResolvedTick = _tick;
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

    private Vector3 ApplyModifiers(IList<InputModifier> modifiers, ModifierMemory[] memories, Vector3 value, ActionValueKind kind, ValueNature nature)
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

    private static Vector3 Accumulate(Vector3 accumulated, Vector3 value, ValueAccumulation mode)
    {
        if (mode == ValueAccumulation.Sum) return accumulated + value;
        return new Vector3(Strongest(accumulated.X, value.X), Strongest(accumulated.Y, value.Y), Strongest(accumulated.Z, value.Z));
    }

    private static float Strongest(float a, float b) => Mathf.Abs(b) > Mathf.Abs(a) ? b : a;

    private static Vector3 Trim(ActionValueKind kind, Vector3 value) => kind switch
    {
        ActionValueKind.Digital or ActionValueKind.Axis1D => new Vector3(value.X, 0f, 0f),
        ActionValueKind.Axis2D => new Vector3(value.X, value.Y, 0f),
        _ => value,
    };

    // ------------------------------------------------------------------
    //  Reading raw input with consumption and ignore-until-release
    // ------------------------------------------------------------------

    bool IChannelMask.IsMasked(in SourceKey exactKey)
        => _readEntry != null && ChannelHidden(_readEntry, Raw.PhysicalOf(exactKey));

    Vector3 ISourceReader.Read(in SourceKey key) => ReadRaw(key, _readEntry != null ? this : null);

    Vector2 ISourceReader.ReadStick(JoyAxis x, JoyAxis y, int device) => Raw.ReadStick(x, y, device, _readEntry != null ? this : null);

    private Vector3 ReadRaw(in SourceKey key, IChannelMask mask)
    {
        var value = Raw.Read(key, mask);
        if (key.Kind == RawKind.InputMapAction && PollInputMap && _replay == null && !string.IsNullOrEmpty(key.Name) && (mask == null || !mask.IsMasked(key)))
        {
            if (!_inputMapNames.TryGetValue(key.Name, out var name))
                _inputMapNames[key.Name] = name = new StringName(key.Name);
            if (InputMap.HasAction(name))
            {
                float polled = Input.GetActionRawStrength(name);
                if (polled > value.X) value = new Vector3(polled, 0f, 0f);
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
                if (!_states.TryGetValue(action, out var state) || state.LastTick != _tick || state.Events == ActionEvents.None) continue;

                foreach (var flag in EventOrder)
                {
                    if ((state.Events & flag) == 0) continue;
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
