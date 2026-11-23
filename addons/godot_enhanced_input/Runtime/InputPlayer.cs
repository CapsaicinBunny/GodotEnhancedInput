using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// One local player's input: which devices it listens to, its active binding contexts, its raw input
/// state and the state of every action it has seen. Created and ticked by <see cref="EnhancedInput"/>;
/// can also be used on its own (feed events with <see cref="FeedEvent(InputEvent)"/> and call
/// <see cref="Tick"/>), which is how the tests drive it.
/// </summary>
public partial class InputPlayer : Node, ISourceReader
{
    /// <summary>Raised when an action starts (leaves idle).</summary>
    [Signal] public delegate void ActionStartedEventHandler(GameAction action);

    /// <summary>Raised every tick an action is ongoing.</summary>
    [Signal] public delegate void ActionOngoingEventHandler(GameAction action);

    /// <summary>Raised every tick an action is triggered.</summary>
    [Signal] public delegate void ActionTriggeredEventHandler(GameAction action);

    /// <summary>Raised when an action goes from triggered back to idle.</summary>
    [Signal] public delegate void ActionCompletedEventHandler(GameAction action);

    /// <summary>Raised when an action goes from ongoing back to idle without triggering.</summary>
    [Signal] public delegate void ActionCanceledEventHandler(GameAction action);

    /// <summary>Raised when a context is added (or re-added with a new priority).</summary>
    [Signal] public delegate void ContextAddedEventHandler(BindingContext context, int priority);

    /// <summary>Raised when a context is removed.</summary>
    [Signal] public delegate void ContextRemovedEventHandler(BindingContext context);

    /// <summary>Every action event of this player, in the order they are raised.</summary>
    public event Action<ActionEventArgs> ActionEventRaised;

    // ------------------------------------------------------------------
    //  Settings
    // ------------------------------------------------------------------

    /// <summary>Index of this player in <see cref="EnhancedInput.Players"/>.</summary>
    public int PlayerIndex { get; internal set; }

    /// <summary>Whether keyboard and mouse events reach this player.</summary>
    [Export] public bool AcceptKeyboardMouse { get; set; } = true;

    /// <summary>Joypad device indices this player listens to. Empty: every joypad.</summary>
    [Export] public Array<int> JoypadDevices { get; set; } = new();

    /// <summary>Whether <see cref="InputEventAction"/> events reach this player.</summary>
    [Export] public bool AcceptActionEvents { get; set; } = true;

    /// <summary>
    /// When on, InputMap sources also poll <c>Input.GetActionRawStrength</c>, so
    /// <c>Input.ActionPress</c> and InputMap-bound devices drive them even without events.
    /// </summary>
    [Export] public bool PollInputMap { get; set; } = true;

    /// <summary>
    /// When on, an <see cref="InputEventAction"/> whose action name equals the <see cref="GameAction.Name"/>
    /// of an action in an active context drives that action directly, as injected input.
    /// </summary>
    [Export] public bool RouteGameActionEvents { get; set; } = true;

    /// <summary>Camera used by view-dependent modifiers. When null, the viewport's current 3D camera is used.</summary>
    [Export] public Camera3D ViewCamera { get; set; }

    /// <summary>Overrides the view basis for view-dependent modifiers (handy for 2D games and tests).</summary>
    public Basis? ViewBasisOverride { get; set; }

    /// <summary>Overrides the vertical field of view, in degrees, for <see cref="FovScaleModifier"/>.</summary>
    public float? ViewFovOverride { get; set; }

    /// <summary>The raw input this player has received.</summary>
    public RawInputState Raw { get; } = new();

    /// <summary>Number of ticks so far.</summary>
    public ulong TickCount => _tick;

    /// <summary>Seconds passed to the current (or last) tick.</summary>
    public double LastDelta => _delta;

    // ------------------------------------------------------------------
    //  State
    // ------------------------------------------------------------------

    private sealed class ContextEntry
    {
        public BindingContext Context;
        public int Priority;
        public ulong Sequence;
        public ContextOptions Options;
        public int Rank;
        public readonly HashSet<SourceKey> Ignored = new();
        public BindingMemory[] Memories = System.Array.Empty<BindingMemory>();

