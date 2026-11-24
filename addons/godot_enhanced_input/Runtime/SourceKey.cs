using Godot;

namespace GodotEnhancedInput;

/// <summary>Which table of raw input a <see cref="SourceKey"/> addresses.</summary>
public enum RawKind
{
    /// <summary>A key by physical position.</summary>
    PhysicalKey,
    /// <summary>A key by key code.</summary>
    LogicalKey,
    /// <summary>A key by printed label.</summary>
    KeyLabel,
    /// <summary>A mouse button.</summary>
    MouseButton,
    /// <summary>Mouse movement this tick.</summary>
    MouseMotion,
    /// <summary>Mouse position.</summary>
    MousePosition,
    /// <summary>A joypad button.</summary>
    JoyButton,
    /// <summary>A joypad axis.</summary>
    JoyAxis,
    /// <summary>A Godot InputMap action.</summary>
    InputMapAction,
    /// <summary>A named virtual control.</summary>
    Virtual,
    /// <summary>Whether a finger is down.</summary>
    TouchPressed,
    /// <summary>Where a finger is.</summary>
    TouchPosition,
    /// <summary>A finger's movement this tick.</summary>
    TouchDrag,
    /// <summary>Scrolling this tick.</summary>
    MouseWheel,
    /// <summary>A gesture's amount this tick.</summary>
    Gesture,
    /// <summary>Pen pressure.</summary>
    PenPressure,
    /// <summary>Pen tilt.</summary>
    PenTilt,
    /// <summary>Pen eraser end in use.</summary>
    PenEraser,
    /// <summary>A motion sensor.</summary>
    Motion,
    /// <summary>A control of a backend device.</summary>
    DeviceControl,
}

