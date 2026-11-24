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
        if (_ticking) return;

        _tick++;
        _delta = Math.Max(0.0, delta);
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
    }

    private void PrepareTick()
    {
        OrderEntries();

        // Release per-action binding lists back to the pool.
        foreach (var list in _bindingsByAction.Values)
        {
            list.Clear();
            _listPool.Push(list);
        }
        _bindingsByAction.Clear();
        _tickActions.Clear();
        _tickActionSet.Clear();
        _consumedBy.Clear();
        _polledSensors.Clear();
        _blockRank = int.MaxValue;

        foreach (var entry in _ordered)
        {
            UpdateIgnored(entry);

            var bindings = entry.Context.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var action = binding?.Action;
                if (action == null) continue;
                if (!IsBindingActive(binding)) continue;   // not in the active mode's groups

                if (!_bindingsByAction.TryGetValue(action, out var list))
                {
                    list = _listPool.Count > 0 ? _listPool.Pop() : new List<(ContextEntry, int)>();
                    _bindingsByAction[action] = list;
                }
                list.Add((entry, i));
                AddTickAction(action);

                // Consumption depends only on which contexts are active, never on this tick's results,
                // so actions can be evaluated in any order. Channels keep their device or finger; a
                // source reading "any device" consumes the channel on every device.
                var source = GetEffectiveSource(binding);
                if (source != null) PollSensors(source);
                if (action.ConsumeInput && source != null)
                {
                    _keyScratch.Clear();
                    source.CollectKeys(_keyScratch);
                    foreach (var key in _keyScratch) _consumedBy.TryAdd(Raw.PhysicalOf(key), entry.Rank);
                }
            }

            if (entry.Context.BlockLowerPriority && _blockRank == int.MaxValue) _blockRank = entry.Rank;
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
                    var (entry, index) = list[i];
                    if (index >= entry.Context.Bindings.Count) continue;
                    var binding = entry.Context.Bindings[index];
                    if (binding == null) continue;
                    var memory = entry.GetMemory(index, binding);
                    var source = GetEffectiveSource(binding);

                    if (silenced || entry.Rank > _blockRank || IsFullySuppressed(entry, source))
                    {
                        memory.Reset();
                        continue;
                    }
                    anyEvaluated = true;

                    _readEntry = entry;
                    var value = source?.Read(this) ?? Vector3.Zero;
                    _readEntry = null;

                    value = ActionValue.Shape(kind, value);
                    value = ApplyModifiers(binding.Modifiers, memory.Modifiers, value, kind, source?.Nature ?? ValueNature.State);
                    raw = Accumulate(raw, value, action.Accumulation);

                    if (HasTriggers(binding.Triggers))
                    {
                        var phase = TriggerSet.Evaluate(binding.Triggers, memory.Triggers, value, kind, _delta, this);
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
    private bool IsFullySuppressed(ContextEntry entry, InputSource source)
    {
        if (source == null || source.Kind == SourceKind.None) return false;
        _keyScratch.Clear();
        source.CollectKeys(_keyScratch);
        if (_keyScratch.Count == 0) return false;
        foreach (var key in _keyScratch)
            if (!ChannelHidden(entry, Raw.PhysicalOf(key))) return false;
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
        if (key.Kind == RawKind.InputMapAction && PollInputMap && !string.IsNullOrEmpty(key.Name) && (mask == null || !mask.IsMasked(key)))
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
        EmitSignal(signal, action);
    }

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
