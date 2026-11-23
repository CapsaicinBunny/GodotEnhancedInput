using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Remaps each component through a Godot <see cref="Curve"/>. By default the curve is sampled at the
/// component's magnitude and the result takes the component's sign, so one curve covers both
/// directions. A missing curve leaves its component unchanged.
/// </summary>
[Tool]
[GlobalClass]
public partial class CurveModifier : InputModifier
{
    [Export] public Curve CurveX { get; set; }
    [Export] public Curve CurveY { get; set; }
    [Export] public Curve CurveZ { get; set; }

    /// <summary>When off, the curve is sampled at the signed value and its result used as is.</summary>
    [Export] public bool Symmetric { get; set; } = true;

    public override Vector3 Apply(Vector3 v, in ModifierContext context)
        => new(Map(CurveX, v.X), Map(CurveY, v.Y), Map(CurveZ, v.Z));

    private float Map(Curve curve, float v)
    {
        if (curve == null) return v;
        if (!Symmetric) return curve.Sample(v);
        if (v == 0f) return 0f;
        return Mathf.Sign(v) * curve.Sample(Mathf.Abs(v));
    }
}
