using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Reading actions (polling), subscribing to their events, and feeding them values from code.</summary>
public partial class InputPlayer
{
    /// <summary>
    /// Feeds a value to an action for the next tick only, as if from an extra binding with no
    /// modifiers or triggers. The action's own modifiers and triggers still apply.
    /// </summary>
    public void InjectInput(GameAction action, Vector4 value)
    {
        if (action == null || LiveInputBlocked) return;
        Record(new RecordedInput { Kind = RecordedInputKind.Inject, Name = action.Name, Value = value });
        GetInjection(action).AddPeak(value);
    }

    /// <inheritdoc cref="InjectInput(GameAction, Vector4)"/>
    public void InjectInput(GameAction action, Vector3 value) => InjectInput(action, new Vector4(value.X, value.Y, value.Z, 0f));

    /// <inheritdoc cref="InjectInput(GameAction, Vector4)"/>
    public void InjectInput(GameAction action, Quaternion value) => InjectInput(action, ActionValue.FromQuaternion(value));

    /// <inheritdoc cref="InjectInput(GameAction, Vector4)"/>
    public void InjectInput(GameAction action, Vector2 value) => InjectInput(action, new Vector4(value.X, value.Y, 0f, 0f));

    /// <inheritdoc cref="InjectInput(GameAction, Vector4)"/>
    public void InjectInput(GameAction action, float value) => InjectInput(action, new Vector4(value, 0f, 0f, 0f));

    /// <inheritdoc cref="InjectInput(GameAction, Vector4)"/>
    public void InjectInput(GameAction action, bool value) => InjectInput(action, value ? 1f : 0f);

    /// <summary>Feeds a value to an action on every tick until <see cref="StopInjecting"/>.</summary>
    public void StartInjecting(GameAction action, Vector3 value) => StartInjecting(action, new Vector4(value.X, value.Y, value.Z, 0f));

    /// <summary>Feeds a rotation to an action on every tick until <see cref="StopInjecting"/>.</summary>
    public void StartInjecting(GameAction action, Quaternion value) => StartInjecting(action, ActionValue.FromQuaternion(value));

    /// <summary>Feeds a value to an action on every tick until <see cref="StopInjecting"/>.</summary>
    public void StartInjecting(GameAction action, Vector4 value)
    {
        if (action == null || LiveInputBlocked) return;
        Record(new RecordedInput { Kind = RecordedInputKind.InjectStart, Name = action.Name, Value = value });
        StartInjectingCore(action, value);
    }

    private void StartInjectingCore(GameAction action, Vector4 value)
    {
        var injection = GetInjection(action);
        injection.Continuous = true;
        injection.ContinuousValue = value;
        injection.AddPeak(value);
    }

    /// <summary>Stops a continuous injection. A press that started this tick is still seen once.</summary>
    public void StopInjecting(GameAction action)
    {
        if (action == null || LiveInputBlocked) return;
        Record(new RecordedInput { Kind = RecordedInputKind.InjectStop, Name = action.Name });
        StopInjectingCore(action);
    }

    private void StopInjectingCore(GameAction action)
    {
        if (_injections.TryGetValue(action, out var injection)) injection.Continuous = false;
    }

    private Injection GetInjection(GameAction action)
    {
        if (!_injections.TryGetValue(action, out var injection))
            _injections[action] = injection = new Injection();
        return injection;
    }

    /// <summary>The state of an action for this player (an idle state if it has never been evaluated).</summary>
    public ActionState GetState(GameAction action) => action == null ? null : GetOrCreateState(action);

    /// <summary>The action's value this tick.</summary>
    public ActionValue GetValue(GameAction action)
        => action != null && _states.TryGetValue(action, out var s) ? s.Value : ActionValue.Zero(action?.ValueKind ?? ActionValueKind.Digital);

    /// <summary>The action's phase this tick.</summary>
    public TriggerPhase GetPhase(GameAction action)
        => action != null && _states.TryGetValue(action, out var s) ? s.Phase : TriggerPhase.Idle;

    /// <summary>The events the action raised on the last tick.</summary>
    public ActionEvents GetEvents(GameAction action)
        => action != null && _states.TryGetValue(action, out var s) && s.LastTick == _tick ? s.Events : ActionEvents.None;

