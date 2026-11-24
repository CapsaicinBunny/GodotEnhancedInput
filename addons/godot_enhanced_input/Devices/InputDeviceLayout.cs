using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// One control a device offers: its source path (bind it with <see cref="CreateSource"/>), what kind of
/// value it gives, its range and nature, and hints for tools (rebindable, noisy, tracked).
/// </summary>
public sealed class InputControlDescriptor
{
    /// <summary>Describes a control.</summary>
    public InputControlDescriptor(string path, string name, string displayName, ActionValueKind valueKind, ValueNature nature,
        float minimum, float maximum, bool isRebindable = true, bool isNoisy = false, bool isTracked = false, string unit = "")
    {
        Path = path;
        Name = name;
        DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        ValueKind = valueKind;
        Nature = nature;
        Minimum = minimum;
        Maximum = maximum;
        IsRebindable = isRebindable;
        IsNoisy = isNoisy;
        IsTracked = isTracked;
        Unit = unit ?? string.Empty;
    }

    /// <summary>The source path that reads it (see <see cref="InputSourcePath"/>), e.g. "pad:left_stick", "@xr:trigger".</summary>
    public string Path { get; }

    /// <summary>The control's name on its device ("left_stick", "trigger").</summary>
    public string Name { get; }

    /// <summary>Text for people ("Left Stick", "R2").</summary>
    public string DisplayName { get; }

    /// <summary>The shape of its value.</summary>
    public ActionValueKind ValueKind { get; }

    /// <summary>Held state, a per-frame amount, or an absolute reading.</summary>
    public ValueNature Nature { get; }

    /// <summary>The smallest value per component.</summary>
    public float Minimum { get; }

    /// <summary>The largest value per component.</summary>
    public float Maximum { get; }

    /// <summary>Whether players should be offered it when rebinding.</summary>
    public bool IsRebindable { get; }

    /// <summary>Whether it moves on its own (sticks at rest, sensors): capture should ignore small changes.</summary>
    public bool IsNoisy { get; }

    /// <summary>A tracked quantity (pose, gaze) that can be absent (see <see cref="ActionState.IsTracked"/>).</summary>
    public bool IsTracked { get; }

    /// <summary>The unit, when not a plain fraction ("rad/s", "m", "px").</summary>
    public string Unit { get; }

    /// <summary>A source that reads this control.</summary>
    public InputSource CreateSource() => InputSourcePath.Parse(Path);

    /// <inheritdoc/>
    public override string ToString() => $"{Path} ({ValueKind}, {Minimum}…{Maximum}{(Unit.Length > 0 ? " " + Unit : "")})";
}

/// <summary>
/// The controls a device offers (see <see cref="InputDeviceInfo.Layout"/>). Built-in layouts describe
/// gamepads (with the extras their family has: touchpad, back buttons, motion sensors), the keyboard and
/// mouse, touch and pens; backends describe theirs through <see cref="InputBackend.DescribeDevice"/>.
/// </summary>
public sealed class InputDeviceLayout
{
    private readonly List<InputControlDescriptor> _controls;

    /// <summary>Creates a layout.</summary>
    public InputDeviceLayout(string name, IEnumerable<InputControlDescriptor> controls)
    {
        Name = name;
        _controls = new List<InputControlDescriptor>(controls ?? Array.Empty<InputControlDescriptor>());
    }

    /// <summary>The layout's name ("Gamepad", "Keyboard and mouse").</summary>
    public string Name { get; }

    /// <summary>Its controls.</summary>
    public IReadOnlyList<InputControlDescriptor> Controls => _controls;

