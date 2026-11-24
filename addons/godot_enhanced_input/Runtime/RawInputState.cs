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
/// Every on/off change of a channel (press, release, an axis leaving or returning to zero, and a cut-off
/// with its reason) is queued with a sequence number, the input event it came from and the time it
/// arrived. A tick evaluates the queue in steps, one input event at a time (a channel that changes
/// twice within one event, like a stick reversal, splits it), in arrival order; so every change
/// received since the last tick is processed in that tick, in order, at the moment it arrived. Changes
/// that do not cross zero (an axis moving from 0.4 to 0.7) simply update the latest value. The tick's
/// final step is "now": it carries the frame's per-frame amounts (mouse motion, scrolling, drags,
/// gestures).
/// </summary>
public sealed partial class RawInputState
{

    private readonly Dictionary<SourceKey, Vector4> _current = new();
    private readonly Dictionary<SourceKey, ChannelQueue> _pending = new();
    private readonly List<ChannelQueue> _active = new();
    private long _sequence;
    private long _groupCounter;
    private long _currentGroup;
    private int _groupDepth;
    private long _boundary = long.MaxValue;
    private long _savedBoundary = long.MaxValue;
    private bool _frameStep = true;
    private ulong _windowStart, _windowEnd;
    private bool _useStamps;
    private bool _clearing;
    private readonly HashSet<SourceKey> _present = new();
    private readonly HashSet<SourceKey> _absolute = new();

    /// <summary>The clock changes are stamped with, in microseconds (Godot's monotonic clock by default).</summary>
    public System.Func<ulong> Clock { get; set; } = Time.GetTicksUsec;

    /// <summary>When set (not NaN), changes are placed at this fraction of the coming tick instead of by their arrival time (replays).</summary>
    public double FractionOverride { get; set; } = double.NaN;

    /// <summary>While true, cut-offs and clears are ignored (a replay owns the state).</summary>
    public bool Frozen { get; set; }

    /// <summary>Raised for every cut-off (key, reason) and once for a full clear (null key), for recording.</summary>
    public event System.Action<SourceKey?, InputEndReason> CutOff;

    private readonly Dictionary<SourceKey, InputEndReason> _canceled = new();
    private readonly Dictionary<RawKind, SortedSet<int>> _devices = new();
    private readonly SortedSet<int> _joyDevices = new();
    private readonly SortedSet<int> _fingers = new();
    private Vector2 _wheel;

    // Gestures (fingers tracked from touch events; values summed per tick).
    private readonly Dictionary<int, Vector2> _gestureFingers = new();
    private readonly Dictionary<int, (Vector2 Start, Vector2 Velocity)> _gestureTracks = new();
    private float _pinch;
    private float _rotate;
    private Vector2 _twoFingerDrag;
    private Vector2 _swipe;

    /// <summary>The shortest finger travel, in pixels, that counts as a swipe.</summary>
    public float SwipeMinDistance { get; set; } = 60f;

    /// <summary>The slowest finger speed at release, in pixels per second, that counts as a swipe.</summary>
    public float SwipeMinSpeed { get; set; } = 400f;
    private readonly Dictionary<int, Vector2> _touchDrag = new();
    private readonly Dictionary<string, Vector4> _virtualDelta = new(System.StringComparer.Ordinal);
    private readonly Dictionary<SourceKey, SourceKey> _physicalOf = new();
    private readonly List<SourceKey> _scratch = new();
    private Vector2 _motion;
    private Vector2 _position;

    /// <summary>Mouse movement summed since the last tick.</summary>
    public Vector2 MouseMotion => _motion;

    /// <summary>Last known mouse position in viewport pixels.</summary>
    public Vector2 MousePosition => _position;

    /// <summary>Scrolling summed since the last tick (wheel notches and trackpad pans), down and right positive.</summary>
    public Vector2 MouseWheel => _wheel;

    /// <summary>
    /// Ignore mouse events that Godot generates from touches (Emulate Mouse From Touch), so one touch is
    /// not also a click. On by default.
    /// </summary>
    public bool IgnoreTouchEmulatedMouse { get; set; } = true;

