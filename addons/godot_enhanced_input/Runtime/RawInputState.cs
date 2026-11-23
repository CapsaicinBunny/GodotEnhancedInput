using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Decides whether one exact raw channel (with its device or finger) is hidden from the reader.</summary>
public interface IChannelMask
{
    /// <summary>True when the channel must read as zero.</summary>
    bool IsMasked(in SourceKey exactKey);
}

/// <summary>
/// The raw input one player has received. Events only write here; all evaluation happens when the
/// player ticks.
///
/// Every on/off change of a channel (press, release, an axis leaving or returning to zero) is kept in
/// order in a per-channel queue, and each tick observes at most one of them. So no press is lost: two
/// presses that arrive between the same two ticks are seen as press, release, press, release on four
/// consecutive ticks, in the order they happened. Changes that do not cross zero (an axis moving from
/// 0.4 to 0.7) simply update the latest value.
/// </summary>
public sealed class RawInputState
{
    private readonly Dictionary<SourceKey, Vector3> _current = new();
    private readonly Dictionary<SourceKey, List<Vector3>> _pending = new();
    private readonly SortedSet<int> _joyDevices = new();
    private readonly SortedSet<int> _fingers = new();
    private readonly Dictionary<int, Vector2> _touchDrag = new();
    private readonly Dictionary<string, Vector3> _virtualDelta = new(System.StringComparer.Ordinal);
    private readonly Dictionary<SourceKey, SourceKey> _physicalOf = new();
    private readonly List<SourceKey> _scratch = new();
    private readonly Stack<List<Vector3>> _listPool = new();
    private Vector2 _motion;
    private Vector2 _position;

    /// <summary>Mouse movement summed since the last tick.</summary>
    public Vector2 MouseMotion => _motion;

    /// <summary>Last known mouse position in viewport pixels.</summary>
    public Vector2 MousePosition => _position;

    /// <summary>Joypad devices this state has seen.</summary>
    public IReadOnlyCollection<int> JoypadDevices => _joyDevices;

    /// <summary>Touch fingers this state has seen.</summary>
    public IReadOnlyCollection<int> Fingers => _fingers;

    /// <summary>Sets a channel's value. On/off changes are queued so each tick sees at most one.</summary>
    public void Set(in SourceKey key, Vector3 value)
    {
        bool on = value != Vector3.Zero;
        if (_pending.TryGetValue(key, out var queue) && queue.Count > 0)
        {
            if ((queue[^1] != Vector3.Zero) == on) queue[^1] = value;   // same side of zero: just the latest value
            else queue.Add(value);
        }
        else
        {
            bool wasOn = GetCurrent(key) != Vector3.Zero;
            if (wasOn != on)
            {
                if (queue == null)
                {
                    queue = _listPool.Count > 0 ? _listPool.Pop() : new List<Vector3>(4);
                    _pending[key] = queue;
                }
                queue.Add(value);
            }
        }

        _current[key] = value;
        if (key.IsJoypad && key.Device >= 0) _joyDevices.Add(key.Device);
        if (key.IsTouch && key.Device >= 0) _fingers.Add(key.Device);
    }

    /// <summary>Sets a scalar channel's value.</summary>
    public void Set(in SourceKey key, float value) => Set(key, new Vector3(value, 0f, 0f));

    /// <summary>Adds to a virtual control for this tick only (on top of its held value).</summary>
    public void AddVirtualDelta(string name, Vector3 delta)
    {
        name ??= string.Empty;
        _virtualDelta[name] = (_virtualDelta.TryGetValue(name, out var sum) ? sum : Vector3.Zero) + delta;
    }

    /// <summary>Adds finger movement for this tick.</summary>
    public void AddTouchDrag(int finger, Vector2 relative)
    {
        _touchDrag[finger] = (_touchDrag.TryGetValue(finger, out var sum) ? sum : Vector2.Zero) + relative;
        _fingers.Add(finger);
    }

