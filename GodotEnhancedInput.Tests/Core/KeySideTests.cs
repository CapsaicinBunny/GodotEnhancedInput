using FluentAssertions;
using Godot;
using Xunit;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>Left and right Shift, Ctrl, Alt and Meta as separate inputs.</summary>
[Collection("Godot")]
public class KeySideTests
{
    private static InputEventKey Side(Key key, KeyLocation side, bool pressed)
        => new() { PhysicalKeycode = key, Keycode = key, Location = side, Pressed = pressed };

    [Theory]
    [InlineData("key:left_shift", Key.Shift, KeyLocation.Left)]
    [InlineData("key:right_ctrl", Key.Ctrl, KeyLocation.Right)]
    [InlineData("key:right_control", Key.Ctrl, KeyLocation.Right)]
    [InlineData("key:left_alt", Key.Alt, KeyLocation.Left)]
    [InlineData("key:right_meta", Key.Meta, KeyLocation.Right)]
    [InlineData("key:shift", Key.Shift, KeyLocation.Unspecified)]
    public void Paths_NameTheSide(string path, Key key, KeyLocation side)
    {
        var source = InputSourcePath.Parse(path);
        source.Key.Should().Be(key);
        source.KeySide.Should().Be(side);
        InputSourcePath.Format(source).Should().Be(path.Replace("control", "ctrl"));
    }

    [Fact]
    public void Left_IsStillTheArrowKey()
    {
        var source = InputSourcePath.Parse("key:left");
        source.Key.Should().Be(Key.Left);
        source.KeySide.Should().Be(KeyLocation.Unspecified);
    }

    [Fact]
    public void Names_ShowTheSide()
    {
        InputPrompts.LayoutLabelsSupported = false;
        InputPrompts.GetName(InputSource.ForKey(Key.Shift, KeyLocation.Left)).Should().StartWith("Left ");
        InputPrompts.GetName(InputSource.ForKey(Key.Ctrl, KeyLocation.Right)).Should().StartWith("Right ");
        InputPrompts.GetName(InputSource.ForKey(Key.Shift)).Should().NotContain("Left");
    }

    [Fact]
    public void Capture_RecordsTheSide()
    {
        var source = InputSource.FromEvent(Side(Key.Shift, KeyLocation.Right, true));
        source.Key.Should().Be(Key.Shift);
        source.KeySide.Should().Be(KeyLocation.Right);
        InputSourceJson.SameInput(source, InputSourceJson.FromJson(InputSourceJson.ToJson(source), out _)).Should().BeTrue();
        InputSourceJson.SameInput(source, InputSource.ForKey(Key.Shift)).Should().BeFalse();
    }

    [Fact]
    public void SidedSources_ReadOnlyTheirSide_EitherReadsBoth()
    {
        var left = EnhancedInputAction.Create("left", ActionValueKind.Digital);
        var right = EnhancedInputAction.Create("right", ActionValueKind.Digital);
        var either = EnhancedInputAction.Create("either", ActionValueKind.Digital);
        var context = EnhancedInputMappingContext.Create("sides");
        context.Bindings.Add(ActionBinding.Create(left, InputSource.ForKey(Key.Shift, KeyLocation.Left)));
        context.Bindings.Add(ActionBinding.Create(right, InputSource.ForKey(Key.Shift, KeyLocation.Right)));
        context.Bindings.Add(ActionBinding.Create(either, InputSource.ForKey(Key.Shift)));
        using var rig = new Rig().Add(context);

        rig.Player.FeedEvent(Side(Key.Shift, KeyLocation.Left, true));
        rig.Tick();
        rig.Player.IsTriggered(left).Should().BeTrue();
        rig.Player.IsTriggered(right).Should().BeFalse();
        rig.Player.IsTriggered(either).Should().BeTrue();

        rig.Player.FeedEvent(Side(Key.Shift, KeyLocation.Left, false));
        rig.Player.FeedEvent(Side(Key.Shift, KeyLocation.Right, true));
        rig.Tick();
        rig.Player.IsTriggered(left).Should().BeFalse();
        rig.Player.IsTriggered(right).Should().BeTrue();
        rig.Player.IsTriggered(either).Should().BeTrue();
    }
}
