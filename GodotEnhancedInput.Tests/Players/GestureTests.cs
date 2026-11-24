using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>GST-1 … GST-6: gestures, safe areas, touch layouts.</summary>
[Collection("Godot")]
public class GestureTests(EngineFixture fixture)
{
    private static InputEventScreenTouch Down(int finger, float x, float y) => new() { Index = finger, Position = new Vector2(x, y), Pressed = true };
    private static InputEventScreenTouch Up(int finger, float x, float y) => new() { Index = finger, Position = new Vector2(x, y), Pressed = false };
    private static InputEventScreenDrag Drag(int finger, float x, float y, float vx = 0, float vy = 0) => new() { Index = finger, Position = new Vector2(x, y), Velocity = new Vector2(vx, vy) };

    private static (Rig rig, EnhancedInputAction action) With(GestureChannel channel, ActionValueKind kind)
    {
        var action = Make.Action(channel.ToString(), kind);
        return (new Rig().Add(Make.Context("C", Make.Bind(action, InputSource.ForGesture(channel)))), action);
    }

    [Fact]
    public void Pinch_SpreadAndClose()
    {
        var (rig, pinch) = With(GestureChannel.Pinch, ActionValueKind.Axis1D);
        using (rig)
        {
            rig.Player.FeedEvent(Down(0, 100, 100));
            rig.Player.FeedEvent(Down(1, 200, 100));
            rig.Player.FeedEvent(Drag(1, 300, 100));   // 100 → 200 apart
            rig.Tick();
            rig.Player.GetValue(pinch).AsFloat().Should().BeApproximately(1f, 1e-4f);
            rig.Player.FeedEvent(Drag(1, 150, 100));   // 200 → 50
            rig.Tick();
            rig.Player.GetValue(pinch).AsFloat().Should().BeApproximately(-0.75f, 1e-4f);
            rig.Tick();
            rig.Player.GetValue(pinch).AsFloat().Should().Be(0f, "per tick");
        }
    }

    [Fact]
    public void TrackpadMagnify_IsAPinch()
    {
        var (rig, pinch) = With(GestureChannel.Pinch, ActionValueKind.Axis1D);
        using (rig)
        {
            rig.Player.FeedEvent(new InputEventMagnifyGesture { Factor = 1.2f });
            rig.Tick();
            rig.Player.GetValue(pinch).AsFloat().Should().BeApproximately(0.2f, 1e-5f);
        }
    }