        public BindingMemory GetMemory(int index, ActionBinding binding)
        {
            if (Memories.Length <= index) System.Array.Resize(ref Memories, Math.Max(index + 1, Context.Bindings.Count));
            var memory = Memories[index];
            if (memory == null || !ReferenceEquals(memory.Binding, binding))
                Memories[index] = memory = new BindingMemory { Binding = binding };
            memory.Ensure(binding.Modifiers?.Count ?? 0, binding.Triggers?.Count ?? 0);
            return memory;
        }
    }

    private sealed class BindingMemory
    {
        public ActionBinding Binding;
        public ModifierMemory[] Modifiers = System.Array.Empty<ModifierMemory>();
        public TriggerMemory[] Triggers = System.Array.Empty<TriggerMemory>();

        public void Ensure(int modifiers, int triggers)
        {
            EnsureArray(ref Modifiers, modifiers);
            EnsureArray(ref Triggers, triggers);
        }
    }

    private sealed class Injection
    {
        public bool Continuous;
        public Vector3 ContinuousValue;
        public bool HasPeak;
        public Vector3 Peak;

        public bool TryRead(out Vector3 value)
        {
            if (Continuous) { value = ContinuousValue; return true; }
            if (HasPeak) { value = Peak; return true; }
            value = Vector3.Zero;
            return false;
        }

        public void AddPeak(Vector3 value)
        {
            if (!HasPeak || value.LengthSquared() > Peak.LengthSquared()) Peak = value;
            HasPeak = true;
        }
    }

    /// <summary>A handle for a callback registered with <see cref="Bind"/>. Dispose it to unsubscribe.</summary>
    public sealed class ActionSubscription : IDisposable
    {
        internal ActionSubscription(InputPlayer player, GameAction action, ActionEvents events, Action<ActionEventArgs> callback)
        {
            Player = player;
            Action = action;
            Events = events;
            Callback = callback;
        }

        public InputPlayer Player { get; }
        public GameAction Action { get; }
        public ActionEvents Events { get; }
        internal Action<ActionEventArgs> Callback { get; }
        public bool IsActive { get; internal set; } = true;

        public void Dispose() => Player.Unbind(this);
    }

    private readonly List<ContextEntry> _entries = new();
    private readonly List<ContextEntry> _ordered = new();
    private readonly System.Collections.Generic.Dictionary<GameAction, ActionState> _states = new();
    private readonly System.Collections.Generic.Dictionary<GameAction, List<(ContextEntry Entry, int Index)>> _bindingsByAction = new();
    private readonly System.Collections.Generic.Dictionary<string, GameAction> _actionsByName = new(StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<SourceKey, int> _consumedBy = new();
    private readonly System.Collections.Generic.Dictionary<GameAction, Injection> _injections = new();
    private readonly System.Collections.Generic.Dictionary<GameAction, List<ActionSubscription>> _subscriptions = new();
    private readonly System.Collections.Generic.Dictionary<string, StringName> _inputMapNames = new(StringComparer.Ordinal);
    private readonly List<GameAction> _tickActions = new();
    private readonly HashSet<GameAction> _tickActionSet = new();
    private readonly List<SourceKey> _keyScratch = new();
    private readonly Stack<List<(ContextEntry, int)>> _listPool = new();

    private ulong _sequence;
    private ulong _tick;
    private double _delta;
    private bool _ticking;
    private bool _dispatching;
    private int _blockRank = int.MaxValue;

    // Reader masking for the binding being read.
    private ContextEntry _readEntry;
    private bool _readSilenced;

    // ------------------------------------------------------------------
    //  Contexts
    // ------------------------------------------------------------------

    /// <summary>
    /// Activates a context with a priority (higher is evaluated first; ties go to the most recently
    /// added). Adding a context that is already active updates its priority and options.
    /// </summary>
    public void AddContext(BindingContext context, int priority = 0, ContextOptions options = default)
    {
        if (context == null) return;

        var entry = FindEntry(context);
        if (entry == null)
        {
            entry = new ContextEntry { Context = context };
            _entries.Add(entry);
        }
        else
        {
            entry.Ignored.Clear();
        }

        entry.Priority = priority;
        entry.Options = options;
        entry.Sequence = ++_sequence;

        if (options.IgnoreHeldSourcesUntilReleased)
        {
            foreach (var binding in context.Bindings)
            {
                var source = GetEffectiveSource(binding);
                if (source == null) continue;
                _keyScratch.Clear();
                source.CollectKeys(_keyScratch);
                foreach (var key in _keyScratch)
                {
                    var plain = key.WithoutDevice();
                    if (ReadRaw(plain) != Vector3.Zero) entry.Ignored.Add(plain);
                }
            }
        }

        RebuildNameIndex();
        EmitSignal(SignalName.ContextAdded, context, priority);
    }

    /// <summary>Deactivates a context. Its actions finish normally on the next tick. Returns false if it was not active.</summary>
    public bool RemoveContext(BindingContext context)
    {
        var entry = FindEntry(context);
        if (entry == null) return false;
        _entries.Remove(entry);
        RebuildNameIndex();
        EmitSignal(SignalName.ContextRemoved, context);
        return true;
    }

    /// <summary>Removes every context.</summary>
    public void ClearContexts()
    {
        for (int i = _entries.Count - 1; i >= 0; i--) RemoveContext(_entries[i].Context);
    }

    /// <summary>Whether a context is active.</summary>
    public bool HasContext(BindingContext context) => FindEntry(context) != null;

    /// <summary>The priority of an active context, or null.</summary>
    public int? GetContextPriority(BindingContext context) => FindEntry(context)?.Priority;

    /// <summary>The active contexts in evaluation order (highest priority first).</summary>
    public List<BindingContext> GetContexts()
    {
        OrderEntries();
        var result = new List<BindingContext>(_ordered.Count);
        foreach (var entry in _ordered) result.Add(entry.Context);
        return result;
    }

    private ContextEntry FindEntry(BindingContext context)
    {
        if (context == null) return null;
        foreach (var entry in _entries)
            if (ReferenceEquals(entry.Context, context)) return entry;
        return null;
    }

    private void OrderEntries()
    {
        _ordered.Clear();
        _ordered.AddRange(_entries);
        _ordered.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : b.Sequence.CompareTo(a.Sequence));
        for (int i = 0; i < _ordered.Count; i++) _ordered[i].Rank = i;
    }

