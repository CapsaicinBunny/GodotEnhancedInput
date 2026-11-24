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
    //  Contexts held for a scope
    // ------------------------------------------------------------------

    /// <summary>
    /// Adds a context and returns a handle that removes it when disposed — for a vehicle, a menu, a
    /// cutscene. Context changes take effect together at the next tick (or the next step, when made from a
    /// callback), so swapping several contexts never leaves a frame with a half-switched set.
    /// <code>
    /// _driving = player.PushContext(vehicle, priority: 100);
    /// …
    /// _driving.Dispose();   // back on foot
    /// </code>
    /// </summary>
    public ContextHandle PushContext(EnhancedInputMappingContext context, int priority = 0, ContextOptions options = default)
    {
        AddContext(context, priority, options);
        return new ContextHandle(this, context);
    }

    /// <summary>Replaces one active context by another in a single change (no frame with both or neither).</summary>
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

/// <summary>An active context that is removed when disposed (see <see cref="InputPlayer.PushContext"/>).</summary>
public sealed class ContextHandle : IDisposable
{
    internal ContextHandle(InputPlayer player, EnhancedInputMappingContext context)
    {
        Player = player;
        Context = context;
    }

    /// <summary>The player the context is active on.</summary>
    public InputPlayer Player { get; }

    /// <summary>The context.</summary>
    public EnhancedInputMappingContext Context { get; }

    /// <summary>False once disposed.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Removes the context (once).</summary>
    public void Dispose()
    {
        if (!IsActive) return;
        IsActive = false;
        if (GodotObject.IsInstanceValid(Player)) Player.RemoveContext(Context);
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
