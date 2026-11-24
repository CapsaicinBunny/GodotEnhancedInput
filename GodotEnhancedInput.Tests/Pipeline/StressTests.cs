using System.Diagnostics;
using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>PRF-1 … PRF-7: compiled bindings, reentrancy, cycles, frame-rate extremes, scale.</summary>
[Collection("Godot")]
public class StressTests
{
    [Fact]
    public void Bindings_CompileOnce_AndRecompileOnChanges()
    {
        var jump = Make.Action("jump");
        var fire = Make.Action("fire");
        var context = Make.Context("C", Make.Bind(jump, Key.Space).Rebindable("jump"));
        using var rig = new Rig().Add(context);
        rig.Tick();
        int compiles = rig.Player.PlanCompileCount;
        rig.Tick(30);
        rig.Player.PlanCompileCount.Should().Be(compiles, "nothing changed");

        context.Add(fire, InputSource.ForKey(Key.F));
        rig.Press(Key.F).Tick();
        rig.Player.IsTriggered(fire).Should().BeTrue("added through the context");

        context.Bindings.RemoveAt(1);
        rig.Tick();
        rig.Player.IsTriggered(fire).Should().BeFalse("removal noticed");

        rig.Player.Rebind("jump", InputSource.ForKey(Key.J));
        rig.Press(Key.J).Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue("rebinding recompiles");

        // In-place edits need a nudge.
        context.Bindings[0].Triggers.Add(new PressedTrigger());
        context.NotifyBindingsChanged();
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue("the new trigger sees the held key once");
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeFalse("now a one-tick press");

        var before = rig.Player.PlanCompileCount;
        rig.Player.RefreshBindings();
        rig.Tick();
        rig.Player.PlanCompileCount.Should().Be(before + 1);

        var mode = InputMode.Create("pad", DeviceKinds.Joypad, new[] { "pad" });
        rig.Player.ActiveMode = mode;
        rig.Tick();
        rig.Player.PlanCompileCount.Should().Be(before + 2, "mode change");
    }

