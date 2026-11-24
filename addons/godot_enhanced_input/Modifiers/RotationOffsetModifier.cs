using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Turns a quaternion by a fixed offset — for a controller's grip angle, a sensor mounted sideways, or a
/// player's "recenter" (store the inverse of the rest pose as the offset, e.g. from a player setting).
/// The offset is given as Euler angles in degrees, or read from three player settings when
/// <see cref="SettingPrefix"/> is set ("<prefix>_x", "_y", "_z", in degrees).
/// </summary>
[Tool]
[GlobalClass]
public partial class RotationOffsetModifier : InputModifier
{
    /// <summary>The offset as Euler angles in degrees (YXZ order).</summary>
    [Export] public Vector3 OffsetDegrees { get; set; } = Vector3.Zero;

    /// <summary>Apply the offset in the rotation's own frame (after it) instead of before it.</summary>
    [Export] public bool Local { get; set; } = true;

    /// <summary>Optional player settings prefix for the offset (replaces <see cref="OffsetDegrees"/> when set).</summary>
    [Export] public string SettingPrefix { get; set; } = string.Empty;

    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        var degrees = OffsetDegrees;
        if (!string.IsNullOrEmpty(SettingPrefix) && context.Player != null)
            degrees = new Vector3(
                (float)context.Player.GetSetting(SettingPrefix + "_x", degrees.X),
                (float)context.Player.GetSetting(SettingPrefix + "_y", degrees.Y),
                (float)context.Player.GetSetting(SettingPrefix + "_z", degrees.Z));
        if (degrees == Vector3.Zero) return value;
        var offset = Quaternion.FromEuler(new Vector3(Mathf.DegToRad(degrees.X), Mathf.DegToRad(degrees.Y), Mathf.DegToRad(degrees.Z)));
        var q = ActionValue.ToQuaternion(value);
        return ActionValue.FromQuaternion((Local ? q * offset : offset * q).Normalized());
    }
}
