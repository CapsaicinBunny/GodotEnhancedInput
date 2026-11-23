using Godot;

namespace GodotEnhancedInput;

/// <summary>Flips the sign of the chosen components.</summary>
[Tool]
[GlobalClass]
public partial class NegateModifier : InputModifier
{
    [Export] public bool X { get; set; } = true;
    [Export] public bool Y { get; set; } = true;
    [Export] public bool Z { get; set; } = true;

    public override Vector3 Apply(Vector3 value, in ModifierContext context)
        => new(X ? -value.X : value.X, Y ? -value.Y : value.Y, Z ? -value.Z : value.Z);
}
