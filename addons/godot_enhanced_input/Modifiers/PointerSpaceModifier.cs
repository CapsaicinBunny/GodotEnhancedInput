using Godot;

namespace GodotEnhancedInput;

/// <summary>Where <see cref="PointerSpaceModifier"/> expresses a pointer position.</summary>
public enum PointerSpace
{
    /// <summary>0…1 across the viewport, (0, 0) top-left.</summary>
    ViewportNormalized,
    /// <summary>−1…1 across the viewport, (0, 0) at the centre, +Y down (screen convention).</summary>
    ViewportCentered,
    /// <summary>0…1 across <see cref="PointerSpaceModifier.Rect"/> (viewport pixels); outside the rect goes below 0 or above 1.</summary>
    RectNormalized,
    /// <summary>2D world coordinates under the pointer (through the viewport's canvas transform, i.e. its Camera2D).</summary>
    World2D,
}

/// <summary>
/// Converts a pointer position in viewport pixels (from <see cref="SourceKind.MousePosition"/> or a
/// touch position) into another space: normalized or centred viewport coordinates, a rect (a panel,
/// one half of a split screen), or 2D world coordinates. The viewport is the player's
/// <see cref="InputPlayer.PointerViewport"/> (by default its own).
/// </summary>
[Tool]
[GlobalClass]
public partial class PointerSpaceModifier : InputModifier
{
    private PointerSpace _space = PointerSpace.ViewportNormalized;

    [Export]
    public PointerSpace Space
    {
        get => _space;
        set { _space = value; NotifyPropertyListChanged(); }
    }

    /// <summary>The rect for <see cref="PointerSpace.RectNormalized"/>, in viewport pixels.</summary>
    [Export] public Rect2 Rect { get; set; } = new(0, 0, 640, 360);

    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var position = new Vector2(value.X, value.Y);
        var player = context.Player;
        Vector2 result;
        switch (_space)
        {
            case PointerSpace.World2D:
                result = (player?.GetCanvasTransform() ?? Transform2D.Identity).AffineInverse() * position;
                break;
            case PointerSpace.RectNormalized:
                result = Normalize(position - Rect.Position, Rect.Size);
                break;
            case PointerSpace.ViewportCentered:
                result = Normalize(position, player?.GetViewportSize() ?? Vector2.Zero) * 2f - Vector2.One;
                break;
            default:
                result = Normalize(position, player?.GetViewportSize() ?? Vector2.Zero);
                break;
        }
        return new Vector3(result.X, result.Y, value.Z);
    }

    private static Vector2 Normalize(Vector2 position, Vector2 size)
        => new(size.X != 0f ? position.X / size.X : 0f, size.Y != 0f ? position.Y / size.Y : 0f);

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsString() == nameof(Rect) && _space != PointerSpace.RectNormalized)
            property["usage"] = (int)PropertyUsageFlags.NoEditor;
    }
}
