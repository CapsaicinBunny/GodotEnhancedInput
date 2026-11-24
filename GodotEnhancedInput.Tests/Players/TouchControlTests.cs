using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>TCH-3 … TCH-6: on-screen controls, driven through <see cref="TouchControl.HandlePointerEvent"/>.</summary>
[Collection("Godot")]
public class TouchControlTests(EngineFixture fixture)
{
    private T Place<T>(T control, Rig rig, Vector2 position, Vector2 size) where T : TouchControl
    {
        control.TargetPlayer = rig.Player;
        control.Position = position;
        control.Size = size;
        fixture.Tree.Root.AddChild(control);
        return control;
    }

    private static void Free(Node node)
    {
        node.GetParent()?.RemoveChild(node);
        node.Free();
    }

    private static InputEventScreenTouch Touch(int finger, float x, float y, bool pressed = true)
        => new() { Index = finger, Position = new Vector2(x, y), Pressed = pressed };

    private static InputEventScreenDrag Drag(int finger, float x, float y, float dx = 0, float dy = 0)
        => new() { Index = finger, Position = new Vector2(x, y), Relative = new Vector2(dx, dy) };

    [Fact]
    public void Joystick_WritesClampedVector_AndZeroOnRelease()
    {
        var move = Make.Action("move", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(move, InputSource.ForVirtual("touch_move"))));
        var stick = Place(new VirtualJoystick { Radius = 50 }, rig, new Vector2(0, 0), new Vector2(200, 200));
        try
        {
            stick.HandlePointerEvent(Touch(0, 100, 100)).Should().BeTrue();
            stick.HandlePointerEvent(Drag(0, 125, 100)).Should().BeTrue();
            rig.Tick();
            rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(0.5f, 0f));

