using System;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The value of a game action: its kind and a <see cref="Vector3"/> carrying up to three axes.
/// Components the kind does not have are always zero.
/// </summary>
public readonly struct ActionValue : IEquatable<ActionValue>
{
    /// <summary>The value's shape.</summary>
    public ActionValueKind Kind { get; }

    /// <summary>The components. For <see cref="ActionValueKind.Digital"/> the strength is in X.</summary>
    public Vector3 Raw { get; }

    public ActionValue(ActionValueKind kind, Vector3 raw)
    {
        Kind = kind;
        Raw = raw;
    }

    /// <summary>A zero value of the given kind.</summary>
    public static ActionValue Zero(ActionValueKind kind) => new(kind, Vector3.Zero);

    /// <summary>
    /// Builds a value of the given kind from any vector: keeps only the components the kind has.
    /// A digital value takes the vector's length as its strength.
    /// </summary>
    public static ActionValue From(ActionValueKind kind, Vector3 raw) => new(kind, Shape(kind, raw));

    /// <summary>Keeps only the components <paramref name="kind"/> has (see <see cref="From"/>).</summary>
    public static Vector3 Shape(ActionValueKind kind, Vector3 raw) => kind switch
    {
        ActionValueKind.Digital => new Vector3(raw.Length(), 0f, 0f),
        ActionValueKind.Axis1D => new Vector3(raw.X, 0f, 0f),
        ActionValueKind.Axis2D => new Vector3(raw.X, raw.Y, 0f),
        _ => raw,
    };

    /// <summary>True when any component is non-zero.</summary>
    public bool IsNonZero => Raw != Vector3.Zero;

    /// <summary>The length of the vector (for a digital value: its strength).</summary>
    public float Magnitude => Raw.Length();

    /// <summary>On/off reading: true when the value is non-zero.</summary>
    public bool AsBool() => IsNonZero;

    /// <summary>The first axis.</summary>
    public float AsFloat() => Raw.X;

    /// <summary>The first two axes.</summary>
    public Vector2 AsVector2() => new(Raw.X, Raw.Y);

    /// <summary>All three axes.</summary>
    public Vector3 AsVector3() => Raw;

    public static implicit operator bool(ActionValue v) => v.AsBool();
    public static implicit operator float(ActionValue v) => v.AsFloat();
    public static implicit operator Vector2(ActionValue v) => v.AsVector2();
    public static implicit operator Vector3(ActionValue v) => v.AsVector3();

    public bool Equals(ActionValue other) => Kind == other.Kind && Raw == other.Raw;
    public override bool Equals(object obj) => obj is ActionValue other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Kind, Raw);
    public static bool operator ==(ActionValue a, ActionValue b) => a.Equals(b);
    public static bool operator !=(ActionValue a, ActionValue b) => !a.Equals(b);

    public override string ToString() => Kind switch
    {
        ActionValueKind.Digital => $"{AsBool()} ({Raw.X:0.###})",
        ActionValueKind.Axis1D => $"{Raw.X:0.###}",
        ActionValueKind.Axis2D => $"({Raw.X:0.###}, {Raw.Y:0.###})",
        _ => $"({Raw.X:0.###}, {Raw.Y:0.###}, {Raw.Z:0.###})",
    };
}
