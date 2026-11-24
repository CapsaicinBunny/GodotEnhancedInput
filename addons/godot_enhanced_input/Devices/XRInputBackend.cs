using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Makes XR trackers (controllers, hands, the head) devices of type "xr". Every button, trigger and
/// thumbstick a tracker reports becomes a control with the name XR uses ("trigger", "grip",
/// "primary", "ax_button"…), and every pose a control pair "&lt;pose&gt;/position" and
/// "&lt;pose&gt;/rotation" (Euler angles), plus "&lt;pose&gt;/linear_velocity" and "&lt;pose&gt;/angular_velocity".
/// Bind them with <c>InputSource.ForDeviceControl("xr", "trigger")</c>. Device indices are stable per
/// tracker name; <see cref="IndexOf"/> tells which index a tracker got.
/// Add it with <c>EnhancedInput.Instance.AddBackend(new XRInputBackend())</c>.
/// </summary>
public sealed class XRInputBackend : InputBackend
{
    public const string DeviceType = "xr";

    private readonly Dictionary<string, int> _indices = new();
    private readonly Dictionary<string, XRPositionalTracker> _trackers = new();
    private readonly List<(XRPositionalTracker Tracker, string Name, System.Delegate[] Handlers)> _subscriptions = new();

    /// <summary>Which tracker kinds become devices (controllers by default; add hands or the head as needed).</summary>
    public XRServer.TrackerType TrackerTypes { get; set; } = XRServer.TrackerType.Controller;

    /// <summary>Poses read every tick (most controllers report "default", "aim" and "grip").</summary>
    public string[] Poses { get; set; } = { "default", "aim", "grip" };

    public override string Name => "XR";

    /// <summary>The device index a tracker name maps to (assigned on first sight, then stable).</summary>
    public int IndexOf(string trackerName)
    {
        if (!_indices.TryGetValue(trackerName, out var index))
            _indices[trackerName] = index = _indices.Count;
        return index;
    }

    public override void Attached()
    {
        XRServer.TrackerAdded += OnTrackerAdded;
        XRServer.TrackerRemoved += OnTrackerRemoved;
        foreach (var pair in XRServer.GetTrackers((int)TrackerTypes))
            if (pair.Value.AsGodotObject() is XRPositionalTracker tracker) Add(tracker);
    }

    public override void Detached()
    {
        XRServer.TrackerAdded -= OnTrackerAdded;
        XRServer.TrackerRemoved -= OnTrackerRemoved;
        foreach (var (tracker, _, handlers) in _subscriptions)
        {
            if (!GodotObject.IsInstanceValid(tracker)) continue;
            tracker.ButtonPressed -= (XRPositionalTracker.ButtonPressedEventHandler)handlers[0];
            tracker.ButtonReleased -= (XRPositionalTracker.ButtonReleasedEventHandler)handlers[1];
            tracker.InputFloatChanged -= (XRPositionalTracker.InputFloatChangedEventHandler)handlers[2];
            tracker.InputVector2Changed -= (XRPositionalTracker.InputVector2ChangedEventHandler)handlers[3];
        }
        _subscriptions.Clear();
        _trackers.Clear();
    }

    private void OnTrackerAdded(StringName name, long type)
    {
        if (((int)type & (int)TrackerTypes) == 0) return;
        if (XRServer.GetTracker(name) is XRPositionalTracker tracker) Add(tracker);
    }

    private void OnTrackerRemoved(StringName name, long type)
    {
        string key = name;
        if (!_trackers.Remove(key)) return;
        RemoveDevice(DeviceType, IndexOf(key));
    }

    private void Add(XRPositionalTracker tracker)
    {
        string trackerName = tracker.Name;
        if (_trackers.ContainsKey(trackerName)) return;
        _trackers[trackerName] = tracker;
        int index = IndexOf(trackerName);
        AddDevice(DeviceType, index, trackerName, trackerName);

        XRPositionalTracker.ButtonPressedEventHandler pressed = n => SetControl(DeviceType, index, n, 1f);
        XRPositionalTracker.ButtonReleasedEventHandler released = n => SetControl(DeviceType, index, n, 0f);
        XRPositionalTracker.InputFloatChangedEventHandler floatChanged = (n, v) => SetControl(DeviceType, index, n, (float)v);
        XRPositionalTracker.InputVector2ChangedEventHandler vectorChanged = (n, v) => SetControl(DeviceType, index, n, new Vector3(v.X, v.Y, 0f));
        tracker.ButtonPressed += pressed;
        tracker.ButtonReleased += released;
        tracker.InputFloatChanged += floatChanged;
        tracker.InputVector2Changed += vectorChanged;
        _subscriptions.Add((tracker, trackerName, new System.Delegate[] { pressed, released, floatChanged, vectorChanged }));
    }

    public override void Poll(double delta)
    {
        foreach (var pair in _trackers)
        {
            var tracker = pair.Value;
            if (!GodotObject.IsInstanceValid(tracker)) continue;
            int index = IndexOf(pair.Key);
            foreach (var poseName in Poses)
            {
                if (!tracker.HasPose(poseName)) continue;
                var pose = tracker.GetPose(poseName);
                if (pose == null || !pose.HasTrackingData) continue;
                var transform = pose.Transform;
                SetControl(DeviceType, index, poseName + "/position", transform.Origin);
                SetControl(DeviceType, index, poseName + "/rotation", transform.Basis.GetEuler());
                SetControl(DeviceType, index, poseName + "/linear_velocity", pose.LinearVelocity);
                SetControl(DeviceType, index, poseName + "/angular_velocity", pose.AngularVelocity);
            }
        }
    }

    public override void Rumble(InputDeviceInfo device, float weak, float strong, float seconds)
    {
        var xr = XRServer.PrimaryInterface;
        if (xr == null || device == null) return;
        xr.TriggerHapticPulse("haptic", device.Name, 0.0, Mathf.Max(weak, strong), seconds, 0.0);
    }
}
