using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>One rebindable slot of a player: every binding that shares a rebind id.</summary>
public sealed class RebindSlot
{
    internal RebindSlot(string id) { Id = id; }

    /// <summary>The rebind id.</summary>
    public string Id { get; }

    /// <summary>Name for menus (the first binding's rebind display name, or its action's display name).</summary>
    public string DisplayName { get; internal set; }

    /// <summary>Group for menus.</summary>
    public string Category { get; internal set; }

    /// <summary>The action the slot drives.</summary>
    public GameAction Action { get; internal set; }

    /// <summary>The designer's source (from the first binding with this id).</summary>
    public InputSource DefaultSource { get; internal set; }

    /// <summary>The source in use: the profile's override, or the default. Null when cleared.</summary>
    public InputSource CurrentSource { get; internal set; }

    /// <summary>Whether the player's profile overrides this slot.</summary>
    public bool IsOverridden { get; internal set; }

    /// <summary>The bindings with this id, with the context each one is in.</summary>
    public IReadOnlyList<(BindingContext Context, ActionBinding Binding)> Bindings => _bindings;

    internal readonly List<(BindingContext Context, ActionBinding Binding)> _bindings = new();

    /// <inheritdoc/>
    public override string ToString() => $"{Id}: {CurrentSource?.Describe() ?? "None"}{(IsOverridden ? " *" : "")}";
}

/// <summary>Another binding that reads the same input as a source being assigned to a slot.</summary>
public readonly struct RebindConflict
{
    /// <summary>Creates a conflict record.</summary>
    public RebindConflict(BindingContext context, ActionBinding binding, InputSource source)
    {
        Context = context;
        Binding = binding;
        Source = source;
    }

    /// <summary>The context of the conflicting binding.</summary>
    public BindingContext Context { get; }

    /// <summary>The conflicting binding.</summary>
    public ActionBinding Binding { get; }

    /// <summary>Its current source.</summary>
    public InputSource Source { get; }

    /// <summary>The conflicting binding's rebind id, or null when it is fixed.</summary>
    public string RebindId => Binding.IsRebindable ? Binding.RebindId : null;

    /// <summary>The conflicting binding's action.</summary>
    public GameAction Action => Binding.Action;

    /// <inheritdoc/>
    public override string ToString() => $"{Action} ({RebindId ?? "fixed"}) in {Context}: {Source?.Describe()}";
}

/// <summary>What a rebind did.</summary>
public sealed class RebindResult
{
    internal RebindResult(string id) { Id = id; }

    /// <summary>The slot.</summary>
    public string Id { get; }

    /// <summary>Whether the slot now uses the new source.</summary>
    public bool Applied { get; internal set; }

    /// <summary>The source requested (null to clear).</summary>
    public InputSource Source { get; internal set; }

    /// <summary>The slot's source before the change.</summary>
    public InputSource PreviousSource { get; internal set; }

    /// <summary>Bindings that read the same input, found before the change.</summary>
    public List<RebindConflict> Conflicts { get; } = new();

    /// <summary>Rebindable slots the policy changed (swapped or cleared).</summary>
    public List<string> ChangedSlots { get; } = new();

    /// <summary>Why nothing was applied, when <see cref="Applied"/> is false.</summary>
    public string FailureReason { get; internal set; }

    /// <inheritdoc/>
    public override string ToString()
        => Applied ? $"{Id} -> {Source?.Describe() ?? "None"} ({Conflicts.Count} conflicts)" : $"{Id} not rebound: {FailureReason}";
}

/// <summary>Settings for <see cref="InputPlayer.StartCapture"/>.</summary>
public sealed class CaptureOptions
{
    /// <summary>Accept keyboard keys.</summary>
    public bool Keyboard { get; set; } = true;

    /// <summary>Accept mouse buttons and wheel.</summary>
    public bool Mouse { get; set; } = true;

    /// <summary>Accept joypad buttons and axes.</summary>
    public bool Joypad { get; set; } = true;

    /// <summary>Keys that cancel the capture instead of being bound.</summary>
    public Key[] CancelKeys { get; set; } = { Key.Escape };

    /// <summary>Joypad buttons that cancel the capture instead of being bound.</summary>
    public JoyButton[] CancelJoyButtons { get; set; } = System.Array.Empty<JoyButton>();

    /// <summary>How far a joypad axis must move to be captured.</summary>
    public float AxisThreshold { get; set; } = 0.5f;

    /// <summary>How captured keys are matched.</summary>
    public KeyMatch KeyMatch { get; set; } = KeyMatch.Physical;

    /// <summary>Bind captured joypad input to any joypad the player uses, rather than the one pressed.</summary>
    public bool AnyJoypad { get; set; } = true;

    /// <summary>Keys that never bind (e.g. keys the menu itself uses); pressing them does nothing.</summary>
    public Key[] ExcludedKeys { get; set; } = System.Array.Empty<Key>();

    /// <summary>Joypad buttons that never bind.</summary>
    public JoyButton[] ExcludedJoyButtons { get; set; } = System.Array.Empty<JoyButton>();

    /// <summary>Optional rule: a captured source is used only when this returns true (others are ignored).</summary>
    public System.Func<InputSource, bool> Filter { get; set; }

    /// <summary>Ignore inputs held (and sticks off-centre) when the capture starts until they are released. On by default.</summary>
    public bool WaitForRelease { get; set; } = true;

    /// <summary>Capture modifier chords: holding Ctrl and pressing S binds "Ctrl + S"; a modifier released alone binds itself.</summary>
    public bool AllowChords { get; set; }

    /// <summary>Give up after this many seconds without a usable press (0: wait forever). Counted in player ticks.</summary>
    public double TimeoutSeconds { get; set; }

    /// <summary>What to do with conflicting bindings.</summary>
    public RebindConflictPolicy ConflictPolicy { get; set; } = RebindConflictPolicy.Allow;

    /// <summary>Where to look for conflicts.</summary>
    public ConflictScope ConflictScope { get; set; } = ConflictScope.SameContext;
}
