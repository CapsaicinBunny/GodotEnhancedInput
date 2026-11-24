using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Multiplies each component by <see cref="Scale"/> (and W by <see cref="ScaleW"/>). On a quaternion
/// action it scales the rotation angle by <c>Scale.X</c> instead (2 = twice the turn, 0.5 = half).
/// </summary>
[Tool]
public partial class ScaleModifier : InputModifier
{
    /// <summary>Factor per component (rotations: X scales the angle).</summary>
    [Export] public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>Multiplier for the fourth component of 4D values.</summary>
    [Export] public float ScaleW { get; set; } = 1f;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context) => value * Scale;

    /// <inheritdoc/>
    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        if (context.Kind == ActionValueKind.Quaternion) return ActionValue.ScaleRotation(value, Scale.X);
        return new Vector4(value.X * Scale.X, value.Y * Scale.Y, value.Z * Scale.Z, value.W * ScaleW);
    }
}
