using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// A game's whole input setup in one resource (optional — everything also works with loose resources or
/// in code): its actions, binding contexts and the ones active from the start, control schemes
/// (<see cref="InputMode"/>s), default settings, haptic effects, touch layouts and prompt icons.
/// <code>
/// player.LoadConfiguration(gameInput);     // or EnhancedInput.Configuration for every player
/// player.ActivateContext("vehicle");
/// </code>
/// Everything is found by stable id or by name, so files can be renamed and moved.
/// </summary>
[Tool]
[GlobalClass]
public partial class EnhancedInputConfig : Resource
{
    private string _id = string.Empty;

    /// <summary>Stable identifier (falls back to <see cref="Name"/>).</summary>
    [Export]
    public string Id
    {
        get => string.IsNullOrEmpty(_id) ? Name : _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Name for logs and tools (falls back to the file name).</summary>
    [Export] public string Name { get; set; } = string.Empty;

    /// <summary>Every action of the game.</summary>
    [Export] public Array<EnhancedInputAction> Actions { get; set; } = new();

    /// <summary>Every binding context of the game ("gameplay", "vehicle", "menu"…).</summary>
    [Export] public Array<EnhancedInputMappingContext> Contexts { get; set; } = new();

    /// <summary>Contexts active as soon as the configuration is loaded, each at its <see cref="EnhancedInputMappingContext.DefaultPriority"/>.</summary>
    [Export] public Array<EnhancedInputMappingContext> StartupContexts { get; set; } = new();

    /// <summary>Control schemes: keyboard and mouse, gamepad, touch, wheel… (see <see cref="InputMode"/>).</summary>
    [Export] public Array<InputMode> Modes { get; set; } = new();

    /// <summary>The scheme to start in (none: the first one, or none at all when there are no modes).</summary>
    [Export] public InputMode DefaultMode { get; set; }

    /// <summary>Setting values used when neither the player's profile nor the active mode sets them.</summary>
    [Export] public Dictionary<string, double> DefaultSettings { get; set; } = new();

    /// <summary>Haptic effects the game plays, found by name with <see cref="FindHapticEffect"/>.</summary>
    [Export] public Array<HapticEffect> HapticEffects { get; set; } = new();

    /// <summary>On-screen control layouts (default, left-handed…).</summary>
    [Export] public Array<TouchLayout> TouchLayouts { get; set; } = new();

    /// <summary>Icons for button prompts.</summary>
    [Export] public PromptIconSet PromptIcons { get; set; }

    /// <summary>Profile presets to offer (res:// paths of exported profiles: "default", "left-handed"…).</summary>
    [Export(PropertyHint.File, "*.json")] public string[] ProfilePresets { get; set; } = System.Array.Empty<string>();

    /// <summary>Free-form notes.</summary>
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

    /// <summary>An action by id, name or former id, or null.</summary>
    public EnhancedInputAction FindAction(string idOrName)
    {
        foreach (var action in Actions) if (action != null && action.Matches(idOrName)) return action;
        foreach (var context in Contexts)
            if (context != null)
                foreach (var binding in context.Bindings)
                    if (binding?.Action != null && binding.Action.Matches(idOrName)) return binding.Action;
        return null;
    }

    /// <summary>A context by id, name or former id, or null.</summary>
    public EnhancedInputMappingContext FindContext(string idOrName)
    {
        foreach (var context in Contexts) if (context != null && context.Matches(idOrName)) return context;
        foreach (var context in StartupContexts) if (context != null && context.Matches(idOrName)) return context;
        return null;
    }

    /// <summary>A control scheme by id or name, or null.</summary>
    public InputMode FindMode(string idOrName)
    {
        foreach (var mode in Modes) if (mode != null && (mode.Id == idOrName || mode.Name == idOrName)) return mode;
        return null;
    }

    /// <summary>A haptic effect by resource name or file name, or null.</summary>
    public HapticEffect FindHapticEffect(string name)
    {
        foreach (var effect in HapticEffects)
        {
            if (effect == null) continue;
            if (effect.ResourceName == name || (!string.IsNullOrEmpty(effect.ResourcePath) && effect.ResourcePath.GetFile().GetBaseName() == name)) return effect;
        }
        return null;
    }

    /// <summary>A touch layout by resource name or file name, or null.</summary>
    public TouchLayout FindTouchLayout(string name)
    {
        foreach (var layout in TouchLayouts)
        {
            if (layout == null) continue;
            if (layout.ResourceName == name || (!string.IsNullOrEmpty(layout.ResourcePath) && layout.ResourcePath.GetFile().GetBaseName() == name)) return layout;
        }
        return null;
    }

    /// <inheritdoc/>
    public override string ToString() => string.IsNullOrEmpty(Name) ? (string.IsNullOrEmpty(ResourcePath) ? "EnhancedInputConfig" : ResourcePath.GetFile().GetBaseName()) : Name;
}
