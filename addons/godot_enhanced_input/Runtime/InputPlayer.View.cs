using Godot;

namespace GodotEnhancedInput;

/// <summary>The view the player's camera-relative and pointer modifiers use.</summary>
public partial class InputPlayer
{
    /// <summary>The camera used for view-dependent modifiers, if any.</summary>
    public Camera3D GetViewCamera()
    {
        if (ViewCamera != null && IsInstanceValid(ViewCamera)) return ViewCamera;
        return IsInsideTree() ? GetViewport()?.GetCamera3D() : null;
    }

    /// <summary>The view basis (override, camera, or identity).</summary>
    public Basis GetViewBasis()
    {
        if (ViewBasisOverride.HasValue) return ViewBasisOverride.Value;
        var camera = GetViewCamera();
        if (camera == null) return Basis.Identity;
        return camera.IsInsideTree() ? camera.GlobalBasis : camera.Basis;
    }

    /// <summary>The vertical field of view in degrees (override, camera, or null).</summary>
    public float? GetViewFov()
    {
        if (ViewFovOverride.HasValue) return ViewFovOverride.Value;
        return GetViewCamera()?.Fov;
    }

    private Viewport GetPointerViewport()
    {
        if (PointerViewport != null && IsInstanceValid(PointerViewport)) return PointerViewport;
        if (PointerRegion is SubViewportContainer container && IsInstanceValid(container))
            foreach (var child in container.GetChildren())
                if (child is SubViewport sub) return sub;
        return IsInsideTree() ? GetViewport() : null;
    }

    /// <summary>The pointer viewport's visible size in pixels (zero when unknown).</summary>
    public Vector2 GetViewportSize()
        => ViewportSizeOverride ?? GetPointerViewport()?.GetVisibleRect().Size ?? Vector2.Zero;

    /// <summary>The pointer viewport's canvas transform (camera and canvas offset), or identity.</summary>
    public Transform2D GetCanvasTransform()
        => CanvasTransformOverride ?? GetPointerViewport()?.CanvasTransform ?? Transform2D.Identity;
}
