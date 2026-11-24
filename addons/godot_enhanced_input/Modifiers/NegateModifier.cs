using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Flips the sign of the chosen components. On a quaternion action, flipping X, Y and Z (the default)
/// inverts the rotation; W is never flipped there (it would describe the same rotation).
/// </summary>
[Tool]
public partial class NegateModifier : InputModifier
{
    /// <summary>Flip X.</summary>
    [Export] public bool X { get; set; } = true;
    /// <summary>Flip Y.</summary>
    [Export] public bool Y { get; set; } = true;
    /// <summary>Flip Z.</summary>
    [Export] public bool Z { get; set; } = true;
    /// <summary>Flip W (ignored for rotations).</summary>
    [Export] public bool W { get; set; } = true;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
        => new(X ? -value.X : value.X, Y ? -value.Y : value.Y, Z ? -value.Z : value.Z);

    /// <inheritdoc/>
    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        bool w = W && context.Kind != ActionValueKind.Quaternion;
        return new Vector4(X ? -value.X : value.X, Y ? -value.Y : value.Y, Z ? -value.Z : value.Z, w ? -value.W : value.W);
    }
}
