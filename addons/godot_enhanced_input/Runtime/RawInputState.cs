using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Decides whether one exact raw channel (with its device or finger) is hidden from the reader.</summary>
public interface IChannelMask
{
    /// <summary>True when the channel must read as zero.</summary>
    bool IsMasked(in SourceKey exactKey);
}

/// <summary>Why held input ended without being released by the player.</summary>
public enum InputEndReason
{
    /// <summary>A genuine release (or the action simply stopped firing).</summary>
    Released,
    DeviceDisconnected,
    FocusLost,
    ApplicationSuspended,
    ContextRemoved,
    DeviceReassigned,
    BindingChanged,
    ModeChanged,
    /// <summary>Blocked by a higher context, consumed, or ignored until released.</summary>
    Blocked,
    /// <summary>The scene tree was paused and the action does not run while paused.</summary>
    Paused,
    /// <summary>Raw input was cleared by code (<see cref="InputPlayer.ClearRawInput"/>).</summary>
    Cleared,
    /// <summary>A replay started or stopped.</summary>
    Replay,
}

/// <summary>
/// The raw input one player has received. Events only write here; all evaluation happens when the
/// player ticks.
///
/// Every on/off change of a channel (press, release, an axis leaving or returning to zero) is queued
/// with a global sequence number. A tick evaluates in steps: each step observes the longest run of
/// queued changes, in arrival order, in which no channel changes twice; so all changes received since
/// the last tick are processed in that tick, in the order they happened (a press and release in one
/// frame are both seen in that frame, and a chord pressed in order stays in order). Changes that do
/// not cross zero (an axis moving from 0.4 to 0.7) simply update the latest value. The last step is
/// "now": it carries the frame's per-frame amounts (mouse motion, scrolling, drags, gestures); earlier
/// steps are the instants in between.
/// </summary>
public sealed class RawInputState
{
    /// <summary>A channel's queued on/off changes (a small ring buffer).</summary>
    private sealed class ChannelQueue
    {
        public SourceKey Key;
        public Vector4 Base;          // the value observed before the first queued change
        private Vector4[] _values = new Vector4[4];
        private long[] _seq = new long[4];
        private int _head;
        public int Count;
        public bool Active;

        public Vector4 First => _values[_head];
        public long FirstSeq => _seq[_head];
        public long SeqAt(int i) => _seq[(_head + i) % _seq.Length];
        public Vector4 Last => _values[(_head + Count - 1) % _values.Length];

        public void ReplaceLast(Vector4 value) => _values[(_head + Count - 1) % _values.Length] = value;

        public void Push(Vector4 value, long seq)
        {
            if (Count == _values.Length)
            {
                var values = new Vector4[_values.Length * 2];
                var seqs = new long[_seq.Length * 2];
                for (int i = 0; i < Count; i++)
                {
                    values[i] = _values[(_head + i) % _values.Length];
                    seqs[i] = _seq[(_head + i) % _seq.Length];
                }
                _values = values;
                _seq = seqs;
                _head = 0;
            }
            int at = (_head + Count) % _values.Length;
            _values[at] = value;
            _seq[at] = seq;
            Count++;
        }

        public void PopFirst()
        {
            Base = _values[_head];
            _head = (_head + 1) % _values.Length;
            Count--;
        }

        public void Clear()
        {
            Count = 0;
            _head = 0;
        }
    }

    private readonly Dictionary<SourceKey, Vector4> _current = new();
    private readonly Dictionary<SourceKey, ChannelQueue> _pending = new();
    private readonly List<ChannelQueue> _active = new();
    private long _sequence;
    private long _boundary = long.MaxValue;
    private bool _frameStep = true;
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

