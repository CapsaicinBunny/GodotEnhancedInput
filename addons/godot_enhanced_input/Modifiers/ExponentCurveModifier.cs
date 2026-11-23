using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Bends each component with a power curve, keeping its sign: <c>sign(v)·|v|^exponent</c>.
/// Exponents above 1 give finer control near the centre; below 1 make small inputs stronger.
/// </summary>
[Tool]
[GlobalClass]
public partial class ExponentCurveModifier : InputModifier
{
    [Export] public Vector3 Exponent { get; set; } = new(2f, 2f, 2f);

    public override Vector3 Apply(Vector3 v, in ModifierContext context)
        => new(Bend(v.X, Exponent.X), Bend(v.Y, Exponent.Y), Bend(v.Z, Exponent.Z));

    private static float Bend(float v, float exponent) => v == 0f ? 0f : Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), exponent);
}
