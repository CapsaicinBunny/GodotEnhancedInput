using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>SRC-4, SRC-5: Godot's InputMap and InputEventAction drive game actions.</summary>
[Collection("Godot")]
public class InputMapTests
{
    private const string MapAction = "gei_test_jump";

    private static void WithInputMapAction(Action body)
    {
        if (!InputMap.HasAction(MapAction)) InputMap.AddAction(MapAction);
        try { body(); }
        finally
        {
            Input.ActionRelease(MapAction);
            InputMap.EraseAction(MapAction);
        }
    }

    [Fact]
    public void InputMapSource_FollowsInputEventAction()
    {
        WithInputMapAction(() =>
        {
            var jump = Make.Action("Jump");
            using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, InputSource.ForInputMapAction(MapAction))));

            rig.Player.FeedEvent(new InputEventAction { Action = MapAction, Pressed = true, Strength = 1f });
            rig.Tick();
            rig.Player.WasStarted(jump).Should().BeTrue();
            rig.Player.IsTriggered(jump).Should().BeTrue();

            rig.Player.FeedEvent(new InputEventAction { Action = MapAction, Pressed = false });
            rig.Tick();
            rig.Player.WasCompleted(jump).Should().BeTrue();
        });
    }

    [Fact]
    public void InputMapSource_FollowsInputActionPress()
    {
        WithInputMapAction(() =>
        {
            var jump = Make.Action("Jump");
            using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, InputSource.ForInputMapAction(MapAction))));

            Input.ActionPress(MapAction, 0.75f);
            rig.Tick();
            rig.Player.IsTriggered(jump).Should().BeTrue();
            rig.Player.GetValue(jump).AsFloat().Should().BeApproximately(0.75f, 1e-6f);

            Input.ActionRelease(MapAction);
            rig.Tick();
            rig.Player.WasCompleted(jump).Should().BeTrue();
        });
    }

    [Fact]
    public void InputMapSource_IgnoresPolling_WhenDisabled()
    {
        WithInputMapAction(() =>
        {
            var jump = Make.Action("Jump");
            using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, InputSource.ForInputMapAction(MapAction))));
            rig.Player.PollInputMap = false;

            Input.ActionPress(MapAction);
            rig.Tick();
            rig.Player.IsTriggered(jump).Should().BeFalse();
        });
    }

    [Fact]
    public void InputEventAction_WithGameActionName_DrivesAction()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space, new PressedTrigger())));

        rig.Player.FeedEvent(new InputEventAction { Action = "Jump", Pressed = true, Strength = 1f });
        rig.Tick();
        rig.Player.WasTriggered(jump).Should().BeTrue("the event drives the game action directly");

        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue("injected input has no binding triggers, so it stays down while held");

        rig.Player.FeedEvent(new InputEventAction { Action = "Jump", Pressed = false });
        rig.Tick();
        rig.Player.WasCompleted(jump).Should().BeTrue();
    }

    [Fact]
    public void InputEventAction_QuickPressAndRelease_IsSeenOnce()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));

        rig.Player.FeedEvent(new InputEventAction { Action = "Jump", Pressed = true, Strength = 1f });
        rig.Player.FeedEvent(new InputEventAction { Action = "Jump", Pressed = false });
        rig.Tick();
        rig.Player.WasTriggered(jump).Should().BeTrue();
        rig.Tick();
        rig.Player.WasCompleted(jump).Should().BeTrue();
    }

    [Fact]
    public void InputEventAction_Routing_CanBeTurnedOff()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));
        rig.Player.RouteGameActionEvents = false;

        rig.Player.FeedEvent(new InputEventAction { Action = "Jump", Pressed = true, Strength = 1f });
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeFalse();
    }

    [Fact]
    public void InputEventAction_ForActionOutsideActiveContexts_IsIgnored()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig();
        rig.Player.FeedEvent(new InputEventAction { Action = "Jump", Pressed = true, Strength = 1f });
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeFalse();
    }
}
