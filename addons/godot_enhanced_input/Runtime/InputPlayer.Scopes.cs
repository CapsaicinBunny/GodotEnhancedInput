using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Shorter reads, contexts held for a scope, input scopes tied to a node, and turning single actions off.</summary>
public partial class InputPlayer
{
    /// <summary>True on the frame the action's input went down (it started), whatever its triggers.</summary>
    public bool WasPressed(EnhancedInputAction action) => (GetEvents(action) & ActionEvents.Started) != 0;

    /// <summary>True on the frame its input was let go after it fired (completed; not when cut off).</summary>
    public bool WasReleased(EnhancedInputAction action) => (GetEvents(action) & ActionEvents.Completed) != 0;

    /// <summary>The value as on/off.</summary>
    public bool ReadBool(EnhancedInputAction action) => GetValue(action).AsBool();

    /// <summary>The value's first axis.</summary>
    public float ReadFloat(EnhancedInputAction action) => GetValue(action).AsFloat();

    /// <summary>The value's first two axes.</summary>
    public Vector2 ReadVector2(EnhancedInputAction action) => GetValue(action).AsVector2();

    /// <summary>The value's first three axes.</summary>
    public Vector3 ReadVector3(EnhancedInputAction action) => GetValue(action).AsVector3();

    /// <summary>The value as a rotation.</summary>
    public Quaternion ReadRotation(EnhancedInputAction action) => GetValue(action).AsQuaternion();

    // ------------------------------------------------------------------
    //  Input buffering
    // ------------------------------------------------------------------

    /// <summary>
    /// Input buffering: true once for each time the action became triggered, if that was within the last
    /// <paramref name="withinSeconds"/> of input time (or it is triggered now) and it has not been consumed
    /// yet. A press of Jump just before landing is kept until the character can jump:
    /// <code>
    /// if (IsOnFloor() &amp;&amp; player.ConsumeTriggered(jump, 0.15)) Jump();
    /// </code>
    /// Phases and events are not changed (a release still completes the action), so other code reading the
    /// action is unaffected. With a <see cref="BufferTrigger"/> on the binding, the action itself stays
    /// triggered for the buffer window and the window can be left at 0.
    /// </summary>
    public bool ConsumeTriggered(EnhancedInputAction action, double withinSeconds = 0.0)
    {
        if (!WasTriggeredWithin(action, withinSeconds)) return false;
        var state = GetState(action);
        state.ConsumedActivation = state.TriggerCount;
        return true;
    }

    /// <summary>
    /// Whether the action became triggered within the last <paramref name="withinSeconds"/> of input time,
    /// or is triggered now, and that activation was not consumed by <see cref="ConsumeTriggered"/>.
    /// </summary>
    public bool WasTriggeredWithin(EnhancedInputAction action, double withinSeconds)
    {
        if (action == null) return false;
        var state = GetState(action);
        if (state == null || state.TriggerCount == 0 || state.ConsumedActivation == state.TriggerCount) return false;
        return state.IsTriggered || InputTime - state.LastTriggeredTime <= Math.Max(0.0, withinSeconds) + 1e-9;
    }

    // ------------------------------------------------------------------
    //  Contexts held for a scope
    // ------------------------------------------------------------------

    /// <summary>
    /// Activates a context and returns a handle that releases it when disposed — for a vehicle, a menu, a
    /// cutscene. Context changes take effect together at the next tick (or the next step, when made from a
    /// callback), so swapping several contexts never leaves a frame with a half-switched set.
    /// <code>
    /// _driving = player.PushContext(vehicle, priority: 100);
    /// …
    /// _driving.Dispose();   // back on foot
    /// </code>
    /// Handles are counted: when several scopes push the same context, it stays active until the last
    /// handle is disposed (and while <see cref="AddContext"/> also holds it). Pushing a context that is
    /// already active shares it and keeps its priority and options. <see cref="RemoveContext"/>,
    /// <see cref="SwitchContext"/> and <see cref="ClearContexts"/> still remove it at once, for every holder.
    /// </summary>
    public ContextHandle PushContext(EnhancedInputMappingContext context, int priority = 0, ContextOptions options = default)
    {
        var entry = FindEntry(context) ?? AddContextCore(context, priority, options, direct: false);
        if (entry == null) return new ContextHandle(this, context, null);
        entry.Handles++;
        return new ContextHandle(this, context, entry);
    }

    /// <summary>Whether a handle's activation is still in place (its context was not removed another way).</summary>
    internal static bool IsHeld(object activation) => activation is ContextEntry { Removed: false };

    /// <summary>Releases one handle's hold; the context is removed when nothing holds it any more.</summary>
    internal void ReleaseHandle(object activation)
    {
        if (activation is not ContextEntry entry || entry.Removed) return;
        if (--entry.Handles > 0 || entry.Direct) return;
        RemoveContext(entry.Context);
    }

    /// <summary>
    /// Replaces one active context by another in a single change (no frame with both or neither).
    /// <paramref name="from"/> is removed whoever holds it, like <see cref="RemoveContext"/>.
    /// </summary>
    public void SwitchContext(EnhancedInputMappingContext from, EnhancedInputMappingContext to, int priority = 0, ContextOptions options = default)
    {
        if (from != null) RemoveContext(from);
        if (to != null) AddContext(to, priority, options);
    }

