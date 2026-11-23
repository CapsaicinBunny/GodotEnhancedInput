using Godot;

namespace GodotEnhancedInput;

/// <summary>Multiplies each component by <see cref="Scale"/>.</summary>
[Tool]
[GlobalClass]
public partial class ScaleModifier : InputModifier
{
    [Export] public Vector3 Scale { get; set; } = Vector3.One;

    public override Vector3 Apply(Vector3 value, in ModifierContext context) => value * Scale;
}
