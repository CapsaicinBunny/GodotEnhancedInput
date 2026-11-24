using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// "This source drives this action", with modifiers and triggers that apply to this binding only.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/godot_enhanced_input/Icons/ActionBinding.svg")]
public partial class ActionBinding : Resource
{
    /// <summary>The action this binding drives.</summary>
    [Export] public GameAction Action { get; set; }

    /// <summary>What it reads. A null source reads zero.</summary>
    [Export] public InputSource Source { get; set; }

    /// <summary>Applied in order to this binding's value before its triggers.</summary>
    [Export] public Array<InputModifier> Modifiers { get; set; } = new();

    /// <summary>Decide this binding's phase. Empty: triggered while the value is non-zero.</summary>
    [Export] public Array<InputTrigger> Triggers { get; set; } = new();

    /// <summary>
    /// Binding groups this binding belongs to ("keyboard_mouse", "gamepad", "touch", "left_handed"…).
    /// When the player has an active <see cref="InputMode"/>, a binding with groups is evaluated only if
    /// the mode turns one of them on. Empty: always evaluated.
    /// </summary>
    [Export] public string[] Groups { get; set; } = System.Array.Empty<string>();

    /// <summary>Puts the binding in groups and returns it (for code-built contexts).</summary>
    public ActionBinding InGroups(params string[] groups)
    {
        Groups = groups ?? System.Array.Empty<string>();
        return this;
    }

    /// <summary>
    /// Makes the binding rebindable: a player's profile can replace its source under this id. Bindings
    /// in different contexts that share an id are rebound together. Empty: the binding is fixed.
    /// </summary>
    [ExportGroup("Rebinding")]
    [Export] public string RebindId { get; set; } = string.Empty;

    /// <summary>Name shown in rebinding menus. Falls back to the action's display name.</summary>
    [Export] public string RebindDisplayName { get; set; } = string.Empty;

    /// <summary>Group for rebinding menus, e.g. "Keyboard", "Gamepad", "Combat".</summary>
    [Export] public string RebindCategory { get; set; } = string.Empty;

    /// <summary>Ids this binding had before; saved profiles that use them still apply.</summary>
    [Export] public string[] RebindFormerIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Whether this binding can be rebound.</summary>
    public bool IsRebindable => !string.IsNullOrEmpty(RebindId);

    /// <summary>Makes the binding rebindable and returns it (for code-built contexts).</summary>
    public ActionBinding Rebindable(string id, string displayName = null, string category = null)
    {
        RebindId = id ?? string.Empty;
        RebindDisplayName = displayName ?? string.Empty;
        RebindCategory = category ?? string.Empty;
        return this;
    }

    /// <summary>Creates a binding in code. Extra arguments may be modifiers and triggers, in order.</summary>
    public static ActionBinding Create(GameAction action, InputSource source, params Resource[] modifiersAndTriggers)
    {
        var binding = new ActionBinding { Action = action, Source = source };
        foreach (var extra in modifiersAndTriggers)
        {
            switch (extra)
            {
                case InputModifier modifier: binding.Modifiers.Add(modifier); break;
                case InputTrigger trigger: binding.Triggers.Add(trigger); break;
            }
        }
        return binding;
    }

    public override string ToString() => $"{Action} <- {Source?.Describe() ?? "None"}";
}
