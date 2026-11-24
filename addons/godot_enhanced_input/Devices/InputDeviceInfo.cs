namespace GodotEnhancedInput;

/// <summary>What a device is for, as far as the plugin can tell (from a <see cref="DeviceProfile"/> or its name).</summary>
public enum DeviceRole
{
    /// <summary>Not known.</summary>
    Other,
    /// <summary>Keyboard and mouse.</summary>
    KeyboardMouse,
    /// <summary>A touchscreen.</summary>
    Touchscreen,
    /// <summary>A pen or stylus.</summary>
    Pen,
    /// <summary>A gamepad.</summary>
    Gamepad,
    /// <summary>A steering wheel.</summary>
    Wheel,
    /// <summary>Pedals.</summary>
    Pedals,
    /// <summary>A flight stick.</summary>
    FlightStick,
    /// <summary>A throttle.</summary>
    Throttle,
    /// <summary>An XR controller.</summary>
    XRController,
    /// <summary>A tracked hand (hand tracking, no controller).</summary>
    XRHand,
    /// <summary>A headset or head tracker.</summary>
    HeadTracker,
    /// <summary>An eye tracker (desktop, or the face and eye tracking of a headset).</summary>
    EyeTracker,
}

/// <summary>Several <see cref="DeviceRole"/>s at once (for <see cref="InputMode.RequiredRoles"/>).</summary>
[System.Flags]
public enum DeviceRoles
{
    /// <summary>No roles.</summary>
    None = 0,
    /// <summary><see cref="DeviceRole.KeyboardMouse"/>.</summary>
    KeyboardMouse = 1 << DeviceRole.KeyboardMouse,
    /// <summary><see cref="DeviceRole.Touchscreen"/>.</summary>
    Touchscreen = 1 << DeviceRole.Touchscreen,
    /// <summary><see cref="DeviceRole.Pen"/>.</summary>
    Pen = 1 << DeviceRole.Pen,
    /// <summary><see cref="DeviceRole.Gamepad"/>.</summary>
    Gamepad = 1 << DeviceRole.Gamepad,
    /// <summary><see cref="DeviceRole.Wheel"/>.</summary>
    Wheel = 1 << DeviceRole.Wheel,
    /// <summary><see cref="DeviceRole.Pedals"/>.</summary>
    Pedals = 1 << DeviceRole.Pedals,
    /// <summary><see cref="DeviceRole.FlightStick"/>.</summary>
    FlightStick = 1 << DeviceRole.FlightStick,
    /// <summary><see cref="DeviceRole.Throttle"/>.</summary>
    Throttle = 1 << DeviceRole.Throttle,
    /// <summary><see cref="DeviceRole.XRController"/>.</summary>
    XRController = 1 << DeviceRole.XRController,
    /// <summary><see cref="DeviceRole.XRHand"/>.</summary>
    XRHand = 1 << DeviceRole.XRHand,
    /// <summary><see cref="DeviceRole.HeadTracker"/>.</summary>
    HeadTracker = 1 << DeviceRole.HeadTracker,
    /// <summary><see cref="DeviceRole.EyeTracker"/>.</summary>
    EyeTracker = 1 << DeviceRole.EyeTracker,
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

    /// <summary>The hardware serial number, when the platform reports one (tells identical pads apart).</summary>
    public string Serial { get; internal set; } = string.Empty;

    /// <summary>The index the device had before its last disconnect (−1 if it never moved).</summary>
    public int PreviousIndex { get; internal set; } = -1;

    /// <summary>
    /// The controls the device offers (paths, value kinds, ranges): a gamepad with what its family adds, the
    /// keyboard and mouse, touch, the pen, or what its backend describes. Null when unknown.
    /// </summary>
    public InputDeviceLayout Layout => Kind switch
    {
        InputDeviceKind.Joypad => InputDeviceLayout.Gamepad(HardwareFeatures | Features, Style),
        InputDeviceKind.KeyboardMouse => InputDeviceLayout.KeyboardMouse,
        InputDeviceKind.Touch => InputDeviceLayout.Touch,
        InputDeviceKind.Pen => InputDeviceLayout.Pen,
        InputDeviceKind.Custom => Backend?.DescribeDevice(this),
        _ => null,
    };

    /// <summary>How many times the device has (re)connected.</summary>
    public int ConnectionCount { get; internal set; }

    /// <summary>
    /// The pad's model regardless of how it is connected (see <see cref="ControllerDetection.ModelKey"/>);
    /// a returning pad is matched by serial number and model, so switching between a cable and
    /// Bluetooth keeps its identity even though its GUID changes.
    /// </summary>
    public string ModelKey { get; internal set; } = string.Empty;

    /// <summary>
    /// The player this device belongs to — the record of ownership, kept by the device's persistent
    /// identity: it stays while the device is missing (the device returns to this player wherever it
    /// reconnects), and numeric joypad indices are only how its input is routed while connected. Null
    /// when nobody owns it. Change it with <see cref="EnhancedInput.AssignJoypad(InputPlayer, int)"/>,
    /// <see cref="EnhancedInput.AssignDevice"/> or <see cref="EnhancedInput.UnassignJoypad"/>.
    /// </summary>
    public InputPlayer Owner { get; internal set; }

    /// <summary>Order of disconnection (lower: lost earlier).</summary>
    internal long LostOrder;

    /// <summary>
    /// A stable identifier that survives reconnecting, also under another index: "keyboard_mouse",
    /// "touch", "pen", "joypad:&lt;guid&gt;:&lt;serial&gt;" (or "joypad:&lt;guid&gt;#n" for identical pads without
    /// serials), "&lt;type&gt;:&lt;index&gt;" for backend devices.
    /// </summary>
    public string Id { get; }

    /// <summary>The device family (for prompts and players' device filters).</summary>
    public InputDeviceKind Kind { get; }

    /// <summary>The current joypad index or custom device index (−1 for keyboard, touch and pen). A pad can come back under another index.</summary>
    public int Index { get; internal set; }

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

    /// <summary>Which controller this is (joypads), from its USB ids and name.</summary>
    public ControllerFamily Family { get; internal set; } = ControllerFamily.Unknown;

    /// <summary>What the controller has in hardware (some of it Godot cannot drive; see <see cref="Features"/>).</summary>
    public ControllerFeatures HardwareFeatures { get; internal set; }

    /// <summary>What can be used through Godot on this device right now.</summary>
    public ControllerFeatures Features { get; internal set; }

    /// <summary>USB vendor id (0 when unknown).</summary>
    public int VendorId { get; internal set; }

    /// <summary>USB product id (0 when unknown).</summary>
    public int ProductId { get; internal set; }

    /// <summary>The button-label style for prompts.</summary>
    public JoypadStyle Style => ControllerDetection.StyleOf(Family);

    /// <summary>When the device was last connected (engine ticks in milliseconds).</summary>
    public ulong ConnectedAtMs { get; internal set; }

    /// <summary>A name for menus: the profile's display name, the recognised family, or the device's own.</summary>
    public string DisplayName => Profile != null && !string.IsNullOrEmpty(Profile.DisplayName) ? Profile.DisplayName
        : Family is not (ControllerFamily.Unknown or ControllerFamily.Generic) ? ControllerDetection.DisplayName(Family)
        : Name;

    /// <inheritdoc/>
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
