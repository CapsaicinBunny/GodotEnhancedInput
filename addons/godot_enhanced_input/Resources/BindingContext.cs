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

    /// <summary>The bindings in this context.</summary>
    [Export] public Array<ActionBinding> Bindings { get; set; } = new();

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
        return binding;
    }

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