    /// <summary>The value last written to a channel, ignoring changes not yet observed by a tick.</summary>
    public Vector3 GetCurrent(in SourceKey key) => _current.TryGetValue(key, out var v) ? v : Vector3.Zero;

    /// <summary>Adds mouse movement for this tick.</summary>
    public void AddMouseMotion(Vector2 relative) => _motion += relative;

    /// <summary>Updates the mouse position.</summary>
    public void SetMousePosition(Vector2 position) => _position = position;

    /// <summary>
    /// The physical key behind a logical or label key, as learned from the key events received (so
    /// that consuming "Space" means the same key whichever way a binding names it). Other channels
    /// are returned unchanged.
    /// </summary>
    public SourceKey PhysicalOf(in SourceKey key)
        => key.Kind is RawKind.LogicalKey or RawKind.KeyLabel && _physicalOf.TryGetValue(key, out var physical) ? physical : key;

    /// <summary>The value a channel has for this tick (see the class summary). Nothing is masked.</summary>
    public Vector3 Read(in SourceKey key) => Read(key, null);

    /// <summary>
    /// The value a channel has for this tick. Channels with <see cref="SourceKey.AnyDevice"/> read the
    /// strongest unmasked device (joypads) or finger (touch); a masked exact channel reads zero.
    /// </summary>
    public Vector3 Read(in SourceKey key, IChannelMask mask)
    {
        switch (key.Kind)
        {
            case RawKind.MouseMotion:
                return Masked(mask, key) ? Vector3.Zero : new Vector3(_motion.X, _motion.Y, 0f);
            case RawKind.MousePosition:
                return Masked(mask, key) ? Vector3.Zero : new Vector3(_position.X, _position.Y, 0f);
            case RawKind.Virtual:
            {
                if (Masked(mask, key)) return Vector3.Zero;
                var held = Observed(key);
                return _virtualDelta.TryGetValue(key.Name ?? string.Empty, out var delta) ? held + delta : held;
            }
            case RawKind.TouchDrag:
            {
                if (key.Device >= 0)
                    return Masked(mask, key) || !_touchDrag.TryGetValue(key.Device, out var d) ? Vector3.Zero : new Vector3(d.X, d.Y, 0f);
                var sum = Vector2.Zero;
                foreach (var pair in _touchDrag)
                    if (!Masked(mask, key with { Device = pair.Key })) sum += pair.Value;
                return new Vector3(sum.X, sum.Y, 0f);
            }
            case RawKind.TouchPosition when key.Device == SourceKey.AnyDevice:
                // The lowest-numbered finger that is down this tick.
                foreach (var finger in _fingers)
                {
                    var position = key with { Device = finger };
                    if (Masked(mask, position)) continue;
                    if (Observed(SourceKey.ForTouch(TouchChannel.Pressed, finger)) != Vector3.Zero) return GetCurrent(position);
                }
                return Vector3.Zero;
            case RawKind.TouchPosition:
                return Masked(mask, key) ? Vector3.Zero : GetCurrent(key);
        }

        if (key.Device == SourceKey.AnyDevice && (key.IsJoypad || key.IsTouch))
        {
            var devices = key.IsTouch ? (IEnumerable<int>)_fingers : _joyDevices;
            var best = Vector3.Zero;
            foreach (var device in devices)
            {
                var exact = key with { Device = device };
                if (Masked(mask, exact)) continue;
                var v = Observed(exact);
                if (v.LengthSquared() > best.LengthSquared()) best = v;
            }
            return best;
        }

        return Masked(mask, key) ? Vector3.Zero : Observed(key);
    }

    /// <summary>
    /// Both axes of a joypad stick, taken together from one device: the given one, or — for
    /// <see cref="SourceKey.AnyDevice"/> — the unmasked device whose stick is pushed furthest. Two pads
    /// never combine into a direction neither of them points.
    /// </summary>
    public Vector2 ReadStick(JoyAxis x, JoyAxis y, int device, IChannelMask mask = null)
    {
        if (device >= 0) return StickOf(x, y, device, mask);
        var best = Vector2.Zero;
        foreach (var d in _joyDevices)
        {
            var v = StickOf(x, y, d, mask);
            if (v.LengthSquared() > best.LengthSquared()) best = v;
        }
        return best;
    }

