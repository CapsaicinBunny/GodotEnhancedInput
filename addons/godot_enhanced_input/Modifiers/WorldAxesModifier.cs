using Godot;

namespace GodotEnhancedInput;

/// <summary>Where <see cref="WorldAxesModifier"/> puts a 2D input.</summary>
public enum WorldPlane
{
    /// <summary>The ground plane of a Y-up world: X stays X, Y becomes Z. Up on a stick (−Y) becomes −Z, Godot's forward.</summary>
    GroundXZ,
    /// <summary>The upright XY plane with Y flipped: up on a stick becomes +Y.</summary>
    UprightXY,
}

/// <summary>
/// Turns a 2D input (in Godot's screen convention, up = −Y) into a 3D world direction. Use it on an
/// <see cref="ActionValueKind.Axis3D"/> action driven by sticks or key composites.
/// </summary>
[Tool]
[GlobalClass]
public partial class WorldAxesModifier : InputModifier
{
    [Export] public WorldPlane Plane { get; set; } = WorldPlane.GroundXZ;

    public override Vector3 Apply(Vector3 v, in ModifierContext context) => Plane switch
    {
        WorldPlane.UprightXY => new Vector3(v.X, -v.Y, 0f),
        _ => new Vector3(v.X, 0f, v.Y),
    };
}