    /// <summary>Whether the action is triggered this tick.</summary>
    public bool IsTriggered(GameAction action) => GetPhase(action) == TriggerPhase.Triggered;

    /// <summary>Whether the action started on the last tick.</summary>
    public bool WasStarted(GameAction action) => (GetEvents(action) & ActionEvents.Started) != 0;

    /// <summary>Whether the action was ongoing on the last tick.</summary>
    public bool WasOngoing(GameAction action) => (GetEvents(action) & ActionEvents.Ongoing) != 0;

    /// <summary>Whether the action triggered on the last tick.</summary>
    public bool WasTriggered(GameAction action) => (GetEvents(action) & ActionEvents.Triggered) != 0;

    /// <summary>Whether the action completed on the last tick.</summary>
    public bool WasCompleted(GameAction action) => (GetEvents(action) & ActionEvents.Completed) != 0;

    /// <summary>Whether the action was canceled on the last tick.</summary>
    public bool WasCanceled(GameAction action) => (GetEvents(action) & ActionEvents.Canceled) != 0;

    /// <summary>
    /// The action's value with its validity: <see cref="InputSample.IsTracked"/> for tracked input (an XR
    /// pose at the identity is a reading; a lost pose is not), and when it last changed.
    /// </summary>
    public InputSample GetSample(GameAction action)
    {
        if (action == null || !_states.TryGetValue(action, out var s))
            return new InputSample(ActionValue.Zero(action?.ValueKind ?? ActionValueKind.Digital), false, false, 0);
        return new InputSample(s.Value, s.IsTracked, s.IsTracked || s.Value.IsNonZero, s.LastChangeTime);
    }

    /// <summary>Every action state this player holds.</summary>
    public IEnumerable<ActionState> GetAllStates() => _states.Values;

    /// <summary>
    /// For triggers that depend on other actions: the other action's phase this tick (evaluating it
    /// first if needed). In a dependency cycle the previous tick's phase is returned.
    /// </summary>
    public TriggerPhase GetPhaseThisTick(GameAction action)
    {
        if (action == null) return TriggerPhase.Idle;
        if (_ticking) Resolve(action);
        return GetPhase(action);
    }

    /// <summary>
    /// Like <see cref="GetPhaseThisTick"/>, for the events the other action raises. During a tick these are
    /// the events of the step being evaluated (so a combo sees its steps in the order they happened, even
    /// within one frame); outside a tick, the last tick's.
    /// </summary>
    public ActionEvents GetEventsThisTick(GameAction action)
    {
        if (action == null) return ActionEvents.None;
        if (!_ticking) return GetEvents(action);
        Resolve(action);
        return _states.TryGetValue(action, out var state) && state.StepId == _step ? state.StepEvents : ActionEvents.None;
    }

    private ActionState GetOrCreateState(GameAction action)
    {
        if (!_states.TryGetValue(action, out var state))
            _states[action] = state = new ActionState(action);
        return state;
    }

    /// <summary>
    /// Calls <paramref name="callback"/> once for each of the given events the action raises. Callbacks
    /// run after the whole tick has been evaluated. Dispose the returned handle to unsubscribe.
    /// </summary>
    public ActionSubscription Bind(GameAction action, ActionEvents events, Action<ActionEventArgs> callback)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        var subscription = new ActionSubscription(this, action, events, callback);
        if (!_subscriptions.TryGetValue(action, out var list))
            _subscriptions[action] = list = new List<ActionSubscription>();
        list.Add(subscription);
        return subscription;
    }

    /// <summary>Removes a subscription.</summary>
    public void Unbind(ActionSubscription subscription)
    {
        if (subscription == null || !subscription.IsActive) return;
        subscription.IsActive = false;
        if (!_dispatching && _subscriptions.TryGetValue(subscription.Action, out var list)) list.Remove(subscription);
    }

    /// <summary>Removes every subscription of an action.</summary>
    public void UnbindAll(GameAction action)
    {
        if (action == null || !_subscriptions.TryGetValue(action, out var list)) return;
        foreach (var s in list) s.IsActive = false;
        if (!_dispatching) list.Clear();
    }
}
