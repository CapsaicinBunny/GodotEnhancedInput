using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Evaluating one action: reading its bindings with consumption and ignore-until-release applied,
/// modifiers, triggers, accumulation, and the phase state machine.
/// </summary>
public partial class InputPlayer : IChannelMask
{
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
        _stepResolved.Add(action);
        state.Resolving = true;
        // This action's own time: what passed since it was last evaluated in this tick.
        double savedDelta = _delta;
        bool evaluatedThisFrame = state.EvalTick == _tick;
        if (state.EvalTick != _tick)
        {
            state.EvalTick = _tick;
            state.EvalTime = 0;
        }
        _delta = Math.Max(0.0, _frameTime - state.EvalTime);
        bool wasTracked = state.IsTracked;
        bool tracked = false;
        try
        {
            var kind = action.ValueKind;
            bool disabled = IsDisabled(action);
            bool silenced = disabled || (IsTreePaused && !action.TriggerWhenPaused);
            var mode = action.Accumulation;
            var firing = Vector4.Zero;
            var raw = Vector4.Zero;
            var input = Vector4.Zero;
            var bindingPhase = TriggerPhase.Idle;
            bool triggerlessNonZero = false;
            bool anyEvaluated = false;
            InputEndReason? cut = null;
            ValueNature? nature = null;
            CompiledBinding strongest = null, strongestAny = null;
            float strongestMagnitude = -1f, strongestAnyMagnitude = -1f;
            bool passThrough = action.Evaluation == ActionEvaluation.PassThrough && _eventPhase;
            state.PassBinding = null;

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
                        memory.Suppressed = true;
                        continue;
                    }
                    anyEvaluated = true;
                    var bindingNature = source?.Nature ?? ValueNature.State;
                    nature = nature == null || nature == bindingNature ? bindingNature : ValueNature.State;

                    _readEntry = entry;
                    var value = source?.Read(this) ?? Vector4.Zero;
                    _readEntry = null;
                    if (memory.Suppressed) memory.Resume(held: value != Vector4.Zero);
                    bool bindingTracked = false;
                    for (int k = 0; k < compiled.Keys.Length && !bindingTracked; k++)
                        bindingTracked = Raw.IsTracked(compiled.Keys[k]);
                    tracked |= bindingTracked;

                    // Rotation actions keep all four components until their modifiers ran (a quaternion
                    // source converted to Euler angles needs its W); every value is shaped afterwards.
                    var shaped = ActionValue.Shape(kind, value);
                    input = Accumulate(kind, input, shaped, mode);
                    if (kind != ActionValueKind.Euler) value = shaped;
                    value = ApplyModifiers(compiled.Modifiers, memory.Modifiers, value, kind, source?.Nature ?? ValueNature.State);
                    if (kind == ActionValueKind.Euler) value = ActionValue.Shape(kind, value);
                    raw = Accumulate(kind, raw, value, mode);

                    bool fires;
                    if (compiled.HasTriggers)
                    {
                        var phase = TriggerSet.Evaluate(compiled.Triggers, memory.Triggers, value, kind, _delta, this);
                        if (phase > bindingPhase) bindingPhase = phase;
                        fires = phase == TriggerPhase.Triggered;
                        if (fires) firing = Accumulate(kind, firing, value, mode);
                    }
                    else
                    {
                        firing = Accumulate(kind, firing, value, mode);
                        // A tracked pose is input even at the origin or the identity rotation.
                        fires = bindingTracked || !ActionValue.IsZeroOf(kind, value);
                        if (fires) triggerlessNonZero = true;
                    }

                    // Where the input came from: the strongest firing binding (else the strongest at all).
                    float magnitude = ActionValue.MagnitudeOf(kind, value);
                    if (fires && magnitude > strongestMagnitude) { strongest = compiled; strongestMagnitude = magnitude; }
                    if (magnitude > strongestAnyMagnitude) { strongestAny = compiled; strongestAnyMagnitude = magnitude; }
                    if (passThrough && state.PassBinding == null && TouchesStep(compiled.Keys))
                    {
                        state.PassBinding = compiled.Binding;
                        state.PassKeys = compiled.Keys;
                        state.PassValue = new ActionValue(kind, Trim(kind, value));
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

            var origin = strongest ?? strongestAny;
            state.SourceBinding = origin?.Binding;
            state.SourceKeys = origin?.Keys;
            state.IsTracked = tracked;
            if (wasTracked && !tracked && state.Phase != TriggerPhase.Idle) cut ??= InputEndReason.TrackingLost;
            if (_injectionCuts.Count > 0 && _injectionCuts.Remove(action, out var injectionCut)) cut ??= injectionCut;

            if (!anyEvaluated)
            {
                state.ResetMemories();
                state.ToggleOn = false;
                state.TogglePressed = false;
                state.InputValue = ActionValue.Zero(kind);
                // Nothing left to evaluate is never a release: the input was taken away.
                InputEndReason? why = cut ?? (disabled ? InputEndReason.Blocked : silenced ? InputEndReason.Paused
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
                // A tracked action is triggered exactly while tracked, whatever the value.
                if (action.Evaluation == ActionEvaluation.Tracked) finalPhase = tracked ? TriggerPhase.Triggered : TriggerPhase.Idle;
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
                // A one-shot that fired at an input event this frame (pressed, released, tap, combo…) stays
                // triggered until the frame ends, as frame-based code expects; it ends on the next frame.
                bool latched = false;
                if (_finalStep && evaluatedThisFrame && state.Phase == TriggerPhase.Triggered && finalPhase != TriggerPhase.Triggered && !cut.HasValue)
                {
                    finalPhase = TriggerPhase.Triggered;
                    value = state.Value.Raw;
                    latched = true;
                }
                state.InputValue = new ActionValue(kind, Trim(kind, input));
                if (state.Value.Raw != value) state.LastChangeTime = StepClock;
                state.Advance(finalPhase, new ActionValue(kind, value), _delta, new ActionValue(kind, Trim(kind, raw)), _tick, cut);
                if (latched) state.HoldOver();   // carried to the frame's end: nothing new happened
            }

            state.LastTick = _tick;
            state.ResolvedTick = _step;
            state.StepId = _step;
        }
        finally
        {
            state.Resolving = false;
            state.EvalTime = _frameTime;
            _delta = savedDelta;
        }
    }

    /// <summary>Whether one of these channels changed in the event being evaluated.</summary>
    private bool TouchesStep(SourceKey[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            for (int s = 0; s < _stepKeys.Count; s++)
            {
                var changed = _stepKeys[s];
                if (changed == key || (key.Device == SourceKey.AnyDevice && changed with { Device = SourceKey.AnyDevice } == key)) return true;
            }
        }
        return false;
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
}
