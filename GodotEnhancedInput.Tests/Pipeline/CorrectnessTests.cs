using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>COR-1 … COR-9: ordering within a frame, cut-off input, isolation, memory invalidation.</summary>
[Collection("Godot")]
public class CorrectnessTests
{
    private sealed class TwoDeviceBackend : InputBackend
    {
        public override string Name => "rig";
        public override void Attached()
        {
            AddDevice("wheel", 0, "Rig Wheel", "wheel-guid");
            AddDevice("pedals", 0, "Rig Pedals", "pedals-guid");
        }
        public void Steer(float v) => SetControl("wheel", 0, "steering", v);
    }

    [Fact]
    public void ReleaseTrigger_DoesNotFire_WhenInputIsCutOff()
    {
        var grenade = Make.Action("throw");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(grenade, Key.G, new ReleasedTrigger())));
            system.FeedEvent(Rig.KeyEvent(Key.G, true));
            system.Tick(Rig.Dt);
            player.GetPhase(grenade).Should().Be(TriggerPhase.Ongoing);

            system._Notification((int)Node.NotificationApplicationFocusOut);
            system.Tick(Rig.Dt);
            player.WasTriggered(grenade).Should().BeFalse("alt-tab is not a throw");
            player.WasCanceled(grenade).Should().BeTrue();
            player.GetState(grenade).EndReason.Should().Be(InputEndReason.FocusLost);

            // A genuine release still throws.
            system.FeedEvent(Rig.KeyEvent(Key.G, true));
            system.Tick(Rig.Dt);
            system.FeedEvent(Rig.KeyEvent(Key.G, false));
            system.Tick(Rig.Dt);
            player.WasTriggered(grenade).Should().BeTrue();
        }
        finally { system.Free(); }
    }

    [Fact]
    public void ReleaseTrigger_DoesNotFire_WhenThePadDisconnects_OrIsReassigned()
    {
        var grenade = Make.Action("throw");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            p0.AddContext(Make.Context("C", Make.Bind(grenade, InputSource.ForJoyButton(JoyButton.B), new ReleasedTrigger())));
            system.AssignJoypad(p0, 0, "pad");
            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true, Device = 0 });
            system.Tick(Rig.Dt);
            system.NotifyJoypadConnection(0, false);
            system.Tick(Rig.Dt);
            p0.WasTriggered(grenade).Should().BeFalse();
            p0.GetState(grenade).EndReason.Should().Be(InputEndReason.DeviceDisconnected);

            system.NotifyJoypadConnection(0, true, "pad");
            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true, Device = 0 });
            system.Tick(Rig.Dt);
            system.AssignJoypad(p1, 0, "pad");
            system.Tick(Rig.Dt);
            p0.WasTriggered(grenade).Should().BeFalse();
            p0.GetState(grenade).EndReason.Should().Be(InputEndReason.DeviceReassigned);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void ChangesWithinAFrame_KeepTheirOrder_AcrossChannels()
    {
        var a = Make.Action("a");
        var b = Make.Action("b");
        var order = new List<string>();
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(a, Key.A), Make.Bind(b, Key.B)));
        rig.Player.Bind(a, ActionEvents.Started, _ => order.Add("a+"));
        rig.Player.Bind(a, ActionEvents.Completed, _ => order.Add("a-"));
        rig.Player.Bind(b, ActionEvents.Started, _ => order.Add("b+"));
        rig.Player.Bind(b, ActionEvents.Completed, _ => order.Add("b-"));

        // A tapped, then B tapped, all within one frame.
        rig.Press(Key.A).Release(Key.A).Press(Key.B).Release(Key.B).Tick();
        order.Should().Equal("a+", "a-", "b+", "b-");
    }

    [Fact]
    public void ChordPressedInOrder_WithinOneFrame_Fires()
    {
        var shift = Make.Action("shift");
        var dash = Make.Action("dash");
        int dashes = 0;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(shift, Key.Shift),
            Make.Bind(dash, Key.Space, new PressedTrigger(), new ChordTrigger { ChordAction = shift })));
        rig.Player.Bind(dash, ActionEvents.Triggered, _ => dashes++);
        rig.Press(Key.Shift).Press(Key.Space).Release(Key.Space).Release(Key.Shift).Tick();
        dashes.Should().Be(1);
    }

    [Fact]
    public void DoubleTap_WithinOneFrame_IsADoubleTap()
    {
        var roll = Make.Action("roll");
        int rolls = 0;
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(roll, Key.D, new MultiTapTrigger())));
        rig.Player.Bind(roll, ActionEvents.Triggered, _ => rolls++);
        rig.Press(Key.D).Release(Key.D).Press(Key.D).Release(Key.D).Tick();   // a 4 fps frame
        rolls.Should().Be(1);
    }

    [Fact]
    public void MouseMotion_IsInTheFramesFinalState_WhenKeysChangeToo()
    {
        var look = Make.Action("look", ActionValueKind.Axis2D);
        var fire = Make.Action("fire");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(look, InputSource.ForMouseMotion()), Make.Bind(fire, Key.F)));
        rig.Press(Key.F).Release(Key.F);
        rig.Player.FeedEvent(new InputEventMouseMotion { Relative = new Vector2(4, -2) });
        rig.Tick();
        rig.Player.GetValue(look).AsVector2().Should().Be(new Vector2(4, -2));
        rig.Player.WasTriggered(fire).Should().BeTrue();
    }

    [Fact]
    public void DeviceControls_AreRecorded_AndIgnoredDuringReplay()
    {
        var steer = Make.Action("steer", ActionValueKind.Axis1D);
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var backend = new TwoDeviceBackend();
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(steer, InputSource.ForDeviceControl("wheel", "steering"))));
            system.AddBackend(backend);

            var recording = player.StartRecording();
            backend.Steer(0.5f);
            system.Tick(Rig.Dt);
            backend.Steer(0f);
            system.Tick(Rig.Dt);
            player.StopRecording();
            recording.Ticks[0].Inputs.Should().Contain(i => i.Kind == RecordedInputKind.DeviceControl && i.Value.X == 0.5f);

            player.StartReplay(InputRecording.FromJson(recording.ToJson()));
            backend.Steer(-1f);                      // live: ignored
            system.Tick(Rig.Dt);
            player.GetValue(steer).AsFloat().Should().Be(0.5f, "the recorded value plays, not the live one");
        }
        finally { system.Free(); }
    }

    [Fact]
    public void ExplicitPadIndices_RespectOwnership()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            system.AssignJoypad(p0, 3, "pad");
            p0.MayUseJoypad(3).Should().BeTrue();
            p1.MayUseJoypad(3).Should().BeFalse("player 2 cannot read player 1's gyroscope or rumble it");
            p1.ReceivesUnownedJoypads = true;
            p1.MayUseJoypad(5).Should().BeTrue("an unowned pad");
        }
        finally { system.Free(); }
    }

    [Fact]
    public void ActionModifiers_SeeTheBindingsNature()
    {
        var look = Make.Action("look", ActionValueKind.Axis2D);
        look.Modifiers.Add(new DeltaTimeScaleModifier());
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(look, InputSource.ForMouseMotion())));
        rig.Player.FeedEvent(new InputEventMouseMotion { Relative = new Vector2(10, 0) });
        rig.Tick();
        rig.Player.GetValue(look).AsVector2().Should().Be(new Vector2(10, 0), "mouse motion is already per frame");
    }

    [Fact]
    public void TouchReassignment_CutsOffTheOldPlayersFingers()
    {
        var tap = Make.Action("tap");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            p0.AddContext(Make.Context("C", Make.Bind(tap, InputSource.ForTouch(TouchChannel.Pressed), new ReleasedTrigger())));
            system.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = true });
            system.Tick(Rig.Dt);
            system.AssignDevice(p1, system.GetDevice("touch"));
            system.Tick(Rig.Dt);
            p0.WasTriggered(tap).Should().BeFalse();
            p0.GetState(tap).EndReason.Should().Be(InputEndReason.DeviceReassigned);
            p0.AcceptTouch.Should().BeFalse();
        }
        finally { system.Free(); }
    }

    [Fact]
    public void Rebinding_ForgetsHalfDoneHolds()
    {
        var charge = Make.Action("charge");
        var binding = Make.Bind(charge, Key.C, new HoldTrigger { HoldSeconds = 0.5f }).Rebindable("charge");
        using var rig = new Rig().Add(Make.Context("C", binding));
        rig.Press(Key.C).Press(Key.V).TickFor(0.4);
        rig.Player.Rebind("charge", InputSource.ForKey(Key.V));
        rig.TickFor(0.2);
        rig.Player.IsTriggered(charge).Should().BeFalse("the hold restarts on the new key");
        rig.TickFor(0.35);
        rig.Player.IsTriggered(charge).Should().BeTrue();
    }

    [Fact]
    public void Modes_CanRequireSeparateRoles_FromConnectedDevices()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            system.DeviceProfiles.Add(new DeviceProfile { NameContains = new[] { "Wheel" }, Role = DeviceRole.Wheel });
            system.DeviceProfiles.Add(new DeviceProfile { NameContains = new[] { "Pedals" }, Role = DeviceRole.Pedals });
            var player = system.GetPlayer(0);
            var racing = InputMode.Create("racing", DeviceKinds.None, new[] { "racing" });
            racing.RequiredRoles = DeviceRoles.Wheel | DeviceRoles.Pedals;
            player.RequiredDevicesAvailable(racing).Should().BeFalse();

            var backend = new TwoDeviceBackend();
            system.AddBackend(backend);
            player.RequiredDevicesAvailable(racing).Should().BeTrue();

            system.RemoveBackend(backend);
            player.RequiredDevicesAvailable(racing).Should().BeFalse("disconnected devices do not count");

            var pad = InputMode.Create("pad", DeviceKinds.Joypad, new[] { "pad" });
            player.RequiredDevicesAvailable(pad).Should().Be(system.ConnectedJoypads.Count > 0);
        }
        finally { system.Free(); }
    }
}
