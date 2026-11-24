using FluentAssertions;
using Godot;
using Xunit;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>Input buffering (consume API, BufferTrigger), input time and cooldowns.</summary>
[Collection("Godot")]
public class BufferAndCooldownTests
{
    private static (Rig Rig, EnhancedInputAction Jump) JumpRig(params Resource[] triggers)
    {
        var jump = Make.Action("jump");
        var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space, triggers)));
        return (rig, jump);
    }

    [Fact]
    public void InputTime_IsTheSumOfDeltas()
    {
        using var rig = new Rig();
        rig.Tick(3, 0.1);
        rig.Player.InputTime.Should().BeApproximately(0.3, 1e-9);
    }

    [Fact]
    public void ConsumeTriggered_KeepsAnEarlyPress_ForTheWindow_Once()
    {
        var (rig, jump) = JumpRig(new PressedTrigger());
        using var _ = rig;
        rig.Press(Key.Space).Tick().Release(Key.Space).Tick();
        rig.TickFor(0.08);   // "landing" a little later

        rig.Player.WasTriggeredWithin(jump, 0.15).Should().BeTrue();
        rig.Player.ConsumeTriggered(jump, 0.15).Should().BeTrue();
        rig.Player.ConsumeTriggered(jump, 0.15).Should().BeFalse("one press is used once");

        rig.Press(Key.Space).Tick().Release(Key.Space).Tick();
        rig.Player.ConsumeTriggered(jump, 0.15).Should().BeTrue("a new press is a new activation");
    }

    [Fact]
    public void ConsumeTriggered_WindowExpires()
    {
        var (rig, jump) = JumpRig(new PressedTrigger());
        using var _ = rig;
        rig.Press(Key.Space).Tick().Release(Key.Space).Tick();
        rig.TickFor(0.3);
        rig.Player.ConsumeTriggered(jump, 0.15).Should().BeFalse();
        rig.Player.GetState(jump).TriggerCount.Should().Be(1UL);
    }

    [Fact]
    public void ConsumeTriggered_DoesNotChangePhasesOrEvents()
    {
        var (rig, jump) = JumpRig();
        using var _ = rig;
        rig.Press(Key.Space).Tick();
        rig.Player.ConsumeTriggered(jump).Should().BeTrue("triggered now");
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue("still held");
        rig.Release(Key.Space).Tick();
        rig.Player.WasReleased(jump).Should().BeTrue("the release still completes it");
    }

    [Fact]
    public void BufferTrigger_HoldsAPressTriggered_ForTheWindow()
    {
        var (rig, jump) = JumpRig(new BufferTrigger { BufferSeconds = 0.15f });
        using var _ = rig;
        rig.Press(Key.Space).Tick().Release(Key.Space).Tick();
        rig.TickFor(0.08);
        rig.Player.IsTriggered(jump).Should().BeTrue("buffered");
        jump.ConsumeTriggered(player: rig.Player).Should().BeTrue();
        jump.ConsumeTriggered(player: rig.Player).Should().BeFalse();
        rig.TickFor(0.1);
        rig.Player.IsTriggered(jump).Should().BeFalse("the window ended");
    }

    [Fact]
    public void Cooldown_BlocksRepeats_UntilItRunsOut()
    {
        var (rig, dash) = JumpRig(new PressedTrigger(), new CooldownTrigger { CooldownSeconds = 0.5f });
        using var _ = rig;
        rig.Press(Key.Space).Tick();
        rig.Player.WasTriggered(dash).Should().BeTrue();
        rig.Release(Key.Space).TickFor(0.2);
        rig.Press(Key.Space).Tick();
        rig.Player.WasTriggered(dash).Should().BeFalse("cooling down");
        rig.Release(Key.Space).TickFor(0.4);
        rig.Press(Key.Space).Tick();
        rig.Player.WasTriggered(dash).Should().BeTrue("ready again");
    }

    [Fact]
    public void Cooldown_IsShared_ByTheActionsBindings_AndNeverCutsAnActivation()
    {
        var dash = Make.Action("dash");
        var cooldown = new CooldownTrigger { CooldownSeconds = 0.5f };
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(dash, Key.Shift, new DownTrigger(), cooldown),
            Make.Bind(dash, Key.B, new PressedTrigger(), cooldown)));

        rig.Press(Key.Shift).Tick();
        rig.TickFor(0.8);
        rig.Player.IsTriggered(dash).Should().BeTrue("held past the cooldown, the activation goes on");
        rig.Release(Key.Shift).Tick();
        rig.Press(Key.B).Tick();
        rig.Player.WasTriggered(dash).Should().BeTrue("the cooldown runs from when it became triggered");

        rig.Release(Key.B).Tick().Press(Key.Shift).Tick();
        rig.Player.IsTriggered(dash).Should().BeFalse("the other binding shares the cooldown");
        cooldown.Remaining(rig.Player, dash).Should().BeGreaterThan(0.0);
    }
}
