using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Rebinding;

/// <summary>MAP-4, MAP-5.</summary>
[Collection("Godot")]
public class CaptureTests
{
    [Fact]
    public void FromEvent_Key_IsPhysicalByDefault()
    {
        var source = InputSource.FromEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.Z, Pressed = true });
        source.Kind.Should().Be(SourceKind.Key);
        source.Key.Should().Be(Key.W);
        source.KeyMatch.Should().Be(KeyMatch.Physical);

        InputSource.FromEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.Z, Pressed = true }, KeyMatch.Logical).Key.Should().Be(Key.Z);
    }

    [Fact]
    public void FromEvent_IgnoresReleasesEchoesAndSmallMotion()
    {
        InputSource.FromEvent(new InputEventKey { PhysicalKeycode = Key.W, Pressed = false }).Should().BeNull();
        InputSource.FromEvent(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true, Echo = true }).Should().BeNull();
        InputSource.FromEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.2f }).Should().BeNull();
        InputSource.FromEvent(new InputEventMouseMotion { Relative = new Vector2(30, 0) }).Should().BeNull();
    }

    [Fact]
    public void FromEvent_MouseJoyButtonAndHalfAxis()
    {
        InputSource.FromEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true }).MouseButton.Should().Be(MouseButton.WheelUp);

        var button = InputSource.FromEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true, Device = 2 });
        button.JoyButton.Should().Be(JoyButton.Y);
        button.Device.Should().Be(SourceKey.AnyDevice);
        InputSource.FromEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true, Device = 2 }, anyJoypad: false).Device.Should().Be(2);

        var axis = InputSource.FromEvent(new InputEventJoypadMotion { Axis = JoyAxis.TriggerLeft, AxisValue = 0.9f });
        axis.Kind.Should().Be(SourceKind.JoyAxis);
        axis.AxisRange.Should().Be(JoyAxisRange.Positive);
        InputSource.FromEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -0.8f }).AxisRange.Should().Be(JoyAxisRange.Negative);
    }

    private static (Rig rig, EnhancedInputAction jump, EnhancedInputAction fire) Setup()
    {
        var jump = Make.Action("jump");
        var fire = Make.Action("fire");
        var context = Make.Context("gameplay");
        context.Add(jump, InputSource.ForKey(Key.Space)).Rebindable("jump_key");
        context.Add(fire, InputSource.ForKey(Key.F)).Rebindable("fire_key");
        return (new Rig().Add(context), jump, fire);
    }

    [Fact]
    public void Capture_BindsTheNextPress_AndSwallowsIt()
    {
        var (rig, jump, _) = Setup();
        using (rig)
        {
            string capturedId = null;
            InputSource captured = null;
            rig.Player.RebindCaptured += (id, source) => { capturedId = id; captured = source; };

            rig.Player.StartCapture("jump_key").Should().BeTrue();
            rig.Player.IsCapturing.Should().BeTrue();

            rig.Player.FeedEvent(Rig.KeyEvent(Key.J, true)).Should().BeTrue("the capture took the press");
            rig.Tick();
            rig.Player.IsTriggered(jump).Should().BeFalse("the captured press does not reach actions");
            rig.Player.IsCapturing.Should().BeFalse();
            capturedId.Should().Be("jump_key");
            captured.Key.Should().Be(Key.J);

            rig.Release(Key.J).Tick().Press(Key.J).Tick();
            rig.Player.IsTriggered(jump).Should().BeTrue();
        }
    }

    [Fact]
    public void Capture_CancelKey_ChangesNothing()
    {
        var (rig, _, _) = Setup();
        using (rig)
        {
            string canceled = null;
            rig.Player.RebindCaptureCanceled += id => canceled = id;
            rig.Player.StartCapture("jump_key");

            rig.Player.FeedEvent(Rig.KeyEvent(Key.Escape, true)).Should().BeTrue();
            canceled.Should().Be("jump_key");
            rig.Player.GetRebindSlot("jump_key").CurrentSource.Key.Should().Be(Key.Space);
        }
    }

    [Fact]
    public void Capture_DeviceFilter_LetsOtherInputThrough()
    {
        var (rig, _, fire) = Setup();
        using (rig)
        {
            rig.Player.StartCapture("jump_key", new CaptureOptions { Keyboard = false });

            rig.Player.FeedEvent(Rig.KeyEvent(Key.F, true)).Should().BeFalse();
            rig.Tick();
            rig.Player.IsTriggered(fire).Should().BeTrue();
            rig.Player.IsCapturing.Should().BeTrue();

            rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true }).Should().BeTrue();
            rig.Player.GetRebindSlot("jump_key").CurrentSource.JoyButton.Should().Be(JoyButton.B);
        }
    }

    [Fact]
    public void Capture_AppliesConflictPolicy()
    {
        var (rig, _, _) = Setup();
        using (rig)
        {
            RebindResult result = null;
            rig.Player.RebindCompleted += r => result = r;
            rig.Player.StartCapture("jump_key", new CaptureOptions { ConflictPolicy = RebindConflictPolicy.Swap });

            rig.Player.FeedEvent(Rig.KeyEvent(Key.F, true));
            result.Applied.Should().BeTrue();
            rig.Player.GetRebindSlot("fire_key").CurrentSource.Key.Should().Be(Key.Space);
        }
    }

    [Fact]
    public void Capture_OnlyFromEarlyOrDirectStages()
    {
        var (rig, _, _) = Setup();
        using (rig)
        {
            rig.Player.StartCapture("jump_key");
            rig.Player.FeedEvent(Rig.KeyEvent(Key.K, true), IntakeStage.Unhandled).Should().BeFalse();
            rig.Player.FeedEvent(Rig.KeyEvent(Key.K, true), IntakeStage.Early).Should().BeTrue();
        }
    }

    [Fact]
    public void Capture_UnknownSlot_DoesNotStart()
    {
        var (rig, _, _) = Setup();
        using (rig) rig.Player.StartCapture("nope").Should().BeFalse();
    }

    [Fact]
    public void System_MarksOnlyCapturedEventsHandled()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var context = Make.Context("gameplay");
            context.Add(Make.Action("jump"), InputSource.ForKey(Key.Space)).Rebindable("jump_key");
            var player = system.GetPlayer(0);
            player.AddContext(context);
            player.StartCapture("jump_key");

            system.FeedEvent(Rig.KeyEvent(Key.H, true), IntakeStage.Early).Should().BeTrue();
            system.FeedEvent(Rig.KeyEvent(Key.H, true), IntakeStage.Early).Should().BeFalse();
            player.GetRebindSlot("jump_key").CurrentSource.Key.Should().Be(Key.H);
        }
        finally
        {
            system.Free();
        }
    }
}
