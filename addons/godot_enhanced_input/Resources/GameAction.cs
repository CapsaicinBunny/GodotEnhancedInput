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
[Icon("res://addons/godot_enhanced_input/Icons/GameAction.svg")]
public partial class GameAction : Resource
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

    /// <summary>Text for menus and prompts. Falls back to <see cref="Name"/>.</summary>
    [Export] public string DisplayName { get; set; } = string.Empty;

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
    public static GameAction Create(string name, ActionValueKind kind = ActionValueKind.Digital)
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
    public override string ToString() => string.IsNullOrEmpty(Name) ? $"GameAction#{GetInstanceId()}" : Name;
}
