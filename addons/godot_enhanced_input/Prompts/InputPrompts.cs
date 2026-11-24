using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Names and icon keys for sources, for button prompts ("Press [Space] to jump"). Keys are named as
/// the current keyboard layout labels them; joypad buttons follow a <see cref="JoypadStyle"/>.
/// Icon keys are stable, lower-case identifiers (<c>key_space</c>, <c>mouse_left</c>,
/// <c>joy_a</c>, <c>joy_left_stick</c>…) for looking up artwork in a <see cref="PromptIconSet"/>.
/// </summary>
public static class InputPrompts
{
    // ------------------------------------------------------------------
    //  Device kinds and styles
    // ------------------------------------------------------------------

    /// <summary>The device family a source belongs to. Virtual controls count as touch (on-screen controls).</summary>
    public static InputDeviceKind DeviceKindOf(InputSource source) => source?.Kind switch
    {
        SourceKind.Key or SourceKind.KeyAxis1D or SourceKind.KeyAxis2D or SourceKind.MouseButton
            or SourceKind.MouseMotion or SourceKind.MousePosition => InputDeviceKind.KeyboardMouse,
        SourceKind.JoyButton or SourceKind.JoyAxis or SourceKind.JoyStick => InputDeviceKind.Joypad,
        SourceKind.Touch or SourceKind.Virtual => InputDeviceKind.Touch,
        SourceKind.MouseWheel => InputDeviceKind.KeyboardMouse,
        SourceKind.Gesture => InputDeviceKind.Touch,
        SourceKind.Pen => InputDeviceKind.Pen,
        SourceKind.Motion => source.MotionOfHandheld ? InputDeviceKind.Touch : InputDeviceKind.Joypad,
        SourceKind.DeviceControl => InputDeviceKind.Custom,
        SourceKind.Composite => source.Parts.Count > 0 ? DeviceKindOf(source.Parts[^1]) : InputDeviceKind.Other,
        _ => InputDeviceKind.Other,
    };

    /// <summary>Guesses a joypad's style from its name (as reported by <c>Input.GetJoyName</c>).</summary>
    public static JoypadStyle DetectStyle(string joypadName)
    {
        var name = (joypadName ?? string.Empty).ToLowerInvariant();
        if (name.Contains("xbox") || name.Contains("xinput")) return JoypadStyle.Xbox;
        if (name.Contains("playstation") || name.Contains("ps3") || name.Contains("ps4") || name.Contains("ps5")
            || name.Contains("dualshock") || name.Contains("dualsense")) return JoypadStyle.PlayStation;
        if (name.Contains("nintendo") || name.Contains("switch") || name.Contains("joy-con") || name.Contains("joycon"))
            return JoypadStyle.Nintendo;
        return JoypadStyle.Generic;
    }

    /// <summary>The style of a connected joypad (generic when unknown or not connected).</summary>
    public static JoypadStyle DetectStyle(int device)
        => device >= 0 ? DetectStyle(Input.GetJoyName(device)) : JoypadStyle.Generic;

    // ------------------------------------------------------------------
    //  Names
    // ------------------------------------------------------------------

