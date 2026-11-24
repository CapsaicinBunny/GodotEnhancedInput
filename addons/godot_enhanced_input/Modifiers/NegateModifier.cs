using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Flips the sign of the chosen components. On a quaternion action, flipping X, Y and Z (the default)
/// inverts the rotation; W is never flipped there (it would describe the same rotation).
/// </summary>
[Tool]
[GlobalClass]
public partial class NegateModifier : InputModifier
{
    [Export] public bool X { get; set; } = true;
    [Export] public bool Y { get; set; } = true;
    [Export] public bool Z { get; set; } = true;
    [Export] public bool W { get; set; } = true;

    public override Vector3 Apply(Vector3 value, in ModifierContext context)
        => new(X ? -value.X : value.X, Y ? -value.Y : value.Y, Z ? -value.Z : value.Z);

    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        bool w = W && context.Kind != ActionValueKind.Quaternion;
        return new Vector4(X ? -value.X : value.X, Y ? -value.Y : value.Y, Z ? -value.Z : value.Z, w ? -value.W : value.W);
    }
}