    private void RebuildNameIndex()
    {
        _actionsByName.Clear();
        foreach (var entry in _entries)
        {
            foreach (var binding in entry.Context.Bindings)
            {
                var action = binding?.Action;
                if (action == null) continue;
                var name = action.Name;
                if (!string.IsNullOrEmpty(name)) _actionsByName.TryAdd(name, action);
            }
        }
    }

    /// <summary>Finds an action by <see cref="GameAction.Name"/> among the active contexts.</summary>
    public GameAction FindAction(string name)
        => name != null && _actionsByName.TryGetValue(name, out var action) ? action : null;

    // ------------------------------------------------------------------
    //  Raw input
    // ------------------------------------------------------------------

    /// <summary>Whether this player listens to the device that produced <paramref name="event"/>.</summary>
    public bool Accepts(InputEvent @event) => @event switch
    {
        InputEventKey or InputEventMouse => AcceptKeyboardMouse,
        InputEventJoypadButton or InputEventJoypadMotion => JoypadDevices.Count == 0 || JoypadDevices.Contains(@event.Device),
        InputEventAction => AcceptActionEvents,
        _ => false,
    };

    /// <summary>
    /// Applies an event to this player's raw state right away (tests, replays, custom routing).
    /// Returns true when a running capture took the event instead.
    /// </summary>
    public bool FeedEvent(InputEvent @event) => FeedEvent(@event, IntakeStage.Direct);

    /// <summary>
    /// Applies an event as offered from one stage of Godot's input propagation. Returns true when a
    /// running capture took the event instead (it then does not reach any action).
    /// </summary>
    public bool FeedEvent(InputEvent @event, IntakeStage stage)
    {
        if (@event == null) return false;
        if (TryCapture(@event, stage)) return true;

        if (@event is InputEventAction actionEvent && RouteGameActionEvents)
        {
            var action = FindAction(actionEvent.Action);
            if (action != null)
            {
                bool allowed = stage switch
                {
                    IntakeStage.Early => !actionEvent.Pressed,
                    IntakeStage.Unhandled => actionEvent.Pressed,
                    _ => true,
                };
                if (allowed)
                {
                    if (actionEvent.Pressed) StartInjecting(action, new Vector3(actionEvent.Strength, 0f, 0f));
                    else StopInjecting(action);
                }
            }
        }

        Raw.Apply(@event, stage);
        return false;
    }

    /// <summary>Sets a named virtual control (on-screen sticks, touch, tests).</summary>
    public void SetVirtualControl(string name, Vector3 value) => Raw.Set(SourceKey.ForVirtual(name), value);