    /// <summary>A short name for a source, as a player would read it.</summary>
    public static string GetName(InputSource source, JoypadStyle style = JoypadStyle.Generic)
    {
        if (source == null) return string.Empty;
        switch (source.Kind)
        {
            case SourceKind.Key:
                return KeyName(source.Key, source.KeyMatch);
            case SourceKind.KeyAxis1D:
                return $"{KeyName(source.NegativeKey, source.KeyMatch)}/{KeyName(source.PositiveKey, source.KeyMatch)}";
            case SourceKind.KeyAxis2D:
                return $"{KeyName(source.UpKey, source.KeyMatch)}/{KeyName(source.NegativeKey, source.KeyMatch)}/"
                       + $"{KeyName(source.DownKey, source.KeyMatch)}/{KeyName(source.PositiveKey, source.KeyMatch)}";
            case SourceKind.MouseButton:
                return MouseButtonName(source.MouseButton);
            case SourceKind.MouseMotion:
                return "Mouse";
            case SourceKind.MousePosition:
                return "Pointer";
            case SourceKind.JoyButton:
                return source.RawIndex >= 0 && source.RawIndex > (int)JoyButton.Touchpad ? $"Button {source.RawIndex}" : JoyButtonName(source.EffectiveJoyButton, style);
            case SourceKind.JoyAxis:
                return source.RawIndex > (int)JoyAxis.TriggerRight ? $"Axis {source.RawIndex}" : JoyAxisName(source.EffectiveJoyAxis, source.AxisRange, style);
            case SourceKind.MouseWheel:
                return "Mouse Wheel";
            case SourceKind.Gesture:
                return source.GestureChannel switch
                {
                    GestureChannel.Pinch => "Pinch",
                    GestureChannel.Rotate => "Twist",
                    GestureChannel.TwoFingerDrag => "Two-Finger Drag",
                    _ => "Swipe",
                };
            case SourceKind.Pen:
                return source.PenChannel switch { PenChannel.Tilt => "Pen Tilt", PenChannel.Eraser => "Eraser", _ => "Pen" };
            case SourceKind.Motion:
                return source.MotionSensor switch
                {
                    MotionSensor.Gyroscope => source.MotionOfHandheld ? "Tilt Device" : "Gyro",
                    MotionSensor.Accelerometer => "Shake",
                    MotionSensor.Gravity => source.MotionOfHandheld ? "Tilt Device" : "Tilt Controller",
                    _ => "Compass",
                };
            case SourceKind.DeviceControl:
                return source.ControlName.Replace('_', ' ');
            case SourceKind.Composite:
            {
                var names = new List<string>();
                foreach (var part in source.Parts) names.Add(GetName(part, style));
                return source.CompositeMode switch
                {
                    CompositeMode.WithModifiers => string.Join(" + ", names),
                    CompositeMode.AnyOf => string.Join(" / ", names),
                    _ => string.Join("/", names),
                };
            }
            case SourceKind.JoyStick:
                return source.JoyStick == JoyStickSide.Right ? "Right Stick" : "Left Stick";
            case SourceKind.InputMapAction:
            {
                var name = new StringName(source.InputMapAction);
                if (InputMap.HasAction(name))
                {
                    var events = InputMap.ActionGetEvents(name);
                    if (events.Count > 0) return EventName(events[0], style);
                }
                return source.InputMapAction;
            }
            case SourceKind.Touch:
                return source.TouchChannel == TouchChannel.Drag ? "Swipe" : "Touch";
            case SourceKind.Virtual:
                return source.VirtualName;
            default:
                return string.Empty;
        }
    }

    /// <summary>The label of a key. Physical keys are translated to what the current layout prints on them.</summary>
    public static string KeyName(Key key, KeyMatch match = KeyMatch.Physical)
    {
        if (key == Key.None) return string.Empty;
        var shown = key;
        if (match == KeyMatch.Physical && LayoutLabelsSupported)
        {
            var label = DisplayServer.KeyboardGetLabelFromPhysical(key);
            if (label != Key.None) shown = label;
        }
        var text = OS.GetKeycodeString(shown);
        return string.IsNullOrEmpty(text) ? key.ToString() : text;
    }

    private static bool? _layoutLabelsSupported;

    /// <summary>
    /// Whether the display server can say what the keyboard layout prints on a physical key. Headless
    /// runs cannot; physical keys are then named by their position (US layout).
    /// </summary>
    public static bool LayoutLabelsSupported
    {
        get => _layoutLabelsSupported ??= DisplayServer.GetName() is not ("headless" or "dummy" or "");
        set => _layoutLabelsSupported = value;
    }

    /// <summary>A mouse button's name.</summary>
    public static string MouseButtonName(MouseButton button) => button switch
    {
        MouseButton.Left => "Left Mouse",
        MouseButton.Right => "Right Mouse",
        MouseButton.Middle => "Middle Mouse",
        MouseButton.WheelUp => "Wheel Up",
        MouseButton.WheelDown => "Wheel Down",
        MouseButton.WheelLeft => "Wheel Left",
        MouseButton.WheelRight => "Wheel Right",
        MouseButton.Xbutton1 => "Mouse 4",
        MouseButton.Xbutton2 => "Mouse 5",
        _ => $"Mouse {(int)button}",
    };

