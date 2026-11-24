using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The common base of the plugin's three assets — <see cref="EnhancedInputAction"/>,
/// <see cref="EnhancedInputMappingContext"/> and <see cref="EnhancedInputConfig"/> — so that Godot's
/// <i>Create New Resource</i> dialog shows them together under one entry. It cannot be created itself.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/godot_enhanced_input/Icons/EnhancedInputResource.svg")]
public abstract partial class EnhancedInputResource : Resource
{
}
