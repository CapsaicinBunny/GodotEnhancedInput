using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>CTX-1 … CTX-7.</summary>
[Collection("Godot")]
public class ContextTests
{
    [Fact]
    public void AddRemove_ChangesWhatIsBound()
    {
        var jump = Make.Action("Jump");
        var context = Make.Context("Gameplay", Make.Bind(jump, Key.Space));
        using var rig = new Rig();

        rig.Press(Key.Space).Tick();
        rig.Player.IsTriggered(jump).Should().BeFalse("no context is active");

        rig.Add(context).Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue();
        rig.Player.HasContext(context).Should().BeTrue();

        rig.Player.RemoveContext(context).Should().BeTrue();
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeFalse();
        rig.Player.HasContext(context).Should().BeFalse();
        rig.Player.RemoveContext(context).Should().BeFalse();
    }

    [Fact]
    public void ReAdd_UpdatesPriority()
    {
        var a = Make.Context("A");
        var b = Make.Context("B");
        using var rig = new Rig().Add(a, 5).Add(b, 1);
        rig.Player.GetContexts().Should().Equal(a, b);

        rig.Add(b, 10);
        rig.Player.GetContexts().Should().Equal(b, a);
        rig.Player.GetContextPriority(b).Should().Be(10);
        rig.Player.GetContexts().Should().HaveCount(2, "re-adding does not duplicate");
    }

    [Fact]
    public void EqualPriority_MostRecentlyAddedComesFirst()
    {
        var a = Make.Context("A");
        var b = Make.Context("B");
        using var rig = new Rig().Add(a).Add(b);
        rig.Player.GetContexts().Should().Equal(b, a);
    }

    [Fact]
    public void ContextsWithSameName_AreDistinct()
    {
        var jump = Make.Action("Jump");
        var first = Make.Context("Same", Make.Bind(jump, Key.Space));
        var second = Make.Context("Same");
        using var rig = new Rig().Add(first).Add(second);

        rig.Player.GetContexts().Should().HaveCount(2);
        rig.Player.RemoveContext(second);
        rig.Player.HasContext(first).Should().BeTrue();
    }

    [Fact]
    public void HigherContext_ConsumesSharedKey()
    {
        var confirm = Make.Action("Confirm");
        var jump = Make.Action("Jump");
        var menu = Make.Context("Menu", Make.Bind(confirm, Key.Space, new PressedTrigger()));
        var gameplay = Make.Context("Gameplay", Make.Bind(jump, Key.Space));
        using var rig = new Rig().Add(gameplay, 0).Add(menu, 10);

        rig.Press(Key.Space).Tick();
        rig.Player.WasTriggered(confirm).Should().BeTrue();
        rig.Player.IsTriggered(jump).Should().BeFalse();

        rig.Tick(5);
        rig.Player.IsTriggered(jump).Should().BeFalse("the key stays consumed while held, not only while the menu action fires");
    }

    [Fact]
    public void NonConsumingAction_LetsKeyThrough()
    {
        var peek = Make.Action("Peek");
        peek.ConsumeInput = false;
        var jump = Make.Action("Jump");
        using var rig = new Rig()
            .Add(Make.Context("Gameplay", Make.Bind(jump, Key.Space)), 0)
            .Add(Make.Context("Overlay", Make.Bind(peek, Key.Space)), 10);

        rig.Press(Key.Space).Tick();
        rig.Player.IsTriggered(peek).Should().BeTrue();
        rig.Player.IsTriggered(jump).Should().BeTrue();
    }

    [Fact]
    public void SameContext_BothBindingsSeeKey()
    {
        var a = Make.Action("A");
        var b = Make.Action("B");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(a, Key.E), Make.Bind(b, Key.E)));

        rig.Press(Key.E).Tick();
        rig.Player.IsTriggered(a).Should().BeTrue();
        rig.Player.IsTriggered(b).Should().BeTrue();
    }

    [Fact]
    public void Consumption_IsPerKey_ForComposites()
    {
        var move = Make.Action("Move", ActionValueKind.Axis2D);
        var interact = Make.Action("Interact");
        using var rig = new Rig()
            .Add(Make.Context("Gameplay", Make.Bind(move, InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D))), 0)
            .Add(Make.Context("Prompt", Make.Bind(interact, Key.W)), 10);

        rig.Press(Key.W).Press(Key.D).Tick();
        rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(1f, 0f), "W is consumed, D is not");
        rig.Player.IsTriggered(interact).Should().BeTrue();
    }

    [Fact]
    public void BlockingContext_SilencesLowerContexts()
    {
        var jump = Make.Action("Jump");
        var pause = Make.Context("Pause");
        pause.BlockLowerPriority = true;
        using var rig = new Rig().Add(Make.Context("Gameplay", Make.Bind(jump, Key.Space)), 0).Add(pause, 100);

        rig.Press(Key.Space).Tick();
        rig.Player.IsTriggered(jump).Should().BeFalse();

        rig.Player.RemoveContext(pause);
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue();
    }

    [Fact]
    public void IgnoreHeldUntilRelease()
    {
        var close = Make.Action("Close");
        var menu = Make.Context("Menu", Make.Bind(close, Key.Escape));
        using var rig = new Rig();

        rig.Press(Key.Escape).Tick();
        rig.Add(menu, 10, new ContextOptions { IgnoreHeldSourcesUntilReleased = true });
        rig.Tick(3);
        rig.Player.IsTriggered(close).Should().BeFalse("the key that opened the menu is still held");

        rig.Release(Key.Escape).Tick();
        rig.Press(Key.Escape).Tick();
        rig.Player.IsTriggered(close).Should().BeTrue();
    }

    [Fact]
    public void RemovingContext_CancelsTriggeredAction_WithItsReason()
    {
        var jump = Make.Action("Jump");
        var context = Make.Context("Gameplay", Make.Bind(jump, Key.Space));
        int completed = 0, canceled = 0;
        using var rig = new Rig().Add(context);
        rig.Player.Bind(jump, ActionEvents.Completed, _ => completed++);
        rig.Player.Bind(jump, ActionEvents.Canceled, _ => canceled++);

        rig.Press(Key.Space).Tick();
        rig.Player.RemoveContext(context);
        rig.Tick();

        completed.Should().Be(0, "the key was not released");
        canceled.Should().Be(1);
        rig.Player.GetState(jump).EndReason.Should().Be(InputEndReason.ContextRemoved);
        rig.Player.GetPhase(jump).Should().Be(TriggerPhase.Idle);
    }

    [Fact]
    public void RemovingContext_CancelsOngoingAction()
    {
        var charge = Make.Action("Charge");
        var context = Make.Context("Gameplay", Make.Bind(charge, Key.F, new HoldTrigger { HoldSeconds = 1f }));
        using var rig = new Rig().Add(context);

        rig.Press(Key.F).Tick();
        rig.Player.GetPhase(charge).Should().Be(TriggerPhase.Ongoing);
        rig.Player.RemoveContext(context);
        rig.Tick();
        rig.Player.WasCanceled(charge).Should().BeTrue();
    }

    [Fact]
    public void Signals_AddedRemoved()
    {
        var context = Make.Context("C");
        using var rig = new Rig();
        BindingContext added = null, removed = null;
        int addedPriority = -1;
        rig.Player.ContextAdded += (c, p) => { added = c; addedPriority = p; };
        rig.Player.ContextRemoved += c => removed = c;

        rig.Add(context, 7);
        rig.Player.RemoveContext(context);

        added.Should().BeSameAs(context);
        addedPriority.Should().Be(7);
        removed.Should().BeSameAs(context);
    }
}
