using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>EDT-2: the editing helpers the editor dock uses.</summary>
[Collection("Godot")]
public class BindingEditingTests
{
    [Fact]
    public void FindsEveryModifierAndTrigger_AndCreatesThemByName()
    {
        BindingEditing.ModifierTypes.Should().Contain(typeof(DeadzoneModifier)).And.Contain(typeof(RotationSmoothingModifier));
        BindingEditing.TriggerTypes.Should().Contain(typeof(HoldTrigger)).And.Contain(typeof(DwellTrigger));
        BindingEditing.TriggerTypes.Should().NotContain(t => t.IsAbstract);
        BindingEditing.Create("Hold").Should().BeOfType<HoldTrigger>();
        BindingEditing.Create("DeadzoneModifier").Should().BeOfType<DeadzoneModifier>();
        BindingEditing.Create("Nope").Should().BeNull();
        BindingEditing.ShortName(new PressedTrigger()).Should().Be("Pressed");
    }

    [Fact]
    public void PathsGroupsSummariesAndSearch()
    {
        BindingEditing.TryParsePath("pad:left_stick", out var error).Kind.Should().Be(SourceKind.JoyStick);
        error.Should().BeNull();
        BindingEditing.TryParsePath("pad:nonsense", out error).Should().BeNull();
        error.Should().NotBeNullOrEmpty();
        BindingEditing.ParseGroups(" kbm , pad,, ").Should().Equal("kbm", "pad");

        var jump = Make.Action("jump");
        var binding = Make.Bind(jump, InputSource.ForJoyButton(JoyButton.A), new HoldTrigger(), new ScaleModifier()).InGroups("pad").Rebindable("jump_pad");
        BindingEditing.Summary(binding).Should().Be("Scale  ·  Hold  ·  groups: pad  ·  rebind: jump_pad");
        BindingEditing.DeviceKindOf(binding.Source).Should().Be(InputDeviceKind.Joypad);
        BindingEditing.DeviceKindOf(InputSource.ForKey(Key.A)).Should().Be(InputDeviceKind.KeyboardMouse);
        BindingEditing.Matches(binding, "JUMP").Should().BeTrue();
        BindingEditing.Matches(binding, "pad:a").Should().BeTrue();
        BindingEditing.Matches(binding, "hold").Should().BeTrue();
        BindingEditing.Matches(binding, "mouse").Should().BeFalse();
        BindingEditing.ActionsIn(new[] { Make.Context("C", binding, Make.Bind(Make.Action("aim"), Key.Q)) }).Select(a => a.Name).Should().Equal("aim", "jump");
    }
}
