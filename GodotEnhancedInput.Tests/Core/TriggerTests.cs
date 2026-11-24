using FluentAssertions;
using Godot;
using static GodotEnhancedInput.TriggerPhase;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>TRG-1 … TRG-8, TRG-11 (unit level), TRG-12 roles.</summary>
[Collection("Godot")]
public class TriggerTests
{
    private const double Step = 0.1;

    /// <summary>Feeds one value per tick (0.1 s each) and returns the phases.</summary>
    private static TriggerPhase[] Run(InputTrigger trigger, params float[] values)
    {
        var memory = new TriggerMemory();
        return values.Select(v => trigger.Evaluate(new TriggerContext(new Vector3(v, 0, 0), ActionValueKind.Digital, Step, null, memory))).ToArray();
    }

    private static TriggerPhase Set(float value, params InputTrigger[] triggers)
        => TriggerSet.Evaluate(triggers, triggers.Select(_ => new TriggerMemory()).ToArray(), new Vector3(value, 0, 0), ActionValueKind.Digital, Step, null);

    [Fact]
    public void NoTriggers_TriggeredWhileNonZero()
    {
        Set(0.01f).Should().Be(Triggered);
        Set(0f).Should().Be(Idle);
    }

    [Fact]
    public void ActuationThreshold_ZeroIsNeverActuated()
    {
        new DownTrigger { ActuationThreshold = 0f }.IsActuated(Vector3.Zero).Should().BeFalse();
        new DownTrigger { ActuationThreshold = 0f }.IsActuated(new Vector3(0.01f, 0, 0)).Should().BeTrue();
        new DownTrigger { ActuationThreshold = 0.5f }.IsActuated(new Vector3(0.4f, 0, 0)).Should().BeFalse();
    }

    [Fact]
    public void Down_TriggeredWhileActuated()
        => Run(new DownTrigger(), 0, 1, 1, 0.3f, 0).Should().Equal(Idle, Triggered, Triggered, Idle, Idle);

    [Fact]
    public void Pressed_OnlyOnTheFirstTick()
        => Run(new PressedTrigger(), 0, 1, 1, 0, 1).Should().Equal(Idle, Triggered, Idle, Idle, Triggered);

    [Fact]
    public void Released_OngoingThenTriggeredOnRelease()
        => Run(new ReleasedTrigger(), 1, 1, 0, 0).Should().Equal(Ongoing, Ongoing, Triggered, Idle);

    [Fact]
    public void Hold_TriggersAfterHoldTime_EveryTick()
        => Run(new HoldTrigger { HoldSeconds = 0.3f }, 1, 1, 1, 1, 0).Should().Equal(Ongoing, Ongoing, Triggered, Triggered, Idle);

    [Fact]
    public void Hold_OneShot_TriggersOnce()
        => Run(new HoldTrigger { HoldSeconds = 0.2f, OneShot = true }, 1, 1, 1, 1, 0, 1, 1)
            .Should().Equal(Ongoing, Triggered, Idle, Idle, Idle, Ongoing, Triggered);

    [Fact]
    public void Hold_ReleasedEarly_GoesIdle()
        => Run(new HoldTrigger { HoldSeconds = 0.5f }, 1, 1, 0).Should().Equal(Ongoing, Ongoing, Idle);

    [Fact]
    public void HoldRelease_TriggersOnReleaseAfterHoldTime()
        => Run(new HoldReleaseTrigger { HoldSeconds = 0.2f }, 1, 1, 1, 0, 0).Should().Equal(Ongoing, Ongoing, Ongoing, Triggered, Idle);

    [Fact]
    public void HoldRelease_ReleasedTooEarly_IsIdle()
        => Run(new HoldReleaseTrigger { HoldSeconds = 0.5f }, 1, 1, 0).Should().Equal(Ongoing, Ongoing, Idle);

    [Fact]
    public void Tap_QuickRelease_Triggers()
        => Run(new TapTrigger { TapSeconds = 0.2f }, 1, 1, 0, 0).Should().Equal(Ongoing, Ongoing, Triggered, Idle);

