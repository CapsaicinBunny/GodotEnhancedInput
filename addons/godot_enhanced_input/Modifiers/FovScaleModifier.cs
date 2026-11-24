using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Scales by the view camera's field of view relative to <see cref="ReferenceFov"/>, so aiming feels
/// the same when zoomed in. The factor is <c>tan(fov/2) / tan(reference/2)</c>. Without a camera or
/// <see cref="InputPlayer.ViewFovOverride"/> the value is unchanged.
/// </summary>
[Tool]
public partial class FovScaleModifier : InputModifier
{
    /// <summary>The vertical field of view, in degrees, at which the scale is 1.</summary>
    [Export(PropertyHint.Range, "1,179,0.1")] public float ReferenceFov { get; set; } = 75f;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var fov = context.Player?.GetViewFov();
        if (fov is not float current || current <= 0f || ReferenceFov <= 0f) return value;
        float scale = Mathf.Tan(Mathf.DegToRad(current) * 0.5f) / Mathf.Tan(Mathf.DegToRad(ReferenceFov) * 0.5f);
        return value * scale;
    }
}
