using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>True while the scene tree is paused (only when this player is inside a tree).</summary>
    public bool IsTreePaused => IsInsideTree() && GetTree().Paused;

    /// <summary>
    /// Evaluates every action once:
    /// <list type="number">
    /// <item>orders the active contexts and works out which sources each one consumes or blocks;</item>
    /// <item>for each action (lazily, so triggers can ask for other actions): reads its bindings' sources,
    /// applies binding modifiers and triggers, combines the bindings, applies the action's modifiers
    /// and triggers, and moves the phase state machine;</item>
    /// <item>raises callbacks and signals for the events of this tick;</item>
    /// <item>clears per-tick raw data (peaks, mouse motion, one-tick injections).</item>
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

                if (!_bindingsByAction.TryGetValue(action, out var list))
                {
                    list = _listPool.Count > 0 ? _listPool.Pop() : new List<(ContextEntry, int)>();
                    _bindingsByAction[action] = list;
                }
                list.Add((entry, i));
                AddTickAction(action);

                // Consumption depends only on which contexts are active, never on this tick's results,
                // so actions can be evaluated in any order.
                var source = GetEffectiveSource(binding);
                if (action.ConsumeInput && source != null)
                {
                    _keyScratch.Clear();
                    source.CollectKeys(_keyScratch);
                    foreach (var key in _keyScratch) _consumedBy.TryAdd(key.WithoutDevice(), entry.Rank);
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
            if (ReadRaw(key) == Vector3.Zero) _keyScratch.Add(key);
        foreach (var key in _keyScratch) entry.Ignored.Remove(key);
    }

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
            var accumulated = Vector3.Zero;
            var bindingPhase = TriggerPhase.Idle;

            if (_bindingsByAction.TryGetValue(action, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var (entry, index) = list[i];
                    if (index >= entry.Context.Bindings.Count) continue;
                    var binding = entry.Context.Bindings[index];
                    if (binding == null) continue;
                    var memory = entry.GetMemory(index, binding);

                    _readEntry = entry;
                    _readSilenced = silenced;
                    var value = GetEffectiveSource(binding)?.Read(this) ?? Vector3.Zero;
                    _readEntry = null;
                    _readSilenced = false;

                    value = ActionValue.Shape(kind, value);
                    value = ApplyModifiers(binding.Modifiers, memory.Modifiers, value, kind);
                    var phase = TriggerSet.Evaluate(binding.Triggers, memory.Triggers, value, kind, _delta, this);

                    accumulated = Accumulate(accumulated, value, action.Accumulation);
                    if (phase > bindingPhase) bindingPhase = phase;
                }
            }

            if (!silenced && _injections.TryGetValue(action, out var injection) && injection.TryRead(out var injected))
            {
                injected = ActionValue.Shape(kind, injected);
                accumulated = Accumulate(accumulated, injected, action.Accumulation);
                if (injected != Vector3.Zero) bindingPhase = TriggerPhase.Triggered;
            }

            EnsureArray(ref state.ModifierMemories, action.Modifiers?.Count ?? 0);
            EnsureArray(ref state.TriggerMemories, action.Triggers?.Count ?? 0);

            var final = ApplyModifiers(action.Modifiers, state.ModifierMemories, accumulated, kind);
            final = Trim(kind, final);

            var finalPhase = bindingPhase;
            if (action.Triggers != null && action.Triggers.Count > 0)
            {
                var actionPhase = TriggerSet.Evaluate(action.Triggers, state.TriggerMemories, final, kind, _delta, this);
                if (actionPhase < finalPhase) finalPhase = actionPhase;
            }

            state.Advance(finalPhase, new ActionValue(kind, final), _delta);
            state.LastTick = _tick;
            state.ResolvedTick = _tick;
        }
        finally
        {
            state.Resolving = false;
        }
    }

    private Vector3 ApplyModifiers(IList<InputModifier> modifiers, ModifierMemory[] memories, Vector3 value, ActionValueKind kind)
    {
        if (modifiers == null) return value;
        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (modifier == null) continue;
            value = modifier.Run(value, new ModifierContext(kind, _delta, this, memories[i]));
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
    //  Reading raw input with consumption, blocking and ignore-until-release
    // ------------------------------------------------------------------

    Vector3 ISourceReader.Read(in SourceKey key)
    {
        if (_readSilenced) return Vector3.Zero;

        var entry = _readEntry;
        if (entry != null)
        {
            if (entry.Rank > _blockRank) return Vector3.Zero;
            var plain = key.WithoutDevice();
            if (_consumedBy.TryGetValue(plain, out var consumerRank) && consumerRank < entry.Rank) return Vector3.Zero;
            if (entry.Ignored.Count > 0 && entry.Ignored.Contains(plain)) return Vector3.Zero;
        }

        return ReadRaw(key);
    }

    private Vector3 ReadRaw(in SourceKey key)
    {
        var value = Raw.Read(key);
        if (key.Kind == RawKind.InputMapAction && PollInputMap && !string.IsNullOrEmpty(key.Name))
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
