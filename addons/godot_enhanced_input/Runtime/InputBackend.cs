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

    /// <summary>Registers (or re-connects) a device of this backend and returns its record.</summary>
    protected InputDeviceInfo AddDevice(string deviceType, int index, string name, string guid = null)
        => System?.RegisterCustomDevice(this, deviceType, index, name, guid);

    /// <summary>Marks a device as disconnected and releases its controls.</summary>
    protected void RemoveDevice(string deviceType, int index) => System?.UnregisterCustomDevice(deviceType, index);

    /// <summary>Writes a control value (buttons 0/1, axes, vectors, positions).</summary>
    protected void SetControl(string deviceType, int index, string control, Vector3 value)
        => System?.SetDeviceControl(deviceType, index, control, value);

    /// <summary>Writes a scalar control value.</summary>
    protected void SetControl(string deviceType, int index, string control, float value)
        => SetControl(deviceType, index, control, new Vector3(value, 0f, 0f));
}
