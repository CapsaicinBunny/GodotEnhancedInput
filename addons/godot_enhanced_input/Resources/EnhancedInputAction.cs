using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// Something a player wants to do — "Jump", "Move", "Look" — with the shape of its value.
/// Gameplay code reads actions, never keys. Actions are compared by resource identity: two
/// resources with the same name are two different actions.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/godot_enhanced_input/Icons/EnhancedInputAction.svg")]
public partial class EnhancedInputAction : EnhancedInputResource
{
    private string _name = string.Empty;

    /// <summary>
    /// Identifier used to match <see cref="InputEventAction"/> events and for logs and debug views.
    /// When empty, the resource file name (without extension) is used.
    /// </summary>
    [Export]
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? FallbackName() : _name;
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

    /// <summary>Ids this action had before; recordings and saves that use them still find it.</summary>
    [Export] public string[] FormerIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Whether a text is this action's id, name or a former id.</summary>
    public bool Matches(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return false;
        if (idOrName == Id || idOrName == Name) return true;
        foreach (var former in FormerIds) if (former == idOrName) return true;
        return false;
    }

    /// <summary>Text for menus and prompts. Falls back to <see cref="Name"/>.</summary>
    [Export] public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Input buffering (<see cref="InputPlayer.ConsumeTriggered"/>): keep a buffered press when the action is
    /// cut off (its context removed, the action disabled, focus lost…). Off by default: a cut-off forgets it.
    /// </summary>
    [Export] public bool KeepBufferedWhenCanceled { get; set; }

    /// <summary>
    /// How the action reports while it is held (see <see cref="ActionEvaluation"/>): every frame
    /// (default), only when its value changes, only on presses and releases, per control, or as a
    /// tracked pose.
    /// </summary>
    [Export] public ActionEvaluation Evaluation { get; set; } = ActionEvaluation.Continuous;

    /// <summary>The shape of the value.</summary>
    [Export] public ActionValueKind ValueKind { get; set; } = ActionValueKind.Digital;

    /// <summary>How the values of several bindings that drive this action at once are combined.</summary>
    [Export] public ValueAccumulation Accumulation { get; set; } = ValueAccumulation.Strongest;

    /// <summary>
    /// When on, the sources bound to this action in a context are hidden from all lower-priority
    /// contexts while that context is active.
    /// </summary>
    [Export] public bool ConsumeInput { get; set; } = true;

    /// <summary>When off, the action receives no input while the scene tree is paused.</summary>
    [Export] public bool TriggerWhenPaused { get; set; }

    /// <summary>Modifiers applied to the combined value of all bindings.</summary>
    [Export] public Array<InputModifier> Modifiers { get; set; } = new();

    /// <summary>Triggers evaluated on the combined value; they gate the bindings' phase.</summary>
    [Export] public Array<InputTrigger> Triggers { get; set; } = new();

    /// <summary>Free-form notes for designers.</summary>
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

    /// <summary>Creates an action in code.</summary>
    public static EnhancedInputAction Create(string name, ActionValueKind kind = ActionValueKind.Digital)
        => new() { Name = name, ValueKind = kind };

    /// <summary>Text for menus and prompts.</summary>
    public string GetDisplayName() => string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;

    private string FallbackName()
    {
        if (!string.IsNullOrEmpty(ResourceName)) return ResourceName;
        if (!string.IsNullOrEmpty(ResourcePath)) return ResourcePath.GetFile().GetBaseName();
        return string.Empty;
    }

    /// <inheritdoc/>
    public override string ToString() => string.IsNullOrEmpty(Name) ? $"EnhancedInputAction#{GetInstanceId()}" : Name;
}
