using FluentAssertions;
using Godot;
using static GodotEnhancedInput.ActionEvents;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>EVT-1 (one test per transition) and EVT-2 (timings).</summary>
[Collection("Godot")]
public class StateMachineTests
{
    /// <summary>
    /// An action driven by a virtual control whose single trigger reports whatever phase the test asks for,
    /// so every transition can be produced directly.
    /// </summary>
    private sealed class Scripted : IDisposable
    {
        public readonly Rig Rig = new();
        public readonly GameAction Action = Make.Action("Scripted");
        private readonly ScriptedTrigger _trigger = new();

        public Scripted()
        {
            Rig.Add(Make.Context("C", Make.Bind(Action, InputSource.ForVirtual("v"), _trigger)));
        }

        public ActionEvents Step(TriggerPhase phase)
        {
            _trigger.Next = phase;
            Rig.Player.SetVirtualControl("v", 1f);
            Rig.Tick();
            return Rig.Player.GetEvents(Action);
        }

        public void Dispose() => Rig.Dispose();
    }

    [Fact] public void IdleToIdle_RaisesNothing() { using var s = new Scripted(); s.Step(TriggerPhase.Idle).Should().Be(None); }
    [Fact] public void IdleToOngoing_StartedAndOngoing() { using var s = new Scripted(); s.Step(TriggerPhase.Ongoing).Should().Be(Started | Ongoing); }
    [Fact] public void IdleToTriggered_StartedAndTriggered() { using var s = new Scripted(); s.Step(TriggerPhase.Triggered).Should().Be(Started | Triggered); }

    [Fact]
    public void OngoingToOngoing_Ongoing()
    {
        using var s = new Scripted();
        s.Step(TriggerPhase.Ongoing);
        s.Step(TriggerPhase.Ongoing).Should().Be(Ongoing);
    }

    [Fact]
    public void OngoingToTriggered_Triggered()
    {
        using var s = new Scripted();
        s.Step(TriggerPhase.Ongoing);
        s.Step(TriggerPhase.Triggered).Should().Be(Triggered);
    }

    [Fact]
    public void OngoingToIdle_Canceled()
    {
        using var s = new Scripted();
        s.Step(TriggerPhase.Ongoing);
        s.Step(TriggerPhase.Idle).Should().Be(Canceled);
    }

    [Fact]
    public void TriggeredToTriggered_Triggered()
    {
        using var s = new Scripted();
        s.Step(TriggerPhase.Triggered);
        s.Step(TriggerPhase.Triggered).Should().Be(Triggered);
    }

    [Fact]
    public void TriggeredToOngoing_Ongoing()
    {
        using var s = new Scripted();
        s.Step(TriggerPhase.Triggered);
        s.Step(TriggerPhase.Ongoing).Should().Be(Ongoing);
    }

    [Fact]
    public void TriggeredToIdle_Completed()
    {
        using var s = new Scripted();
        s.Step(TriggerPhase.Triggered);
        s.Step(TriggerPhase.Idle).Should().Be(Completed);
    }

    [Fact]
    public void Timing_ElapsedAndTriggeredSeconds()
    {
        using var s = new Scripted();
        const double dt = Rig.Dt;
        var state = s.Rig.Player.GetState(s.Action);

        s.Step(TriggerPhase.Ongoing);
        s.Step(TriggerPhase.Ongoing);
        state.ElapsedSeconds.Should().BeApproximately(2 * dt, 1e-9);
        state.TriggeredSeconds.Should().Be(0);

        s.Step(TriggerPhase.Triggered);
        s.Step(TriggerPhase.Triggered);
        state.ElapsedSeconds.Should().BeApproximately(4 * dt, 1e-9);
        state.TriggeredSeconds.Should().BeApproximately(2 * dt, 1e-9);

        s.Step(TriggerPhase.Idle);
        state.Events.Should().Be(Completed);
        state.ElapsedSeconds.Should().BeApproximately(4 * dt, 1e-9, "the completing tick still reports the totals");

        s.Step(TriggerPhase.Idle);
        state.ElapsedSeconds.Should().Be(0);
        state.TriggeredSeconds.Should().Be(0);

        s.Step(TriggerPhase.Triggered);
        state.ElapsedSeconds.Should().BeApproximately(dt, 1e-9, "a new start resets the timings");
    }
}

/// <summary>A trigger whose phase is set by the test.</summary>
public partial class ScriptedTrigger : InputTrigger
{
    public TriggerPhase Next { get; set; }

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated) => Next;
}