    /// <summary>
    /// A set of subscriptions and contexts released together. With an owner node, it is released when the
    /// node leaves the tree — so a character or a screen never leaves callbacks or contexts behind.
    /// <code>
    /// var input = player.CreateScope(this);
    /// input.OnTriggered(jump, Jump);
    /// input.PushContext(onFoot);
    /// </code>
    /// </summary>
    public InputScope CreateScope(Node owner = null) => new(this, owner);

    // ------------------------------------------------------------------
    //  Turning actions off
    // ------------------------------------------------------------------

    private readonly HashSet<EnhancedInputAction> _disabled = new();

    /// <summary>
    /// Turns one action off or on without touching contexts. A disabled action receives no input: held
    /// input ends canceled (reason <see cref="InputEndReason.Blocked"/>), and it stays idle until enabled.
    /// </summary>
    public void SetActionEnabled(EnhancedInputAction action, bool enabled)
    {
        if (action == null) return;
        if (enabled) _disabled.Remove(action);
        else _disabled.Add(action);
    }

    /// <summary>Whether an action is enabled (see <see cref="SetActionEnabled"/>).</summary>
    public bool IsActionEnabled(EnhancedInputAction action) => action != null && !_disabled.Contains(action);

    private bool IsDisabled(EnhancedInputAction action) => _disabled.Count > 0 && _disabled.Contains(action);
}

/// <summary>
/// A hold on an active context, released when disposed (see <see cref="InputPlayer.PushContext"/>). The
/// context is removed when its last hold is released.
/// </summary>
public sealed class ContextHandle : IDisposable
{
    private readonly object _activation;
    private bool _disposed;

    internal ContextHandle(InputPlayer player, EnhancedInputMappingContext context, object activation)
    {
        Player = player;
        Context = context;
        _activation = activation;
    }

    /// <summary>The player the context is active on.</summary>
    public InputPlayer Player { get; }

    /// <summary>The context.</summary>
    public EnhancedInputMappingContext Context { get; }

    /// <summary>
    /// True while this handle holds the context: false once disposed, or once the context was removed
    /// another way (<see cref="InputPlayer.RemoveContext"/>, <see cref="InputPlayer.SwitchContext"/>,
    /// <see cref="InputPlayer.ClearContexts"/>).
    /// </summary>
    public bool IsActive => !_disposed && InputPlayer.IsHeld(_activation);

    /// <summary>Releases this hold (once); the context is removed if nothing else holds it.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(Player)) Player.ReleaseHandle(_activation);
    }
}

/// <summary>
/// Subscriptions and contexts of one owner, released together (see <see cref="InputPlayer.CreateScope"/>).
/// </summary>
public sealed class InputScope : IDisposable
{
    private readonly List<IDisposable> _items = new();
    private readonly Node _owner;

    internal InputScope(InputPlayer player, Node owner)
    {
        Player = player;
        _owner = owner;
        if (owner != null) owner.TreeExiting += Dispose;
    }

    /// <summary>The player the scope belongs to.</summary>
    public InputPlayer Player { get; }

    /// <summary>False once disposed.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Calls <paramref name="callback"/> for the given events of an action until the scope ends.</summary>
    public InputPlayer.ActionSubscription On(EnhancedInputAction action, ActionEvents events, Action<ActionEventArgs> callback)
        => Keep(Player.Bind(action, events, callback));

    /// <summary>Calls <paramref name="callback"/> each time the action fires, until the scope ends.</summary>
    public InputPlayer.ActionSubscription OnTriggered(EnhancedInputAction action, Action callback) => On(action, ActionEvents.Triggered, _ => callback());

    /// <summary>Calls <paramref name="callback"/> when the action's input goes down, until the scope ends.</summary>
    public InputPlayer.ActionSubscription OnStarted(EnhancedInputAction action, Action callback) => On(action, ActionEvents.Started, _ => callback());

    /// <summary>Calls <paramref name="callback"/> when the action completes, until the scope ends.</summary>
    public InputPlayer.ActionSubscription OnCompleted(EnhancedInputAction action, Action callback) => On(action, ActionEvents.Completed, _ => callback());

    /// <summary>Activates a context until the scope ends.</summary>
    public ContextHandle PushContext(EnhancedInputMappingContext context, int priority = 0, ContextOptions options = default)
        => Keep(Player.PushContext(context, priority, options));

    /// <summary>Adds anything disposable to the scope.</summary>
    public T Keep<T>(T item) where T : IDisposable
    {
        if (!IsActive) { item?.Dispose(); return item; }
        if (item != null) _items.Add(item);
        return item;
    }

    /// <summary>Releases everything in the scope (once): subscriptions end, contexts are removed.</summary>
    public void Dispose()
    {
        if (!IsActive) return;
        IsActive = false;
        if (_owner != null && GodotObject.IsInstanceValid(_owner)) _owner.TreeExiting -= Dispose;
        for (int i = _items.Count - 1; i >= 0; i--) _items[i].Dispose();
        _items.Clear();
    }
}
