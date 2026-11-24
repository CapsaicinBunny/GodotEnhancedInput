using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// One physical or virtual input that can drive a game action: a key, a key composite, a mouse
/// button or the mouse itself, a joypad button, axis or stick, a Godot InputMap action, or a named
/// virtual control. A source only describes what to read; it holds no runtime state, so one
/// resource can be shared by many bindings and players.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/godot_enhanced_input/Icons/InputSource.svg")]
public partial class InputSource : Resource
{
    private SourceKind _kind = SourceKind.Key;
    private string _inputMapAction = string.Empty;
    private StringName _inputMapActionName;

    /// <summary>What this source reads. The Inspector only shows the fields that apply.</summary>
    [Export]
    public SourceKind Kind
    {
        get => _kind;
        set { _kind = value; NotifyPropertyListChanged(); }
    }

    /// <summary>How keys are matched: by position (default), by key code, or by printed label.</summary>
    [Export] public KeyMatch KeyMatch { get; set; } = KeyMatch.Physical;

    /// <summary>The key for <see cref="SourceKind.Key"/>.</summary>
    [Export] public Key Key { get; set; } = Key.None;

    /// <summary>Key giving +1 for <see cref="SourceKind.KeyAxis1D"/>, or +X for <see cref="SourceKind.KeyAxis2D"/>.</summary>
    [Export] public Key PositiveKey { get; set; } = Key.D;

    /// <summary>Key giving −1 for <see cref="SourceKind.KeyAxis1D"/>, or −X for <see cref="SourceKind.KeyAxis2D"/>.</summary>
    [Export] public Key NegativeKey { get; set; } = Key.A;

    /// <summary>Key giving −Y (up, as in Godot's screen convention) for <see cref="SourceKind.KeyAxis2D"/>.</summary>
    [Export] public Key UpKey { get; set; } = Key.W;

    /// <summary>Key giving +Y (down) for <see cref="SourceKind.KeyAxis2D"/>.</summary>
    [Export] public Key DownKey { get; set; } = Key.S;

    /// <summary>The mouse button (including wheel directions) for <see cref="SourceKind.MouseButton"/>.</summary>
    [Export] public MouseButton MouseButton { get; set; } = MouseButton.Left;

    /// <summary>The joypad button for <see cref="SourceKind.JoyButton"/>.</summary>
    [Export] public JoyButton JoyButton { get; set; } = JoyButton.A;

    /// <summary>The joypad axis for <see cref="SourceKind.JoyAxis"/>.</summary>
    [Export] public JoyAxis JoyAxis { get; set; } = JoyAxis.LeftX;

    /// <summary>Which part of the axis <see cref="SourceKind.JoyAxis"/> reads: all of it, or one half as 0…1.</summary>
    [Export] public JoyAxisRange AxisRange { get; set; } = JoyAxisRange.Full;

    /// <summary>The stick for <see cref="SourceKind.JoyStick"/>.</summary>
    [Export] public JoyStickSide JoyStick { get; set; } = JoyStickSide.Left;

    /// <summary>Joypad device index, or −1 for any joypad the player owns.</summary>
    [Export(PropertyHint.Range, "-1,15,1")] public int Device { get; set; } = SourceKey.AnyDevice;

    /// <summary>The InputMap action name for <see cref="SourceKind.InputMapAction"/>.</summary>
    [Export]
    public string InputMapAction
    {
        get => _inputMapAction;
        set { _inputMapAction = value ?? string.Empty; _inputMapActionName = null; }
    }

    /// <summary>The control name for <see cref="SourceKind.Virtual"/>.</summary>
    [Export] public string VirtualName { get; set; } = string.Empty;

    /// <summary>What a <see cref="SourceKind.Touch"/> source reads.</summary>
    [Export] public TouchChannel TouchChannel { get; set; } = TouchChannel.Pressed;

    /// <summary>The finger index for <see cref="SourceKind.Touch"/>, or −1 for any finger.</summary>
    [Export(PropertyHint.Range, "-1,9,1")] public int TouchIndex { get; set; } = SourceKey.AnyDevice;

    internal StringName InputMapActionName => _inputMapActionName ??= new StringName(_inputMapAction);

    // ------------------------------------------------------------------
    //  Factories for code-built bindings
    // ------------------------------------------------------------------

    public static InputSource ForKey(Key key, KeyMatch match = KeyMatch.Physical)
        => new() { Kind = SourceKind.Key, Key = key, KeyMatch = match };

