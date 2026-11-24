using Godot;

namespace GodotEnhancedInput;

/// <summary>What a device is for, as far as the plugin can tell (from a <see cref="DeviceProfile"/> or its name).</summary>
public enum DeviceRole
{
    Other,
    KeyboardMouse,
    Touchscreen,
    Pen,
    Gamepad,
    Wheel,
    Pedals,
    FlightStick,
    Throttle,
    XRController,
}

/// <summary>
/// One input device known to <see cref="EnhancedInput"/>: the keyboard and mouse, the touchscreen, the
/// pen, each joypad, and each device of a custom backend. Joypads are identified by their GUID when
/// connected, so a different pad that later gets the same index is a different device.
/// </summary>
public sealed class InputDeviceInfo
{
    internal InputDeviceInfo(string id, InputDeviceKind kind, int index, string name)
    {
        Id = id;
        Kind = kind;
        Index = index;
        Name = name;
    }

    /// <summary>A stable identifier: "keyboard_mouse", "touch", "pen", "joypad:&lt;guid or index&gt;", "&lt;type&gt;:&lt;index&gt;".</summary>
    public string Id { get; }

    /// <summary>The device family (for prompts and players' device filters).</summary>
    public InputDeviceKind Kind { get; }

    /// <summary>The joypad index or custom device index (−1 for keyboard, touch and pen).</summary>
    public int Index { get; }

    /// <summary>The device's name.</summary>
    public string Name { get; internal set; }

    /// <summary>The joypad GUID (or backend-given identity), if known.</summary>
    public string Guid { get; internal set; } = string.Empty;

    /// <summary>For custom devices: the device type bindings use ("xr", "wheel"…).</summary>
    public string DeviceType { get; internal set; } = string.Empty;

    /// <summary>What the device is (wheel, pedals, flight stick…).</summary>
    public DeviceRole Role { get; internal set; }

    /// <summary>The matching profile, if any.</summary>
    public DeviceProfile Profile { get; internal set; }

    /// <summary>The backend that provides a custom device.</summary>
    public InputBackend Backend { get; internal set; }

    /// <summary>Whether the device is connected now.</summary>
    public bool IsConnected { get; internal set; } = true;

    /// <summary>Whether the device can vibrate (joypads Godot reports as such; backend devices say so).</summary>
    public bool CanVibrate { get; internal set; }

    /// <summary>Whether the device has motion sensors.</summary>
    public bool HasMotionSensors { get; internal set; }

    /// <summary>Whether the device has a light that can be coloured.</summary>
    public bool HasLight { get; internal set; }

    /// <summary>A name for menus: the profile's display name, or the device's own.</summary>
    public string DisplayName => Profile != null && !string.IsNullOrEmpty(Profile.DisplayName) ? Profile.DisplayName : Name;

    public override string ToString() => $"{Id} ({Kind}, {Role}{(IsConnected ? "" : ", disconnected")})";

    /// <summary>Guesses the role of a joypad from its name.</summary>
    public static DeviceRole GuessJoypadRole(string name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("pedal")) return DeviceRole.Pedals;
        if (n.Contains("wheel") || n.Contains("racing") || n.Contains("driving") || n.Contains("g29") || n.Contains("g920") || n.Contains("g923") || n.Contains("t300"))
            return DeviceRole.Wheel;
        if (n.Contains("throttle")) return DeviceRole.Throttle;
        if (n.Contains("hotas") || n.Contains("flight") || n.Contains("x52") || n.Contains("x56") || n.Contains("16000") || n.Contains("warthog") || n.Contains("joystick"))
            return DeviceRole.FlightStick;
        return DeviceRole.Gamepad;
    }
}
