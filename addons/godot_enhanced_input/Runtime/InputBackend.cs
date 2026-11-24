using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// A provider of input devices that Godot does not deliver as ordinary input events: XR controllers,
/// racing wheels or flight sticks read through a native library, network or replay input, test
/// fixtures. A backend registers its devices with <see cref="EnhancedInput"/> and writes the values of
/// their named controls; bindings read them with <see cref="SourceKind.DeviceControl"/> sources
/// ("deviceType / control"). Devices are routed to players like joypads: to the player they are
/// assigned to, or to players that take unowned devices.
/// </summary>
public abstract class InputBackend
{
    /// <summary>The system this backend is attached to (set by <see cref="EnhancedInput.AddBackend"/>).</summary>
    public EnhancedInput System { get; internal set; }

    /// <summary>A short name for logs and debug views.</summary>
    public abstract string Name { get; }

    /// <summary>Called once when the backend is added.</summary>
    public virtual void Attached() { }

    /// <summary>Called once when the backend is removed.</summary>
    public virtual void Detached() { }

    /// <summary>Called every tick before the players are evaluated: read the hardware and write control values.</summary>
    public virtual void Poll(double delta) { }

    /// <summary>Vibration request for one of this backend's devices (ignore it if the device cannot vibrate).</summary>
    public virtual void Rumble(InputDeviceInfo device, float weak, float strong, float seconds) { }

    /// <summary>Vibrates a device at a frequency in Hz (0: the device's default). Defaults to <see cref="Rumble(InputDeviceInfo, float, float, float)"/>.</summary>
    public virtual void Rumble(InputDeviceInfo device, float weak, float strong, float seconds, float frequency) => Rumble(device, weak, strong, seconds);

    /// <summary>Applies force feedback to a device (wheels, force-feedback sticks). Does nothing unless overridden.</summary>
    public virtual void SetForceFeedback(InputDeviceInfo device, ForceFeedback force) { }

    /// <summary>
    /// Describes the controls of one of this backend's devices (see <see cref="InputDeviceInfo.Layout"/>), so
    /// tools and rebinding menus can list them. Null (default): not described.
    /// </summary>
    public virtual InputDeviceLayout DescribeDevice(InputDeviceInfo device) => null;

    /// <summary>
    /// Tells this backend's devices apart from another backend's that use the same device type and index
    /// (their ids become "type:namespace:index"). The class name by default.
    /// </summary>
    public virtual string Namespace => GetType().Name;

    /// <summary>
    /// Registers (or re-connects) a device of this backend and returns its record. Give a
    /// <paramref name="guid"/> that identifies the hardware: the device's id ("type:guid") then stays the
    /// same across reconnections and backends. <paramref name="index"/> is this backend's own number for
    /// the device; when another backend already uses it for that type, bindings and channels see another
    /// runtime index (<see cref="InputDeviceInfo.Index"/>), and this backend keeps using its own.
    /// </summary>
    protected InputDeviceInfo AddDevice(string deviceType, int index, string name, string guid = null)
        => System?.RegisterCustomDevice(this, deviceType, index, name, guid);

    /// <summary>Marks a device as disconnected and releases its controls.</summary>
    protected void RemoveDevice(string deviceType, int index) => System?.UnregisterCustomDevice(this, deviceType, index);

    /// <summary>Writes a control value (buttons 0/1, axes, vectors, positions).</summary>
    protected void SetControl(string deviceType, int index, string control, Vector3 value)
        => SetControl(deviceType, index, control, new Vector4(value.X, value.Y, value.Z, 0f));

    /// <summary>Sets a control to a rotation (stored as x, y, z, w).</summary>
    protected void SetControl(string deviceType, int index, string control, Quaternion value)
        => SetControl(deviceType, index, control, ActionValue.FromQuaternion(value));

    /// <summary>Sets a control of one of this backend's devices (up to four components).</summary>
    protected void SetControl(string deviceType, int index, string control, Vector4 value)
        => System?.SetDeviceControl(this, deviceType, index, control, value, tracked: false);

    /// <summary>
    /// Sets a tracked quantity — a pose's position or rotation, a gaze point: while it has a value it
    /// counts as input even at the origin or identity (see <see cref="ActionState.IsTracked"/>). Call
    /// <see cref="ClearControl"/> when tracking is lost.
    /// </summary>
    protected void SetTrackedControl(string deviceType, int index, string control, Vector4 value)
        => System?.SetDeviceControl(this, deviceType, index, control, value, tracked: true);

    /// <inheritdoc cref="SetTrackedControl(string, int, string, Vector4)"/>
    protected void SetTrackedControl(string deviceType, int index, string control, Vector3 value)
        => SetTrackedControl(deviceType, index, control, new Vector4(value.X, value.Y, value.Z, 0f));

    /// <inheritdoc cref="SetTrackedControl(string, int, string, Vector4)"/>
    protected void SetTrackedControl(string deviceType, int index, string control, Quaternion value)
        => SetTrackedControl(deviceType, index, control, ActionValue.FromQuaternion(value));

    /// <summary>A control has no value any more (tracking lost): actions reading it end canceled with <see cref="InputEndReason.TrackingLost"/>.</summary>
    protected void ClearControl(string deviceType, int index, string control)
        => System?.ClearDeviceControl(this, deviceType, index, control);

    /// <summary>Writes a scalar control value.</summary>
    protected void SetControl(string deviceType, int index, string control, float value)
        => SetControl(deviceType, index, control, new Vector4(value, 0f, 0f, 0f));
}
