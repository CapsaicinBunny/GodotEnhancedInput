using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>EVT-3, EVT-4, EVT-5, EVT-7.</summary>
[Collection("Godot")]
public class EventTests
{
    [Fact]
    public void Bind_ReceivesEachRequestedEvent_InOrder()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));
        var seen = new List<ActionEvents>();
        rig.Player.Bind(jump, ActionEvents.All, e => seen.Add(e.Event));

        rig.Press(Key.Space).Tick().Tick().Release(Key.Space).Tick();

        seen.Should().Equal(ActionEvents.Started, ActionEvents.Triggered, ActionEvents.Triggered, ActionEvents.Completed);
    }

    [Fact]
    public void Bind_FiltersByEvent_AndCarriesValue()
    {
        var move = Make.Action("Move", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(move, InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D))));
        var values = new List<Vector2>();
        rig.Player.Bind(move, ActionEvents.Triggered, e => values.Add(e.Value.AsVector2()));

        rig.Press(Key.D).Tick().Release(Key.D).Tick();

        values.Should().Equal(new Vector2(1, 0));
    }

    [Fact]
    public void Bind_HandleUnsubscribes()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));
        int count = 0;
        var handle = rig.Player.Bind(jump, ActionEvents.Triggered, _ => count++);

        rig.Press(Key.Space).Tick();
        handle.Dispose();
        rig.Tick();

        count.Should().Be(1);
        handle.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Bind_UnsubscribingDuringDispatch_IsSafe()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));
        int count = 0;
        InputPlayer.ActionSubscription handle = null;
        handle = rig.Player.Bind(jump, ActionEvents.Triggered, _ => { count++; handle.Dispose(); });
        rig.Player.Bind(jump, ActionEvents.Triggered, _ => count += 10);

        rig.Press(Key.Space).Tick().Tick();

        count.Should().Be(21);
    }

    [Fact]
    public void Bind_ThrowingCallback_DoesNotStopOthers()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));
        bool second = false;
        rig.Player.Bind(jump, ActionEvents.Triggered, _ => throw new InvalidOperationException("test"));
        rig.Player.Bind(jump, ActionEvents.Triggered, _ => second = true);

        rig.Press(Key.Space).Tick();

        second.Should().BeTrue();
    }

    [Fact]
    public void Poll_ReflectsLastTick()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space, new PressedTrigger())));

        rig.Press(Key.Space).Tick();
        rig.Player.WasStarted(jump).Should().BeTrue();
        rig.Player.WasTriggered(jump).Should().BeTrue();
        rig.Player.IsTriggered(jump).Should().BeTrue();
        rig.Player.GetValue(jump).AsBool().Should().BeTrue();

        rig.Tick();
        rig.Player.WasTriggered(jump).Should().BeFalse();
        rig.Player.WasCompleted(jump).Should().BeTrue();
        rig.Player.GetState(jump).RawValue.AsBool().Should().BeTrue("the raw value is reported whatever the triggers");
        rig.Player.GetValue(jump).AsBool().Should().BeFalse("a binding held back by its trigger adds no value");

        rig.Tick();
        rig.Player.GetEvents(jump).Should().Be(ActionEvents.None);
    }

    [Fact]
    public void Poll_UnknownAction_IsIdleZero()
    {
        using var rig = new Rig();
        var unknown = Make.Action("Unknown", ActionValueKind.Axis2D);
        rig.Player.GetPhase(unknown).Should().Be(TriggerPhase.Idle);
        rig.Player.GetValue(unknown).Should().Be(ActionValue.Zero(ActionValueKind.Axis2D));
        rig.Player.WasTriggered(unknown).Should().BeFalse();
    }

    [Fact]
    public void Signals_AreEmitted()
    {
        var charge = Make.Action("Charge");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(charge, Key.F, new HoldTrigger { HoldSeconds = 0.05f })));
        var log = new List<string>();
        rig.Player.ActionStarted += a => log.Add("started");
        rig.Player.ActionOngoing += a => log.Add("ongoing");
        rig.Player.ActionTriggered += a => log.Add("triggered");
        rig.Player.ActionCompleted += a => log.Add("completed");
        rig.Player.ActionCanceled += a => log.Add("canceled");

        rig.Press(Key.F).Tick().Release(Key.F).Tick();      // started, ongoing, canceled
        rig.Press(Key.F).Tick(3).Release(Key.F).Tick();     // started, ongoing, ongoing, triggered, completed

        log.Should().Equal("started", "ongoing", "canceled", "started", "ongoing", "ongoing", "triggered", "completed");
    }

    [Fact]
    public void ActionEventRaised_StreamsEverything()
    {
        var a = Make.Action("A");
        var b = Make.Action("B");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(a, Key.Q), Make.Bind(b, Key.E)));
        var seen = new List<(string, ActionEvents)>();
        rig.Player.ActionEventRaised += e => seen.Add((e.Action.Name, e.Event));

        rig.Press(Key.Q).Press(Key.E).Tick();

        seen.Should().BeEquivalentTo(new[] { ("A", ActionEvents.Started), ("A", ActionEvents.Triggered), ("B", ActionEvents.Started), ("B", ActionEvents.Triggered) });
    }

    [Fact]
    public void Callbacks_SeeConsistentState()
    {
        var first = Make.Action("First");
        var second = Make.Action("Second");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(first, Key.Q), Make.Bind(second, Key.Q)));
        bool secondSeenTriggered = false;
        rig.Player.Bind(first, ActionEvents.Triggered, _ => secondSeenTriggered = rig.Player.IsTriggered(second));

        rig.Press(Key.Q).Tick();

        secondSeenTriggered.Should().BeTrue("callbacks run after the whole tick has been evaluated");
    }

    [Fact]
    public void Inject_OneTick()
    {
        var jump = Make.Action("Jump");
        using var rig = new Rig();
        rig.Player.InjectInput(jump, true);
        rig.Tick();
        rig.Player.WasTriggered(jump).Should().BeTrue("injection works without any context");
        rig.Tick();
        rig.Player.WasCompleted(jump).Should().BeTrue();
    }

    [Fact]
    public void Inject_Continuous_UntilStopped()
    {
        var move = Make.Action("Move", ActionValueKind.Axis2D);
        using var rig = new Rig();
        rig.Player.StartInjecting(move, new Vector3(0, -1, 0));
        rig.Tick(3);
        rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(0, -1));
        rig.Player.StopInjecting(move);
        rig.Tick();
        rig.Player.WasCompleted(move).Should().BeTrue();
    }

    [Fact]
    public void Inject_GoesThroughActionModifiersAndTriggers()
    {
        var charge = Make.Action("Charge", ActionValueKind.Axis1D);
        charge.Modifiers.Add(new ScaleModifier { Scale = new Vector3(3, 1, 1) });
        charge.Triggers.Add(new HoldTrigger { HoldSeconds = 0.05f });
        using var rig = new Rig();
        rig.Player.StartInjecting(charge, Vector3.Right);

        rig.Tick();
        rig.Player.GetPhase(charge).Should().Be(TriggerPhase.Ongoing);
        rig.Player.GetValue(charge).AsFloat().Should().Be(3f);
        rig.Tick(3);
        rig.Player.IsTriggered(charge).Should().BeTrue();
    }
}