    private Vector2 StickOf(JoyAxis x, JoyAxis y, int device, IChannelMask mask)
    {
        var kx = SourceKey.ForJoyAxis(x, device);
        var ky = SourceKey.ForJoyAxis(y, device);
        return new Vector2(Masked(mask, kx) ? 0f : Observed(kx).X, Masked(mask, ky) ? 0f : Observed(ky).X);
    }

    private static bool Masked(IChannelMask mask, in SourceKey key) => mask != null && mask.IsMasked(key);

    /// <summary>The value this tick observes: the oldest unobserved change, or the current value.</summary>
    private Vector3 Observed(in SourceKey key)
        => _pending.TryGetValue(key, out var queue) && queue.Count > 0 ? queue[0] : GetCurrent(key);

    /// <summary>Called by the player after each tick: moves every channel on by one queued change and clears per-tick data.</summary>
    public void EndTick()
    {
        foreach (var queue in _pending.Values)
            if (queue.Count > 0) queue.RemoveAt(0);
        _motion = Vector2.Zero;
        _touchDrag.Clear();
        _virtualDelta.Clear();
    }

    /// <summary>Forgets everything (e.g. when the window loses focus).</summary>
    public void Clear()
    {
        _current.Clear();
        foreach (var queue in _pending.Values) { queue.Clear(); _listPool.Push(queue); }
        _pending.Clear();
        _joyDevices.Clear();
        _fingers.Clear();
        _touchDrag.Clear();
        _virtualDelta.Clear();
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
            if (_pending.Remove(key, out var queue)) { queue.Clear(); _listPool.Push(queue); }
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
                var physical = SourceKey.ForKey(key.PhysicalKeycode, KeyMatch.Physical);
                if (key.PhysicalKeycode != Key.None) { Set(physical, v); any = true; }
                if (key.Keycode != Key.None)
                {
                    var logical = SourceKey.ForKey(key.Keycode, KeyMatch.Logical);
                    if (key.PhysicalKeycode != Key.None) _physicalOf[logical] = physical;
                    Set(logical, v);
                    any = true;
                }
                if (key.KeyLabel != Key.None)
                {
                    var label = SourceKey.ForKey(key.KeyLabel, KeyMatch.Label);
                    if (key.PhysicalKeycode != Key.None) _physicalOf[label] = physical;
                    Set(label, v);
                    any = true;
                }
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
                // Toward zero, or across it, lowers the current direction: never let the GUI hold it back.
                bool raising = Mathf.Abs(joyMotion.AxisValue) > Mathf.Abs(previous) && Mathf.Sign(joyMotion.AxisValue) != -Mathf.Sign(previous);
                if (!StageAllows(stage, raising)) return false;
                Set(key, joyMotion.AxisValue);
                return true;
            }
            case InputEventScreenTouch touch:
            {
                bool down = touch.Pressed && !touch.Canceled;
                if (!StageAllows(stage, raising: down)) return false;
                Set(SourceKey.ForTouch(TouchChannel.Position, touch.Index), new Vector3(touch.Position.X, touch.Position.Y, 0f));
                Set(SourceKey.ForTouch(TouchChannel.Pressed, touch.Index), down ? 1f : 0f);
                return true;
            }
            case InputEventScreenDrag drag:
            {
                // Like the mouse: position is never contested; movement is "raising" input.
                if (stage != IntakeStage.Unhandled)
                    Set(SourceKey.ForTouch(TouchChannel.Position, drag.Index), new Vector3(drag.Position.X, drag.Position.Y, 0f));
                if (StageAllows(stage, raising: true)) AddTouchDrag(drag.Index, drag.Relative);
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