    [Fact]
    public void Tick_FromCallbacksAndTriggers_IsIgnored()
    {
        var jump = Make.Action("jump");
        var probe = Make.Action("probe");
        InputPlayer player = null;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(jump, Key.Space),
            Make.Bind(probe, Key.P, new PredicateTrigger { Evaluator = _ => { player.Tick(1.0); return TriggerPhase.Triggered; } })));
        player = rig.Player;
        int triggered = 0;
        rig.Player.Bind(jump, ActionEvents.Triggered, _ => { triggered++; rig.Player.Tick(1.0); });

        rig.Press(Key.Space).Press(Key.P).Tick();
        triggered.Should().Be(1);
        rig.Player.ReentrantTicksIgnored.Should().BeGreaterThanOrEqualTo(2, "the callback and the trigger tried (a trigger reading code runs at every step)");
        rig.Player.GetState(jump).TriggeredSeconds.Should().BeApproximately(Rig.Dt, 1e-9, "no nested tick advanced time");
        rig.Tick();
        triggered.Should().Be(2);
    }

    [Fact]
    public void ChordCycle_DoesNotHangOrThrow()
    {
        var a = Make.Action("a");
        var b = Make.Action("b");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(a, Key.A, new ChordTrigger { ChordAction = b }),
            Make.Bind(b, Key.B, new ChordTrigger { ChordAction = a })));
        var act = () => rig.Press(Key.A).Press(Key.B).Tick(10).Release(Key.A).Tick(3);
        act.Should().NotThrow();
        rig.Player.GetPhase(a).Should().NotBe(TriggerPhase.Triggered, "a cycle can never be satisfied");
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(30.0)]
    [InlineData(240.0)]
    [InlineData(1000.0)]
    public void Hold_TriggersAfterTheSameTime_AtAnyFrameRate(double fps)
    {
        var charge = Make.Action("charge");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(charge, Key.E, new HoldTrigger { HoldSeconds = 0.5f })));
        double dt = 1.0 / fps;
        rig.Press(Key.E);
        double elapsed = 0;
        while (!rig.Player.IsTriggered(charge) && elapsed < 2)
        {
            rig.Player.Tick(dt);
            elapsed += dt;
        }
        elapsed.Should().BeApproximately(0.5, dt + 1e-9);
    }

    [Fact]
    public void ZeroAndNegativeDeltas_DoNotAdvanceTimers()
    {
        var charge = Make.Action("charge");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(charge, Key.E, new HoldTrigger { HoldSeconds = 0.1f })));
        rig.Press(Key.E).Tick(50, 0.0).Tick(5, -1.0);
        rig.Player.IsTriggered(charge).Should().BeFalse();
        rig.Player.GetState(charge).ElapsedSeconds.Should().Be(0);
        rig.TickFor(0.12);
        rig.Player.IsTriggered(charge).Should().BeTrue();
    }

    [Fact]
    public void Tap_OneHugeFrame_IsNotATap()
    {
        var tap = Make.Action("tap");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(tap, Key.T, new TapTrigger { TapSeconds = 0.2f })));
        int taps = 0;
        rig.Player.Bind(tap, ActionEvents.Triggered, _ => taps++);
        rig.Press(Key.T).Tick(1, 0.5).Release(Key.T).Tick(1, 0.5);
        taps.Should().Be(0, "held for half a second");
        rig.Press(Key.T).Tick(1, 0.05).Release(Key.T).Tick(1, 0.05);
        taps.Should().Be(1);
    }

    [Fact]
    public void ReassigningAJoypadMidHold_ReleasesForTheOldOwner_AndDoesNotPressForTheNew()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var jump = Make.Action("jump");
            var context = Make.Context("C", Make.Bind(jump, InputSource.ForJoyButton(JoyButton.A)));
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            p0.AddContext(context);
            p1.AddContext(context);
            system.AssignJoypad(p0, 0, "pad");

            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 0 });
            system.Tick(Rig.Dt);
            p0.IsTriggered(jump).Should().BeTrue();
            p1.IsTriggered(jump).Should().BeFalse();

            system.AssignJoypad(p1, 0, "pad");
            system.Tick(Rig.Dt);
            p0.IsTriggered(jump).Should().BeFalse();
            p0.WasCanceled(jump).Should().BeTrue("reassignment cuts input off");
            p1.IsTriggered(jump).Should().BeFalse("the held button belonged to the old route");

            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false, Device = 0 });
            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 0 });
            system.Tick(Rig.Dt);
            system.Tick(Rig.Dt);
            p1.IsTriggered(jump).Should().BeTrue();
            p0.IsTriggered(jump).Should().BeFalse();
        }
        finally { system.Free(); }
    }

    [Fact]
    public void ManyActionsAndContexts_TickQuickly_WithoutGarbage()
    {
        // 20 contexts × 50 bindings = 1000 bindings over 500 actions, a few of them held.
        var actions = new EnhancedInputAction[500];
        for (int i = 0; i < actions.Length; i++) actions[i] = Make.Action($"a{i}", i % 3 == 0 ? ActionValueKind.Axis2D : ActionValueKind.Digital);
        using var rig = new Rig();
        var keys = new[] { Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H };
        for (int c = 0; c < 20; c++)
        {
            var context = EnhancedInputMappingContext.Create($"c{c}");
            for (int b = 0; b < 50; b++)
            {
                var action = actions[(c * 50 + b) % actions.Length];
                var binding = context.Add(action, InputSource.ForKey(keys[(c + b) % keys.Length]));
                if (b % 4 == 0) binding.Triggers.Add(new HoldTrigger { HoldSeconds = 0.2f });
                if (b % 5 == 0) binding.Modifiers.Add(new ScaleModifier { Scale = new Vector3(2, 2, 2) });
            }
            rig.Add(context, c);
        }
        rig.Press(Key.A).Press(Key.E).Tick(30);   // warm up

        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        const int ticks = 200;
        rig.Tick(ticks);
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        double msPerTick = watch.Elapsed.TotalMilliseconds / ticks;
        Console.WriteLine($"1000 bindings / 500 actions: {msPerTick:F3} ms per tick, {allocated / ticks} bytes per tick");
        rig.Player.PlanCompileCount.Should().Be(1);
        msPerTick.Should().BeLessThan(5.0, "a generous ceiling for slow CI machines");
        (allocated / ticks).Should().BeLessThan(256, "steady-state ticks do not allocate");
    }

    [Fact]
    public void WorstCase_ManyEventsInOneFrame_StaysCheap()
    {
        // The same 1000 bindings; every frame 16 keys go down and up (32 events, each evaluated on its own)
        // while mouse motion and a held key keep the frame step busy.
        var actions = new EnhancedInputAction[500];
        for (int i = 0; i < actions.Length; i++) actions[i] = Make.Action($"a{i}");
        using var rig = new Rig();
        var keys = new[] { Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J, Key.K, Key.L, Key.M, Key.N, Key.O, Key.P };
        for (int c = 0; c < 20; c++)
        {
            var context = EnhancedInputMappingContext.Create($"c{c}");
            for (int b = 0; b < 50; b++)
            {
                var binding = context.Add(actions[(c * 50 + b) % actions.Length], InputSource.ForKey(keys[(c + b) % keys.Length]));
                if (b % 3 == 0) binding.Triggers.Add(new PressedTrigger());
                if (b % 4 == 0) binding.Triggers.Add(new HoldTrigger { HoldSeconds = 0.2f });
            }
            rig.Add(context, c);
        }
        var events = new InputEventKey[keys.Length * 2];
        for (int k = 0; k < keys.Length; k++)
        {
            events[k * 2] = Rig.KeyEvent(keys[k], true);
            events[k * 2 + 1] = Rig.KeyEvent(keys[k], false);
        }
        var motion = new InputEventMouseMotion { Relative = new Vector2(3, 1) };
        void Frame()
        {
            foreach (var e in events) rig.Player.FeedEvent(e);
            rig.Player.FeedEvent(motion);
            rig.Player.Tick(Rig.Dt);
        }
        for (int i = 0; i < 20; i++) Frame();   // warm up

        const int frames = 100;
        var intake = new Stopwatch();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            intake.Start();
            foreach (var e in events) rig.Player.FeedEvent(e);
            rig.Player.FeedEvent(motion);
            intake.Stop();
            rig.Player.Tick(Rig.Dt);
        }
        watch.Stop();
        double msPerFrame = watch.Elapsed.TotalMilliseconds / frames;
        double usPerEvent = intake.Elapsed.TotalMilliseconds * 1000.0 / (frames * (events.Length + 1));
        Console.WriteLine($"worst case, 33 events per frame over 1000 bindings: {msPerFrame:F3} ms per frame, intake {usPerEvent:F2} us per event, {rig.Player.LastTickSteps} steps");
        rig.Player.LastTickSteps.Should().Be(events.Length + 1);
        msPerFrame.Should().BeLessThan(16.0, "a generous ceiling for slow CI machines");
    }
}