    /// <summary>Joypad devices this state has seen.</summary>
    public IReadOnlyCollection<int> JoypadDevices => _joyDevices;

    /// <summary>Touch fingers this state has seen.</summary>
    public IReadOnlyCollection<int> Fingers => _fingers;

    private static Vector4 V4(float x, float y, float z) => new(x, y, z, 0f);

    /// <summary>Sets a channel's value (three components; the fourth is zero).</summary>
    public void Set(in SourceKey key, Vector3 value) => Set(key, new Vector4(value.X, value.Y, value.Z, 0f));

    /// <summary>Sets a channel's value from a rotation (x, y, z, w).</summary>
    public void Set(in SourceKey key, Quaternion value) => Set(key, new Vector4(value.X, value.Y, value.Z, value.W));

    /// <summary>
    /// How analog channels (axes, pressure, backend controls, virtual controls) are sampled between ticks.
    /// <see cref="GodotEnhancedInput.AnalogSampling.Extremes"/> (default) keeps every turning point, so a
    /// trigger squeezed past a threshold and back within one frame is seen past it.
    /// </summary>
    public AnalogSampling AnalogSampling { get; set; } = AnalogSampling.Extremes;

    /// <summary>How far an analog value must move back from a peak or dip before the turning point is kept (filters resting noise).</summary>
    public float AnalogBand { get; set; } = 0.02f;

    private AnalogSampling SamplingOf(in SourceKey key)
    {
        if (_absolute.Contains(key)) return AnalogSampling.Latest;   // poses and gaze: the latest reading is what matters
        return key.Kind is RawKind.JoyAxis or RawKind.PenPressure or RawKind.InputMapAction or RawKind.DeviceControl or RawKind.Virtual
            ? AnalogSampling
            : AnalogSampling.Latest;
    }

    /// <summary>
    /// Sets a channel's value. Changes that cross zero, tracking gained, and (per <see cref="AnalogSampling"/>)
    /// analog turning points are queued, so the tick evaluates them in order; other movement updates the
    /// latest value.
    /// </summary>
    public void Set(in SourceKey key, Vector4 value)
    {
        var before = GetCurrent(key);
        bool presentBefore = _present.Contains(key);
        bool crossing = (before != Vector4.Zero) != (value != Vector4.Zero);
        _pending.TryGetValue(key, out var queue);
        if (crossing || (!presentBefore && _absolute.Contains(key)))
        {
            queue ??= QueueOf(key);
            Enqueue(queue, value, before, -1, present: true, presentBefore);
            queue.Peak = value;
            queue.Trend = 0;
        }
        else if (value != before && value != Vector4.Zero)
        {
            switch (SamplingOf(key))
            {
                case AnalogSampling.Latest:
                    if (queue != null && queue.Count > 0) queue.ReplaceLast(value);   // the queued change shows the latest value
                    break;
                case AnalogSampling.All:
                    Enqueue(queue ?? QueueOf(key), value, before, -1, present: true, presentBefore);
                    break;
                default:
                    TrackExtreme(queue ?? QueueOf(key), before, value, presentBefore);
                    break;
            }
        }

        _current[key] = value;
        _present.Add(key);
        if (key.Device >= 0 && key.IsPerDevice)
        {
            if (!_devices.TryGetValue(key.Kind, out var set)) _devices[key.Kind] = set = new SortedSet<int>();
            set.Add(key.Device);
            if (key.IsJoypad) _joyDevices.Add(key.Device);
            if (key.IsTouch) _fingers.Add(key.Device);
        }
    }

    /// <summary>Adds every channel whose current value has at least <paramref name="threshold"/> magnitude.</summary>
    public void CollectActiveChannels(List<SourceKey> into, float threshold = 0.0001f)
    {
        float min = threshold * threshold;
        foreach (var pair in _current)
            if (pair.Value != Vector4.Zero && pair.Value.LengthSquared() >= min
                && pair.Key.Kind is RawKind.PhysicalKey or RawKind.LogicalKey or RawKind.MouseButton or RawKind.JoyButton or RawKind.JoyAxis)
                into.Add(pair.Key);
    }

