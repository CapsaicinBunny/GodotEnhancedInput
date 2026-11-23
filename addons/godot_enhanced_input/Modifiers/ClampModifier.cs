using Godot;

namespace GodotEnhancedInput;

/// <summary>Clamps each component to a range.</summary>
[Tool]
[GlobalClass]
public partial class ClampModifier : InputModifier
{
    [Export] public Vector3 Min { get; set; } = new(-1f, -1f, -1f);
    [Export] public Vector3 Max { get; set; } = new(1f, 1f, 1f);

    public override Vector3 Apply(Vector3 v, in ModifierContext context) => v.Clamp(Min, Max);
}
