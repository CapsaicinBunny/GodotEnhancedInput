using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// A short text form for sources, for configuration files, consoles, tests and tools — device-independent
/// where possible ("pad:a" is the bottom face button of whichever pad the player uses).
/// <code>
/// key:space          physical key        keycode:a / keylabel:a   by key code / printed label
/// keys:a/d           two keys, one axis  keys:w/s/a/d             four keys, up/down/left/right
/// mouse:left         mouse button        mouse:wheel_up           a wheel notch as a button
/// mouse:motion  mouse:position  mouse:wheel
/// pad:a  pad2:a      button (any pad / pad 2)    pad:button12     button by number
/// pad:left_x  pad:left_y-  pad:axis7+            axis, half axis, axis by number
/// pad:left_stick  pad:right_stick                pad:gyroscope    a pad's motion sensor
/// device:gyroscope   the handheld device's sensor
/// touch:pressed  touch1:drag  pen:pressure  inputmap:jump  virtual:touch_move
/// @wheel:steering  @xr1:trigger                  control of a backend device (type, index)
/// key:ctrl + key:s   held modifiers then a value      mouse:left | pad:rt   the strongest of several
/// axis1d(pad:lt, pad:rt)   axis2d(pad:dpad_up, pad:dpad_down, pad:dpad_left, pad:dpad_right)
/// none
/// </code>
/// Names are case- and underscore-insensitive versions of Godot's enum names.
/// </summary>
public static class InputSourcePath
{
    /// <summary>Parses a path; throws <see cref="FormatException"/> when it is not one.</summary>
    public static InputSource Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("empty source path");
        return ParseExpression(text.Trim());
    }

    /// <summary>Parses a path, or returns null.</summary>
    public static InputSource TryParse(string text)
    {
        try { return Parse(text); }
        catch (FormatException) { return null; }
    }

    private static InputSource ParseExpression(string text)
    {
        var anyOf = SplitTopLevel(text, '|');
        if (anyOf.Count > 1) return InputSource.Composite(CompositeMode.AnyOf, anyOf.ConvertAll(ParseExpression).ToArray());
        var withModifiers = SplitTopLevel(text, '+');
        if (withModifiers.Count > 1 && !LooksLikeHalfAxis(text)) return InputSource.Composite(CompositeMode.WithModifiers, withModifiers.ConvertAll(ParseExpression).ToArray());
        return ParseSingle(text.Trim());
    }

    // "pad:left_x+" ends with '+', which is a half axis, not a composite.
    private static bool LooksLikeHalfAxis(string text) => SplitTopLevel(text.TrimEnd('+'), '+').Count == 1;

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == separator && depth == 0)
            {
                // A '+' directly after an axis name (no space) is a half-axis suffix.
                if (separator == '+' && (i + 1 >= text.Length || text[i + 1] != ' ') && i > 0 && text[i - 1] != ' ') continue;
                parts.Add(text.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        parts.Add(text.Substring(start).Trim());
        parts.RemoveAll(string.IsNullOrEmpty);
        return parts;
    }

    private static InputSource ParseSingle(string text)
    {
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return InputSource.Nothing();

        if (text.StartsWith("axis1d(", StringComparison.OrdinalIgnoreCase) || text.StartsWith("axis2d(", StringComparison.OrdinalIgnoreCase))
        {
            if (!text.EndsWith(")")) throw new FormatException($"missing ')' in '{text}'");
            var inner = text.Substring(7, text.Length - 8);
            var parts = SplitTopLevel(inner, ',').ConvertAll(ParseExpression);
            bool two = text[4] == '2';
            if (parts.Count != (two ? 4 : 2)) throw new FormatException($"'{text}' needs {(two ? 4 : 2)} parts");
            return InputSource.Composite(two ? CompositeMode.Axis2D : CompositeMode.Axis1D, parts.ToArray());
        }

        int colon = text.IndexOf(':');
        if (colon <= 0) throw new FormatException($"'{text}' is not a source path (expected device:control)");
        string head = text.Substring(0, colon).Trim().ToLowerInvariant();
        string control = text.Substring(colon + 1).Trim();
        if (control.Length == 0) throw new FormatException($"'{text}' has no control");

        if (head.StartsWith("@"))
        {
            var (type, index) = SplitIndex(head.Substring(1));
            return InputSource.ForDeviceControl(type, control, index);
        }

        var (device, number) = SplitIndex(head);
        switch (device)
        {
            case "key": return InputSource.ForKey(ParseEnum<Key>(control));
            case "keycode": return InputSource.ForKey(ParseEnum<Key>(control), KeyMatch.Logical);
            case "keylabel": return InputSource.ForKey(ParseEnum<Key>(control), KeyMatch.Label);
            case "keys":
            {
                var keys = control.Split('/');
                if (keys.Length == 2) return InputSource.ForKeyAxis1D(ParseEnum<Key>(keys[1]), ParseEnum<Key>(keys[0]));
                if (keys.Length == 4) return InputSource.ForKeyAxis2D(ParseEnum<Key>(keys[0]), ParseEnum<Key>(keys[1]), ParseEnum<Key>(keys[2]), ParseEnum<Key>(keys[3]));
                throw new FormatException($"'{text}' needs 2 or 4 keys");
            }
            case "mouse":
                return Normalize(control) switch
                {
                    "motion" => InputSource.ForMouseMotion(),
                    "position" => InputSource.ForMousePosition(),
                    "wheel" => InputSource.ForMouseWheel(),
                    _ => InputSource.ForMouseButton(ParseEnum<MouseButton>(control)),
                };
            case "pad": return ParsePad(control, number);
            case "device": return InputSource.ForMotion(ParseEnum<MotionSensor>(control));
            case "touch": return InputSource.ForTouch(ParseEnum<TouchChannel>(control), number);
            case "pen": return InputSource.ForPen(ParseEnum<PenChannel>(control));
            case "gesture": return InputSource.ForGesture(ParseEnum<GestureChannel>(control));
            case "inputmap": return InputSource.ForInputMapAction(control);
            case "virtual": return InputSource.ForVirtual(control);
            default: throw new FormatException($"unknown device '{device}' in '{text}'");
        }
    }

    private static InputSource ParsePad(string control, int device)
    {
        var name = Normalize(control);
        var range = JoyAxisRange.Full;
        if (control.EndsWith("+")) { range = JoyAxisRange.Positive; name = Normalize(control[..^1]); }
        else if (control.EndsWith("-")) { range = JoyAxisRange.Negative; name = Normalize(control[..^1]); }

        if (name == "leftstick") return InputSource.ForJoyStick(JoyStickSide.Left, device);
        if (name == "rightstick") return InputSource.ForJoyStick(JoyStickSide.Right, device);
        if (TryParseEnum<MotionSensor>(name, out var sensor)) return InputSource.ForMotion(sensor, device);
        if (name.StartsWith("button") && int.TryParse(name.Substring(6), out var button)) return InputSource.ForJoyButtonIndex(button, device);
        if (name.StartsWith("axis") && int.TryParse(name.Substring(4), out var axisIndex)) return InputSource.ForJoyAxisIndex(axisIndex, device, range);
        if (TryParseEnum<JoyAxis>(name, out var axis) || TryAxisAlias(name, out axis)) return InputSource.ForJoyAxis(axis, device, range);
        if (TryParseEnum<JoyButton>(name, out var joyButton) || TryButtonAlias(name, out joyButton)) return InputSource.ForJoyButton(joyButton, device);
        throw new FormatException($"unknown pad control '{control}'");
    }

    private static bool TryAxisAlias(string name, out JoyAxis axis)
    {
        switch (name)
        {
            case "lt": axis = JoyAxis.TriggerLeft; return true;
            case "rt": axis = JoyAxis.TriggerRight; return true;
            default: axis = default; return false;
        }
    }

    private static bool TryButtonAlias(string name, out JoyButton button)
    {
        switch (name)
        {
            case "south": button = JoyButton.A; return true;
            case "east": button = JoyButton.B; return true;
            case "west": button = JoyButton.X; return true;
            case "north": button = JoyButton.Y; return true;
            case "lb": button = JoyButton.LeftShoulder; return true;
            case "rb": button = JoyButton.RightShoulder; return true;
            default: button = default; return false;
        }
    }

    private static (string Name, int Index) SplitIndex(string head)
    {
        int i = head.Length;
        while (i > 0 && char.IsDigit(head[i - 1])) i--;
        if (i == head.Length) return (head, SourceKey.AnyDevice);
        return (head.Substring(0, i), int.Parse(head.Substring(i)));
    }

    private static string Normalize(string text) => text.Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();

    private static bool TryParseEnum<T>(string text, out T value) where T : struct, Enum
    {
        var wanted = Normalize(text);
        foreach (var name in Enum.GetNames<T>())
        {
            if (Normalize(name) != wanted) continue;
            value = Enum.Parse<T>(name);
            return true;
        }
        value = default;
        return false;
    }

    private static T ParseEnum<T>(string text) where T : struct, Enum
        => TryParseEnum<T>(text, out var value) ? value : throw new FormatException($"unknown {typeof(T).Name} '{text}'");

    // ------------------------------------------------------------------
    //  Formatting
    // ------------------------------------------------------------------

    /// <summary>The path of a source (round-trips through <see cref="Parse"/>).</summary>
    public static string Format(InputSource source)
    {
        if (source == null) return "none";
        string Pad() => source.Device >= 0 ? $"pad{source.Device}" : "pad";
        string Name(object value) => InputPrompts.Snake(value.ToString());
        switch (source.Kind)
        {
            case SourceKind.None: return "none";
            case SourceKind.Key:
                return (source.KeyMatch switch { KeyMatch.Logical => "keycode", KeyMatch.Label => "keylabel", _ => "key" }) + ":" + Name(source.Key);
            case SourceKind.KeyAxis1D: return $"keys:{Name(source.NegativeKey)}/{Name(source.PositiveKey)}";
            case SourceKind.KeyAxis2D: return $"keys:{Name(source.UpKey)}/{Name(source.DownKey)}/{Name(source.NegativeKey)}/{Name(source.PositiveKey)}";
            case SourceKind.MouseButton: return "mouse:" + Name(source.MouseButton);
            case SourceKind.MouseMotion: return "mouse:motion";
            case SourceKind.MousePosition: return "mouse:position";
            case SourceKind.MouseWheel: return "mouse:wheel";
            case SourceKind.JoyButton: return source.RawIndex >= 0 ? $"{Pad()}:button{source.RawIndex}" : $"{Pad()}:{Name(source.JoyButton)}";
            case SourceKind.JoyAxis:
            {
                string axis = source.RawIndex >= 0 ? $"axis{source.RawIndex}" : Name(source.JoyAxis);
                string suffix = source.AxisRange switch { JoyAxisRange.Positive => "+", JoyAxisRange.Negative => "-", _ => string.Empty };
                return $"{Pad()}:{axis}{suffix}";
            }
            case SourceKind.JoyStick: return $"{Pad()}:{(source.JoyStick == JoyStickSide.Right ? "right_stick" : "left_stick")}";
            case SourceKind.Motion: return source.MotionOfHandheld ? $"device:{Name(source.MotionSensor)}" : $"{Pad()}:{Name(source.MotionSensor)}";
            case SourceKind.Touch: return $"touch{(source.TouchIndex >= 0 ? source.TouchIndex.ToString() : string.Empty)}:{Name(source.TouchChannel)}";
            case SourceKind.Pen: return "pen:" + Name(source.PenChannel);
            case SourceKind.Gesture: return "gesture:" + Name(source.GestureChannel);
            case SourceKind.InputMapAction: return "inputmap:" + source.InputMapAction;
            case SourceKind.Virtual: return "virtual:" + source.VirtualName;
            case SourceKind.DeviceControl: return $"@{source.DeviceType}{(source.Device >= 0 ? source.Device.ToString() : string.Empty)}:{source.ControlName}";
            case SourceKind.Composite:
            {
                var parts = new List<string>();
                foreach (var part in source.Parts) parts.Add(Format(part));
                return source.CompositeMode switch
                {
                    CompositeMode.AnyOf => string.Join(" | ", parts),
                    CompositeMode.Axis1D => $"axis1d({string.Join(", ", parts)})",
                    CompositeMode.Axis2D => $"axis2d({string.Join(", ", parts)})",
                    _ => string.Join(" + ", parts),
                };
            }
            default: return "none";
        }
    }
}