    /// <summary>
    /// Releases keys and buttons this state holds that Godot's <c>Input</c> reports as not held (their
    /// release never reached the player). Never presses anything. Returns how many were released.
    /// </summary>
    public int ReleaseStuck()
    {
        _scratch.Clear();
        foreach (var pair in _current)
        {
            if (pair.Value == Vector4.Zero) continue;
            var key = pair.Key;
            bool held = key.Kind switch
            {
                RawKind.PhysicalKey => Input.IsPhysicalKeyPressed(key.KeyCode),
                RawKind.LogicalKey => Input.IsKeyPressed(key.KeyCode),
                RawKind.KeyLabel => Input.IsKeyLabelPressed(key.KeyCode),
                RawKind.MouseButton => IsWheel((MouseButton)key.Code) || Input.IsMouseButtonPressed((MouseButton)key.Code),
                RawKind.JoyButton when key.Device >= 0 => Input.IsJoyButtonPressed(key.Device, (JoyButton)key.Code),
                _ => true,
            };
            if (!held) _scratch.Add(key);
        }
        for (int i = 0; i < _scratch.Count; i++) Set(_scratch[i], Vector4.Zero);
        return _scratch.Count;
    }

    /// <summary>Adds scrolling for this tick.</summary>
    public void AddWheel(Vector2 amount) => _wheel += amount;

    private static readonly SortedSet<int> NoDevices = new();

    /// <summary>The devices (or fingers) that have written a channel of this kind.</summary>
    public IReadOnlyCollection<int> DevicesWithChannel(RawKind kind) => DevicesOf(kind);

    private SortedSet<int> DevicesOf(RawKind kind)
    {
        if (kind is RawKind.TouchPressed or RawKind.TouchPosition or RawKind.TouchDrag) return _fingers;
        return _devices.TryGetValue(kind, out var set) ? set : NoDevices;
    }

    /// <summary>Sets a scalar channel's value.</summary>
    public void Set(in SourceKey key, float value) => Set(key, V4(value, 0f, 0f));

    /// <summary>Adds to a virtual control for this tick only (three components).</summary>
    public void AddVirtualDelta(string name, Vector3 delta) => AddVirtualDelta(name, new Vector4(delta.X, delta.Y, delta.Z, 0f));

    /// <summary>Adds to a virtual control for this tick only (on top of its held value).</summary>
    public void AddVirtualDelta(string name, Vector4 delta)
    {
        name ??= string.Empty;
        _virtualDelta[name] = (_virtualDelta.TryGetValue(name, out var sum) ? sum : Vector4.Zero) + delta;
    }

    /// <summary>Adds finger movement for this tick.</summary>
    public void AddTouchDrag(int finger, Vector2 relative)
    {
        _touchDrag[finger] = (_touchDrag.TryGetValue(finger, out var sum) ? sum : Vector2.Zero) + relative;
        _fingers.Add(finger);
    }

    /// <summary>The value last written to a channel, ignoring changes not yet observed by a tick.</summary>
    public Vector4 GetCurrent(in SourceKey key) => _current.TryGetValue(key, out var v) ? v : Vector4.Zero;

    /// <summary>Adds mouse movement for this tick.</summary>
    public void AddMouseMotion(Vector2 relative) => _motion += relative;

    /// <summary>Updates the mouse position.</summary>
    public void SetMousePosition(Vector2 position) => _position = position;

    /// <summary>
    /// The physical key behind a logical or label key, as learned from the key events received (so
    /// that consuming "Space" means the same key whichever way a binding names it). A side is dropped:
    /// consuming Left Shift consumes Shift. Other channels are returned unchanged.
    /// </summary>
    public SourceKey PhysicalOf(in SourceKey key)
        => (key.Kind is RawKind.LogicalKey or RawKind.KeyLabel && _physicalOf.TryGetValue(key, out var physical) ? physical : key).WithoutSide();

