using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>API-1 … API-8: shortcuts, ids, configurations, provenance, evaluation policies, scopes, layouts.</summary>
[Collection("Godot")]
public class ApiTests(EngineFixture fixture)
{
    // ------------------------------------------------------------------
    //  Shortcuts
    // ------------------------------------------------------------------

    [Fact]
    public void ActionsAnswerForThemselves_AndContextsBindFromPaths()
    {
        var jump = GameAction.Button("jump");
        var move = GameAction.Axis2D("move");
        var charge = GameAction.Button("charge");
        var gameplay = BindingContext.Create("gameplay")
            .Bind(move, "wasd", "pad:left_stick")
            .Bind(jump, "key:space", "pad:a");
        gameplay.Add(charge, "key:f").Hold(0.3f);
        using var rig = new Rig().Add(gameplay);
        var player = rig.Player;
        int jumps = 0;
        jump.OnTriggered(() => jumps++, player);

        rig.Press(Key.Space).Press(Key.D).Press(Key.F).Tick();
        jump.WasPressed(player).Should().BeTrue();
        jump.IsTriggered(player).Should().BeTrue();
        move.ReadVector2(player).Should().Be(new Vector2(1, 0));
        jumps.Should().Be(1);
        charge.IsTriggered(player).Should().BeFalse("held 1/60 s of 0.3 s");
        rig.TickFor(0.3);
        charge.IsTriggered(player).Should().BeTrue();

        rig.Release(Key.Space).Tick();
        jump.WasReleased(player).Should().BeTrue();
        player.ReadVector2(move).Should().Be(new Vector2(1, 0));
    }

    [Fact]
    public void ScopesAndHandles_ReleaseWhatTheyHold()
    {
        var jump = GameAction.Button("jump");
        var vehicle = BindingContext.Create("vehicle").Bind(jump, "key:space");
        using var rig = new Rig();
        var owner = new Node();
        fixture.Tree.Root.AddChild(owner);
        int calls = 0;

        var scope = rig.Player.CreateScope(owner);
        var handle = scope.PushContext(vehicle, 10);
        scope.OnTriggered(jump, () => calls++);
        rig.Player.HasContext(vehicle).Should().BeTrue();
        rig.Press(Key.Space).Tick();
        calls.Should().Be(1);

        owner.QueueFree();
        fixture.Tree.Root.RemoveChild(owner);   // leaves the tree: the scope ends
        scope.IsActive.Should().BeFalse();
        handle.IsActive.Should().BeFalse();
        rig.Player.HasContext(vehicle).Should().BeFalse();
        rig.Release(Key.Space).Tick().Press(Key.Space).Tick();
        calls.Should().Be(1, "the subscription ended with the scope");
    }

    [Fact]
    public void ASingleAction_CanBeTurnedOff()
    {
        var fire = GameAction.Button("fire");
        using var rig = new Rig().Add(BindingContext.Create("C").Bind(fire, "mouse:left"));
        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        rig.Tick();
        rig.Player.SetActionEnabled(fire, false);
        rig.Tick();
        rig.Player.WasCanceled(fire).Should().BeTrue();
        rig.Player.GetState(fire).EndReason.Should().Be(InputEndReason.Blocked);
        rig.Player.IsActionEnabled(fire).Should().BeFalse();
        rig.Player.SetActionEnabled(fire, true);
        rig.Tick();
        rig.Player.IsTriggered(fire).Should().BeTrue("held all along: it continues");
    }

    // ------------------------------------------------------------------
    //  Ids and configurations
    // ------------------------------------------------------------------

    [Fact]
    public void Ids_SurviveRenames_AndFormerIdsStillResolve()
    {
        var jump = GameAction.Button("jump");
        jump.Id.Should().Be("jump", "an action without an id goes by its name");
        jump.Id = "act.jump";
        jump.Name = "Hop";
        jump.FormerIds = new[] { "jump" };
        using var rig = new Rig().Add(BindingContext.Create("C").Bind(jump, "key:space"));
        rig.Player.FindAction("act.jump").Should().BeSameAs(jump);
        rig.Player.FindAction("Hop").Should().BeSameAs(jump);
        rig.Player.FindAction("jump").Should().BeSameAs(jump, "a former id");

        // A recording made under the id still plays after the display name changes again.
        rig.Player.StartRecording();
        rig.Player.InjectInput(jump, true);
        rig.Tick();
        var recording = rig.Player.StopRecording();
        recording.Ticks[0].Inputs[0].Name.Should().Be("act.jump");
        jump.Name = "Leap";
        using var replay = new Rig().Add(BindingContext.Create("C").Bind(jump, "key:space"));
        replay.Player.StartReplay(InputRecording.FromJson(recording.ToJson()));
        replay.Tick();
        replay.Player.WasTriggered(jump).Should().BeTrue();
    }