    public static InputSource ForKeyAxis1D(Key positive, Key negative, KeyMatch match = KeyMatch.Physical)
        => new() { Kind = SourceKind.KeyAxis1D, PositiveKey = positive, NegativeKey = negative, KeyMatch = match };

    public static InputSource ForKeyAxis2D(Key up, Key down, Key left, Key right, KeyMatch match = KeyMatch.Physical)
        => new() { Kind = SourceKind.KeyAxis2D, UpKey = up, DownKey = down, NegativeKey = left, PositiveKey = right, KeyMatch = match };

    public static InputSource ForMouseButton(MouseButton button) => new() { Kind = SourceKind.MouseButton, MouseButton = button };
    public static InputSource ForMouseMotion() => new() { Kind = SourceKind.MouseMotion };
    public static InputSource ForMousePosition() => new() { Kind = SourceKind.MousePosition };

    public static InputSource ForJoyButton(JoyButton button, int device = SourceKey.AnyDevice)
        => new() { Kind = SourceKind.JoyButton, JoyButton = button, Device = device };

    public static InputSource ForJoyAxis(JoyAxis axis, int device = SourceKey.AnyDevice, JoyAxisRange range = JoyAxisRange.Full)
        => new() { Kind = SourceKind.JoyAxis, JoyAxis = axis, Device = device, AxisRange = range };

    public static InputSource ForJoyStick(JoyStickSide side, int device = SourceKey.AnyDevice)
        => new() { Kind = SourceKind.JoyStick, JoyStick = side, Device = device };

    public static InputSource ForInputMapAction(string action) => new() { Kind = SourceKind.InputMapAction, InputMapAction = action };
    public static InputSource ForVirtual(string name) => new() { Kind = SourceKind.Virtual, VirtualName = name };
    public static InputSource Nothing() => new() { Kind = SourceKind.None };

    /// <summary>What kind of quantity this source produces.</summary>
    public ValueNature Nature => _kind switch
    {
        SourceKind.MouseMotion => ValueNature.Delta,
        SourceKind.MousePosition => ValueNature.Absolute,
        SourceKind.Touch => TouchChannel switch
        {
            TouchChannel.Drag => ValueNature.Delta,
            TouchChannel.Position => ValueNature.Absolute,
            _ => ValueNature.State,
        },
        _ => ValueNature.State,
    };

    public static InputSource ForTouch(TouchChannel channel, int finger = SourceKey.AnyDevice)
        => new() { Kind = SourceKind.Touch, TouchChannel = channel, TouchIndex = finger };

    // ------------------------------------------------------------------
    //  Reading
    // ------------------------------------------------------------------

    /// <summary>Reads this source's value through a reader (normally the player's masked raw state).</summary>
    public Vector3 Read(ISourceReader reader)
    {
        switch (_kind)
        {
            case SourceKind.Key:
                return Scalar(reader, SourceKey.ForKey(Key, KeyMatch));

            case SourceKind.KeyAxis1D:
                return new Vector3(
                    Scalar(reader, SourceKey.ForKey(PositiveKey, KeyMatch)).X - Scalar(reader, SourceKey.ForKey(NegativeKey, KeyMatch)).X,
                    0f, 0f);

            case SourceKind.KeyAxis2D:
                return new Vector3(
                    Scalar(reader, SourceKey.ForKey(PositiveKey, KeyMatch)).X - Scalar(reader, SourceKey.ForKey(NegativeKey, KeyMatch)).X,
                    Scalar(reader, SourceKey.ForKey(DownKey, KeyMatch)).X - Scalar(reader, SourceKey.ForKey(UpKey, KeyMatch)).X,
                    0f);

            case SourceKind.MouseButton:
                return Scalar(reader, SourceKey.ForMouseButton(MouseButton));

            case SourceKind.MouseMotion:
                return reader.Read(SourceKey.MouseMotion);

            case SourceKind.MousePosition:
                return reader.Read(SourceKey.MousePosition);

            case SourceKind.JoyButton:
                return Scalar(reader, SourceKey.ForJoyButton(JoyButton, Device));

            case SourceKind.JoyAxis:
            {
                float v = reader.Read(SourceKey.ForJoyAxis(JoyAxis, Device)).X;
                return new Vector3(AxisRange switch
                {
                    JoyAxisRange.Positive => Mathf.Max(v, 0f),
                    JoyAxisRange.Negative => Mathf.Max(-v, 0f),
                    _ => v,
                }, 0f, 0f);
            }

            case SourceKind.JoyStick:
            {
                // Both axes from the same pad: two pads never combine into a direction neither points.
                var (x, y) = StickAxes(JoyStick);
                var stick = reader.ReadStick(x, y, Device);
                return new Vector3(stick.X, stick.Y, 0f);
            }

            case SourceKind.InputMapAction:
                return Scalar(reader, SourceKey.ForInputMapAction(_inputMapAction));

            case SourceKind.Virtual:
                return reader.Read(SourceKey.ForVirtual(VirtualName));

            case SourceKind.Touch:
                return reader.Read(SourceKey.ForTouch(TouchChannel, TouchIndex));

            default:
                return Vector3.Zero;
        }
    }

