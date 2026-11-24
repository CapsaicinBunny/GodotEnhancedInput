using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>ANA-1, TRK-2, HAP-3, DEV-10, BKD-2: the fourth review's findings.</summary>
[Collection("Godot")]
public class ReviewFourTests
{
    private static InputEventJoypadMotion Axis(float value) => new() { Axis = JoyAxis.TriggerRight, AxisValue = value, Device = 0 };

    // ------------------------------------------------------------------
    //  Analog transitions within a frame
    // ------------------------------------------------------------------

    [Fact]
    public void ATriggerSqueezedPastAThresholdAndBack_WithinOneFrame_IsSeen()
    {
        var fire = Make.Action("fire", ActionValueKind.Axis1D);
        int fired = 0;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(fire, InputSource.ForJoyAxis(JoyAxis.TriggerRight), new ThresholdTrigger { Threshold = 0.5f })));
        rig.Player.Bind(fire, ActionEvents.Triggered, _ => fired++);

        rig.Player.FeedEvent(Axis(0.2f));
        rig.Tick();
        rig.Player.FeedEvent(Axis(0.6f));
        rig.Player.FeedEvent(Axis(0.9f));   // the peak
        rig.Player.FeedEvent(Axis(0.2f));
        rig.Tick();
        fired.Should().Be(1, "0.2 → 0.9 → 0.2 crossed 0.5 even though it never crossed zero");
        rig.Player.GetValue(fire).AsFloat().Should().BeApproximately(0.9f, 1e-6f, "a one-shot keeps the value it fired with to the frame's end");
        rig.Player.GetState(fire).InputValue.AsFloat().Should().BeApproximately(0.2f, 1e-6f, "the untouched input is where the trigger is now");
    }

    [Fact]
    public void AnalogSampling_Latest_CoalescesAndAll_KeepsEverySample()
    {
        var fire = Make.Action("fire", ActionValueKind.Axis1D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(fire, InputSource.ForJoyAxis(JoyAxis.TriggerRight), new ThresholdTrigger { Threshold = 0.5f })));
        rig.Player.FeedEvent(Axis(0.2f));
        rig.Tick();

        rig.Player.AnalogSampling = AnalogSampling.Latest;
        rig.Player.FeedEvent(Axis(0.9f));
        rig.Player.FeedEvent(Axis(0.2f));
        rig.Tick();
        rig.Player.WasTriggered(fire).Should().BeFalse("only the latest value counts");

        rig.Player.AnalogSampling = AnalogSampling.All;
        foreach (var v in new[] { 0.3f, 0.4f, 0.45f }) rig.Player.FeedEvent(Axis(v));
        rig.Tick();
        rig.Player.LastTickSteps.Should().Be(4, "three samples, each on its own, then the frame");
    }

    [Fact]
    public void RestingNoise_DoesNotCreateSteps()
    {
        var fire = Make.Action("fire", ActionValueKind.Axis1D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(fire, InputSource.ForJoyAxis(JoyAxis.TriggerRight))));
        rig.Player.FeedEvent(Axis(0.30f));
        rig.Tick();
        foreach (var v in new[] { 0.31f, 0.30f, 0.31f, 0.305f, 0.30f }) rig.Player.FeedEvent(Axis(v));
        rig.Tick();
        rig.Player.LastTickSteps.Should().Be(1, "wobbles within the band are not turning points");
    }

    // ------------------------------------------------------------------
    //  Tracking gained and lost on the event timeline
    // ------------------------------------------------------------------

    private sealed class PositionBackend : InputBackend
    {
        public override string Name => "positions";
        public override void Attached() => AddDevice("tracker", 0, "Tracker", "tracker-guid");
        public void Track(Vector3 p) => SetTrackedControl("tracker", 0, "position", p);
        public void Lose() => ClearControl("tracker", 0, "position");
    }

    [Fact]
    public void APositionAtTheOrigin_GainsAndLosesTracking_AsQueuedChanges()
    {
        var where = Make.Action("where", ActionValueKind.Axis3D);
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var backend = new PositionBackend();
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(where, InputSource.ForDeviceControl("tracker", "position"))));
            system.AddBackend(backend);
            var recording = player.StartRecording();

            backend.Track(Vector3.Zero);
            system.Tick(Rig.Dt);
            player.IsTriggered(where).Should().BeTrue("the origin is a tracked reading");
            player.GetSample(where).IsValid.Should().BeTrue();

            backend.Lose();
            system.Tick(Rig.Dt);
            player.WasCanceled(where).Should().BeTrue("losing tracking at the origin is still a change");
            player.GetState(where).EndReason.Should().Be(InputEndReason.TrackingLost);
            player.StopRecording();
            recording.Ticks[1].Inputs.Should().Contain(i => i.Kind == RecordedInputKind.Cancel && i.Reason == InputEndReason.TrackingLost);

            // Gained and lost within one frame: both happen, in order.
            var events = new List<ActionEvents>();
            player.Bind(where, ActionEvents.All, e => events.Add(e.Event));
            backend.Track(Vector3.Zero);
            backend.Lose();
            system.Tick(Rig.Dt);
            events.Should().Equal(ActionEvents.Started, ActionEvents.Triggered, ActionEvents.Canceled);
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Persistent outputs follow ownership and settings
    // ------------------------------------------------------------------

    private sealed class Output : IHapticsOutput, IAdaptiveTriggerOutput, IForceFeedbackOutput
    {
        public readonly List<string> Log = new();
        public void StartJoypad(int device, float weak, float strong, float seconds) => Log.Add($"start {device}");
        public void StopJoypad(int device) => Log.Add($"stop {device}");
        public void SetJoypadLight(int device, Color color) { }
        public void VibrateHandheld(int milliseconds, float amplitude) { }
        public void StartTriggers(int device, float left, float right, float seconds) { }
        public void SetTriggerResistance(int device, TriggerSide side, TriggerResistance r) => Log.Add($"resist {device} {side} {r.Mode} {r.Strength:0.##}");
        public void SetForceFeedback(int device, ForceFeedback f) => Log.Add($"force {device} {f.Spring:0.##}");
    }

    [Fact]
    public void ResistanceAndForces_AreReleasedOnTransfer_AndFollowTheStrengthSettings()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            var output = new Output();
            p0.HapticsOutput = output;
            system.NotifyJoypadConnection(0, true, "ds", "", "DualSense Wireless Controller");
            system.AssignJoypad(p0, 0, "ds");
            p0.SetTriggerResistance(TriggerSide.Right, TriggerResistance.Feedback(0.3f, 0.8f));
            p0.SetForceFeedback(new ForceFeedback { Spring = 0.6f });

            output.Log.Clear();
            p0.Profile.SetSetting("vibration_strength", 0.5);
            p0.Profile.SetSetting("force_feedback_strength", 0.5);
            p0.UpdateHaptics(Rig.Dt);
            output.Log.Should().Contain("resist 0 Right Feedback 0.4").And.Contain("force 0 0.3", "a comfort setting applies at once");

            output.Log.Clear();
            system.AssignJoypad(p1, 0, "ds");
            output.Log.Should().Contain("resist 0 Left Off 0").And.Contain("resist 0 Right Off 0").And.Contain("force 0 0",
                "the next owner starts from a free trigger and wheel");
        }
        finally { system.Free(); }
    }

    [Fact]
    public void RemovingAPlayer_AndLosingFocus_ReleaseEverything()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            var out0 = new Output();
            var out1 = new Output();
            p0.HapticsOutput = out0;
            p1.HapticsOutput = out1;
            system.AssignJoypad(p0, 0, "a");
            system.AssignJoypad(p1, 1, "b");
            p0.Rumble(0.5f, 0.5f, 0f);
            p1.SetForceFeedback(ForceFeedback.Centering());

            out1.Log.Clear();
            system.RemovePlayer(p1);
            out1.Log.Should().Contain("force 1 0");

            out0.Log.Clear();
            system._Notification((int)Node.NotificationApplicationFocusOut);
            out0.Log.Should().Contain("stop 0");
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Custom devices: implicit recipients, identity
    // ------------------------------------------------------------------

    private sealed class WheelBackend : InputBackend
    {
        private readonly string _ns;
        public WheelBackend(string ns) => _ns = ns;
        public override string Name => "wheel " + _ns;
        public override string Namespace => _ns;
        public InputDeviceInfo Device;
        public override void Attached() => Device = AddDevice("wheel", 0, "Wheel", null);
        public void Steer(float v) => SetControl("wheel", 0, "steering", v);
        public void Unplug() => RemoveDevice("wheel", 0);
    }

    [Fact]
    public void AssigningAnUnownedDevice_CutsOffWhoeverWasUsingIt()
    {
        var steer = Make.Action("steer", ActionValueKind.Axis1D);
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var backend = new WheelBackend("w");
        try
        {
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            foreach (var p in new[] { p0, p1 }) p.AddContext(Make.Context("C", Make.Bind(steer, InputSource.ForDeviceControl("wheel", "steering"))));
            system.AddBackend(backend);
            backend.Steer(0.8f);
            system.Tick(Rig.Dt);
            p0.IsTriggered(steer).Should().BeTrue("player 0 takes devices nobody owns");

            system.AssignDevice(p1, backend.Device);
            system.Tick(Rig.Dt);
            p0.IsTriggered(steer).Should().BeFalse("nothing stays held for the previous user");
            p0.GetState(steer).EndReason.Should().Be(InputEndReason.DeviceReassigned);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void ThreeBackendsInOneNamespace_GetThreeIds_AndRemovedDevicesAreIgnored()
    {
        var steer = Make.Action("steer", ActionValueKind.Axis1D);
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var a = new WheelBackend("same");
        var b = new WheelBackend("same");
        var c = new WheelBackend("same");
        try
        {
            system.AddBackend(a);
            system.AddBackend(b);
            system.AddBackend(c);
            new[] { a.Device.Id, b.Device.Id, c.Device.Id }.Should().OnlyHaveUniqueItems();
            new[] { a.Device.Index, b.Device.Index, c.Device.Index }.Should().OnlyHaveUniqueItems();

            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(steer, InputSource.ForDeviceControl("wheel", "steering", b.Device.Index))));
            b.Unplug();
            b.Steer(1f);   // a removed device still talking
            system.Tick(Rig.Dt);
            player.GetValue(steer).AsFloat().Should().Be(0f);
        }
        finally { system.Free(); }
    }
}
