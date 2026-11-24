using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>XR-1 … XR-4, EYE-1 … EYE-2: XR poses as rotations, hands, face, eye trackers, dwell.</summary>
[Collection("Godot")]
public class XRAndEyeTests
{
    private sealed class FakeEyes : IEyeTracker
    {
        public string Name => "Fake eyes";
        public bool IsAvailable { get; set; } = true;
        public bool IsUserPresent => true;
        public Vector2? Gaze = new Vector2(0.25f, 0.5f);
        public bool TryGetGaze(out Vector2 screen) { screen = Gaze ?? Vector2.Zero; return Gaze.HasValue; }
        public bool TryGetHead(out Vector3 position, out Quaternion rotation)
        {
            position = new Vector3(0, 0, 0.6f);
            rotation = Quaternion.FromEuler(new Vector3(0, 0.3f, 0));
            return true;
        }
    }

    [Fact]
    public void XRPose_GivesQuaternionAndEuler()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var tracker = new XRControllerTracker { Name = "gei_xr_right", Type = XRServer.TrackerType.Controller };
        var backend = new XRInputBackend();
        try
        {
            var aim = Make.Action("aim", ActionValueKind.Quaternion);
            var tilt = Make.Action("tilt", ActionValueKind.Euler);
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C",
                Make.Bind(aim, InputSourcePath.Parse("@xr:aim/orientation")),
                Make.Bind(tilt, InputSourcePath.Parse("@xr:aim/orientation"), new RotationConvertModifier { Conversion = RotationConversion.QuaternionToEuler })));
            system.AddBackend(backend);
            XRServer.AddTracker(tracker);

            var rotation = Quaternion.FromEuler(new Vector3(0.2f, 0.5f, -0.1f));
            tracker.SetPose("aim", new Transform3D(new Basis(rotation), Vector3.Zero), Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
            system.Tick(Rig.Dt);

            var q = player.GetValue(aim).AsQuaternion();
            q.IsEqualApprox(rotation).Should().BeTrue($"{q} vs {rotation}");
            player.GetValue(tilt).AsEuler().DistanceTo(new Vector3(0.2f, 0.5f, -0.1f)).Should().BeLessThan(1e-4f);
            player.IsTriggered(aim).Should().BeTrue("a tracked rotation away from rest is input");
        }
        finally
        {
            XRServer.RemoveTracker(tracker);
            system.Free();
        }
    }

    [Fact]
    public void XRHand_PinchAndGrab()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var hand = new XRHandTracker { Name = "/user/hand_tracker/gei_left", Type = XRServer.TrackerType.Hand, HasTrackingData = true };
        var backend = new XRInputBackend();
        try
        {
            var pinch = Make.Action("pinch", ActionValueKind.Axis1D);
            var grab = Make.Action("grab", ActionValueKind.Axis1D);
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C",
                Make.Bind(pinch, InputSourcePath.Parse("@xr:hand/pinch")),
                Make.Bind(grab, InputSourcePath.Parse("@xr:hand/grab"))));
            system.AddBackend(backend);
            XRServer.AddTracker(hand);

            void Joint(XRHandTracker.HandJoint joint, Vector3 at)
            {
                hand.SetHandJointTransform(joint, new Transform3D(Basis.Identity, at));
                hand.SetHandJointFlags(joint, XRHandTracker.HandJointFlags.PositionValid | XRHandTracker.HandJointFlags.OrientationValid);
            }
            Joint(XRHandTracker.HandJoint.Palm, Vector3.Zero);
            Joint(XRHandTracker.HandJoint.Wrist, new Vector3(0, -0.05f, 0));
            Joint(XRHandTracker.HandJoint.ThumbTip, new Vector3(0.03f, 0.05f, 0));
            Joint(XRHandTracker.HandJoint.IndexFingerTip, new Vector3(0.04f, 0.05f, 0));   // 1 cm apart: full pinch
            foreach (var tip in new[] { XRHandTracker.HandJoint.MiddleFingerTip, XRHandTracker.HandJoint.RingFingerTip, XRHandTracker.HandJoint.PinkyFingerTip })
                Joint(tip, new Vector3(0, 0.1f, 0));                                        // fingers open
            system.Tick(Rig.Dt);

            player.GetValue(pinch).AsFloat().Should().Be(1f);
            player.GetValue(grab).AsFloat().Should().BeLessThan(0.5f);
            system.GetDevice($"xr:{backend.IndexOf("/user/hand_tracker/gei_left")}").Role.Should().Be(DeviceRole.XRHand);
        }
        finally
        {
            XRServer.RemoveTracker(hand);
            system.Free();
        }
    }

    [Fact]
    public void XRFace_EyeGazeAndBlinks()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var face = new XRFaceTracker { Name = "/user/face_tracker/gei", Type = XRServer.TrackerType.Face };
        var backend = new XRInputBackend();
        try
        {
            var gaze = Make.Action("gaze", ActionValueKind.Axis2D);
            var blink = Make.Action("blink");
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C",
                Make.Bind(gaze, InputSourcePath.Parse("@xr:eyes/gaze")),
                Make.Bind(blink, InputSourcePath.Parse("@xr:eyes/closed_left"))));
            system.AddBackend(backend);
            XRServer.AddTracker(face);
            face.SetBlendShape(XRFaceTracker.BlendShapeEntry.EyeLookOutRight, 0.6f);
            face.SetBlendShape(XRFaceTracker.BlendShapeEntry.EyeLookInLeft, 0.6f);
            face.SetBlendShape(XRFaceTracker.BlendShapeEntry.EyeClosedLeft, 1f);
            system.Tick(Rig.Dt);

            player.GetValue(gaze).AsVector2().X.Should().BeApproximately(0.6f, 1e-5f, "both eyes look right");
            player.IsTriggered(blink).Should().BeTrue();
        }
        finally
        {
            XRServer.RemoveTracker(face);
            system.Free();
        }
    }

    [Fact]
    public void EyeTracker_GazeHeadAndPresence_AndDisconnects()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var eyes = new FakeEyes();
        var backend = new EyeTrackerBackend(eyes);
        try
        {
            var look = Make.Action("look", ActionValueKind.Axis2D);
            var head = Make.Action("head", ActionValueKind.Quaternion);
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C",
                Make.Bind(look, InputSourcePath.Parse("@eye:gaze_centered")),
                Make.Bind(head, InputSourcePath.Parse("@eye:head/orientation"))));
            system.AddBackend(backend);
            system.Tick(Rig.Dt);

            player.GetValue(look).AsVector2().Should().Be(new Vector2(-0.5f, 0f));
            player.GetValue(head).AsEuler().Y.Should().BeApproximately(0.3f, 1e-4f);
            var device = system.GetDevice("eye:0");
            device.Role.Should().Be(DeviceRole.EyeTracker);
            device.IsConnected.Should().BeTrue();

            eyes.IsAvailable = false;
            system.Tick(Rig.Dt);
            device.IsConnected.Should().BeFalse();
            system.Tick(Rig.Dt);
            player.GetValue(look).AsVector2().Should().Be(Vector2.Zero);
            player.GetState(look).EndReason.Should().Be(InputEndReason.DeviceDisconnected);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void Dwell_SelectsWhereTheGazeRests()
    {
        var select = Make.Action("select");
        int selections = 0;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(select, InputSource.ForVirtual("gaze"), new DwellTrigger { Radius = 0.05f, DwellSeconds = 0.5f })));
        rig.Player.Bind(select, ActionEvents.Triggered, _ => selections++);

        rig.Player.SetVirtualControl("gaze", new Vector2(0.3f, 0.3f));
        rig.TickFor(0.3);
        rig.Player.SetVirtualControl("gaze", new Vector2(0.6f, 0.3f));   // glanced away: start over
        rig.TickFor(0.3);
        selections.Should().Be(0);
        rig.Player.SetVirtualControl("gaze", new Vector2(0.61f, 0.31f)); // small jitter stays
        rig.TickFor(0.3);
        selections.Should().Be(1);
        rig.TickFor(1.0);
        selections.Should().Be(1, "once until the gaze moves");
    }
}
