using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Absolute to relative: outputs how much the value changed since the previous tick — a pointer or
/// tracked position into movement, a wheel angle into turning speed. With <see cref="PerSecond"/> the
/// change is divided by delta time. The first tick outputs zero.
/// </summary>
[Tool]
[GlobalClass]
public partial class DifferentiateModifier : InputModifier
{
    /// <summary>Divide the change by the tick's delta (a rate per second).</summary>
    [Export] public bool PerSecond { get; set; }

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var m = context.Memory;
        var change = m.HasPrevious ? value - m.Previous : Vector3.Zero;
        m.Previous = value;
        m.HasPrevious = true;
        return PerSecond && context.Delta > 0 ? change / (float)context.Delta : change;
    }
}