/// <summary>
/// Identifies one raw input channel: a key, a button, an axis, mouse motion, an InputMap action or a
/// virtual control. Sources read raw input through these keys, and consumption between contexts is
/// tracked per key.
/// </summary>
public readonly record struct SourceKey(RawKind Kind, long Code, int Device, string Name)
{
    /// <summary>Device value meaning "any device the player owns".</summary>
    public const int AnyDevice = -1;

    /// <summary>Device value for the handheld device itself (phone or tablet sensors).</summary>
    public const int Handheld = -2;

    /// <summary>Bit position of the key side (left or right) in a key channel's code.</summary>
    private const int SideShift = 40;

    /// <summary>The channel of a key; with a side, only that side's key (left or right Shift, Ctrl, Alt, Meta).</summary>
    public static SourceKey ForKey(Key key, KeyMatch match = KeyMatch.Physical, KeyLocation side = KeyLocation.Unspecified)
    {
        long code = (long)key | ((long)side << SideShift);
        return match switch
        {
            KeyMatch.Logical => new SourceKey(RawKind.LogicalKey, code, AnyDevice, null),
            KeyMatch.Label => new SourceKey(RawKind.KeyLabel, code, AnyDevice, null),
            _ => new SourceKey(RawKind.PhysicalKey, code, AnyDevice, null),
        };
    }

    /// <summary>True for key channels.</summary>
    public bool IsKey => Kind is RawKind.PhysicalKey or RawKind.LogicalKey or RawKind.KeyLabel;

    /// <summary>The key of a key channel, without its side.</summary>
    public Key KeyCode => (Key)(Code & ((1L << SideShift) - 1));

    /// <summary>The side of a key channel (<see cref="KeyLocation.Unspecified"/>: either side).</summary>
    public KeyLocation KeySide => IsKey ? (KeyLocation)(Code >> SideShift) : KeyLocation.Unspecified;

    /// <summary>The same key channel for either side.</summary>
    public SourceKey WithoutSide() => IsKey && KeySide != KeyLocation.Unspecified ? this with { Code = (long)KeyCode } : this;

    /// <summary>The channel of a mouse button.</summary>
    public static SourceKey ForMouseButton(MouseButton button) => new(RawKind.MouseButton, (long)button, AnyDevice, null);
    /// <summary>The mouse motion channel.</summary>
    public static SourceKey MouseMotion => new(RawKind.MouseMotion, 0, AnyDevice, null);
    /// <summary>The mouse position channel.</summary>
    public static SourceKey MousePosition => new(RawKind.MousePosition, 0, AnyDevice, null);
    /// <summary>The channel of a joypad button.</summary>
    public static SourceKey ForJoyButton(JoyButton button, int device = AnyDevice) => new(RawKind.JoyButton, (long)button, device, null);
    /// <summary>The channel of a joypad axis.</summary>
    public static SourceKey ForJoyAxis(JoyAxis axis, int device = AnyDevice) => new(RawKind.JoyAxis, (long)axis, device, null);
    /// <summary>The channel of an InputMap action.</summary>
    public static SourceKey ForInputMapAction(string action) => new(RawKind.InputMapAction, 0, AnyDevice, action ?? string.Empty);
    /// <summary>The channel of a virtual control.</summary>
    public static SourceKey ForVirtual(string name) => new(RawKind.Virtual, 0, AnyDevice, name ?? string.Empty);

    /// <summary>The scrolling channel.</summary>
    public static SourceKey MouseWheel => new(RawKind.MouseWheel, 0, AnyDevice, null);

    /// <summary>The channel of a gesture.</summary>
    public static SourceKey ForGesture(GestureChannel channel) => new(RawKind.Gesture, (long)channel, AnyDevice, null);

    /// <summary>The channel of a pen reading.</summary>
    public static SourceKey ForPen(PenChannel channel) => channel switch
    {
        PenChannel.Tilt => new(RawKind.PenTilt, 0, AnyDevice, null),
        PenChannel.Eraser => new(RawKind.PenEraser, 0, AnyDevice, null),
        _ => new(RawKind.PenPressure, 0, AnyDevice, null),
    };

    /// <summary>A motion sensor of a joypad (its index, or any) or of the handheld device (<see cref="Handheld"/>).</summary>
    public static SourceKey ForMotion(MotionSensor sensor, int device = Handheld) => new(RawKind.Motion, (long)sensor, device, null);

    /// <summary>A named control of a custom device: "<c>deviceType/control</c>", on one device index or any.</summary>
    public static SourceKey ForDeviceControl(string deviceType, string control, int device = AnyDevice)
        => new(RawKind.DeviceControl, 0, device, $"{deviceType}/{control}");

    /// <summary>A touch channel of one finger (its index), or of any finger with <see cref="AnyDevice"/>.</summary>
    public static SourceKey ForTouch(TouchChannel channel, int finger = AnyDevice) => channel switch
    {
        TouchChannel.Position => new(RawKind.TouchPosition, 0, finger, null),
        TouchChannel.Drag => new(RawKind.TouchDrag, 0, finger, null),
        _ => new(RawKind.TouchPressed, 0, finger, null),
    };

    /// <summary>True for joypad channels, which are stored per device.</summary>
    public bool IsJoypad => Kind is RawKind.JoyButton or RawKind.JoyAxis || (Kind == RawKind.Motion && Device != Handheld);

    /// <summary>True for channels stored per device or finger, which "any device" reads iterate.</summary>
    public bool IsPerDevice => IsJoypad || IsTouch || Kind == RawKind.DeviceControl;

    /// <summary>True for touch channels, which are stored per finger (in <see cref="Device"/>).</summary>
    public bool IsTouch => Kind is RawKind.TouchPressed or RawKind.TouchPosition or RawKind.TouchDrag;

    /// <summary>The same channel with the device cleared, as used for consumption between contexts.</summary>
    public SourceKey WithoutDevice() => Device == AnyDevice ? this : this with { Device = AnyDevice };

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        RawKind.PhysicalKey or RawKind.LogicalKey or RawKind.KeyLabel => KeySide == KeyLocation.Unspecified ? $"{Kind}:{KeyCode}" : $"{Kind}:{KeySide}{KeyCode}",
        RawKind.MouseButton => $"Mouse:{(MouseButton)Code}",
        RawKind.JoyButton => $"Joy{(Device < 0 ? "*" : Device.ToString())}:{(JoyButton)Code}",
        RawKind.JoyAxis => $"Joy{(Device < 0 ? "*" : Device.ToString())}:{(JoyAxis)Code}",
        RawKind.InputMapAction => $"InputMap:{Name}",
        RawKind.Virtual => $"Virtual:{Name}",
        RawKind.TouchPressed or RawKind.TouchPosition or RawKind.TouchDrag => $"{Kind}:{(Device < 0 ? "*" : Device.ToString())}",
        RawKind.Motion => $"Motion:{(MotionSensor)Code}@{(Device == Handheld ? "handheld" : Device < 0 ? "*" : Device.ToString())}",
        RawKind.DeviceControl => $"Device{(Device < 0 ? "*" : Device.ToString())}:{Name}",
        _ => Kind.ToString(),
    };
}

/// <summary>Reads raw input channels. Implemented by the player, which applies consumption and blocking.</summary>
public interface ISourceReader
{
    /// <summary>The current value of a channel (scalar channels use X).</summary>
    Vector4 Read(in SourceKey key);

    /// <summary>Both axes of a stick from one device (for any device: the one pushed furthest).</summary>
    Vector2 ReadStick(JoyAxis x, JoyAxis y, int device);

    /// <summary>
    /// When the channel last went from zero to non-zero, as an ever-increasing count (larger = later; 0 =
    /// never). Opposing-input rules (<see cref="OpposingInputs"/>) use it to tell which side came last.
    /// </summary>
    ulong PressOrder(in SourceKey key) => 0;
}
