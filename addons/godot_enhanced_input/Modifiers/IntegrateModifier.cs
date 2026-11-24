using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Relative to absolute: adds the value up over time into a position — mouse motion into a cursor or aim
/// offset, a stick into a throttle that stays where it was pushed. With <see cref="PerSecond"/>, state
/// values count as rates (×delta time); delta values are always added as they are. The total is clamped
/// to <see cref="Min"/>…<see cref="Max"/> and can drift back to zero at <see cref="ReturnRate"/> per second
/// while there is no input.
/// </summary>
[Tool]
[GlobalClass]
public partial class IntegrateModifier : InputModifier
{
    /// <summary>Lowest accumulated value per component.</summary>
    [Export] public Vector3 Min { get; set; } = new(-1f, -1f, -1f);
    /// <summary>Highest accumulated value per component.</summary>
    [Export] public Vector3 Max { get; set; } = new(1f, 1f, 1f);
    /// <summary>Treat the input as a rate per second (multiply by delta).</summary>
    [Export] public bool PerSecond { get; set; } = true;
    /// <summary>How fast the value drifts back to zero, per second (0: it stays).</summary>
    [Export(PropertyHint.Range, "0,10,0.01,or_greater")] public float ReturnRate { get; set; }

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var m = context.Memory;
        var step = context.Nature != ValueNature.Delta && PerSecond ? value * (float)context.Delta : value;
        var total = m.Previous + step;
        if (ReturnRate > 0f && step == Vector3.Zero) total = total.MoveToward(Vector3.Zero, ReturnRate * (float)context.Delta);
        total = total.Clamp(Min, Max);
        m.Previous = total;
        m.HasPrevious = true;
        return total;
    }
}
