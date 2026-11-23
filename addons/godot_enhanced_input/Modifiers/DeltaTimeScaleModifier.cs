using Godot;

namespace GodotEnhancedInput;

/// <summary>Multiplies the value by the tick's delta time, turning a rate into a per-tick amount.</summary>
[Tool]
[GlobalClass]
public partial class DeltaTimeScaleModifier : InputModifier
{
    public override Vector3 Apply(Vector3 value, in ModifierContext context) => value * (float)context.Delta;
}