    /// <summary>Adds every raw channel this source reads to <paramref name="keys"/>.</summary>
    public void CollectKeys(List<SourceKey> keys)
    {
        switch (_kind)
        {
            case SourceKind.Key:
                keys.Add(SourceKey.ForKey(Key, KeyMatch));
                break;
            case SourceKind.KeyAxis1D:
                keys.Add(SourceKey.ForKey(PositiveKey, KeyMatch));
                keys.Add(SourceKey.ForKey(NegativeKey, KeyMatch));
                break;
            case SourceKind.KeyAxis2D:
                keys.Add(SourceKey.ForKey(PositiveKey, KeyMatch));
                keys.Add(SourceKey.ForKey(NegativeKey, KeyMatch));
                keys.Add(SourceKey.ForKey(UpKey, KeyMatch));
                keys.Add(SourceKey.ForKey(DownKey, KeyMatch));
                break;
            case SourceKind.MouseButton:
                keys.Add(SourceKey.ForMouseButton(MouseButton));
                break;
            case SourceKind.MouseMotion:
                keys.Add(SourceKey.MouseMotion);
                break;
            case SourceKind.MousePosition:
                keys.Add(SourceKey.MousePosition);
                break;
            case SourceKind.JoyButton:
                keys.Add(SourceKey.ForJoyButton(JoyButton, Device));
                break;
            case SourceKind.JoyAxis:
                keys.Add(SourceKey.ForJoyAxis(JoyAxis, Device));
                break;
            case SourceKind.JoyStick:
            {
                var (x, y) = StickAxes(JoyStick);
                keys.Add(SourceKey.ForJoyAxis(x, Device));
                keys.Add(SourceKey.ForJoyAxis(y, Device));
                break;
            }
            case SourceKind.InputMapAction:
                keys.Add(SourceKey.ForInputMapAction(_inputMapAction));
                break;
            case SourceKind.Virtual:
                keys.Add(SourceKey.ForVirtual(VirtualName));
                break;
            case SourceKind.Touch:
                keys.Add(SourceKey.ForTouch(TouchChannel, TouchIndex));
                break;
        }
    }

    /// <summary>
    /// Builds a source from a press event, or returns null when the event is not a press this can
    /// bind: a key (matched as <paramref name="keyMatch"/>), a mouse button or wheel notch, a joypad
    /// button, or a joypad axis pushed at least <paramref name="axisThreshold"/> (as the half axis in
    /// the pushed direction). Joypad sources take the event's device unless <paramref name="anyJoypad"/>.
    /// </summary>
    public static InputSource FromEvent(InputEvent @event, KeyMatch keyMatch = KeyMatch.Physical, float axisThreshold = 0.5f, bool anyJoypad = true)
    {
        int device = anyJoypad ? SourceKey.AnyDevice : @event?.Device ?? SourceKey.AnyDevice;
        switch (@event)
        {
            case InputEventKey key when key.Pressed && !key.Echo:
            {
                var code = keyMatch switch
                {
                    KeyMatch.Logical => key.Keycode,
                    KeyMatch.Label => key.KeyLabel,
                    _ => key.PhysicalKeycode,
                };
                if (code != Key.None) return ForKey(code, keyMatch);
                // Hand-made events may only carry the logical code.
                return key.Keycode != Key.None ? ForKey(key.Keycode, KeyMatch.Logical) : null;
            }
            case InputEventMouseButton mouse when mouse.Pressed:
                return ForMouseButton(mouse.ButtonIndex);
            case InputEventJoypadButton button when button.Pressed:
                return ForJoyButton(button.ButtonIndex, device);
            case InputEventJoypadMotion motion when Mathf.Abs(motion.AxisValue) >= Mathf.Max(axisThreshold, 0.01f):
                return ForJoyAxis(motion.Axis, device, motion.AxisValue > 0f ? JoyAxisRange.Positive : JoyAxisRange.Negative);
            default:
                return null;
        }
    }

