using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Rotates a world direction by the player's view (its camera, or <see cref="InputPlayer.ViewBasisOverride"/>),
/// so "forward" means "away from the camera". With <see cref="YawOnly"/>, only the view's heading is
/// used and the result stays on the ground plane. Usually placed after <see cref="WorldAxesModifier"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class ViewRelativeModifier : InputModifier
{
    /// <summary>Use only the camera's heading (movement stays on the ground).</summary>
    [Export] public bool YawOnly { get; set; } = true;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var basis = context.Player?.GetViewBasis() ?? Basis.Identity;
        if (YawOnly)
        {
            // Heading of the view's forward (−Z) on the ground plane.
            var forward = -basis.Z;
            if (Mathf.IsZeroApprox(forward.X) && Mathf.IsZeroApprox(forward.Z)) forward = basis.Y; // looking straight down/up
            float yaw = Mathf.Atan2(-forward.X, -forward.Z);
            basis = new Basis(Vector3.Up, yaw);
        }
        return basis * value;
    }
}
