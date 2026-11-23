using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The raw input one player has received: the current value of every channel, the strongest value
/// each channel reached since the last tick, the mouse motion summed over the tick, and the mouse
/// position. Events only write here; all evaluation happens when the player ticks.
/// </summary>
public sealed class RawInputState
{
    private readonly Dictionary<SourceKey, Vector3> _current = new();
    private readonly Dictionary<SourceKey, Vector3> _peak = new();
    private readonly HashSet<int> _joyDevices = new();
    private readonly List<SourceKey> _scratch = new();
    private Vector2 _motion;
    private Vector2 _position;

    /// <summary>Mouse movement summed since the last tick.</summary>
    public Vector2 MouseMotion => _motion;

    /// <summary>Last known mouse position in viewport pixels.</summary>
    public Vector2 MousePosition => _position;

    /// <summary>Sets a channel's value. The strongest value since the last tick is remembered.</summary>
    public void Set(in SourceKey key, Vector3 value)
    {
        _current[key] = value;
        if (!_peak.TryGetValue(key, out var peak) || value.LengthSquared() > peak.LengthSquared())
            _peak[key] = value;
        if (key.IsJoypad && key.Device >= 0) _joyDevices.Add(key.Device);
    }

    /// <summary>Sets a scalar channel's value.</summary>
    public void Set(in SourceKey key, float value) => Set(key, new Vector3(value, 0f, 0f));

    /// <summary>The value last written to a channel, ignoring what happened earlier in the tick.</summary>
    public Vector3 GetCurrent(in SourceKey key) => _current.TryGetValue(key, out var v) ? v : Vector3.Zero;

    /// <summary>Adds mouse movement for this tick.</summary>
    public void AddMouseMotion(Vector2 relative) => _motion += relative;

    /// <summary>Updates the mouse position.</summary>
    public void SetMousePosition(Vector2 position) => _position = position;

    /// <summary>
    /// The value a channel has for this tick. If a channel was released before the tick but had been
    /// actuated since the previous one, its peak is returned, so a press shorter than a tick is
    /// still seen once. Joypad channels with <see cref="SourceKey.AnyDevice"/> read the strongest device.
    /// </summary>
    public Vector3 Read(in SourceKey key)
    {
        switch (key.Kind)
        {
            case RawKind.MouseMotion: return new Vector3(_motion.X, _motion.Y, 0f);
            case RawKind.MousePosition: return new Vector3(_position.X, _position.Y, 0f);
        }

        if (key.IsJoypad && key.Device == SourceKey.AnyDevice)
        {
            var best = Vector3.Zero;
            foreach (var device in _joyDevices)
            {
                var v = ReadExact(key with { Device = device });
                if (v.LengthSquared() > best.LengthSquared()) best = v;
            }
            return best;
        }

        return ReadExact(key);
    }

    private Vector3 ReadExact(in SourceKey key)
    {
        var current = _current.TryGetValue(key, out var c) ? c : Vector3.Zero;
        if (current == Vector3.Zero && _peak.TryGetValue(key, out var peak)) return peak;
        return current;
    }

    /// <summary>Called by the player after each tick: forgets peaks and this tick's mouse motion.</summary>
    public void EndTick()
    {
        _peak.Clear();
        _motion = Vector2.Zero;
    }

    /// <summary>Forgets everything (e.g. when the window loses focus).</summary>
    public void Clear()
    {
        _current.Clear();
        _peak.Clear();
        _joyDevices.Clear();
        _motion = Vector2.Zero;
    }

    /// <summary>Forgets all channels of one joypad (e.g. when it disconnects).</summary>
    public void ClearDevice(int device)
    {
        _scratch.Clear();
        foreach (var key in _current.Keys)
            if (key.IsJoypad && key.Device == device) _scratch.Add(key);
        foreach (var key in _scratch)
        {
            _current.Remove(key);
            _peak.Remove(key);
        }
        _joyDevices.Remove(device);
    }

    /// <summary>
    /// Applies a Godot input event. With <see cref="IntakeStage.Early"/> only changes that lower a value
    /// are applied; with <see cref="IntakeStage.Unhandled"/> only changes that raise one;
    /// <see cref="IntakeStage.Direct"/> applies everything. Returns true when the event changed anything.
    /// </summary>
    public bool Apply(InputEvent @event, IntakeStage stage)
    {
        switch (@event)
        {
            case InputEventKey key:
            {
                if (key.Echo) return false;
                if (!StageAllows(stage, raising: key.Pressed)) return false;
                float v = key.Pressed ? 1f : 0f;
                bool any = false;
                if (key.PhysicalKeycode != Key.None) { Set(SourceKey.ForKey(key.PhysicalKeycode, KeyMatch.Physical), v); any = true; }
                if (key.Keycode != Key.None) { Set(SourceKey.ForKey(key.Keycode, KeyMatch.Logical), v); any = true; }
                if (key.KeyLabel != Key.None) { Set(SourceKey.ForKey(key.KeyLabel, KeyMatch.Label), v); any = true; }
                return any;
            }
            case InputEventMouseButton mouseButton:
            {
                if (!StageAllows(stage, raising: mouseButton.Pressed)) return false;
                float strength = mouseButton.Factor > 0f && IsWheel(mouseButton.ButtonIndex) ? mouseButton.Factor : 1f;
                Set(SourceKey.ForMouseButton(mouseButton.ButtonIndex), mouseButton.Pressed ? strength : 0f);
                if (stage != IntakeStage.Unhandled) _position = mouseButton.Position;
                return true;
            }
            case InputEventMouseMotion motion:
            {
                // Position is never contested by the GUI; movement is "raising" input.
                if (stage != IntakeStage.Unhandled) _position = motion.Position;
                if (StageAllows(stage, raising: true)) _motion += motion.Relative;
                return true;
            }
            case InputEventJoypadButton joyButton:
            {
                if (!StageAllows(stage, raising: joyButton.Pressed)) return false;
                Set(SourceKey.ForJoyButton(joyButton.ButtonIndex, joyButton.Device), joyButton.Pressed ? 1f : 0f);
                return true;
            }
            case InputEventJoypadMotion joyMotion:
            {
                var key = SourceKey.ForJoyAxis(joyMotion.Axis, joyMotion.Device);
                float previous = GetCurrent(key).X;
                bool raising = Mathf.Abs(joyMotion.AxisValue) > Mathf.Abs(previous);
                if (!StageAllows(stage, raising)) return false;
                Set(key, joyMotion.AxisValue);
                return true;
            }
            case InputEventAction action:
            {
                if (!StageAllows(stage, raising: action.Pressed)) return false;
                Set(SourceKey.ForInputMapAction(action.Action), action.Pressed ? action.Strength : 0f);
                return true;
            }
        }
        return false;
    }

    private static bool StageAllows(IntakeStage stage, bool raising) => stage switch
    {
        IntakeStage.Early => !raising,
        IntakeStage.Unhandled => raising,
        _ => true,
    };

    private static bool IsWheel(MouseButton button)
        => button is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight;
}