    /// <summary>Sets a channel's value. On/off changes are queued so each tick sees at most one.</summary>
    public void Set(in SourceKey key, Vector4 value)
    {
        bool on = value != Vector4.Zero;
        if (_pending.TryGetValue(key, out var queue) && queue.Count > 0)
        {
            if ((queue.Last != Vector4.Zero) == on) queue.ReplaceLast(value);   // same side of zero: just the latest value
            else queue.Push(value, ++_sequence);
        }
        else
        {
            var previous = GetCurrent(key);
            if ((previous != Vector4.Zero) != on)
            {
                if (queue == null) _pending[key] = queue = new ChannelQueue { Key = key };
                queue.Base = previous;
                queue.Push(value, ++_sequence);
                if (!queue.Active) { queue.Active = true; _active.Add(queue); }
            }
        }

        _current[key] = value;
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
                RawKind.PhysicalKey => Input.IsPhysicalKeyPressed((Key)key.Code),
                RawKind.LogicalKey => Input.IsKeyPressed((Key)key.Code),
                RawKind.KeyLabel => Input.IsKeyLabelPressed((Key)key.Code),
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
    /// that consuming "Space" means the same key whichever way a binding names it). Other channels
    /// are returned unchanged.
    /// </summary>
    public SourceKey PhysicalOf(in SourceKey key)
        => key.Kind is RawKind.LogicalKey or RawKind.KeyLabel && _physicalOf.TryGetValue(key, out var physical) ? physical : key;

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

    /// <summary>The value the current step observes: its first queued change if it belongs to this step, else the last observed value.</summary>
    private Vector4 Observed(in SourceKey key)
    {
        if (!_pending.TryGetValue(key, out var queue) || queue.Count == 0) return GetCurrent(key);
        return queue.FirstSeq < _boundary ? queue.First : queue.Base;
    }

    /// <summary>Whether queued changes are waiting for another evaluation step.</summary>
    public bool HasPendingChanges
    {
        get
        {
            for (int i = 0; i < _active.Count; i++)
                if (_active[i].Count > 0) return true;
            return false;
        }
    }

    /// <summary>
    /// Starts an evaluation step: decides which queued changes it observes (the longest in-order run with
    /// no channel twice). Returns true when this is the tick's last step (nothing remains after it), which
    /// carries the per-frame amounts. <paramref name="forceLast"/> makes it the last step regardless
    /// (changes beyond it wait for the next tick).
    /// </summary>
    public bool BeginStep(bool forceLast = false)
    {
        _boundary = long.MaxValue;
        for (int i = 0; i < _active.Count; i++)
        {
            var q = _active[i];
            if (q.Count >= 2 && q.SeqAt(1) < _boundary) _boundary = q.SeqAt(1);
        }
        _frameStep = forceLast || _boundary == long.MaxValue;
        return _frameStep;
    }

    /// <summary>Ends an evaluation step: the changes it observed become the channels' settled values.</summary>
    public void EndStep()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var q = _active[i];
            if (q.Count > 0 && q.FirstSeq < _boundary) q.PopFirst();
            if (q.Count == 0)
            {
                q.Active = false;
                _active[i] = _active[^1];
                _active.RemoveAt(_active.Count - 1);
            }
        }
        _boundary = long.MaxValue;
        _canceled.Clear();
    }

    /// <summary>Whether a channel was cut off this step (device lost, focus lost…) rather than released, and why.</summary>
    public bool WasCanceled(in SourceKey key, out InputEndReason reason)
    {
        if (_canceled.TryGetValue(key, out reason)) return true;
        if (key.Device == SourceKey.AnyDevice && key.IsPerDevice)
            foreach (var pair in _canceled)
                if (pair.Key.Kind == key.Kind && pair.Key.Code == key.Code && pair.Key.Name == key.Name) { reason = pair.Value; return true; }
        return false;
    }

    /// <summary>Whether any channel was cut off this step.</summary>
    public bool AnyCanceled => _canceled.Count > 0;

    /// <summary>Sets a channel to zero at once (no queued release), remembering that it was cut off and why.</summary>
    public void Cancel(in SourceKey key, InputEndReason reason)
    {
        bool held = _current.TryGetValue(key, out var value) && value != Vector4.Zero;
        if (_pending.TryGetValue(key, out var queue))
        {
            if (queue.Count > 0 && queue.Base != Vector4.Zero) held = true;   // a release queued but not yet observed
            queue.Clear();
            queue.Base = Vector4.Zero;
        }
        if (held) _canceled[key] = reason;
        _current[key] = Vector4.Zero;
    }

    /// <summary>Called by the player after each tick: clears per-frame data (mouse motion, scrolling, drags, gestures).</summary>
    public void EndTick()
    {
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

    /// <summary>Forgets everything; held channels count as cut off for <paramref name="reason"/>.</summary>
    public void Clear(InputEndReason reason)
    {
        foreach (var pair in _current)
            if (pair.Value != Vector4.Zero) _canceled[pair.Key] = reason;
        _current.Clear();
        foreach (var queue in _pending.Values) { queue.Clear(); queue.Active = false; }
        _pending.Clear();
        _active.Clear();
        _joyDevices.Clear();
        _devices.Clear();
        _fingers.Clear();
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
        _scratch.Clear();
        foreach (var key in _current.Keys)
            if (key.IsTouch) _scratch.Add(key);
        foreach (var key in _scratch) Cancel(key, reason);
        _touchDrag.Clear();
        _gestureFingers.Clear();
        _gestureTracks.Clear();
    }

    /// <summary>Cuts off every keyboard, mouse-button and pen channel (keyboard and mouse move to another player).</summary>
    public void ClearKeyboardMouse(InputEndReason reason = InputEndReason.DeviceReassigned)
    {
        _scratch.Clear();
        foreach (var key in _current.Keys)
            if (key.Kind is RawKind.PhysicalKey or RawKind.LogicalKey or RawKind.KeyLabel or RawKind.MouseButton or RawKind.PenPressure or RawKind.PenTilt or RawKind.PenEraser)
                _scratch.Add(key);
        foreach (var key in _scratch) Cancel(key, reason);
        _motion = Vector2.Zero;
        _wheel = Vector2.Zero;
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
            case InputEventMouse emulated when IgnoreTouchEmulatedMouse && emulated.Device == InputEvent.DeviceIdEmulation:
                return false;
            case InputEventMouseButton mouseButton:
            {
                if (!StageAllows(stage, raising: mouseButton.Pressed)) return false;
                bool wheel = IsWheel(mouseButton.ButtonIndex);
                float strength = mouseButton.Factor > 0f && wheel ? mouseButton.Factor : 1f;
                Set(SourceKey.ForMouseButton(mouseButton.ButtonIndex), mouseButton.Pressed ? strength : 0f);
                if (wheel && mouseButton.Pressed) _wheel += WheelDirection(mouseButton.ButtonIndex) * strength;
                if (stage != IntakeStage.Unhandled) _position = mouseButton.Position;
                if (mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
                {
                    Set(SourceKey.ForPen(PenChannel.Pressure), 0f);
                    Set(SourceKey.ForPen(PenChannel.Eraser), 0f);
                }
                return true;
            }
            case InputEventMouseMotion motion:
            {
                // Position is never contested by the GUI; movement is "raising" input.
                if (stage != IntakeStage.Unhandled)
                {
                    _position = motion.Position;
                    // Pen data rides on motion events (a plain mouse reports zero pressure and no tilt).
                    if (motion.Pressure > 0f || motion.Tilt != Vector2.Zero || motion.PenInverted || GetCurrent(SourceKey.ForPen(PenChannel.Pressure)) != Vector4.Zero)
                    {
                        Set(SourceKey.ForPen(PenChannel.Pressure), motion.Pressure);
                        Set(SourceKey.ForPen(PenChannel.Tilt), V4(motion.Tilt.X, motion.Tilt.Y, 0f));
                        Set(SourceKey.ForPen(PenChannel.Eraser), motion.PenInverted && motion.Pressure > 0f ? 1f : 0f);
                    }
                }
                if (StageAllows(stage, raising: true)) _motion += motion.Relative;
                return true;
            }
            case InputEventPanGesture pan:
            {
                if (!StageAllows(stage, raising: true)) return false;
                _wheel += pan.Delta;
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
                float value = joyMotion.AxisValue;
                if (previous != 0f && value != 0f && Mathf.Sign(value) != Mathf.Sign(previous))
                {
                    // A reversal is a release of the old direction and a press of the new one. The release
                    // is never held back by the GUI; the press must pass it like any other.
                    if (stage == IntakeStage.Early) { Set(key, 0f); return true; }
                    if (stage == IntakeStage.Direct) { Set(key, 0f); Set(key, value); return true; }
                    previous = 0f;   // Unhandled: the early stage already released it
                }
                // Toward zero lowers the current direction: never let the GUI hold it back.
                bool raising = Mathf.Abs(value) > Mathf.Abs(previous);
                if (!StageAllows(stage, raising)) return false;
                Set(key, value);
                return true;
            }
            case InputEventScreenTouch touch:
            {
                bool down = touch.Pressed && !touch.Canceled;
                if (!StageAllows(stage, raising: down)) return false;
                GestureTouch(touch.Index, touch.Position, down, touch.Canceled);
                Set(SourceKey.ForTouch(TouchChannel.Position, touch.Index), V4(touch.Position.X, touch.Position.Y, 0f));
                Set(SourceKey.ForTouch(TouchChannel.Pressed, touch.Index), down ? 1f : 0f);
                return true;
            }
            case InputEventScreenDrag drag:
            {
                // Like the mouse: position is never contested; movement is "raising" input.
                if (stage != IntakeStage.Unhandled)
                    Set(SourceKey.ForTouch(TouchChannel.Position, drag.Index), V4(drag.Position.X, drag.Position.Y, 0f));
                if (StageAllows(stage, raising: true))
                {
                    AddTouchDrag(drag.Index, drag.Relative);
                    GestureDrag(drag.Index, drag.Position, drag.Velocity);
                }
                return true;
            }
            case InputEventMagnifyGesture magnify:
            {
                if (!StageAllows(stage, raising: true)) return false;
                _pinch += magnify.Factor - 1f;
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

    private void GestureTouch(int finger, Vector2 position, bool down, bool canceled)
    {
        if (down)
        {
            _gestureFingers[finger] = position;
            _gestureTracks[finger] = (position, Vector2.Zero);
            return;
        }
        if (_gestureTracks.TryGetValue(finger, out var track) && !canceled && _gestureFingers.Count == 1)
        {
            var travel = position - track.Start;
            if (travel.Length() >= SwipeMinDistance && track.Velocity.Length() >= SwipeMinSpeed)
                _swipe = travel.Normalized();
        }
        _gestureFingers.Remove(finger);
        _gestureTracks.Remove(finger);
    }

    private void GestureDrag(int finger, Vector2 position, Vector2 velocity)
    {
        if (!_gestureFingers.ContainsKey(finger))
        {
            _gestureFingers[finger] = position;
            _gestureTracks[finger] = (position, velocity);
            return;
        }
        if (_gestureTracks.TryGetValue(finger, out var track)) _gestureTracks[finger] = (track.Start, velocity);

        if (_gestureFingers.Count == 2)
        {
            int other = -1;
            foreach (var f in _gestureFingers.Keys)
                if (f != finger) other = f;
            var otherPosition = _gestureFingers[other];
            var before = _gestureFingers[finger] - otherPosition;
            var after = position - otherPosition;
            if (before.Length() > 1f)
            {
                _pinch += after.Length() / before.Length() - 1f;
                _rotate += before.AngleTo(after);
            }
            var midpointBefore = (_gestureFingers[finger] + otherPosition) * 0.5f;
            var midpointAfter = (position + otherPosition) * 0.5f;
            _twoFingerDrag += midpointAfter - midpointBefore;
        }
        _gestureFingers[finger] = position;
    }

    private static Vector2 WheelDirection(MouseButton button) => button switch
    {
        MouseButton.WheelUp => new Vector2(0f, -1f),
        MouseButton.WheelDown => new Vector2(0f, 1f),
        MouseButton.WheelLeft => new Vector2(-1f, 0f),
        _ => new Vector2(1f, 0f),
    };

    private static bool IsWheel(MouseButton button)
        => button is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight;
}
