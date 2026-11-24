using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// A set of bindings that are switched on and off together ("on foot", "driving", "menu"). Add it to
/// a player with a priority; higher priorities are evaluated first and may consume or block the
/// sources of lower ones. Contexts are compared by resource identity.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/godot_enhanced_input/Icons/BindingContext.svg")]
public partial class BindingContext : Resource
{
    private string _name = string.Empty;

    /// <summary>Name for logs and debug views. Falls back to the resource file name.</summary>
    [Export]
    public string Name
    {
        get
        {
            if (!string.IsNullOrEmpty(_name)) return _name;
            if (!string.IsNullOrEmpty(ResourceName)) return ResourceName;
            return string.IsNullOrEmpty(ResourcePath) ? string.Empty : ResourcePath.GetFile().GetBaseName();
        }
        set => _name = value ?? string.Empty;
    }

    /// <summary>
    /// Stable identifier, independent of the name shown, the file name and the object — saved settings,
    /// recordings, configurations and tools refer to it, so renaming or moving the resource breaks nothing.
    /// Empty: <see cref="Name"/> (it is written into the file the first time the resource is saved).
    /// </summary>
    [Export]
    public string Id
    {
        get => string.IsNullOrEmpty(_id) ? Name : _id;
        set => _id = value ?? string.Empty;
    }

    private string _id = string.Empty;

    /// <summary>Ids this context had before.</summary>
    [Export] public string[] FormerIds { get; set; } = System.Array.Empty<string>();

    /// <summary>The priority it gets when activated without one (<see cref="InputPlayer.ActivateContext"/>, startup contexts).</summary>
    [Export] public int DefaultPriority { get; set; }

    /// <summary>Whether a text is this context's id, name or a former id.</summary>
    public bool Matches(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return false;
        if (idOrName == Id || idOrName == Name) return true;
        foreach (var former in FormerIds) if (former == idOrName) return true;
        return false;
    }

    private Array<ActionBinding> _bindings = new();

    /// <summary>
    /// The bindings in this context. Players compile active contexts once and reuse the result; adding
    /// or removing bindings is noticed, but after changing a binding in place (its source, modifiers,
    /// triggers or groups) while the context is active, call <see cref="NotifyBindingsChanged"/>.
    /// </summary>
    [Export]
    public Array<ActionBinding> Bindings
    {
        get => _bindings;
        set { _bindings = value ?? new(); Version++; }
    }

    /// <summary>Goes up whenever the bindings are known to have changed.</summary>
    public int Version { get; private set; }

    /// <summary>Tells players using this context that its bindings changed in place.</summary>
    public void NotifyBindingsChanged() => Version++;

    /// <summary>
    /// When on, every lower-priority context reads zero from all sources while this one is active —
    /// for modal screens such as a pause menu.
    /// </summary>
    [Export] public bool BlockLowerPriority { get; set; }

    /// <summary>Free-form notes for designers.</summary>
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

    /// <summary>Creates an empty context in code.</summary>
    public static BindingContext Create(string name) => new() { Name = name };

    /// <summary>Adds a binding built from the arguments and returns it.</summary>
    public ActionBinding Add(GameAction action, InputSource source, params Resource[] modifiersAndTriggers)
    {
        var binding = ActionBinding.Create(action, source, modifiersAndTriggers);
        Bindings.Add(binding);
        Version++;
        return binding;
    }

    /// <inheritdoc/>
    public override string ToString() => string.IsNullOrEmpty(Name) ? $"BindingContext#{GetInstanceId()}" : Name;
}

/// <summary>Options for adding a context to a player.</summary>
public struct ContextOptions
{
    /// <summary>
    /// Sources that are actuated at the moment the context is added read as zero in it until they have
    /// been released once — so the key that opened a menu does not also act inside it.
    /// </summary>
    public bool IgnoreHeldSourcesUntilReleased { get; set; }
}
