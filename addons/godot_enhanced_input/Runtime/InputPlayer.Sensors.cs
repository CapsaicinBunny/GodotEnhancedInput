using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>
    /// When on (default), motion sensors bound in active contexts are read from Godot each tick: the
    /// handheld device's own sensors (for players that accept touch) and the sensors of the joypads this
    /// player uses, which are switched on when first needed. Turn it off to feed them yourself
    /// (<see cref="RawInputState.Set(in SourceKey, Vector3)"/>, tests, replays).
    /// </summary>
    [Export] public bool PollMotionSensors { get; set; } = true;

    private readonly HashSet<SourceKey> _polledSensors = new();

    private void PollSensors(InputSource source)
    {
        if (!PollMotionSensors || _replay != null) return;
        if (source.Kind == SourceKind.Composite)
        {
            foreach (var part in source.Parts)
                if (part != null) PollSensors(part);
            return;
        }
        if (source.Kind != SourceKind.Motion) return;

        if (source.MotionOfHandheld)
        {
            if (!AcceptTouch) return;
            var key = SourceKey.ForMotion(source.MotionSensor, SourceKey.Handheld);
            if (!_polledSensors.Add(key)) return;
            Raw.Set(key, source.MotionSensor switch
            {
                MotionSensor.Accelerometer => Input.GetAccelerometer(),
                MotionSensor.Gyroscope => Input.GetGyroscope(),
                MotionSensor.Gravity => Input.GetGravity(),
                _ => Input.GetMagnetometer(),
            });
            return;
        }

        if (source.MotionSensor == MotionSensor.Magnetometer) return;   // joypads have none
        foreach (var device in JoypadsToRead(source.Device))
        {
            var key = SourceKey.ForMotion(source.MotionSensor, device);
            if (!_polledSensors.Add(key)) continue;
            if (!Input.HasJoyMotionSensors(device)) continue;
            if (!Input.IsJoyMotionSensorsEnabled(device)) Input.SetJoyMotionSensorsEnabled(device, true);
            Raw.Set(key, source.MotionSensor switch
            {
                MotionSensor.Accelerometer => Input.GetJoyAccelerometer(device),
                MotionSensor.Gravity => Input.GetJoyGravity(device),
                _ => Input.GetJoyGyroscope(device),
            });
        }
    }

    /// <summary>The connected joypads this player reads: one device, its own, or (when it takes unowned pads) every connected one.</summary>
    private IEnumerable<int> JoypadsToRead(int device)
    {
        var connected = Input.GetConnectedJoypads();
        if (device >= 0)
        {
            if (connected.Contains(device)) yield return device;
            yield break;
        }
        if (JoypadDevices.Count > 0)
        {
            foreach (var d in JoypadDevices)
                if (connected.Contains(d)) yield return d;
            yield break;
        }
        if (!ReceivesUnownedJoypads) yield break;
        var system = GetParent() as EnhancedInput;
        foreach (var d in connected)
            if (system?.GetJoypadOwner(d) == null) yield return d;
    }
}
