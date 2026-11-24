using System;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The value of a game action: its kind and a <see cref="Vector4"/> carrying up to four components.
/// Components the kind does not have are always zero. Rotations: a <see cref="ActionValueKind.Quaternion"/>
/// value holds (x, y, z, w) and reads as the identity when zero; an <see cref="ActionValueKind.Euler"/>
/// value holds angles in radians.
/// </summary>
public readonly struct ActionValue : IEquatable<ActionValue>
{
    /// <summary>The value's shape.</summary>
    public ActionValueKind Kind { get; }

    /// <summary>The components as stored, unconverted. For <see cref="ActionValueKind.Digital"/> the strength is in X.</summary>
    public Vector4 Raw { get; }

    /// <summary>A value of a kind from four components, stored as given.</summary>
    public ActionValue(ActionValueKind kind, Vector4 raw)
    {
        Kind = kind;
        Raw = raw;
    }

    /// <summary>A value of a kind from three components.</summary>
    public ActionValue(ActionValueKind kind, Vector3 raw) : this(kind, new Vector4(raw.X, raw.Y, raw.Z, 0f)) { }

    /// <summary>A quaternion value.</summary>
    public ActionValue(Quaternion rotation) : this(ActionValueKind.Quaternion, new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W)) { }

    /// <summary>A zero value of the given kind (for a quaternion: no rotation).</summary>
    public static ActionValue Zero(ActionValueKind kind) => new(kind, Vector4.Zero);

    /// <summary>
    /// Builds a value of the given kind from any vector: keeps only the components the kind has.
    /// A digital value takes the vector's length as its strength.
    /// </summary>
    public static ActionValue From(ActionValueKind kind, Vector4 raw) => new(kind, Shape(kind, raw));

    /// <inheritdoc cref="From(ActionValueKind, Vector4)"/>
    public static ActionValue From(ActionValueKind kind, Vector3 raw) => From(kind, new Vector4(raw.X, raw.Y, raw.Z, 0f));

    /// <summary>Keeps only the components <paramref name="kind"/> has (see <see cref="From(ActionValueKind, Vector4)"/>).</summary>
    public static Vector4 Shape(ActionValueKind kind, Vector4 raw) => kind switch
    {
        ActionValueKind.Digital => new Vector4(raw.Length(), 0f, 0f, 0f),
        ActionValueKind.Axis1D => new Vector4(raw.X, 0f, 0f, 0f),
        ActionValueKind.Axis2D => new Vector4(raw.X, raw.Y, 0f, 0f),
        ActionValueKind.Axis3D or ActionValueKind.Euler => new Vector4(raw.X, raw.Y, raw.Z, 0f),
        _ => raw,
    };

    /// <summary>Keeps only the components <paramref name="kind"/> has, for a three-component value.</summary>
    public static Vector3 Shape(ActionValueKind kind, Vector3 raw)
    {
        var shaped = Shape(kind, new Vector4(raw.X, raw.Y, raw.Z, 0f));
        return new Vector3(shaped.X, shaped.Y, shaped.Z);
    }

    /// <summary>
    /// How strongly a value of this kind is actuated: the vector length, or — for a quaternion — the
    /// rotation angle away from the identity as a fraction of half a turn (0…1).
    /// </summary>
    public static float MagnitudeOf(ActionValueKind kind, Vector4 raw)
    {
        if (kind != ActionValueKind.Quaternion) return raw.Length();
        if (raw == Vector4.Zero) return 0f;
        float length = raw.Length();
        float w = Mathf.Clamp(Mathf.Abs(raw.W) / length, 0f, 1f);
        return 2f * Mathf.Acos(w) / Mathf.Pi;
    }

    /// <summary>True when a value of this kind means "no input" (zero; for a quaternion also the identity).</summary>
    public static bool IsZeroOf(ActionValueKind kind, Vector4 raw)
        => raw == Vector4.Zero || (kind == ActionValueKind.Quaternion && raw.X == 0f && raw.Y == 0f && raw.Z == 0f);

    /// <summary>True when the value means input (any component non-zero; a quaternion other than the identity).</summary>
    public bool IsNonZero => !IsZeroOf(Kind, Raw);

    /// <summary>The length of the vector (digital: its strength; quaternion: its angle, see <see cref="MagnitudeOf"/>).</summary>
    public float Magnitude => MagnitudeOf(Kind, Raw);

    /// <summary>On/off reading: true when the value is non-zero.</summary>
    public bool AsBool() => IsNonZero;

    /// <summary>The first axis.</summary>
    public float AsFloat() => Raw.X;

    /// <summary>The first two axes.</summary>
    public Vector2 AsVector2() => new(Raw.X, Raw.Y);

    /// <summary>The first three axes.</summary>
    public Vector3 AsVector3() => new(Raw.X, Raw.Y, Raw.Z);

    /// <summary>All four components, unconverted.</summary>
    public Vector4 AsVector4() => Raw;

    /// <summary>
    /// The value as a rotation: a quaternion value normalized (identity when zero); an Euler or 3D value
    /// read as Euler angles in radians.
    /// </summary>
    public Quaternion AsQuaternion()
    {
        if (Kind == ActionValueKind.Quaternion) return ToQuaternion(Raw);
        return Quaternion.FromEuler(AsVector3());
    }

    /// <summary>The value as Euler angles in radians (a quaternion value is converted, YXZ order).</summary>
    public Vector3 AsEuler() => Kind == ActionValueKind.Quaternion ? ToQuaternion(Raw).GetEuler() : AsVector3();

    /// <summary>The value as a rotation basis.</summary>
    public Basis AsBasis() => new(AsQuaternion());

    /// <summary>A stored (x, y, z, w) quaternion, normalized; zero reads as the identity.</summary>
    public static Quaternion ToQuaternion(Vector4 raw)
    {
        if (raw == Vector4.Zero) return Quaternion.Identity;
        var q = new Quaternion(raw.X, raw.Y, raw.Z, raw.W);
        float length = q.Length();
        return length < 1e-6f ? Quaternion.Identity : q / length;
    }

    /// <summary>
    /// A stored rotation with its angle multiplied by <paramref name="factor"/> (same axis). Zero and the
    /// identity stay as they are.
    /// </summary>
    public static Vector4 ScaleRotation(Vector4 raw, float factor)
    {
        if (raw == Vector4.Zero) return raw;
        var q = ToQuaternion(raw);
        float angle = q.GetAngle();
        if (angle < 1e-6f) return FromQuaternion(Quaternion.Identity);
        if (angle > Mathf.Pi) { q = -q; angle = q.GetAngle(); }   // take the short way round
        return FromQuaternion(new Quaternion(q.GetAxis().Normalized(), angle * factor));
    }

    /// <summary>A quaternion stored as (x, y, z, w).</summary>
    public static Vector4 FromQuaternion(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary><see cref="AsBool"/>.</summary>
    public static implicit operator bool(ActionValue v) => v.AsBool();
    /// <summary><see cref="AsFloat"/>.</summary>
    public static implicit operator float(ActionValue v) => v.AsFloat();
    /// <summary><see cref="AsVector2"/>.</summary>
    public static implicit operator Vector2(ActionValue v) => v.AsVector2();
    /// <summary><see cref="AsVector3"/>.</summary>
    public static implicit operator Vector3(ActionValue v) => v.AsVector3();
    /// <summary><see cref="AsVector4"/>.</summary>
    public static implicit operator Vector4(ActionValue v) => v.AsVector4();
    /// <summary><see cref="AsQuaternion"/>.</summary>
    public static implicit operator Quaternion(ActionValue v) => v.AsQuaternion();

    /// <summary>Same kind and same components.</summary>
    public bool Equals(ActionValue other) => Kind == other.Kind && Raw == other.Raw;
    /// <inheritdoc/>
    public override bool Equals(object obj) => obj is ActionValue other && Equals(other);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine((int)Kind, Raw);
    /// <summary>Same kind and same components.</summary>
    public static bool operator ==(ActionValue a, ActionValue b) => a.Equals(b);
    /// <summary>Different kind or components.</summary>
    public static bool operator !=(ActionValue a, ActionValue b) => !a.Equals(b);

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        ActionValueKind.Digital => $"{AsBool()} ({Raw.X:0.###})",
        ActionValueKind.Axis1D => $"{Raw.X:0.###}",
        ActionValueKind.Axis2D => $"({Raw.X:0.###}, {Raw.Y:0.###})",
        ActionValueKind.Axis3D => $"({Raw.X:0.###}, {Raw.Y:0.###}, {Raw.Z:0.###})",
        ActionValueKind.Euler => $"euler ({Mathf.RadToDeg(Raw.X):0.#}°, {Mathf.RadToDeg(Raw.Y):0.#}°, {Mathf.RadToDeg(Raw.Z):0.#}°)",
        ActionValueKind.Quaternion => $"quat ({AsQuaternion().X:0.###}, {AsQuaternion().Y:0.###}, {AsQuaternion().Z:0.###}, {AsQuaternion().W:0.###})",
        _ => $"({Raw.X:0.###}, {Raw.Y:0.###}, {Raw.Z:0.###}, {Raw.W:0.###})",
    };
}
