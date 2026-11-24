using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>Raised when the player starts using another kind of device (or another joypad).</summary>
    [Signal] public delegate void InputDeviceChangedEventHandler(InputDeviceKind kind, int joypad);

    /// <summary>Mouse movement (pixels per event) needed to count as using the mouse.</summary>
    public float MouseMotionThreshold { get; set; } = 3f;

    /// <summary>Stick or trigger travel needed to count as using a joypad.</summary>
    public float JoyAxisThreshold { get; set; } = 0.5f;

    /// <summary>The kind of device the player last gave a deliberate input with.</summary>
    public InputDeviceKind LastDeviceKind { get; private set; }

    /// <summary>The joypad last used, or −1.</summary>
    public int LastJoypad { get; private set; } = -1;

    /// <summary>The style of the joypad the player uses: the last one used, else its first owned one, else generic.</summary>
    public JoypadStyle JoypadStyle
    {
        get
        {
            if (JoypadStyleOverride.HasValue) return JoypadStyleOverride.Value;
            int device = LastJoypad >= 0 ? LastJoypad : JoypadDevices.Count > 0 ? JoypadDevices[0] : -1;
            return InputPrompts.DetectStyle(device);
        }
    }

    /// <summary>Forces a joypad style for this player's prompts (e.g. from a settings menu).</summary>
    public JoypadStyle? JoypadStyleOverride { get; set; }

    /// <summary>Records which device an event came from (only deliberate input counts).</summary>
    private void TrackDevice(InputEvent @event)
    {
        InputDeviceKind kind;
        int joypad = LastJoypad;
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false }:
            case InputEventMouseButton { Pressed: true } mouse when mouse.Device != InputEvent.DeviceIdEmulation:
                kind = InputDeviceKind.KeyboardMouse;
                break;
            case InputEventMouseMotion motion when motion.Device != InputEvent.DeviceIdEmulation && motion.Relative.Length() >= MouseMotionThreshold:
                kind = InputDeviceKind.KeyboardMouse;
                break;
            case InputEventJoypadButton { Pressed: true } button:
                kind = InputDeviceKind.Joypad;
                joypad = button.Device;
                break;
            case InputEventJoypadMotion axis when Mathf.Abs(axis.AxisValue) >= JoyAxisThreshold:
                kind = InputDeviceKind.Joypad;
                joypad = axis.Device;
                break;
            case InputEventScreenTouch { Pressed: true }:
                kind = InputDeviceKind.Touch;
                break;
            default:
                return;
        }

        if (kind == LastDeviceKind && joypad == LastJoypad) return;
        LastDeviceKind = kind;
        LastJoypad = joypad;
        EmitSignal(SignalName.InputDeviceChanged, Variant.From(kind), joypad);
    }

    /// <summary>
    /// The sources that currently drive an action in this player's active contexts (rebinds applied;
    /// cleared slots and empty sources skipped), as prompts. With a device kind, only that kind is
    /// listed — by default the last used one; when nothing matches, all are listed.
    /// </summary>
    public List<ActionPrompt> GetPrompts(GameAction action, InputDeviceKind? kind = null, JoypadStyle? style = null)
        => GetPrompts(action, kind, style, depth: 0);

    private List<ActionPrompt> GetPrompts(GameAction action, InputDeviceKind? kind, JoypadStyle? style, int depth)
    {
        var all = new List<ActionPrompt>();
        if (action == null || depth > 3) return all;
        var joypadStyle = style ?? JoypadStyle;
        var seen = new HashSet<string>();

        foreach (var context in GetContexts())
        {
            foreach (var binding in context.Bindings)
            {
                if (binding?.Action != action) continue;
                var source = GetEffectiveSource(binding);
                if (source == null || source.Kind == SourceKind.None) continue;
                // A chord ("Shift + Space") is shown in front of the source, using the chord action's prompt
                // for the same kind of device.
                string chord = null;
                foreach (var trigger in binding.Triggers)
                {
                    if (trigger is not ChordTrigger { ChordAction: { } chordAction }) continue;
                    var chordPrompts = GetPrompts(chordAction, InputPrompts.DeviceKindOf(source), joypadStyle, depth + 1);
                    if (chordPrompts.Count > 0) chord = chordPrompts[0].Text;
                    break;
                }
                var prompt = new ActionPrompt(action, binding, context, source, joypadStyle, chord, ProfileNameOf(source));
                if (string.IsNullOrEmpty(prompt.Text) || !seen.Add(prompt.IconKey + "|" + prompt.Text)) continue;
                all.Add(prompt);
            }
        }

        var wanted = kind ?? LastDeviceKind;
        if (wanted == InputDeviceKind.Other) return all;
        var filtered = all.FindAll(p => p.DeviceKind == wanted);
        return filtered.Count > 0 ? filtered : all;
    }

    /// <summary>A device profile's name for a numbered joypad control of this player's pad, or null.</summary>
    private string ProfileNameOf(InputSource source)
    {
        if (source.RawIndex < 0 || source.Kind is not (SourceKind.JoyButton or SourceKind.JoyAxis)) return null;
        var system = GetParent() as EnhancedInput;
        if (system == null) return null;
        int pad = source.Device >= 0 ? source.Device : LastJoypad >= 0 ? LastJoypad : JoypadDevices.Count > 0 ? JoypadDevices[0] : -1;
        var profile = pad >= 0 ? system.GetJoypadDevice(pad)?.Profile : null;
        if (profile == null) return null;
        return source.Kind == SourceKind.JoyButton ? profile.ButtonName(source.RawIndex) : profile.AxisName(source.RawIndex);
    }

    /// <summary>The prompt texts for an action joined with <paramref name="separator"/> (see <see cref="GetPrompts"/>).</summary>
    public string GetPromptText(GameAction action, InputDeviceKind? kind = null, string separator = " / ")
        => string.Join(separator, GetPrompts(action, kind).ConvertAll(p => p.Text));
}