    /// <summary>The value a channel has for this tick (see the class summary). Nothing is masked.</summary>
    public Vector4 Read(in SourceKey key) => Read(key, null);

    /// <summary>
    /// The value a channel has for this tick. Channels with <see cref="SourceKey.AnyDevice"/> read the
    /// strongest unmasked device (joypads) or finger (touch); a masked exact channel reads zero.
    /// </summary>
    public Vector4 Read(in SourceKey key, IChannelMask mask)
    {
        switch (key.Kind)
        {
            case RawKind.MouseMotion:
                return !_frameStep || Masked(mask, key) ? Vector4.Zero : V4(_motion.X, _motion.Y, 0f);
            case RawKind.MouseWheel:
                return !_frameStep || Masked(mask, key) ? Vector4.Zero : V4(_wheel.X, _wheel.Y, 0f);
            case RawKind.Gesture:
                if (!_frameStep || Masked(mask, key)) return Vector4.Zero;
                return (GestureChannel)key.Code switch
                {
                    GestureChannel.Pinch => V4(_pinch, 0f, 0f),
                    GestureChannel.Rotate => V4(_rotate, 0f, 0f),
                    GestureChannel.TwoFingerDrag => V4(_twoFingerDrag.X, _twoFingerDrag.Y, 0f),
                    _ => V4(_swipe.X, _swipe.Y, 0f),
                };
            case RawKind.MousePosition:
                return Masked(mask, key) ? Vector4.Zero : V4(_position.X, _position.Y, 0f);
            case RawKind.Virtual:
            {
                if (Masked(mask, key)) return Vector4.Zero;
                var held = Observed(key);
                return _frameStep && _virtualDelta.TryGetValue(key.Name ?? string.Empty, out var delta) ? held + delta : held;
            }
            case RawKind.TouchDrag:
            {
                if (!_frameStep) return Vector4.Zero;
                if (key.Device >= 0)
                    return Masked(mask, key) || !_touchDrag.TryGetValue(key.Device, out var d) ? Vector4.Zero : V4(d.X, d.Y, 0f);
                var sum = Vector2.Zero;
                foreach (var pair in _touchDrag)
                    if (!Masked(mask, key with { Device = pair.Key })) sum += pair.Value;
                return V4(sum.X, sum.Y, 0f);
            }
            case RawKind.TouchPosition when key.Device == SourceKey.AnyDevice:
                // The lowest-numbered finger that is down this tick.
                foreach (var finger in _fingers)
                {
                    var position = key with { Device = finger };
                    if (Masked(mask, position)) continue;
                    if (Observed(SourceKey.ForTouch(TouchChannel.Pressed, finger)) != Vector4.Zero) return GetCurrent(position);
                }
                return Vector4.Zero;
            case RawKind.TouchPosition:
                return Masked(mask, key) ? Vector4.Zero : GetCurrent(key);
        }

        if (key.Device == SourceKey.AnyDevice && key.IsPerDevice)
        {
            var devices = DevicesOf(key.Kind);
            var best = Vector4.Zero;
            foreach (var device in devices)
            {
                var exact = key with { Device = device };
                if (Masked(mask, exact)) continue;
                var v = Observed(exact);
                if (v.LengthSquared() > best.LengthSquared()) best = v;
            }
            return best;
        }

        return Masked(mask, key) ? Vector4.Zero : Observed(key);
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
        foreach (var d in DevicesOf(RawKind.JoyAxis))
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

    /// <summary>Called by the player after each tick: clears per-frame data (mouse motion, scrolling, drags, gestures).</summary>
    public void EndTick()
    {
        foreach (var queue in _pending.Values) queue.BaseFresh = false;   // the next frame's samples start a new run
        _frameStep = true;
        _motion = Vector2.Zero;
        _wheel = Vector2.Zero;
        _touchDrag.Clear();
        _virtualDelta.Clear();
        _pinch = 0f;
        _rotate = 0f;
        _twoFingerDrag = Vector2.Zero;
        _swipe = Vector2.Zero;
    }

    /// <summary>Forgets everything (e.g. when the window loses focus). Held channels are cut off, not released.</summary>
    public void Clear() => Clear(InputEndReason.Cleared);

    /// <summary>
    /// Forgets everything; held channels are cut off for <paramref name="reason"/> (queued, so the tick sees
    /// them end canceled). Ignored while <see cref="Frozen"/> unless <paramref name="force"/>.
    /// </summary>
    public void Clear(InputEndReason reason, bool force = false)
    {
        if (Frozen && !force) return;
        bool frozen = Frozen;
        Frozen = false;
        _clearing = true;
        try
        {
            _scratch.Clear();
            foreach (var key in _current.Keys) _scratch.Add(key);
            foreach (var queue in _pending.Values)
                if (queue.Count > 0 && !_current.ContainsKey(queue.Key)) _scratch.Add(queue.Key);
            foreach (var key in _scratch) Cancel(key, reason);
        }
        finally
        {
            _clearing = false;
            Frozen = frozen;
        }
        _present.Clear();
        CutOff?.Invoke(null, reason);
        _wheel = Vector2.Zero;
        _gestureFingers.Clear();
        _gestureTracks.Clear();
        _pinch = _rotate = 0f;
        _twoFingerDrag = _swipe = Vector2.Zero;
        _touchDrag.Clear();
        _virtualDelta.Clear();
        _motion = Vector2.Zero;
    }

    /// <summary>Forgets all channels of one joypad (e.g. when it disconnects). Held channels are cut off, not released.</summary>
    public void ClearDevice(int device, InputEndReason reason = InputEndReason.DeviceDisconnected)
    {
        if (Frozen) return;
        _scratch.Clear();
        foreach (var key in _current.Keys)
            if (key.IsJoypad && key.Device == device) _scratch.Add(key);
        foreach (var key in _scratch) Cancel(key, reason);
        _joyDevices.Remove(device);
        foreach (var pair in _devices)
            if (pair.Key is RawKind.JoyButton or RawKind.JoyAxis or RawKind.Motion) pair.Value.Remove(device);
    }

    /// <summary>Forgets all channels of one custom device (e.g. when its backend removes it). Held channels are cut off.</summary>
    public void ClearCustomDevice(string deviceType, int device, InputEndReason reason = InputEndReason.DeviceDisconnected)
    {
        if (Frozen) return;
        _scratch.Clear();
        var prefix = deviceType + "/";
        foreach (var key in _current.Keys)
            if (key.Kind == RawKind.DeviceControl && key.Device == device && key.Name != null && key.Name.StartsWith(prefix, System.StringComparison.Ordinal))
                _scratch.Add(key);
        foreach (var key in _scratch) Cancel(key, reason);
    }

    /// <summary>Cuts off every touch channel (fingers move to another player, or the touchscreen is taken away).</summary>
    public void ClearTouch(InputEndReason reason = InputEndReason.DeviceReassigned)
    {
        if (Frozen) return;
        _scratch.Clear();
        foreach (var key in _current.Keys)
            if (key.IsTouch || (key.Kind == RawKind.Motion && key.Device == SourceKey.Handheld)) _scratch.Add(key);   // the handheld's sensors go with the touchscreen
        foreach (var key in _scratch) Cancel(key, reason);
        _touchDrag.Clear();
        _gestureFingers.Clear();
        _gestureTracks.Clear();
    }

    /// <summary>Cuts off every keyboard, mouse-button and pen channel (keyboard and mouse move to another player).</summary>
    public void ClearKeyboardMouse(InputEndReason reason = InputEndReason.DeviceReassigned)
    {
        if (Frozen) return;
        _scratch.Clear();
        foreach (var key in _current.Keys)
            if (key.Kind is RawKind.PhysicalKey or RawKind.LogicalKey or RawKind.KeyLabel or RawKind.MouseButton or RawKind.PenPressure or RawKind.PenTilt or RawKind.PenEraser)
                _scratch.Add(key);
        foreach (var key in _scratch) Cancel(key, reason);
        _motion = Vector2.Zero;
        _wheel = Vector2.Zero;
    }

}
