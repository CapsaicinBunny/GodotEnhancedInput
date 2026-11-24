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
    [Signal] public delegate void ActionStartedEventHandler(EnhancedInputAction action);

    /// <summary>Raised every tick an action is ongoing.</summary>
    [Signal] public delegate void ActionOngoingEventHandler(EnhancedInputAction action);

    /// <summary>Raised every tick an action is triggered.</summary>
    [Signal] public delegate void ActionTriggeredEventHandler(EnhancedInputAction action);

    /// <summary>Raised when an action goes from triggered back to idle.</summary>
    [Signal] public delegate void ActionCompletedEventHandler(EnhancedInputAction action);

    /// <summary>Raised when an action goes from ongoing back to idle without triggering.</summary>
    [Signal] public delegate void ActionCanceledEventHandler(EnhancedInputAction action);

    /// <summary>Raised when a context is added (or re-added with a new priority).</summary>
    [Signal] public delegate void ContextAddedEventHandler(EnhancedInputMappingContext context, int priority);

    /// <summary>Raised when a context is removed.</summary>
    [Signal] public delegate void ContextRemovedEventHandler(EnhancedInputMappingContext context);

    /// <summary>Every action event of this player, in the order they are raised.</summary>
    public event Action<ActionEventArgs> ActionEventRaised;

    // ------------------------------------------------------------------
    //  Settings
    // ------------------------------------------------------------------

    /// <summary>Index of this player in <see cref="EnhancedInput.Players"/>.</summary>
    public int PlayerIndex { get; internal set; }

    /// <summary>Whether keyboard and mouse events reach this player.</summary>
    [Export] public bool AcceptKeyboardMouse { get; set; } = true;

    /// <summary>
    /// Joypad device indices this player owns. Empty: the player receives the joypads no other player
    /// owns. Change it through <see cref="EnhancedInput.AssignJoypad(InputPlayer, int)"/> to keep ownership exclusive.
    /// </summary>
    [Export] public Array<int> JoypadDevices { get; set; } = new();

    /// <summary>Whether touchscreen events reach this player.</summary>
    [Export] public bool AcceptTouch { get; set; } = true;

    /// <summary>
    /// When on and <see cref="JoypadDevices"/> is empty, the player receives joypads nobody owns.
    /// On for player 0, off for players added later, so an unowned pad never drives several players.
    /// </summary>
    [Export] public bool ReceivesUnownedJoypads { get; set; } = true;

    /// <summary>Whether <see cref="InputEventAction"/> events reach this player.</summary>
    [Export] public bool AcceptActionEvents { get; set; } = true;

    /// <summary>
    /// When on, InputMap sources also poll <c>Input.GetActionRawStrength</c>, so
    /// <c>Input.ActionPress</c> and InputMap-bound devices drive them even without events. That is
    /// Godot's global input state: it ignores this player's device filter and what the GUI accepted.
    /// On for player 0, off for players added later; turn it off to read InputMap actions only from
    /// the <see cref="InputEventAction"/> events this player receives.
    /// </summary>
    [Export] public bool PollInputMap { get; set; } = true;

    /// <summary>
    /// When on, an <see cref="InputEventAction"/> whose action name equals the <see cref="EnhancedInputAction.Name"/>
    /// of an action in an active context drives that action directly, as injected input.
    /// </summary>
    [Export] public bool RouteGameActionEvents { get; set; } = true;

    /// <summary>Camera used by view-dependent modifiers. When null, the viewport's current 3D camera is used.</summary>
    [Export] public Camera3D ViewCamera { get; set; }

    /// <summary>Overrides the view basis for view-dependent modifiers (handy for 2D games and tests).</summary>
    public Basis? ViewBasisOverride { get; set; }

    /// <summary>Overrides the vertical field of view, in degrees, for <see cref="FovScaleModifier"/>.</summary>
    public float? ViewFovOverride { get; set; }

    /// <summary>Overrides the screen DPI for <see cref="DpiScaleModifier"/> (tests, custom displays).</summary>
    public float? ScreenDpiOverride { get; set; }

    /// <summary>The raw input this player has received.</summary>
    public RawInputState Raw { get; } = new();

    /// <summary>How analog input between ticks is sampled (see <see cref="RawInputState.AnalogSampling"/>).</summary>
    public AnalogSampling AnalogSampling
    {
        get => Raw.AnalogSampling;
        set => Raw.AnalogSampling = value;
    }

    /// <summary>Creates a player (the autoload creates them; standalone players work too).</summary>
    public InputPlayer()
    {
        Raw.CutOff += RecordCutOff;
    }

    /// <summary>Number of ticks so far.</summary>
    public ulong TickCount => _tick;

    /// <summary>Seconds passed to the current (or last) tick.</summary>
    public double LastDelta => _delta;

    // ------------------------------------------------------------------
    //  State
    // ------------------------------------------------------------------

    private sealed class ContextEntry
    {
        public EnhancedInputMappingContext Context;
        public int Priority;
        public ulong Sequence;
        public ContextOptions Options;
        public int Rank;
        public readonly HashSet<SourceKey> Ignored = new();

        // Who holds the activation: handles from PushContext, and AddContext called directly. The
        // context stays active while any holder remains; Removed ends the activation for every holder.
        public int Handles;
        public bool Direct;
        public bool Removed;
        public BindingMemory[] Memories = System.Array.Empty<BindingMemory>();

        // What the compiled plan saw of the context.
        public int PlanVersion = -1;
        public int PlanCount = -1;
        public Godot.Collections.Array<ActionBinding> PlanArray;

        /// <summary>
        /// The binding's memory; a fresh one when the binding, its effective source, or its modifier or
        /// trigger lists changed (so a half-done hold or smoothing history never survives a rebind).
        /// </summary>
        public BindingMemory GetMemory(CompiledBinding compiled, out bool replaced)
        {
            int index = compiled.Index;
            if (Memories.Length <= index) System.Array.Resize(ref Memories, index + 1);
            var memory = Memories[index];
            replaced = false;
            if (memory == null || !memory.Matches(compiled))
            {
                replaced = memory != null;
                Memories[index] = memory = new BindingMemory
                {
                    Binding = compiled.Binding, Source = compiled.Source, ModifierRefs = compiled.Modifiers, TriggerRefs = compiled.Triggers,
                };
            }
            memory.Ensure(compiled.Modifiers.Length, compiled.Triggers.Length);
            return memory;
        }
    }

    /// <summary>One active binding, read once from its resources when the plan is compiled.</summary>
    private sealed class CompiledBinding
    {
        public ContextEntry Entry;
        public int Index;
        public ActionBinding Binding;
        public EnhancedInputAction Action;
        public InputSource Source;
        public SourceKey[] Keys;
        public InputModifier[] Modifiers;
        public InputTrigger[] Triggers;
        public bool HasTriggers;
        public bool PollsSensors;
    }

    private sealed class BindingMemory
    {
        public ActionBinding Binding;
        public InputSource Source;
        public InputModifier[] ModifierRefs;
        public InputTrigger[] TriggerRefs;
        public ModifierMemory[] Modifiers = System.Array.Empty<ModifierMemory>();

        public bool Matches(CompiledBinding compiled)
        {
            if (!ReferenceEquals(Binding, compiled.Binding) || !ReferenceEquals(Source, compiled.Source)) return false;
            if (ReferenceEquals(ModifierRefs, compiled.Modifiers) && ReferenceEquals(TriggerRefs, compiled.Triggers)) return true;
            if (!SameItems(ModifierRefs, compiled.Modifiers) || !SameItems(TriggerRefs, compiled.Triggers)) return false;
            ModifierRefs = compiled.Modifiers;   // same items, recompiled array
            TriggerRefs = compiled.Triggers;
            return true;
        }

        private static bool SameItems<T>(T[] a, T[] b) where T : class
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }
        public TriggerMemory[] Triggers = System.Array.Empty<TriggerMemory>();

        public void Ensure(int modifiers, int triggers)
        {
            EnsureArray(ref Modifiers, modifiers);
            EnsureArray(ref Triggers, triggers);
        }

        /// <summary>Forgets timers, counters and smoothing (used when the binding is suppressed).</summary>
        public void Reset()
        {
            foreach (var m in Modifiers) m?.Reset();
            foreach (var t in Triggers) t?.Reset();
        }

        /// <summary>Set while the binding is hidden (blocked, consumed, paused); see <see cref="Resume"/>.</summary>
        public bool Suppressed;

        /// <summary>
        /// The binding is evaluated again after being hidden. If its input is held now, it was held all
        /// along: its triggers start as already actuated, so a held key does not look freshly pressed
        /// (closing a menu with Esc does not also press "pause"), while held actions simply continue.
        /// </summary>
        public void Resume(bool held)
        {
            Suppressed = false;
            if (!held) return;
            foreach (var t in Triggers)
                if (t != null) t.WasActuated = true;
        }
    }

    private sealed class Injection
    {
        public bool Continuous;
        public Vector4 ContinuousValue;
        public bool HasPeak;
        public Vector4 Peak;

        public bool TryRead(out Vector4 value)
        {
            if (Continuous) { value = ContinuousValue; return true; }
            if (HasPeak) { value = Peak; return true; }
            value = Vector4.Zero;
            return false;
        }

        public void AddPeak(Vector4 value)
        {
            if (!HasPeak || value.LengthSquared() > Peak.LengthSquared()) Peak = value;
            HasPeak = true;
        }
    }

    /// <summary>A handle for a callback registered with <see cref="Bind"/>. Dispose it to unsubscribe.</summary>
    public sealed class ActionSubscription : IDisposable
    {
        internal ActionSubscription(InputPlayer player, EnhancedInputAction action, ActionEvents events, Action<ActionEventArgs> callback)
        {
            Player = player;
            Action = action;
            Events = events;
            Callback = callback;
        }

        /// <summary>The player the callback is registered with.</summary>
        public InputPlayer Player { get; }
        /// <summary>The action it listens to.</summary>
        public EnhancedInputAction Action { get; }
        /// <summary>The events it is called for.</summary>
        public ActionEvents Events { get; }
        internal Action<ActionEventArgs> Callback { get; }
        /// <summary>False once unsubscribed.</summary>
        public bool IsActive { get; internal set; } = true;

        /// <summary>Unsubscribes.</summary>
        public void Dispose() => Player.Unbind(this);
    }

    private readonly List<ContextEntry> _entries = new();
    private readonly List<ContextEntry> _ordered = new();
    private readonly System.Collections.Generic.Dictionary<EnhancedInputAction, ActionState> _states = new();
    private readonly System.Collections.Generic.Dictionary<EnhancedInputAction, List<CompiledBinding>> _bindingsByAction = new();
    private readonly List<CompiledBinding> _plan = new();
    private bool _planDirty = true;
    private InputEndReason _planChangeReason = InputEndReason.ContextRemoved;
    private ulong _step;
    private readonly System.Collections.Generic.Dictionary<string, EnhancedInputAction> _actionsByName = new(StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<SourceKey, int> _consumedBy = new();
    private readonly System.Collections.Generic.Dictionary<EnhancedInputAction, Injection> _injections = new();
    private readonly System.Collections.Generic.Dictionary<EnhancedInputAction, List<ActionSubscription>> _subscriptions = new();
    private readonly System.Collections.Generic.Dictionary<string, StringName> _inputMapNames = new(StringComparer.Ordinal);
    private readonly List<EnhancedInputAction> _tickActions = new();
    private readonly HashSet<EnhancedInputAction> _tickActionSet = new();
    private readonly List<SourceKey> _keyScratch = new();

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
    /// added). Adding a context that is already active updates its priority and options. A context
    /// added this way stays active until <see cref="RemoveContext"/>, even when handles from
    /// <see cref="PushContext"/> that share it are disposed.
    /// </summary>
    public void AddContext(EnhancedInputMappingContext context, int priority = 0, ContextOptions options = default)
        => AddContextCore(context, priority, options, direct: true);

    private ContextEntry AddContextCore(EnhancedInputMappingContext context, int priority, ContextOptions options, bool direct)
    {
        if (context == null) return null;

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
        if (direct) entry.Direct = true;

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
                    var channel = Raw.PhysicalOf(key);
                    if (ReadRaw(channel, null) != Vector4.Zero) entry.Ignored.Add(channel);
                }
            }
        }

        _planDirty = true;
        _planChangeReason = InputEndReason.ContextRemoved;
        RebuildNameIndex();
        EmitSignal(SignalName.ContextAdded, context, priority);
        return entry;
    }

    /// <summary>
    /// Deactivates a context, whoever activated it: handles from <see cref="PushContext"/> that held it
    /// become inactive (disposing them later does nothing). Its actions finish normally on the next
    /// tick. Returns false if it was not active.
    /// </summary>
    public bool RemoveContext(EnhancedInputMappingContext context)
    {
        var entry = FindEntry(context);
        if (entry == null) return false;
        entry.Removed = true;
        _entries.Remove(entry);
        _planDirty = true;
        _planChangeReason = InputEndReason.ContextRemoved;
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
    public bool HasContext(EnhancedInputMappingContext context) => FindEntry(context) != null;

    /// <summary>The priority of an active context, or null.</summary>
    public int? GetContextPriority(EnhancedInputMappingContext context) => FindEntry(context)?.Priority;

    /// <summary>The active contexts in evaluation order (highest priority first).</summary>
    public List<EnhancedInputMappingContext> GetContexts()
    {
        // A separate list: this may be called from callbacks while a tick walks _ordered.
        var entries = new List<ContextEntry>(_entries);
        entries.Sort(CompareEntries);
        var result = new List<EnhancedInputMappingContext>(entries.Count);
        foreach (var entry in entries) result.Add(entry.Context);
        return result;
    }

    private static int CompareEntries(ContextEntry a, ContextEntry b)
        => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : b.Sequence.CompareTo(a.Sequence);

    private ContextEntry FindEntry(EnhancedInputMappingContext context)
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
        _ordered.Sort(CompareEntries);
        for (int i = 0; i < _ordered.Count; i++) _ordered[i].Rank = i;
    }

    private void RebuildNameIndex()
    {
        _actionsByName.Clear();
        foreach (var entry in _entries)
            foreach (var binding in entry.Context.Bindings)
                IndexAction(binding?.Action);
        if (Configuration != null)
            foreach (var action in Configuration.Actions) IndexAction(action);
    }

    private void IndexAction(EnhancedInputAction action)
    {
        if (action == null) return;
        if (!string.IsNullOrEmpty(action.Id)) _actionsByName.TryAdd(action.Id, action);
        if (!string.IsNullOrEmpty(action.Name)) _actionsByName.TryAdd(action.Name, action);
        foreach (var former in action.FormerIds)
            if (!string.IsNullOrEmpty(former)) _actionsByName.TryAdd(former, action);
    }

    /// <summary>Finds an action by id, name or former id among the active contexts and the loaded configuration.</summary>
    public EnhancedInputAction FindAction(string idOrName)
        => idOrName != null && _actionsByName.TryGetValue(idOrName, out var action) ? action : null;

    // ------------------------------------------------------------------
    //  Raw input
    // ------------------------------------------------------------------

    /// <summary>Whether this player listens to the device that produced <paramref name="event"/>.</summary>
    public bool Accepts(InputEvent @event) => @event switch
    {
        InputEventKey or InputEventMouse => AcceptKeyboardMouse,
        InputEventJoypadButton or InputEventJoypadMotion => JoypadDevices.Count == 0 || JoypadDevices.Contains(@event.Device),
        InputEventAction => AcceptActionEvents,
        InputEventScreenTouch or InputEventScreenDrag => AcceptTouch,
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
        if (@event == null || LiveInputBlocked) return false;
        if (TryCapture(@event, stage)) return true;
        RecordEvent(@event, stage);
        if (stage != IntakeStage.Unhandled) TrackDevice(@event);

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
                    if (actionEvent.Pressed) StartInjectingCore(action, new Vector4(actionEvent.Strength, 0f, 0f, 0f));
                    else StopInjectingCore(action);
                }
            }
        }

        Raw.Apply(@event, stage);
        return false;
    }

    /// <summary>Sets a named virtual control (on-screen sticks, touch, tests).</summary>
    public void SetVirtualControl(string name, Vector4 value)
    {
        if (LiveInputBlocked) return;
        Record(new RecordedInput { Kind = RecordedInputKind.Virtual, Name = name ?? string.Empty, Value = value });
        Raw.Set(SourceKey.ForVirtual(name), value);
    }

    /// <summary>Sets a named virtual control.</summary>
    public void SetVirtualControl(string name, Vector3 value) => SetVirtualControl(name, new Vector4(value.X, value.Y, value.Z, 0f));

    /// <summary>Sets a named virtual control to a rotation (stored as x, y, z, w).</summary>
    public void SetVirtualControl(string name, Quaternion value) => SetVirtualControl(name, ActionValue.FromQuaternion(value));

    /// <summary>Sets a named virtual control.</summary>
    public void SetVirtualControl(string name, Vector2 value) => SetVirtualControl(name, new Vector4(value.X, value.Y, 0f, 0f));

    /// <summary>Sets a named virtual control.</summary>
    public void SetVirtualControl(string name, float value) => SetVirtualControl(name, new Vector4(value, 0f, 0f, 0f));

    /// <summary>Sets a virtual control to zero as cut off (not released): release-based triggers do not fire.</summary>
    public void CancelVirtualControl(string name, InputEndReason reason = InputEndReason.Cleared)
    {
        if (LiveInputBlocked) return;
        Raw.Cancel(SourceKey.ForVirtual(name), reason);
    }

    /// <summary>Adds to a named virtual control for the next tick only, on top of its held value (drags, flicks).</summary>
    public void AddToVirtualControl(string name, Vector4 delta)
    {
        if (LiveInputBlocked) return;
        Record(new RecordedInput { Kind = RecordedInputKind.VirtualDelta, Name = name ?? string.Empty, Value = delta });
        Raw.AddVirtualDelta(name, delta);
    }

    /// <summary>Adds to a named virtual control for the next tick only.</summary>
    public void AddToVirtualControl(string name, Vector3 delta) => AddToVirtualControl(name, new Vector4(delta.X, delta.Y, delta.Z, 0f));

    /// <summary>Adds to a named virtual control for the next tick only.</summary>
    public void AddToVirtualControl(string name, Vector2 delta) => AddToVirtualControl(name, new Vector4(delta.X, delta.Y, 0f, 0f));

    /// <summary>
    /// Forgets all raw input. Held actions end on the next tick as cut off (<see cref="ActionEvents.Canceled"/>
    /// with <paramref name="reason"/>), so release-based triggers do not fire.
    /// </summary>
    public void ClearRawInput(InputEndReason reason = InputEndReason.Cleared)
    {
        bool replayOwnsIt = _replay != null && !_replayFeeding && reason != InputEndReason.Replay;
        if (replayOwnsIt) return;   // live cut-offs (focus loss…) do not reach a replay
        Raw.Clear(reason, force: true);
        foreach (var pair in _injections)
        {
            // Injected input is cut off like any other: its action ends canceled, with the reason.
            if (pair.Value.Continuous || pair.Value.HasPeak) _injectionCuts[pair.Key] = reason;
            pair.Value.Continuous = false;
            pair.Value.HasPeak = false;
        }
    }

    private readonly System.Collections.Generic.Dictionary<EnhancedInputAction, InputEndReason> _injectionCuts = new();

    // ------------------------------------------------------------------
    //  View helpers for modifiers
    // ------------------------------------------------------------------

    /// <summary>
    /// The viewport whose pixels pointer positions are in (for <see cref="PointerSpaceModifier"/>).
    /// Defaults to this player's own viewport; set it for split screen or a SubViewport.
    /// </summary>
    public Viewport PointerViewport { get; set; }

    /// <summary>Overrides the pointer viewport's size, in pixels (tests, custom layouts).</summary>
    public Vector2? ViewportSizeOverride { get; set; }

    /// <summary>Overrides the pointer viewport's canvas transform (tests, custom layouts).</summary>
    public Transform2D? CanvasTransformOverride { get; set; }

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

    /// <inheritdoc/>
    public override string ToString() => $"InputPlayer {PlayerIndex}";
}
