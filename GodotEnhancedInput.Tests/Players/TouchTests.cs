using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>TCH-1, TCH-2.</summary>
[Collection("Godot")]
public class TouchTests
{
    [Fact]
    public void TouchSource_Pressed_AnyFingerAndSpecificFinger()
    {
        var any = Make.Action("any");
        var second = Make.Action("second");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(any, InputSource.ForTouch(TouchChannel.Pressed)),
            Make.Bind(second, InputSource.ForTouch(TouchChannel.Pressed, 1))));

        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = new Vector2(10, 10) });
        rig.Tick();
        rig.Player.IsTriggered(any).Should().BeTrue();
        rig.Player.IsTriggered(second).Should().BeFalse();

        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 1, Pressed = true, Position = new Vector2(50, 10) });
        rig.Tick();
        rig.Player.IsTriggered(second).Should().BeTrue();
    }

    [Fact]
    public void TouchSource_QuickTap_IsSeenOnce()
    {
        var tap = Make.Action("tap");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(tap, InputSource.ForTouch(TouchChannel.Pressed))));

        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = true });
        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = false });
        rig.Tick();
        rig.Player.WasTriggered(tap).Should().BeTrue();
        rig.Tick();
        rig.Player.WasCompleted(tap).Should().BeTrue();
    }

    [Fact]
    public void TouchSource_Position_FollowsTheFirstFingerDown()
    {
        var where = Make.Action("where", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(where, InputSource.ForTouch(TouchChannel.Position))));

        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 1, Pressed = true, Position = new Vector2(300, 40) });
        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = new Vector2(20, 40) });
        rig.Player.FeedEvent(new InputEventScreenDrag { Index = 0, Position = new Vector2(25, 45), Relative = new Vector2(5, 5) });
        rig.Tick();
        rig.Player.GetValue(where).AsVector2().Should().Be(new Vector2(25, 45));

        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = false, Position = new Vector2(25, 45) });
        rig.Tick();
        rig.Player.GetValue(where).AsVector2().Should().Be(new Vector2(300, 40));
    }

    [Fact]
    public void TouchSource_Drag_IsSummedOverTheTick()
    {
        var look = Make.Action("look", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(look, InputSource.ForTouch(TouchChannel.Drag))));

        rig.Player.FeedEvent(new InputEventScreenDrag { Index = 0, Relative = new Vector2(4, 0) });
        rig.Player.FeedEvent(new InputEventScreenDrag { Index = 1, Relative = new Vector2(0, -2) });
        rig.Tick();
        rig.Player.GetValue(look).AsVector2().Should().Be(new Vector2(4, -2));
        rig.Tick();
        rig.Player.GetValue(look).AsVector2().Should().Be(Vector2.Zero);
    }

    [Fact]
    public void TouchSource_FollowsGuiRules()
    {
        var tap = Make.Action("tap");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(tap, InputSource.ForTouch(TouchChannel.Pressed))));

        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = true }, IntakeStage.Early);
        rig.Tick();
        rig.Player.IsTriggered(tap).Should().BeFalse("a touch the GUI accepted is not gameplay input");
    }

    [Fact]
    public void TouchSource_RoundTripsThroughJson()
    {
        var source = InputSource.ForTouch(TouchChannel.Drag, 2);
        InputSourceJson.SameInput(source, InputSourceJson.FromJson(InputSourceJson.ToJson(source), out _)).Should().BeTrue();
    }

    [Fact]
    public void VirtualDelta_LastsOneTick()
    {
        var look = Make.Action("look", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(look, InputSource.ForVirtual("v"))));

        rig.Player.SetVirtualControl("v", new Vector2(1, 0));
        rig.Player.AddToVirtualControl("v", new Vector2(0, 3));
        rig.Player.AddToVirtualControl("v", new Vector2(0, 2));
        rig.Tick();
        rig.Player.GetValue(look).AsVector2().Should().Be(new Vector2(1, 5));
        rig.Tick();
        rig.Player.GetValue(look).AsVector2().Should().Be(new Vector2(1, 0));
    }
}
