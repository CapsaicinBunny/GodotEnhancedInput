using Godot;

namespace GodotEnhancedInput;

/// <summary>How <see cref="QuantizeModifier"/> rounds.</summary>
public enum QuantizeMode
{
    /// <summary>Each component to a multiple of <see cref="QuantizeModifier.Step"/>.</summary>
    Steps,
    /// <summary>A 2D direction to the nearest of <see cref="QuantizeModifier.Directions"/> directions, keeping its length.</summary>
    Directions,
}

/// <summary>Rounds values: to steps (a throttle in 10 % notches) or a stick to 4 or 8 directions.</summary>
[Tool]
[GlobalClass]
public partial class QuantizeModifier : InputModifier
{
    /// <summary>Round to steps, or snap a direction.</summary>
    [Export] public QuantizeMode Mode { get; set; } = QuantizeMode.Steps;
    /// <summary>Step size (steps mode).</summary>
    [Export(PropertyHint.Range, "0.001,1,0.001,or_greater")] public float Step { get; set; } = 0.25f;
    /// <summary>Number of directions (directions mode: 4, 8…).</summary>
    [Export(PropertyHint.Range, "2,64,1")] public int Directions { get; set; } = 8;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 v, in ModifierContext context)
    {
        if (Mode == QuantizeMode.Steps)
            return Step <= 0f ? v : new Vector3(Mathf.Snapped(v.X, Step), Mathf.Snapped(v.Y, Step), Mathf.Snapped(v.Z, Step));

        var flat = new Vector2(v.X, v.Y);
        float length = flat.Length();
        if (length == 0f || Directions < 2) return v;
        float slice = Mathf.Tau / Directions;
        var snapped = Vector2.FromAngle(Mathf.Snapped(flat.Angle(), slice)) * length;
        if (Mathf.IsZeroApprox(snapped.X)) snapped.X = 0f;
        if (Mathf.IsZeroApprox(snapped.Y)) snapped.Y = 0f;
        return new Vector3(snapped.X, snapped.Y, v.Z);
    }
}