    /// <summary>
    /// Whether two sources read any of the same input channels on the same device (a source reading
    /// any device overlaps every device). Opposite halves of one joypad axis do not overlap.
    /// </summary>
    public bool Overlaps(InputSource other)
    {
        if (other == null) return false;
        if (_kind == SourceKind.JoyAxis && other._kind == SourceKind.JoyAxis && JoyAxis == other.JoyAxis
            && AxisRange != JoyAxisRange.Full && other.AxisRange != JoyAxisRange.Full && AxisRange != other.AxisRange)
            return false;

        var mine = new List<SourceKey>();
        var theirs = new List<SourceKey>();
        CollectKeys(mine);
        other.CollectKeys(theirs);
        // Same channel on the same device (or finger); "any device" overlaps every device.
        foreach (var a in mine)
            foreach (var b in theirs)
                if (a.WithoutDevice() == b.WithoutDevice()
                    && (a.Device == SourceKey.AnyDevice || b.Device == SourceKey.AnyDevice || a.Device == b.Device))
                    return true;
        return false;
    }

    /// <summary>A short human-readable description, e.g. "Key Space" or "Joy LeftX".</summary>
    public string Describe() => _kind switch
    {
        SourceKind.None => "None",
        SourceKind.Key => $"Key {Key}",
        SourceKind.KeyAxis1D => $"Keys {NegativeKey}/{PositiveKey}",
        SourceKind.KeyAxis2D => $"Keys {UpKey}/{NegativeKey}/{DownKey}/{PositiveKey}",
        SourceKind.MouseButton => $"Mouse {MouseButton}",
        SourceKind.MouseMotion => "Mouse motion",
        SourceKind.MousePosition => "Mouse position",
        SourceKind.JoyButton => $"Joy {JoyButton}",
        SourceKind.JoyAxis => AxisRange switch
        {
            JoyAxisRange.Positive => $"Joy {JoyAxis}+",
            JoyAxisRange.Negative => $"Joy {JoyAxis}-",
            _ => $"Joy {JoyAxis}",
        },
        SourceKind.JoyStick => $"Joy {JoyStick} stick",
        SourceKind.InputMapAction => $"InputMap {_inputMapAction}",
        SourceKind.Virtual => $"Virtual {VirtualName}",
        SourceKind.Touch => TouchIndex < 0 ? $"Touch {TouchChannel}" : $"Touch {TouchIndex} {TouchChannel}",
        _ => _kind.ToString(),
    };

    private static Vector3 Scalar(ISourceReader reader, in SourceKey key) => reader.Read(key);

    internal static (JoyAxis X, JoyAxis Y) StickAxes(JoyStickSide side)
        => side == JoyStickSide.Right ? (JoyAxis.RightX, JoyAxis.RightY) : (JoyAxis.LeftX, JoyAxis.LeftY);

    // ------------------------------------------------------------------
    //  Inspector
    // ------------------------------------------------------------------

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        string name = property["name"].AsString();
        bool keys = _kind is SourceKind.Key or SourceKind.KeyAxis1D or SourceKind.KeyAxis2D;
        bool joy = _kind is SourceKind.JoyButton or SourceKind.JoyAxis or SourceKind.JoyStick;

        bool visible = name switch
        {
            nameof(KeyMatch) => keys,
            nameof(Key) => _kind == SourceKind.Key,
            nameof(PositiveKey) or nameof(NegativeKey) => _kind is SourceKind.KeyAxis1D or SourceKind.KeyAxis2D,
            nameof(UpKey) or nameof(DownKey) => _kind == SourceKind.KeyAxis2D,
            nameof(MouseButton) => _kind == SourceKind.MouseButton,
            nameof(JoyButton) => _kind == SourceKind.JoyButton,
            nameof(JoyAxis) or nameof(AxisRange) => _kind == SourceKind.JoyAxis,
            nameof(JoyStick) => _kind == SourceKind.JoyStick,
            nameof(Device) => joy,
            nameof(InputMapAction) => _kind == SourceKind.InputMapAction,
            nameof(VirtualName) => _kind == SourceKind.Virtual,
            nameof(TouchChannel) or nameof(TouchIndex) => _kind == SourceKind.Touch,
            _ => true,
        };

        if (!visible) property["usage"] = (int)PropertyUsageFlags.NoEditor;
    }
}
