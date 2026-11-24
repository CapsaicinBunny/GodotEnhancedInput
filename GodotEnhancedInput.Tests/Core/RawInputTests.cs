using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>SRC-1, SRC-2, SRC-3, SRC-6, SRC-7, SRC-8, SRC-9.</summary>
[Collection("Godot")]
public class RawInputTests
{
    private static (Rig rig, EnhancedInputAction action) Single(InputSource source, ActionValueKind kind = ActionValueKind.Digital)
    {
        var action = Make.Action("A", kind);
        var rig = new Rig().Add(Make.Context("C", Make.Bind(action, source)));
        return (rig, action);
    }

    [Fact]
    public void Key_MatchesPhysicalKeycode_ByDefault()
    {
        var (rig, action) = Single(InputSource.ForKey(Key.W));
        using (rig)
        {
            // A layout where the W position produces "Z": physical W, logical Z.
            rig.Player.FeedEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.Z, Pressed = true });
            rig.Tick();
            rig.Player.IsTriggered(action).Should().BeTrue();

            rig.Player.FeedEvent(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.Z, Pressed = false });
            rig.Player.FeedEvent(new InputEventKey { PhysicalKeycode = Key.Q, Keycode = Key.W, Pressed = true });
            rig.Tick();
            rig.Player.IsTriggered(action).Should().BeFalse("only the physical position counts");
        }
    }

    [Fact]
    public void Key_LogicalMode_MatchesKeycode()
    {
        var (rig, action) = Single(InputSource.ForKey(Key.W, KeyMatch.Logical));
        using (rig)
        {
            rig.Player.FeedEvent(new InputEventKey { PhysicalKeycode = Key.Q, Keycode = Key.W, Pressed = true });
            rig.Tick();
            rig.Player.IsTriggered(action).Should().BeTrue();
        }
    }

    [Fact]
    public void Key_LabelMode_MatchesKeyLabel()
    {
        var (rig, action) = Single(InputSource.ForKey(Key.A, KeyMatch.Label));
        using (rig)
        {
            rig.Player.FeedEvent(new InputEventKey { PhysicalKeycode = Key.Q, KeyLabel = Key.A, Pressed = true });
            rig.Tick();
            rig.Player.IsTriggered(action).Should().BeTrue();
        }
    }

    [Fact]
    public void EchoEvents_AreIgnored()
    {
        var (rig, action) = Single(InputSource.ForKey(Key.Space));
        using (rig)
        {
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Space, true, echo: true));
            rig.Tick();
            rig.Player.IsTriggered(action).Should().BeFalse();
        }
    }

    [Fact]
    public void TapWithinOneTick_IsSeenInThatTick_InOrder()
    {
        var (rig, action) = Single(InputSource.ForKey(Key.Space));
        using (rig)
        {
            var seen = new List<ActionEvents>();
            rig.Player.Bind(action, ActionEvents.Started | ActionEvents.Triggered | ActionEvents.Completed, e => seen.Add(e.Event));
            rig.Press(Key.Space).Release(Key.Space).Tick();
            rig.Player.WasTriggered(action).Should().BeTrue("the press is seen in the frame it happened");
            rig.Player.WasCompleted(action).Should().BeTrue("and so is the release: no frame of delay");
            rig.Player.GetPhase(action).Should().Be(TriggerPhase.Idle, "polling reads the frame's final state");
            rig.Player.LastTickSteps.Should().Be(3, "the press, the release, and the frame");
            seen.Should().Equal(ActionEvents.Started, ActionEvents.Triggered, ActionEvents.Completed);
            rig.Tick();
            rig.Player.GetEvents(action).Should().Be(ActionEvents.None);
        }
    }

    [Fact]
    public void MouseButton_AndWheelNotch()
    {
        var click = Make.Action("Click");
        var wheel = Make.Action("Wheel", ActionValueKind.Axis1D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(click, InputSource.ForMouseButton(MouseButton.Left)),
            Make.Bind(wheel, InputSource.ForMouseButton(MouseButton.WheelUp))));

        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Factor = 2f });
        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = false, Factor = 2f });

        float notch = 0f;
        rig.Player.Bind(wheel, ActionEvents.Triggered, e => notch = e.Value.AsFloat());
        rig.Tick();

        rig.Player.IsTriggered(click).Should().BeTrue();
        rig.Player.WasTriggered(wheel).Should().BeTrue("a notch is a press and release within the frame");
        notch.Should().Be(2f, "the press step carries its strength");
        rig.Player.GetValue(wheel).AsFloat().Should().Be(0f, "the frame ends released");
    }

    [Fact]
    public void MouseMotion_IsSummedOverTheTick_AndReset()
    {
        var (rig, action) = Single(InputSource.ForMouseMotion(), ActionValueKind.Axis2D);
        using (rig)
        {
            rig.Player.FeedEvent(new InputEventMouseMotion { Relative = new Vector2(3, 1) });
            rig.Player.FeedEvent(new InputEventMouseMotion { Relative = new Vector2(2, -4) });
            rig.Tick();
            rig.Player.GetValue(action).AsVector2().Should().Be(new Vector2(5, -3));
            rig.Tick();
            rig.Player.GetValue(action).AsVector2().Should().Be(Vector2.Zero);
        }
    }

    [Fact]
    public void MousePosition_IsKept()
    {
        var (rig, action) = Single(InputSource.ForMousePosition(), ActionValueKind.Axis2D);
        using (rig)
        {
            rig.Player.FeedEvent(new InputEventMouseMotion { Position = new Vector2(100, 50), Relative = new Vector2(1, 0) });
            rig.Tick(2);
            rig.Player.GetValue(action).AsVector2().Should().Be(new Vector2(100, 50));
        }
    }

    [Fact]
    public void JoyButton_AxisAndStick()
    {
        var button = Make.Action("Button");
        var trigger = Make.Action("Trigger", ActionValueKind.Axis1D);
        var stick = Make.Action("Stick", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(button, InputSource.ForJoyButton(JoyButton.A)),
            Make.Bind(trigger, InputSource.ForJoyAxis(JoyAxis.TriggerRight)),
            Make.Bind(stick, InputSource.ForJoyStick(JoyStickSide.Left))));

        rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 0 });
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 0.7f, Device = 0 });
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.5f, Device = 0 });
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -0.25f, Device = 0 });
        rig.Tick();

        rig.Player.IsTriggered(button).Should().BeTrue();
        rig.Player.GetValue(trigger).AsFloat().Should().BeApproximately(0.7f, 1e-6f);
        rig.Player.GetValue(stick).AsVector2().Should().Be(new Vector2(0.5f, -0.25f));
    }

    [Fact]
    public void JoyAxis_HalfRanges()
    {
        var right = Make.Action("Right", ActionValueKind.Axis1D);
        var left = Make.Action("Left", ActionValueKind.Axis1D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(right, InputSource.ForJoyAxis(JoyAxis.LeftX, range: JoyAxisRange.Positive)),
            Make.Bind(left, InputSource.ForJoyAxis(JoyAxis.LeftX, range: JoyAxisRange.Negative))));

        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -0.6f });
        rig.Tick();
        rig.Player.GetValue(left).AsFloat().Should().BeApproximately(0.6f, 1e-6f);
        rig.Player.GetValue(right).AsFloat().Should().Be(0f);
    }

    [Fact]
    public void JoyButton_DeviceFilter()
    {
        var any = Make.Action("Any");
        var second = Make.Action("Second");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(any, InputSource.ForJoyButton(JoyButton.B)),
            Make.Bind(second, InputSource.ForJoyButton(JoyButton.B, device: 1))));

        rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true, Device = 0 });
        rig.Tick();
        rig.Player.IsTriggered(any).Should().BeTrue();
        rig.Player.IsTriggered(second).Should().BeFalse();

        rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true, Device = 1 });
        rig.Tick();
        rig.Player.IsTriggered(second).Should().BeTrue();
    }

    [Fact]
    public void JoyDisconnect_ClearsState()
    {
        var (rig, action) = Single(InputSource.ForJoyButton(JoyButton.X));
        using (rig)
        {
            rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.X, Pressed = true, Device = 2 });
            rig.Tick();
            rig.Player.IsTriggered(action).Should().BeTrue();

            rig.Player.Raw.ClearDevice(2);
            rig.Tick();
            rig.Player.WasCanceled(action).Should().BeTrue("a lost pad cuts input off");
        }
    }

    [Fact]
    public void KeyAxis1D_PositiveMinusNegative()
    {
        var (rig, action) = Single(InputSource.ForKeyAxis1D(Key.D, Key.A), ActionValueKind.Axis1D);
        using (rig)
        {
            rig.Press(Key.A).Tick();
            rig.Player.GetValue(action).AsFloat().Should().Be(-1f);
            rig.Press(Key.D).Tick();
            rig.Player.GetValue(action).AsFloat().Should().Be(0f);
        }
    }

    [Fact]
    public void KeyAxis2D_FollowsGodotScreenConvention()
    {
        var (rig, action) = Single(InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D), ActionValueKind.Axis2D);
        using (rig)
        {
            rig.Press(Key.W).Press(Key.D).Tick();
            rig.Player.GetValue(action).AsVector2().Should().Be(new Vector2(1f, -1f), "up is −Y, like Input.GetVector and sticks");
        }
    }

    [Fact]
    public void VirtualControl_IsReadBySource()
    {
        var (rig, action) = Single(InputSource.ForVirtual("touch_stick"), ActionValueKind.Axis2D);
        using (rig)
        {
            rig.Player.SetVirtualControl("touch_stick", new Vector2(0.25f, 0.5f));
            rig.Tick();
            rig.Player.GetValue(action).AsVector2().Should().Be(new Vector2(0.25f, 0.5f));
        }
    }

    [Fact]
    public void SourceKinds_ReportTheirChannels()
    {
        var keys = new List<SourceKey>();
        InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D).CollectKeys(keys);
        keys.Should().HaveCount(4);
        keys.Clear();
        InputSource.ForJoyStick(JoyStickSide.Right).CollectKeys(keys);
        keys.Should().BeEquivalentTo(new[] { SourceKey.ForJoyAxis(JoyAxis.RightX), SourceKey.ForJoyAxis(JoyAxis.RightY) });
        keys.Clear();
        InputSource.Nothing().CollectKeys(keys);
        keys.Should().BeEmpty();
    }
}
