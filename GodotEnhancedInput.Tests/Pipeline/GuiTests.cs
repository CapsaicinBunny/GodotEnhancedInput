using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>
/// GD-1: event intake shares input with the GUI. Godot offers each event to <c>_Input</c> first, then to
/// the GUI, then to <c>_UnhandledInput</c> if no control accepted it. These tests offer events the way
/// those stages would.
/// </summary>
[Collection("Godot")]
public class GuiTests
{
    [Fact]
    public void PressConsumedByGui_IsIgnored()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));

        // A focused control accepted the press, so it was only ever offered from _Input.
        rig.Player.FeedEvent(Rig.KeyEvent(Key.Space, true), IntakeStage.Early);
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeFalse();
    }

    [Fact]
    public void UnhandledPress_IsRead()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));

        var press = Rig.KeyEvent(Key.Space, true);
        rig.Player.FeedEvent(press, IntakeStage.Early);
        rig.Player.FeedEvent(press, IntakeStage.Unhandled);
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue();
    }

    [Fact]
    public void ReleaseIsAlwaysSeen()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));

        rig.Player.FeedEvent(Rig.KeyEvent(Key.Space, true), IntakeStage.Unhandled);
        rig.Tick();
        // Focus moved to a text field, which accepts the release: only _Input saw it.
        rig.Player.FeedEvent(Rig.KeyEvent(Key.Space, false), IntakeStage.Early);
        rig.Tick();
        rig.Player.WasCompleted(jump).Should().BeTrue("a key never sticks because of the GUI");
    }

    [Fact]
    public void AxisMovingTowardZero_IsReadEarly_AwayFromZero_Unhandled()
    {
        var steer = Make.Action("Steer", ActionValueKind.Axis1D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(steer, InputSource.ForJoyAxis(JoyAxis.LeftX))));

        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.8f }, IntakeStage.Early);
        rig.Tick();
        rig.Player.GetValue(steer).AsFloat().Should().Be(0f, "moving away from zero waits for the GUI to decline it");

        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.8f }, IntakeStage.Unhandled);
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.1f }, IntakeStage.Early);
        rig.Tick();
        rig.Player.GetValue(steer).AsFloat().Should().BeApproximately(0.1f, 1e-6f);
    }

    [Fact]
    public void MouseMotion_PositionEarly_DeltaUnhandled()
    {
        var look = Make.Action("Look", ActionValueKind.Axis2D);
        var cursor = Make.Action("Cursor", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(look, InputSource.ForMouseMotion()),
            Make.Bind(cursor, InputSource.ForMousePosition())));

        // The pointer moved over a GUI panel that stopped the event.
        rig.Player.FeedEvent(new InputEventMouseMotion { Position = new Vector2(40, 30), Relative = new Vector2(5, 0) }, IntakeStage.Early);
        rig.Tick();
        rig.Player.GetValue(look).AsVector2().Should().Be(Vector2.Zero);
        rig.Player.GetValue(cursor).AsVector2().Should().Be(new Vector2(40, 30));
    }

    [Fact]
    public void System_NeverMarksEventsHandled_AndRespectsPolicy()
    {
        var jump = Make.Action("Jump");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(jump, Key.Space)));

            system._Input(Rig.KeyEvent(Key.Space, true));
            system.Tick(Rig.Dt);
            player.IsTriggered(jump).Should().BeFalse("RespectGui reads presses only when unhandled");

            system.GuiPolicy = GuiPolicy.ReadEverything;
            system._Input(Rig.KeyEvent(Key.Space, true));
            system.Tick(Rig.Dt);
            player.IsTriggered(jump).Should().BeTrue();
        }
        finally
        {
            system.Free();
        }
    }
}