    /// <summary>Sets a named virtual control.</summary>
    public void SetVirtualControl(string name, Vector2 value) => SetVirtualControl(name, new Vector3(value.X, value.Y, 0f));

    /// <summary>Sets a named virtual control.</summary>
    public void SetVirtualControl(string name, float value) => SetVirtualControl(name, new Vector3(value, 0f, 0f));

    /// <summary>Forgets all raw input; held actions finish on the next tick.</summary>
    public void ClearRawInput()
    {
        Raw.Clear();
        foreach (var injection in _injections.Values) injection.Continuous = false;
    }

    // ------------------------------------------------------------------
    //  Injection
    // ------------------------------------------------------------------

    /// <summary>
    /// Feeds a value to an action for the next tick only, as if from an extra binding with no
    /// modifiers or triggers. The action's own modifiers and triggers still apply.
    /// </summary>
    public void InjectInput(GameAction action, Vector3 value)
    {
        if (action == null) return;
        GetInjection(action).AddPeak(value);
    }

    /// <inheritdoc cref="InjectInput(GameAction, Vector3)"/>
    public void InjectInput(GameAction action, Vector2 value) => InjectInput(action, new Vector3(value.X, value.Y, 0f));

    /// <inheritdoc cref="InjectInput(GameAction, Vector3)"/>
    public void InjectInput(GameAction action, float value) => InjectInput(action, new Vector3(value, 0f, 0f));

    /// <inheritdoc cref="InjectInput(GameAction, Vector3)"/>
    public void InjectInput(GameAction action, bool value) => InjectInput(action, value ? 1f : 0f);

    /// <summary>Feeds a value to an action on every tick until <see cref="StopInjecting"/>.</summary>
    public void StartInjecting(GameAction action, Vector3 value)
    {
        if (action == null) return;
        var injection = GetInjection(action);
        injection.Continuous = true;
        injection.ContinuousValue = value;
        injection.AddPeak(value);
    }

    /// <summary>Stops a continuous injection. A press that started this tick is still seen once.</summary>
    public void StopInjecting(GameAction action)
    {
        if (action != null && _injections.TryGetValue(action, out var injection)) injection.Continuous = false;
    }

    private Injection GetInjection(GameAction action)
    {
        if (!_injections.TryGetValue(action, out var injection))
            _injections[action] = injection = new Injection();
        return injection;
    }

    // ------------------------------------------------------------------
    //  Polling
    // ------------------------------------------------------------------

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

    /// <summary>Like <see cref="GetPhaseThisTick"/>, for the events the other action raises this tick.</summary>
    public ActionEvents GetEventsThisTick(GameAction action)
    {
        if (action == null) return ActionEvents.None;
        if (_ticking) Resolve(action);
        return GetEvents(action);
    }

    private ActionState GetOrCreateState(GameAction action)
    {
        if (!_states.TryGetValue(action, out var state))
            _states[action] = state = new ActionState(action);
        return state;
    }

    // ------------------------------------------------------------------
    //  Subscriptions
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    //  View helpers for modifiers
    // ------------------------------------------------------------------

    /// <summary>The camera used for view-dependent modifiers, if any.</summary>
    public Camera3D GetViewCamera()
    {
        if (ViewCamera != null && IsInstanceValid(ViewCamera)) return ViewCamera;
        return IsInsideTree() ? GetViewport()?.GetCamera3D() : null;
    }

    /// <summary>The view basis (override, camera, or identity).</summary>
    public Basis GetViewBasis()
    {
        if (ViewBasisOverride.HasValue) return ViewBasisOverride.Value;
        var camera = GetViewCamera();
        if (camera == null) return Basis.Identity;
        return camera.IsInsideTree() ? camera.GlobalBasis : camera.Basis;
    }

    /// <summary>The vertical field of view in degrees (override, camera, or null).</summary>
    public float? GetViewFov()
    {
        if (ViewFovOverride.HasValue) return ViewFovOverride.Value;
        return GetViewCamera()?.Fov;
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private static void EnsureArray<T>(ref T[] array, int count) where T : class, new()
    {
        if (array.Length != count)
        {
            int old = array.Length;
            System.Array.Resize(ref array, count);
            for (int i = old; i < count; i++) array[i] = new T();
        }
    }

    public override string ToString() => $"InputPlayer {PlayerIndex}";
}