            stick.HandlePointerEvent(Drag(0, 100, 0));
            rig.Tick();
            rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(0f, -1f), "clamped to the radius; up is −Y");

            stick.HandlePointerEvent(Touch(0, 100, 0, pressed: false)).Should().BeTrue();
            rig.Tick();
            rig.Player.GetValue(move).AsVector2().Should().Be(Vector2.Zero);
        }
        finally { Free(stick); }
    }

    [Fact]
    public void Joystick_IgnoresTouchesOutside_AndSecondFingers()
    {
        using var rig = new Rig();
        var stick = Place(new VirtualJoystick { Radius = 50 }, rig, new Vector2(0, 0), new Vector2(200, 200));
        try
        {
            stick.HandlePointerEvent(Touch(0, 500, 500)).Should().BeFalse("outside the control");
            stick.HandlePointerEvent(Drag(0, 510, 500)).Should().BeFalse("not followed");

            stick.HandlePointerEvent(Touch(1, 100, 100)).Should().BeTrue();
            stick.HandlePointerEvent(Touch(2, 120, 100)).Should().BeFalse("the stick follows one finger");
        }
        finally { Free(stick); }
    }

    [Fact]
    public void Joystick_Floating_CentresWhereTheFingerLands()
    {
        using var rig = new Rig();
        var stick = Place(new VirtualJoystick { Radius = 50, Floating = true }, rig, Vector2.Zero, new Vector2(400, 400));
        try
        {
            stick.HandlePointerEvent(Touch(0, 300, 300));
            stick.Value.Should().Be(Vector2.Zero);
            stick.HandlePointerEvent(Drag(0, 300, 350));
            stick.Value.Should().Be(new Vector2(0, 1));
        }
        finally { Free(stick); }
    }

    [Fact]
    public void Button_PressedWhileAnyFingerIsDown()
    {
        var jump = Make.Action("jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, InputSource.ForVirtual("touch_jump"), new PressedTrigger())));
        var button = Place(new VirtualButton { ControlName = "touch_jump" }, rig, new Vector2(300, 300), new Vector2(80, 80));
        try
        {
            button.HandlePointerEvent(Touch(0, 340, 340)).Should().BeTrue();
            rig.Tick();
            rig.Player.WasTriggered(jump).Should().BeTrue();

            button.HandlePointerEvent(Touch(1, 350, 350));
            button.HandlePointerEvent(Touch(0, 340, 340, pressed: false));
            rig.Tick();
            button.IsPressed.Should().BeTrue("finger 1 is still down");
            rig.Player.GetPhase(jump).Should().Be(TriggerPhase.Idle, "pressed trigger: only the first tick");

            button.HandlePointerEvent(Touch(1, 350, 350, pressed: false));
            rig.Tick();
            rig.Player.GetValue(jump).AsBool().Should().BeFalse();
        }
        finally { Free(button); }
    }

    [Fact]
    public void Zone_TurnsDragsIntoPerTickDeltas()
    {
        var look = Make.Action("look", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(look, InputSource.ForVirtual("touch_look"))));
        var zone = Place(new TouchZone { Sensitivity = 0.5f, PressedControlName = "look_down" }, rig, new Vector2(500, 0), new Vector2(500, 600));
        try
        {
            zone.HandlePointerEvent(Touch(0, 700, 300)).Should().BeTrue();
            zone.HandlePointerEvent(Drag(0, 710, 300, 10, 0));
            zone.HandlePointerEvent(Drag(0, 720, 296, 10, -4));
            rig.Tick();
            rig.Player.GetValue(look).AsVector2().Should().Be(new Vector2(10, -2));
            rig.Player.Raw.Read(SourceKey.ForVirtual("look_down")).X.Should().Be(1f);

            rig.Tick();
            rig.Player.GetValue(look).AsVector2().Should().Be(Vector2.Zero);
        }
        finally { Free(zone); }
    }

    [Fact]
    public void MultiTouch_StickAndButtonAtOnce()
    {
        var move = Make.Action("move", ActionValueKind.Axis2D);
        var fire = Make.Action("fire");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(move, InputSource.ForVirtual("touch_move")),
            Make.Bind(fire, InputSource.ForVirtual("touch_fire"))));
        var stick = Place(new VirtualJoystick { Radius = 50 }, rig, Vector2.Zero, new Vector2(200, 200));
        var button = Place(new VirtualButton { ControlName = "touch_fire" }, rig, new Vector2(600, 0), new Vector2(100, 100));
        try
        {
            foreach (var e in new InputEvent[] { Touch(0, 100, 100), Touch(1, 650, 50), Drag(0, 150, 100) })
            {
                bool used = stick.HandlePointerEvent(e) | button.HandlePointerEvent(e);
                used.Should().BeTrue();
            }
            rig.Tick();
            rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(1, 0));
            rig.Player.IsTriggered(fire).Should().BeTrue();
        }
        finally { Free(stick); Free(button); }
    }

    [Fact]
    public void Mouse_DrivesControls_ButNotTouchEmulatedClicks()
    {
        using var rig = new Rig();
        var button = Place(new VirtualButton { ControlName = "b" }, rig, Vector2.Zero, new Vector2(100, 100));
        try
        {
            var emulated = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(50, 50), Device = (int)InputEvent.DeviceIdEmulation };
            button.HandlePointerEvent(emulated).Should().BeFalse();

            button.HandlePointerEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(50, 50) }).Should().BeTrue();
            button.IsPressed.Should().BeTrue();

            button.AcceptMouse = false;
            button.ReleaseAll();
            button.HandlePointerEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(50, 50) }).Should().BeFalse();
        }
        finally { Free(button); }
    }

    [Fact]
    public void HiddenControls_IgnoreInput_AndReleaseWhenHidden()
    {
        using var rig = new Rig();
        var button = Place(new VirtualButton { ControlName = "b" }, rig, Vector2.Zero, new Vector2(100, 100));
        try
        {
            button.HandlePointerEvent(Touch(0, 50, 50));
            button.Hide();
            button.IsPressed.Should().BeFalse("hiding releases held fingers");
            rig.Player.Raw.GetCurrent(SourceKey.ForVirtual("b")).X.Should().Be(0f, "a press and release within one tick still reads once, but the current value is 0");
            button.HandlePointerEvent(Touch(1, 50, 50)).Should().BeFalse();
        }
        finally { Free(button); }
    }
}