    /// <summary>A control by name or path, or null.</summary>
    public InputControlDescriptor FindControl(string nameOrPath)
    {
        foreach (var c in _controls)
            if (string.Equals(c.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Path, nameOrPath, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({_controls.Count} controls)";

    // ------------------------------------------------------------------
    //  Built-in layouts
    // ------------------------------------------------------------------

    private static InputControlDescriptor Button(string path, string name, string display)
        => new(path, name, display, ActionValueKind.Digital, ValueNature.State, 0f, 1f);

    /// <summary>A gamepad: sticks, triggers, face, shoulder, menu and d-pad buttons, plus what the features add.</summary>
    public static InputDeviceLayout Gamepad(ControllerFeatures features = ControllerFeatures.None, JoypadStyle style = JoypadStyle.Generic)
    {
        var controls = new List<InputControlDescriptor>
        {
            new("pad:left_stick", "left_stick", "Left Stick", ActionValueKind.Axis2D, ValueNature.State, -1f, 1f, isNoisy: true),
            new("pad:right_stick", "right_stick", "Right Stick", ActionValueKind.Axis2D, ValueNature.State, -1f, 1f, isNoisy: true),
            new("pad:lt", "left_trigger", InputPrompts.JoyAxisName(JoyAxis.TriggerLeft, style: style), ActionValueKind.Axis1D, ValueNature.State, 0f, 1f),
            new("pad:rt", "right_trigger", InputPrompts.JoyAxisName(JoyAxis.TriggerRight, style: style), ActionValueKind.Axis1D, ValueNature.State, 0f, 1f),
        };
        foreach (var button in new[]
        {
            JoyButton.A, JoyButton.B, JoyButton.X, JoyButton.Y, JoyButton.LeftShoulder, JoyButton.RightShoulder,
            JoyButton.LeftStick, JoyButton.RightStick, JoyButton.Back, JoyButton.Start, JoyButton.Guide,
            JoyButton.DpadUp, JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.DpadRight,
        })
            controls.Add(Button($"pad:{Snake(button.ToString())}", Snake(button.ToString()), InputPrompts.JoyButtonName(button, style)));
        if ((features & ControllerFeatures.BackButtons) != 0)
            foreach (var paddle in new[] { JoyButton.Paddle1, JoyButton.Paddle2, JoyButton.Paddle3, JoyButton.Paddle4 })
                controls.Add(Button($"pad:{Snake(paddle.ToString())}", Snake(paddle.ToString()), InputPrompts.JoyButtonName(paddle, style)));
        if ((features & (ControllerFeatures.Touchpad | ControllerFeatures.Trackpads)) != 0)
            controls.Add(Button("pad:touchpad", "touchpad", InputPrompts.JoyButtonName(JoyButton.Touchpad, style)));
        if ((features & ControllerFeatures.ShareButton) != 0)
            controls.Add(Button("pad:misc1", "misc1", InputPrompts.JoyButtonName(JoyButton.Misc1, style)));
        if ((features & ControllerFeatures.MotionSensors) != 0)
        {
            controls.Add(new("pad:gyroscope", "gyroscope", "Gyroscope", ActionValueKind.Axis3D, ValueNature.State, -35f, 35f, isRebindable: false, isNoisy: true, unit: "rad/s"));
            controls.Add(new("pad:accelerometer", "accelerometer", "Accelerometer", ActionValueKind.Axis3D, ValueNature.State, -80f, 80f, isRebindable: false, isNoisy: true, unit: "m/s²"));
        }
        return new InputDeviceLayout("Gamepad", controls);
    }

    private static InputDeviceLayout _keyboardMouse;

    /// <summary>The keyboard (every key, by physical position) and the mouse.</summary>
    public static InputDeviceLayout KeyboardMouse => _keyboardMouse ??= BuildKeyboardMouse();

    private static InputDeviceLayout BuildKeyboardMouse()
    {
        var controls = new List<InputControlDescriptor>
        {
            Button("mouse:left", "mouse_left", "Left Mouse Button"),
            Button("mouse:right", "mouse_right", "Right Mouse Button"),
            Button("mouse:middle", "mouse_middle", "Middle Mouse Button"),
            Button("mouse:xbutton1", "mouse_back", "Mouse Back"),
            Button("mouse:xbutton2", "mouse_forward", "Mouse Forward"),
            new("mouse:motion", "mouse_motion", "Mouse Movement", ActionValueKind.Axis2D, ValueNature.Delta, float.MinValue, float.MaxValue, isRebindable: false, unit: "px"),
            new("mouse:position", "mouse_position", "Mouse Position", ActionValueKind.Axis2D, ValueNature.Absolute, 0f, float.MaxValue, isRebindable: false, unit: "px"),
            new("mouse:wheel", "mouse_wheel", "Mouse Wheel", ActionValueKind.Axis2D, ValueNature.Delta, float.MinValue, float.MaxValue),
        };
        foreach (Key key in Enum.GetValues(typeof(Key)))
        {
            if (key == Key.None || key == Key.Unknown || key == Key.Special) continue;
            string name = Snake(key.ToString());
            controls.Add(Button($"key:{name}", name, OS.GetKeycodeString(key)));
        }
        return new InputDeviceLayout("Keyboard and mouse", controls);
    }

    private static InputDeviceLayout _touch;

    /// <summary>The touchscreen: fingers and gestures.</summary>
    public static InputDeviceLayout Touch => _touch ??= new InputDeviceLayout("Touchscreen", new[]
    {
        new InputControlDescriptor("touch:pressed", "pressed", "Touch", ActionValueKind.Digital, ValueNature.State, 0f, 1f),
        new InputControlDescriptor("touch:position", "position", "Touch Position", ActionValueKind.Axis2D, ValueNature.Absolute, 0f, float.MaxValue, isRebindable: false, unit: "px"),
        new InputControlDescriptor("touch:drag", "drag", "Touch Drag", ActionValueKind.Axis2D, ValueNature.Delta, float.MinValue, float.MaxValue, isRebindable: false, unit: "px"),
        new InputControlDescriptor("gesture:pinch", "pinch", "Pinch", ActionValueKind.Axis1D, ValueNature.Delta, -1f, 1f),
        new InputControlDescriptor("gesture:rotate", "rotate", "Twist", ActionValueKind.Axis1D, ValueNature.Delta, -Mathf.Pi, Mathf.Pi, unit: "rad"),
        new InputControlDescriptor("gesture:two_finger_drag", "two_finger_drag", "Two-finger Drag", ActionValueKind.Axis2D, ValueNature.Delta, float.MinValue, float.MaxValue, unit: "px"),
        new InputControlDescriptor("gesture:swipe", "swipe", "Swipe", ActionValueKind.Axis2D, ValueNature.Delta, -1f, 1f),
    });

    private static InputDeviceLayout _pen;

    /// <summary>A pen or stylus.</summary>
    public static InputDeviceLayout Pen => _pen ??= new InputDeviceLayout("Pen", new[]
    {
        new InputControlDescriptor("pen:pressure", "pressure", "Pen Pressure", ActionValueKind.Axis1D, ValueNature.State, 0f, 1f),
        new InputControlDescriptor("pen:tilt", "tilt", "Pen Tilt", ActionValueKind.Axis2D, ValueNature.State, -1f, 1f, isRebindable: false),
        new InputControlDescriptor("pen:eraser", "eraser", "Eraser", ActionValueKind.Digital, ValueNature.State, 0f, 1f),
    });

    /// <summary>"DpadUp" → "dpad_up".</summary>
    internal static string Snake(string name)
    {
        var text = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(name[i - 1])) text.Append('_');
            else if (char.IsDigit(c) && i > 0 && !char.IsDigit(name[i - 1])) text.Append('_');
            text.Append(char.ToLowerInvariant(c));
        }
        return text.ToString();
    }
}
