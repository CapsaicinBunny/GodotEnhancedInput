using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>EVT-1 … EVT-4, CUT-3, REC-2, TRK-1, HAP-2, BKD-1: the third review's findings.</summary>
[Collection("Godot")]
public class ReviewThreeTests
{
    // ------------------------------------------------------------------
    //  Time within a frame
    // ------------------------------------------------------------------

    private static (Rig Rig, Func<ulong> Advance) TimedRig(params ActionBinding[] bindings)
    {
        var rig = new Rig().Add(Make.Context("C", bindings));
        ulong now = 1_000_000;
        rig.Player.Clock = () => now;
        rig.Player.UseEventTimes = true;
        rig.Tick();   // the first frame starts the clock
        return (rig, null);
    }

    [Fact]
    public void ALongPressWithinOneSlowFrame_IsNotATap_AndHoldsCountItsRealLength()
    {
        var tap = Make.Action("tap");
        var hold = Make.Action("hold");
        ulong now = 1_000_000;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(tap, Key.Space, new TapTrigger { TapSeconds = 0.2f }),
            Make.Bind(hold, Key.Space, new HoldTrigger { HoldSeconds = 0.3f })));
        rig.Player.Clock = () => now;
        rig.Player.UseEventTimes = true;
        rig.Tick();

        // A 2 fps frame: pressed 50 ms in, released 400 ms later.
        now += 50_000; rig.Press(Key.Space);
        now += 400_000; rig.Release(Key.Space);
        now += 50_000; rig.Tick(1, 0.5);

        rig.Player.WasTriggered(tap).Should().BeFalse("it was held 0.4 s, longer than a tap");
        rig.Player.WasTriggered(hold).Should().BeTrue("it was held past 0.3 s before the release");
        rig.Player.GetState(hold).EndReason.Should().Be(InputEndReason.Released);
    }

    [Fact]
    public void WithoutArrivalTimes_ChangesCountFromTheStartOfTheFrame()
    {
        var tap = Make.Action("tap");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(tap, Key.Space, new TapTrigger { TapSeconds = 0.2f })));
        rig.Player.UseEventTimes.Should().BeFalse("a standalone player is ticked by hand");
        rig.Press(Key.Space).Release(Key.Space).Tick(1, 0.5);
        rig.Player.WasTriggered(tap).Should().BeTrue("both changes are placed at the frame start: a tap");
    }

    [Fact]
    public void AHeldAction_FiresOncePerFrame_WhatEverElseHappens()
    {
        var move = Make.Action("move");
        var click = Make.Action("click");
        int moves = 0, clicks = 0;
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(move, Key.W), Make.Bind(click, Key.J, new PressedTrigger())));
        rig.Player.Bind(move, ActionEvents.Triggered, _ => moves++);
        rig.Player.Bind(click, ActionEvents.Triggered, _ => clicks++);

        rig.Press(Key.W).Tick();
        moves.Should().Be(1);
        for (int i = 0; i < 3; i++) rig.Press(Key.J).Release(Key.J);
        rig.Tick();
        moves.Should().Be(2, "one steady Triggered per frame, however many other events arrived");
        clicks.Should().Be(3, "each click is its own press");
        rig.Player.LastTickSteps.Should().Be(7);
    }

    [Fact]
    public void ACombo_SeesItsStepsInOrder_WithinOneFrame()
    {
        var a = Make.Action("a");
        var b = Make.Action("b");
        var special = Make.Action("special");
        var combo = new ComboTrigger();
        combo.Steps.Add(ComboStep.Create(a));
        combo.Steps.Add(ComboStep.Create(b, timeLimit: 0.5f));
        int fired = 0;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(special, InputSource.Nothing(), combo),
            Make.Bind(a, Key.J, new PressedTrigger()),
            Make.Bind(b, Key.K, new PressedTrigger())));
        rig.Player.Bind(special, ActionEvents.Triggered, _ => fired++);

        rig.Press(Key.K).Press(Key.J).Tick();   // wrong order in one frame
        fired.Should().Be(0);
        rig.Release(Key.K).Release(Key.J).Tick();
        rig.Press(Key.J).Press(Key.K).Tick();   // right order in one frame
        fired.Should().Be(1);
    }

    // ------------------------------------------------------------------
    //  Cut-offs everywhere
    // ------------------------------------------------------------------

    [Fact]
    public void InjectedInput_IsCutOffLikeAnyOther()
    {
        var aim = Make.Action("aim");
        using var rig = new Rig().Add(Make.Context("C"));
        rig.Player.StartInjecting(aim, Vector3.Right);
        rig.Tick();
        rig.Player.IsTriggered(aim).Should().BeTrue();
        rig.Player.ClearRawInput(InputEndReason.FocusLost);
        rig.Tick();
        rig.Player.WasCanceled(aim).Should().BeTrue();
        rig.Player.WasCompleted(aim).Should().BeFalse();
        rig.Player.GetState(aim).EndReason.Should().Be(InputEndReason.FocusLost);
    }

    [Fact]
    public void ATouchTheSystemCanceled_IsNotARelease()
    {
        var tap = Make.Action("tap");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(tap, InputSource.ForTouch(TouchChannel.Pressed), new ReleasedTrigger())));
        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = true });
        rig.Tick();
        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = false, Canceled = true });
        rig.Tick();
        rig.Player.WasTriggered(tap).Should().BeFalse();
        rig.Player.GetState(tap).EndReason.Should().Be(InputEndReason.SystemCanceled);
    }

    [Fact]
    public void AKeyHeldThroughAMenu_DoesNotPressAgainWhenTheMenuCloses_ButHeldActionsGoOn()
    {
        var pause = Make.Action("pause");
        var run = Make.Action("run");
        int pauses = 0;
        var menu = Make.Context("Menu");
        menu.BlockLowerPriority = true;
        using var rig = new Rig().Add(Make.Context("G", Make.Bind(pause, Key.Escape, new PressedTrigger()), Make.Bind(run, Key.Shift)));
        rig.Player.Bind(pause, ActionEvents.Triggered, _ => pauses++);

        rig.Press(Key.Shift).Tick();
        rig.Player.AddContext(menu, 100);
        rig.Press(Key.Escape).Tick(3);
        rig.Player.RemoveContext(menu);
        rig.Tick();
        pauses.Should().Be(0, "Esc went down while the menu hid it");
        rig.Player.IsTriggered(run).Should().BeTrue("a held action simply continues");
        rig.Release(Key.Escape).Tick().Press(Key.Escape).Tick();
        pauses.Should().Be(1);
    }

    // ------------------------------------------------------------------
    //  Recording
    // ------------------------------------------------------------------

    [Fact]
    public void CutOffsAndArrivalMoments_AreRecorded_AndReplayTheSame()
    {
        var tap = Make.Action("tap");
        var run = Make.Action("run");
        ulong now = 1_000_000;
        InputRecording recording;
        var live = new List<string>();
        Func<BindingContext> context = () => Make.Context("C",
            Make.Bind(tap, Key.Space, new TapTrigger { TapSeconds = 0.2f }), Make.Bind(run, Key.Shift));
        using (var rig = new Rig().Add(context()))
        {
            rig.Player.Clock = () => now;
            rig.Player.UseEventTimes = true;
            rig.Tick();
            rig.Player.StartRecording();
            now += 10_000; rig.Press(Key.Space).Press(Key.Shift);
            now += 400_000; rig.Release(Key.Space);
            now += 10_000; rig.Tick(1, 0.42);
            live.Add($"{rig.Player.GetEvents(tap)}|{rig.Player.GetEvents(run)}");
            rig.Player.ClearRawInput(InputEndReason.FocusLost);
            rig.Tick();
            live.Add($"{rig.Player.GetEvents(run)}|{rig.Player.GetState(run).EndReason}");
            recording = InputRecording.FromJson(rig.Player.StopRecording().ToJson());
        }
        recording.Ticks[1].Inputs.Should().Contain(i => i.Kind == RecordedInputKind.ClearAll && i.Reason == InputEndReason.FocusLost);
        recording.Ticks[0].Inputs.Should().Contain(i => i.At > 0.9, "the release arrived near the end of its frame");

        using var replay = new Rig().Add(context());
        replay.Player.StartReplay(recording);
        var replayed = new List<string>();
        replay.Tick();
        replayed.Add($"{replay.Player.GetEvents(tap)}|{replay.Player.GetEvents(run)}");
        replay.Tick();
        replayed.Add($"{replay.Player.GetEvents(run)}|{replay.Player.GetState(run).EndReason}");
        replayed.Should().Equal(live);
        live[0].Should().NotContain("Triggered|", "the 0.4 s press was not a tap, live or replayed");
    }

    [Fact]
    public void AReplayThatEndsWithAKeyDown_ReleasesIt()
    {
        var run = Make.Action("run");
        InputRecording recording;
        using (var rig = new Rig().Add(Make.Context("C", Make.Bind(run, Key.Shift))))
        {
            rig.Player.StartRecording();
            rig.Press(Key.Shift).Tick();
            recording = rig.Player.StopRecording();
        }
        using var replay = new Rig().Add(Make.Context("C", Make.Bind(run, Key.Shift)));
        replay.Player.StartReplay(recording);
        replay.Tick();
        replay.Player.IsTriggered(run).Should().BeTrue();
        replay.Player.IsReplaying.Should().BeFalse();
        replay.Tick();
        replay.Player.IsTriggered(run).Should().BeFalse("nothing stays held after the replay");
        replay.Player.GetState(run).EndReason.Should().Be(InputEndReason.Replay);
    }

    // ------------------------------------------------------------------
    //  Tracked values
    // ------------------------------------------------------------------

    private sealed class PoseBackend : InputBackend
    {
        public override string Name => "poses";
        public override void Attached() => AddDevice("pose", 0, "Pose", "pose-guid");
        public void Track(Quaternion q) => SetTrackedControl("pose", 0, "orientation", q);
        public void Lose() => ClearControl("pose", 0, "orientation");
    }

    [Fact]
    public void ATrackedIdentity_IsInput_AndLosingTrackingCancels()
    {
        var aim = Make.Action("aim", ActionValueKind.Quaternion);
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var backend = new PoseBackend();
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(aim, InputSource.ForDeviceControl("pose", "orientation"))));
            system.AddBackend(backend);
            backend.Track(Quaternion.Identity);
            system.Tick(Rig.Dt);
            player.IsTriggered(aim).Should().BeTrue("the identity is a real, tracked orientation");
            var sample = player.GetSample(aim);
            sample.IsTracked.Should().BeTrue();
            sample.IsValid.Should().BeTrue();
            sample.Value.AsQuaternion().Should().Be(Quaternion.Identity);

            backend.Lose();
            system.Tick(Rig.Dt);
            player.WasCanceled(aim).Should().BeTrue();
            player.GetState(aim).EndReason.Should().Be(InputEndReason.TrackingLost);
            player.GetSample(aim).IsValid.Should().BeFalse("no reading, not the identity");
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Haptics
    // ------------------------------------------------------------------

    private sealed class Output : IHapticsOutput, IAdaptiveTriggerOutput
    {
        public readonly List<string> Log = new();
        public void StartJoypad(int device, float weak, float strong, float seconds) => Log.Add($"start {device} {weak:0.###} {strong:0.###}");
        public void StopJoypad(int device) => Log.Add($"stop {device}");
        public void SetJoypadLight(int device, Color color) => Log.Add($"light {device}");
        public void VibrateHandheld(int milliseconds, float amplitude) { }
        public void StartTriggers(int device, float left, float right, float seconds) => Log.Add($"triggers {device} {left:0.##} {right:0.##}");
        public void SetTriggerResistance(int device, TriggerSide side, TriggerResistance r) => Log.Add($"resist {device} {side} {r.Mode}");
    }

    [Fact]
    public void GivingAPadAway_StopsItsRumble_BeforeTheNewOwnerTakesOver()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            var out0 = new Output();
            p0.HapticsOutput = out0;
            system.AssignJoypad(p0, 0, "pad");
            var engine = p0.Haptics.Play(HapticEffect.Constant(0.5f, 0.5f, 0f), loop: true);
            out0.Log.Should().ContainMatch("start 0 *");

            out0.Log.Clear();
            system.AssignJoypad(p1, 0, "pad");
            out0.Log.Should().Equal("stop 0");
            engine.IsPlaying.Should().BeTrue("an effect on all of the player's pads goes on; there are just none now");
            out0.Log.Clear();
            p0.UpdateHaptics(1.0);
            out0.Log.Should().BeEmpty("player 1 commands that pad now");
        }
        finally { system.Free(); }
    }

    [Fact]
    public void StoppingOnePad_LeavesItOutOfEffectsOnAllPads()
    {
        using var rig = new Rig();
        var output = new Output();
        rig.Player.HapticsOutput = output;
        rig.Player.AcceptTouch = false;
        rig.Player.JoypadDevices.Add(2);
        rig.Player.JoypadDevices.Add(4);
        rig.Player.Haptics.Play(HapticEffect.Engine(0.5f), loop: true);
        output.Log.Clear();
        rig.Player.StopRumble(2);
        output.Log.Should().Contain("stop 2");
        output.Log.Clear();
        rig.Player.UpdateHaptics(1.0);
        output.Log.Should().ContainSingle().Which.Should().StartWith("start 4");
    }

    [Fact]
    public void ASlowRamp_IsSent_OnceItHasMovedEnough()
    {
        using var rig = new Rig();
        var output = new Output();
        rig.Player.HapticsOutput = output;
        rig.Player.AcceptTouch = false;
        rig.Player.JoypadDevices.Add(1);
        rig.Player.HapticRefreshSeconds = 100f;
        var handle = rig.Player.Haptics.Play(HapticEffect.Constant(1f, 1f, 0f), loop: true, strength: 0.5f);
        output.Log.Clear();
        for (int i = 0; i < 5; i++)
        {
            handle.Strength += 0.01f;
            rig.Player.UpdateHaptics(0.01);
        }
        output.Log.Should().NotBeEmpty("0.01 steps add up past the threshold");
    }

    [Fact]
    public void LightsTriggerMotorsAndResistance_GoOnlyToPadsThatHaveThem()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var player = system.GetPlayer(0);
            var output = new Output();
            player.HapticsOutput = output;
            player.AcceptTouch = false;
            system.NotifyJoypadConnection(0, true, "ds", "", "DualSense Wireless Controller");
            system.NotifyJoypadConnection(1, true, "xs", "", "Xbox Series X Controller");
            system.AssignJoypad(player, 0, "ds");
            system.AssignJoypad(player, 1, "xs");

            player.SetJoypadLight(Colors.Red);
            output.Log.Should().Equal("light 0");

            output.Log.Clear();
            player.SetTriggerResistance(TriggerSide.Right, TriggerResistance.Weapon(0.2f, 0.6f, 0.8f));
            output.Log.Should().Equal("resist 0 Right Weapon");

            output.Log.Clear();
            player.Haptics.Play(new HapticEffect { Weak = 0f, Strong = 0f, TriggerRight = 0.7f, Duration = 0.2f, Handheld = false });
            output.Log.Should().Contain(l => l.StartsWith("triggers 1")).And.NotContain(l => l.StartsWith("triggers 0"),
                "impulse triggers are an Xbox feature; the DualSense has resistance instead");
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Backend device identity
    // ------------------------------------------------------------------

    private sealed class WheelBackend : InputBackend
    {
        private readonly string _ns;
        private readonly string _guid;
        public WheelBackend(string ns, string guid = null) { _ns = ns; _guid = guid; }
        public override string Name => "wheels " + _ns;
        public override string Namespace => _ns;
        public InputDeviceInfo Device;
        public override void Attached() => Device = AddDevice("wheel", 0, "Wheel " + _ns, _guid);
        public void Steer(float v) => SetControl("wheel", 0, "steering", v);
        public void Unplug() => RemoveDevice("wheel", 0);
    }

    [Fact]
    public void TwoBackends_WithTheSameTypeAndIndex_StayApart()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var a = new WheelBackend("a");
        var b = new WheelBackend("b");
        try
        {
            var steerA = Make.Action("a", ActionValueKind.Axis1D);
            var player = system.GetPlayer(0);
            system.AddBackend(a);
            system.AddBackend(b);
            a.Device.Id.Should().NotBe(b.Device.Id);
            a.Device.Index.Should().NotBe(b.Device.Index, "the second one gets another runtime index");
            player.AddContext(Make.Context("C", Make.Bind(steerA, InputSource.ForDeviceControl("wheel", "steering", a.Device.Index))));
            b.Steer(1f);
            a.Steer(-0.5f);
            system.Tick(Rig.Dt);
            player.GetValue(steerA).AsFloat().Should().Be(-0.5f);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void AReplacementBackend_TakesTheDeviceOver()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var first = new WheelBackend("first", "wheel-serial-1");
        var second = new WheelBackend("second", "wheel-serial-1");
        try
        {
            system.AddBackend(first);
            var device = first.Device;
            first.Unplug();
            system.RemoveBackend(first);
            system.AddBackend(second);
            second.Device.Should().BeSameAs(device, "the same hardware, recognised by its identity");
            device.Backend.Should().BeSameAs(second);
            device.IsConnected.Should().BeTrue();
        }
        finally { system.Free(); }
    }
}
