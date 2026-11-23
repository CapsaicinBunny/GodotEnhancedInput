using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Prompts;

/// <summary>PRM-1 … PRM-5.</summary>
[Collection("Godot")]
public class PromptTests
{
    [Fact]
    public void Names_Keys()
    {
        InputPrompts.GetName(InputSource.ForKey(Key.Space)).Should().Be("Space");
        InputPrompts.GetName(InputSource.ForKey(Key.W, KeyMatch.Logical)).Should().Be("W");
        InputPrompts.GetName(InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D)).Should().Be("W/A/S/D");
        InputPrompts.GetName(InputSource.ForKeyAxis1D(Key.D, Key.A)).Should().Be("A/D");
        InputPrompts.KeyName(Key.None).Should().BeEmpty();
    }

    [Fact]
    public void Names_Mouse()
    {
        InputPrompts.GetName(InputSource.ForMouseButton(MouseButton.Left)).Should().Be("Left Mouse");
        InputPrompts.GetName(InputSource.ForMouseButton(MouseButton.WheelUp)).Should().Be("Wheel Up");
        InputPrompts.GetName(InputSource.ForMouseMotion()).Should().Be("Mouse");
    }

    [Fact]
    public void Names_JoypadAxesAndSticks()
    {
        InputPrompts.GetName(InputSource.ForJoyStick(JoyStickSide.Left)).Should().Be("Left Stick");
        InputPrompts.GetName(InputSource.ForJoyAxis(JoyAxis.LeftY, range: JoyAxisRange.Negative)).Should().Be("Left Stick Up");
        InputPrompts.GetName(InputSource.ForJoyAxis(JoyAxis.RightX, range: JoyAxisRange.Positive)).Should().Be("Right Stick Right");
        InputPrompts.GetName(InputSource.ForJoyAxis(JoyAxis.LeftX)).Should().Be("Left Stick X");
        InputPrompts.GetName(InputSource.ForJoyAxis(JoyAxis.TriggerRight), JoypadStyle.Xbox).Should().Be("RT");
    }

    [Fact]
    public void Names_InputMapAction_UsesItsFirstEvent()
    {
        const string action = "gei_prompt_test";
        InputMap.AddAction(action);
        try
        {
            InputPrompts.GetName(InputSource.ForInputMapAction(action)).Should().Be(action, "no events yet");
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = Key.E });
            InputPrompts.GetName(InputSource.ForInputMapAction(action)).Should().Be("E");
        }
        finally { InputMap.EraseAction(action); }
    }

    [Fact]
    public void Names_TouchAndVirtual()
    {
        InputPrompts.GetName(InputSource.ForTouch(TouchChannel.Pressed)).Should().Be("Touch");
        InputPrompts.GetName(InputSource.ForTouch(TouchChannel.Drag)).Should().Be("Swipe");
        InputPrompts.GetName(InputSource.ForVirtual("touch_jump")).Should().Be("touch_jump");
    }

    [Theory]
    [InlineData("Xbox Wireless Controller", JoypadStyle.Xbox)]
    [InlineData("PS5 Controller", JoypadStyle.PlayStation)]
    [InlineData("DualSense Wireless Controller", JoypadStyle.PlayStation)]
    [InlineData("Nintendo Switch Pro Controller", JoypadStyle.Nintendo)]
    [InlineData("USB Gamepad", JoypadStyle.Generic)]
    [InlineData(null, JoypadStyle.Generic)]
    public void Style_DetectedFromName(string name, JoypadStyle expected) => InputPrompts.DetectStyle(name).Should().Be(expected);

    [Theory]
    [InlineData(JoypadStyle.Generic, "South", "Left Shoulder", "Start")]
    [InlineData(JoypadStyle.Xbox, "A", "LB", "Menu")]
    [InlineData(JoypadStyle.PlayStation, "Cross", "L1", "Options")]
    [InlineData(JoypadStyle.Nintendo, "B", "L", "+")]
    public void Style_NamesButtons(JoypadStyle style, string south, string leftShoulder, string start)
    {
        InputPrompts.JoyButtonName(JoyButton.A, style).Should().Be(south);
        InputPrompts.JoyButtonName(JoyButton.LeftShoulder, style).Should().Be(leftShoulder);
        InputPrompts.JoyButtonName(JoyButton.Start, style).Should().Be(start);
    }

    [Fact]
    public void IconKeys_AreStable()
    {
        InputPrompts.GetIconKey(InputSource.ForKey(Key.Space)).Should().Be("key_space");
        InputPrompts.GetIconKey(InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D)).Should().Be("keys_w_a_s_d");
        InputPrompts.GetIconKey(InputSource.ForMouseButton(MouseButton.WheelUp)).Should().Be("mouse_wheel_up");
        InputPrompts.GetIconKey(InputSource.ForJoyButton(JoyButton.LeftShoulder)).Should().Be("joy_left_shoulder");
        InputPrompts.GetIconKey(InputSource.ForJoyAxis(JoyAxis.LeftY, range: JoyAxisRange.Negative)).Should().Be("joy_left_y_negative");
        InputPrompts.GetIconKey(InputSource.ForJoyStick(JoyStickSide.Right)).Should().Be("joy_right_stick");
        InputPrompts.GetIconKey(InputSource.ForVirtual("touch_jump")).Should().Be("virtual_touch_jump");
    }

    [Fact]
    public void LastDevice_FollowsDeliberateInput()
    {
        using var rig = new Rig();
        var changes = new List<(InputDeviceKind, int)>();
        rig.Player.InputDeviceChanged += (kind, pad) => changes.Add((kind, pad));

        rig.Player.LastDeviceKind.Should().Be(InputDeviceKind.Other);
        rig.Press(Key.Q);
        rig.Player.FeedEvent(new InputEventMouseMotion { Relative = new Vector2(1, 0) });
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.2f, Device = 1 });
        rig.Player.LastDeviceKind.Should().Be(InputDeviceKind.KeyboardMouse, "tiny mouse and stick movements do not count");

        rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 2 });
        rig.Player.LastDeviceKind.Should().Be(InputDeviceKind.Joypad);
        rig.Player.LastJoypad.Should().Be(2);

        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.9f, Device = 3 });
        rig.Player.FeedEvent(new InputEventScreenTouch { Index = 0, Pressed = true });

        changes.Should().Equal((InputDeviceKind.KeyboardMouse, -1), (InputDeviceKind.Joypad, 2), (InputDeviceKind.Joypad, 3), (InputDeviceKind.Touch, 3));
    }

    [Fact]
    public void LastDevice_IgnoresTheUnhandledStage()
    {
        using var rig = new Rig();
        int changes = 0;
        rig.Player.InputDeviceChanged += (_, _) => changes++;
        var press = Rig.KeyEvent(Key.Q, true);
        rig.Player.FeedEvent(press, IntakeStage.Early);
        rig.Player.FeedEvent(press, IntakeStage.Unhandled);
        changes.Should().Be(1);
    }

    private static (Rig rig, GameAction jump, BindingContext context) Jump()
    {
        var jump = Make.Action("jump");
        var context = Make.Context("gameplay");
        context.Add(jump, InputSource.ForKey(Key.Space)).Rebindable("jump_key");
        context.Add(jump, InputSource.ForJoyButton(JoyButton.A)).Rebindable("jump_pad");
        context.Add(jump, InputSource.ForVirtual("touch_jump"));
        return (new Rig().Add(context), jump, context);
    }

    [Fact]
    public void ActionPrompts_FollowTheLastUsedDevice()
    {
        var (rig, jump, _) = Jump();
        using (rig)
        {
            rig.Player.GetPrompts(jump).Should().HaveCount(3, "nothing used yet: everything");

            rig.Press(Key.Q);
            rig.Player.GetPromptText(jump).Should().Be("Space");

            rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true, Device = 0 });
            rig.Player.JoypadStyleOverride = JoypadStyle.Xbox;
            var prompt = rig.Player.GetPrompts(jump).Single();
            prompt.Text.Should().Be("A");
            prompt.IconKey.Should().Be("joy_a");
            prompt.DeviceKind.Should().Be(InputDeviceKind.Joypad);
            prompt.Style.Should().Be(JoypadStyle.Xbox);

            rig.Player.GetPromptText(jump, InputDeviceKind.Touch).Should().Be("touch_jump");
        }
    }

    [Fact]
    public void ActionPrompts_FollowRebinds_AndSkipClearedSlots()
    {
        var (rig, jump, _) = Jump();
        using (rig)
        {
            rig.Player.Rebind("jump_key", InputSource.ForKey(Key.J));
            rig.Player.GetPromptText(jump, InputDeviceKind.KeyboardMouse).Should().Be("J");

            rig.Player.ClearRebind("jump_key");
            rig.Player.GetPrompts(jump, InputDeviceKind.KeyboardMouse).Select(p => p.DeviceKind)
                .Should().NotContain(InputDeviceKind.KeyboardMouse, "cleared; falls back to every other source");
        }
    }

    [Fact]
    public void ActionPrompts_OnlyFromActiveContexts_WithoutDuplicates()
    {
        var (rig, jump, context) = Jump();
        using (rig)
        {
            var other = Make.Context("other", Make.Bind(jump, Key.Space));
            rig.Add(other, 5);
            rig.Player.GetPrompts(jump, InputDeviceKind.KeyboardMouse).Should().ContainSingle();

            rig.Player.RemoveContext(context);
            rig.Player.RemoveContext(other);
            rig.Player.GetPrompts(jump).Should().BeEmpty();
        }
    }

    [Fact]
    public void Icons_StyledKeyFirst_ThenPlain_ThenFallback()
    {
        var generic = new ImageTexture();
        var xbox = new ImageTexture();
        var keyboard = new ImageTexture();
        var set = new PromptIconSet();
        set.Icons["joy_a"] = generic;
        set.Icons["xbox/joy_a"] = xbox;
        set.Fallback = new PromptIconSet();
        set.Fallback.Icons["key_space"] = keyboard;

        set.GetIcon("joy_a", JoypadStyle.Xbox).Should().BeSameAs(xbox);
        set.GetIcon("joy_a", JoypadStyle.PlayStation).Should().BeSameAs(generic);
        set.GetIcon("key_space").Should().BeSameAs(keyboard);
        set.GetIcon("joy_b").Should().BeNull();
    }

    [Fact]
    public void Icons_BBCode_ImageOrBoldText()
    {
        var jump = Make.Action("jump");
        var space = new ActionPrompt(jump, null, null, InputSource.ForKey(Key.Space), JoypadStyle.Generic);
        var set = new PromptIconSet();
        set.ToBBCode(space).Should().Be("[b]Space[/b]");

        var texture = new ImageTexture { ResourcePath = "res://icons/key_space.png" };
        set.Icons["key_space"] = texture;
        set.ToBBCode(space, 24).Should().Be("[img height=24]res://icons/key_space.png[/img]");
        PromptIconSet.Escape("[x]").Should().Be("[lb]x]");
    }
}
