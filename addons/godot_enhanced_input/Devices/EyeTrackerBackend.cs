using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// One desktop eye tracker (Tobii and similar), as seen by <see cref="EyeTrackerBackend"/>. Godot has
/// no eye-tracker API of its own, so a small adapter around the vendor's SDK implements this; the
/// plugin does the rest (bindings, gaze dwell, recording, prompts).
/// </summary>
public interface IEyeTracker
{
    /// <summary>A name for menus ("Tobii Eye Tracker 5").</summary>
    string Name { get; }

    /// <summary>Whether the tracker is connected and running.</summary>
    bool IsAvailable { get; }

    /// <summary>Where the user looks, in 0…1 screen coordinates (top-left origin). False when unknown (blinking, looking away).</summary>
    bool TryGetGaze(out Vector2 screen);

    /// <summary>Whether someone sits in front of the screen.</summary>
    bool IsUserPresent { get; }

    /// <summary>The head pose relative to the screen, in metres and as a rotation, when the tracker reports it.</summary>
    bool TryGetHead(out Vector3 position, out Quaternion rotation);
}

/// <summary>
/// Turns an <see cref="IEyeTracker"/> into a device of type "eye" with these controls:
/// "gaze" (0…1 screen position), "gaze_pixels" (viewport pixels), "gaze_centered" (−1…1 from the
/// screen centre), "gaze_valid" (1 while the gaze is known), "present" (1 while a user is present),
/// "head/position", "head/orientation" (quaternion) and "head/rotation" (Euler). Bind with
/// <c>@eye:gaze</c> or <c>InputSource.ForDeviceControl("eye", "gaze_centered")</c>; pair with a
/// <see cref="DwellTrigger"/> for look-to-select. With no tracker connected the device disconnects,
/// so input modes and prompts fall back to other devices.
/// </summary>
public sealed class EyeTrackerBackend : InputBackend
{
    public const string DeviceType = "eye";

    private readonly IEyeTracker _tracker;
    private readonly int _index;
    private bool _added;

    public EyeTrackerBackend(IEyeTracker tracker, int index = 0)
    {
        _tracker = tracker;
        _index = index;
    }

    public override string Name => "Eye tracker";

    /// <summary>The viewport whose size "gaze_pixels" uses (the main window's when null).</summary>
    public Viewport Viewport { get; set; }

    public override void Poll(double delta)
    {
        bool available = _tracker != null && _tracker.IsAvailable;
        if (available && !_added) { AddDevice(DeviceType, _index, _tracker.Name, "eye:" + _tracker.Name); _added = true; }
        else if (!available && _added) { RemoveDevice(DeviceType, _index); _added = false; }
        if (!available) return;

        bool valid = _tracker.TryGetGaze(out var gaze);
        SetControl(DeviceType, _index, "gaze_valid", valid ? 1f : 0f);
        SetControl(DeviceType, _index, "present", _tracker.IsUserPresent ? 1f : 0f);
        if (valid)
        {
            gaze = gaze.Clamp(Vector2.Zero, Vector2.One);
            SetControl(DeviceType, _index, "gaze", new Vector3(gaze.X, gaze.Y, 0f));
            var centered = gaze * 2f - Vector2.One;
            SetControl(DeviceType, _index, "gaze_centered", new Vector3(centered.X, centered.Y, 0f));
            var viewport = Viewport ?? (Engine.GetMainLoop() as SceneTree)?.Root;
            if (viewport != null)
            {
                var size = viewport.GetVisibleRect().Size;
                SetControl(DeviceType, _index, "gaze_pixels", new Vector3(gaze.X * size.X, gaze.Y * size.Y, 0f));
            }
        }
        if (_tracker.TryGetHead(out var position, out var rotation))
        {
            SetControl(DeviceType, _index, "head/position", position);
            SetControl(DeviceType, _index, "head/orientation", rotation);
            SetControl(DeviceType, _index, "head/rotation", rotation.GetEuler());
        }
    }

    public override void Detached()
    {
        if (_added) RemoveDevice(DeviceType, _index);
        _added = false;
    }
}

/// <summary>
/// An <see cref="IEyeTracker"/> that follows the mouse, for developing gaze features without the
/// hardware (and as an accessibility fallback for head-mouse users).
/// </summary>
public sealed class MouseGazeSimulator : IEyeTracker
{
    public string Name => "Mouse (gaze simulator)";
    public bool IsAvailable => Engine.GetMainLoop() is SceneTree;
    public bool IsUserPresent => true;

    public bool TryGetGaze(out Vector2 screen)
    {
        screen = Vector2.Zero;
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return false;
        var size = tree.Root.GetVisibleRect().Size;
        if (size.X <= 0 || size.Y <= 0) return false;
        screen = tree.Root.GetMousePosition() / size;
        return true;
    }

    public bool TryGetHead(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.Zero;
        rotation = Quaternion.Identity;
        return false;
    }
}
