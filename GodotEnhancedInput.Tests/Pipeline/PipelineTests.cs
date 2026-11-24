using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>ACT-3, ACT-4, SRC-10, TRG-9 … TRG-14, EVT-6, GD-2.</summary>
[Collection("Godot")]
public class PipelineTests(EngineFixture fixture)
{
    [Fact]
    public void Accumulation_Strongest_PicksLargestPerAxis()
    {
        var move = Make.Action("Move", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(move, InputSource.ForVirtual("a")),
            Make.Bind(move, InputSource.ForVirtual("b"))));

        rig.Player.SetVirtualControl("a", new Vector2(0.3f, -0.9f));
        rig.Player.SetVirtualControl("b", new Vector2(-0.5f, 0.2f));
        rig.Tick();
        rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(-0.5f, -0.9f));
    }

    [Fact]
    public void Accumulation_Sum_OppositeKeysCancel()
    {
        var move = Make.Action("Move", ActionValueKind.Axis1D);
        move.Accumulation = ValueAccumulation.Sum;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(move, Key.D),
            Make.Bind(move, Key.A, new NegateModifier())));

        rig.Press(Key.A).Tick();
        rig.Player.GetValue(move).AsFloat().Should().Be(-1f);
        rig.Press(Key.D).Tick();
        rig.Player.GetValue(move).AsFloat().Should().Be(0f);
    }

    [Fact]
    public void KeyOnAxisAction_BuildsAVectorFromModifiers()
    {
        // The classic "four keys, four bindings" layout: each key feeds X, modifiers place it.
        var move = Make.Action("Move", ActionValueKind.Axis2D);
        move.Accumulation = ValueAccumulation.Sum;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(move, Key.D),
            Make.Bind(move, Key.A, new NegateModifier()),
            Make.Bind(move, Key.S, new SwizzleModifier { Order = SwizzleOrder.YXZ }),
            Make.Bind(move, Key.W, new SwizzleModifier { Order = SwizzleOrder.YXZ }, new NegateModifier())));

        rig.Press(Key.W).Press(Key.A).Tick();
        rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(-1f, -1f));
    }

    [Fact]
    public void Actions_AreComparedByIdentity()
    {
        var first = Make.Action("Same");
        var second = Make.Action("Same");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(first, Key.Space)));

        rig.Press(Key.Space).Tick();
        rig.Player.IsTriggered(first).Should().BeTrue();
        rig.Player.IsTriggered(second).Should().BeFalse();
    }

    [Fact]
    public void SharedSourceResource_IsIndependentPerPlayer()
    {
        var jump = Make.Action("Jump");
        var context = Make.Context("C", Make.Bind(jump, InputSource.ForKey(Key.Space), new HoldTrigger { HoldSeconds = 0.05f }));
        using var one = new Rig().Add(context);
        using var two = new Rig().Add(context);

        one.Press(Key.Space).Tick(5);
        two.Tick(5);
        one.Player.IsTriggered(jump).Should().BeTrue();
        two.Player.GetPhase(jump).Should().Be(TriggerPhase.Idle, "the same resources hold no per-player state");
    }

    [Fact]
    public void Hold_CompletesWithoutFurtherEvents()
    {
        var charge = Make.Action("Charge");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(charge, Key.F, new HoldTrigger { HoldSeconds = 0.5f })));

        rig.Press(Key.F).Tick();
        rig.Player.WasStarted(charge).Should().BeTrue();
        rig.TickFor(0.5);
        rig.Player.IsTriggered(charge).Should().BeTrue("ticks advance time even when no new events arrive");
    }

    [Fact]
    public void Pulse_RepeatsWhileHeld()
    {
        var fire = Make.Action("Fire");
        int triggers = 0;
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(fire, Key.J, new PulseTrigger { IntervalSeconds = 0.25f })));
        rig.Player.Bind(fire, ActionEvents.Triggered, _ => triggers++);

        rig.Press(Key.J).TickFor(1.0);
        triggers.Should().Be(5, "the press counts from the start of its frame: one on press, then at 0.25, 0.5, 0.75 and 1.0 s");
    }

    [Fact]
    public void Chord_RequiresBothActions()
    {
        var shift = Make.Action("Shift");
        var dash = Make.Action("Dash");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(shift, Key.Shift),
            Make.Bind(dash, Key.Space, new ChordTrigger { ChordAction = shift })));

        rig.Press(Key.Space).Tick();
        rig.Player.IsTriggered(dash).Should().BeFalse();
        rig.Player.GetPhase(dash).Should().Be(TriggerPhase.Ongoing, "the key is down but the chord is missing");

        rig.Release(Key.Space).Tick();
        rig.Player.WasCanceled(dash).Should().BeTrue();

        rig.Press(Key.Shift).Tick();
        rig.Player.GetPhase(dash).Should().Be(TriggerPhase.Idle, "the chord alone does not start the action");

        rig.Press(Key.Space).Tick();
        rig.Player.IsTriggered(dash).Should().BeTrue();
    }

    [Fact]
    public void Chord_WorksWhicheverOrderActionsAreEvaluated()
    {
        var shift = Make.Action("Shift");
        var dash = Make.Action("Dash");
        // The dependent action comes first in the context.
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(dash, Key.Space, new ChordTrigger { ChordAction = shift }),
            Make.Bind(shift, Key.Shift)));

        rig.Press(Key.Shift).Press(Key.Space).Tick();
        rig.Player.IsTriggered(dash).Should().BeTrue();
    }

    [Fact]
    public void ChordBlocker_SilencesPlainAction()
    {
        var shift = Make.Action("Shift");
        var dash = Make.Action("Dash");
        var jump = Make.Action("Jump");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(shift, Key.Shift),
            Make.Bind(dash, Key.Space, new ChordTrigger { ChordAction = shift }),
            Make.Bind(jump, Key.Space, new ChordBlockerTrigger { BlockingAction = dash })));

        rig.Press(Key.Space).Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue();
        rig.Release(Key.Space).Tick();

        rig.Press(Key.Shift).Press(Key.Space).Tick();
        rig.Player.IsTriggered(dash).Should().BeTrue();
        rig.Player.IsTriggered(jump).Should().BeFalse();
    }

    [Fact]
    public void Combo_FiresOnSequence()
    {
        var a = Make.Action("A");
        var b = Make.Action("B");
        var special = Make.Action("Special");
        var combo = new ComboTrigger();
        combo.Steps.Add(ComboStep.Create(a));
        combo.Steps.Add(ComboStep.Create(b, timeLimit: 0.5f));
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(special, InputSource.Nothing(), combo),
            Make.Bind(a, Key.J, new PressedTrigger()),
            Make.Bind(b, Key.K, new PressedTrigger())));

        rig.Press(Key.J).Tick();
        rig.Player.GetPhase(special).Should().Be(TriggerPhase.Ongoing);
        rig.Release(Key.J).Tick(3);
        rig.Press(Key.K).Tick();
        rig.Player.WasTriggered(special).Should().BeTrue();
        rig.Tick();
        rig.Player.WasCompleted(special).Should().BeTrue();
    }

    [Fact]
    public void Combo_TimesOut()
    {
        var a = Make.Action("A");
        var b = Make.Action("B");
        var special = Make.Action("Special");
        var combo = new ComboTrigger();
        combo.Steps.Add(ComboStep.Create(a));
        combo.Steps.Add(ComboStep.Create(b, timeLimit: 0.2f));
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(special, InputSource.Nothing(), combo),
            Make.Bind(a, Key.J, new PressedTrigger()),
            Make.Bind(b, Key.K, new PressedTrigger())));

        rig.Press(Key.J).Tick().Release(Key.J).TickFor(0.5);
        rig.Player.GetPhase(special).Should().Be(TriggerPhase.Idle);
        rig.Press(Key.K).Tick();
        rig.Player.IsTriggered(special).Should().BeFalse();
    }

    [Fact]
    public void Combo_WrongOrderAndCancelActions_Reset()
    {
        var a = Make.Action("A");
        var b = Make.Action("B");
        var c = Make.Action("C");
        var special = Make.Action("Special");
        var combo = new ComboTrigger();
        combo.Steps.Add(ComboStep.Create(a));
        combo.Steps.Add(ComboStep.Create(b));
        combo.Steps.Add(ComboStep.Create(c));
        combo.CancelActions.Add(c);
        using var rig = new Rig().Add(Make.Context("Ctx",
            Make.Bind(special, InputSource.Nothing(), combo),
            Make.Bind(a, Key.J, new PressedTrigger()),
            Make.Bind(b, Key.K, new PressedTrigger()),
            Make.Bind(c, Key.L, new PressedTrigger())));

        // B before A: nothing.
        rig.Press(Key.K).Tick().Release(Key.K).Tick();
        rig.Player.GetPhase(special).Should().Be(TriggerPhase.Idle);

        // A then C: C is a cancel action, so the combo resets.
        rig.Press(Key.J).Tick().Release(Key.J).Tick();
        rig.Player.GetPhase(special).Should().Be(TriggerPhase.Ongoing);
        rig.Press(Key.L).Tick();
        rig.Player.GetPhase(special).Should().Be(TriggerPhase.Idle);
    }

    [Fact]
    public void ActionLevelTriggers_GateBindingPhase()
    {
        var charge = Make.Action("Charge");
        charge.Triggers.Add(new HoldTrigger { HoldSeconds = 0.1f });
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(charge, Key.F)));

        rig.Press(Key.F).Tick();
        rig.Player.GetPhase(charge).Should().Be(TriggerPhase.Ongoing);
        rig.TickFor(0.1);
        rig.Player.IsTriggered(charge).Should().BeTrue();
    }

    [Fact]
    public void ActionLevelModifiers_ApplyToCombinedValue()
    {
        var look = Make.Action("Look", ActionValueKind.Axis2D);
        look.Modifiers.Add(new ScaleModifier { Scale = new Vector3(2, 2, 1) });
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(look, InputSource.ForMouseMotion(), new ScaleModifier { Scale = new Vector3(0.5f, 0.5f, 1) })));

        rig.Player.FeedEvent(new InputEventMouseMotion { Relative = new Vector2(3, -1) });
        rig.Tick();
        rig.Player.GetValue(look).AsVector2().Should().Be(new Vector2(3, -1));
    }

    [Fact]
    public void AnalogTrigger_DigitalAction_RespectsThreshold()
    {
        var shoot = Make.Action("Shoot");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(shoot, InputSource.ForJoyAxis(JoyAxis.TriggerRight), new DownTrigger { ActuationThreshold = 0.5f })));

        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 0.3f });
        rig.Tick();
        rig.Player.IsTriggered(shoot).Should().BeFalse();
        rig.Player.GetState(shoot).RawValue.AsFloat().Should().BeApproximately(0.3f, 1e-6f, "the raw value keeps the strength");
        rig.Player.GetValue(shoot).AsFloat().Should().Be(0f, "below the threshold the binding does not fire");

        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 0.8f });
        rig.Tick();
        rig.Player.IsTriggered(shoot).Should().BeTrue();
    }

    [Fact]
    public void Paused_OnlyPauseActionsRun()
    {
        var jump = Make.Action("Jump");
        var unpause = Make.Action("Unpause");
        unpause.TriggerWhenPaused = true;
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space), Make.Bind(unpause, Key.Escape)));
        fixture.Tree.Root.AddChild(rig.Player);
        try
        {
            rig.Press(Key.Space).Tick();
            rig.Player.IsTriggered(jump).Should().BeTrue();

            fixture.Tree.Paused = true;
            rig.Press(Key.Escape).Tick();
            rig.Player.WasCanceled(jump).Should().BeTrue("paused actions lose their input");
            rig.Player.GetState(jump).EndReason.Should().Be(InputEndReason.Paused);
            rig.Player.IsTriggered(unpause).Should().BeTrue();
        }
        finally
        {
            fixture.Tree.Paused = false;
        }
    }

    [Fact]
    public void ManualTick_DrivesEverything_WithSystemNode()
    {
        var jump = Make.Action("Jump");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(jump, Key.Space)));
            system.FeedEvent(Rig.KeyEvent(Key.Space, true));
            system.Tick(Rig.Dt);
            player.IsTriggered(jump).Should().BeTrue();
        }
        finally
        {
            system.Free();
        }
    }

    [Fact]
    public void System_RoutesEventsByDevice()
    {
        var jump = Make.Action("Jump");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var first = system.GetPlayer(0);
            first.JoypadDevices.Add(0);
            var second = system.AddPlayer();
            second.JoypadDevices.Add(1);
            second.AcceptKeyboardMouse = false;
            var context = Make.Context("C", Make.Bind(jump, InputSource.ForJoyButton(JoyButton.A)), Make.Bind(jump, Key.Space));
            first.AddContext(context);
            second.AddContext(context);

            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 1 });
            system.Tick(Rig.Dt);
            first.IsTriggered(jump).Should().BeFalse();
            second.IsTriggered(jump).Should().BeTrue();

            system.FeedEvent(Rig.KeyEvent(Key.Space, true));
            system.Tick(Rig.Dt);
            first.IsTriggered(jump).Should().BeTrue();
            second.PlayerIndex.Should().Be(1);
        }
        finally
        {
            system.Free();
        }
    }
}
