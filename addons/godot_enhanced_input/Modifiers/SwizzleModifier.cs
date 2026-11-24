using Godot;

namespace GodotEnhancedInput;

/// <summary>The component order produced by <see cref="SwizzleModifier"/>, read as "new X, new Y, new Z".</summary>
public enum SwizzleOrder
{
    /// <summary>Unchanged.</summary>
    XYZ,
    /// <summary>Swaps X and Y — e.g. to put a key (which feeds X) on the Y axis.</summary>
    YXZ,
    /// <summary>Swaps Y and Z.</summary>
    XZY,
    /// <summary>Swaps X and Z.</summary>
    ZYX,
    /// <summary>New X is old Y, new Y is old Z, new Z is old X.</summary>
    YZX,
    /// <summary>New X is old Z, new Y is old X, new Z is old Y.</summary>
    ZXY,
}

/// <summary>Reorders the components of the value.</summary>
[Tool]
[GlobalClass]
public partial class SwizzleModifier : InputModifier
{
    /// <summary>The new order of the components.</summary>
    [Export] public SwizzleOrder Order { get; set; } = SwizzleOrder.YXZ;

    /// <inheritdoc/>
    public override Vector3 Apply(Vector3 v, in ModifierContext context) => Order switch
    {
        SwizzleOrder.YXZ => new Vector3(v.Y, v.X, v.Z),
        SwizzleOrder.XZY => new Vector3(v.X, v.Z, v.Y),
        SwizzleOrder.ZYX => new Vector3(v.Z, v.Y, v.X),
        SwizzleOrder.YZX => new Vector3(v.Y, v.Z, v.X),
        SwizzleOrder.ZXY => new Vector3(v.Z, v.X, v.Y),
        _ => v,
    };
}
