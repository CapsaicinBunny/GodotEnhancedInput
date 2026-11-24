using Godot;

namespace GodotEnhancedInput;

/// <summary>What <see cref="RotationConvertModifier"/> turns into what.</summary>
public enum RotationConversion
{
    /// <summary>Euler angles in radians (X pitch, Y yaw, Z roll) → quaternion (x, y, z, w).</summary>
    EulerToQuaternion,
    /// <summary>Quaternion (x, y, z, w) → Euler angles in radians.</summary>
    QuaternionToEuler,
    /// <summary>Euler angles in degrees → radians.</summary>
    DegreesToRadians,
    /// <summary>A rotation vector (axis × angle in radians, as gyroscopes and angular velocities give) → quaternion.</summary>
    RotationVectorToQuaternion,
    /// <summary>Quaternion → rotation vector (axis × angle in radians).</summary>
    QuaternionToRotationVector,
}

/// <summary>
/// Converts between rotation forms, so any rotation source can drive any rotation action: an XR
/// controller's quaternion into an Euler action, integrated gyroscope angles into a quaternion action,
/// degrees from a custom device into radians. Uses Godot's YXZ Euler order.
/// </summary>
[Tool]
[GlobalClass]
public partial class RotationConvertModifier : InputModifier
{
    [Export] public RotationConversion Conversion { get; set; } = RotationConversion.EulerToQuaternion;

    public override Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        var xyz = new Vector3(value.X, value.Y, value.Z);
        switch (Conversion)
        {
            case RotationConversion.EulerToQuaternion:
                return xyz == Vector3.Zero ? Vector4.Zero : ActionValue.FromQuaternion(Quaternion.FromEuler(xyz));
            case RotationConversion.QuaternionToEuler:
            {
                if (value == Vector4.Zero) return Vector4.Zero;
                var euler = ActionValue.ToQuaternion(value).GetEuler();
                return new Vector4(euler.X, euler.Y, euler.Z, 0f);
            }
            case RotationConversion.DegreesToRadians:
                return new Vector4(Mathf.DegToRad(value.X), Mathf.DegToRad(value.Y), Mathf.DegToRad(value.Z), value.W);
            case RotationConversion.RotationVectorToQuaternion:
            {
                float angle = xyz.Length();
                return angle < 1e-6f ? Vector4.Zero : ActionValue.FromQuaternion(new Quaternion(xyz / angle, angle));
            }
            default:
            {
                if (ActionValue.IsZeroOf(ActionValueKind.Quaternion, value)) return Vector4.Zero;
                var q = ActionValue.ToQuaternion(value);
                if (q.W < 0f) q = -q;
                float angle = q.GetAngle();
                var v = angle < 1e-6f ? Vector3.Zero : q.GetAxis().Normalized() * angle;
                return new Vector4(v.X, v.Y, v.Z, 0f);
            }
        }
    }
}