    [Fact]
    public void Tap_HeldTooLong_GoesIdleUntilReleased()
        => Run(new TapTrigger { TapSeconds = 0.2f }, 1, 1, 1, 1, 0).Should().Equal(Ongoing, Ongoing, Idle, Idle, Idle);

    [Fact]
    public void Pulse_TriggersOnStartThenEveryInterval()
        => Run(new PulseTrigger { IntervalSeconds = 0.3f }, 1, 1, 1, 1, 1, 1, 1, 0)
            .Should().Equal(Triggered, Ongoing, Ongoing, Triggered, Ongoing, Ongoing, Triggered, Idle);

    [Fact]
    public void Pulse_WithoutStartPulse()
        => Run(new PulseTrigger { IntervalSeconds = 0.2f, TriggerOnStart = false }, 1, 1, 1)
            .Should().Equal(Ongoing, Ongoing, Triggered);

    [Fact]
    public void Pulse_MaxPulses_ThenIdleUntilRelease()
        => Run(new PulseTrigger { IntervalSeconds = 0.1f, MaxPulses = 2 }, 1, 1, 1, 1, 0, 1)
            .Should().Equal(Triggered, Triggered, Idle, Idle, Idle, Triggered);

    [Fact]
    public void Roles_AnyExplicitTriggers()
        => Set(1f, new PressedTrigger { ActuationThreshold = 2f }, new DownTrigger()).Should().Be(Triggered);

    [Fact]
    public void Roles_ExplicitProgress_IsOngoing()
        => Set(1f, new HoldTrigger { HoldSeconds = 1f }).Should().Be(Ongoing);

    [Fact]
    public void Roles_AllImplicitMustTrigger()
    {
        var always = new FixedTrigger(TriggerRole.Implicit, Triggered);
        var never = new FixedTrigger(TriggerRole.Implicit, Idle);
        Set(1f, always).Should().Be(Triggered, "no explicit triggers: a non-zero value satisfies the explicit side");
        Set(1f, always, never).Should().Be(Ongoing, "the value shows progress but one implicit trigger is missing");
        Set(0f, always).Should().Be(Idle, "an implicit trigger alone does not start anything");
    }

    [Fact]
    public void Roles_BlockerWins()
    {
        var blocker = new FixedTrigger(TriggerRole.Blocker, Triggered);
        Set(1f, new DownTrigger(), blocker).Should().Be(Idle);
        Set(1f, new DownTrigger(), new FixedTrigger(TriggerRole.Blocker, Idle)).Should().Be(Triggered);
    }

    [Fact]
    public void Roles_ImplicitOngoing_KeepsSetOngoing()
        => Set(0f, new DownTrigger(), new FixedTrigger(TriggerRole.Implicit, Ongoing)).Should().Be(Ongoing);

    [Fact]
    public void Memory_IsPerInstance_SoSharedResourcesDoNotInterfere()
    {
        var hold = new HoldTrigger { HoldSeconds = 0.2f };
        var a = new TriggerMemory();
        var b = new TriggerMemory();
        hold.Evaluate(new TriggerContext(Vector3.Right, ActionValueKind.Digital, Step, null, a));
        hold.Evaluate(new TriggerContext(Vector3.Right, ActionValueKind.Digital, Step, null, a)).Should().Be(Triggered);
        hold.Evaluate(new TriggerContext(Vector3.Right, ActionValueKind.Digital, Step, null, b)).Should().Be(Ongoing);
    }
}

/// <summary>A test trigger that always reports the same phase with a chosen role.</summary>
public partial class FixedTrigger : InputTrigger
{
    private readonly TriggerRole _role;
    private readonly TriggerPhase _phase;

    public FixedTrigger() { }

    public FixedTrigger(TriggerRole role, TriggerPhase phase)
    {
        _role = role;
        _phase = phase;
    }

    public override TriggerRole Role => _role;

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated) => _phase;
}
