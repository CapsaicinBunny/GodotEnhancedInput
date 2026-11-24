using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Pipeline;

/// <summary>REC-1 … REC-4: recording and deterministic replay.</summary>
[Collection("Godot")]
public class ReplayTests
{
    private sealed class Game
    {
        public readonly GameAction Jump = Make.Action("jump");
        public readonly GameAction Move = Make.Action("move", ActionValueKind.Axis2D);
        public readonly GameAction Charge = Make.Action("charge");
        public readonly GameAction Stick = Make.Action("stick", ActionValueKind.Axis2D);
        public readonly GameAction Ai = Make.Action("ai", ActionValueKind.Axis1D);

        public BindingContext Context()
            => Make.Context("Game",
                Make.Bind(Jump, Key.Space, new PressedTrigger()),
                Make.Bind(Move, InputSourcePath.Parse("keys:w/s/a/d")),
                Make.Bind(Charge, Key.E, new HoldTrigger { HoldSeconds = 0.3f }),
                Make.Bind(Stick, InputSource.ForVirtual("touch_move")),
                Make.Bind(Ai, InputSource.ForVirtual("unused")));

        public GameAction[] All => new[] { Jump, Move, Charge, Stick, Ai };
    }

    private static string Snapshot(InputPlayer player, Game game)
    {
        var parts = new List<string>();
        foreach (var action in game.All)
        {
            var state = player.GetState(action);
            parts.Add($"{action.Name}:{state.Value.AsVector3()}:{state.Phase}:{state.Events}:{state.TriggeredSeconds:F4}");
        }
        return string.Join("|", parts);
    }

    private static List<string> Play(Rig rig, Game game, InputRecording recording)
    {
        var snapshots = new List<string>();
        rig.Player.StartReplay(recording);
        while (rig.Player.IsReplaying)
        {
            rig.Player.FeedEvent(Rig.KeyEvent(Key.W, true));     // live input is ignored during a replay
            rig.Player.SetVirtualControl("touch_move", Vector2.One);
            rig.Player.Tick(123.0);                               // the recorded time step wins
            snapshots.Add(Snapshot(rig.Player, game));
        }
        return snapshots;
    }

    [Fact]
    public void RecordAndReplay_ReproducesEveryTick()
    {
        var game = new Game();
        var recorded = new List<string>();
        InputRecording recording;
        using (var rig = new Rig().Add(game.Context()))
        {
            rig.Player.StartRecording("test");
            void Step(double dt) { rig.Player.Tick(dt); recorded.Add(Snapshot(rig.Player, game)); }

            rig.Press(Key.Space); Step(1 / 60.0);
            rig.Release(Key.Space); Step(1 / 30.0);
            rig.Press(Key.W).Press(Key.D); Step(1 / 144.0);
            rig.Press(Key.E);
            for (int i = 0; i < 25; i++) Step(i % 2 == 0 ? 1 / 60.0 : 1 / 45.0);
            rig.Release(Key.E).Release(Key.W);
            rig.Player.SetVirtualControl("touch_move", new Vector2(0.5f, -0.25f));
            rig.Player.InjectInput(game.Ai, 0.75f);
            Step(1 / 60.0);
            rig.Player.SetVirtualControl("touch_move", Vector2.Zero);
            rig.Release(Key.D);
            Step(1 / 60.0);
            Step(1 / 60.0);
            recording = rig.Player.StopRecording();
        }
        recording.Ticks.Should().HaveCount(recorded.Count);

        // Survives saving.
        var loaded = InputRecording.FromJson(recording.ToJson());
        loaded.Should().NotBeNull();
        loaded.Notes.Should().Be("test");
        loaded.Duration.Should().BeApproximately(recording.Duration, 1e-9);

        using var replayRig = new Rig().Add(game.Context());
        bool finished = false;
        replayRig.Player.ReplayFinished += () => finished = true;
        Play(replayRig, game, loaded).Should().Equal(recorded);
        finished.Should().BeTrue();

        // Live input works again afterwards.
        replayRig.Press(Key.Space).Tick();
        replayRig.Player.IsTriggered(game.Jump).Should().BeTrue();
    }

    [Fact]
    public void ReplayInto_RunsAllTicks()
    {
        var game = new Game();
        InputRecording recording;
        using (var rig = new Rig().Add(game.Context()))
        {
            rig.Player.StartRecording();
            rig.Press(Key.E).TickFor(0.5);
            recording = rig.Player.StopRecording();
        }
        using var replay = new Rig().Add(game.Context());
        recording.ReplayInto(replay.Player);
        replay.Player.IsReplaying.Should().BeFalse();
        replay.Player.IsTriggered(game.Charge).Should().BeTrue();
    }

    [Fact]
    public void Recording_KeepsAllEventKinds()
    {
        var recording = new InputRecording();
        var tick = new RecordedTick { Delta = 0.016 };
        InputEvent[] events =
        {
            new InputEventKey { PhysicalKeycode = Key.A, Keycode = Key.Q, Pressed = true, ShiftPressed = true },
            new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(3, 4) },
            new InputEventMouseMotion { Relative = new Vector2(1, -2), Pressure = 0.5f },
            new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 2 },
            new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = -0.5f, Device = 1 },
            new InputEventScreenTouch { Index = 1, Position = new Vector2(9, 9), Pressed = true },
            new InputEventScreenDrag { Index = 1, Relative = new Vector2(2, 2), Velocity = new Vector2(10, 0) },
            new InputEventMagnifyGesture { Factor = 1.5f },
            new InputEventPanGesture { Delta = new Vector2(0, 3) },
            new InputEventAction { Action = "ui_accept", Pressed = true, Strength = 0.8f },
        };
        foreach (var e in events) tick.Inputs.Add(new RecordedInput { Kind = RecordedInputKind.Event, Event = e, Stage = IntakeStage.Unhandled });
        tick.Inputs.Add(new RecordedInput { Kind = RecordedInputKind.InjectStart, Name = "x", Value = Vector4.One });
        recording.Ticks.Add(tick);

        var back = InputRecording.FromJson(recording.ToJson());
        back.Ticks[0].Inputs.Should().HaveCount(events.Length + 1);
        for (int i = 0; i < events.Length; i++)
        {
            var copy = back.Ticks[0].Inputs[i];
            copy.Stage.Should().Be(IntakeStage.Unhandled);
            copy.Event.GetType().Should().Be(events[i].GetType());
            copy.Event.Device.Should().Be(events[i].Device);
            copy.Event.AsText().Should().Be(events[i].AsText());
        }
        ((InputEventKey)back.Ticks[0].Inputs[0].Event).ShiftPressed.Should().BeTrue();
        ((InputEventJoypadMotion)back.Ticks[0].Inputs[4].Event).AxisValue.Should().Be(-0.5f);
        back.Ticks[0].Inputs[^1].Kind.Should().Be(RecordedInputKind.InjectStart);
        InputRecording.FromJson("not json").Should().BeNull();
    }

    [Fact]
    public void StopReplay_ReturnsToLiveInput()
    {
        var game = new Game();
        var recording = new InputRecording();
        for (int i = 0; i < 10; i++) recording.Ticks.Add(new RecordedTick { Delta = Rig.Dt });
        using var rig = new Rig().Add(game.Context());
        rig.Player.StartReplay(recording);
        rig.Tick(3);
        rig.Player.ReplayPosition.Should().Be(3);
        rig.Player.StopReplay();
        rig.Player.IsReplaying.Should().BeFalse();
        rig.Press(Key.Space).Tick();
        rig.Player.IsTriggered(game.Jump).Should().BeTrue();
    }
}
