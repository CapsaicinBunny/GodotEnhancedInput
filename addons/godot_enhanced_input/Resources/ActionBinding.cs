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
