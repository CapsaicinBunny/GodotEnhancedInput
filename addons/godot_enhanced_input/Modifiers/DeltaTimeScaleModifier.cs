using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Multiplies the value by the tick's delta time, turning a rate (a stick held at 0.5) into a per-tick
/// amount. Values that already are per-tick amounts (mouse motion, touch drags) are left alone unless
/// <see cref="ScaleDeltaValues"/> is on.
/// </summary>
[Tool]
[GlobalClass]
public partial class DeltaTimeScaleModifier : InputModifier
{
    /// <summary>Also scale delta values (mouse motion, drags), which are normally already per tick.</summary>
    [Export] public bool ScaleDeltaValues { get; set; }

    public override Vector3 Apply(Vector3 value, in ModifierContext context)
        => context.Nature == ValueNature.Delta && !ScaleDeltaValues ? value : value * (float)context.Delta;
}