    [Fact]
    public void AConfiguration_SetsUpAPlayer()
    {
        var move = GameAction.Axis2D("move");
        var steer = GameAction.Axis("steer");
        var onFoot = BindingContext.Create("on_foot").Bind(move, "wasd");
        var vehicle = BindingContext.Create("vehicle").Bind(steer, "keys:d/a");
        vehicle.DefaultPriority = 50;
        vehicle.Id = "ctx.vehicle";
        var keyboard = InputMode.Create("keyboard", DeviceKinds.KeyboardMouse, new[] { "kbm" });
        var pad = InputMode.Create("gamepad", DeviceKinds.Joypad, new[] { "pad" });
        var config = new InputConfiguration { Name = "game" };
        config.Actions.Add(move);
        config.Actions.Add(steer);
        config.Contexts.Add(onFoot);
        config.Contexts.Add(vehicle);
        config.StartupContexts.Add(onFoot);
        config.Modes.Add(keyboard);
        config.Modes.Add(pad);
        config.DefaultMode = pad;
        config.DefaultSettings["look_sensitivity"] = 2.5;

        using var rig = new Rig();
        rig.Player.LoadConfiguration(config);
        rig.Player.HasContext(onFoot).Should().BeTrue("startup contexts are activated");
        rig.Player.ActiveMode.Should().BeSameAs(pad);
        rig.Player.GetSetting("look_sensitivity").Should().Be(2.5);
        rig.Player.FindAction("steer").Should().BeSameAs(steer, "known from the configuration before any context binds it");

        rig.Player.ActivateContext("ctx.vehicle").Should().BeTrue();
        rig.Player.GetContextPriority(vehicle).Should().Be(50);
        rig.Player.DeactivateContext("vehicle").Should().BeTrue("by name too");
        rig.Player.ActivateContext("nope").Should().BeFalse();
        config.FindContext("ctx.vehicle").Should().BeSameAs(vehicle);
    }

