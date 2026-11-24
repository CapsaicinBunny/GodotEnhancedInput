using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Smooths a quaternion by following it along the shortest arc at <see cref="Rate"/> per second
/// (frame-rate independent). Use it on tracked controllers or device orientation where per-component
/// smoothing would bend the rotation. The first value passes straight through.
/// </summary>
[Tool]
public partial class RotationSmoothingModifier : InputModifier
{
    /// <summary>How fast the output catches up, per second (higher: snappier).</summary>
    [Export(PropertyHint.Range, "0.1,100,0.1,or_greater")] public float Rate { get; set; } = 20f;

    /// <inheritdoc/>
    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        var memory = context.Memory;
        if (value == Vector4.Zero)
        {
            memory.HasPrevious = false;
            return value;
        }
        var target = ActionValue.ToQuaternion(value);
        if (!memory.HasPrevious)
        {
            memory.HasPrevious = true;
            memory.Previous4 = ActionValue.FromQuaternion(target);
            return memory.Previous4;
        }
        float weight = 1f - Mathf.Exp(-Rate * (float)context.Delta);
        var current = ActionValue.ToQuaternion(memory.Previous4).Slerp(target, Mathf.Clamp(weight, 0f, 1f));
        memory.Previous4 = ActionValue.FromQuaternion(current.Normalized());
        return memory.Previous4;
    }
}