    /// <summary>A joypad button's name in a style.</summary>
    public static string JoyButtonName(JoyButton button, JoypadStyle style = JoypadStyle.Generic)
    {
        switch (button)
        {
            case JoyButton.A: return style switch { JoypadStyle.Xbox => "A", JoypadStyle.PlayStation => "Cross", JoypadStyle.Nintendo => "B", _ => "South" };
            case JoyButton.B: return style switch { JoypadStyle.Xbox => "B", JoypadStyle.PlayStation => "Circle", JoypadStyle.Nintendo => "A", _ => "East" };
            case JoyButton.X: return style switch { JoypadStyle.Xbox => "X", JoypadStyle.PlayStation => "Square", JoypadStyle.Nintendo => "Y", _ => "West" };
            case JoyButton.Y: return style switch { JoypadStyle.Xbox => "Y", JoypadStyle.PlayStation => "Triangle", JoypadStyle.Nintendo => "X", _ => "North" };
            case JoyButton.LeftShoulder: return style switch { JoypadStyle.Xbox => "LB", JoypadStyle.PlayStation => "L1", JoypadStyle.Nintendo => "L", _ => "Left Shoulder" };
            case JoyButton.RightShoulder: return style switch { JoypadStyle.Xbox => "RB", JoypadStyle.PlayStation => "R1", JoypadStyle.Nintendo => "R", _ => "Right Shoulder" };
            case JoyButton.LeftStick: return style switch { JoypadStyle.Xbox or JoypadStyle.PlayStation => "L3", _ => "Left Stick Press" };
            case JoyButton.RightStick: return style switch { JoypadStyle.Xbox or JoypadStyle.PlayStation => "R3", _ => "Right Stick Press" };
            case JoyButton.Back: return style switch { JoypadStyle.Xbox => "View", JoypadStyle.PlayStation => "Create", JoypadStyle.Nintendo => "−", _ => "Back" };
            case JoyButton.Start: return style switch { JoypadStyle.Xbox => "Menu", JoypadStyle.PlayStation => "Options", JoypadStyle.Nintendo => "+", _ => "Start" };
            case JoyButton.Guide: return style switch { JoypadStyle.Xbox => "Xbox", JoypadStyle.PlayStation => "PS", JoypadStyle.Nintendo => "Home", _ => "Guide" };
            case JoyButton.DpadUp: return "D-Pad Up";
            case JoyButton.DpadDown: return "D-Pad Down";
            case JoyButton.DpadLeft: return "D-Pad Left";
            case JoyButton.DpadRight: return "D-Pad Right";
            case JoyButton.Touchpad: return "Touchpad";
            case JoyButton.Misc1: return style switch { JoypadStyle.Xbox => "Share", JoypadStyle.PlayStation => "Mic", JoypadStyle.Nintendo => "Capture", _ => "Misc" };
            case JoyButton.Paddle1: return "Paddle 1";
            case JoyButton.Paddle2: return "Paddle 2";
            case JoyButton.Paddle3: return "Paddle 3";
            case JoyButton.Paddle4: return "Paddle 4";
            default: return $"Button {(int)button}";
        }
    }

    /// <summary>A joypad axis's name in a style, including half axes ("Left Stick Up").</summary>
    public static string JoyAxisName(JoyAxis axis, JoyAxisRange range = JoyAxisRange.Full, JoypadStyle style = JoypadStyle.Generic)
    {
        switch (axis)
        {
            case JoyAxis.TriggerLeft: return style switch { JoypadStyle.Xbox => "LT", JoypadStyle.PlayStation => "L2", JoypadStyle.Nintendo => "ZL", _ => "Left Trigger" };
            case JoyAxis.TriggerRight: return style switch { JoypadStyle.Xbox => "RT", JoypadStyle.PlayStation => "R2", JoypadStyle.Nintendo => "ZR", _ => "Right Trigger" };
        }

        string stick = axis is JoyAxis.LeftX or JoyAxis.LeftY ? "Left Stick" : axis is JoyAxis.RightX or JoyAxis.RightY ? "Right Stick" : $"Axis {(int)axis}";
        bool horizontal = axis is JoyAxis.LeftX or JoyAxis.RightX;
        return range switch
        {
            JoyAxisRange.Positive => $"{stick} {(horizontal ? "Right" : "Down")}",
            JoyAxisRange.Negative => $"{stick} {(horizontal ? "Left" : "Up")}",
            _ => $"{stick} {(horizontal ? "X" : "Y")}",
        };
    }

    /// <summary>The name of a Godot input event (for InputMap actions).</summary>
    public static string EventName(InputEvent @event, JoypadStyle style = JoypadStyle.Generic) => @event switch
    {
        InputEventKey key when key.PhysicalKeycode != Key.None => KeyName(key.PhysicalKeycode),
        InputEventKey key => KeyName(key.Keycode, KeyMatch.Logical),
        InputEventMouseButton mouse => MouseButtonName(mouse.ButtonIndex),
        InputEventJoypadButton button => JoyButtonName(button.ButtonIndex, style),
        InputEventJoypadMotion motion => JoyAxisName(motion.Axis, motion.AxisValue > 0 ? JoyAxisRange.Positive : motion.AxisValue < 0 ? JoyAxisRange.Negative : JoyAxisRange.Full, style),
        null => string.Empty,
        _ => @event.AsText(),
    };

    // ------------------------------------------------------------------
    //  Icon keys
    // ------------------------------------------------------------------

