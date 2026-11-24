using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Removes gyroscope drift: while the reading stays close to the learned resting bias (the device is
/// still), the bias slowly follows it; the output is the reading minus the bias. Put it first on a
/// gyroscope binding. The first tick only learns (outputs zero).
/// </summary>
[Tool]
public partial class GyroCalibrationModifier : InputModifier
{
    /// <summary>Readings within this distance of the bias (per component) count as still.</summary>
    [Export(PropertyHint.Range, "0,1,0.001")] public float StillThreshold { get; set; } = 0.05f;

    /// <summary>How fast the bias follows the resting reading, per second.</summary>
    [Export(PropertyHint.Range, "0.01,10,0.01")] public float LearnRate { get; set; } = 1f;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var m = context.Memory;
        if (!m.HasPrevious)
        {
            m.Previous = value;
            m.HasPrevious = true;
            return Vector3.Zero;
        }
        var offset = value - m.Previous;
        bool still = Mathf.Abs(offset.X) <= StillThreshold && Mathf.Abs(offset.Y) <= StillThreshold && Mathf.Abs(offset.Z) <= StillThreshold;
        if (still)
        {
            float t = 1f - Mathf.Exp(-LearnRate * (float)context.Delta);
            m.Previous = m.Previous.Lerp(value, t);
            offset = value - m.Previous;
        }
        return offset;
    }
}
