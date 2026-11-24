using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Makes XR trackers devices of type "xr": controllers, hands, the headset, the face and eyes (VR, AR
/// and mixed reality alike — whatever the active XR interface reports).
/// <list type="bullet">
/// <item><b>Controllers and the head</b>: every button, trigger and thumbstick as a control with the
/// name XR uses ("trigger", "grip", "primary", "ax_button"…); every pose as "&lt;pose&gt;/position",
/// "&lt;pose&gt;/orientation" (quaternion), "&lt;pose&gt;/rotation" (Euler angles),
/// "&lt;pose&gt;/linear_velocity" and "&lt;pose&gt;/angular_velocity". Eye gaze from the XR runtime is a
/// tracker with a pose too (see <see cref="Poses"/>).</item>
/// <item><b>Hands</b> (hand tracking): "hand/&lt;joint&gt;/position" and "…/orientation" for the wrist, palm and
/// fingertips, "hand/pinch" (thumb to index, 0…1), "hand/grab" (fingers curled, 0…1) and "hand/tracked".</item>
/// <item><b>Face</b>: "eyes/gaze" (where both eyes look, −1…1, right and down positive),
/// "eyes/closed_left", "eyes/closed_right", "eyes/wide", "jaw_open".</item>
/// </list>
/// Bind with <c>InputSource.ForDeviceControl("xr", "trigger")</c> or the path <c>@xr:hand/pinch</c>. Device
/// indices are stable per tracker name (<see cref="IndexOf"/>). Vibration goes to the controller it is
/// played on (per hand) at the effect's frequency. Add it with
/// <c>EnhancedInput.Instance.AddBackend(new XRInputBackend())</c>.
/// </summary>
public sealed class XRInputBackend : InputBackend
{
    public const string DeviceType = "xr";

    private readonly Dictionary<string, int> _indices = new();
    private readonly Dictionary<string, XRTracker> _trackers = new();
    private readonly List<(XRPositionalTracker Tracker, System.Delegate[] Handlers)> _subscriptions = new();

    /// <summary>Which tracker kinds become devices.</summary>
    // Head is named Camera from Godot 4.8 on; the old name still works there and is the only one in 4.7.
#pragma warning disable CS0618
    public XRServer.TrackerType TrackerTypes { get; set; } =
        XRServer.TrackerType.Controller | XRServer.TrackerType.Head | XRServer.TrackerType.Hand | XRServer.TrackerType.Face;
#pragma warning restore CS0618

    /// <summary>Poses read every tick (controllers report "default", "aim", "grip", "palm"; eye gaze is often "eye_gaze_pose").</summary>
    public string[] Poses { get; set; } = { "default", "aim", "grip", "palm", "eye_gaze_pose" };

    /// <summary>Name of the XR action used for vibration.</summary>
    public string HapticAction { get; set; } = "haptic";

    /// <summary>Thumb-to-index distance (metres) that counts as a full pinch, and one that counts as none.</summary>
    public Vector2 PinchRange { get; set; } = new(0.015f, 0.05f);

    /// <summary>Average fingertip-to-palm distance (metres) that counts as a full grab, and one that counts as none.</summary>
    public Vector2 GrabRange { get; set; } = new(0.04f, 0.09f);

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
            if (pair.Value.AsGodotObject() is XRTracker tracker) Add(tracker);
    }

    public override void Detached()
    {
        XRServer.TrackerAdded -= OnTrackerAdded;
        XRServer.TrackerRemoved -= OnTrackerRemoved;
        foreach (var (tracker, handlers) in _subscriptions)
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
        if (XRServer.GetTracker(name) is XRTracker tracker) Add(tracker);
    }

    private void OnTrackerRemoved(StringName name, long type)
    {
        string key = name;
        if (!_trackers.Remove(key)) return;
        for (int i = _subscriptions.Count - 1; i >= 0; i--)
            if (!GodotObject.IsInstanceValid(_subscriptions[i].Tracker) || _subscriptions[i].Tracker.Name == key) _subscriptions.RemoveAt(i);
        RemoveDevice(DeviceType, IndexOf(key));
    }

    private void Add(XRTracker tracker)
    {
        string trackerName = tracker.Name;
        if (_trackers.ContainsKey(trackerName)) return;
        _trackers[trackerName] = tracker;
        int index = IndexOf(trackerName);
        AddDevice(DeviceType, index, trackerName, trackerName);

        if (tracker is not XRPositionalTracker positional || tracker is XRHandTracker) return;
        XRPositionalTracker.ButtonPressedEventHandler pressed = n => SetControl(DeviceType, index, n, 1f);
        XRPositionalTracker.ButtonReleasedEventHandler released = n => SetControl(DeviceType, index, n, 0f);
        XRPositionalTracker.InputFloatChangedEventHandler floatChanged = (n, v) => SetControl(DeviceType, index, n, (float)v);
        XRPositionalTracker.InputVector2ChangedEventHandler vectorChanged = (n, v) => SetControl(DeviceType, index, n, new Vector3(v.X, v.Y, 0f));
        positional.ButtonPressed += pressed;
        positional.ButtonReleased += released;
        positional.InputFloatChanged += floatChanged;
        positional.InputVector2Changed += vectorChanged;
        _subscriptions.Add((positional, new System.Delegate[] { pressed, released, floatChanged, vectorChanged }));
    }

    private static readonly (XRHandTracker.HandJoint Joint, string Name)[] Joints =
    {
        (XRHandTracker.HandJoint.Wrist, "wrist"), (XRHandTracker.HandJoint.Palm, "palm"),
        (XRHandTracker.HandJoint.ThumbTip, "thumb_tip"), (XRHandTracker.HandJoint.IndexFingerTip, "index_tip"),
        (XRHandTracker.HandJoint.MiddleFingerTip, "middle_tip"), (XRHandTracker.HandJoint.RingFingerTip, "ring_tip"),
        (XRHandTracker.HandJoint.PinkyFingerTip, "pinky_tip"),
    };

    public override void Poll(double delta)
    {
        foreach (var pair in _trackers)
        {
            var tracker = pair.Value;
            if (!GodotObject.IsInstanceValid(tracker)) continue;
            int index = IndexOf(pair.Key);
            switch (tracker)
            {
                case XRHandTracker hand: PollHand(hand, index); break;
                case XRFaceTracker face: PollFace(face, index); break;
                case XRPositionalTracker positional: PollPoses(positional, index); break;
            }
        }
    }

    private void PollPoses(XRPositionalTracker tracker, int index)
    {
        foreach (var poseName in Poses)
        {
            if (!tracker.HasPose(poseName)) continue;
            var pose = tracker.GetPose(poseName);
            if (pose == null || !pose.HasTrackingData) continue;
            var transform = pose.Transform;
            var rotation = transform.Basis.GetRotationQuaternion();
            SetControl(DeviceType, index, poseName + "/position", transform.Origin);
            SetControl(DeviceType, index, poseName + "/orientation", rotation);
            SetControl(DeviceType, index, poseName + "/rotation", rotation.GetEuler());
            SetControl(DeviceType, index, poseName + "/linear_velocity", pose.LinearVelocity);
            SetControl(DeviceType, index, poseName + "/angular_velocity", pose.AngularVelocity);
        }
    }

    private void PollHand(XRHandTracker hand, int index)
    {
        bool tracked = hand.HasTrackingData;
        SetControl(DeviceType, index, "hand/tracked", tracked ? 1f : 0f);
        if (!tracked) return;
        foreach (var (joint, name) in Joints)
        {
            var flags = hand.GetHandJointFlags(joint);
            var t = hand.GetHandJointTransform(joint);
            if ((flags & XRHandTracker.HandJointFlags.PositionValid) != 0) SetControl(DeviceType, index, $"hand/{name}/position", t.Origin);
            if ((flags & XRHandTracker.HandJointFlags.OrientationValid) != 0) SetControl(DeviceType, index, $"hand/{name}/orientation", t.Basis.GetRotationQuaternion());
        }
        Vector3 Pos(XRHandTracker.HandJoint j) => hand.GetHandJointTransform(j).Origin;
        float pinchDistance = Pos(XRHandTracker.HandJoint.ThumbTip).DistanceTo(Pos(XRHandTracker.HandJoint.IndexFingerTip));
        SetControl(DeviceType, index, "hand/pinch", Closeness(pinchDistance, PinchRange));
        var palm = Pos(XRHandTracker.HandJoint.Palm);
        float curl = (palm.DistanceTo(Pos(XRHandTracker.HandJoint.IndexFingerTip)) + palm.DistanceTo(Pos(XRHandTracker.HandJoint.MiddleFingerTip))
            + palm.DistanceTo(Pos(XRHandTracker.HandJoint.RingFingerTip)) + palm.DistanceTo(Pos(XRHandTracker.HandJoint.PinkyFingerTip))) * 0.25f;
        SetControl(DeviceType, index, "hand/grab", Closeness(curl, GrabRange));
    }

    /// <summary>1 at or below range.X, 0 at or beyond range.Y, linear in between.</summary>
    private static float Closeness(float distance, Vector2 range)
        => range.Y <= range.X ? (distance <= range.X ? 1f : 0f) : Mathf.Clamp((range.Y - distance) / (range.Y - range.X), 0f, 1f);

    private void PollFace(XRFaceTracker face, int index)
    {
        float B(XRFaceTracker.BlendShapeEntry e) => face.GetBlendShape(e);
        // Each eye looks out (away from the nose) or in; average both eyes into one gaze direction.
        float right = (B(XRFaceTracker.BlendShapeEntry.EyeLookOutRight) - B(XRFaceTracker.BlendShapeEntry.EyeLookInRight)
            + B(XRFaceTracker.BlendShapeEntry.EyeLookInLeft) - B(XRFaceTracker.BlendShapeEntry.EyeLookOutLeft)) * 0.5f;
        float down = (B(XRFaceTracker.BlendShapeEntry.EyeLookDownRight) - B(XRFaceTracker.BlendShapeEntry.EyeLookUpRight)
            + B(XRFaceTracker.BlendShapeEntry.EyeLookDownLeft) - B(XRFaceTracker.BlendShapeEntry.EyeLookUpLeft)) * 0.5f;
        SetControl(DeviceType, index, "eyes/gaze", new Vector3(right, down, 0f));
        SetControl(DeviceType, index, "eyes/closed_left", B(XRFaceTracker.BlendShapeEntry.EyeClosedLeft));
        SetControl(DeviceType, index, "eyes/closed_right", B(XRFaceTracker.BlendShapeEntry.EyeClosedRight));
        SetControl(DeviceType, index, "eyes/wide", Mathf.Max(B(XRFaceTracker.BlendShapeEntry.EyeWideLeft), B(XRFaceTracker.BlendShapeEntry.EyeWideRight)));
        SetControl(DeviceType, index, "jaw_open", B(XRFaceTracker.BlendShapeEntry.JawOpen));
    }

    public override void Rumble(InputDeviceInfo device, float weak, float strong, float seconds)
        => Rumble(device, weak, strong, seconds, 0f);

    public override void Rumble(InputDeviceInfo device, float weak, float strong, float seconds, float frequency)
    {
        var xr = XRServer.PrimaryInterface;
        if (xr == null || device == null) return;
        xr.TriggerHapticPulse(HapticAction, device.Name, frequency, Mathf.Max(weak, strong), seconds, 0.0);
    }
}
