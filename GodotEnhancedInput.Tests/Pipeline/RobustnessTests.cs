using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>
/// ROB-1 … ROB-12: action-level release triggers, suppression, ordered transitions, device identity,
/// stick coherence, trigger-gated values, player isolation, pad identity, focus loss, profile ids.
/// </summary>
[Collection("Godot")]
public class RobustnessTests(EngineFixture fixture)
{
    // ------------------------------------------------------------------
    //  ROB-1  Action-level release, tap and hold-release triggers
    // ------------------------------------------------------------------

    [Fact]
    public void ActionLevel_Released_TriggersOnRelease()
    {
        var drop = Make.Action("drop");
        drop.Triggers.Add(new ReleasedTrigger());
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(drop, Key.G)));

        rig.Press(Key.G).Tick();
        rig.Player.GetPhase(drop).Should().Be(TriggerPhase.Ongoing);
        rig.Release(Key.G).Tick();
        rig.Player.WasTriggered(drop).Should().BeTrue();
    }

    [Fact]
    public void ActionLevel_Tap_And_HoldRelease()
    {
        var tap = Make.Action("tap");
        tap.Triggers.Add(new TapTrigger { TapSeconds = 0.1f });
        var throwIt = Make.Action("throw");
        throwIt.Triggers.Add(new HoldReleaseTrigger { HoldSeconds = 0.05f });
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(tap, Key.T), Make.Bind(throwIt, Key.Y)));

        rig.Press(Key.T).Press(Key.Y).Tick(4).Release(Key.T).Release(Key.Y).Tick();
        rig.Player.WasTriggered(tap).Should().BeTrue();
        rig.Player.WasTriggered(throwIt).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  ROB-2  Suppression cancels instead of looking like a release
    // ------------------------------------------------------------------

    [Fact]
    public void Blocking_DoesNotLookLikeARelease()
    {
        var drop = Make.Action("drop");
        int triggered = 0;
        var pause = Make.Context("pause");
        pause.BlockLowerPriority = true;
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(drop, Key.G, new ReleasedTrigger())));
        rig.Player.Bind(drop, ActionEvents.Triggered, _ => triggered++);

        rig.Press(Key.G).Tick();
        rig.Add(pause, 100).Tick();
        rig.Player.WasCanceled(drop).Should().BeTrue();
        rig.Release(Key.G).Tick(2);
        triggered.Should().Be(0);
    }

    [Fact]
    public void Consumption_And_ContextRemoval_DoNotFireActionLevelRelease()
    {
        var drop = Make.Action("drop");
        drop.Triggers.Add(new ReleasedTrigger());
        int triggered = 0;
        var gameplay = Make.Context("C", Make.Bind(drop, Key.G));
        using var rig = new Rig().Add(gameplay);
        rig.Player.Bind(drop, ActionEvents.Triggered, _ => triggered++);

        rig.Press(Key.G).Tick();
        rig.Add(Make.Context("menu", Make.Bind(Make.Action("confirm"), Key.G)), 10).Tick();
        rig.Player.WasCanceled(drop).Should().BeTrue("the key is consumed: canceled, not released");

        rig.Player.ClearContexts();
        rig.Add(gameplay).Tick();
        rig.Player.RemoveContext(gameplay);
        rig.Tick();
        triggered.Should().Be(0);
    }

    [Fact]
    public void Suppression_ResetsSmoothing()
    {
        var look = Make.Action("look", ActionValueKind.Axis1D);
        var gameplay = Make.Context("C", Make.Bind(look, InputSource.ForVirtual("v"), new SmoothingModifier { Mode = SmoothingMode.Average, Samples = 4 }));
        var pause = Make.Context("pause");
        pause.BlockLowerPriority = true;
        using var rig = new Rig().Add(gameplay);

        rig.Player.SetVirtualControl("v", 4f);
        rig.Tick(4);
        rig.Add(pause, 100).Tick();
        rig.Player.GetValue(look).AsFloat().Should().Be(0f);

        rig.Player.RemoveContext(pause);
        rig.Player.SetVirtualControl("v", 1f);
        rig.Tick();
        rig.Player.GetValue(look).AsFloat().Should().Be(1f, "the average starts over instead of mixing in old samples");
    }

    [Fact]
    public void BlockedCombo_DoesNotAdvance()
    {
        var a = Make.Action("a");
        var b = Make.Action("b");
        var special = Make.Action("special");
        var combo = new ComboTrigger();
        combo.Steps.Add(ComboStep.Create(a));
        combo.Steps.Add(ComboStep.Create(b));
        var top = Make.Context("top", Make.Bind(a, Key.J, new PressedTrigger()), Make.Bind(b, Key.K, new PressedTrigger()));
        top.BlockLowerPriority = true;
        using var rig = new Rig().Add(Make.Context("low", Make.Bind(special, InputSource.Nothing(), combo))).Add(top, 10);

        rig.Press(Key.J).Tick().Release(Key.J).Tick().Press(Key.K).Tick();
        rig.Player.GetPhase(special).Should().Be(TriggerPhase.Idle);
    }

    // ------------------------------------------------------------------
    //  ROB-3  No transition is lost between ticks
    // ------------------------------------------------------------------

    [Fact]
    public void TwoPressesBetweenTicks_AreTwoPresses_InOrder()
    {
        var jump = Make.Action("jump");
        int presses = 0;
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space, new PressedTrigger())));
        rig.Player.Bind(jump, ActionEvents.Triggered, _ => presses++);

        rig.Press(Key.Space).Release(Key.Space).Press(Key.Space).Release(Key.Space);
        rig.Tick();

        presses.Should().Be(2, "both presses are evaluated in the frame they arrived, in order");
        rig.Player.LastTickSteps.Should().Be(4);
        rig.Player.GetState(jump).RawValue.AsBool().Should().BeFalse("the frame ends released");
        rig.Tick();
        presses.Should().Be(2);
    }

    [Fact]
    public void WheelNotches_AreCountedOneByOne()
    {
        var next = Make.Action("next_weapon");
        int count = 0;
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(next, InputSource.ForMouseButton(MouseButton.WheelDown), new PressedTrigger())));
        rig.Player.Bind(next, ActionEvents.Triggered, _ => count++);

        for (int i = 0; i < 3; i++)
        {
            rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1f });
            rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false, Factor = 1f });
        }
        rig.Tick(8);
        count.Should().Be(3);
    }

    [Fact]
    public void AxisMovesWithinATick_ReadTheLatestValue()
    {
        var steer = Make.Action("steer", ActionValueKind.Axis1D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(steer, InputSource.ForJoyAxis(JoyAxis.LeftX))));
        foreach (var v in new[] { 0.2f, 0.5f, 0.9f })
            rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = v });
        rig.Tick();
        rig.Player.GetValue(steer).AsFloat().Should().BeApproximately(0.9f, 1e-6f, "changes on the same side of zero do not queue");
    }

    // ------------------------------------------------------------------
    //  ROB-4  Consumption keeps device and key identity
    // ------------------------------------------------------------------

    private static InputEventJoypadButton Pad(int device, JoyButton button = JoyButton.A, bool pressed = true)
        => new() { ButtonIndex = button, Pressed = pressed, Device = device };

    [Fact]
    public void ConsumingOnePadsButton_LeavesOtherPadsAlone()
    {
        var menuConfirm = Make.Action("confirm");
        var jump = Make.Action("jump");
        using var rig = new Rig()
            .Add(Make.Context("low", Make.Bind(jump, InputSource.ForJoyButton(JoyButton.A))))
            .Add(Make.Context("top", Make.Bind(menuConfirm, InputSource.ForJoyButton(JoyButton.A, device: 0))), 10);

        rig.Player.FeedEvent(Pad(0));
        rig.Tick();
        rig.Player.IsTriggered(menuConfirm).Should().BeTrue();
        rig.Player.IsTriggered(jump).Should().BeFalse("pad 0's A is consumed");

        rig.Player.FeedEvent(Pad(1));
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue("pad 1's A is not");
    }

    [Fact]
    public void ConsumingAnyPad_HidesEveryPad()
    {
        var jump = Make.Action("jump");
        using var rig = new Rig()
            .Add(Make.Context("low", Make.Bind(jump, InputSource.ForJoyButton(JoyButton.A, device: 1))))
            .Add(Make.Context("top", Make.Bind(Make.Action("confirm"), InputSource.ForJoyButton(JoyButton.A))), 10);

        rig.Player.FeedEvent(Pad(1));
        rig.Tick();
        rig.Player.IsTriggered(jump).Should().BeFalse();
    }

    [Fact]
    public void LogicalAndPhysicalNamesOfOneKey_ConsumeEachOther()
    {
        var interact = Make.Action("interact");
        // A layout where the key in the Q position produces "A".
        using var rig = new Rig()
            .Add(Make.Context("low", Make.Bind(interact, InputSource.ForKey(Key.A, KeyMatch.Logical))))
            .Add(Make.Context("top", Make.Bind(Make.Action("ability"), InputSource.ForKey(Key.Q))), 10);

        rig.Player.FeedEvent(new InputEventKey { PhysicalKeycode = Key.Q, Keycode = Key.A, Pressed = true });
        rig.Tick();
        rig.Player.IsTriggered(interact).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  ROB-5  Sticks come from one pad
    // ------------------------------------------------------------------

    [Fact]
    public void TwoPads_NeverCombineIntoADiagonal()
    {
        var move = Make.Action("move", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(move, InputSource.ForJoyStick(JoyStickSide.Left))));
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1f, Device = 0 });
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0.6f, Device = 1 });
        rig.Tick();
        rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(1f, 0f), "pad 0 is pushed furthest; its whole stick is used");
    }

    // ------------------------------------------------------------------
    //  ROB-6  Bindings held back by their triggers add no value
    // ------------------------------------------------------------------

    [Fact]
    public void UnsatisfiedChord_AddsNoValue()
    {
        var shift = Make.Action("shift");
        var push = Make.Action("push", ActionValueKind.Axis1D);
        push.Accumulation = ValueAccumulation.Sum;
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(shift, Key.Shift),
            Make.Bind(push, Key.Space, new ChordTrigger { ChordAction = shift }),
            Make.Bind(push, Key.D)));

        rig.Press(Key.Space).Press(Key.D).Tick();
        rig.Player.GetValue(push).AsFloat().Should().Be(1f);
        rig.Player.GetState(push).RawValue.AsFloat().Should().Be(2f);
        rig.Press(Key.Shift).Tick();
        rig.Player.GetValue(push).AsFloat().Should().Be(2f);
    }

    // ------------------------------------------------------------------
    //  ROB-7  Player isolation
    // ------------------------------------------------------------------

    [Fact]
    public void AddedPlayers_DoNotPollTheInputMap_OrTakeUnownedPads()
    {
        const string mapAction = "gei_isolation_test";
        InputMap.AddAction(mapAction);
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var jump = Make.Action("jump");
            var context = Make.Context("C", Make.Bind(jump, InputSource.ForInputMapAction(mapAction)), Make.Bind(jump, InputSource.ForJoyButton(JoyButton.A)));
            var first = system.GetPlayer(0);
            var second = system.AddPlayer();
            first.AddContext(context);
            second.AddContext(context);
            second.PollInputMap.Should().BeFalse();
            second.ReceivesUnownedJoypads.Should().BeFalse();

            Input.ActionPress(mapAction);
            system.Tick(Rig.Dt);
            first.IsTriggered(jump).Should().BeTrue();
            second.IsTriggered(jump).Should().BeFalse();
            Input.ActionRelease(mapAction);

            system.FeedEvent(Pad(6));
            system.Tick(Rig.Dt);
            second.IsTriggered(jump).Should().BeFalse("an unowned pad reaches player 0 only");
        }
        finally
        {
            Input.ActionRelease(mapAction);
            InputMap.EraseAction(mapAction);
            system.Free();
        }
    }

    [Fact]
    public void ReconnectedPadWithAnotherIdentity_DoesNotInheritTheAssignment()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var second = system.AddPlayer();
            InputPlayer lost = null;
            system.JoypadUnassigned += (p, d) => lost = p;
            system.AssignJoypad(second, 3, "pad-a");

            system.NotifyJoypadConnection(3, false);
            system.NotifyJoypadConnection(3, true, "pad-a");
            system.GetJoypadOwner(3).Should().BeSameAs(second, "the same pad came back");

            system.NotifyJoypadConnection(3, false);
            system.NotifyJoypadConnection(3, true, "pad-b");
            system.GetJoypadOwner(3).Should().BeNull("another pad got the same index");
            lost.Should().BeSameAs(second);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void ProfileIds_SurviveReindexing()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var second = system.AddPlayer();
            var third = system.AddPlayer();
            system.RemovePlayer(second);
            third.PlayerIndex.Should().Be(1);
            third.ProfileId.Should().Be("player_2");
            third.DefaultProfilePath.Should().EndWith("player_2.json");
            system.AddPlayer().ProfileId.Should().Be("player_3");
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  ROB-8  Focus loss
    // ------------------------------------------------------------------

    [Fact]
    public void FocusLoss_ReleasesHeldInput()
    {
        var run = Make.Action("run");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(run, Key.Shift)));
            system.FeedEvent(Rig.KeyEvent(Key.Shift, true));
            system.Tick(Rig.Dt);
            player.IsTriggered(run).Should().BeTrue();

            system._Notification((int)Node.NotificationApplicationFocusOut);
            system.Tick(Rig.Dt);
            player.IsTriggered(run).Should().BeFalse();
            player.WasCanceled(run).Should().BeTrue("focus loss cuts input off; it is not a release");
            player.GetState(run).EndReason.Should().Be(InputEndReason.FocusLost);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void FocusLoss_ReleasesOnScreenControls()
    {
        using var rig = new Rig();
        var button = new VirtualButton { ControlName = "b", TargetPlayer = rig.Player, Size = new Vector2(100, 100) };
        fixture.Tree.Root.AddChild(button);
        try
        {
            button.HandlePointerEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = new Vector2(50, 50) });
            button.IsPressed.Should().BeTrue();
            button._Notification((int)Node.NotificationApplicationFocusOut);
            button.IsPressed.Should().BeFalse();
        }
        finally
        {
            button.GetParent().RemoveChild(button);
            button.Free();
        }
    }

    // ------------------------------------------------------------------
    //  ROB-9  GUI: reversing a stick is never held back
    // ------------------------------------------------------------------

    [Fact]
    public void StickReversal_ReleasesBeforeTheGui_AndPressesOnlyIfTheGuiAllows()
    {
        var steer = Make.Action("steer", ActionValueKind.Axis1D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(steer, InputSource.ForJoyAxis(JoyAxis.LeftX))));
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.8f }, IntakeStage.Unhandled);
        rig.Tick();

        // The GUI took the reversal: the old direction is released, the new one never reaches gameplay.
        var reversal = new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -0.9f };
        rig.Player.FeedEvent(reversal, IntakeStage.Early);
        rig.Tick();
        rig.Player.GetValue(steer).AsFloat().Should().Be(0f);

        // The GUI let it through.
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.8f }, IntakeStage.Unhandled);
        rig.Tick();
        rig.Player.FeedEvent(reversal, IntakeStage.Early);
        rig.Player.FeedEvent(reversal, IntakeStage.Unhandled);
        rig.Tick();
        rig.Player.GetValue(steer).AsFloat().Should().BeApproximately(-0.9f, 1e-6f);
    }

    // ------------------------------------------------------------------
    //  ROB-10  Rebind conflicts tell pads apart
    // ------------------------------------------------------------------

    [Fact]
    public void Overlaps_RespectsDevices()
    {
        InputSource.ForJoyButton(JoyButton.A, 0).Overlaps(InputSource.ForJoyButton(JoyButton.A, 1)).Should().BeFalse();
        InputSource.ForJoyButton(JoyButton.A, 0).Overlaps(InputSource.ForJoyButton(JoyButton.A)).Should().BeTrue();
        InputSource.ForTouch(TouchChannel.Pressed, 0).Overlaps(InputSource.ForTouch(TouchChannel.Pressed, 1)).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  ROB-11  Value-changed trigger, ROB-12 pointer spaces
    // ------------------------------------------------------------------

    [Fact]
    public void ChangedTrigger_FiresOnChangesOnly()
    {
        var trigger = new ChangedTrigger();
        var memory = new TriggerMemory();
        var phases = new[] { 0f, 1f, 1f, 0.5f, 0.5f, 0f }
            .Select(v => trigger.Evaluate(new TriggerContext(new Vector3(v, 0, 0), ActionValueKind.Axis1D, 0.1, null, memory)))
            .ToArray();
        phases.Should().Equal(TriggerPhase.Idle, TriggerPhase.Triggered, TriggerPhase.Idle, TriggerPhase.Triggered, TriggerPhase.Idle, TriggerPhase.Triggered);

        var tolerant = new ChangedTrigger { Tolerance = 0.1f };
        var m2 = new TriggerMemory();
        tolerant.Evaluate(new TriggerContext(new Vector3(0.05f, 0, 0), ActionValueKind.Axis1D, 0.1, null, m2)).Should().Be(TriggerPhase.Idle);
    }

    [Fact]
    public void PointerSpace_Conversions()
    {
        using var rig = new Rig();
        rig.Player.ViewportSizeOverride = new Vector2(200, 100);
        rig.Player.CanvasTransformOverride = new Transform2D(0f, new Vector2(-100, 0));
        var position = new Vector3(50, 50, 0);
        Vector3 Run(PointerSpaceModifier m) => m.Run(position, new ModifierContext(ActionValueKind.Axis2D, Rig.Dt, rig.Player, new ModifierMemory()));

        Run(new PointerSpaceModifier { Space = PointerSpace.ViewportNormalized }).Should().Be(new Vector3(0.25f, 0.5f, 0));
        Run(new PointerSpaceModifier { Space = PointerSpace.ViewportCentered }).Should().Be(new Vector3(-0.5f, 0f, 0));
        Run(new PointerSpaceModifier { Space = PointerSpace.RectNormalized, Rect = new Rect2(0, 0, 100, 100) }).Should().Be(new Vector3(0.5f, 0.5f, 0));
        Run(new PointerSpaceModifier { Space = PointerSpace.World2D }).Should().Be(new Vector3(150, 50, 0));
    }
}