    /// <summary>
    /// A stable icon key for a source: <c>key_space</c>, <c>keys_w_a_s_d</c>, <c>mouse_left</c>,
    /// <c>mouse_motion</c>, <c>joy_a</c>, <c>joy_trigger_left</c>, <c>joy_left_x_positive</c>,
    /// <c>joy_left_stick</c>, <c>touch</c>, <c>virtual_&lt;name&gt;</c>, <c>action_&lt;name&gt;</c>.
    /// Joypad keys use Godot's button and axis names (position-based), not a style's labels.
    /// </summary>
    public static string GetIconKey(InputSource source)
    {
        if (source == null) return string.Empty;
        return source.Kind switch
        {
            SourceKind.Key => "key_" + Snake(KeyName(source.Key, source.KeyMatch)),
            SourceKind.KeyAxis1D => $"keys_{Snake(KeyName(source.NegativeKey, source.KeyMatch))}_{Snake(KeyName(source.PositiveKey, source.KeyMatch))}",
            SourceKind.KeyAxis2D => $"keys_{Snake(KeyName(source.UpKey, source.KeyMatch))}_{Snake(KeyName(source.NegativeKey, source.KeyMatch))}_"
                                    + $"{Snake(KeyName(source.DownKey, source.KeyMatch))}_{Snake(KeyName(source.PositiveKey, source.KeyMatch))}",
            SourceKind.MouseButton => "mouse_" + Snake(source.MouseButton.ToString()),
            SourceKind.MouseMotion => "mouse_motion",
            SourceKind.MousePosition => "mouse_position",
            SourceKind.JoyButton => "joy_" + Snake(source.JoyButton.ToString()),
            SourceKind.JoyAxis => "joy_" + Snake(source.JoyAxis.ToString()) + source.AxisRange switch
            {
                JoyAxisRange.Positive => "_positive",
                JoyAxisRange.Negative => "_negative",
                _ => string.Empty,
            },
            SourceKind.JoyStick => source.JoyStick == JoyStickSide.Right ? "joy_right_stick" : "joy_left_stick",
            SourceKind.Touch => source.TouchChannel == TouchChannel.Drag ? "touch_drag" : "touch",
            SourceKind.Virtual => "virtual_" + Snake(source.VirtualName),
            SourceKind.InputMapAction => "action_" + Snake(source.InputMapAction),
            SourceKind.MouseWheel => "mouse_wheel",
            SourceKind.Gesture => "gesture_" + Snake(source.GestureChannel.ToString()),
            SourceKind.Pen => "pen_" + Snake(source.PenChannel.ToString()),
            SourceKind.Motion => (source.MotionOfHandheld ? "device_" : "joy_") + Snake(source.MotionSensor.ToString()),
            SourceKind.DeviceControl => Snake(source.DeviceType) + "_" + Snake(source.ControlName),
            SourceKind.Composite => string.Join("+", source.Parts.Select(GetIconKey)),
            _ => string.Empty,
        };
    }

    /// <summary>Lower-case words joined with underscores: "LeftShoulder" → "left_shoulder", "Page Up" → "page_up".</summary>
    public static string Snake(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var result = new StringBuilder(text.Length + 4);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsLetterOrDigit(c))
            {
                if (char.IsUpper(c) && i > 0 && result.Length > 0 && result[^1] != '_' && (char.IsLower(text[i - 1]) || char.IsDigit(text[i - 1])))
                    result.Append('_');
                result.Append(char.ToLowerInvariant(c));
            }
            else if (result.Length > 0 && result[^1] != '_')
            {
                result.Append('_');
            }
        }
        return result.ToString().Trim('_');
    }
}

/// <summary>One prompt for an action: a source that currently drives it, with its text and icon key.</summary>
public readonly struct ActionPrompt
{
    public ActionPrompt(GameAction action, ActionBinding binding, BindingContext context, InputSource source, JoypadStyle style, string chordText = null, string sourceText = null)
    {
        Action = action;
        Binding = binding;
        Context = context;
        Source = source;
        Style = style;
        DeviceKind = InputPrompts.DeviceKindOf(source);
        SourceText = sourceText ?? InputPrompts.GetName(source, style);
        ChordText = chordText ?? string.Empty;
        Text = string.IsNullOrEmpty(ChordText) ? SourceText : $"{ChordText} + {SourceText}";
        IconKey = InputPrompts.GetIconKey(source);
    }

    /// <summary>The source's own name, without a chord.</summary>
    public string SourceText { get; }

    /// <summary>The prompt of the action the binding is chorded with (see <see cref="ChordTrigger"/>), or empty.</summary>
    public string ChordText { get; }

    public GameAction Action { get; }
    public ActionBinding Binding { get; }
    public BindingContext Context { get; }

    /// <summary>The source in use (rebinds applied).</summary>
    public InputSource Source { get; }

    /// <summary>The joypad style used for <see cref="Text"/>.</summary>
    public JoypadStyle Style { get; }

    public InputDeviceKind DeviceKind { get; }

    /// <summary>What to show, e.g. "Space", "LB", "W/A/S/D", "Shift + Space".</summary>
    public string Text { get; }

    /// <summary>See <see cref="InputPrompts.GetIconKey"/>.</summary>
    public string IconKey { get; }

    public override string ToString() => Text;
}
