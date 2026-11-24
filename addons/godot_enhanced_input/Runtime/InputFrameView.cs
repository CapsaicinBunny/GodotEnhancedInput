using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>The update loops game code reads input in.</summary>
public enum InputLoop
{
    /// <summary><c>_PhysicsProcess</c>: fixed steps, zero, one or several per rendered frame.</summary>
    Physics,
    /// <summary><c>_Process</c>: once per rendered frame.</summary>
    Process,
}

/// <summary>
/// One update loop's view of a player's input events. The player ticks at its own rate; a view collects
/// the events of every tick and shows them for exactly one frame of its loop, from one
/// <see cref="Publish"/> to the next. So a short press that happens in a rendered frame without a physics
/// step is still seen by the next physics step (not lost), and a press is seen by one physics step even
/// when a frame runs several (not doubled) — whatever <see cref="TickMode"/> is used. The
/// <see cref="EnhancedInput"/> autoload publishes <see cref="InputPlayer.PhysicsFrame"/> at the start of
/// each physics step and <see cref="InputPlayer.ProcessFrame"/> each rendered frame (after ticking); with
/// manual ticking, call <see cref="Publish"/> where your loop's frame starts.
/// </summary>
public sealed class InputFrameView
{
    private Dictionary<EnhancedInputAction, ActionEvents> _pending = new();
    private Dictionary<EnhancedInputAction, ActionEvents> _published = new();

    internal InputFrameView(InputPlayer player, InputLoop loop)
    {
        Player = player;
        Loop = loop;
    }

    /// <summary>The player this view reads.</summary>
    public InputPlayer Player { get; }

    /// <summary>The loop this view is for.</summary>
    public InputLoop Loop { get; }

    /// <summary>How many frames have been published.</summary>
    public ulong Frame { get; private set; }

    internal void Collect(EnhancedInputAction action, ActionEvents events)
    {
        if (events == ActionEvents.None || action == null) return;
        _pending.TryGetValue(action, out var before);
        _pending[action] = before | events;
    }

    /// <summary>Starts a new frame of this loop: the events collected since the last call become visible, once.</summary>
    public void Publish()
    {
        (_published, _pending) = (_pending, _published);
        _pending.Clear();
        Frame++;
    }

    /// <summary>The events this frame of the loop sees for an action (every tick since the previous frame, together).</summary>
    public ActionEvents GetEvents(EnhancedInputAction action)
        => action != null && _published.TryGetValue(action, out var events) ? events : ActionEvents.None;

    /// <summary>The action's input went down since the previous frame of this loop.</summary>
    public bool WasPressed(EnhancedInputAction action) => (GetEvents(action) & ActionEvents.Started) != 0;

    /// <summary>The action fired since the previous frame of this loop.</summary>
    public bool WasTriggered(EnhancedInputAction action) => (GetEvents(action) & ActionEvents.Triggered) != 0;

    /// <summary>The action was released after firing since the previous frame of this loop.</summary>
    public bool WasReleased(EnhancedInputAction action) => (GetEvents(action) & ActionEvents.Completed) != 0;

    /// <summary>The action ended without firing, or was cut off, since the previous frame of this loop.</summary>
    public bool WasCanceled(EnhancedInputAction action) => (GetEvents(action) & ActionEvents.Canceled) != 0;

    /// <summary>The action is triggered now (state, not an event: the same in every loop).</summary>
    public bool IsTriggered(EnhancedInputAction action) => Player != null && Player.IsTriggered(action);

    /// <summary>The action's current value.</summary>
    public ActionValue Read(EnhancedInputAction action) => Player?.GetValue(action) ?? ActionValue.Zero(action?.ValueKind ?? ActionValueKind.Digital);
}