    [Fact]
    public void Rotate_AndTwoFingerDrag()
    {
        var rotate = Make.Action("rotate", ActionValueKind.Axis1D);
        var pan = Make.Action("pan", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(rotate, InputSource.ForGesture(GestureChannel.Rotate)),
            Make.Bind(pan, InputSource.ForGesture(GestureChannel.TwoFingerDrag))));
        rig.Player.FeedEvent(Down(0, 100, 100));
        rig.Player.FeedEvent(Down(1, 200, 100));
        rig.Player.FeedEvent(Drag(1, 100, 200));   // the second finger swings a quarter turn around the first
        rig.Tick();
        rig.Player.GetValue(rotate).AsFloat().Should().BeApproximately(Mathf.Pi / 2f, 1e-4f);
        rig.Player.GetValue(pan).AsVector2().Should().Be(new Vector2(-50, 50));
    }

    [Fact]
    public void Swipe_NeedsDistanceAndSpeed()
    {
        var (rig, swipe) = With(GestureChannel.Swipe, ActionValueKind.Axis2D);
        using (rig)
        {
            rig.Player.FeedEvent(Down(0, 100, 300));
            rig.Player.FeedEvent(Drag(0, 100, 150, 0, -900));
            rig.Player.FeedEvent(Up(0, 100, 150));
            rig.Tick();
            rig.Player.GetValue(swipe).AsVector2().Should().Be(new Vector2(0, -1));
            rig.Tick();
            rig.Player.GetValue(swipe).AsVector2().Should().Be(Vector2.Zero);

            rig.Player.FeedEvent(Down(0, 100, 300));
            rig.Player.FeedEvent(Drag(0, 100, 150, 0, -50));   // slow: a drag, not a swipe
            rig.Player.FeedEvent(Up(0, 100, 150));
            rig.Tick();
            rig.Player.GetValue(swipe).AsVector2().Should().Be(Vector2.Zero);
        }
    }

    [Fact]
    public void Gestures_WorkWithTriggers_LongPressAndDoubleTap()
    {
        var longPress = Make.Action("long_press");
        var doubleTap = Make.Action("double_tap");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(longPress, InputSource.ForTouch(TouchChannel.Pressed), new HoldTrigger { HoldSeconds = 0.5f, OneShot = true }),
            Make.Bind(doubleTap, InputSource.ForTouch(TouchChannel.Pressed), new MultiTapTrigger())));
        int longPresses = 0;
        rig.Player.Bind(longPress, ActionEvents.Triggered, _ => longPresses++);
        rig.Player.FeedEvent(Down(0, 10, 10));
        rig.TickFor(0.6);
        longPresses.Should().Be(1, "one-shot hold fires once");
        rig.Player.FeedEvent(Up(0, 10, 10));
        rig.Tick(2);

        int doubles = 0;
        rig.Player.Bind(doubleTap, ActionEvents.Triggered, _ => doubles++);
        rig.Player.FeedEvent(Down(0, 10, 10)); rig.Tick(2);
        rig.Player.FeedEvent(Up(0, 10, 10)); rig.Tick(2);
        rig.Player.FeedEvent(Down(0, 10, 10)); rig.Tick(2);
        rig.Player.FeedEvent(Up(0, 10, 10)); rig.Tick(2);
        doubles.Should().Be(1);
    }

    [Fact]
    public void Paths_AndJson_ForGestures()
    {
        InputSourcePath.Format(InputSourcePath.Parse("gesture:two_finger_drag")).Should().Be("gesture:two_finger_drag");
        var source = InputSource.ForGesture(GestureChannel.Rotate);
        InputSourceJson.SameInput(source, InputSourceJson.FromJson(InputSourceJson.ToJson(source), out _)).Should().BeTrue();
        InputPrompts.GetName(source).Should().Be("Twist");
    }

    [Fact]
    public void SafeArea_KeepsControlsInside()
    {
        using var rig = new Rig();
        var button = new VirtualButton { TargetPlayer = rig.Player, Position = new Vector2(-30, 500), Size = new Vector2(80, 80) };
        fixture.Tree.Root.AddChild(button);
        try
        {
            button.SafeAreaOverride = new Rect2(20, 20, 600, 400);
            button.FitToSafeArea();
            button.GlobalPosition.Should().Be(new Vector2(20, 340));
        }
        finally
        {
            button.GetParent().RemoveChild(button);
            button.Free();
        }
    }

    [Fact]
    public void TouchLayout_ApplyCaptureEditAndSave()
    {
        using var rig = new Rig();
        var root = new TouchLayoutRoot { Size = new Vector2(1000, 500) };
        var jump = new VirtualButton { Name = "Jump", ControlName = "touch_jump", TargetPlayer = rig.Player, KeepInSafeArea = false };
        root.AddChild(jump);
        fixture.Tree.Root.AddChild(root);
        try
        {
            var layout = new TouchLayout { Scale = 1f, Opacity = 0.5f };
            layout.Rects["Jump"] = new Rect2(0.8f, 0.6f, 0.1f, 0.2f);
            root.Layout = layout;
            root.Apply();
            jump.Position.DistanceTo(new Vector2(800, 300)).Should().BeLessThan(1e-3f);
            jump.Size.DistanceTo(new Vector2(100, 100)).Should().BeLessThan(1e-3f);
            jump.Modulate.A.Should().BeApproximately(0.5f, 1e-5f);

            root.EditMode = true;
            jump.HandlePointerEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = new Vector2(850, 350) }).Should().BeFalse("controls do not act while editing");
            root.HandleEditEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = new Vector2(850, 350) }).Should().BeTrue();
            root.HandleEditEvent(new InputEventScreenDrag { Index = 0, Relative = new Vector2(-100, 0) });
            root.HandleEditEvent(new InputEventScreenTouch { Index = 0, Pressed = false });
            root.EditMode = false;
            jump.Position.DistanceTo(new Vector2(700, 300)).Should().BeLessThan(1e-3f);

            var captured = root.Capture();
            captured.Rects["Jump"].Position.X.Should().BeApproximately(0.7f, 1e-5f);
            var back = TouchLayout.FromJson(captured.ToJson());
            back.Rects["Jump"].Should().Be(captured.Rects["Jump"]);
            back.Opacity.Should().BeApproximately(0.5f, 1e-5f);
        }
        finally
        {
            root.GetParent().RemoveChild(root);
            root.Free();
        }
    }
}
