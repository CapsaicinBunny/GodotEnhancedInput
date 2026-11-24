using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>
    /// When on (default), motion sensors bound in active contexts are read from Godot each tick: the
    /// handheld device's own sensors (for players that accept touch) and the sensors of the joypads this
    /// player uses, which are switched on when first needed. Turn it off to feed them yourself
    /// (<see cref="RawInputState.Set(in SourceKey, Vector4)"/>, tests, replays).
    /// </summary>
    [Export] public bool PollMotionSensors { get; set; } = true;

    private readonly HashSet<SourceKey> _polledSensors = new();

    private void PollSensors(InputSource source)
    {
        if (source.Kind == SourceKind.Composite)
        {
            foreach (var part in source.Parts)
                if (part != null) PollSensors(part);
            return;
        }
        if (source.Kind != SourceKind.Motion) return;

        if (source.MotionSensor == MotionSensor.Orientation)
        {
            // Fused from the raw sensors after polling (also when they are fed by hand, or replayed).
            _fusionWanted.Add(source.MotionOfHandheld ? SourceKey.Handheld : source.Device);
            PollSensor(MotionSensor.Gyroscope, source.MotionOfHandheld, source.Device);
            PollSensor(source.MotionOfHandheld ? MotionSensor.Gravity : MotionSensor.Accelerometer, source.MotionOfHandheld, source.Device);
            return;
        }
        PollSensor(source.MotionSensor, source.MotionOfHandheld, source.Device);
    }

    private void PollSensor(MotionSensor sensor, bool handheld, int device)
    {
        if (!PollMotionSensors || _replay != null) return;
        if (handheld)
        {
            if (!AcceptTouch) return;
            var key = SourceKey.ForMotion(sensor, SourceKey.Handheld);
            if (!_polledSensors.Add(key)) return;
            var v = sensor switch
            {
                MotionSensor.Accelerometer => Input.GetAccelerometer(),
                MotionSensor.Gyroscope => Input.GetGyroscope(),
                MotionSensor.Gravity => Input.GetGravity(),
                _ => Input.GetMagnetometer(),
            };
            ReceiveSensor(sensor, SourceKey.Handheld, new Vector4(v.X, v.Y, v.Z, 0f));
            return;
        }

        if (sensor == MotionSensor.Magnetometer) return;   // joypads have none
        foreach (var pad in JoypadsToRead(device))
        {
            var key = SourceKey.ForMotion(sensor, pad);
            if (!_polledSensors.Add(key)) continue;
            if (!Input.HasJoyMotionSensors(pad)) continue;
            if (!Input.IsJoyMotionSensorsEnabled(pad)) Input.SetJoyMotionSensorsEnabled(pad, true);
            var v = sensor switch
            {
                MotionSensor.Accelerometer => Input.GetJoyAccelerometer(pad),
                MotionSensor.Gravity => Input.GetJoyGravity(pad),
                _ => Input.GetJoyGyroscope(pad),
            };
            ReceiveSensor(sensor, pad, new Vector4(v.X, v.Y, v.Z, 0f));
        }
    }

    // ------------------------------------------------------------------
    //  Sensor fusion
    // ------------------------------------------------------------------

    private readonly HashSet<int> _fusionWanted = new();
    private readonly List<int> _fusionDevices = new();
    private readonly Dictionary<int, Quaternion> _orientations = new();
    private const float StandardGravity = 9.80665f;

    /// <summary>
    /// How fast a fused orientation's tilt is pulled towards the measured gravity, per second (1 by default).
    /// Higher trusts the accelerometer more (less drift, more shake); 0 uses the gyroscope alone.
    /// </summary>
    [Export(PropertyHint.Range, "0,20,0.05,suffix:/s")] public float OrientationGravityRate { get; set; } = 1f;

    /// <summary>
    /// Forgets the fused orientation of one device (a pad index, <see cref="SourceKey.Handheld"/>), or of all
    /// with <see cref="SourceKey.AnyDevice"/>: it starts again from the measured tilt with the current heading
    /// as straight ahead — a "recenter" button.
    /// </summary>
    public void ResetOrientation(int device = SourceKey.AnyDevice)
    {
        if (device == SourceKey.AnyDevice) _orientations.Clear();
        else _orientations.Remove(device);
    }

    /// <summary>The fused orientation of a device (identity until it has one).</summary>
    public Quaternion GetOrientation(int device) => _orientations.TryGetValue(device, out var q) ? q : Quaternion.Identity;

    private void FuseOrientations(double delta)
    {
        // Writing an orientation adds to the device set, so the devices are listed first.
        _fusionDevices.Clear();
        foreach (int wanted in _fusionWanted)
        {
            if (wanted == SourceKey.Handheld) { if (!_fusionDevices.Contains(SourceKey.Handheld)) _fusionDevices.Add(SourceKey.Handheld); continue; }
            foreach (int device in Raw.DevicesWithChannel(RawKind.Motion))
                if (device >= 0 && (wanted < 0 || wanted == device) && !_fusionDevices.Contains(device)) _fusionDevices.Add(device);
        }
        foreach (int device in _fusionDevices)
            Fuse(device, device == SourceKey.Handheld ? MotionSensor.Gravity : MotionSensor.Accelerometer, delta);
    }

    private void Fuse(int device, MotionSensor gravitySensor, double delta)
    {
        var g4 = Raw.GetCurrent(SourceKey.ForMotion(MotionSensor.Gyroscope, device));
        var a4 = Raw.GetCurrent(SourceKey.ForMotion(gravitySensor, device));
        if (a4 == Vector4.Zero && gravitySensor == MotionSensor.Gravity)
            a4 = Raw.GetCurrent(SourceKey.ForMotion(MotionSensor.Accelerometer, device));
        var gyro = new Vector3(g4.X, g4.Y, g4.Z);
        var down = new Vector3(a4.X, a4.Y, a4.Z);
        if (gyro == Vector3.Zero && down == Vector3.Zero && !_orientations.ContainsKey(device)) return;   // no readings yet

        _orientations.TryGetValue(device, out var q);
        bool fresh = !_orientations.ContainsKey(device);
        q = FuseStep(fresh ? Quaternion.Identity : q, gyro, down, (float)delta, OrientationGravityRate, fresh, gravitySensor == MotionSensor.Gravity);
        _orientations[device] = q;

        var key = SourceKey.ForMotion(MotionSensor.Orientation, device);
        Raw.MarkTracked(key);   // the identity is a real orientation, not "no input"
        ReceiveSensor(MotionSensor.Orientation, device, new Vector4(q.X, q.Y, q.Z, q.W));
    }

    /// <summary>
    /// One complementary-filter step: turns <paramref name="q"/> (device → world) by the angular velocity
    /// <paramref name="gyro"/> (rad/s, device axes) over <paramref name="dt"/>, then pulls its tilt towards the
    /// measured <paramref name="down"/> (gravity in device axes, pointing to the ground) at <paramref name="rate"/>
    /// per second. A fresh orientation starts level with the measured tilt. Accelerometer readings far from
    /// 1 g (shaking) are not used for the correction.
    /// </summary>
    public static Quaternion FuseStep(Quaternion q, Vector3 gyro, Vector3 down, float dt, float rate, bool fresh, bool downIsGravity = false)
    {
        float g = down.Length();
        bool downUsable = g > 1e-3f && (downIsGravity || (g > 0.5f * StandardGravity && g < 1.5f * StandardGravity));
        if (fresh) q = downUsable ? new Quaternion(down / g, Vector3.Down).Normalized() : Quaternion.Identity;

        float angle = gyro.Length() * dt;
        if (angle > 1e-9f) q = (q * new Quaternion(gyro / gyro.Length(), angle)).Normalized();

        if (!fresh && downUsable && rate > 0f && dt > 0f)
        {
            var predicted = (q * (down / g)).Normalized();   // where the device's down points in the world
            var correction = new Quaternion(predicted, Vector3.Down);
            float k = 1f - Mathf.Exp(-rate * dt);
            q = (Quaternion.Identity.Slerp(correction, k) * q).Normalized();
        }
        return q;
    }

    /// <summary>
    /// The connected joypads this player reads: one device (only if the player may use it), its own, or
    /// (when it takes unowned pads) every connected pad nobody owns.
    /// </summary>
    private IEnumerable<int> JoypadsToRead(int device)
    {
        var system = GetParent() as EnhancedInput;
        IList<int> connected = system != null ? system.ConnectedJoypads : Input.GetConnectedJoypads();
        if (device >= 0)
        {
            if (connected.Contains(device) && MayUseJoypad(device, system)) yield return device;
            yield break;
        }
        if (JoypadDevices.Count > 0)
        {
            foreach (var d in JoypadDevices)
                if (connected.Contains(d)) yield return d;
            yield break;
        }
        if (!ReceivesUnownedJoypads) yield break;
        foreach (var d in connected)
            if (system?.GetJoypadOwner(d) == null) yield return d;
    }

    /// <summary>Whether this player may read or drive a joypad: its own, or an unowned one when it takes unowned pads.</summary>
    public bool MayUseJoypad(int device, EnhancedInput system = null)
    {
        if (JoypadDevices.Contains(device)) return true;
        if (JoypadDevices.Count > 0 || !ReceivesUnownedJoypads) return false;
        system ??= GetParent() as EnhancedInput;
        return system?.GetJoypadOwner(device) == null;
    }
}
