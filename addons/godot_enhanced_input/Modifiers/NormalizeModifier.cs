using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Scales the value to unit length, so diagonals are not faster than straight lines. Vectors shorter
/// than <see cref="MinLength"/> become zero. With <see cref="OnlyIfLonger"/>, vectors up to length 1
/// are kept as they are and only longer ones are shortened.
/// </summary>
[Tool]
[GlobalClass]
public partial class NormalizeModifier : InputModifier
{
    /// <summary>Shorter vectors become zero.</summary>
    [Export] public float MinLength { get; set; } = 0.001f;
    /// <summary>Only shorten vectors longer than 1; keep shorter ones.</summary>
    [Export] public bool OnlyIfLonger { get; set; }

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        float length = value.Length();
        if (length == 0f || length < MinLength) return Vector3.Zero;
        if (OnlyIfLonger && length <= 1f) return value;
        return value / length;
    }

    /// <inheritdoc/>
    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        if (context.Kind == ActionValueKind.Quaternion)
            return ActionValue.IsZeroOf(context.Kind, value) ? Vector4.Zero : ActionValue.FromQuaternion(ActionValue.ToQuaternion(value));
        float length = value.Length();
        if (length == 0f || length < MinLength) return Vector4.Zero;
        if (OnlyIfLonger && length <= 1f) return value;
        return value / length;
    }
}