    [Fact]
    public void TheAutoloadsConfiguration_ReachesEveryPlayer()
    {
        var onFoot = BindingContext.Create("on_foot");
        var config = new InputConfiguration();
        config.StartupContexts.Add(onFoot);
        var system = new EnhancedInput { TickMode = TickMode.Manual, Configuration = config };
        try
        {
            system.GetPlayer(0).HasContext(onFoot).Should().BeTrue();
            system.AddPlayer().HasContext(onFoot).Should().BeTrue("players added later get it too");
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Provenance
    // ------------------------------------------------------------------

    [Fact]
    public void Events_SayWhichBindingChannelAndDeviceCausedThem()
    {
        var fire = GameAction.Button("fire");
        var context = BindingContext.Create("C");
        var mouse = context.Add(fire, "mouse:left");
        var pad = context.Add(fire, "pad:rt");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(context);
            system.NotifyJoypadConnection(2, true, "x", "", "Xbox Series X Controller");
            var seen = new List<ActionEventArgs>();
            player.Bind(fire, ActionEvents.Started | ActionEvents.Completed, e => seen.Add(e));

            system.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            system.Tick(Rig.Dt);
            seen[0].Binding.Should().BeSameAs(mouse);
            seen[0].Source.Value.Kind.Should().Be(RawKind.MouseButton);
            seen[0].Device.Id.Should().Be("keyboard_mouse");
            system.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            system.Tick(Rig.Dt);
            seen[1].EndReason.Should().Be(InputEndReason.Released);

            seen.Clear();
            system.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1f, Device = 2 });
            system.Tick(Rig.Dt);
            seen[0].Binding.Should().BeSameAs(pad);
            seen[0].Device.Should().BeSameAs(system.GetJoypadDevice(2));
            seen[0].Source.Value.Device.Should().Be(2);
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Evaluation policies
    // ------------------------------------------------------------------

    private static int CountTriggered(ActionEvaluation policy, Action<Rig> drive)
    {
        var action = GameAction.Axis("a");
        action.Evaluation = policy;
        using var rig = new Rig().Add(BindingContext.Create("C").Bind(action, "keys:d/a", "virtual:v"));
        int count = 0;
        rig.Player.Bind(action, ActionEvents.Triggered, _ => count++);
        drive(rig);
        return count;
    }

    [Fact]
    public void Policies_DecideHowAHeldActionReports()
    {
        void Hold(Rig rig) { rig.Press(Key.D).Tick(); rig.Tick(2); }
        CountTriggered(ActionEvaluation.Continuous, Hold).Should().Be(3, "every frame while held");
        CountTriggered(ActionEvaluation.EventDriven, Hold).Should().Be(1, "only the press");
        CountTriggered(ActionEvaluation.OnChange, rig =>
        {
            rig.Player.SetVirtualControl("v", 0.5f);
            rig.Tick(2);
            rig.Player.SetVirtualControl("v", 0.8f);
            rig.Tick(2);
        }).Should().Be(2, "only when the value changed");
    }

    [Fact]
    public void PassThrough_ReportsEachControl_WithItsOwnValueAndSource()
    {
        var paint = GameAction.Axis("paint");
        paint.Evaluation = ActionEvaluation.PassThrough;
        var context = BindingContext.Create("C");
        var left = context.Add(paint, "virtual:left");
        var right = context.Add(paint, "virtual:right");
        using var rig = new Rig().Add(context);
        var seen = new List<(ActionBinding Binding, float Value)>();
        rig.Player.Bind(paint, ActionEvents.Triggered, e => seen.Add((e.Binding, e.Value.AsFloat())));

        rig.Player.SetVirtualControl("right", 0.9f);
        rig.Player.SetVirtualControl("left", 0.3f);
        rig.Tick();
        seen.Should().Equal((right, 0.9f), (left, 0.3f));
    }

    private sealed class PoseBackend : InputBackend
    {
        public override string Name => "poses";
        public override void Attached() => AddDevice("pose", 0, "Pose", "pose-guid");
        public void Track(Quaternion q) => SetTrackedControl("pose", 0, "orientation", q);
    }

    [Fact]
    public void Tracked_IsTriggeredExactlyWhileTracked()
    {
        var aim = GameAction.Rotation("aim");
        aim.Evaluation = ActionEvaluation.Tracked;
        aim.Triggers.Add(new PressedTrigger());   // would otherwise make it a one-shot
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var backend = new PoseBackend();
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(BindingContext.Create("C").Bind(aim, "@pose:orientation"));
            system.AddBackend(backend);
            backend.Track(Quaternion.Identity);
            system.Tick(Rig.Dt);
            system.Tick(Rig.Dt);
            player.IsTriggered(aim).Should().BeTrue("tracked, whatever the triggers and the value");
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Device layouts
    // ------------------------------------------------------------------

    [Fact]
    public void Layouts_DescribeControls_AndEveryPathBinds()
    {
        var full = InputDeviceLayout.Gamepad(ControllerFeatures.MotionSensors | ControllerFeatures.Touchpad | ControllerFeatures.BackButtons | ControllerFeatures.ShareButton);
        var stick = full.FindControl("left_stick");
        stick.ValueKind.Should().Be(ActionValueKind.Axis2D);
        (stick.Minimum, stick.Maximum).Should().Be((-1f, 1f));
        stick.IsNoisy.Should().BeTrue();
        full.FindControl("gyroscope").Unit.Should().Be("rad/s");
        full.FindControl("touchpad").Should().NotBeNull();
        InputDeviceLayout.Gamepad().FindControl("touchpad").Should().BeNull("a plain pad has none");

        foreach (var layout in new[] { full, InputDeviceLayout.KeyboardMouse, InputDeviceLayout.Touch, InputDeviceLayout.Pen })
            foreach (var control in layout.Controls)
                InputSourcePath.TryParse(control.Path).Should().NotBeNull($"{layout.Name}: {control.Path} must bind");

        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            system.NotifyJoypadConnection(0, true, "ds", "", "DualSense Wireless Controller");
            system.GetJoypadDevice(0).Layout.FindControl("touchpad").Should().NotBeNull("a DualSense has a touchpad");
            system.GetDevice("keyboard_mouse").Layout.FindControl("space").Path.Should().Be("key:space");
        }
        finally { system.Free(); }
    }
}
